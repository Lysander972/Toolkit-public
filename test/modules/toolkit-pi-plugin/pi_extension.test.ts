import { test } from 'vitest'
import assert from 'node:assert/strict'
import piExtension from '../../../src/toolkit/pi_plugin/PiExtension.ts'

/**
 * @anchor pi-host-bootstrap
 * @anchor pi-extension-placeholder
 */
export class PiExtensionTest {
  private constructor() {}

  static {
    test('官方扩展占位明确失败，不创建可用会话', (): void => {
      assert.throws(
        (): unknown => Reflect.apply(piExtension, null, []),
        (error: unknown): boolean =>
          error instanceof Error && error.message === 'TODO: Pi 官方扩展 flags、事件与会话资源登记',
      )
    })
  }
}
