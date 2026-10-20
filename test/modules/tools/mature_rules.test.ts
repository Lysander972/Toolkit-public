import { test, afterAll } from 'vitest'
import assert from 'node:assert/strict'
import { ESLint, Linter } from 'eslint'
import tseslint from 'typescript-eslint'
import { CodebaseConfiguration } from '../../../tools/lint/style/eslint.config.ts'

/** @anchor mature-rule-composition */
export class MatureRulesTest {
  private constructor() {}

  private static diagnostics(source: string, relativePath: string): Linter.LintMessage[] {
    const messages: Linter.LintMessage[] = new Linter().verify(
      source,
      [
        ...CodebaseConfiguration.create(['tools/**/*.{ts,tsx}']),
        tseslint.configs.disableTypeChecked,
      ],
      relativePath,
    )
    assert.ok(!messages.some((message: Linter.LintMessage): boolean => message.fatal === true))
    return messages
  }

  /**
   * @anchor mature-rule-replacements
   * @anchor fallback-diagnostics
   */
  private static replacements(this: void): void {
    const rejected: readonly [string, string][] = [
      ['const value = input!', '@typescript-eslint/no-non-null-assertion'],
      ['const value: string | undefined = find()', 'no-restricted-syntax'],
      ['try { work() } catch {}', 'no-empty'],
      ['Reflect.get(input, key); Reflect["get"](input, key)', 'no-restricted-globals'],
      ['input.trim(); input["toUpperCase"]()', 'no-restricted-properties'],
      ['const result = { ...(input ?? {}) }', 'unicorn/no-useless-fallback-in-spread'],
      ['const result = new Set(input ?? [])', 'unicorn/no-useless-collection-argument'],
      ['try { work() } catch (error) { throw error }', 'no-useless-catch'],
      ['try { work() } catch { throw new Error("failed") }', 'preserve-caught-error'],
      [
        'try { work() } catch ({ message }) { throw new Error(message, { cause: other }) }',
        'preserve-caught-error',
      ],
    ]
    for (const [source, ruleId] of rejected) {
      assert.ok(
        MatureRulesTest.diagnostics(source, 'tools/sample.ts').some(
          (message: Linter.LintMessage): boolean => message.ruleId === ruleId,
        ),
        ruleId + ': ' + source,
      )
      assert.ok(
        !MatureRulesTest.diagnostics(
          'export class Sample { private constructor() {} public static read(): string { return ' +
            JSON.stringify(source) +
            ' } }',
          'tools/sample.ts',
        ).some((message: Linter.LintMessage): boolean => message.ruleId === ruleId),
        ruleId + ': text: ' + source,
      )
    }
    const allowed: readonly [string, string][] = [
      ['try { work() } catch { /* 释放失败不覆盖原始错误 */ }', 'no-empty'],
      ['const result = [...(input ?? [])]', 'unicorn/no-useless-fallback-in-spread'],
      [
        'try { work() } catch (error) { throw new Error("failed", { cause: error }) }',
        'preserve-caught-error',
      ],
    ]
    for (const [source, ruleId] of allowed) {
      assert.ok(
        !MatureRulesTest.diagnostics(source, 'tools/sample.ts').some(
          (message: Linter.LintMessage): boolean => message.ruleId === ruleId,
        ),
        ruleId + ': ' + source,
      )
    }
  }

  /** @anchor mature-sonar-rules */
  private static sonar(this: void): void {
    const rejected: readonly [string, string][] = [
      ['try { first(); second() } catch (error) { return false }', 'sonarjs/no-ignored-exceptions'],
      ['function run() { let value = work(); value = 2; return value }', 'sonarjs/no-dead-store'],
      ['if (ready) { work() } else { work() }', 'sonarjs/no-all-duplicated-branches'],
      [
        'function first() {\nprepare()\nwork()\nreturn finish()\n}\nfunction second() {\nprepare()\nwork()\nreturn finish()\n}',
        'sonarjs/no-identical-functions',
      ],
    ]
    for (const [source, ruleId] of rejected) {
      assert.ok(
        MatureRulesTest.diagnostics(source, 'tools/sample.ts').some(
          (message: Linter.LintMessage): boolean => message.ruleId === ruleId,
        ),
        ruleId + ': ' + source,
      )
    }
    const catchMessages: readonly (string | null)[] = MatureRulesTest.diagnostics(
      'try { first(); second() } catch (error) {}',
      'tools/sample.ts',
    )
      .filter(
        (message: Linter.LintMessage): boolean =>
          message.ruleId === 'no-empty' || message.ruleId === 'sonarjs/no-ignored-exceptions',
      )
      .map((message: Linter.LintMessage): string | null => message.ruleId)
    assert.deepEqual(catchMessages, ['sonarjs/no-ignored-exceptions', 'no-empty'])
  }

  /** @anchor mature-rule-composition */
  private static async exemptions(this: void): Promise<void> {
    const eslint: ESLint = new ESLint({
      overrideConfigFile: true,
      overrideConfig: [
        ...CodebaseConfiguration.create(['tools/**/*.{ts,tsx}']),
        tseslint.configs.disableTypeChecked,
      ],
    })
    const exemptSource: string =
      'class Sample {\n// eslint-disable-next-line no-restricted-properties -- 外部输入边界需要去除协议定界空白。\npublic read(input: string): string { return input.trim().toLowerCase() }\n}'
    const results: ESLint.LintResult[][] = await Promise.all(
      [
        exemptSource,
        'class Sample { public read(input: string): string { return input.trim() } }',
        exemptSource.replace('input.trim().toLowerCase()', 'Reflect.get(input, "value").trim()'),
        exemptSource +
          '\nclass Other { public read(input: string): string { return input.trim() } }',
        exemptSource.replace(' -- 外部输入边界需要去除协议定界空白。', ''),
        exemptSource.replace(
          '// eslint-disable-next-line no-restricted-properties -- 外部输入边界需要去除协议定界空白。',
          '/* eslint-disable no-restricted-properties -- 外部输入边界需要去除协议定界空白。 */',
        ),
        exemptSource.replace('no-restricted-properties --', 'no-restricted-globals --'),
      ].map((source: string): Promise<ESLint.LintResult[]> =>
        eslint.lintText(source, { filePath: 'tools/sample.ts' }),
      ),
    )
    assert.deepEqual(
      results.map((result: readonly ESLint.LintResult[]): readonly (string | null)[] =>
        result
          .flatMap((entry: ESLint.LintResult): Linter.LintMessage[] => entry.messages)
          .filter(
            (message: Linter.LintMessage): boolean =>
              message.ruleId === 'no-restricted-properties' ||
              message.ruleId === 'no-restricted-globals' ||
              message.ruleId?.startsWith('eslint-comments/') === true,
          )
          .map((message: Linter.LintMessage): string | null => message.ruleId),
      ),
      [
        [],
        ['no-restricted-properties'],
        ['no-restricted-globals'],
        ['no-restricted-properties'],
        ['eslint-comments/require-description'],
        ['eslint-comments/no-use'],
        [
          'eslint-comments/no-restricted-disable',
          'no-restricted-properties',
          'no-restricted-properties',
        ],
      ],
    )
  }

  static {
    test('成熟规则替换坏味道并隔离代码文本', MatureRulesTest.replacements)
    test('SonarJS 规则保留原生重叠诊断', MatureRulesTest.sonar)
    test('格式豁免只影响合法声明范围', MatureRulesTest.exemptions)
    afterAll((): void => {
      process.stdout.write(
        '成熟规则内存样例：夹具实际写入 0 文件，累计 0 字节，最大文件 0 字节。\n',
      )
    })
  }
}
