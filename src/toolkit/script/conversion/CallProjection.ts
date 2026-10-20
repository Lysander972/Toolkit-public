import { Option } from 'effect'
import { z } from 'zod'
import type { CstNode, IToken } from 'chevrotain'
import type { CallArgument, CallKind, CallSignature } from '../local_ast/LocalAst.ts'
import type { SourceField } from '../local_ast/SourceLayout.ts'
import { BescriptVocabulary, type ScriptParameter } from '../vocabulary/Vocabulary.ts'
import { SyntaxTree } from './SyntaxTree.ts'
import { CallText } from './CallText.ts'

/** @anchor bescript-call-projection */
export interface ProjectedCall {
  readonly call: CallSignature
  readonly fields: readonly SourceField[]
  readonly start: number
  readonly end: number
}

/** @anchor bescript-call-projection */
export class CallProjection {
  private constructor() {}

  /**
   * @anchor bescript-call-projection
   * @format-exempt bescript-scalar-text
   * @fallback 无返回声明时，起点按文法属于丢弃返回的等号或调用种类 token。
   * @fallback 等号缺席时起点为调用种类，不添加返回前缀。
   */
  public static project(tree: CstNode, text: string): ProjectedCall {
    const kindToken: IToken = SyntaxTree.token(tree, 'CallKind')
    const kind: CallKind = z.enum(['fun', 'toolf', 'varf', 'act', 'dvf']).parse(kindToken.image)
    const signature: readonly IToken[] = SyntaxTree.tokens(
      SyntaxTree.node(tree, 'signature'),
      'Word',
    )
    const names: readonly string[] = signature.map((token: IToken): string => token.image)
    const owned: boolean = kind === 'varf' || kind === 'act' || kind === 'dvf'
    if (owned && names.length < 3) throw new Error('属主调用签名必须包含属主、类型和方法')
    const owner: string = owned
      ? Option.getOrThrow(Option.fromNullishOr(names[kind === 'dvf' ? 0 : 1]))
      : ''
    const alias: boolean =
      kind === 'act' &&
      (owner.startsWith('/') ||
        (owner !== 'this' &&
          !z
            .string()
            .regex(/^[0-9]+$/u)
            .safeParse(owner).success))
    const argumentsList: readonly CstNode[] = SyntaxTree.nodes(tree, 'argument')
    const call: CallSignature = {
      kind,
      functionName: owned ? names.slice(2).join('.') : names.join('.'),
      saveName: names.join('.'),
      ownerType: owned
        ? Option.some(Option.getOrThrow(Option.fromNullishOr(names[kind === 'dvf' ? 1 : 0])))
        : Option.none(),
      ownerName: owned
        ? Option.some(kind === 'act' && owner.startsWith('/') ? owner.slice(1) : owner)
        : Option.none(),
      ownerNameIsAlias: alias,
      arguments: argumentsList.map((argument: CstNode, index: number): CallArgument =>
        CallProjection.argument(argument, text, index),
      ),
      returnVariable: CallProjection.returned(tree),
      discardsReturn:
        SyntaxTree.tokens(tree, 'Assign').length !== 0 &&
        SyntaxTree.tokens(tree, 'returnType').length === 0,
    }
    const first: Option.Option<IToken> = Option.fromNullishOr(
      SyntaxTree.tokens(tree, 'returnType')[0],
    )
    const assign: Option.Option<IToken> = Option.fromNullishOr(SyntaxTree.tokens(tree, 'Assign')[0])
    const start: number = Option.isSome(first)
      ? first.value.startOffset
      : Option.isSome(assign)
        ? assign.value.startOffset
        : kindToken.startOffset
    const fields: SourceField[] = [
      { key: 'return', start, end: kindToken.startOffset, value: CallText.returned(call) },
      { key: 'kind', start: kindToken.startOffset, end: SyntaxTree.end(kindToken), value: kind },
    ]
    fields.push({
      key: 'signature',
      start: Option.getOrThrow(Option.fromNullishOr(signature[0])).startOffset,
      end: SyntaxTree.end(Option.getOrThrow(Option.fromNullishOr(signature.at(-1)))),
      value: CallText.signature(call).join('.'),
    })
    if (Option.isSome(first)) {
      const returned: ScriptParameter = Option.getOrThrow(call.returnVariable)
      const name: IToken = SyntaxTree.token(tree, 'returnName')
      fields.push({
        key: 'returnType',
        start: first.value.startOffset,
        end: SyntaxTree.end(first.value),
        value: returned.typeName,
      })
      fields.push({
        key: 'returnName',
        start: name.startOffset,
        end: SyntaxTree.end(name),
        value: returned.name,
      })
    }
    if (owned) {
      const parts: readonly string[] = CallText.signature(call)
      signature.slice(0, 3).forEach((token: IToken, index: number): void => {
        fields.push({
          key: 'signature' + String(index),
          start: token.startOffset,
          end:
            index === 2
              ? SyntaxTree.end(Option.getOrThrow(Option.fromNullishOr(signature.at(-1))))
              : SyntaxTree.end(token),
          value: Option.getOrThrow(Option.fromNullishOr(parts[index])),
        })
      })
    } else {
      fields.push({
        key: 'signature0',
        start: Option.getOrThrow(Option.fromNullishOr(signature[0])).startOffset,
        end: SyntaxTree.end(Option.getOrThrow(Option.fromNullishOr(signature.at(-1)))),
        value: call.functionName,
      })
    }
    argumentsList.forEach((argument: CstNode, index: number): void => {
      const head: IToken = SyntaxTree.token(argument, 'ArgumentHead')
      const parsed: CallArgument = Option.getOrThrow(Option.fromNullishOr(call.arguments[index]))
      fields.push({
        key: 'type' + String(index),
        start: head.startOffset,
        end: SyntaxTree.end(head) - 1,
        value: parsed.typeName,
      })
      fields.push({
        key: 'value' + String(index),
        start: SyntaxTree.end(head),
        end: CallProjection.argumentEnd(argument),
        value: CallText.value(parsed),
      })
    })
    fields.push({
      key: 'arguments',
      start: SyntaxTree.end(SyntaxTree.token(tree, 'Open')),
      end: SyntaxTree.token(tree, 'Close').startOffset,
      value: CallText.arguments(call),
    })
    return { call, fields, start, end: SyntaxTree.end(SyntaxTree.token(tree, 'Close')) }
  }

  /** @anchor bescript-call-projection */
  private static returned(tree: CstNode): Option.Option<ScriptParameter> {
    const type: Option.Option<IToken> = Option.fromNullishOr(
      SyntaxTree.tokens(tree, 'returnType')[0],
    )
    if (Option.isNone(type)) return Option.none()
    return Option.some({
      typeName: BescriptVocabulary.varType.parse(type.value.image),
      name: SyntaxTree.token(tree, 'returnName').image,
    })
  }

  /**
   * @anchor bescript-call-projection
   * @fallback 空常量参数合法，其值区间结束于参数头后，不补造参数值。
   */
  private static argumentEnd(tree: CstNode): number {
    const canvas: Option.Option<IToken> = Option.fromNullishOr(
      SyntaxTree.tokens(tree, 'CanvasEnd')[0],
    )
    if (Option.isSome(canvas)) return SyntaxTree.end(canvas.value)
    const raw: Option.Option<IToken> = Option.fromNullishOr(SyntaxTree.tokens(tree, 'RawValue')[0])
    return Option.isSome(raw)
      ? SyntaxTree.end(raw.value)
      : SyntaxTree.end(SyntaxTree.token(tree, 'ArgumentHead'))
  }

  /** @anchor bescript-call-projection */
  private static argument(tree: CstNode, text: string, index: number): CallArgument {
    const head: IToken = SyntaxTree.token(tree, 'ArgumentHead')
    const typeName: string = head.image.slice(0, -1)
    const rawText: string = text.slice(SyntaxTree.end(head), CallProjection.argumentEnd(tree))
    const parsed: z.ZodSafeParseResult<ReturnType<typeof BescriptVocabulary.varType.parse>> =
      BescriptVocabulary.varType.safeParse(typeName)
    const variable: boolean = typeName === 'var'
    if (variable && (rawText.length === 0 || BescriptVocabulary.rejectsEscaped(rawText)))
      throw new Error('变量参数名无效')
    return {
      sourceRef: Option.some(index),
      variable,
      typeName,
      rawText,
      decodedText: BescriptVocabulary.decode(rawText),
      varType: !variable && parsed.success ? Option.some(parsed.data) : Option.none(),
    }
  }
}
