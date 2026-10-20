import assert from 'node:assert/strict'
import { test } from 'vitest'
import { SdkDocument, type SdkDocumentData } from '../../../src/toolkit/semantic/SdkDocument.ts'

/** @anchor sdk-document-contract */
export class SdkDocumentTests {
  private constructor() {}

  static {
    test('SDK 双向空壳明确失败并保持非空文档', (): void => {
      const document: SdkDocumentData = {
        formatVersion: 1,
        sourceRevision: 'platform-v1',
        platform: {
          types: [],
          methods: [],
          events: [],
          enums: [],
          identifiers: [{ besName: '速度', tsName: 'speed' }],
        },
        documentation: [{ symbol: '速度', text: '中文说明\n"引号"与\\路径' }],
      }
      const original: SdkDocumentData = structuredClone(document)
      const text: string = JSON.stringify(document)
      assert.throws((): SdkDocumentData => SdkDocument.read(text), {
        name: 'Error',
        message: 'TODO: 校验并导入结构化 SDK JSON 文档',
      })
      assert.throws((): string => SdkDocument.write(document), {
        name: 'Error',
        message: 'TODO: 校验并导出结构化 SDK JSON 文档',
      })
      assert.deepEqual(document, original)
    })
  }
}
