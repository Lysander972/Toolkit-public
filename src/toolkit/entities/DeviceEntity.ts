import { LogicalDevice, type LogicalVariable } from '@toolkit/project-model/logical-entities'
import { Option } from 'effect'
import type { BescriptDeviceContext } from '@toolkit/script/device_context'
import type { DeviceConversionBasis } from '@toolkit/script/ts_conversion'
import { FormatEntity } from './FormatEntity.ts'
import type { EncodedEntityFile } from './FormatEntity.ts'
import type { EntityReadBaseline } from './EntityReadBaseline.ts'

/** @anchor entity-format-contract */
export class DeviceEntity extends FormatEntity {
  declare private readonly deviceNominal: never

  /** @anchor entity-format-contract */
  public constructor(
    public readonly declaration: LogicalDevice,
    public readonly context: BescriptDeviceContext,
    public readonly originals: readonly EncodedEntityFile[],
    public readonly conversionBasis: DeviceConversionBasis,
    baseline: EntityReadBaseline,
  ) {
    super(declaration.id, declaration.primaryRelativePath, baseline)
    if (conversionBasis.identity.deviceRelativePath !== declaration.primaryRelativePath)
      throw new Error('零件身份与转换依据不一致')
  }

  /** @anchor entity-format-contract */
  public override encode(): readonly EncodedEntityFile[] {
    throw new Error('TODO: 零件完整语义与原始格式的无损编码')
  }

  /** @anchor entity-format-contract */
  public withContext(context: BescriptDeviceContext): DeviceEntity {
    const declaration: LogicalDevice = new LogicalDevice({
      primaryRelativePath: this.declaration.primaryRelativePath,
      parentId: this.declaration.parentEntityId,
      files: this.declaration.files,
      directories: this.declaration.directories,
      deviceId: this.declaration.deviceId,
      deviceType: context.metadata.deviceType,
      fields: {
        logicName: context.metadata.logicName,
        mechFileGuid: context.metadata.mechFileGuid,
        stickToBone: context.metadata.stickToBone,
      },
      variables:
        Option.isNone(this.declaration.variables) && context.globalVariables.length === 0
          ? Option.none()
          : Option.some(
              context.globalVariables.map(
                (variable: BescriptDeviceContext['globalVariables'][number]): LogicalVariable => ({
                  ...variable,
                }),
              ),
            ),
    })
    return new DeviceEntity(
      declaration,
      context,
      this.originals,
      this.conversionBasis,
      this.baseline,
    )
  }
}
