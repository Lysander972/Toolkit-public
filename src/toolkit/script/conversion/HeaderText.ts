import { Option } from 'effect'
import { BescriptVocabulary } from '../vocabulary/Vocabulary.ts'
import type { ScriptHeader, ScriptParameter } from '../vocabulary/Vocabulary.ts'

/** @anchor bescript-header-output */
export class HeaderText {
  private constructor() {}

  /** @anchor bescript-header-output */
  public static fields(header: ScriptHeader): ReadonlyMap<string, string> {
    const fields: Map<string, string> = new Map([['name', 'name:' + header.name]])
    if (Option.isSome(header.comment))
      fields.set('comment', 'comment:' + BescriptVocabulary.encode(header.comment.value))
    if (Option.isSome(header.guid)) fields.set('guid', 'guid:' + header.guid.value)
    if (Option.isSome(header.color)) {
      fields.set(
        'color',
        'color:' +
          [header.color.value.red, header.color.value.green, header.color.value.blue].join(','),
      )
    }
    if (header.parameters.length !== 0)
      fields.set('params', 'params:' + header.parameters.map(HeaderText.parameter).join(','))
    if (Option.isSome(header.returnParameter))
      fields.set(
        'return',
        'return:' + header.returnParameter.value.typeName + ' ' + header.returnParameter.value.name,
      )
    if (header.debug) fields.set('debug', 'debug:1')
    if (header.skip) fields.set('skip', 'skip:1')
    if (header.isPrivate) fields.set('private', 'private:1')
    return fields
  }

  /** @anchor bescript-header-output */
  private static parameter(this: void, parameter: ScriptParameter): string {
    return parameter.typeName + ' ' + parameter.name
  }

  /** @anchor bescript-header-output */
  public static parameterFields(header: ScriptHeader): ReadonlyMap<string, string> {
    const values: Map<string, string> = new Map()
    header.parameters.forEach((parameter: ScriptParameter, index: number): void => {
      values.set('parameterType' + String(index), parameter.typeName)
      values.set('parameterName' + String(index), parameter.name)
    })
    return values
  }

  /** @anchor bescript-header-output */
  public static value(fields: ReadonlyMap<string, string>, key: string): string {
    if (fields.has(key)) return Option.getOrThrow(Option.fromNullishOr(fields.get(key)))
    if (key === 'params') return 'params:'
    return ''
  }
}
