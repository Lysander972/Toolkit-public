import { Option } from 'effect'
import type { SourceField } from '../local_ast/SourceLayout.ts'

/** @anchor bescript-source-output */
export class SourceWriter {
  private constructor() {}

  /** @anchor bescript-source-output */
  public static rewrite(
    text: string,
    fields: readonly SourceField[],
    values: ReadonlyMap<string, string>,
  ): string {
    let cursor: number = 0
    const chunks: string[] = []
    for (const field of fields) {
      if (field.start < cursor || field.start > field.end || field.end > text.length)
        throw new Error('来源字段区间重叠或越界')
      const value: string = Option.getOrThrow(Option.fromNullishOr(values.get(field.key)))
      chunks.push(
        text.slice(cursor, field.start),
        value === field.value ? text.slice(field.start, field.end) : value,
      )
      cursor = field.end
    }
    chunks.push(text.slice(cursor))
    return chunks.join('')
  }
}
