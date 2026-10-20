import type { LogicalDevice, LogicalEntitySnapshot } from '@toolkit/project-model/logical-entities'
import type { CallGraph } from '@toolkit/analysis/call-graph'
import type { BescriptDeviceContext } from '@toolkit/script/device_context'
import type { DeviceConversionBasis } from '@toolkit/script/ts_conversion'
import type { SdkPlatformData } from './SdkDocument.ts'

/** @anchor semantic-assembly-pending */
export interface SemanticAssemblyInput {
  readonly entities: LogicalEntitySnapshot
  readonly analysis: CallGraph
  readonly platform: SdkPlatformData
}

/** @anchor semantic-assembly-pending */
export interface SemanticDevice {
  readonly declaration: LogicalDevice
  readonly context: BescriptDeviceContext
  readonly conversionBasis: DeviceConversionBasis
}

/** @anchor semantic-assembly-pending */
export class SemanticAssembly {
  private constructor() {}

  /** @anchor semantic-assembly-pending */
  public static assemble(_input: SemanticAssemblyInput): readonly SemanticDevice[] {
    throw new Error('TODO: 从同次逻辑快照、分析与平台输入装配完整零件语义')
  }
}
