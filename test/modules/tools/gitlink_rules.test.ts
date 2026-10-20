import { test, vi } from 'vitest'
import type { MockInstance } from 'vitest'
import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { Git } from '../../../tools/cli/Git.ts'
import { CheckGitlink } from '../../../tools/cli/CheckGitlink.ts'

/** @anchor submodule-gate */
export class GitlinkTest {
  private constructor() {}

  private static synchronize(this: void): void {
    mkdirSync(resolve('_build/tests'), { recursive: true })
    const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'gitlink-lifecycle-'))
    const sourceFullPath: string = join(rootFullPath, 'source')
    const repositoryFullPath: string = join(rootFullPath, 'repository')
    const receiverFullPath: string = join(rootFullPath, 'receiver')
    const remoteFullPath: string = join(rootFullPath, 'main.git')
    const childRemoteFullPath: string = join(rootFullPath, 'child.git')
    try {
      vi.stubEnv('HOME', rootFullPath)
      writeFileSync(join(rootFullPath, '.gitconfig'), '[protocol "file"]\n\tallow = always\n')
      for (const fullPath of [sourceFullPath, repositoryFullPath]) {
        mkdirSync(fullPath)
        Git.run(fullPath, ['init', '--initial-branch=main'])
        Git.run(fullPath, ['config', 'user.name', 'Gitlink Test'])
        Git.run(fullPath, ['config', 'user.email', 'gitlink@example.invalid'])
        Git.run(fullPath, ['config', 'protocol.file.allow', 'always'])
      }
      Git.run(rootFullPath, ['init', '--bare', '--initial-branch=main', childRemoteFullPath])
      Git.run(rootFullPath, ['init', '--bare', '--initial-branch=main', remoteFullPath])
      writeFileSync(join(sourceFullPath, 'source.txt'), 'first')
      Git.run(sourceFullPath, ['add', '.'])
      Git.run(sourceFullPath, ['commit', '-m', 'test: first child'])
      Git.run(sourceFullPath, ['remote', 'add', 'origin', childRemoteFullPath])
      Git.run(sourceFullPath, ['push', '-u', 'origin', 'main'])
      const first: string = Git.run(sourceFullPath, ['rev-parse', 'HEAD'])
      Git.run(repositoryFullPath, [
        '-c',
        'protocol.file.allow=always',
        'submodule',
        'add',
        childRemoteFullPath,
        'reference',
      ])
      Git.run(repositoryFullPath, ['commit', '-m', 'test: parent'])
      Git.run(repositoryFullPath, ['remote', 'add', 'origin', remoteFullPath])
      assert.equal(CheckGitlink.synchronize(repositoryFullPath), 0)
      Git.run(repositoryFullPath, ['config', '--local', 'push.recurseSubmodules', 'on-demand'])
      Git.run(repositoryFullPath, ['config', '--local', 'fetch.recurseSubmodules', 'on-demand'])
      assert.equal(
        Git.run(repositoryFullPath, ['config', '--get', 'push.recurseSubmodules']),
        'on-demand',
      )
      assert.equal(
        Git.run(repositoryFullPath, ['config', '--get', 'fetch.recurseSubmodules']),
        'on-demand',
      )
      const childFullPath: string = join(repositoryFullPath, 'reference')
      Git.run(childFullPath, ['config', 'user.name', 'Gitlink Test'])
      Git.run(childFullPath, ['config', 'user.email', 'gitlink@example.invalid'])
      writeFileSync(join(childFullPath, 'source.txt'), 'dirty')
      Git.run(repositoryFullPath, [
        'config',
        '--file',
        '.gitmodules',
        'submodule.absent.path',
        'absent',
      ])
      Git.run(repositoryFullPath, [
        'config',
        '--file',
        '.gitmodules',
        'submodule.absent.url',
        childRemoteFullPath,
      ])
      Git.run(repositoryFullPath, ['add', '.gitmodules'])
      Git.run(repositoryFullPath, [
        'update-index',
        '--add',
        '--cacheinfo',
        '160000,' + first + ',absent',
      ])
      const dirtyCalls: MockInstance<typeof Git.run> = vi.spyOn(Git, 'run')
      assert.throws((): void => {
        CheckGitlink.synchronize(repositoryFullPath)
      }, /未提交修改/)
      assert.equal(
        dirtyCalls.mock.calls.some(([, args]: Parameters<typeof Git.run>): boolean =>
          args.includes('submodule'),
        ),
        false,
      )
      dirtyCalls.mockRestore()
      Git.run(repositoryFullPath, ['update-index', '--force-remove', 'absent'])
      Git.run(repositoryFullPath, [
        'config',
        '--file',
        '.gitmodules',
        '--remove-section',
        'submodule.absent',
      ])
      Git.run(repositoryFullPath, ['add', '.gitmodules'])
      Git.run(childFullPath, ['add', '.'])
      Git.run(childFullPath, ['commit', '-m', 'test: clean drift'])
      assert.notEqual(Git.run(childFullPath, ['rev-parse', 'HEAD']), first)
      const published: string = Git.run(childFullPath, ['rev-parse', 'HEAD'])
      Git.run(repositoryFullPath, ['add', 'reference'])
      Git.run(repositoryFullPath, ['commit', '-m', 'test: publish parent'])
      Git.run(repositoryFullPath, ['push', '-u', 'origin', 'main'])
      assert.equal(
        Git.run(rootFullPath, ['--git-dir=' + childRemoteFullPath, 'cat-file', '-t', published]),
        'commit',
      )
      Git.run(repositoryFullPath, ['update-index', '--cacheinfo', '160000,' + first + ',reference'])
      assert.equal(CheckGitlink.synchronize(repositoryFullPath), 0)
      assert.equal(Git.run(childFullPath, ['rev-parse', 'HEAD']), first)
      Git.run(rootFullPath, ['clone', remoteFullPath, receiverFullPath])
      Git.run(receiverFullPath, ['config', 'protocol.file.allow', 'always'])
      assert.equal(CheckGitlink.synchronize(receiverFullPath), 0)
      assert.equal(Git.run(join(receiverFullPath, 'reference'), ['rev-parse', 'HEAD']), published)
      assert.equal(CheckGitlink.run(receiverFullPath, 'HEAD'), 0)
    } finally {
      vi.unstubAllEnvs()
      rmSync(rootFullPath, { recursive: true })
    }
  }

  static {
    test('真实子模块 dirty、干净失配、缺失初始化与 on-demand 推送', GitlinkTest.synchronize)
    test('gitlink 接受未物化；已物化时拒绝脏工作树和 HEAD 漂移', (): void => {
      mkdirSync(resolve('_build/tests'), { recursive: true })
      const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-gitlink-'))
      const childFullPath: string = join(rootFullPath, 'reference')
      try {
        Git.run(rootFullPath, ['init'])
        writeFileSync(
          join(rootFullPath, '.gitmodules'),
          '[submodule "reference"]\n path = reference\n url = ../child.git\n',
        )
        Git.run(rootFullPath, ['add', '.gitmodules'])
        Git.run(rootFullPath, [
          'update-index',
          '--add',
          '--cacheinfo',
          '160000,' + 'a'.repeat(40) + ',reference',
        ])
        assert.equal(CheckGitlink.run(rootFullPath, Git.run(rootFullPath, ['write-tree'])), 0)
        const missingCalls: MockInstance<typeof Git.run> = vi.spyOn(Git, 'run')
        assert.equal(CheckGitlink.maintain(rootFullPath), 0)
        assert.equal(CheckGitlink.maintain(rootFullPath), 0)
        assert.equal(
          missingCalls.mock.calls.some(([, args]: Parameters<typeof Git.run>): boolean =>
            args.includes('submodule'),
          ),
          false,
        )
        missingCalls.mockRestore()
        mkdirSync(childFullPath)
        Git.run(childFullPath, ['init'])
        Git.run(childFullPath, ['config', 'user.name', 'Gitlink Test'])
        Git.run(childFullPath, ['config', 'user.email', 'gitlink@example.invalid'])
        writeFileSync(join(childFullPath, 'source.txt'), 'first')
        Git.run(childFullPath, ['add', 'source.txt'])
        Git.run(childFullPath, ['commit', '-m', 'test: first'])
        const first: string = Git.run(childFullPath, ['rev-parse', 'HEAD'])
        Git.run(rootFullPath, ['update-index', '--cacheinfo', '160000,' + first + ',reference'])
        const index: string = Git.run(rootFullPath, ['write-tree'])
        assert.equal(CheckGitlink.run(rootFullPath, index), 0)
        writeFileSync(join(childFullPath, 'source.txt'), 'second')
        assert.equal(CheckGitlink.run(rootFullPath, index), 1)
        Git.run(childFullPath, ['add', 'source.txt'])
        Git.run(childFullPath, ['commit', '-m', 'test: second'])
        assert.equal(CheckGitlink.run(rootFullPath, index), 1)
        Git.run(rootFullPath, [
          'update-index',
          '--cacheinfo',
          '160000,' + Git.run(childFullPath, ['rev-parse', 'HEAD']) + ',reference',
        ])
        assert.equal(CheckGitlink.run(rootFullPath, Git.run(rootFullPath, ['write-tree'])), 0)
        Git.run(rootFullPath, ['update-index', '--force-remove', 'reference'])
        assert.equal(CheckGitlink.run(rootFullPath, Git.run(rootFullPath, ['write-tree'])), 1)
        writeFileSync(join(rootFullPath, '.gitmodules'), '')
        Git.run(rootFullPath, ['add', '.gitmodules'])
        assert.equal(CheckGitlink.maintain(rootFullPath), 0)
      } finally {
        rmSync(rootFullPath, { recursive: true })
      }
    })
  }
}
