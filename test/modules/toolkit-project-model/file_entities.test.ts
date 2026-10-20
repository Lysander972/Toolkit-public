import { test } from 'vitest'
import assert from 'node:assert/strict'
import { GuidFormat } from '../../../src/toolkit/project_model/GuidFormat.ts'
import { GuidMetadataParser } from '../../../src/toolkit/project_model/FileEntities.ts'

/** @anchor entity-guid-format */
export class GuidMetadataTests {
  private constructor() {}

  static {
    test('原始 GUID 标量与真实 Hjson 伴生元数据共享身份格式', (): void => {
      const guid: string = '12345678901234567890123456789012'
      assert.equal(GuidMetadataParser.parse(guid), guid)
      assert.equal(GuidMetadataParser.parse(`guid: "${guid}"\nquality: 80\nslice: thumbnail`), guid)
      assert.equal(GuidFormat.validate('a'.repeat(32)), true)
      assert.equal(GuidFormat.validate('A'.repeat(32)), false)
      assert.equal(GuidFormat.validate('a'.repeat(31)), false)
      assert.throws((): string => GuidMetadataParser.parse(`guid: ${guid}`))
      assert.throws((): string => GuidMetadataParser.parse(`guid: ${'z'.repeat(32)}`))
      assert.throws((): string => GuidMetadataParser.parse(`quality: 80`))
      assert.throws((): string => GuidMetadataParser.parse(`"${guid}"`))
    })
  }
}
