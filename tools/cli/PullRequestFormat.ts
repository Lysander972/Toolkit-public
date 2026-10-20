import { readFileSync } from 'node:fs'
import { Option } from 'effect'
import { z } from 'zod'

/** @anchor pull-request-format */
interface PullRequestEvent {
  readonly pull_request: PullRequestData
}

interface PullRequestData {
  readonly title: string
  readonly body: Option.Option<string>
}

/** @anchor pull-request-format */
export class PullRequestFormat {
  private static readonly eventSchema: z.ZodType<PullRequestEvent> = z.object({
    pull_request: z.object({
      title: z.string(),
      body: z.string().nullable().transform(Option.fromNullishOr),
    }),
  })

  private constructor() {}

  /**
   * @anchor pull-request-format
   * @format-exempt pull-request-title 外部单行标题的锚定格式校验，不解析正文结构。
   */
  public static validate(title: string, body: Option.Option<string>): readonly string[] {
    const titlePattern: RegExp =
      /^(feat|fix|refactor|docs|test|build|ci|chore|perf|revert)(\([a-z0-9][a-z0-9._/-]*\))?!?: \S.*$/
    const violations: string[] = []
    if (Array.from(title).length > 72 || !titlePattern.test(title)) {
      violations.push('PR 标题必须使用 Conventional Commits 格式且不超过 72 字符')
    }
    const sections: readonly string[] = ['变更', '验证', '追踪', '风险']
    if (Option.isNone(body)) {
      violations.push('PR 正文必须按顺序包含 ## 变更、## 验证、## 追踪、## 风险')
      return violations
    }
    const lines: readonly string[] = body.value.split('\n')
    const headings: readonly number[] = sections.map((section: string): number =>
      lines.findIndex((line: string): boolean => line === '## ' + section),
    )
    if (headings.some((index: number): boolean => index < 0)) {
      violations.push('PR 正文必须按顺序包含 ## 变更、## 验证、## 追踪、## 风险')
      return violations
    }
    if (
      headings.some(
        (index: number, sectionIndex: number): boolean =>
          sectionIndex > 0 &&
          headings.slice(0, sectionIndex).some((previous: number): boolean => index <= previous),
      )
    ) {
      violations.push('PR 正文四个固定章节必须按变更、验证、追踪、风险顺序出现')
      return violations
    }
    const indices: readonly number[] = Array.from(
      { length: sections.length },
      (_: unknown, index: number): number => index,
    )
    for (const index of indices) {
      const start: number = Option.getOrThrow(Option.fromNullishOr(headings[index])) + 1
      const end: number =
        index + 1 < sections.length
          ? Option.getOrThrow(Option.fromNullishOr(headings[index + 1]))
          : lines.length
      if (!lines.slice(start, end).some((line: string): boolean => line.length > 0)) {
        violations.push(
          'PR 正文章节不得为空：## ' + Option.getOrThrow(Option.fromNullishOr(sections[index])),
        )
      }
    }
    return violations
  }

  /** @anchor pull-request-format */
  public static run(eventFullPath: string): number {
    const event: PullRequestEvent = PullRequestFormat.eventSchema.parse(
      JSON.parse(readFileSync(eventFullPath, 'utf8')),
    )
    const violations: readonly string[] = PullRequestFormat.validate(
      event.pull_request.title,
      event.pull_request.body,
    )
    for (const violation of violations) process.stderr.write(violation + '\n')
    return violations.length === 0 ? 0 : 1
  }
}
