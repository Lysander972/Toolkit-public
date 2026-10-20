import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { Option } from 'effect'
import { test } from 'vitest'
import { TestMetrics } from '../../../tools/cli/TestMetrics.ts'
import { Git } from '../../../tools/cli/Git.ts'
import type { FileResult } from 'mutation-testing-report-schema'

/** @anchor phase-three-metrics */
export class TestMetricsTest {
  private constructor() {}

  static {
    test('官方计数按模块合并，首次 record 建立基线，check 只读并拒绝下降', async (): Promise<void> => {
      mkdirSync(resolve('_build/tests'), { recursive: true })
      const repositoryFullPath: string = mkdtempSync(resolve('_build/tests', 'metrics-'))
      try {
        Git.run(repositoryFullPath, ['init'])
        mkdirSync(join(repositoryFullPath, 'tools/code'), { recursive: true })
        mkdirSync(join(repositoryFullPath, '_build/testing/coverage'), { recursive: true })
        mkdirSync(join(repositoryFullPath, '_build/testing/mutation'), { recursive: true })
        writeFileSync(join(repositoryFullPath, '.gitignore'), '/_build/\n')
        writeFileSync(
          join(repositoryFullPath, 'tools/model.c4'),
          "specification { element system\n element container\n tag tool\n tag doc\n tag todo }\nmodel { app = system 'app' { description '测试模块' metadata { sourcePath 'tools' checks 'selfcheck' } code = container 'code' { #tool description '计算' metadata { sourcePath 'tools/code' files ['tools/code/*.ts'] } } specs = container 'specs' { #doc description '规范' metadata { specPath 'specs' files ['specs/*.md'] } } } }",
        )
        const files: readonly string[] = ['tools/code/first.ts', 'tools/code/second.ts']
        const source: string = 'export const value = 1\n'
        const coverage: Record<string, unknown> = {}
        const mutants: Record<string, FileResult> = {}
        for (const [index, file] of files.entries()) {
          writeFileSync(join(repositoryFullPath, file), source)
          const counter: { total: number; covered: number; skipped: number; pct: number } =
            index === 0
              ? { total: 1, covered: 1, skipped: 0, pct: 100 }
              : { total: 9, covered: 0, skipped: 0, pct: 0 }
          coverage[join(repositoryFullPath, 'producer', file)] = {
            lines: counter,
            statements: counter,
            functions: counter,
            branches: counter,
          }
          mutants[file] = {
            language: 'typescript',
            source,
            mutants: [
              {
                id: String(index),
                mutatorName: 'NumberLiteral',
                location: { start: { line: 1, column: 22 }, end: { line: 1, column: 23 } },
                status: index === 0 ? 'Killed' : 'Survived',
              },
            ],
          }
        }
        writeFileSync(
          join(repositoryFullPath, '_build/testing/coverage/coverage-summary.json'),
          JSON.stringify(coverage),
        )
        writeFileSync(
          join(repositoryFullPath, '_build/testing/mutation/mutation.json'),
          JSON.stringify({
            schemaVersion: '1',
            projectRoot: join(repositoryFullPath, 'producer'),
            thresholds: { high: 80, low: 60 },
            config: { mutate: files },
            files: mutants,
          }),
        )
        const actual: Awaited<ReturnType<typeof TestMetrics.measure>> =
          await TestMetrics.measure(repositoryFullPath)
        const module: NonNullable<(typeof actual.modules)[string]> = Option.getOrThrow(
          Option.fromNullishOr(actual.modules['app.code']),
        )
        assert.deepEqual(Option.getOrThrow(module.coverage), {
          lines: 10,
          statements: 10,
          functions: 10,
          branches: 10,
        })
        assert.equal(Option.getOrThrow(module.mutation), 50)
        writeFileSync(
          join(repositoryFullPath, '_build/testing/mutation/mutation.json'),
          JSON.stringify({
            schemaVersion: '1',
            projectRoot: join(repositoryFullPath, 'producer'),
            thresholds: { high: 80, low: 60 },
            config: { mutate: files },
            files: Object.fromEntries(
              Object.entries(mutants).map(
                ([file, result]: [string, FileResult]): [string, FileResult] => [
                  file,
                  { ...result, mutants: [] },
                ],
              ),
            ),
          }),
        )
        const withoutMutants: typeof actual = await TestMetrics.measure(repositoryFullPath)
        assert.equal(
          Option.isNone(
            Option.getOrThrow(Option.fromNullishOr(withoutMutants.modules['app.code'])).mutation,
          ),
          true,
        )
        assert.equal(TestMetrics.compare(withoutMutants, actual).length, 1)
        writeFileSync(
          join(repositoryFullPath, '_build/testing/mutation/mutation.json'),
          JSON.stringify({
            schemaVersion: '1',
            projectRoot: join(repositoryFullPath, 'producer'),
            thresholds: { high: 80, low: 60 },
            config: { mutate: files },
            files: {},
          }),
        )
        await assert.rejects(
          TestMetrics.measure(repositoryFullPath),
          /变异报告缺少源码: tools\/code\/first.ts/,
        )
        writeFileSync(
          join(repositoryFullPath, '_build/testing/mutation/mutation.json'),
          JSON.stringify({
            schemaVersion: '1',
            projectRoot: join(repositoryFullPath, 'producer'),
            thresholds: { high: 80, low: 60 },
            config: { mutate: files },
            files: mutants,
          }),
        )
        assert.equal(await TestMetrics.run(repositoryFullPath, ['record']), 0)
        const baseline: string = readFileSync(
          join(repositoryFullPath, TestMetrics.baselineRelativePath),
          'utf8',
        )
        assert.equal(await TestMetrics.run(repositoryFullPath, ['check']), 0)
        assert.equal(
          readFileSync(join(repositoryFullPath, TestMetrics.baselineRelativePath), 'utf8'),
          baseline,
        )
        const current: typeof actual = {
          ...actual,
          modules: {
            'app.code': {
              coverage: Option.some({ lines: 9, statements: 10, functions: 10, branches: 10 }),
              mutation: Option.some(49),
            },
          },
        }
        assert.equal(TestMetrics.compare(current, actual).length, 2)
        assert.equal(TestMetrics.compare({ ...actual, modules: {} }, actual).length, 1)
        assert.equal(TestMetrics.compare({ ...actual, modules: {} }, actual, true).length, 1)
        assert.deepEqual(
          TestMetrics.compare({ ...actual, modules: {} }, actual, true, new Set(['app.code'])),
          [],
        )
        assert.equal(TestMetrics.compare(actual, { ...actual, modules: {} }).length, 1)
        assert.deepEqual(TestMetrics.compare(actual, { ...actual, modules: {} }, true), [])
        const pending: typeof actual = {
          ...actual,
          modules: { 'app.code': { coverage: Option.none(), mutation: Option.none() } },
        }
        assert.equal(TestMetrics.compare(pending, actual).length, 1)
        assert.equal(TestMetrics.compare(pending, actual, true).length, 1)
        assert.equal(TestMetrics.compare(actual, pending).length, 1)
        assert.deepEqual(TestMetrics.compare(actual, pending, true), [])
        Git.run(repositoryFullPath, ['add', '.gitignore', 'tools'])
        Git.run(repositoryFullPath, [
          '-c',
          'user.name=Metrics Test',
          '-c',
          'user.email=metrics@example.invalid',
          'commit',
          '-m',
          'metrics baseline',
        ])
        const acceptedRevision: string = Git.run(repositoryFullPath, ['rev-parse', 'HEAD'])
        assert.equal(Git.run(repositoryFullPath, ['rev-list', '--count', 'HEAD']), '1')
        assert.equal(
          await TestMetrics.run(repositoryFullPath, ['check', '--base', acceptedRevision]),
          0,
        )
        await assert.rejects(
          TestMetrics.run(repositoryFullPath, ['check', '--base', '0'.repeat(40)]),
        )
        const lowered: Record<string, unknown> = { ...coverage }
        const zero: { total: number; covered: number; skipped: number; pct: number } = {
          total: 1,
          covered: 0,
          skipped: 0,
          pct: 0,
        }
        const firstFile: string = Option.getOrThrow(Option.fromNullishOr(files[0]))
        lowered[join(repositoryFullPath, 'producer', firstFile)] = {
          lines: zero,
          statements: zero,
          functions: zero,
          branches: zero,
        }
        writeFileSync(
          join(repositoryFullPath, '_build/testing/coverage/coverage-summary.json'),
          JSON.stringify(lowered),
        )
        await assert.rejects(TestMetrics.run(repositoryFullPath, ['record']), /lines 0 < 10/)
        assert.equal(
          readFileSync(join(repositoryFullPath, TestMetrics.baselineRelativePath), 'utf8'),
          baseline,
        )
        writeFileSync(
          join(repositoryFullPath, TestMetrics.baselineRelativePath),
          JSON.stringify({
            schemaVersion: 1,
            modules: {
              'app.code': {
                coverage: { lines: 0, statements: 0, functions: 0, branches: 0 },
                mutation: 50,
              },
            },
          }),
        )
        Git.run(repositoryFullPath, ['add', 'tools/test-metrics.json'])
        Git.run(repositoryFullPath, [
          '-c',
          'user.name=Metrics Test',
          '-c',
          'user.email=metrics@example.invalid',
          'commit',
          '-m',
          'lower baseline with source',
        ])
        await assert.rejects(
          TestMetrics.run(repositoryFullPath, ['check', '--base', acceptedRevision]),
          /lines 0 < 10/,
        )
      } finally {
        rmSync(repositoryFullPath, { recursive: true })
      }
    })
  }
}
