import { Option } from 'effect'
import { z } from 'zod'
import { BescriptVocabulary } from '../vocabulary/Vocabulary.ts'
import { LocalAstSchema, type LocalAst } from '../local_ast/LocalAst.ts'
import { DeviceMetadataSchema, type DeviceMetadata } from './DeviceMetadata.ts'
import { GlobalVariableSchema, type GlobalVariable } from './GlobalVariables.ts'
import { ScriptListSchema, type ScriptEntry, type ScriptGroup } from './ScriptList.ts'

/** @anchor bescript-device-context */
export interface DeviceFunction {
  readonly binding: ScriptEntry
  readonly ast: LocalAst
}

/** @anchor bescript-device-context */
export interface BescriptDeviceContext {
  readonly metadata: DeviceMetadata
  readonly globalVariables: readonly GlobalVariable[]
  readonly scriptGroups: readonly ScriptGroup<DeviceFunction>[]
}

/** @anchor bescript-device-context */
export class DeviceContextSchema {
  private static readonly functionSchema: z.ZodType<DeviceFunction> = z.strictObject({
    binding: z
      .unknown()
      .transform((input: unknown): ScriptEntry => ScriptListSchema.parseEntry(input)),
    ast: z.custom<LocalAst>((input: unknown): boolean => {
      LocalAstSchema.parse(input)
      return true
    }),
  })

  private static readonly group: z.ZodType<ScriptGroup<DeviceFunction>> = z.strictObject({
    kind: z.enum(['custom', 'event']),
    eventName: BescriptVocabulary.option(z.string().min(1)),
    entries: z.array(DeviceContextSchema.functionSchema),
  })

  private static readonly context: z.ZodType<BescriptDeviceContext> = z.strictObject({
    metadata: DeviceMetadataSchema.schema,
    globalVariables: z
      .unknown()
      .transform((input: unknown): readonly GlobalVariable[] => GlobalVariableSchema.parse(input)),
    scriptGroups: z.array(DeviceContextSchema.group),
  })

  private constructor() {}

  /** @anchor bescript-device-context */
  public static parse(input: unknown): BescriptDeviceContext {
    const context: BescriptDeviceContext = DeviceContextSchema.context.parse(input)
    const groups: Set<string> = new Set<string>()
    for (const group of context.scriptGroups) {
      if ((group.kind === 'event') !== Option.isSome(group.eventName)) {
        throw new Error('事件组必须携带事件名，自定义组不能携带事件名')
      }
      const key: string = JSON.stringify([group.kind, group.eventName])
      if (groups.has(key)) {
        throw new Error('脚本组重复: ' + key)
      }
      groups.add(key)
      const names: Set<string> = new Set<string>()
      for (const item of group.entries) {
        if (names.has(item.binding.name)) {
          throw new Error('脚本组内引用名称重复: ' + item.binding.name)
        }
        names.add(item.binding.name)
      }
    }
    return context
  }
}
