import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { test } from 'vitest'
import { Git } from '../../../tools/cli/Git.ts'

/** @anchor mutation-incremental */
export class TaskCachePathTest {
  private constructor() {}

  static {
    test('同一仓库的 worktree 共用 Git common dir 里的 Stryker incremental 文件', (): void => {
      mkdirSync(resolve('_build/tests'), { recursive: true })
      const fixtureFullPath: string = mkdtempSync(resolve('_build/tests', 'incremental-'))
      const repositoryFullPath: string = join(fixtureFullPath, 'repository')
      const worktreeFullPath: string = join(fixtureFullPath, 'worktree')
      mkdirSync(repositoryFullPath)
      try {
        Git.run(repositoryFullPath, ['init'], false)
        Git.run(repositoryFullPath, ['config', 'core.autocrlf', 'false'], false)
        writeFileSync(join(repositoryFullPath, 'README.md'), 'incremental\n')
        Git.run(repositoryFullPath, ['add', 'README.md'], false)
        Git.run(
          repositoryFullPath,
          [
            '-c',
            'user.name=Incremental Test',
            '-c',
            'user.email=incremental@example.invalid',
            '-c',
            'commit.gpgsign=false',
            'commit',
            '-m',
            'incremental fixture',
          ],
          false,
        )
        Git.run(repositoryFullPath, ['worktree', 'add', '--detach', worktreeFullPath], false)
        assert.equal(Git.incrementalFile(repositoryFullPath), Git.incrementalFile(worktreeFullPath))
        assert.equal(
          Git.incrementalFile(repositoryFullPath),
          join(
            Git.run(
              repositoryFullPath,
              ['rev-parse', '--path-format=absolute', '--git-common-dir'],
              false,
            ),
            'stryker-incremental.json',
          ),
        )
      } finally {
        rmSync(fixtureFullPath, { recursive: true, force: true })
      }
    })
  }
}
