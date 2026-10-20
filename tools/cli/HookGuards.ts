import { readFileSync } from 'node:fs'
import { execFileSync } from 'node:child_process'
import { resolve } from 'node:path'
import { Option } from 'effect'
import { Git } from './Git.ts'

/** @anchor hook-boundary */
export class CommitMessageGuard {
  private constructor() {}

  /**
   * @anchor hook-boundary
   */
  public static run(repositoryFullPath: string, messageFileRelativePath: string): number {
    // 容错说明: Windows 环境下 COMMIT_EDITMSG 可能以 CRLF 落盘,行尾 \r 属可纠正的
    // 格式误差(语义不变),按 LF 等价剥离后判定;不构成对非法标题的放宽。
    const output: string = execFileSync('git', ['stripspace', '--strip-comments'], {
      cwd: repositoryFullPath,
      env: Git.cleanEnv(),
      input: readFileSync(resolve(repositoryFullPath, messageFileRelativePath), 'utf8'),
      encoding: 'utf8',
      windowsHide: true,
      timeout: 60000,
    })
    // eslint-disable-next-line no-restricted-properties -- git stripspace 的输出末尾换行不计为提交消息空行。
    const lines: string[] = output
      .trimEnd()
      .split('\n')
      .map((line: string): string => (line.endsWith('\r') ? line.slice(0, -1) : line))
    const title: string = Option.getOrElse(Option.fromNullishOr(lines[0]), (): string => {
      throw new Error('提交消息不能为空')
    })
    if (
      title.length === 0 ||
      lines.length > 15 ||
      lines.some((line: string): boolean => Array.from(line).length > 72)
    ) {
      throw new Error('提交标题须非空；每行最多 72 字符，总计最多 15 行')
    }
    return 0
  }
}
