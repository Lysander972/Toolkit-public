import { test, vi } from 'vitest'
import assert from 'node:assert/strict'
import { Option } from 'effect'
import { CodebaseConfiguration } from '../../../tools/lint/style/eslint.config.ts'
import { StructuralRules } from '../../../tools/lint/style/StructuralRules.ts'
import { ESLint } from 'eslint'
import type { Linter } from 'eslint'

/** @anchor selfcheck-gate */
export class RulesTest {
  private constructor() {}

  private static async rules(this: void): Promise<void> {
    const eslint: ESLint = new ESLint({
      overrideConfigFile: true,
      overrideConfig: [
        ...CodebaseConfiguration.create(['tools/**/*.ts']),
        {
          languageOptions: {
            parserOptions: { disallowAutomaticSingleRunInference: true },
          },
        },
      ],
    })
    for (const source of [
      'export class Sample { private constructor() {} public static value(): number { return 1 } }',
      'export interface Sample { value(): number }',
      'export enum Sample { First, Second }',
      'export class Sample { private count: number = 0; public increment(): number { return ++this.count } }',
      'export class Sample { public readonly value: number = 1 }',
      'export class Sample { private constructor() {} public static values(input: readonly number[]): readonly number[] { return [...input, 1] } }',
      'export class Sample { private constructor() {} public static value(): number { const operation: () => number = (): number => 1; return operation() } }',
      'export class Sample { private constructor() {} public static run(operation: () => void): void { try { operation() } catch { /* 此次尽力释放失败不覆盖原始错误 */ } } }',
      'export class Sample { private constructor() {} public static run(operation: () => void): void { try { operation() } catch {\n// 此次尽力释放失败不覆盖原始错误\n} } }',
      "type Mode = 'build' | 'test'\nexport class Sample { private constructor() {} public read(input: Mode): Mode { return input } }",
      'export type Level = 1 | 2 | 3',
    ]) {
      const results: ESLint.LintResult[] = await eslint.lintText(source, {
        filePath: 'tools/cli/Cli.ts',
      })
      assert.equal(results[0]?.errorCount, 0, JSON.stringify(results[0]?.messages))
    }
    for (const [source, expected] of [
      [
        "export class Sample { public value(input: string): string { return input || '' } }",
        '@typescript-eslint/strict-boolean-expressions',
      ],
      [
        'export class Sample { public value(input: object): object { return input || {} } }',
        '@typescript-eslint/no-unnecessary-condition',
      ],
      [
        'export class Sample { public value(input: boolean | undefined): boolean { return input || true } }',
        '@typescript-eslint/strict-boolean-expressions',
      ],

      ['export const sample: number = 1', 'no-restricted-syntax'],
      [
        'export class Sample { public value(): string | undefined { return undefined } }',
        'no-restricted-syntax',
      ],
      ['export function sample(): number { return 1 }', 'no-restricted-syntax'],
      ['export default class Sample {}', 'no-restricted-syntax'],
      [
        'export class Sample { public value(): number { var value: number = 1; return value } }',
        'no-var',
      ],
      [
        'export class Sample { value(): number { return 1 } }',
        '@typescript-eslint/explicit-member-accessibility',
      ],
      [
        'export class Sample { public value() { return 1 } }',
        '@typescript-eslint/explicit-function-return-type',
      ],
      [
        'export class Sample { public value(input): number { return input } }',
        '@typescript-eslint/typedef',
      ],
      [
        'export class Sample { public value(): number { const value = 1; return value } }',
        '@typescript-eslint/typedef',
      ],
      [
        'export class Sample { public hasValue(input: ReadonlyMap<string, string>): boolean { const value = input.get("key"); return value !== undefined } }',
        '@typescript-eslint/typedef',
      ],
      ['/* eslint-disable */ export const sample: number = 1', 'eslint-comments/no-use'],
      ['export class Sample { public value: number = 1 }', 'no-restricted-syntax'],
      ['export class Sample { protected value: number = 1 }', 'no-restricted-syntax'],
      [
        'export class Sample { public constructor(public value: number) {} }',
        'no-restricted-syntax',
      ],
      [
        'export class Sample { private constructor() {} public static value(input: number): number { input += 1; return input } }',
        'no-param-reassign',
      ],
      [
        'export class Sample { private constructor() {} public static value(input: { value: number }): void { input.value = 1 } }',
        'no-param-reassign',
      ],
      [
        'export class Sample { private constructor() {} public static value(input: number[]): number { return input.length } }',
        'no-restricted-syntax',
      ],
      [
        'export class Sample { private constructor() {} public static value(input: Map<string, number>): number { return input.size } }',
        'no-restricted-syntax',
      ],
      [
        'export class Sample { private constructor() {} public static value(input: Set<number>): number { return input.size } }',
        'no-restricted-syntax',
      ],
      [
        'export class Sample { private value: number = 1; public getValue(): number { return this.value } }',
        '@typescript-eslint/prefer-readonly',
      ],
      [
        'export class Sample { private constructor() {} public static value(): void { Promise.resolve(1) } }',
        '@typescript-eslint/no-floating-promises',
      ],
      [
        'export class Sample { private constructor() {} public static value(): boolean { if (Promise.resolve(true)) { return true } return false } }',
        '@typescript-eslint/no-misused-promises',
      ],
      [
        'export class Sample { public isValue(input: string): input is string { return input.length > 0 } }',
        'no-restricted-syntax',
      ],
      [
        'export class Sample { private holder: { value?: number } = {}; public read(): number { return this.holder.value! } }',
        '@typescript-eslint/no-non-null-assertion',
      ],
      [
        'export class Sample { public value(): void { const done: true = true; if (done) { return } } }',
        '@typescript-eslint/no-unnecessary-condition',
      ],
      [
        'export class Sample { public value(input: string): string { return input.substring(0, 2) } }',
        'unicorn/prefer-string-slice',
      ],
      [
        'export class Sample { public value(input: string): number { if (input) { return 1 } return 0 } }',
        '@typescript-eslint/strict-boolean-expressions',
      ],
      [
        'export class Sample { public value(input: string): string { const total = 1 + input; return total } }',
        '@typescript-eslint/restrict-plus-operands',
      ],
      [
        'export class Sample { public value(input: { data: number }): string { return String(input) } }',
        '@typescript-eslint/no-base-to-string',
      ],
      [
        'export class Sample { public value(count: number): string { return `v$' + '{count}` } }',
        '@typescript-eslint/restrict-template-expressions',
      ],
      [
        'export class Sample { private constructor() {} public static value(): number {\n// @ts-expect-error 压制类型错误\nreturn 1 } }',
        '@typescript-eslint/ban-ts-comment',
      ],
    ]) {
      assert.ok(source !== undefined && expected !== undefined)
      const results: ESLint.LintResult[] = await eslint.lintText(source, {
        filePath: 'tools/cli/Cli.ts',
      })
      const matched: boolean = Option.match(Option.fromNullishOr(results[0]), {
        onSome: (result: ESLint.LintResult): boolean =>
          result.messages.some(
            (message: Linter.LintMessage): boolean => message.ruleId === expected,
          ),
        onNone: (): boolean => false,
      })
      assert.ok(matched, JSON.stringify(results[0]?.messages))
    }
    for (const [source, expected] of [
      [
        'export class Sample { public static value(input: ReadonlyMap<string, string>): void { const value = input.get("key"); console.log(value) } }',
        '@typescript-eslint/typedef',
      ],
      [
        'export class Sample { public static value(input): void { console.log(input) } }',
        '@typescript-eslint/typedef',
      ],
      ['export class Sample { private value = 1 }', '@typescript-eslint/typedef'],
      [
        'export class Sample { public static value() { return 1 } }',
        '@typescript-eslint/explicit-function-return-type',
      ],
      [
        'export class Sample { public static value(): string | undefined { return undefined } }',
        'no-restricted-syntax',
      ],
    ]) {
      assert.ok(source !== undefined && expected !== undefined)
      const results: ESLint.LintResult[] = await eslint.lintText(source, {
        filePath: 'test/modules/tools/style_rules.test.ts',
      })
      assert.ok(
        results[0]?.messages.some(
          (message: Linter.LintMessage): boolean => message.ruleId === expected,
        ) ?? false,
        JSON.stringify(results[0]?.messages),
      )
    }
  }

  private static testQuality(this: void): void {
    assert.equal(
      StructuralRules.testQuality(
        "import { readFileSync } from 'node:fs'; export class Sample { public test(): void { const source = readFileSync('src/entry.ts', 'utf8'); assert.ok(source.includes('marker')) } }",
        'sample.ts',
      ).length,
      1,
    )
    assert.deepEqual(
      StructuralRules.testQuality(
        'export class Sample { public test(): void { assert.deepEqual(parse(result.next), { marker: true }) } }',
        'sample.ts',
      ),
      [],
    )
    for (const source of [
      "import { readFileSync as read } from 'node:fs'; const source = read('src/entry.ts', 'utf8'); const alias = source; assert.equal(alias.indexOf('marker'), 0)",
      "import { readFile } from 'node:fs/promises'; const source = await readFile('src/entry.tsx', 'utf8'); assert.ok(source.includes('marker'))",
      'assert.equal(actual.value, actual.value)',
      'assert.deepEqual(parse(input), parse(input))',
      "import { readFileSync } from 'node:fs'; const source = readFileSync('src/entry.ts', 'utf8'); expect(source).toContain('marker')",
      "import { readFileSync } from 'node:fs'; const source = readFileSync('src/entry.ts', 'utf8'); expect(source.includes('marker')).not.toBe(false)",
      'expect(actual.value).toStrictEqual(actual.value)',
    ])
      assert.equal(StructuralRules.testQuality(source, 'sample.ts').length, 1, source)
    for (const source of [
      "import { readFileSync } from 'node:fs'; assert.equal(readFileSync('src/entry.ts', 'utf8'), original)",
      "import { readFileSync } from 'node:fs'; const source = readFileSync('input.xml', 'utf8'); assert.ok(source.includes('<canvas>'))",
      "assert.ok(result.next.includes('marker'))",
      'assert.deepEqual(parse(result.next), { marker: true })',
      'expect(parse(result.next)).toEqual({ marker: true })',
    ])
      assert.deepEqual(StructuralRules.testQuality(source, 'sample.ts'), [])
  }

  static {
    test('ast-grep 区分代码与文本并拒绝结构坏味道', RulesTest.structural)
    test('Java 风格规则允许合法声明并拒绝各项违例', async (): Promise<void> => {
      vi.stubEnv('CI', 'true')
      try {
        await RulesTest.rules()
      } finally {
        vi.unstubAllEnvs()
      }
    })
    test('测试质量门禁拒绝源码文本断言并允许结构化结果', RulesTest.testQuality)
  }

  private static diagnostics(source: string, relativePath: string): string[] {
    return StructuralRules.check(source, relativePath)
  }

  private static structural(this: void): void {
    for (const [source, expected] of [
      ['const value: string | Buffer = read()', 'TK023'],
      ['const value: Array<string | number> = read()', 'TK023'],
      ['const cached: Options | null = resolve()', 'TK023'],
      ["const mode: 'build' | null = parse()", 'TK023'],
      ['const value: { id: number } = find()', 'TK026'],
      ['const value: Array<{ id: number }> = findAll()', 'TK026'],
      ['const schema: z.ZodObject<{ id: z.ZodString }> = build()', 'TK026'],
      ['interface Nested { readonly head: { ref: string } }', 'TK026'],
      ['export class PublicOnly { public value(): number { return 1 } }', 'TK027'],
      [
        "export class OnlyStatic { private static readonly token: string = 'x' public value(): number { return 1 } }",
        'TK027',
      ],
      ['const value = JSON.parse(text)', 'TK028'],
      ['function read(): unknown { return JSON.parse(text) }', 'TK028'],
      ['const schema = z.object({ id: z.string() })', 'TK029'],
      [
        'function build(): unknown { return z.strictObject({ id: z.string() }).parse(input) }',
        'TK029',
      ],
      ['export class Sample { public f(x: Input): Output { return this.g(x) } }', 'TK030'],
      [
        'export class Sample { public f(x: Input): Promise<Output> { return await this.g(x) } }',
        'TK030',
      ],
      [
        'export class Sample { public write(message: string): void { this.output.append(message) } }',
        'TK030',
      ],
      ['const value: Record<string, unknown> = read()', 'TK032'],
      ['const value: Map<string, object> = read()', 'TK032'],
      ['const value: Record<string, Record<string, string>> = read()', 'TK032'],
      ['interface Index { readonly [key: string]: unknown }', 'TK032'],
      ['type Sample = { id: number }', 'TK033'],
      ['type Sample = (input: string) => number', 'TK033'],
      ['type Sample = string | number', 'TK033'],
      ["type Sample = 'a' | 1", 'TK033'],
      ['type Sample = ReadonlyArray<string>', 'TK033'],
      ["const mode: 'build' | 'test' = parse()", 'TK034'],
      ["function pick(input: 'build' | 'test'): void {}", 'TK034'],
      ["function mode(): 'build' | 'test' { return 'build' }", 'TK034'],
      ['const pattern = /ab+c/', 'TK035'],
      ['const matched: boolean = /ab+c/.test(input)', 'TK035'],
      ["const pattern = new RegExp('ab+c')", 'TK036'],
      ["const pattern = RegExp('ab+c')", 'TK036'],
    ]) {
      assert.ok(source !== undefined && expected !== undefined)
      assert.ok(
        RulesTest.diagnostics(source, 'sample.ts').some((message: string): boolean =>
          message.includes(expected),
        ),
        expected + ': ' + source,
      )
      assert.deepEqual(
        RulesTest.diagnostics('const text = ' + JSON.stringify(source), 'sample.ts'),
        [],
      )
    }
    assert.deepEqual(
      RulesTest.diagnostics('const value = options.timeout ?? Defaults.timeout', 'sample.ts'),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics('const value = first ?? second ?? third', 'sample.ts'),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'export class Sample {\nprivate constructor() {}\n/**\n * @format-exempt sample-exempt\n */\npublic valid(input: string): boolean { return /a+/.test(input) }\n}',
        'sample.ts',
      ),
      [],
    )
    assert.ok(
      RulesTest.diagnostics(
        'export class Sample {\npublic a(input: string): boolean { return /a+/.test(input) }\n/**\n * @format-exempt sample-exempt\n */\npublic b(input: string): boolean { return /b+/.test(input) }\n}',
        'sample.ts',
      ).some((message: string): boolean => message.includes('TK035')),
    )
    assert.deepEqual(RulesTest.diagnostics('const value = String(input)', 'sample.ts'), [])
    assert.deepEqual(
      RulesTest.diagnostics(
        "type Mode = 'build' | 'test'\nconst mode: Mode = 'build'",
        'sample.ts',
      ),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        "type Mode = 'build' | 'test'\nfunction pick(input: Mode): void {}",
        'sample.ts',
      ),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics('function pick(input: number | null): void {}', 'sample.ts'),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics('const value = schema.parse(JSON.parse(text))', 'sample.ts'),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'const schema: z.ZodType<Input> = z.object({ id: z.string() })',
        'sample.ts',
      ),
      [],
    )
    assert.deepEqual(RulesTest.diagnostics('if (value === undefined) { return }', 'sample.ts'), [])
    assert.deepEqual(
      RulesTest.diagnostics(
        'interface Range { readonly start: number; readonly end: number }\nconst value: Range = pick()',
        'sample.ts',
      ),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics('const pair: [string, number] = split()', 'sample.ts'),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics('const score: (input: string) => number = grade()', 'sample.ts'),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'export class Sealed { private constructor() {} public static value(): number { return 1 } }',
        'sample.ts',
      ),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics('try { work() } catch (error) { return { error } }', 'sample.ts'),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'try { work() } catch (error) { log("return ", error); throw error }',
        'sample.ts',
      ),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'interface Contract { f(x: Input): Output }\nexport class Sample implements Contract { private constructor() {} public f(x: Input): Output { return this.g(x) } }',
        'sample.ts',
      ),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'class Base { private constructor() {} public f(x: Input): Output { return this.g(wrap(x)) } }\nexport class Sample extends Base { private constructor() { super() } public override f(x: Input): Output { return this.g(x) } }',
        'sample.ts',
      ),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'export class Sample { private constructor() {} public f(x: Input): Output { return this.g(wrap(x)) } }',
        'sample.ts',
      ),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'export class Sample { private constructor() {} public f(x: Input): readonly Output[] { return this.g(x).map(scale) } }',
        'sample.ts',
      ),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'export class Sample {\nprivate constructor() {}\nprivate steps(): number {\nconst first: number = 1\nconst second: number = 2\nconst third: number = 3\nconst fourth: number = 4\nreturn first + second + third + fourth\n}\npublic value(): number { return this.steps() + 1 }\n}',
        'sample.ts',
      ),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'export class Sample {\nprivate constructor() {}\n/**\n * @format-exempt sample-exempt\n */\npublic f(x: Input): Output { return this.g(x) }\n}',
        'sample.ts',
      ),
      [
        'sample.ts:3: TK030 纯转发壳必须消除：零加工转发没有承载行为；确需保留时使用方法级 @passthrough-exempt 理由标签。',
      ],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'export class Sample {\nprivate constructor() {}\n/**\n * @passthrough-exempt 外部契约转发\n */\npublic f(x: Input): Output { return this.g(x) }\n}',
        'sample.ts',
      ),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics('const value: Map<string, number> = read()', 'sample.ts'),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics("const value: Record<'a' | 'b', number> = read()", 'sample.ts'),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics('const value: Record<string, Device> = read()', 'sample.ts'),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics('interface Index { readonly [key: string]: number }', 'sample.ts'),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'export class Sample {\nprivate constructor() {}\npublic f(x: Input): Output { return (this.g(x)) }\n}',
        'sample.ts',
      ),
      [
        'sample.ts:3: TK030 纯转发壳必须消除：零加工转发没有承载行为；确需保留时使用方法级 @passthrough-exempt 理由标签。',
      ],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'export class Sample {\nprivate constructor() {}\nprivate pass(x: Input): Output { return this.g(x) }\npublic run(x: Input): Output { return this.pass(x) }\n}',
        'sample.ts',
      ),
      [
        'sample.ts:3: TK030 纯转发壳必须消除：零加工转发没有承载行为；确需保留时使用方法级 @passthrough-exempt 理由标签。',
        'sample.ts:4: TK030 纯转发壳必须消除：零加工转发没有承载行为；确需保留时使用方法级 @passthrough-exempt 理由标签。',
      ],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'export class Sample {\nprivate constructor() {}\npublic close(): Promise<void> { await this.context.close() }\n}',
        'sample.ts',
      ),
      [
        'sample.ts:3: TK030 纯转发壳必须消除：零加工转发没有承载行为；确需保留时使用方法级 @passthrough-exempt 理由标签。',
      ],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'export class Sample {\nprivate constructor() {}\npublic finish(): void { this.database.exec("DELETE FROM rebase_input;") }\n}',
        'sample.ts',
      ),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'export class Sample {\nprivate constructor() {}\npublic static create(): Sample { return new Sample() }\n}',
        'sample.ts',
      ),
      [],
    )
    assert.deepEqual(
      RulesTest.diagnostics(
        'export class Sample {\nprivate constructor() {}\n/**\n * @passthrough-exempt 子节点保存在本对象的 childNodes\n */\npublic addChild(child: Node): void { this.childNodes.push(child) }\n}',
        'sample.ts',
      ),
      [],
    )
  }
}
