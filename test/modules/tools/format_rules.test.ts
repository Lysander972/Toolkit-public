import { test } from 'vitest'
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import type { SpawnSyncReturns } from 'node:child_process'

/** @anchor typescript-formatting */
export class FormatTest {
  private constructor() {}
  static {
    test('Prettier 官方命令拒绝未格式化输入且检查不改写文件', (): void => {
      mkdirSync(resolve('_build/tests'), { recursive: true })
      const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-format-'))
      try {
        const sourceFullPath: string = join(rootFullPath, 'sample.ts')
        const run: () => SpawnSyncReturns<string> = (): SpawnSyncReturns<string> =>
          spawnSync(
            process.execPath,
            [
              fileURLToPath(import.meta.resolve('prettier/bin/prettier.cjs')),
              '--check',
              '--ignore-path',
              join(rootFullPath, '.prettierignore'),
              '--config',
              join(process.cwd(), 'package.json'),
              sourceFullPath,
            ],
            { encoding: 'utf8', windowsHide: true },
          )
        writeFileSync(join(rootFullPath, '.prettierignore'), '')
        writeFileSync(sourceFullPath, 'export class Example {}\n')
        const valid: SpawnSyncReturns<string> = run()
        assert.equal(valid.status, 0, valid.stdout + valid.stderr)
        writeFileSync(sourceFullPath, 'export class Example{ }')
        assert.equal(run().status, 1)
        assert.equal(readFileSync(sourceFullPath, 'utf8'), 'export class Example{ }')
      } finally {
        rmSync(rootFullPath, { recursive: true })
      }
    })
  }
}
