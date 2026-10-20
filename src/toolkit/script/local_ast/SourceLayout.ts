import type { CodeFileLayout, HeaderSource, ParameterSource } from '../code_file/CodeFile.ts'
import { Option } from 'effect'
import { z } from 'zod'
import { BescriptVocabulary } from '../vocabulary/Vocabulary.ts'

/** @anchor bescript-source-layout */
export interface SourceField {
  readonly key: string
  readonly start: number
  readonly end: number
  readonly value: string
}

/** @anchor bescript-source-layout */
export interface NodeSource {
  readonly id: number
  readonly leading: string
  readonly prefix: string
  readonly numberPrefix: string
  readonly depth: number
  readonly disabled: boolean
  readonly disabledStart: number
  readonly command: string
  readonly suffix: string
  readonly fields: readonly SourceField[]
  readonly callKind: string
  readonly argumentCount: number
  readonly hasReturn: boolean
  readonly close: Option.Option<string>
  readonly closePrefixLength: number
  readonly closeNumberPrefix: string
  readonly closeDisabled: boolean
  readonly tail: string
}

/** @anchor bescript-source-layout */
export interface ScriptSource {
  readonly file: CodeFileLayout
  readonly headers: readonly SourceField[]
  readonly nodes: readonly NodeSource[]
  readonly tail: string
}

/** @anchor bescript-source-layout */
export class SourceLayoutSchema {
  private static readonly header: z.ZodType<HeaderSource> = z.strictObject({
    key: z.string(),
    start: z.number().int().min(0),
    end: z.number().int().min(0),
  })

  private static readonly parameter: z.ZodType<ParameterSource> = z.strictObject({
    typeStart: z.number().int().min(0),
    typeEnd: z.number().int().min(0),
    nameStart: z.number().int().min(0),
    nameEnd: z.number().int().min(0),
  })

  private static readonly file: z.ZodType<CodeFileLayout> = z.strictObject({
    text: z.string(),
    beginStart: z.number().int().min(0),
    beginEnd: z.number().int().min(0),
    linesStart: z.number().int().min(0),
    bodyStart: z.number().int().min(0),
    bodyEnd: z.number().int().min(0),
    fields: z.array(SourceLayoutSchema.header),
    parameters: z.array(SourceLayoutSchema.parameter),
  })

  private static readonly field: z.ZodType<SourceField> = z.strictObject({
    key: z.string(),
    start: z.number().int().min(0),
    end: z.number().int().min(0),
    value: z.string(),
  })

  private static readonly node: z.ZodType<NodeSource> = z.strictObject({
    id: z.number().int().min(0),
    leading: z.string(),
    prefix: z.string(),
    numberPrefix: z.string(),
    depth: z.number().int().min(0),
    disabled: z.boolean(),
    disabledStart: z.number().int().min(0),
    command: z.string(),
    suffix: z.string(),
    fields: z.array(SourceLayoutSchema.field),
    callKind: z.string(),
    argumentCount: z.number().int().min(0),
    hasReturn: z.boolean(),
    close: BescriptVocabulary.option(z.string()),
    closePrefixLength: z.number().int().min(0),
    closeNumberPrefix: z.string(),
    closeDisabled: z.boolean(),
    tail: z.string(),
  })

  public static readonly source: z.ZodType<ScriptSource> = z
    .strictObject({
      file: SourceLayoutSchema.file,
      headers: z.array(SourceLayoutSchema.field),
      nodes: z.array(SourceLayoutSchema.node),
      tail: z.string(),
    })
    .superRefine((source: ScriptSource, context: z.RefinementCtx<ScriptSource>): void => {
      SourceLayoutSchema.check(source, context)
    })

  private constructor() {}

  /** @anchor bescript-source-layout */
  private static check(source: ScriptSource, context: z.RefinementCtx<ScriptSource>): void {
    const file: CodeFileLayout = source.file
    SourceLayoutSchema.checkFile(file, context)
    SourceLayoutSchema.checkHeaders(source, context)
    const ids: Set<number> = new Set()
    for (const node of source.nodes) {
      if (ids.has(node.id)) context.addIssue({ code: 'custom', message: '节点来源 ID 重复' })
      ids.add(node.id)
      SourceLayoutSchema.checkNode(node, context)
    }
  }

  /** @anchor bescript-source-layout */
  private static checkFile(file: CodeFileLayout, context: z.RefinementCtx<ScriptSource>): void {
    if (!(
      file.beginStart <= file.beginEnd &&
      file.beginEnd <= file.linesStart &&
      file.linesStart <= file.bodyStart &&
      file.bodyStart <= file.bodyEnd &&
      file.bodyEnd <= file.text.length
    ))
      context.addIssue({ code: 'custom', message: '文件来源区间无效' })
    for (const parameter of file.parameters) {
      if (!(
        parameter.typeStart < parameter.typeEnd &&
        parameter.typeEnd < parameter.nameStart &&
        parameter.nameStart < parameter.nameEnd &&
        parameter.nameEnd <= file.linesStart
      ))
        context.addIssue({ code: 'custom', message: '头部参数来源区间无效' })
    }
  }

  /** @anchor bescript-source-layout */
  private static checkHeaders(source: ScriptSource, context: z.RefinementCtx<ScriptSource>): void {
    const file: CodeFileLayout = source.file
    if (source.headers.length !== file.fields.length)
      context.addIssue({ code: 'custom', message: '头部字段与读取来源数量不一致' })
    const headerKeys: Set<string> = new Set()
    for (const [index, field] of source.headers.entries()) {
      const original: Option.Option<HeaderSource> = Option.fromNullishOr(file.fields[index])
      if (
        Option.isNone(original) ||
        original.value.key !== field.key ||
        original.value.start !== field.start ||
        original.value.end !== field.end
      )
        context.addIssue({ code: 'custom', message: '头部字段与读取来源关联不一致' })
      if (headerKeys.has(field.key))
        context.addIssue({ code: 'custom', message: '头部来源字段重复' })
      headerKeys.add(field.key)
      if (field.start < file.beginEnd || field.start > field.end || field.end > file.linesStart)
        context.addIssue({ code: 'custom', message: '头部来源区间无效' })
    }
    if (!headerKeys.has('name')) context.addIssue({ code: 'custom', message: '来源缺少 name 头部' })
  }

  /** @anchor bescript-source-layout */
  private static checkNode(node: NodeSource, context: z.RefinementCtx<ScriptSource>): void {
    const expected: Set<string> = SourceLayoutSchema.expectedFields(node)
    for (const field of node.fields) {
      if (!expected.delete(field.key))
        context.addIssue({ code: 'custom', message: '节点来源字段未知或重复: ' + field.key })
      if (field.start > field.end || field.end > node.command.length)
        context.addIssue({ code: 'custom', message: '节点来源字段区间无效' })
    }
    if (expected.size !== 0)
      context.addIssue({ code: 'custom', message: '节点来源字段缺失: ' + [...expected].join(',') })
    if (node.disabledStart > node.prefix.length)
      context.addIssue({ code: 'custom', message: '禁用前缀来源位置无效' })
    if (Option.isSome(node.close) && node.closePrefixLength > node.close.value.length)
      context.addIssue({ code: 'custom', message: '块尾来源前缀长度无效' })
  }

  /** @anchor bescript-source-layout */
  private static expectedFields(node: NodeSource): Set<string> {
    const expected: Set<string> = new Set()
    if (node.callKind === '') {
      expected.add('comment')
    } else {
      for (const key of ['wrapper', 'return', 'kind', 'signature', 'arguments']) expected.add(key)
      const count: number = node.callKind === 'fun' || node.callKind === 'toolf' ? 1 : 3
      for (let index: number = 0; index < count; index += 1)
        expected.add('signature' + String(index))
      if (node.hasReturn) {
        expected.add('returnType')
        expected.add('returnName')
      }
      for (let index: number = 0; index < node.argumentCount; index += 1) {
        expected.add('type' + String(index))
        expected.add('value' + String(index))
      }
    }
    return expected
  }
}
