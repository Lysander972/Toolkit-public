import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { Option } from 'effect'
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
          Option.some(
            '## 变更\n整理入口。\n\n## 验证\npnpm check。\n\n## 追踪\nIssue #186。\n\n## 风险\n仅影响文档。',
          ),
        ),
        [],
      )
    })

    test('PR 标题不合规或章节为空时拒绝', (): void => {
      const violations: readonly string[] = PullRequestFormat.validate(
        '整理文档',
        Option.some(
          '## 变更\n\n## 验证\npnpm check。\n\n## 追踪\nIssue #186。\n\n## 风险\n仅影响文档。',
        ),
      )
      assert.equal(violations.length, 2)
      assert.ok(violations.some((violation: string): boolean => violation.includes('标题')))
      assert.ok(violations.some((violation: string): boolean => violation.includes('变更')))
    })

    test('PR 正文缺少固定章节时拒绝', (): void => {
      const violations: readonly string[] = PullRequestFormat.validate(
        'docs(governance): standardize pull request format',
        Option.some('## 变更\n整理入口。\n\n## 风险\n仅影响文档。'),
      )
      assert.equal(violations.length, 1)
      assert.equal(violations[0]?.includes('按顺序包含'), true)
    })

    test('原生事件的 null 正文被拒绝，空格按非空字符串保留', (): void => {
      mkdirSync(resolve('_build/tests'), { recursive: true })
      const directoryFullPath: string = mkdtempSync(resolve('_build/tests/pull-request-'))
      const eventFullPath: string = join(directoryFullPath, 'event.json')
      try {
        writeFileSync(
          eventFullPath,
          JSON.stringify({ pull_request: { title: 'docs: format', body: null } }),
        )
        assert.equal(PullRequestFormat.run(eventFullPath), 1)
        assert.deepEqual(
          PullRequestFormat.validate(
            'docs: format',
            Option.some('## 变更\n \n## 验证\n \n## 追踪\n \n## 风险\n '),
          ),
          [],
        )
      } finally {
        rmSync(directoryFullPath, { recursive: true })
      }
    })
  }
}
