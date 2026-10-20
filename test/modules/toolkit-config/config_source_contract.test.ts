import { test } from 'vitest'
import assert from 'node:assert/strict'
import { VsCodeConfigSource } from '../../../src/frontend/plugin/VsCodeConfigSource.ts'
import { PiConfigSource } from '../../../src/toolkit/config/PiConfigSource.ts'
import type { ConfigSource, ToolkitConfig } from '../../../src/toolkit/config/ConfigSource.ts'

/**
 * @anchor config-source-readonly
 * @anchor config-schema-convergence
 */
export class ConfigSourceContractTest {
  private constructor() {}

  static {
    test('两种实现满足同一只读合同并可互换装配', async (): Promise<void> => {
      const values: Map<string, number> = new Map([['toolkit.mcp.port', 5050]])
      const configured: VsCodeConfigSource = new VsCodeConfigSource({
        get: (section: string): unknown => values.get(section),
      })
      const placeholder: PiConfigSource = PiConfigSource.create()
      const sources: readonly [ConfigSource, ConfigSource] = [configured, placeholder]
      const first: ToolkitConfig = await sources[0].read()
      assert.deepEqual(first, { mcpPort: 5050 })
      values.set('toolkit.mcp.port', 5060)
      assert.deepEqual(await sources[0].read(), { mcpPort: 5060 })
      assert.deepEqual(first, { mcpPort: 5050 })
      await assert.rejects(
        (): Promise<ToolkitConfig> => sources[1].read(),
        (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
      )
    })
  }
}
