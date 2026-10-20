import { Option } from 'effect'
import Hjson from 'hjson'
import { z } from 'zod'
import { GuidFormat } from '@toolkit/project-model/guid'
import { BescriptVocabulary } from '../vocabulary/Vocabulary.ts'

/** @anchor bescript-device-metadata */
export interface DeviceMetadata {
  readonly deviceType: number
  readonly logicName: Option.Option<string>
  readonly mechFileGuid: Option.Option<string>
  readonly stickToBone: Option.Option<string>
}

/**
 * @anchor bescript-device-metadata
 * @format-exempt hjson-object-boundary
 */
interface HjsonFields {
  readonly [key: string]: unknown
}

/** @anchor bescript-device-metadata */
export class DeviceMetadataSchema {
  public static readonly schema: z.ZodType<DeviceMetadata> = z.strictObject({
    deviceType: z.number().int().nonnegative(),
    logicName: BescriptVocabulary.option(z.string().min(1)),
    mechFileGuid: BescriptVocabulary.option(z.string().refine(GuidFormat.validate)),
    stickToBone: BescriptVocabulary.option(z.string().min(1)),
  })

  private constructor() {}

  /** @anchor bescript-device-metadata */
  public static read(deviceText: string): DeviceMetadata {
    const record: HjsonFields = z.record(z.string(), z.unknown()).parse(Hjson.parse(deviceText))
    const ext: Option.Option<HjsonFields> = Object.hasOwn(record, 'ext')
      ? Option.some(z.record(z.string(), z.unknown()).parse(record['ext']))
      : Option.none()
    return DeviceMetadataSchema.schema.parse({
      deviceType: record['type'],
      logicName: Option.flatMap(ext, (fields: HjsonFields): Option.Option<unknown> =>
        Option.map(DeviceMetadataSchema.field(fields, 'logicName'), (value: unknown): unknown =>
          typeof value === 'number' ? String(value) : value,
        ),
      ),
      mechFileGuid: Option.flatMap(ext, (fields: HjsonFields): Option.Option<unknown> =>
        DeviceMetadataSchema.field(fields, 'mechFileGUID'),
      ),
      stickToBone: DeviceMetadataSchema.field(record, 'stickToBone'),
    })
  }

  /** @anchor bescript-device-metadata */
  private static field(record: HjsonFields, key: string): Option.Option<unknown> {
    return Object.hasOwn(record, key) ? Option.some(record[key]) : Option.none()
  }
}
