import { test } from 'vitest'
import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { Option } from 'effect'
import { CommitCheck } from '../../../tools/cli/CommitCheck.ts'
import { HostProcess } from '../../../tools/cli/HostProcess.ts'
import type { HostLaunch } from '../../../tools/cli/HostProcess.ts'
import type { CommitGateWork } from '../../../tools/cli/CommitCheck.ts'
import { Git } from '../../../tools/cli/Git.ts'
import { GitFixture } from '../../../tools/testing/GitFixture.ts'

/** @anchor selfcheck-gate */
class RefuseWork implements CommitGateWork {
  /** @anchor selfcheck-gate */
  public run(): Promise<number> {
    return Promise.resolve(1)
  }
}

/** @anchor selfcheck-gate */
class RestageWork implements CommitGateWork {
  /** @anchor selfcheck-gate */
  public constructor(private readonly repositoryFullPath: string) {}

  /** @anchor selfcheck-gate */
  public run(): Promise<number> {
    const relativePath: string = 'tools/code/alpha.ts'
    const text: string = '/** @anchor alpha */\nexport function alpha(): number { return 2 }\n'
    CommitCheckRulesTest.record(text)
    writeFileSync(join(this.repositoryFullPath, relativePath), text)
    Git.run(this.repositoryFullPath, ['add', '--', relativePath])
    return Promise.resolve(0)
  }
}

/** @anchor selfcheck-gate */
export class CommitCheckRulesTest {
  private static files: number = 0
  private static bytes: number = 0
  private static maximum: number = 0

  private constructor() {}

  /** @anchor selfcheck-gate */
  public static record(text: string): void {
    const size: number = Buffer.byteLength(text)
    CommitCheckRulesTest.files += 1
    CommitCheckRulesTest.bytes += size
    if (size > CommitCheckRulesTest.maximum) CommitCheckRulesTest.maximum = size
  }

  static {
    test('检查失败不写 index，检查期间重新暂存则拒绝', async (): Promise<void> => {
      mkdirSync(resolve('_build/tests'), { recursive: true })
      const rootFullPath: string = mkdtempSync(resolve('_build/tests/commit-gate-'))
      try {
        GitFixture.create(rootFullPath)
        const initial: string = Git.run(rootFullPath, ['write-tree'])
        assert.equal(await CommitCheck.gate(rootFullPath, Option.some(new RefuseWork())), 1)
        assert.equal(Git.run(rootFullPath, ['write-tree']), initial)
        await assert.rejects(
          CommitCheck.gate(rootFullPath, Option.some(new RestageWork(rootFullPath))),
          /检查期间受检 index 已变化/,
        )
        assert.notEqual(Git.run(rootFullPath, ['write-tree']), initial)
      } finally {
        rmSync(rootFullPath, { recursive: true })
      }
    })

    test('已声明的只读检查脚本并发执行，失败命令使组合失败', async (): Promise<void> => {
      mkdirSync(resolve('_build/tests'), { recursive: true })
      const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-commit-tools-'))
      try {
        const manifest: string = JSON.stringify({
          scripts: {
            'check:format': "node --eval \"require('fs').writeFileSync('format.ok','1')\"",
            'check:types': "node --eval \"require('fs').writeFileSync('types.ok','1')\"",
            'check:lint': "node --eval \"require('fs').writeFileSync('lint.ok','1')\"",
            'check:dead-code': 'node --eval "process.exit(0)"',
            'requirements:check':
              "node --eval \"require('fs').writeFileSync('requirements.ok','1')\"",
            'requirements:verify': "node --eval \"require('fs').writeFileSync('verdicts.ok','1')\"",
          },
        })
        CommitCheckRulesTest.record(manifest)
        writeFileSync(join(rootFullPath, 'package.json'), manifest)
        assert.equal(await CommitCheck.tools(rootFullPath), 0)
        assert.equal(readFileSync(join(rootFullPath, 'format.ok'), 'utf8'), '1')
        assert.equal(readFileSync(join(rootFullPath, 'types.ok'), 'utf8'), '1')
        assert.equal(readFileSync(join(rootFullPath, 'lint.ok'), 'utf8'), '1')
        assert.equal(readFileSync(join(rootFullPath, 'requirements.ok'), 'utf8'), '1')
        assert.equal(readFileSync(join(rootFullPath, 'verdicts.ok'), 'utf8'), '1')
        const failing: string = JSON.stringify({
          scripts: {
            'check:format': 'node --eval "process.exit(0)"',
            'check:types': 'node --eval "process.exit(2)"',
            'check:lint': 'node --eval "process.exit(0)"',
            'check:dead-code': 'node --eval "process.exit(0)"',
            'requirements:check': 'node --eval "process.exit(0)"',
            'requirements:verify': 'node --eval "process.exit(0)"',
          },
        })
        CommitCheckRulesTest.record(failing)
        writeFileSync(join(rootFullPath, 'package.json'), failing)
        assert.equal(await CommitCheck.tools(rootFullPath), 1)
      } finally {
        rmSync(rootFullPath, { recursive: true, force: true })
      }
    })

    test('pnpm 按宿主平台表启动，缺失进程返回 1', async (): Promise<void> => {
      const args: readonly string[] = ['--dir', 'repo', '--ignore-workspace', 'run', 'check:format']
      const windows: HostLaunch = HostProcess.pnpm('win32', args)
      assert.deepEqual(windows, {
        file: 'cmd.exe',
        args: ['/d', '/s', '/c', 'pnpm.cmd', ...args],
        windowsHide: true,
      })
      const posix: readonly NodeJS.Platform[] = [
        'aix',
        'android',
        'cygwin',
        'darwin',
        'freebsd',
        'haiku',
        'linux',
        'netbsd',
        'openbsd',
        'sunos',
      ]
      for (const platform of posix) {
        assert.deepEqual(HostProcess.pnpm(platform, args), {
          file: 'pnpm',
          args: [...args],
          windowsHide: true,
        })
      }
      mkdirSync(resolve('_build/tests'), { recursive: true })
      const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-commit-spawn-'))
      const unhandled: unknown[] = []
      const listener: (error: unknown) => void = (error: unknown): void => {
        unhandled.push(error)
      }
      process.on('unhandledRejection', listener)
      try {
        assert.equal(
          await HostProcess.run(
            { file: join(rootFullPath, 'missing-pnpm'), args: [], windowsHide: true },
            rootFullPath,
          ),
          1,
        )
        await new Promise((resolve: (value: number) => void): void => {
          setImmediate((): void => {
            resolve(0)
          })
        })
        assert.equal(unhandled.length, 0)
      } finally {
        process.off('unhandledRejection', listener)
        rmSync(rootFullPath, { recursive: true, force: true })
      }
    })
  }
}
