import { Option } from 'effect'
import type { CallArgument } from '../local_ast/LocalAst.ts'
import type { NodeSource, SourceField } from '../local_ast/SourceLayout.ts'
import { CallText } from './CallText.ts'
import { SourceWriter } from './SourceWriter.ts'

/** @anchor bescript-argument-output */
export class ArgumentWriter {
  private readonly used: Set<number> = new Set()
  private readonly fields: ReadonlyMap<string, SourceField>

  /** @anchor bescript-argument-output */
  public constructor(private readonly source: NodeSource) {
    this.fields = new Map(
      source.fields.map((field: SourceField): [string, SourceField] => [field.key, field]),
    )
  }

  /** @anchor bescript-argument-output */
  public write(argumentsList: readonly CallArgument[]): string {
    return argumentsList.map((argument: CallArgument): string => this.argument(argument)).join(':')
  }

  /**
   * @anchor bescript-argument-output
   * @fallback 参数 sourceRef=None 是显式新增参数，使用固定参数格式，不继承不存在的原文。
   */
  private argument(argument: CallArgument): string {
    if (Option.isNone(argument.sourceRef)) return argument.typeName + ' ' + CallText.value(argument)
    const index: number = argument.sourceRef.value
    if (this.used.has(index)) throw new Error('同一参数来源不能重复使用；新增参数必须使用 None')
    this.used.add(index)
    const type: SourceField = Option.getOrThrow(
      Option.fromNullishOr(this.fields.get('type' + String(index))),
    )
    const value: SourceField = Option.getOrThrow(
      Option.fromNullishOr(this.fields.get('value' + String(index))),
    )
    const start: number =
      index === 0
        ? Option.getOrThrow(Option.fromNullishOr(this.fields.get('arguments'))).start
        : Option.getOrThrow(Option.fromNullishOr(this.fields.get('value' + String(index - 1))))
            .end + 1
    const fields: SourceField[] = [type, value].map((field: SourceField): SourceField => ({
      ...field,
      start: field.start - start,
      end: field.end - start,
    }))
    return SourceWriter.rewrite(
      this.source.command.slice(start, value.end),
      fields,
      new Map([
        [type.key, argument.typeName],
        [value.key, CallText.value(argument)],
      ]),
    )
  }
}
