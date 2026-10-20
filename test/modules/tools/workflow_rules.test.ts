import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { data, Evaluator, Lexer, Parser, type Expr } from '@actions/expressions'
import type { FunctionDefinition } from '@actions/expressions/funcs/info'
import {
  convertWorkflowTemplate,
  isBasicExpression,
  isMapping,
  isNumber,
  isSequence,
  isString,
  NoOperationTraceWriter,
  parseWorkflow,
  type TemplateParseResult,
  type WorkflowTemplate,
} from '@actions/workflow-parser'
import type {
  TypesFilterConfig,
  PathFilterConfig,
  WorkflowJob,
  Step,
} from '@actions/workflow-parser/model/workflow-template'
import type { TemplateToken } from '@actions/workflow-parser/templates/tokens/template-token'
import type { StringToken } from '@actions/workflow-parser/templates/tokens/string-token'
import { Option } from 'effect'
import { isMatch } from 'picomatch'
import { z } from 'zod'
import { test } from 'vitest'

/** @anchor ci-event-policy */
interface ParsedWorkflow {
  readonly root: TemplateToken
  readonly workflow: WorkflowTemplate
}

/** @anchor ci-event-policy */
interface AuditEvent {
  readonly name: string
  readonly action: string
  readonly label: string
  readonly labels: readonly string[]
  readonly draft: boolean
  readonly audit: boolean
  readonly reportOnly: boolean
}

/** @anchor ci-event-policy */
export class WorkflowRulesTest {
  private constructor() {}

  private static readonly relevantClosure: readonly string[] = [
    'tools/**',
    'src/toolkit/**',
    'src/frontend/**',
    'src/canvas_render/**',
    'src/preview/**',
    'test/modules/**',
    'test/data/**',
    'res/**',
    'legacy/**',
    'openspec/**',
    'requirements/**',
    '.agents/skills/**',
    'package.json',
    '.github/pull_request_template.md',
    'pnpm-lock.yaml',
    'pnpm-workspace.yaml',
    'tsconfig.base.json',
    'vite.config.mjs',
    'preview.config.mjs',
    '.prettierignore',
    'vitest.config.mjs',
    'stryker.config.mjs',
    'eslint.config.mjs',
    'knip.config.ts',
    '.github/workflows/**',
    '.gitignore',
    '.gitattributes',
    '.editorconfig',
    'docs/MechToolkit/**',
  ]

  private static evaluate(
    expression: string,
    context: data.Dictionary,
    succeeded: boolean,
  ): boolean {
    const functions: FunctionDefinition[] = [
      {
        name: 'success',
        minArgs: 0,
        maxArgs: 0,
        call: (): data.BooleanData => new data.BooleanData(succeeded),
      },
      {
        name: 'cancelled',
        minArgs: 0,
        maxArgs: 0,
        call: (): data.BooleanData => new data.BooleanData(false),
      },
    ]
    const parsed: Expr = new Parser(
      new Lexer(expression).lex().tokens,
      ['github', 'needs', 'steps'],
      functions,
    ).parse()
    return z
      .instanceof(data.BooleanData)
      .parse(
        new Evaluator(
          parsed,
          context,
          new Map(
            functions.map((fn: FunctionDefinition): readonly [string, FunctionDefinition] => [
              fn.name,
              fn,
            ]),
          ),
        ).evaluate(),
      ).value
  }

  private static async parse(fileName: 'ci.yml' | 'audit.yml'): Promise<ParsedWorkflow> {
    const parsed: TemplateParseResult = parseWorkflow(
      { name: fileName, content: readFileSync(`.github/workflows/${fileName}`, 'utf8') },
      new NoOperationTraceWriter(),
    )
    parsed.context.errors.check()
    const root: TemplateToken = Option.getOrThrow(Option.fromNullishOr(parsed.value))
    const workflow: WorkflowTemplate = await convertWorkflowTemplate(parsed.context, root)
    parsed.context.errors.check()
    return { root, workflow }
  }

  private static job(workflow: WorkflowTemplate, id: string): WorkflowJob {
    return Option.getOrThrow(
      Option.fromNullishOr(workflow.jobs.find((job: WorkflowJob): boolean => job.id.value === id)),
    )
  }

  private static runs(job: WorkflowJob): readonly string[] {
    if (!('steps' in job)) return []
    return job.steps.flatMap((step: Step): readonly string[] =>
      'run' in step ? [step.run.toString()] : [],
    )
  }

  private static dictionary(payload: unknown): data.Dictionary {
    return z.instanceof(data.Dictionary).parse(JSON.parse(JSON.stringify(payload), data.reviver))
  }

  private static githubContext(eventName: string, event: unknown): Record<string, unknown> {
    return { github: { event_name: eventName, event } }
  }

  private static included(files: readonly string[], patterns: readonly string[]): boolean {
    for (const file of files) {
      let included: boolean = false
      for (const pattern of patterns) {
        const negated: boolean = pattern.startsWith('!')
        const glob: string = negated ? pattern.slice(1) : pattern
        if (isMatch(file, glob, { dot: true })) included = !negated
      }
      if (included) return true
    }
    return false
  }

  private static async dispatchCi(
    this: void,
    event: {
      readonly name: string
      readonly eventName: 'pull_request' | 'push'
      readonly action: string
      readonly execution: boolean
    },
  ): Promise<void> {
    const { workflow }: ParsedWorkflow = await WorkflowRulesTest.parse('ci.yml')
    const configured: TypesFilterConfig = Option.getOrThrow(
      Option.fromNullishOr(workflow.events.pull_request),
    )
    assert.deepEqual(Option.getOrThrow(Option.fromNullishOr(configured.types)), [
      'opened',
      'synchronize',
      'reopened',
      'ready_for_review',
      'closed',
    ])
    const push: PathFilterConfig = Option.getOrThrow(Option.fromNullishOr(workflow.events.push))
    assert.deepEqual(push.paths, WorkflowRulesTest.relevantClosure)
    const pullRequestPaths: PathFilterConfig = Option.getOrThrow(
      Option.fromNullishOr(workflow.events.pull_request),
    )
    assert.deepEqual(
      Option.getOrThrow(Option.fromNullishOr(pullRequestPaths.paths)),
      WorkflowRulesTest.relevantClosure,
    )
    const context: data.Dictionary = WorkflowRulesTest.dictionary(
      WorkflowRulesTest.githubContext(event.eventName, { action: event.action }),
    )
    assert.equal(
      WorkflowRulesTest.evaluate(
        WorkflowRulesTest.job(workflow, 'tools').if.expression,
        context,
        true,
      ),
      event.execution,
      'tools 作业执行条件',
    )
    assert.equal(
      WorkflowRulesTest.evaluate(
        WorkflowRulesTest.job(workflow, 'audit').if.expression,
        context,
        true,
      ),
      event.eventName === 'push',
      'audit 作业仅 push 串入发布链',
    )
  }

  private static async publication(this: void): Promise<void> {
    const { workflow }: ParsedWorkflow = await WorkflowRulesTest.parse('ci.yml')
    const publish: WorkflowJob = WorkflowRulesTest.job(workflow, 'publish')
    const tools: WorkflowJob = WorkflowRulesTest.job(workflow, 'tools')
    assert.ok('steps' in tools)
    const assets: Step = Option.getOrThrow(
      Option.fromNullishOr(
        tools.steps.find(
          (step: Step): boolean =>
            'run' in step && step.run.toString() === 'pnpm build:preview:assets',
        ),
      ),
    )
    assert.ok('run' in assets)
    assert.deepEqual(
      Option.getOrThrow(Option.fromNullishOr(publish.needs)).map(
        (need: StringToken): string => need.value,
      ),
      ['tools', 'audit'],
    )
    for (const eventName of ['push', 'pull_request']) {
      for (const ref of ['refs/heads/main', 'refs/heads/feature']) {
        const context: data.Dictionary = WorkflowRulesTest.dictionary({
          github: { event_name: eventName, ref },
        })
        assert.equal(
          WorkflowRulesTest.evaluate(publish.if.expression, context, true),
          eventName === 'push' && ref === 'refs/heads/main',
          `${eventName} ${ref}`,
        )
        assert.equal(
          WorkflowRulesTest.evaluate(assets.if.expression, context, true),
          eventName === 'push',
        )
        assert.equal(WorkflowRulesTest.evaluate(assets.if.expression, context, false), false)
        assert.equal(WorkflowRulesTest.evaluate(publish.if.expression, context, false), false)
      }
    }
  }

  private static async topology(this: void): Promise<void> {
    const { root, workflow }: ParsedWorkflow = await WorkflowRulesTest.parse('ci.yml')
    assert.deepEqual(
      workflow.jobs.map((job: WorkflowJob): string => job.id.value),
      ['tools', 'audit', 'publish'],
    )
    const toolsJob: WorkflowJob = WorkflowRulesTest.job(workflow, 'tools')
    assert.deepEqual(WorkflowRulesTest.runs(toolsJob), [
      'pnpm install --frozen-lockfile',
      'pnpm pull-request:check -- "$GITHUB_EVENT_PATH"',
      'pnpm requirements:build',
      'pnpm exec run-p --max-parallel 3 --npm-path pnpm check check:schemas check:submodules',
      `\${{ format('pnpm test:metrics --base "{0}"', github.event.pull_request.base.sha || (github.event.created && github.sha) || github.event.before) }}`,
      'pnpm build:preview:assets',
    ])
    assert.ok(isMapping(root))
    const jobs: TemplateToken = Option.getOrThrow(Option.fromNullishOr(root.find('jobs')))
    assert.ok(isMapping(jobs))
    const job: TemplateToken = Option.getOrThrow(Option.fromNullishOr(jobs.find('tools')))
    assert.ok(isMapping(job))
    const timeout: TemplateToken = Option.getOrThrow(
      Option.fromNullishOr(job.find('timeout-minutes')),
    )
    assert.ok(isNumber(timeout))
    const steps: TemplateToken = Option.getOrThrow(Option.fromNullishOr(job.find('steps')))
    assert.ok(isSequence(steps))
    const stepTimeouts: number[] = [...steps].flatMap((step: TemplateToken): number[] => {
      assert.ok(isMapping(step))
      const budget: Option.Option<TemplateToken> = Option.fromNullishOr(
        step.find('timeout-minutes'),
      )
      if (Option.isNone(budget)) return []
      assert.ok(isNumber(budget.value))
      return [budget.value.value]
    })
    assert.ok(stepTimeouts.every((budget: number): boolean => budget < timeout.value))
    const cache: TemplateToken = Option.getOrThrow(
      Option.fromNullishOr(
        [...steps].find((step: TemplateToken): boolean => {
          assert.ok(isMapping(step))
          const uses: Option.Option<TemplateToken> = Option.fromNullishOr(step.find('uses'))
          return (
            Option.isSome(uses) && isString(uses.value) && uses.value.value === 'actions/cache@v4'
          )
        }),
      ),
    )
    assert.ok(isMapping(cache))
    const cacheInput: TemplateToken = Option.getOrThrow(Option.fromNullishOr(cache.find('with')))
    assert.ok(isMapping(cacheInput))
    const cachePath: TemplateToken = Option.getOrThrow(
      Option.fromNullishOr(cacheInput.find('path')),
    )
    assert.ok(isString(cachePath))
    assert.equal(cachePath.value, '.git/stryker-incremental.json')
  }

  private static async metricsBase(this: void): Promise<void> {
    const { workflow }: ParsedWorkflow = await WorkflowRulesTest.parse('ci.yml')
    const toolsJob: WorkflowJob = WorkflowRulesTest.job(workflow, 'tools')
    assert.ok('steps' in toolsJob)
    const metrics: Step = Option.getOrThrow(
      Option.fromNullishOr(
        toolsJob.steps.find(
          (step: Step): boolean => 'run' in step && step.run.toString().includes('test:metrics'),
        ),
      ),
    )
    assert.ok('run' in metrics && isBasicExpression(metrics.run))
    const expression: Expr = new Parser(
      new Lexer(metrics.run.expression).lex().tokens,
      ['github'],
      [],
    ).parse()
    const current: string = 'c'.repeat(40)
    const previous: string = 'a'.repeat(40)
    const base: string = 'b'.repeat(40)
    const scenarios: readonly { readonly event: unknown; readonly expected: string }[] = [
      { event: { created: true, before: '0'.repeat(40) }, expected: current },
      { event: { created: false, before: previous }, expected: previous },
      { event: { pull_request: { base: { sha: base } } }, expected: base },
    ]
    for (const scenario of scenarios) {
      const context: data.Dictionary = WorkflowRulesTest.dictionary({
        github: { sha: current, event: scenario.event },
      })
      const command: string = z
        .instanceof(data.StringData)
        .parse(new Evaluator(expression, context, new Map()).evaluate()).value
      assert.equal(command, `pnpm test:metrics --base "${scenario.expected}"`)
    }
  }

  private static matching(this: void): void {
    const scenarios: readonly {
      readonly files: readonly string[]
      readonly ci: boolean
    }[] = [
      { files: ['tools/cli/A.ts'], ci: true },
      { files: ['requirements/sources.rst'], ci: true },
      { files: ['src/toolkit/a.ts'], ci: true },
      { files: ['res/fonts/f.ttf'], ci: true },
      { files: ['test/modules/tools/x.test.ts'], ci: true },
      { files: ['README.md'], ci: false },
      { files: ['.github/workflows/audit.yml'], ci: true },
    ]
    for (const scenario of scenarios) {
      assert.deepEqual(
        {
          ci: WorkflowRulesTest.included(scenario.files, WorkflowRulesTest.relevantClosure),
        },
        { ci: scenario.ci },
        scenario.files.join(','),
      )
    }
  }

  private static async cancellation(this: void): Promise<void> {
    const { root }: ParsedWorkflow = await WorkflowRulesTest.parse('audit.yml')
    assert.ok(isMapping(root))
    const concurrency: TemplateToken = Option.getOrThrow(
      Option.fromNullishOr(root.find('concurrency')),
    )
    assert.ok(isMapping(concurrency))
    const group: TemplateToken = Option.getOrThrow(Option.fromNullishOr(concurrency.find('group')))
    assert.ok(isBasicExpression(group))
    const evaluateGroup: (event: unknown) => string = (event: unknown): string =>
      WorkflowRulesTest.groupValue(group.expression, event)
    assert.equal(
      evaluateGroup({ action: 'synchronize', label: { name: '' }, pull_request: { number: 42 } }),
      'audit-42',
      '代码事件按 PR 分组取消旧审计',
    )
    assert.equal(
      evaluateGroup({ action: 'closed', label: { name: '' }, pull_request: { number: 42 } }),
      'audit-42',
      '关闭事件取消同 PR 审计',
    )
    assert.equal(
      evaluateGroup({
        action: 'labeled',
        label: { name: 'ready-to-merge' },
        pull_request: { number: 42 },
      }),
      'audit-42',
      '就绪标签接管同组审计',
    )
    assert.equal(
      evaluateGroup({ action: 'labeled', label: { name: 'bug' }, pull_request: { number: 42 } }),
      'audit-1234567',
      '无关标签走独占分组，不取消正在运行的审计',
    )
  }

  private static groupValue(expression: string, event: unknown): string {
    const context: data.Dictionary = WorkflowRulesTest.dictionary({
      github: { event_name: 'pull_request', run_id: '1234567', event },
    })
    const parsed: Expr = new Parser(new Lexer(expression).lex().tokens, ['github'], []).parse()
    return String(
      z
        .union([z.instanceof(data.StringData), z.instanceof(data.NumberData)])
        .parse(new Evaluator(parsed, context, new Map()).evaluate()).value,
    )
  }

  private static async dispatchAudit(this: void, event: AuditEvent): Promise<void> {
    const { root, workflow }: ParsedWorkflow = await WorkflowRulesTest.parse('audit.yml')
    const configured: TypesFilterConfig = Option.getOrThrow(
      Option.fromNullishOr(workflow.events.pull_request),
    )
    assert.equal(
      Option.getOrThrow(Option.fromNullishOr(configured.types)).includes(event.action),
      true,
      event.action,
    )
    const pullRequestPaths: PathFilterConfig = Option.getOrThrow(
      Option.fromNullishOr(workflow.events.pull_request),
    )
    assert.equal(
      Option.isNone(Option.fromNullishOr(pullRequestPaths.paths)),
      true,
      'audit 必须对全部 PR 事件可达，不设 paths 过滤',
    )
    const context: data.Dictionary = WorkflowRulesTest.dictionary(
      WorkflowRulesTest.githubContext('pull_request', {
        action: event.action,
        label: { name: event.label },
        pull_request: {
          draft: event.draft,
          labels: event.labels.map((name: string): { name: string } => ({ name })),
        },
      }),
    )
    const audit: WorkflowJob = WorkflowRulesTest.job(workflow, 'audit')
    assert.equal(WorkflowRulesTest.evaluate(audit.if.expression, context, true), event.audit)
    assert.ok(isMapping(root))
    const jobs: TemplateToken = Option.getOrThrow(Option.fromNullishOr(root.find('jobs')))
    assert.ok(isMapping(jobs))
    const job: TemplateToken = Option.getOrThrow(Option.fromNullishOr(jobs.find('audit')))
    assert.ok(isMapping(job))
    const reportOnly: TemplateToken = Option.getOrThrow(
      Option.fromNullishOr(job.find('continue-on-error')),
    )
    assert.ok(isBasicExpression(reportOnly))
    assert.equal(WorkflowRulesTest.evaluate(reportOnly.expression, context, true), event.reportOnly)
  }

  static {
    test('发布仅 main push 触发', WorkflowRulesTest.publication)
    test('原有单作业拓扑保持并校验同次静态产物', WorkflowRulesTest.topology)
    test('指标基线按首次 push、后续 push 与 PR 实际事件求值', WorkflowRulesTest.metricsBase)
    test('具体变更文件经 picomatch 求值工作流短路', WorkflowRulesTest.matching)
    test('无关标签独占分组，审计组按 PR 取消', WorkflowRulesTest.cancellation)
    const ciEvents: readonly {
      readonly name: string
      readonly eventName: 'pull_request' | 'push'
      readonly action: string
      readonly execution: boolean
    }[] = [
      { name: 'main push 执行完整验证并串入审计', eventName: 'push', action: '', execution: true },
      {
        name: 'PR 打开执行完整验证',
        eventName: 'pull_request',
        action: 'opened',
        execution: true,
      },
      {
        name: 'PR 更新执行完整验证',
        eventName: 'pull_request',
        action: 'synchronize',
        execution: true,
      },
      {
        name: 'PR 重新打开执行完整验证',
        eventName: 'pull_request',
        action: 'reopened',
        execution: true,
      },
      {
        name: '草稿转正式执行完整验证',
        eventName: 'pull_request',
        action: 'ready_for_review',
        execution: true,
      },
      {
        name: '关闭 PR 跳过执行并取消旧运行',
        eventName: 'pull_request',
        action: 'closed',
        execution: false,
      },
    ]
    test.each(ciEvents)('$name', WorkflowRulesTest.dispatchCi)
    const auditEvents: readonly AuditEvent[] = [
      {
        name: '草稿打开报告审计',
        action: 'opened',
        label: '',
        labels: [],
        draft: true,
        audit: true,
        reportOnly: true,
      },
      {
        name: '普通 PR 更新报告审计',
        action: 'synchronize',
        label: '',
        labels: [],
        draft: false,
        audit: true,
        reportOnly: true,
      },
      {
        name: '就绪 PR 更新执行审计门禁',
        action: 'synchronize',
        label: '',
        labels: ['ready-to-merge'],
        draft: false,
        audit: true,
        reportOnly: false,
      },
      {
        name: '添加就绪标签执行审计门禁',
        action: 'labeled',
        label: 'ready-to-merge',
        labels: ['ready-to-merge'],
        draft: false,
        audit: true,
        reportOnly: false,
      },
      {
        name: '移除就绪标签只报告审计',
        action: 'unlabeled',
        label: 'ready-to-merge',
        labels: [],
        draft: false,
        audit: true,
        reportOnly: true,
      },
      {
        name: '草稿的就绪标签只报告审计',
        action: 'labeled',
        label: 'ready-to-merge',
        labels: ['ready-to-merge'],
        draft: true,
        audit: true,
        reportOnly: true,
      },
      {
        name: '添加无关标签跳过审计',
        action: 'labeled',
        label: 'bug',
        labels: ['bug'],
        draft: false,
        audit: false,
        reportOnly: true,
      },
      {
        name: '就绪 PR 移除无关标签跳过审计',
        action: 'unlabeled',
        label: 'bug',
        labels: ['ready-to-merge'],
        draft: false,
        audit: false,
        reportOnly: false,
      },
      {
        name: '关闭 PR 取消审计旧运行',
        action: 'closed',
        label: '',
        labels: [],
        draft: false,
        audit: false,
        reportOnly: true,
      },
    ]
    test.each(auditEvents)('$name', WorkflowRulesTest.dispatchAudit)
  }
}
