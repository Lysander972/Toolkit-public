import { Option } from 'effect'
import Hjson from 'hjson'
import { z } from 'zod'
import { GuidFormat } from '@toolkit/project-model/guid'
import { BescriptVocabulary } from '../vocabulary/Vocabulary.ts'

/** @anchor bescript-script-list */
export type ScriptOrigin = 'inline' | 'repository'

/** @anchor bescript-script-list */
export type ScriptGroupKind = 'custom' | 'event'

/** @anchor bescript-script-list */
export interface ScriptEntry {
  readonly origin: ScriptOrigin
  readonly name: string
  readonly guid: Option.Option<string>
  readonly isPrivate: boolean
}

/** @anchor bescript-script-list */
export interface ScriptGroup<T> {
  readonly kind: ScriptGroupKind
  readonly eventName: Option.Option<string>
  readonly entries: readonly T[]
}

/** @anchor bescript-script-list */
export interface ScriptGroupSource {
  readonly directoryName: string
  readonly listText: string
}

/**
 * @anchor bescript-script-list
 * @format-exempt hjson-object-boundary
 */
interface HjsonFields {
  readonly [key: string]: unknown
}

/** @anchor bescript-script-list */
export class ScriptListSchema {
  private static readonly entry: z.ZodType<ScriptEntry> = z.strictObject({
    origin: z.enum(['inline', 'repository']),
    name: z
      .string()
      .min(1)
      .refine((name: string): boolean => !BescriptVocabulary.rejectsEscaped(name)),
    guid: BescriptVocabulary.option(z.string().refine(GuidFormat.validate)),
    isPrivate: z.boolean(),
  })

  private static readonly keys: ReadonlySet<string> = new Set<string>(['name', 'guid', 'private'])

  private constructor() {}

  /** @anchor bescript-script-list */
  public static parseEntry(input: unknown): ScriptEntry {
    const entry: ScriptEntry = ScriptListSchema.entry.parse(input)
    if ((entry.origin === 'repository') !== Option.isSome(entry.guid)) {
      throw new Error('代码仓库引用必须携带 GUID，内联脚本不能携带 GUID')
    }
    if (entry.origin === 'inline' && entry.isPrivate) {
      throw new Error('内联清单条目不携带私有标记，私有标记属于脚本头')
    }
    return entry
  }

  /** @anchor bescript-script-list */
  public static read(source: ScriptGroupSource): ScriptGroup<ScriptEntry> {
    const directoryName: string = source.directoryName
    const custom: boolean = directoryName === 'scripts'
    if (!custom && (!directoryName.startsWith('event_') || directoryName.length === 6)) {
      throw new Error('脚本组目录必须是 scripts 或 event_ 加事件名')
    }
    const items: readonly unknown[] = z.array(z.unknown()).parse(Hjson.parse(source.listText))
    return {
      kind: custom ? 'custom' : 'event',
      eventName: custom ? Option.none() : Option.some(directoryName.slice(6)),
      entries: items.map((item: unknown): ScriptEntry => ScriptListSchema.readEntry(item)),
    }
  }

  /** @anchor bescript-script-list */
  private static readEntry(input: unknown): ScriptEntry {
    if (typeof input === 'string') {
      return ScriptListSchema.parseEntry({
        origin: 'inline',
        name: input,
        guid: Option.none(),
        isPrivate: false,
      })
    }
    const record: HjsonFields = z.record(z.string(), z.unknown()).parse(input)
    for (const key of Object.keys(record)) {
      if (!ScriptListSchema.keys.has(key)) {
        throw new Error('脚本清单条目出现未定义字段: ' + key)
      }
    }
    return ScriptListSchema.parseEntry({
      origin: 'repository',
      name: record['name'],
      guid: Option.some(record['guid']),
      isPrivate: Object.hasOwn(record, 'private') ? z.boolean().parse(record['private']) : false,
    })
  }
}
