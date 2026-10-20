import { spawnSync } from 'node:child_process'
import { test } from 'vitest'
import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, writeFileSync, rmSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import type { SpawnSyncReturns } from 'node:child_process'
import { GitFixture } from '../../../tools/testing/GitFixture.ts'

/** @anchor cli-boundary */
export class CliEntrypointTest {
  private constructor() {}
  static {
    test('正式入口执行消息验证，旧 review 命令已退出', (): void => {
      mkdirSync(resolve('_build/tests'), { recursive: true })
      const rootFullPath: string = mkdtempSync(resolve('_build/tests/cli-'))
      try {
        GitFixture.create(rootFullPath)
        const run: (...args: string[]) => SpawnSyncReturns<string> = (
          ...args: string[]
        ): SpawnSyncReturns<string> =>
          spawnSync(process.execPath, ['--import=tsx', resolve('tools/cli/main.ts'), ...args], {
            cwd: rootFullPath,
            encoding: 'utf8',
            windowsHide: true,
          })
        writeFileSync(join(rootFullPath, 'message'), 'fix(cli): validate command\n')
        assert.equal(run('message', 'message').status, 0)
        writeFileSync(join(rootFullPath, 'message'), 'x'.repeat(73) + '\n')
        const rejected: SpawnSyncReturns<string> = run('message', 'message')
        assert.equal(rejected.status, 1)
        assert.ok(rejected.stderr.includes('提交标题须非空'))
        assert.equal(run('review', 'verify').status, 1)
        assert.equal(run('unknown').status, 1)
        assert.equal(run('check', '--unknown').status, 1)
      } finally {
        rmSync(rootFullPath, { recursive: true })
      }
    })

    test('正式入口保留嵌套异常原因和非 Error 拒绝值', (): void => {
      const failures: readonly string[] = [
        'new Error("外层失败", { cause: new TypeError("底层失败") })',
        '"非 Error 拒绝值"',
      ]
      for (const failure of failures) {
        const result: SpawnSyncReturns<string> = spawnSync(
          process.execPath,
          [
            '--import=tsx',
            '--input-type=module',
            '-e',
            'const { Cli } = await import(process.argv[1]); ' +
              'Cli.run = async () => { throw ' +
              failure +
              ' }; await import(process.argv[2]);',
            pathToFileURL(resolve('tools/cli/Cli.ts')).href,
            pathToFileURL(resolve('tools/cli/main.ts')).href,
          ],
          { encoding: 'utf8', windowsHide: true },
        )
        assert.equal(result.status, 1, result.stdout + result.stderr)
        if (failure === failures[0]) {
          assert.ok(result.stderr.includes('Error: 外层失败'), result.stderr)
          assert.ok(result.stderr.includes('TypeError: 底层失败'), result.stderr)
          assert.ok(result.stderr.includes('[cause]'), result.stderr)
          assert.ok(result.stderr.includes('Cli.run'), result.stderr)
        } else assert.ok(result.stderr.includes('非 Error 拒绝值'), result.stderr)
      }
    })
  }
}
