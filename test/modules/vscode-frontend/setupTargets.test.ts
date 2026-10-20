import assert from 'node:assert/strict'
import { test } from 'vitest'
import { SetupTargets } from '../../../src/frontend/host_client/SetupTargets.ts'
import type { SetupTarget } from '../../../src/frontend/host_client/SetupTargets.ts'
import { ConfigurationEditor } from '../../../src/frontend/host_client/ConfigurationEditor.ts'
import type { ConfigurationPlan } from '../../../src/frontend/host_client/ConfigurationEditor.ts'
import { Option } from 'effect'
import { parse as parseToml } from 'smol-toml'
import type { SetupTargetId } from '../../../src/frontend/host_client/SetupTargets.ts'
import { z } from 'zod'

/** @anchor frontend-setup-targets */
export class SetupTargetsTest {
  private constructor() {}

  static {
    test('所有既有目标生成可重复的基础 MCP 配置', (): void => {
      const endpoint: string = 'http://127.0.0.1:5050/mcp'
      const targets: readonly SetupTarget[] = SetupTargets.forEndpoint(endpoint)
      assert.deepEqual(
        targets.map((target: SetupTarget): string => target.id),
        [
          'claudeCode',
          'codex',
          'gemini',
          'openCode',
          'trae',
          'cursor',
          'factoryDroid',
          'kimiCode',
          'cline',
          'kiloCode',
          'crush',
        ],
      )
      for (const target of targets) {
        assert.equal(target.fullReady(), false)
        const first: ConfigurationPlan = ConfigurationEditor.plan(
          target.configRelativePath,
          target.format,
          Option.none(),
          target.fields,
        )
        const expected: Readonly<Record<SetupTargetId, readonly [readonly string[], unknown]>> = {
          claudeCode: [['mcpServers', 'mechtoolkit'], { type: 'http', url: endpoint }],
          codex: [['mcp_servers', 'mechtoolkit'], { url: endpoint }],
          gemini: [['mcpServers', 'mechtoolkit'], { httpUrl: endpoint }],
          openCode: [['mcp', 'servers', 'mechtoolkit'], { type: 'remote', url: endpoint }],
          trae: [['mcpServers', 'mechtoolkit'], { type: 'http', url: endpoint }],
          cursor: [['mcpServers', 'mechtoolkit'], { url: endpoint }],
          factoryDroid: [['mcpServers', 'mechtoolkit'], { type: 'http', url: endpoint }],
          kimiCode: [['mcpServers', 'mechtoolkit'], { url: endpoint }],
          cline: [
            ['mcpServers', 'mechtoolkit'],
            { type: 'streamableHttp', url: endpoint, disabled: false },
          ],
          kiloCode: [['mcp', 'mechtoolkit'], { type: 'remote', url: endpoint }],
          crush: [['mcp', 'mechtoolkit'], { type: 'http', url: endpoint }],
        }
        const parsed: unknown =
          target.format === 'jsonc' ? JSON.parse(first.next) : parseToml(first.next)
        assert.deepEqual(
          SetupTargetsTest.at(parsed, expected[target.id][0]),
          expected[target.id][1],
        )
        assert.equal(
          ConfigurationEditor.plan(
            target.configRelativePath,
            target.format,
            Option.some(first.next),
            target.fields,
          ).changed,
          false,
        )
      }
    })
  }

  /** @anchor frontend-setup-targets */
  private static at(value: unknown, segments: readonly string[]): unknown {
    let current: unknown = value
    for (const segment of segments) {
      const record: Readonly<Record<string, unknown>> = z
        .record(z.string(), z.unknown())
        .parse(current)
      if (!(segment in record)) throw new Error('配置目标缺少契约字段 ' + segment)
      current = record[segment]
    }
    return current
  }
}
