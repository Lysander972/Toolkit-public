import { test } from 'vitest'
import assert from 'node:assert/strict'
import { EnvironmentScan } from '../../../tools/governance/environment.ts'

/** @anchor environment-boundary */
export class EnvironmentTest {
  private constructor() {}

  /** @anchor environment-boundary */
  public static environment(this: void): void {
    const constants: ReadonlyMap<string, string> = new Map([
      ['EnvironmentVariables.testTmpdir', 'TEST_TMPDIR'],
    ])
    for (const source of [
      "process.env['UNKNOWN_SETTING']",
      'process.env.UNKNOWN_SETTING',
      'globalThis.process.env.UNKNOWN_SETTING',
      "import runtime from 'node:process'; runtime.env.UNKNOWN_SETTING",
      "import { env as settings } from 'node:process'; settings.UNKNOWN_SETTING",
      "process.env[/* @non-environment-variable 不能豁免 API */ 'UNKNOWN_SETTING']",
      "import { spawn as launch } from 'node:child_process'; launch('tool', [], { env: { UNKNOWN_SETTING: 'value' } })",
      "import * as child from 'node:child_process'; const environment = { UNKNOWN_SETTING: 'value' }; child.spawn('tool', [], { env: environment })",
      "import { spawn } from 'node:child_process'; const options = { env: { UNKNOWN_SETTING: 'value' } }; spawn('tool', [], options)",
      "import { spawn } from 'node:child_process'; const defaults = { UNKNOWN_SETTING: 'value' }; spawn('tool', [], { env: { ...defaults } })",
      "import { spawn } from 'node:child_process'; const env = { UNKNOWN_SETTING: 'value' }; spawn('tool', [], { env })",
      "import { spawn } from 'node:child_process'; const options = { env: { UNKNOWN_SETTING: 'value' } }; spawn('tool', [], { ...options })",
    ]) {
      const violations: string[] = EnvironmentScan.check(source, 'sample.ts', [], constants)
      assert.equal(violations.length, 1, source)
      assert.match(violations.join('\n'), /sample\.ts:1: 未登记环境变量 UNKNOWN_SETTING/)
    }
    assert.deepEqual(
      EnvironmentScan.check("process.env['PATH']", 'sample.ts', ['PATH'], constants),
      [],
    )
    assert.deepEqual(
      EnvironmentScan.check(
        'function read(process: Input) { return process.env.value }',
        'sample.ts',
        [],
        constants,
      ),
      [],
    )
    assert.deepEqual(
      EnvironmentScan.check(
        'function read(globalThis: Input) { return globalThis.process.env.value }',
        'sample.ts',
        [],
        constants,
      ),
      [],
    )
    assert.deepEqual(
      EnvironmentScan.check(
        "import runtime from 'node:process'; function read(runtime: Input) { return runtime.env.value }",
        'sample.ts',
        [],
        constants,
      ),
      [],
    )
    assert.deepEqual(
      EnvironmentScan.check(
        "import { EnvironmentVariables as Names } from './EnvironmentVariables.ts'; process.env[Names.testTmpdir]",
        'tools/cli/sample.ts',
        ['TEST_TMPDIR'],
        constants,
      ),
      [],
    )
    assert.equal(
      EnvironmentScan.check(
        'function read(EnvironmentVariables: Input) { return process.env[EnvironmentVariables.testTmpdir] }',
        'sample.ts',
        ['TEST_TMPDIR'],
        constants,
      ).length,
      1,
    )
    assert.deepEqual(
      EnvironmentScan.check(
        "const UNKNOWN_SETTING = { 'OBJECT_KEY': 1 }",
        'sample.ts',
        [],
        constants,
      ),
      [],
    )
    assert.deepEqual(
      EnvironmentScan.check(
        "const options = { env: { UNKNOWN_SETTING: 'value' } }",
        'sample.ts',
        [],
        constants,
      ),
      [],
    )
    assert.deepEqual(
      EnvironmentScan.check(
        "const filename = 'CACHE_DIRECTORY/file.ts'",
        'sample.ts',
        [],
        constants,
      ),
      [],
    )
    assert.deepEqual(EnvironmentScan.check("const name = 'PATH'", 'sample.ts', [], constants), [])
    assert.deepEqual(
      EnvironmentScan.check(
        "// @non-environment-variable 协议枚举\nconst value = 'PROTOCOL_KIND'",
        'sample.ts',
        [],
        constants,
      ),
      [],
    )
    assert.deepEqual(
      EnvironmentScan.check(
        "// @non-environment-variable 整个表达式\nconst value = join('PROTOCOL_KIND', 'OTHER_SETTING')",
        'sample.ts',
        [],
        constants,
      ),
      [],
    )
    assert.equal(
      EnvironmentScan.check(
        "// @non-environment-variable 非紧邻\n\nconst value = 'PROTOCOL_KIND'",
        'sample.ts',
        [],
        constants,
      ).length,
      1,
    )
    assert.equal(
      EnvironmentScan.check("const value = 'UNKNOWN_SETTING'", 'sample.ts', [], constants).length,
      1,
    )
    assert.equal(
      EnvironmentScan.check('const settings = process.env', 'sample.ts', [], constants).length,
      1,
    )
    for (const source of [
      "import { spawn } from 'node:child_process'; spawn('tool', [], { env: obtainEnvironment() })",
      "import { spawn } from 'node:child_process'; spawn('tool', [], { env: { ...obtainEnvironment() } })",
    ]) {
      assert.match(
        EnvironmentScan.check(source, 'sample.ts', [], constants).join('\n'),
        /环境变量访问未明确名称/,
      )
    }
    assert.equal(
      EnvironmentScan.suspected('UNKNOWN_SETTING=value', 'sample.txt', [], false, []).length,
      1,
    )
    assert.deepEqual(
      EnvironmentScan.suspected(
        'PROTOCOL_KIND # @non-environment-variable 协议枚举',
        'sample.txt',
        [],
        false,
        [],
      ),
      [],
    )
  }

  static {
    test('环境变量 API、字符串疑似与豁免边界', EnvironmentTest.environment)
  }
}
