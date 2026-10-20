import { existsSync } from 'node:fs'
import { join } from 'node:path'
import { Git } from './Git.ts'
import { WorkspaceTree } from './WorkspaceTree.ts'
import type { WorkspaceTreeEntry } from './WorkspaceTree.ts'
import { z } from 'zod'

/** @anchor submodule-gate */
interface Gitlink {
  readonly relativePath: string
  readonly revision: string
}

/** @anchor submodule-gate */
export class CheckGitlink {
  private constructor() {}

  /** @anchor submodule-gate */
  public static synchronize(repositoryFullPath: string): number {
    return CheckGitlink.update(repositoryFullPath, true)
  }

  /** @anchor submodule-gate */
  public static maintain(repositoryFullPath: string): number {
    return CheckGitlink.update(repositoryFullPath, false)
  }

  /** @anchor submodule-gate */
  private static update(repositoryFullPath: string, materialize: boolean): number {
    const revision: string = Git.run(repositoryFullPath, ['write-tree'])
    const gitlinks: readonly Gitlink[] = CheckGitlink.gitlinks(repositoryFullPath, revision)
    const registrationErrors: readonly string[] = CheckGitlink.registrations(
      repositoryFullPath,
      revision,
      gitlinks,
    )
    if (registrationErrors.length > 0) throw new Error(registrationErrors.join('\n'))
    const needed: string[] = []
    const dirty: string[] = []
    for (const gitlink of gitlinks) {
      const childFullPath: string = join(repositoryFullPath, gitlink.relativePath)
      if (!existsSync(join(childFullPath, '.git'))) {
        if (materialize) needed.push(gitlink.relativePath)
        continue
      }
      const status: string = Git.run(
        childFullPath,
        ['status', '--porcelain', '--ignore-submodules=none'],
        false,
      )
      if (status.length > 0) dirty.push(gitlink.relativePath + ': 子仓库存在未提交修改\n' + status)
      else if (Git.run(childFullPath, ['rev-parse', 'HEAD'], false) !== gitlink.revision)
        needed.push(gitlink.relativePath)
    }
    if (dirty.length > 0) throw new Error(dirty.join('\n'))
    if (needed.length > 0)
      Git.run(repositoryFullPath, [
        'submodule',
        'update',
        '--init',
        '--recursive',
        '--checkout',
        '--',
        ...needed,
      ])
    return CheckGitlink.run(repositoryFullPath, revision)
  }

  /** @anchor submodule-gate */
  public static run(repositoryFullPath: string, revision: string): number {
    const gitlinks: readonly Gitlink[] = CheckGitlink.gitlinks(repositoryFullPath, revision)
    const violations: string[] = CheckGitlink.registrations(repositoryFullPath, revision, gitlinks)
    for (const gitlink of gitlinks) {
      const childFullPath: string = join(repositoryFullPath, gitlink.relativePath)
      if (!existsSync(join(childFullPath, '.git'))) continue
      const head: string = Git.run(childFullPath, ['rev-parse', 'HEAD'], false)
      if (head !== gitlink.revision)
        violations.push(
          gitlink.relativePath +
            ': 子仓库 HEAD ' +
            head +
            ' 与 gitlink ' +
            gitlink.revision +
            ' 不一致',
        )
      const dirty: string = Git.run(
        childFullPath,
        ['status', '--porcelain', '--ignore-submodules=none'],
        false,
      )
      if (dirty.length > 0) violations.push(gitlink.relativePath + ': 子仓库不干净\n' + dirty)
    }
    for (const violation of violations) process.stderr.write(violation + '\n')
    return violations.length === 0 ? 0 : 1
  }

  /** @anchor submodule-gate */
  private static registrations(
    repositoryFullPath: string,
    revision: string,
    gitlinks: readonly Gitlink[],
  ): string[] {
    const exists: boolean = WorkspaceTree.list(repositoryFullPath, revision, ['.gitmodules']).some(
      (entry: WorkspaceTreeEntry): boolean => entry.relativePath === '.gitmodules',
    )
    if (!exists)
      return gitlinks.map(
        (gitlink: Gitlink): string => gitlink.relativePath + ': gitlink 缺少 .gitmodules 登记',
      )
    const values: readonly string[] = Git.run(repositoryFullPath, [
      'config',
      '--blob',
      revision + ':.gitmodules',
      '--null',
      '--list',
    ])
      .split('\0')
      .filter(Boolean)
    const paths: string[] = []
    for (const value of values) {
      const [key, content]: [string, string] = z
        .tuple([z.string(), z.string()])
        .parse(value.split('\n'))
      if (key.startsWith('submodule.') && key.endsWith('.path')) paths.push(content)
    }
    const expected: ReadonlySet<string> = new Set(
      gitlinks.map((gitlink: Gitlink): string => gitlink.relativePath),
    )
    return [
      ...paths
        .filter((relativePath: string): boolean => !expected.has(relativePath))
        .map(
          (relativePath: string): string => relativePath + ': .gitmodules 保留了已移除的 gitlink',
        ),
      ...gitlinks
        .filter((gitlink: Gitlink): boolean => !paths.includes(gitlink.relativePath))
        .map(
          (gitlink: Gitlink): string => gitlink.relativePath + ': gitlink 缺少 .gitmodules 登记',
        ),
    ]
  }

  /** @anchor submodule-gate */
  private static gitlinks(repositoryFullPath: string, revision: string): readonly Gitlink[] {
    return WorkspaceTree.list(repositoryFullPath, revision, [])
      .filter((entry: WorkspaceTreeEntry): boolean => entry.mode === '160000')
      .map((entry: WorkspaceTreeEntry): Gitlink => ({
        relativePath: entry.relativePath,
        revision: entry.object,
      }))
  }
}
