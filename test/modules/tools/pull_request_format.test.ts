import assert from 'node:assert/strict'
import { test } from 'vitest'
import { PullRequestFormat } from '../../../tools/cli/PullRequestFormat.ts'

/** @anchor pull-request-format */
export class PullRequestFormatTest {
  private constructor() {}

  static {
    test('PR 标题和四个正文分区符合维护者格式', (): void => {
      assert.deepEqual(
        PullRequestFormat.validate(
          'docs(governance): standardize pull request format',
          '## 变更\n整理入口。\n\n## 验证\npnpm check。\n\n## 追踪\nIssue #186。\n\n## 风险\n仅影响文档。',
        ),
        [],
      )
    })

    test('PR 标题不合规或章节为空时拒绝', (): void => {
      const violations: readonly string[] = PullRequestFormat.validate(
        '整理文档',
        '## 变更\n\n## 验证\npnpm check。\n\n## 追踪\nIssue #186。\n\n## 风险\n仅影响文档。',
      )
      assert.equal(violations.length, 2)
      assert.ok(violations.some((violation: string): boolean => violation.includes('标题')))
      assert.ok(violations.some((violation: string): boolean => violation.includes('变更')))
    })

    test('PR 正文缺少固定章节时拒绝', (): void => {
      const violations: readonly string[] = PullRequestFormat.validate(
        'docs(governance): standardize pull request format',
        '## 变更\n整理入口。\n\n## 风险\n仅影响文档。',
      )
      assert.equal(violations.length, 1)
      assert.equal(violations[0]?.includes('按顺序包含'), true)
    })
  }
}
