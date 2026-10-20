import { test } from 'vitest'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { Option } from 'effect'
import {
  DeviceMetadataSchema,
  type DeviceMetadata,
} from '../../../src/toolkit/script/device_context/DeviceMetadata.ts'

/** @anchor bescript-device-metadata */
export class DeviceMetadataTest {
  private constructor() {}

  static {
    test('读取零件元数据并独立校验结构化结果', async (): Promise<void> => {
      const metadata: DeviceMetadata = DeviceMetadataSchema.read(
        await readFile('test/data/projects/姓氏玩法/map.map_/54/device.json', 'utf8'),
      )
      assert.equal(metadata.deviceType, 1101)
      assert.deepEqual(metadata.logicName, Option.some('弹幕'))
      assert.deepEqual(metadata.mechFileGuid, Option.none())
      assert.deepEqual(metadata.stickToBone, Option.none())
      assert.deepEqual(DeviceMetadataSchema.schema.parse(metadata), metadata)
      const mech: DeviceMetadata = DeviceMetadataSchema.read(
        await readFile('test/data/projects/姓氏玩法/map.map_/380/device.json', 'utf8'),
      )
      assert.deepEqual(mech.logicName, Option.some('飞机模型'))
      assert.deepEqual(mech.mechFileGuid, Option.some('9dcedcd82b7f40a58d59792fe39bd476'))
      assert.deepEqual(
        DeviceMetadataSchema.read('type:1\nstickToBone:Head\n').stickToBone,
        Option.some('Head'),
      )
      assert.deepEqual(
        DeviceMetadataSchema.read('type:1100\next:\n{\n logicName:4\n}\n').logicName,
        Option.some('4'),
      )
      const numeric: DeviceMetadata = DeviceMetadataSchema.read(
        await readFile('test/data/projects/颜色对对碰/机械/边界.mech_/6/device.json', 'utf8'),
      )
      assert.deepEqual(numeric.logicName, Option.some('4'))
      assert.deepEqual(DeviceMetadataSchema.schema.parse(numeric), numeric)
    })

    test('缺席与非法值不混淆，不接受原始文本作为结构化元数据', (): void => {
      assert.throws((): DeviceMetadata => DeviceMetadataSchema.schema.parse('type:1\n'))
      assert.throws((): DeviceMetadata =>
        DeviceMetadataSchema.read('type:1\next:\n{\n mechFileGUID:abc\n}\n'),
      )
      assert.throws((): DeviceMetadata => DeviceMetadataSchema.read('type:1\nstickToBone:null\n'))
      assert.throws((): DeviceMetadata => DeviceMetadataSchema.read('type:1\next:null\n'))
    })
  }
}
