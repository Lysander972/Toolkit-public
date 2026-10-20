import { Option } from 'effect'
import type { CstNode } from 'chevrotain'
import {
  CodeFileReader,
  type CodeFileDocument,
  type HeaderSource,
  type ParameterSource,
} from '../code_file/CodeFile.ts'
import { LocalAstSchema, type LocalAst } from '../local_ast/LocalAst.ts'
import type { SourceField, ScriptSource } from '../local_ast/SourceLayout.ts'
import { BodyParser } from './BodyParser.ts'
import { BodyProjection, type ProjectedBody } from './BodyProjection.ts'
import { BodyWriter } from './BodyWriter.ts'
import { HeaderText } from './HeaderText.ts'
import { SourceWriter } from './SourceWriter.ts'

/**
 * @anchor bescript-converter
 * @anchor bescript-first-language-boundary
 */
export class BescriptConverter {
  private constructor() {}

  /** @anchor bescript-converter */
  public static parse(source: string): LocalAst {
    const document: CodeFileDocument = CodeFileReader.read(source)
    const tree: CstNode = BodyParser.parse(document.sourceText)
    const projector: BodyProjection = new BodyProjection(document.sourceText)
    const body: ProjectedBody = projector.body(tree, 0, 0, document.sourceText.length)
    const headerValues: ReadonlyMap<string, string> = HeaderText.fields(document.header)
    return LocalAstSchema.parse({
      version: document.version,
      header: document.header,
      sourceText: document.sourceText,
      nodes: body.nodes,
      source: Option.some({
        file: document.layout,
        headers: document.layout.fields.map((field: HeaderSource): SourceField => ({
          ...field,
          value: HeaderText.value(headerValues, field.key),
        })),
        nodes: projector.layouts(),
        tail: body.tail,
      }),
    })
  }

  /**
   * @anchor bescript-converter
   * @fallback source=None 是用户明确要求的显式新建 AST 模式，按固定格式生成而非丢失来源的降级。
   */
  public static serialize(input: LocalAst): string {
    const ast: LocalAst = LocalAstSchema.parse(input)
    const values: ReadonlyMap<string, string> = HeaderText.fields(ast.header)
    const begin: string = ast.version === 'v1' ? 'begincode' : 'begincodeV2'
    if (Option.isNone(ast.source)) {
      return (
        begin +
        '\n' +
        [...values.values()].join('\n') +
        '\nlines:\n' +
        new BodyWriter([]).write(ast.nodes) +
        'endcode\n'
      )
    }
    const source: ScriptSource = ast.source.value
    const existing: Set<string> = new Set(
      source.headers.map((field: SourceField): string => field.key),
    )
    const fields: SourceField[] = [
      {
        key: 'version',
        start: source.file.beginStart,
        end: source.file.beginEnd,
        value: source.file.text.slice(source.file.beginStart, source.file.beginEnd),
      },
      ...source.headers,
    ]
    if (source.file.parameters.length === ast.header.parameters.length) {
      const parameterHeader: number = fields.findIndex(
        (field: SourceField): boolean => field.key === 'params',
      )
      if (parameterHeader !== -1) fields.splice(parameterHeader, 1)
      source.file.parameters.forEach((parameter: ParameterSource, index: number): void => {
        fields.push({
          key: 'parameterType' + String(index),
          start: parameter.typeStart,
          end: parameter.typeEnd,
          value: source.file.text.slice(parameter.typeStart, parameter.typeEnd),
        })
        fields.push({
          key: 'parameterName' + String(index),
          start: parameter.nameStart,
          end: parameter.nameEnd,
          value: source.file.text.slice(parameter.nameStart, parameter.nameEnd),
        })
      })
      fields.sort((left: SourceField, right: SourceField): number => left.start - right.start)
    }
    const headerValues: Map<string, string> = new Map([['version', begin]])
    for (const field of source.headers)
      headerValues.set(field.key, HeaderText.value(values, field.key))
    HeaderText.parameterFields(ast.header).forEach((value: string, key: string): void => {
      headerValues.set(key, value)
    })
    const header: string = SourceWriter.rewrite(
      source.file.text.slice(0, source.file.linesStart),
      fields,
      headerValues,
    )
    const added: string = [...values.entries()]
      .filter(([key]: [string, string]): boolean => !existing.has(key))
      .map((entry: [string, string]): string => entry[1] + '\n')
      .join('')
    return (
      header +
      added +
      source.file.text.slice(source.file.linesStart, source.file.bodyStart) +
      new BodyWriter(source.nodes).write(ast.nodes) +
      source.tail +
      source.file.text.slice(source.file.bodyEnd)
    )
  }
}
