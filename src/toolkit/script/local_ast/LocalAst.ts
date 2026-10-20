import { Option } from 'effect'
import { z } from 'zod'
import { GuidFormat } from '@toolkit/project-model/guid'
import type { CodeFileVersion } from '../code_file/CodeFile.ts'
import { SourceLayoutSchema } from './SourceLayout.ts'
import type { ScriptSource } from './SourceLayout.ts'
import {
  BescriptVocabulary,
  type ScriptHeader,
  type ScriptParameter,
  type VarTypeName,
} from '../vocabulary/Vocabulary.ts'

/** @anchor bescript-local-ast */
export type StatementKind =
  'if' | 'while' | 'delay' | 'async' | 'comment' | 'skip' | 'break' | 'continue' | 'call'

/** @anchor bescript-local-ast */
export type CallKind = 'fun' | 'toolf' | 'varf' | 'act' | 'dvf'

/** @anchor bescript-local-ast */
export interface CallArgument {
  readonly sourceRef: Option.Option<number>
  readonly variable: boolean
  readonly typeName: string
  readonly rawText: string
  readonly decodedText: string
  readonly varType: Option.Option<VarTypeName>
}

/** @anchor bescript-local-ast */
export interface CallSignature {
  readonly kind: CallKind
  readonly functionName: string
  readonly saveName: string
  readonly ownerType: Option.Option<string>
  readonly ownerName: Option.Option<string>
  readonly ownerNameIsAlias: boolean
  readonly arguments: readonly CallArgument[]
  readonly returnVariable: Option.Option<ScriptParameter>
  readonly discardsReturn: boolean
}

/** @anchor bescript-local-ast */
export interface LocalAstNode {
  readonly sourceRef: Option.Option<number>
  readonly kind: StatementKind
  readonly line: number
  readonly depth: number
  readonly disabled: boolean
  readonly text: string
  readonly children: readonly LocalAstNode[]
  readonly call: Option.Option<CallSignature>
}

/** @anchor bescript-local-ast */
export interface VariableBinding {
  readonly name: string
  readonly typeName: VarTypeName
}

/** @anchor bescript-local-ast */
export interface LocalAst {
  readonly source: Option.Option<ScriptSource>
  readonly version: CodeFileVersion
  readonly header: ScriptHeader
  readonly sourceText: string
  readonly nodes: readonly LocalAstNode[]
}

/** @anchor bescript-local-ast */
export class LocalAstSchema {
  private static readonly statementKind: z.ZodType<StatementKind> = z.enum([
    'if',
    'while',
    'delay',
    'async',
    'comment',
    'skip',
    'break',
    'continue',
    'call',
  ])

  private static readonly callKind: z.ZodType<CallKind> = z.enum([
    'fun',
    'toolf',
    'varf',
    'act',
    'dvf',
  ])

  private static readonly argument: z.ZodType<CallArgument> = z.strictObject({
    sourceRef: BescriptVocabulary.option(z.number().int().min(0)),
    variable: z.boolean(),
    typeName: z.string().min(1).refine(BescriptVocabulary.isBareIdentifier),
    rawText: z.string(),
    decodedText: z.string(),
    varType: BescriptVocabulary.option(BescriptVocabulary.varType),
  })

  private static readonly call: z.ZodType<CallSignature> = z.strictObject({
    kind: LocalAstSchema.callKind,
    functionName: z
      .string()
      .min(1)
      .refine((name: string): boolean => !BescriptVocabulary.rejectsEscaped(name)),
    saveName: z.string().min(1),
    ownerType: BescriptVocabulary.option(
      z.string().min(1).refine(BescriptVocabulary.isBareIdentifier),
    ),
    ownerName: BescriptVocabulary.option(
      z
        .string()
        .min(1)
        .refine((name: string): boolean => !BescriptVocabulary.rejectsEscaped(name)),
    ),
    ownerNameIsAlias: z.boolean(),
    arguments: z.array(LocalAstSchema.argument),
    returnVariable: BescriptVocabulary.option(BescriptVocabulary.parameter),
    discardsReturn: z.boolean(),
  })

  private static readonly node: z.ZodType<LocalAstNode> = z.lazy((): z.ZodType<LocalAstNode> =>
    z.strictObject({
      sourceRef: BescriptVocabulary.option(z.number().int().min(0)),
      kind: LocalAstSchema.statementKind,
      line: z.number().int().min(1),
      depth: z.number().int().min(0),
      disabled: z.boolean(),
      text: z.string(),
      children: z.array(LocalAstSchema.node),
      call: BescriptVocabulary.option(LocalAstSchema.call),
    }),
  )

  private static readonly header: z.ZodType<ScriptHeader> = z.strictObject({
    name: z.string().min(1),
    comment: BescriptVocabulary.option(z.string()),
    debug: z.boolean(),
    skip: z.boolean(),
    isPrivate: z.boolean(),
    guid: BescriptVocabulary.option(z.string().refine(GuidFormat.validate)),
    color: BescriptVocabulary.option(BescriptVocabulary.color),
    parameters: z.array(BescriptVocabulary.parameter),
    returnParameter: BescriptVocabulary.option(BescriptVocabulary.parameter),
  })

  private static readonly document: z.ZodType<LocalAst> = z.strictObject({
    source: BescriptVocabulary.option(SourceLayoutSchema.source),
    version: z.enum(['v1', 'v2']),
    header: LocalAstSchema.header,
    sourceText: z.string(),
    nodes: z.array(LocalAstSchema.node),
  })

  private constructor() {}

  /** @anchor bescript-local-ast */
  public static parse(input: unknown): LocalAst {
    const ast: LocalAst = LocalAstSchema.document.parse(input)
    LocalAstSchema.checkHeader(ast.header)
    for (const node of ast.nodes) {
      if (node.depth !== 0) {
        throw new Error('根节点深度必须为 0')
      }
      LocalAstSchema.checkNode(node)
    }
    return ast
  }

  /** @anchor bescript-local-ast */
  public static bindings(ast: LocalAst): readonly VariableBinding[] {
    const bindings: Map<string, VarTypeName> = new Map<string, VarTypeName>()
    for (const parameter of ast.header.parameters) {
      bindings.set(parameter.name, parameter.typeName)
    }
    const pending: LocalAstNode[] = [...ast.nodes].reverse()
    while (pending.length !== 0) {
      const node: LocalAstNode = Option.getOrThrow(Option.fromNullishOr(pending.pop()))
      if (Option.isSome(node.call) && Option.isSome(node.call.value.returnVariable)) {
        const variable: ScriptParameter = node.call.value.returnVariable.value
        if (!bindings.has(variable.name)) {
          bindings.set(variable.name, variable.typeName)
        }
      }
      pending.push(...[...node.children].reverse())
    }
    return Array.from(bindings, ([name, typeName]: [string, VarTypeName]): VariableBinding => ({
      name,
      typeName,
    }))
  }

  /** @anchor bescript-local-ast */
  private static checkHeader(header: ScriptHeader): void {
    if (BescriptVocabulary.rejectsEscaped(header.name)) {
      throw new Error('脚本名包含需要转义的字符')
    }
    LocalAstSchema.checkNames(header)
  }

  /** @anchor bescript-local-ast */
  private static checkNames(header: ScriptHeader): void {
    const names: Set<string> = new Set<string>()
    for (const parameter of header.parameters) {
      if (names.has(parameter.name)) {
        throw new Error('参数名 重复: ' + parameter.name)
      }
      names.add(parameter.name)
    }
  }

  /** @anchor bescript-local-ast */
  private static checkNode(node: LocalAstNode): void {
    const opensBlock: boolean = node.kind === 'if' || node.kind === 'while'
    const carriesCall: boolean = node.kind !== 'comment' && node.kind !== 'skip'
    if (carriesCall && Option.isNone(node.call)) {
      throw new Error(node.kind + ' 语句必须携带调用')
    }
    if (!carriesCall && Option.isSome(node.call)) {
      throw new Error(node.kind + ' 语句不能携带调用')
    }
    if (!opensBlock && node.children.length !== 0) {
      throw new Error(node.kind + ' 语句不能包含子节点')
    }
    if (Option.isSome(node.call)) {
      LocalAstSchema.checkCall(node.call.value)
    }
    for (const child of node.children) {
      if (child.depth !== node.depth + 1) {
        throw new Error('子节点深度必须比块语句深一层')
      }
      LocalAstSchema.checkNode(child)
    }
  }

  /**
   * @anchor bescript-local-ast
   * @format-exempt bescript-scalar-text
   */
  private static checkCall(call: CallSignature): void {
    LocalAstSchema.checkSignature(call)
    const owned: boolean = call.kind === 'varf' || call.kind === 'act' || call.kind === 'dvf'
    if (owned && (Option.isNone(call.ownerType) || Option.isNone(call.ownerName))) {
      throw new Error(call.kind + ' 调用必须携带属主类型和属主名')
    }
    if (
      call.kind === 'varf' &&
      Option.isSome(call.ownerType) &&
      !BescriptVocabulary.varType.safeParse(call.ownerType.value).success
    ) {
      throw new Error(call.kind + ' 的属主类型必须是词表类型')
    }
    if (!owned && (Option.isSome(call.ownerType) || Option.isSome(call.ownerName))) {
      throw new Error(call.kind + ' 调用不能携带属主')
    }
    if (!owned && call.ownerNameIsAlias) {
      throw new Error(call.kind + ' 调用不能把属主名标成别名')
    }
    if (
      call.kind === 'act' &&
      Option.isSome(call.ownerType) &&
      !z
        .string()
        .regex(/^[0-9]+$/u)
        .safeParse(call.ownerType.value).success
    ) {
      throw new Error('act 的属主类型必须是数字')
    }
    if (Option.isSome(call.returnVariable) && call.discardsReturn) {
      throw new Error('调用不能同时保存返回值并丢弃返回值')
    }
    for (const argument of call.arguments) {
      if (argument.variable !== (argument.typeName === 'var'))
        throw new Error('变量参数标记必须与 var 参数头一致')
      if (
        argument.variable &&
        (argument.decodedText.length === 0 ||
          BescriptVocabulary.rejectsEscaped(argument.decodedText))
      )
        throw new Error('变量参数名无效')
      const inlineCanvas: boolean =
        argument.typeName === 'Canvas' && argument.decodedText.startsWith('==hjson:')
      if (
        argument.decodedText.includes('\r') ||
        (!inlineCanvas && argument.decodedText.includes('\n'))
      )
        throw new Error('参数换行只允许内联 Canvas 的 LF')
      if (inlineCanvas && !argument.decodedText.endsWith('=='))
        throw new Error('内联 Canvas 缺少结束标记')
    }
  }

  /** @anchor bescript-local-ast */
  private static checkSignature(call: CallSignature): void {
    const methodParts: readonly string[] = call.functionName.split('.')
    const parts: string[] = [...methodParts]
    if (Option.isSome(call.ownerName)) parts.push(call.ownerName.value)
    if (Option.isSome(call.ownerType)) parts.push(call.ownerType.value)
    for (const part of parts) {
      if (!BescriptVocabulary.isBareIdentifier(part) || part.includes('.'))
        throw new Error('调用签名部分不能包含空白、点或块定界符')
    }
  }
}
