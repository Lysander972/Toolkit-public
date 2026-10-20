import { Option } from 'effect'
import { BescriptVocabulary } from '../vocabulary/Vocabulary.ts'
import type { CallSignature, LocalAstNode, CallArgument } from '../local_ast/LocalAst.ts'

/** @anchor bescript-call-output */
export class CallText {
  private constructor() {}

  /** @anchor bescript-call-output */
  public static signature(call: CallSignature): readonly string[] {
    if (call.kind === 'fun' || call.kind === 'toolf') return [call.functionName]
    const owner: string = Option.getOrThrow(call.ownerName)
    const type: string = Option.getOrThrow(call.ownerType)
    if (call.kind === 'dvf') return [owner, type, call.functionName]
    return [type, (call.ownerNameIsAlias ? '/' : '') + owner, call.functionName]
  }

  /**
   * @anchor bescript-call-output
   * @fallback 无返回绑定时仅依据显式 discardsReturn 决定等号，未丢弃时不生成返回前缀。
   */
  public static returned(call: CallSignature): string {
    if (Option.isSome(call.returnVariable)) {
      return call.returnVariable.value.typeName + ' ' + call.returnVariable.value.name + '='
    }
    return call.discardsReturn ? '=' : ''
  }

  /** @anchor bescript-call-output */
  public static value(argument: CallArgument): string {
    if (argument.typeName === 'Canvas' && argument.decodedText.startsWith('==hjson:')) {
      if (!argument.decodedText.endsWith('==')) throw new Error('内联 Canvas 缺少结束标记')
      return (
        '==' +
        argument.decodedText
          .slice(2, -2)
          .split('\n')
          .map((line: string): string => BescriptVocabulary.encode(line))
          .join('\n') +
        '=='
      )
    }
    return BescriptVocabulary.encode(argument.decodedText)
  }

  /** @anchor bescript-call-output */
  public static arguments(call: CallSignature): string {
    return call.arguments
      .map((argument: CallArgument): string => argument.typeName + ' ' + CallText.value(argument))
      .join(':')
  }

  /** @anchor bescript-call-output */
  public static call(call: CallSignature): string {
    return (
      CallText.returned(call) +
      call.kind +
      ' ' +
      CallText.signature(call).join('.') +
      '(' +
      CallText.arguments(call) +
      ')'
    )
  }

  /** @anchor bescript-call-output */
  public static wrapper(node: LocalAstNode): string {
    if (node.kind === 'if' || node.kind === 'while') return node.kind + '{'
    if (node.kind === 'async' || node.kind === 'delay') return node.kind + ' '
    return ''
  }

  /** @anchor bescript-call-output */
  public static command(node: LocalAstNode): string {
    if (node.kind === 'comment' || node.kind === 'skip') return node.text
    return CallText.wrapper(node) + CallText.call(Option.getOrThrow(node.call))
  }

  /**
   * @anchor bescript-call-output
   * @fallback 注释语句没有调用，以注释正文为唯一编辑内容，不合成调用。
   */
  public static fields(node: LocalAstNode): ReadonlyMap<string, string> {
    const fields: Map<string, string> = new Map([['wrapper', CallText.wrapper(node)]])
    if (Option.isNone(node.call)) {
      fields.set('comment', node.text)
      return fields
    }
    const call: CallSignature = node.call.value
    fields.set('return', CallText.returned(call))
    fields.set('kind', call.kind)
    fields.set('signature', CallText.signature(call).join('.'))
    if (Option.isSome(call.returnVariable)) {
      fields.set('returnType', call.returnVariable.value.typeName)
      fields.set('returnName', call.returnVariable.value.name)
    }
    fields.set('arguments', CallText.arguments(call))
    CallText.signature(call).forEach((part: string, index: number): void => {
      fields.set('signature' + String(index), part)
    })
    call.arguments.forEach((argument: CallArgument, index: number): void => {
      fields.set('type' + String(index), argument.typeName)
      fields.set('value' + String(index), CallText.value(argument))
    })
    return fields
  }
}
