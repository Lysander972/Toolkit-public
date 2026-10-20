import { test } from 'vitest'
import assert from 'node:assert/strict'
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { spawnSync } from 'node:child_process'
import type { SpawnSyncReturns } from 'node:child_process'
import { delimiter, join, resolve } from 'node:path'
import { Git } from '../../../tools/cli/Git.ts'
import { CommitMessageGuard } from '../../../tools/cli/HookGuards.ts'
import { Option } from 'effect'

/**
 * @anchor hook-boundary
 * @anchor isolated-filesystem
 */
export class HookBoundaryRulesTest {
  private constructor() {}

  static {
    test('提交消息按 Git 配置处理冲突注释，人工正文仍受格式约束', (): void => {
      mkdirSync(resolve('_build/tests'), { recursive: true })
      const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-message-'))
      const messageFileRelativePath: string = 'COMMIT_EDITMSG'
      const messageFullPath: string = join(rootFullPath, messageFileRelativePath)
      try {
        Git.run(rootFullPath, ['init'])
        writeFileSync(
          messageFullPath,
          'test: resolve conflict\r\n\r\n人工正文\r\n# Conflicts:\r\n# ' + 'a'.repeat(100) + '\r\n',
        )
        assert.equal(CommitMessageGuard.run(rootFullPath, messageFileRelativePath), 0)
        Git.run(rootFullPath, ['config', 'core.commentChar', ';'])
        writeFileSync(
          messageFullPath,
          'test: configured comments\n\n人工正文\n; ' + 'b'.repeat(100) + '\n',
        )
        assert.equal(CommitMessageGuard.run(rootFullPath, messageFileRelativePath), 0)
        writeFileSync(messageFullPath, 'test: long body\n\n' + 'a'.repeat(100) + '\n')
        assert.throws(
          (): number => CommitMessageGuard.run(rootFullPath, messageFileRelativePath),
          /每行最多 72 字符/,
        )
        writeFileSync(messageFullPath, '131_第二阶段_tools.cli\n; ' + 'b'.repeat(100) + '\n')
        assert.equal(CommitMessageGuard.run(rootFullPath, messageFileRelativePath), 0)
        writeFileSync(messageFullPath, 'tools.hooks\n')
        assert.equal(CommitMessageGuard.run(rootFullPath, messageFileRelativePath), 0)
        writeFileSync(messageFullPath, '\n; ' + 'b'.repeat(100) + '\n')
        assert.throws(
          (): number => CommitMessageGuard.run(rootFullPath, messageFileRelativePath),
          /提交标题须非空/,
        )
        writeFileSync(messageFullPath, 'test: long message\n\n' + '人工正文\n'.repeat(15))
        assert.throws(
          (): number => CommitMessageGuard.run(rootFullPath, messageFileRelativePath),
          /总计最多 15 行/,
        )
      } finally {
        rmSync(rootFullPath, { recursive: true })
      }
    })

    test('post-commit 仅执行 LFS，失败的 review 入口与提交后步骤隔离', (): void => {
      mkdirSync(resolve('_build/tests'), { recursive: true })
      const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-commit-'))
      const repositoryFullPath: string = join(rootFullPath, 'repository')
      const hooksFullPath: string = join(rootFullPath, 'hooks')
      const binFullPath: string = join(rootFullPath, 'bin')
      const markerFullPath: string = join(rootFullPath, 'lfs-ran')
      try {
        mkdirSync(repositoryFullPath)
        mkdirSync(hooksFullPath)
        mkdirSync(binFullPath)
        Git.run(repositoryFullPath, ['init'])
        Git.run(repositoryFullPath, ['config', 'user.name', 'Commit Test'])
        Git.run(repositoryFullPath, ['config', 'user.email', 'commit@example.invalid'])
        writeFileSync(join(repositoryFullPath, 'package.json'), '{"scripts":{"review":"exit 17"}}')
        writeFileSync(join(repositoryFullPath, 'source.txt'), 'root')
        Git.run(repositoryFullPath, ['add', '.'])
        Git.run(repositoryFullPath, ['commit', '-m', 'test: initial'])
        writeFileSync(join(repositoryFullPath, 'source.txt'), 'changed')
        Git.run(repositoryFullPath, ['add', 'source.txt'])
        writeFileSync(
          join(hooksFullPath, 'post-commit'),
          readFileSync('tools/git-hooks/post-commit'),
        )
        chmodSync(join(hooksFullPath, 'post-commit'), 0o755)
        writeFileSync(
          join(binFullPath, 'git-lfs'),
          '#!/bin/sh\nprintf ran > "' + markerFullPath.replaceAll('\\', '/') + '"\n',
        )
        chmodSync(join(binFullPath, 'git-lfs'), 0o755)
        Git.run(repositoryFullPath, ['config', 'core.hooksPath', hooksFullPath])
        const result: SpawnSyncReturns<string> = spawnSync(
          'git',
          ['--exec-path=' + binFullPath, 'hook', 'run', 'post-commit'],
          {
            cwd: repositoryFullPath,
            env: {
              ...Git.cleanEnv(),
              PATH:
                binFullPath + delimiter + Option.getOrThrow(Option.fromNullishOr(process.env.PATH)),
            },
            encoding: 'utf8',
            windowsHide: true,
          },
        )
        assert.equal(result.status, 0, result.stdout + result.stderr)
        assert.equal(readFileSync(markerFullPath, 'utf8'), 'ran')
        writeFileSync(join(repositoryFullPath, 'package.json'), '{"scripts":{"review":"exit 0"}}')
        const success: SpawnSyncReturns<string> = spawnSync(
          'git',
          ['--exec-path=' + binFullPath, 'hook', 'run', 'post-commit'],
          {
            cwd: repositoryFullPath,
            env: {
              ...Git.cleanEnv(),
              PATH:
                binFullPath + delimiter + Option.getOrThrow(Option.fromNullishOr(process.env.PATH)),
            },
            encoding: 'utf8',
            windowsHide: true,
          },
        )
        assert.equal(success.status, 0, success.stdout + success.stderr)
        assert.equal(readFileSync(markerFullPath, 'utf8'), 'ran')
      } finally {
        rmSync(rootFullPath, { recursive: true })
      }
    })

    test('Git LFS 传输失败阻断真实 Git push', (): void => {
      mkdirSync(resolve('_build/tests'), { recursive: true })
      const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-push-'))
      const repositoryFullPath: string = join(rootFullPath, 'repository')
      const remoteFullPath: string = join(rootFullPath, 'remote.git')
      const hooksFullPath: string = join(rootFullPath, 'hooks')
      try {
        mkdirSync(repositoryFullPath)
        mkdirSync(remoteFullPath)
        mkdirSync(hooksFullPath)
        Git.run(repositoryFullPath, ['init'])
        Git.run(remoteFullPath, ['init', '--bare'])
        Git.run(repositoryFullPath, ['config', 'user.name', 'Push Test'])
        Git.run(repositoryFullPath, ['config', 'user.email', 'push@example.invalid'])
        Git.run(repositoryFullPath, ['config', 'core.hooksPath', hooksFullPath])
        Git.run(repositoryFullPath, ['remote', 'add', 'origin', remoteFullPath])
        Git.run(repositoryFullPath, ['lfs', 'track', '*.bin'])
        writeFileSync(join(repositoryFullPath, 'sample.bin'), 'lfs payload')
        Git.run(repositoryFullPath, ['add', '.'])
        Git.run(repositoryFullPath, ['commit', '-m', 'test: lfs fixture'])
        rmSync(
          join(Git.run(repositoryFullPath, ['rev-parse', '--absolute-git-dir']), 'lfs', 'objects'),
          { recursive: true },
        )
        const hookFullPath: string = join(hooksFullPath, 'pre-push')
        writeFileSync(hookFullPath, readFileSync('tools/git-hooks/pre-push'))
        chmodSync(hookFullPath, 0o755)
        assert.throws((): string =>
          Git.run(repositoryFullPath, ['push', 'origin', 'HEAD:refs/heads/test']),
        )
        assert.equal(
          Git.succeeds(remoteFullPath, ['show-ref', '--verify', 'refs/heads/test']),
          false,
        )
      } finally {
        rmSync(rootFullPath, { recursive: true })
      }
    })

    test('普通 Git push 不被检视传输门禁拦截', (): void => {
      mkdirSync(resolve('_build/tests'), { recursive: true })
      const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-push-'))
      const repositoryFullPath: string = join(rootFullPath, 'repository')
      const remoteFullPath: string = join(rootFullPath, 'remote.git')
      const hooksFullPath: string = join(rootFullPath, 'hooks')
      try {
        mkdirSync(repositoryFullPath)
        mkdirSync(remoteFullPath)
        mkdirSync(hooksFullPath)
        Git.run(repositoryFullPath, ['init'])
        Git.run(remoteFullPath, ['init', '--bare'])
        Git.run(repositoryFullPath, ['config', 'user.name', 'Push Test'])
        Git.run(repositoryFullPath, ['config', 'user.email', 'push@example.invalid'])
        Git.run(repositoryFullPath, ['config', 'core.hooksPath', hooksFullPath])
        Git.run(repositoryFullPath, ['remote', 'add', 'origin', remoteFullPath])
        writeFileSync(join(repositoryFullPath, 'source.txt'), 'source')
        Git.run(repositoryFullPath, ['add', 'source.txt'])
        Git.run(repositoryFullPath, ['commit', '-m', 'test: ordinary push'])
        const hookFullPath: string = join(hooksFullPath, 'pre-push')
        writeFileSync(hookFullPath, readFileSync('tools/git-hooks/pre-push'))
        chmodSync(hookFullPath, 0o755)
        Git.run(repositoryFullPath, ['push', 'origin', 'HEAD:refs/heads/test'])
        assert.equal(
          Git.succeeds(remoteFullPath, ['show-ref', '--verify', 'refs/heads/test']),
          true,
        )
      } finally {
        rmSync(rootFullPath, { recursive: true })
      }
    })
  }
}
