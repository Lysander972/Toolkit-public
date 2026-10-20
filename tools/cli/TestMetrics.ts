import { readFileSync } from 'node:fs'
import { parseArgs } from 'node:util'
import { join } from 'node:path'
import { Option } from 'effect'
import { z } from 'zod'
import { Ajv, type ValidateFunction } from 'ajv'
import { createCoverageSummary } from 'istanbul-lib-coverage'
import type { CoverageSummary, CoverageSummaryData, Totals } from 'istanbul-lib-coverage'
import { calculateMetrics } from 'mutation-testing-metrics'
import { schema } from 'mutation-testing-report-schema'
import type {
  FileResult,
  MutantResult,
  MutationTestResult,
  TestFileDefinitionDictionary,
} from 'mutation-testing-report-schema'
import writeFileAtomic from 'write-file-atomic'
import { ArchitectureProject } from './ArchitectureProject.ts'
import { WorkspaceFiles } from './WorkspaceFiles.ts'
import { Git } from './Git.ts'
import { ArchitectureFiles } from '../governance/architecture-files.ts'
import type {
  ArchitectureDeclarations,
  ArchitectureElement,
  ArchitectureGap,
} from '../governance/derive-rules.ts'
import type { ReadonlyDeep } from 'type-fest'

/** @anchor phase-three-metrics */
interface Measurement {
  readonly coverage: Option.Option<
    Readonly<Record<'lines' | 'statements' | 'functions' | 'branches', number>>
  >
  readonly mutation: Option.Option<number>
}

/** @anchor phase-three-metrics */
interface Baseline {
  readonly schemaVersion: 1
  readonly modules: Readonly<Record<string, Measurement>>
}

interface MutationConfiguration {
  readonly mutate: string[]
}

/** @anchor phase-three-metrics */
export class TestMetrics {
  public static readonly baselineRelativePath: string = 'tools/test-metrics.json'
  private static readonly percentage: z.ZodNumber = z.number().min(0).max(100)
  private static readonly coverageValue: z.ZodType<
    Readonly<Record<'lines' | 'statements' | 'functions' | 'branches', number>>
  > = z.object({
    lines: TestMetrics.percentage,
    statements: TestMetrics.percentage,
    functions: TestMetrics.percentage,
    branches: TestMetrics.percentage,
  })
  private static readonly baselineSchema: z.ZodType<Baseline> = z.object({
    schemaVersion: z.literal(1),
    modules: z.record(
      z.string(),
      z.object({
        coverage: TestMetrics.coverageValue.nullable().transform(Option.fromNullishOr),
        mutation: TestMetrics.percentage.nullable().transform(Option.fromNullishOr),
      }),
    ),
  })
  private static readonly counter: z.ZodType<Totals> = z.object({
    total: z.number().int().nonnegative(),
    covered: z.number().int().nonnegative(),
    skipped: z.number().int().nonnegative(),
    pct: TestMetrics.percentage,
  })
  private static readonly coverageSchema: z.ZodType<Record<string, CoverageSummaryData>> = z.record(
    z.string(),
    z.object({
      lines: TestMetrics.counter,
      statements: TestMetrics.counter,
      functions: TestMetrics.counter,
      branches: TestMetrics.counter,
    }),
  )
  private static readonly validateMutation: ValidateFunction<MutationTestResult> = new Ajv({
    strict: false,
    validateFormats: false,
  }).compile<MutationTestResult>(schema)
  private static readonly mutationSchema: z.ZodType<MutationTestResult> =
    z.custom<MutationTestResult>(
      (value: unknown): boolean => TestMetrics.validateMutation(value),
      'Stryker 报告不符合官方 schema',
    )

  private constructor() {}

  /** @anchor phase-three-metrics */
  public static isTodo(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    element: ReadonlyDeep<ArchitectureElement>,
  ): boolean {
    if (
      model.gaps.some(
        (gap: ArchitectureGap): boolean => gap.kind === 'todo' && gap.target === element.id,
      )
    )
      return true
    if (Option.isNone(element.parentId)) return false
    const parentId: string = element.parentId.value
    return TestMetrics.isTodo(
      model,
      Option.getOrThrow(
        Option.fromNullishOr(
          model.elements.find(
            (parent: ReadonlyDeep<ArchitectureElement>): boolean => parent.id === parentId,
          ),
        ),
      ),
    )
  }

  /** @anchor phase-three-metrics */
  public static measuredFiles(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
  ): readonly string[] {
    const files: readonly string[] = WorkspaceFiles.all(repositoryFullPath)
    const tests: ReadonlySet<string> = new Set(
      WorkspaceFiles.testTypeScript(model, repositoryFullPath, files),
    )
    const production: ReadonlySet<string> = new Set(
      WorkspaceFiles.purposeFiles(model, repositoryFullPath, ['tool', 'common', 'acl'], files),
    )
    const todo: readonly string[] = WorkspaceFiles.scope(model)
      .filter((element: ReadonlyDeep<ArchitectureElement>): boolean =>
        TestMetrics.isTodo(model, element),
      )
      .flatMap((element: ReadonlyDeep<ArchitectureElement>): readonly string[] => element.files)
    return WorkspaceFiles.domainTypeScript(model, repositoryFullPath, files).filter(
      (file: string): boolean =>
        production.has(file) &&
        !tests.has(file) &&
        !file.endsWith('.d.ts') &&
        !todo.some((pattern: string): boolean => ArchitectureFiles.matches(file, pattern)),
    )
  }

  /** @anchor phase-three-metrics */
  public static compare(
    current: Baseline,
    baseline: Baseline,
    record: boolean = false,
    removed: ReadonlySet<string> = new Set(),
  ): readonly string[] {
    const failures: string[] = []
    for (const id of new Set([...Object.keys(current.modules), ...Object.keys(baseline.modules)])) {
      const previous: Option.Option<Measurement> = Option.fromNullishOr(baseline.modules[id])
      const next: Option.Option<Measurement> = Option.fromNullishOr(current.modules[id])
      if (Option.isNone(next)) {
        if (!removed.has(id)) failures.push(`${id}: 当前测量缺少模块`)
        continue
      }
      if (Option.isNone(next.value.coverage)) {
        if (Option.isSome(previous) && Option.isSome(previous.value.coverage))
          failures.push(`${id}: 已测量模块缺少覆盖率`)
        continue
      }
      if (Option.isNone(previous) || Option.isNone(previous.value.coverage)) {
        if (!record) failures.push(`${id}: 需要 record 建立首次实测基线`)
        continue
      }
      const metrics: readonly ('lines' | 'statements' | 'functions' | 'branches')[] = [
        'lines',
        'statements',
        'functions',
        'branches',
      ]
      for (const metric of metrics) {
        const actual: number = next.value.coverage.value[metric]
        const minimum: number = previous.value.coverage.value[metric]
        if (actual < minimum)
          failures.push(`${id}: ${metric} ${String(actual)} < ${String(minimum)}`)
      }
      if (
        Option.isSome(previous.value.mutation) &&
        (Option.isNone(next.value.mutation) ||
          next.value.mutation.value < previous.value.mutation.value)
      )
        failures.push(`${id}: mutation 低于基线 ${String(previous.value.mutation.value)}`)
    }
    return failures
  }

  /** @anchor phase-three-metrics */
  public static async measure(repositoryFullPath: string): Promise<Baseline> {
    const model: ArchitectureDeclarations = await ArchitectureProject.load(
      repositoryFullPath,
      Option.none(),
    )
    const coverage: Record<string, CoverageSummaryData> = TestMetrics.coverageSchema.parse(
      JSON.parse(
        readFileSync(
          join(repositoryFullPath, '_build/testing/coverage/coverage-summary.json'),
          'utf8',
        ),
      ),
    )
    const files: readonly string[] = TestMetrics.measuredFiles(model, repositoryFullPath)
    const mutation: MutationTestResult = TestMetrics.mutationSchema.parse(
      JSON.parse(
        readFileSync(join(repositoryFullPath, '_build/testing/mutation/mutation.json'), 'utf8'),
      ),
    )
    const config: MutationConfiguration = z
      .object({ mutate: z.array(z.string()) })
      .parse(mutation.config)
    if (
      files.length !== config.mutate.length ||
      files.some((file: string): boolean => !config.mutate.includes(file))
    )
      throw new Error('变异报告范围与现役源码不一致')
    for (const [file, result] of Object.entries(mutation.files)) {
      if (!files.includes(file)) throw new Error(`变异报告超出范围: ${file}`)
      if (result.source !== readFileSync(join(repositoryFullPath, file), 'utf8'))
        throw new Error(`变异报告源码已变化: ${file}`)
      if (result.mutants.some((mutant: MutantResult): boolean => mutant.status === 'Pending'))
        throw new Error(`变异报告尚未完成: ${file}`)
    }
    const tests: Option.Option<TestFileDefinitionDictionary> = Option.fromNullishOr(
      mutation.testFiles,
    )
    if (Option.isSome(tests))
      for (const [file, result] of Object.entries(tests.value))
        if (result.source !== readFileSync(join(repositoryFullPath, file), 'utf8'))
          throw new Error(`变异报告测试已变化: ${file}`)
    const normalized: Map<string, CoverageSummaryData> = new Map()
    for (const file of files) {
      const matches: [string, CoverageSummaryData][] = Object.entries(coverage).filter(
        ([name]: [string, CoverageSummaryData]): boolean =>
          name.replaceAll('\\', '/').endsWith('/' + file),
      )
      if (matches.length !== 1) throw new Error(`覆盖率报告必须唯一包含源码: ${file}`)
      normalized.set(file, Option.getOrThrow(Option.fromNullishOr(matches[0]))[1])
    }
    const modules: Record<string, Measurement> = {}
    for (const element of WorkspaceFiles.scope(model)) {
      const moduleFiles: readonly string[] = files.filter((file: string): boolean =>
        element.files.some((pattern: string): boolean => ArchitectureFiles.matches(file, pattern)),
      )
      if (TestMetrics.isTodo(model, element)) {
        modules[element.id] = { coverage: Option.none(), mutation: Option.none() }
        continue
      }
      if (moduleFiles.length === 0) continue
      const summary: CoverageSummary = createCoverageSummary()
      const mutants: Record<string, FileResult> = {}
      for (const file of moduleFiles) {
        summary.merge(
          createCoverageSummary(Option.getOrThrow(Option.fromNullishOr(normalized.get(file)))),
        )
        mutants[file] = Option.getOrThrowWith(
          Option.fromNullishOr(mutation.files[file]),
          (): Error => new Error(`变异报告缺少源码: ${file}`),
        )
      }
      const score: number = calculateMetrics(mutants).metrics.mutationScore
      modules[element.id] = {
        coverage: Option.some(
          TestMetrics.coverageValue.parse({
            lines: summary.lines.pct,
            statements: summary.statements.pct,
            functions: summary.functions.pct,
            branches: summary.branches.pct,
          }),
        ),
        mutation: Number.isNaN(score) ? Option.none() : Option.some(score),
      }
    }
    return { schemaVersion: 1, modules }
  }

  /** @anchor phase-three-record */
  public static async run(repositoryFullPath: string, args: readonly string[]): Promise<number> {
    const parsed: ReturnType<typeof parseArgs> = parseArgs({
      args: [...args],
      allowPositionals: true,
      options: { base: { type: 'string' } },
    })
    const action: string = z
      .enum(['check', 'record'])
      .parse(parsed.positionals.length === 0 ? 'check' : parsed.positionals[0])
    if (parsed.positionals.length > 1) throw new Error('metrics 只接受 check 或 record')
    const revision: string = z.string().min(1).default('HEAD').parse(parsed.values.base)
    if (Object.hasOwn(parsed.values, 'base'))
      Git.run(repositoryFullPath, ['rev-parse', '--verify', revision + '^{commit}'])
    const current: Baseline = await TestMetrics.measure(repositoryFullPath)
    const model: ArchitectureDeclarations = await ArchitectureProject.load(
      repositoryFullPath,
      Option.none(),
    )
    const committed: Option.Option<Baseline> = Git.succeeds(repositoryFullPath, [
      'cat-file',
      '-e',
      revision + ':' + TestMetrics.baselineRelativePath,
    ])
      ? Option.some(
          TestMetrics.baselineSchema.parse(
            JSON.parse(
              Git.treeFile(repositoryFullPath, revision, TestMetrics.baselineRelativePath),
            ),
          ),
        )
      : Option.none()
    const removed: ReadonlySet<string> = new Set(
      Option.isSome(committed)
        ? Object.keys(committed.value.modules).filter(
            (id: string): boolean =>
              !model.elements.some((element: ArchitectureElement): boolean => element.id === id),
          )
        : [],
    )
    const failures: string[] = Option.isSome(committed)
      ? [...TestMetrics.compare(current, committed.value, true, removed)]
      : []
    if (action === 'check') {
      const baseline: Baseline = TestMetrics.baselineSchema.parse(
        JSON.parse(
          readFileSync(join(repositoryFullPath, TestMetrics.baselineRelativePath), 'utf8'),
        ),
      )
      failures.push(...TestMetrics.compare(current, baseline))
      if (Option.isSome(committed))
        failures.push(...TestMetrics.compare(baseline, committed.value, true, removed))
    }
    if (failures.length > 0) throw new Error(failures.join('\n'))
    if (action === 'record')
      await writeFileAtomic(
        join(repositoryFullPath, TestMetrics.baselineRelativePath),
        JSON.stringify(
          {
            ...current,
            modules: Object.fromEntries(
              Object.entries(current.modules).map(
                ([id, value]: [string, Measurement]): [string, unknown] => [
                  id,
                  {
                    coverage: Option.getOrNull(value.coverage),
                    mutation: Option.getOrNull(value.mutation),
                  },
                ],
              ),
            ),
          },
          null,
          2,
        ) + '\n',
      )
    process.stdout.write(
      `测试指标 ${action}: ${String(Object.keys(current.modules).length)} 个模块\n`,
    )
    return 0
  }
}
