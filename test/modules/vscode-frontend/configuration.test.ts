import assert from 'node:assert/strict'
import { test } from 'vitest'
import { Option } from 'effect'
import { parse as parseToml } from 'smol-toml'
import { parseDocument as parseYaml } from 'yaml'
import { ConfigurationEditor } from '../../../src/frontend/host_client/ConfigurationEditor.ts'
import type {
  ConfigurationFormat,
  ConfigurationPlan,
} from '../../../src/frontend/host_client/ConfigurationEditor.ts'

/** @anchor frontend-configuration-editor */
export class ConfigurationEditorTest {
  private constructor() {}

  static {
    test('已有 TOML 管理值保持官方 CLI 的注释与排版', (): void => {
      const original: string =
        '# user comment\n[mcp_servers.mechtoolkit]\nurl="http://localhost:5050/mcp"\n'
      const plan: ConfigurationPlan = ConfigurationEditor.plan(
        'config.toml',
        'toml',
        Option.some(original),
        [{ segments: ['mcp_servers', 'mechtoolkit'], value: { url: 'http://localhost:5050/mcp' } }],
      )
      assert.equal(plan.changed, false)
      assert.equal(plan.next, original)
      const changed: ConfigurationPlan = ConfigurationEditor.plan(
        'config.toml',
        'toml',
        Option.some(original),
        [{ segments: ['mcp_servers', 'mechtoolkit'], value: { url: 'http://localhost:6060/mcp' } }],
      )
      assert.equal(changed.changed, true)
      assert.deepEqual(parseToml(changed.next), {
        mcp_servers: { mechtoolkit: { url: 'http://localhost:6060/mcp' } },
      })
    })
    test.each([
      ['jsonc', '{"unmanaged":42}', ['managed', 'url']],
      ['yaml', 'unmanaged: 42\n', ['managed', 'url']],
      ['toml', 'unmanaged = 42\n', ['managed', 'url']],
      ['xml', '<config><unmanaged>42</unmanaged></config>', ['config', 'managed', 'url']],
    ])(
      '配置 %s 保留其他数据且第二次不改写',
      (format: string, original: string, segments: readonly string[]): void => {
        const configurationFormat: ConfigurationFormat = ConfigurationEditorTest.format(format)
        const first: ConfigurationPlan = ConfigurationEditor.plan(
          'config',
          configurationFormat,
          Option.some(original),
          [{ segments, value: 'http://localhost:5050/mcp' }],
        )
        assert.equal(first.changed, true)
        assert.deepEqual(
          ConfigurationEditorTest.read(format, first.next),
          {
            jsonc: { unmanaged: 42, managed: { url: 'http://localhost:5050/mcp' } },
            yaml: { unmanaged: 42, managed: { url: 'http://localhost:5050/mcp' } },
            toml: { unmanaged: 42, managed: { url: 'http://localhost:5050/mcp' } },
            xml: '<config><unmanaged>42</unmanaged><managed><url>http://localhost:5050/mcp</url></managed></config>',
          }[format],
        )
        const second: ConfigurationPlan = ConfigurationEditor.plan(
          'config',
          configurationFormat,
          Option.some(first.next),
          [{ segments, value: 'http://localhost:5050/mcp' }],
        )
        assert.equal(second.changed, false)
        assert.equal(second.next, first.next)
      },
    )

    test.each([
      ['jsonc', '{"broken":'],
      ['yaml', 'broken: ['],
      ['toml', 'broken = ['],
      ['xml', '<config><broken></config>'],
    ])('损坏的 %s 配置拒绝产生写入计划', (format: string, original: string): void => {
      assert.throws((): ConfigurationPlan =>
        ConfigurationEditor.plan(
          'config',
          ConfigurationEditorTest.format(format),
          Option.some(original),
          [{ segments: ['config', 'managed'], value: 'value' }],
        ),
      )
    })
  }

  /** @anchor frontend-configuration-editor */
  private static format(value: string): ConfigurationFormat {
    switch (value) {
      case 'jsonc':
        return 'jsonc'
      case 'yaml':
        return 'yaml'
      case 'toml':
        return 'toml'
      case 'xml':
        return 'xml'
      default:
        throw new Error('未知测试格式')
    }
  }

  /** @anchor frontend-configuration-editor */
  private static read(format: string, text: string): unknown {
    switch (format) {
      case 'jsonc':
        return JSON.parse(text)
      case 'yaml':
        return parseYaml(text).toJS()
      case 'toml':
        return parseToml(text)
      case 'xml': {
        return text
      }
    }
  }
}
