import assert from 'node:assert/strict'
import { test } from 'vitest'
import { PatchMatcher, type PatchEdit } from '../../../src/toolkit/mcp/PatchMatcher.ts'

/** @anchor patch-matching-contract */
export class PatchMatchingTest {
  private constructor() {}

  static {
    test('匹配空壳明确失败并保留原文及顺序编辑', (): void => {
      const originalText: string = 'namespace 示例 {\r\n  const 文本 = "\\u4e2d😀"\r\n}\r\n'
      const edits: readonly PatchEdit[] = Object.freeze([
        Object.freeze({ oldText: '文本', newText: '标题', replaceAll: false }),
        Object.freeze({ oldText: '标题', newText: '', replaceAll: true }),
      ])
      const before: readonly PatchEdit[] = structuredClone(edits)
      assert.throws((): string => PatchMatcher.apply(originalText, edits), {
        message: 'TODO: 按唯一精确匹配与顺序编辑合同生成完整更新文本',
      })
      assert.deepEqual(edits, before)
    })
  }
}
