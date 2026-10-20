import { isDeepStrictEqual } from 'node:util'
import { posix } from 'node:path'
import Hjson from 'hjson'
import { Option } from 'effect'
import { z } from 'zod'
import { DeviceMetadataSchema } from '@toolkit/script/device_metadata'
import type { DeviceMetadata } from '@toolkit/script/device_metadata'
import { ScriptListSchema } from '@toolkit/script/script_list'
import type { ScriptEntry } from '@toolkit/script/script_list'
import { CodeFileReader } from '@toolkit/script/code_file'
import { HjsonDocument } from '@toolkit/project-model/hjson-document'
import { ProjectSettingsParser } from '@toolkit/project-model'
import { GlobalVariableSchema } from '@toolkit/script/global_variables'
import type { GlobalVariable } from '@toolkit/script/global_variables'
import type {
  LogicalDeviceFields,
  LogicalScriptEntry,
  LogicalVariable,
} from '@toolkit/project-model/logical-entities'

/**
 * @anchor logical-entity-formats
 * @format-exempt hjson-object-boundary
 */
interface HjsonFields {
  [key: string]: unknown
}

/** @anchor logical-entity-formats */
export class EntityFormats {
  private static readonly object: z.ZodType<HjsonFields> = z.custom<HjsonFields>(
    (input: unknown): boolean => z.record(z.string(), z.unknown()).safeParse(input).success,
  )

  private constructor() {}

  /** @anchor logical-entity-formats */
  public static device(source: string, fields: LogicalDeviceFields): string {
    const current: DeviceMetadata = DeviceMetadataSchema.read(source)
    const metadata: DeviceMetadata = DeviceMetadataSchema.schema.parse({ ...current, ...fields })
    if (isDeepStrictEqual(current, metadata)) return source
    const document: HjsonDocument = HjsonDocument.read(source)
    const record: HjsonFields = EntityFormats.object.parse(document.fields)
    if (
      Option.isSome(fields.logicName) ||
      Option.isSome(fields.mechFileGuid) ||
      Object.hasOwn(record, 'ext')
    ) {
      const ext: HjsonFields = Object.hasOwn(record, 'ext')
        ? EntityFormats.object.parse(record['ext'])
        : {}
      if (Option.isSome(fields.logicName)) ext['logicName'] = fields.logicName.value
      else delete ext['logicName']
      if (Option.isSome(fields.mechFileGuid)) ext['mechFileGUID'] = fields.mechFileGuid.value
      else delete ext['mechFileGUID']
      record['ext'] = ext
    }
    if (Option.isSome(fields.stickToBone)) record['stickToBone'] = fields.stickToBone.value
    else delete record['stickToBone']
    return document.text()
  }

  /** @anchor logical-entity-formats */
  public static settings(source: string, name: string, mainMap: string): string {
    const current: ReturnType<typeof ProjectSettingsParser.parse> =
      ProjectSettingsParser.parse(source)
    ProjectSettingsParser.parse(Hjson.stringify({ ...current, name, mainMap }))
    const document: HjsonDocument = HjsonDocument.read(source)
    const record: HjsonFields = EntityFormats.object.parse(document.fields)
    if (record['name'] === name && record['mainMap'] === mainMap) return source
    record['name'] = name
    record['mainMap'] = mainMap
    return document.text()
  }

  /** @anchor logical-entity-formats */
  public static list(
    entries: readonly LogicalScriptEntry[],
    groupRelativePath: string,
    source: string,
  ): string {
    const names: Set<string> = new Set()
    const raw: unknown[] = entries.map((entry: LogicalScriptEntry): unknown => {
      const parsed: ScriptEntry = ScriptListSchema.parseEntry({
        origin: entry.origin,
        name: entry.name,
        guid: entry.guid,
        isPrivate: entry.private,
      })
      if (names.has(parsed.name)) throw new Error(`脚本清单名称重复: ${parsed.name}`)
      names.add(parsed.name)
      if (parsed.origin === 'inline') {
        if (
          Option.isNone(entry.relativePath) ||
          entry.relativePath.value !== posix.join(groupRelativePath, entry.name + '.code')
        )
          throw new Error('内联脚本路径必须来自所属组和名称')
        return parsed.name
      }
      if (Option.isSome(entry.relativePath)) throw new Error('仓库引用不拥有内联文件路径')
      return { name: parsed.name, guid: Option.getOrThrow(parsed.guid), private: parsed.isPrivate }
    })
    const previous: readonly ScriptEntry[] = ScriptListSchema.read({
      directoryName: posix.basename(groupRelativePath),
      listText: source,
    }).entries
    const current: readonly ScriptEntry[] = ScriptListSchema.read({
      directoryName: posix.basename(groupRelativePath),
      listText: Hjson.stringify(raw),
    }).entries
    if (isDeepStrictEqual(previous, current)) return source
    return HjsonDocument.sequence(source, raw)
  }

  /**
   * @anchor logical-entity-formats
   * @fallback 已存在 vars.json 时按原数组更新并保留官方数组注释；不存在时使用新数组文档。
   * @fallback vars.json 缺失时创建官方 Hjson 数组文档，不伪造已有文件内容。
   */
  public static variables(
    variables: readonly LogicalVariable[],
    source: Option.Option<string>,
  ): string {
    const entries: readonly GlobalVariable[] = GlobalVariableSchema.parse(variables)
    if (
      Option.isSome(source) &&
      isDeepStrictEqual(GlobalVariableSchema.read(source.value), entries)
    )
      return source.value
    const raw: readonly unknown[] = entries.map((entry: GlobalVariable): unknown =>
      Option.isSome(entry.comment)
        ? { name: entry.name, type: entry.typeName, comment: entry.comment.value }
        : { name: entry.name, type: entry.typeName },
    )
    return Option.isSome(source)
      ? HjsonDocument.sequence(source.value, raw)
      : Hjson.stringify(raw, { space: 1, eol: '\n' })
  }

  /**
   * @anchor logical-entity-formats
   * @passthrough-exempt 代码文件校验器以读取操作承担源码格式合同，合法源码按原文返回。
   */
  public static script(source: string): string {
    CodeFileReader.read(source)
    return source
  }
}
