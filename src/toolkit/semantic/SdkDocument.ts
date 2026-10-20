import type { DeviceConversionBasis } from '@toolkit/script/ts_conversion'

/** @anchor sdk-document-contract */
export interface SdkPlatformData {
  readonly types: DeviceConversionBasis['types']
  readonly methods: DeviceConversionBasis['methods']
  readonly events: DeviceConversionBasis['events']
  readonly enums: DeviceConversionBasis['enums']
  readonly identifiers: DeviceConversionBasis['identifiers']
}

/** @anchor sdk-document-contract */
interface SdkDocumentationEntry {
  readonly symbol: string
  readonly text: string
}

/** @anchor sdk-document-contract */
export interface SdkDocumentData {
  readonly formatVersion: number
  readonly sourceRevision: string
  readonly platform: SdkPlatformData
  readonly documentation: readonly SdkDocumentationEntry[]
}

/** @anchor sdk-document-contract */
export class SdkDocument {
  private constructor() {}

  /** @anchor sdk-document-contract */
  public static read(_text: string): SdkDocumentData {
    throw new Error('TODO: 校验并导入结构化 SDK JSON 文档')
  }

  /** @anchor sdk-document-contract */
  public static write(_document: SdkDocumentData): string {
    throw new Error('TODO: 校验并导出结构化 SDK JSON 文档')
  }
}
