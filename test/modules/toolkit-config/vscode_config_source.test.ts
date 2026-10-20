import { test } from 'vitest'
import assert from 'node:assert/strict'
import { VsCodeConfigSource } from '../../../src/frontend/plugin/VsCodeConfigSource.ts'
import type { VsCodeWorkspaceConfiguration } from '../../../src/frontend/plugin/VsCodeConfigSource.ts'

/** @anchor config-vscode-source */
export class VsCodeConfigSourceTest {
  private constructor() {}

  static {
    test('已配置的 mcp.port 经校验返回原值', async (): Promise<void> => {
      const source: VsCodeConfigSource = new VsCodeConfigSource(
        new MemoryConfiguration({ 'toolkit.mcp.port': 5060 }),
      )
      assert.equal((await source.read()).mcpPort, 5060)
    })

    test('类型错误的条目读取直接失败', (): void => {
      const source: VsCodeConfigSource = new VsCodeConfigSource(
        new MemoryConfiguration({ 'toolkit.mcp.port': 'not-a-number' }),
      )
      assert.throws((): Promise<{ readonly mcpPort: number }> => source.read())
    })
    test('端口整数边界与每次读取的当前配置', async (): Promise<void> => {
      let port: unknown = 1
      const source: VsCodeConfigSource = new VsCodeConfigSource({ get: (): unknown => port })
      assert.equal((await source.read()).mcpPort, 1)
      port = 65535
      assert.equal((await source.read()).mcpPort, 65535)
      for (const invalid of [undefined, null, 0, 65536, 1.5, '5050', NaN, Infinity]) {
        port = invalid
        assert.throws((): Promise<{ readonly mcpPort: number }> => source.read())
      }
    })
  }
}

/** @anchor config-vscode-source */
class MemoryConfiguration implements VsCodeWorkspaceConfiguration {
  /** @anchor config-vscode-source */
  public constructor(private readonly values: Readonly<Record<string, unknown>>) {}

  /** @anchor config-vscode-source */
  public get(section: string): unknown {
    return this.values[section]
  }
}
