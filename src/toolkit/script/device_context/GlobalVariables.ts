import { Option } from 'effect'
import Hjson from 'hjson'
import { z } from 'zod'
import { BescriptVocabulary, type GlobalVarTypeName } from '../vocabulary/Vocabulary.ts'

/** @anchor bescript-global-variable */
export interface GlobalVariable {
  readonly name: string
  readonly typeName: GlobalVarTypeName
  readonly comment: Option.Option<string>
}

/**
 * Hjson 对象在按字段收窄前没有具名键值。
 * @anchor bescript-global-variable
 * @format-exempt hjson-object-boundary
 */
interface HjsonFields {
  readonly [key: string]: unknown
}

/** @anchor bescript-global-variable */
export class GlobalVariableSchema {
  private static readonly schema: z.ZodType<GlobalVariable> = z.strictObject({
    name: z
      .string()
      .min(1)
      .refine((name: string): boolean => !BescriptVocabulary.rejectsEscaped(name)),
    typeName: BescriptVocabulary.globalVarType,
    comment: BescriptVocabulary.option(z.string()),
  })

  private static readonly keys: ReadonlySet<string> = new Set<string>(['name', 'type', 'comment'])

  private constructor() {}

  /** @anchor bescript-global-variable */
  public static read(source: string): readonly GlobalVariable[] {
    const items: readonly HjsonFields[] = z
      .array(z.record(z.string(), z.unknown()))
      .parse(Hjson.parse(source))
    return GlobalVariableSchema.parse(
      items.map((record: HjsonFields): unknown => {
        for (const key of Object.keys(record)) {
          if (!GlobalVariableSchema.keys.has(key)) {
            throw new Error('全局变量出现未定义字段: ' + key)
          }
        }
        return {
          name: record['name'],
          typeName: record['type'],
          comment: Object.hasOwn(record, 'comment')
            ? Option.some(record['comment'])
            : Option.none(),
        }
      }),
    )
  }

  /** @anchor bescript-global-variable */
  public static parse(input: unknown): readonly GlobalVariable[] {
    const items: readonly GlobalVariable[] = z.array(GlobalVariableSchema.schema).parse(input)
    const names: Set<string> = new Set<string>()
    for (const variable of items) {
      if (names.has(variable.name)) {
        throw new Error('全局变量名重复: ' + variable.name)
      }
      names.add(variable.name)
    }
    return items
  }
}
