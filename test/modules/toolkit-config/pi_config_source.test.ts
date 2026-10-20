import { test } from 'vitest'
import assert from 'node:assert/strict'
import { PiConfigSource } from '../../../src/toolkit/config/PiConfigSource.ts'
import type { ToolkitConfig } from '../../../src/toolkit/config/ConfigSource.ts'

/** @anchor config-pi-source-placeholder */
export class PiConfigSourceTest {
  private constructor() {}

  static {
    test('Pi 占位来源读取抛未实现错误', async (): Promise<void> => {
      const source: PiConfigSource = PiConfigSource.create()
      await assert.rejects(
        (): Promise<ToolkitConfig> => source.read(),
        (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
      )
    })
  }
}
