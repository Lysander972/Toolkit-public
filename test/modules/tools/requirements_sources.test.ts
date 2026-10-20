import assert from 'node:assert/strict'
import { execFileSync, spawnSync } from 'node:child_process'
import type { SpawnSyncReturns } from 'node:child_process'
import {
  copyFileSync,
  chmodSync,
  linkSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
  unlinkSync,
  symlinkSync,
} from 'node:fs'
import { join, resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import { DatabaseSync } from 'node:sqlite'
import { Option } from 'effect'
import { afterEach, test } from 'vitest'
import { z } from 'zod'
import { Git } from '../../../tools/cli/Git.ts'
import { RequirementsCli } from '../../../tools/cli/RequirementsCli.ts'
import { RequirementsSources } from '../../../tools/cli/RequirementsSources.ts'
import { RequirementsReviewStore } from '../../../tools/cli/RequirementsReviewStore.ts'
import { HostProcess } from '../../../tools/cli/HostProcess.ts'

interface SourceContext {
  readonly anchor: { readonly body: string }
  readonly fingerprint: string
}

interface AuditReview {
  readonly ticket: { readonly id: string; readonly token: string }
  readonly context: {
    readonly anchor: { readonly body: string }
    readonly related: readonly { readonly body: string }[]
  }
}

interface FixtureSubmission {
  readonly id: string
  readonly token: string
  readonly verdict: Record<string, unknown>
}

/** @anchor cli-boundary */
export class RequirementsSourcesTest {
  private constructor() {}

  private static readonly id: string = '0e304048-d922-4b0c-9fc7-c2e7ab6f7802'
  private static readonly projects: string[] = []

  private static project(): string {
    mkdirSync(resolve('_build/requirements/tests'), { recursive: true })
    const rootFullPath: string = mkdtempSync(resolve('_build/requirements/tests/sources-'))
    RequirementsSourcesTest.projects.push(rootFullPath)
    mkdirSync(join(rootFullPath, 'requirements'))
    mkdirSync(join(rootFullPath, 'tools'))
    for (const name of [
      'ubproject.toml',
      'schemas.json',
      'quality.toml',
      'conf.py',
      'gherkin_extract.py',
    ])
      copyFileSync(join('requirements', name), join(rootFullPath, 'requirements', name))
    mkdirSync(join(rootFullPath, '_build/requirements'), { recursive: true })
    symlinkSync(
      resolve('requirements/.venv'),
      join(rootFullPath, 'requirements/.venv'),
      process.platform === 'win32' ? 'junction' : 'dir',
    )
    writeFileSync(
      join(rootFullPath, '.gitignore'),
      '_build/\nrequirements/.ub_cache/\nrequirements/.venv/\n',
    )
    writeFileSync(join(rootFullPath, 'requirements/index.rst'), '.. needimport:: toolkit_sources\n')
    writeFileSync(
      join(rootFullPath, 'requirements/bindings.json'),
      JSON.stringify({
        current_version: '1',
        versions: {
          '1': {
            needs: {
              [RequirementsSourcesTest.id]: {
                id: RequirementsSourcesTest.id,
                type: 'implementation',
                title: 'Actual source',
                status: 'active',
                implements: ['96840b50-0dbe-41d9-8805-e7613236017f'],
                content: 'Complete implementation and architecture.',
                bindings: [
                  { kind: 'file', relativePath: 'tools/value.ts' },
                  { kind: 'architecture', element: 'app.code' },
                  { kind: 'file', relativePath: 'tools/model.c4' },
                ],
              },
            },
          },
        },
      }),
    )
    writeFileSync(
      join(rootFullPath, 'tools/value.ts'),
      '\nexport class Value {\n  public result(): number { return 17 }\n}\n\n',
    )
    writeFileSync(
      join(rootFullPath, 'tools/model.c4'),
      "specification { element system\n element container\n tag tool\n tag doc }\nmodel { app = system 'App' { description 'Root' metadata { sourcePath 'tools' checks 'selfcheck' }\n code = container 'Code' { #tool description 'Actual architecture body' metadata { sourcePath 'tools' files ['tools/*.ts'] } }\n contract = container 'Contract' { #doc description 'Fixture source contract' metadata { specPath 'requirements/sources/fixture' files ['requirements/sources/fixture/spec.md'] } } }\n app.code -> app.contract }\n",
    )
    Git.run(rootFullPath, ['init', '--initial-branch=main'])
    Git.run(rootFullPath, ['config', 'user.name', 'Requirements Fixture'])
    Git.run(rootFullPath, ['config', 'user.email', 'requirements@example.invalid'])
    Git.run(rootFullPath, ['config', 'core.autocrlf', 'false'])
    Git.run(rootFullPath, ['add', '.'])
    Git.run(rootFullPath, ['-c', 'commit.gpgsign=false', 'commit', '-m', 'test: original sources'])
    return rootFullPath
  }

  private static executable(): string {
    return resolve(
      '_build/requirements',
      process.platform === 'win32' ? 'ubc-windows-x64-0.37.0.exe' : 'ubc-linux-x64-0.37.0',
    )
  }

  private static review(
    rootFullPath: string,
    args: readonly string[],
    input: Option.Option<string>,
  ): SpawnSyncReturns<string> {
    return spawnSync(
      process.execPath,
      [
        '--import',
        pathToFileURL(resolve('node_modules/tsx/dist/loader.mjs')).href,
        resolve('tools/cli/main.ts'),
        'requirements',
        'review',
        ...args,
      ],
      {
        cwd: rootFullPath,
        encoding: 'utf8',
        windowsHide: true,
        ...(Option.isSome(input) ? { input: input.value } : {}),
      },
    )
  }

  private static context(rootFullPath: string, sourceRelativePath: string): SourceContext {
    return z
      .object({ anchor: z.object({ body: z.string() }), fingerprint: z.string() })
      .parse(
        JSON.parse(
          execFileSync(
            RequirementsSourcesTest.executable(),
            [
              'agent',
              'context',
              '--id',
              RequirementsSourcesTest.id,
              '-c',
              'needs.import_keys.toolkit_sources=' + JSON.stringify('../' + sourceRelativePath),
            ],
            { cwd: join(rootFullPath, 'requirements'), encoding: 'utf8', windowsHide: true },
          ),
        ),
      )
  }

  static {
    afterEach((): void => {
      for (const rootFullPath of RequirementsSourcesTest.projects.splice(0))
        rmSync(rootFullPath, { recursive: true })
    })

    test('已迁移范围新增源码漏绑硬失败，未声明规范归属与待迁移范围分别报告', async (): Promise<void> => {
      const rootFullPath: string = RequirementsSourcesTest.project()
      const original: Awaited<ReturnType<typeof RequirementsSources.coverage>> =
        await RequirementsSources.coverage(rootFullPath, Option.none())
      assert.deepEqual(original.unbound, [])
      assert.deepEqual(original.unassigned, [])
      writeFileSync(join(rootFullPath, 'tools/addition.ts'), 'export class Addition {}\n')
      const missing: Awaited<ReturnType<typeof RequirementsSources.coverage>> =
        await RequirementsSources.coverage(rootFullPath, Option.none())
      assert.deepEqual(missing.unbound, ['tools/addition.ts'])
      await assert.rejects(RequirementsCli.run(rootFullPath, ['check']), /已迁移范围的正文未绑定/)
      mkdirSync(join(rootFullPath, 'src'))
      writeFileSync(join(rootFullPath, 'src/unowned.ts'), 'export class Unowned {}\n')
      const modelFullPath: string = join(rootFullPath, 'tools/model.c4')
      const model: string = readFileSync(modelFullPath, 'utf8')
      writeFileSync(
        modelFullPath,
        model +
          "model { extend app { orphan = container 'Orphan' { #tool description 'No contract yet' metadata { sourcePath 'src' files ['src/*.ts'] } } } }\n",
      )
      const unowned: Awaited<ReturnType<typeof RequirementsSources.coverage>> =
        await RequirementsSources.coverage(rootFullPath, Option.none())
      assert.deepEqual(unowned.unassigned, ['app.orphan'])
      await assert.rejects(RequirementsCli.run(rootFullPath, ['check']), /现役源码未声明规范归属/)
      writeFileSync(
        modelFullPath,
        readFileSync(modelFullPath, 'utf8') +
          "model { extend app { old = container 'Pending contract' { #doc description 'Not migrated' metadata { specPath 'openspec/specs/pending' files ['openspec/specs/pending/spec.md'] } } } app.orphan -> app.old }\n",
      )
      const pending: Awaited<ReturnType<typeof RequirementsSources.coverage>> =
        await RequirementsSources.coverage(rootFullPath, Option.none())
      assert.deepEqual(pending.unassigned, [])
      assert.deepEqual(pending.pending, ['openspec/specs/pending/spec.md'])
      assert.ok(!pending.unbound.includes('src/unowned.ts'))
    })

    test('迁移原文绑定保全 Markdown 全文，Git blob 与工作树各自提供正文', async (): Promise<void> => {
      const rootFullPath: string = RequirementsSourcesTest.project()
      const original: string =
        '# 原始合同\n\n第一段保持原文。\n\n<!-- @anchor id=original -->\n\n最后一段。\n'
      writeFileSync(join(rootFullPath, 'requirements/original.md'), original)
      writeFileSync(
        join(rootFullPath, 'requirements/bindings.json'),
        JSON.stringify({
          current_version: '1',
          versions: {
            '1': {
              needs: {
                [RequirementsSourcesTest.id]: {
                  id: RequirementsSourcesTest.id,
                  type: 'source',
                  title: '迁移原文',
                  status: 'active',
                  implements: [],
                  content: '原文来源由 Git 保留。',
                  bindings: [{ kind: 'file', relativePath: 'requirements/original.md' }],
                },
              },
            },
          },
        }),
      )
      Git.run(rootFullPath, ['add', 'requirements'])
      const tree: string = Git.run(rootFullPath, ['write-tree'])
      writeFileSync(
        join(rootFullPath, 'requirements/original.md'),
        original.replace('最后一段。', '授权纠偏后的末段。'),
      )
      await RequirementsSources.generate(
        rootFullPath,
        '_build/original/needs.json',
        Option.some(tree),
      )
      await RequirementsSources.generate(rootFullPath, '_build/current/needs.json', Option.none())
      const previous: SourceContext = RequirementsSourcesTest.context(
        rootFullPath,
        '_build/original/needs.json',
      )
      const current: SourceContext = RequirementsSourcesTest.context(
        rootFullPath,
        '_build/current/needs.json',
      )
      assert.ok(previous.anchor.body.includes(original.replaceAll('\n', '\n   ').trimEnd()))
      assert.ok(current.anchor.body.includes('授权纠偏后的末段。'))
      assert.ok(!previous.anchor.body.includes('授权纠偏后的末段。'))
      assert.notEqual(previous.fingerprint, current.fingerprint)
    })

    test('完整原生检视允许一致的 HEAD/index，源码暂存变化仍使检视失败', async (): Promise<void> => {
      const rootFullPath: string = RequirementsSourcesTest.project()
      const cwd: string = join(rootFullPath, 'requirements')
      const ids: readonly string[] = [
        '638c682e-d34a-4136-8e59-44ec8e540131',
        'c20c846a-557e-4fe7-8ed0-ba72f195689e',
        'b616041f-b3d5-4fad-80eb-9de1d4b206ce',
        '96840b50-0dbe-41d9-8805-e7613236017f',
        RequirementsSourcesTest.id,
        '30782fb3-42e2-46b0-baf0-6a45be6b043e',
      ]
      writeFileSync(
        join(cwd, 'index.rst'),
        ['source', 'interpretation', 'decision', 'design']
          .map(
            (type: string, index: number): string =>
              '.. ' +
              type +
              ':: Fixture ' +
              type +
              '\n   :id: ' +
              z.string().parse(ids[index]) +
              '\n   :status: active\n' +
              (index === 0 ? '' : '   :implements: ' + z.string().parse(ids[index - 1]) + '\n') +
              '\n   Full fixture body for ' +
              type +
              '.\n\n',
          )
          .join('') +
          '.. needimport:: toolkit_sources\n\n.. verification:: Fixture verification\n   :id: ' +
          z.string().parse(ids[5]) +
          '\n   :status: active\n   :implements: ' +
          RequirementsSourcesTest.id +
          '\n\n   Fixture verification body.\n',
      )
      await RequirementsSources.generate(
        rootFullPath,
        '_build/requirements/sources.json',
        Option.none(),
      )
      const executableFullPath: string = RequirementsSourcesTest.executable()
      linkSync(
        executableFullPath,
        join(
          rootFullPath,
          '_build/requirements',
          process.platform === 'win32' ? 'ubc-windows-x64-0.37.0.exe' : 'ubc-linux-x64-0.37.0',
        ),
      )
      for (const id of ids.slice(1)) {
        const brief: {
          briefs: {
            review_brief: {
              fingerprints: Record<string, string>
              criteria: { fingerprint: string }
            }
          }[]
        } = z
          .object({
            briefs: z.array(
              z.object({
                review_brief: z.object({
                  fingerprints: z.record(z.string(), z.string()),
                  criteria: z.object({ fingerprint: z.string() }),
                }),
              }),
            ),
          })
          .parse(
            JSON.parse(
              execFileSync(executableFullPath, ['agent', 'review-brief', '--ids', id], {
                cwd,
                encoding: 'utf8',
                windowsHide: true,
              }),
            ),
          )
        const review: Option.Option<{
          fingerprints: Record<string, string>
          criteria: { fingerprint: string }
        }> = Option.fromNullishOr(brief.briefs.at(0)?.review_brief)
        execFileSync(
          executableFullPath,
          [
            'agent',
            'verdict-submit',
            id,
            '--file',
            '-',
            '--fingerprint',
            z.string().parse(Option.getOrThrow(review).fingerprints[id]),
            '--criteria-fingerprint',
            Option.getOrThrow(review).criteria.fingerprint,
          ],
          {
            cwd,
            encoding: 'utf8',
            windowsHide: true,
            input: JSON.stringify({
              axes: {
                parent_fit: {
                  score: 2,
                  reason: 'The authored fixture chain is directly connected.',
                },
                reasoning: {
                  score: 2,
                  reason: 'This fixture exercises native verification of committed inputs.',
                },
                evidence: { score: 2, reason: 'Real native verdicts and real Git trees are used.' },
              },
            }),
          },
        )
      }
      Git.run(rootFullPath, ['add', '.'])
      Git.run(rootFullPath, [
        '-c',
        'commit.gpgsign=false',
        'commit',
        '-m',
        'test: complete reviewed graph',
      ])
      const tree: string = Git.run(rootFullPath, ['write-tree'])
      await assert.rejects(RequirementsCli.run(rootFullPath, ['verify', 'HEAD']), /缺少领取证明/)
      const audit: SpawnSyncReturns<string> = RequirementsSourcesTest.review(
        rootFullPath,
        ['audit', '--all'],
        Option.none(),
      )
      assert.equal(audit.status, 0, audit.stdout + audit.stderr)
      const bundle: { reviews: AuditReview[] } = z
        .object({
          reviews: z.array(
            z.object({
              ticket: z.object({ id: z.string(), token: z.string() }),
              context: z.object({
                anchor: z.object({ body: z.string() }),
                related: z.array(z.object({ body: z.string() })),
              }),
            }),
          ),
        })
        .parse(JSON.parse(audit.stdout))
      assert.deepEqual(
        new Set(bundle.reviews.map((review: AuditReview): string => review.ticket.id)),
        new Set(ids.slice(1)),
      )
      const submissions: FixtureSubmission[] = bundle.reviews.map(
        (reviewed: AuditReview): FixtureSubmission => {
          assert.ok(reviewed.context.anchor.body.length > 0)
          assert.ok(
            reviewed.context.related.every(
              (need: { readonly body: string }): boolean => need.body.length > 0,
            ),
          )
          return {
            id: reviewed.ticket.id,
            token: reviewed.ticket.token,
            verdict: {
              axes: {
                parent_fit: { score: 2, reason: 'Fixture graph has its complete upstream body.' },
                reasoning: {
                  score: 2,
                  reason: 'Fixture exercises the atomic read and submit protocol.',
                },
                evidence: {
                  score: 2,
                  reason: 'The real CLI issued and consumed this fixture ticket.',
                },
              },
            },
          }
        },
      )
      const malformed: SpawnSyncReturns<string> = RequirementsSourcesTest.review(
        rootFullPath,
        ['record', '--stdin'],
        Option.some(
          JSON.stringify(
            submissions.map((submission: FixtureSubmission, index: number): FixtureSubmission =>
              index === submissions.length - 1
                ? { ...submission, verdict: { axes: {} } }
                : submission,
            ),
          ),
        ),
      )
      assert.notEqual(malformed.status, 0)
      const recorded: SpawnSyncReturns<string> = RequirementsSourcesTest.review(
        rootFullPath,
        ['record', '--stdin'],
        Option.some(JSON.stringify(submissions)),
      )
      assert.equal(recorded.status, 0, recorded.stdout + recorded.stderr)
      const duplicate: SpawnSyncReturns<string> = RequirementsSourcesTest.review(
        rootFullPath,
        ['record', '--stdin'],
        Option.some(JSON.stringify(submissions)),
      )
      assert.notEqual(duplicate.status, 0)
      const locked: DatabaseSync = new DatabaseSync(
        Git.privatePath(rootFullPath, 'requirements-review.sqlite'),
      )
      try {
        locked.exec('BEGIN IMMEDIATE')
        const denied: SpawnSyncReturns<string> = RequirementsSourcesTest.review(
          rootFullPath,
          ['audit', '--id', RequirementsSourcesTest.id],
          Option.none(),
        )
        assert.notEqual(denied.status, 0)
        assert.equal(denied.stdout, '')
        assert.ok(denied.stderr.includes('database is locked'), denied.stderr)
      } finally {
        locked.exec('ROLLBACK')
        locked.close()
      }
      Git.run(rootFullPath, ['add', 'requirements'])
      Git.run(rootFullPath, [
        '-c',
        'commit.gpgsign=false',
        'commit',
        '-m',
        'test: atomic review proofs',
      ])
      assert.equal(await RequirementsCli.run(rootFullPath, ['verify', 'HEAD']), 0)
      assert.equal(await RequirementsCli.run(rootFullPath, ['verify', 'index']), 0)
      const proofRelativePath: string =
        'requirements/.pharaoh/receipts/' + RequirementsSourcesTest.id + '.json'
      const proof: string = readFileSync(join(rootFullPath, proofRelativePath), 'utf8')
      unlinkSync(join(rootFullPath, proofRelativePath))
      Git.run(rootFullPath, ['add', proofRelativePath])
      const inherited: SpawnSyncReturns<string> = RequirementsSourcesTest.review(
        rootFullPath,
        ['reconcile'],
        Option.none(),
      )
      assert.equal(inherited.status, 0, inherited.stdout + inherited.stderr)
      assert.deepEqual(
        z.object({ inherited: z.array(z.string()) }).parse(JSON.parse(inherited.stdout)).inherited,
        [RequirementsSourcesTest.id],
      )
      assert.equal(readFileSync(join(rootFullPath, proofRelativePath), 'utf8'), proof)
      Git.run(rootFullPath, ['add', proofRelativePath])
      assert.equal(await RequirementsCli.run(rootFullPath, ['verify', 'index']), 0)
      writeFileSync(
        join(rootFullPath, 'package.json'),
        JSON.stringify({
          private: true,
          packageManager: z
            .object({ packageManager: z.string() })
            .parse(JSON.parse(readFileSync('package.json', 'utf8'))).packageManager,
          scripts: {
            requirements:
              'node --import="' +
              pathToFileURL(resolve('node_modules/tsx/dist/loader.mjs')).href +
              '" "' +
              resolve('tools/cli/main.ts').replaceAll('\\', '/') +
              '" requirements',
          },
        }),
      )
      copyFileSync('pnpm-workspace.yaml', join(rootFullPath, 'pnpm-workspace.yaml'))
      assert.equal(
        await HostProcess.run(
          HostProcess.pnpm(process.platform, [
            'install',
            '--lockfile-only',
            '--no-frozen-lockfile',
            '--offline',
            '--ignore-scripts',
          ]),
          rootFullPath,
        ),
        0,
      )
      mkdirSync(join(rootFullPath, 'tools/git-hooks'))
      for (const name of ['pre-rebase', 'post-rewrite']) {
        copyFileSync(join('tools/git-hooks', name), join(rootFullPath, 'tools/git-hooks', name))
        chmodSync(join(rootFullPath, 'tools/git-hooks', name), 0o755)
      }
      Git.run(rootFullPath, ['config', 'core.hooksPath', 'tools/git-hooks'])
      const priorHead: string = Git.run(rootFullPath, ['rev-parse', 'HEAD'])
      Git.run(rootFullPath, ['hook', 'run', 'pre-rebase', '--', priorHead])
      Git.run(rootFullPath, [
        '-c',
        'commit.gpgsign=false',
        'commit',
        '--allow-empty',
        '-m',
        'test: rewritten tip',
      ])
      const rewrittenHead: string = Git.run(rootFullPath, ['rev-parse', 'HEAD'])
      const mappingFullPath: string = Git.privatePath(rootFullPath, 'rewrite-mapping')
      writeFileSync(mappingFullPath, priorHead + ' ' + rewrittenHead + '\n')
      const rewritten: SpawnSyncReturns<string> = spawnSync(
        'git',
        ['hook', 'run', '--to-stdin=' + mappingFullPath, 'post-rewrite', '--', 'rebase'],
        {
          cwd: rootFullPath,
          encoding: 'utf8',
          windowsHide: true,
        },
      )
      assert.equal(rewritten.status, 0, rewritten.stdout + rewritten.stderr)
      const local: RequirementsReviewStore = new RequirementsReviewStore(rootFullPath)
      try {
        assert.equal(Option.isNone(local.rewriteHead()), true)
      } finally {
        local.close()
      }
      assert.equal(await RequirementsCli.run(rootFullPath, ['verify', 'HEAD']), 0)
      writeFileSync(
        join(rootFullPath, 'tools/value.ts'),
        'export class Value { public result(): number { return 97 } }\n',
      )
      assert.equal(await RequirementsCli.run(rootFullPath, ['verify', 'HEAD']), 0)
      Git.run(rootFullPath, ['add', 'tools/value.ts'])
      assert.equal(await RequirementsCli.run(rootFullPath, ['verify', 'index']), 1)
      const pending: SpawnSyncReturns<string> = RequirementsSourcesTest.review(
        rootFullPath,
        ['status', '--id', RequirementsSourcesTest.id],
        Option.none(),
      )
      assert.equal(pending.status, 0, pending.stdout + pending.stderr)
      const diagnostic: { valid: boolean; changes: { change: string }[] } = Option.getOrThrow(
        Option.fromNullishOr(
          z
            .array(
              z.object({ valid: z.boolean(), changes: z.array(z.object({ change: z.string() })) }),
            )
            .parse(JSON.parse(pending.stdout))[0],
        ),
      )
      assert.equal(diagnostic.valid, false)
      assert.ok(
        diagnostic.changes.some(
          (change: { change: string }): boolean => change.change === 'modified',
        ),
      )
      assert.notEqual(Git.run(rootFullPath, ['write-tree']), tree)
      assert.equal(Git.run(rootFullPath, ['diff', '--name-only']), '')
    })

    test('完整 TS 方法和 LikeC4 正文进入原生上下文，独占导入位置不改变内容指纹', async (): Promise<void> => {
      const rootFullPath: string = RequirementsSourcesTest.project()
      await Promise.all([
        RequirementsSources.generate(rootFullPath, '_build/one/needs.json', Option.none()),
        RequirementsSources.generate(rootFullPath, '_build/two/needs.json', Option.none()),
      ])
      const first: SourceContext = RequirementsSourcesTest.context(
        rootFullPath,
        '_build/one/needs.json',
      )
      const second: SourceContext = RequirementsSourcesTest.context(
        rootFullPath,
        '_build/two/needs.json',
      )
      assert.ok(first.anchor.body.includes('public result(): number { return 17 }'))
      assert.ok(first.anchor.body.includes("description 'Actual architecture body'"))
      assert.equal(first.fingerprint, second.fingerprint)
      assert.deepEqual(
        readFileSync(join(rootFullPath, '_build/one/needs.json')),
        readFileSync(join(rootFullPath, '_build/two/needs.json')),
      )
    })

    test.each(['TS 方法', 'LikeC4 正文', 'LikeC4 扩展', 'LikeC4 外部关系'])(
      '%s 改动使真实原生意见过期，隔离输入不互相污染',
      async (change: string): Promise<void> => {
        const rootFullPath: string = RequirementsSourcesTest.project()
        const cwd: string = join(rootFullPath, 'requirements')
        const override: string = 'needs.import_keys.toolkit_sources="../_build/one/needs.json"'
        await RequirementsSources.generate(rootFullPath, '_build/one/needs.json', Option.none())
        const original: SourceContext = RequirementsSourcesTest.context(
          rootFullPath,
          '_build/one/needs.json',
        )
        const brief: { briefs: { review_brief: { criteria: { fingerprint: string } } }[] } = z
          .object({
            briefs: z.array(
              z.object({
                review_brief: z.object({ criteria: z.object({ fingerprint: z.string() }) }),
              }),
            ),
          })
          .parse(
            JSON.parse(
              execFileSync(
                RequirementsSourcesTest.executable(),
                ['agent', 'review-brief', '--ids', RequirementsSourcesTest.id, '-c', override],
                { cwd, encoding: 'utf8', windowsHide: true },
              ),
            ),
          )
        execFileSync(
          RequirementsSourcesTest.executable(),
          [
            'agent',
            'verdict-submit',
            RequirementsSourcesTest.id,
            '--file',
            '-',
            '--fingerprint',
            original.fingerprint,
            '--criteria-fingerprint',
            z.string().parse(brief.briefs.at(0)?.review_brief.criteria.fingerprint),
            '-c',
            override,
          ],
          {
            cwd,
            encoding: 'utf8',
            windowsHide: true,
            input: JSON.stringify({
              axes: {
                parent_fit: { score: 2, reason: 'Fixture source is fully present.' },
                reasoning: { score: 2, reason: 'Fixture has a direct source binding.' },
                evidence: { score: 2, reason: 'Fixture exercises native source freshness.' },
              },
            }),
          },
        )
        execFileSync(
          RequirementsSourcesTest.executable(),
          ['agent', 'verdict-check', '-c', override],
          { cwd, encoding: 'utf8', windowsHide: true },
        )
        if (change === 'TS 方法')
          writeFileSync(
            join(rootFullPath, 'tools/value.ts'),
            'export class Value { public result(): number { return 29 } }\n',
          )
        else if (change === 'LikeC4 正文')
          writeFileSync(
            join(rootFullPath, 'tools/model.c4'),
            readFileSync(join(rootFullPath, 'tools/model.c4'), 'utf8').replace(
              'Actual architecture body',
              'Changed architecture responsibility',
            ),
          )
        else if (change === 'LikeC4 扩展')
          writeFileSync(
            join(rootFullPath, 'tools/extension.c4'),
            "model { extend app.code { extra = container 'Extra' { #tool description 'Extended responsibility' metadata { files ['tools/extra.json'] } } } }\n",
          )
        else
          writeFileSync(
            join(rootFullPath, 'tools/model.c4'),
            readFileSync(join(rootFullPath, 'tools/model.c4'), 'utf8') +
              "specification { tag external } model { external = system 'External' { #external description 'External dependency' } app.code -> external 'Changed dependency' }\n",
          )
        await RequirementsSources.generate(rootFullPath, '_build/two/needs.json', Option.none())
        const changed: SourceContext = RequirementsSourcesTest.context(
          rootFullPath,
          '_build/two/needs.json',
        )
        assert.notEqual(changed.fingerprint, original.fingerprint)
        assert.equal(
          RequirementsSourcesTest.context(rootFullPath, '_build/one/needs.json').fingerprint,
          original.fingerprint,
        )
        const expired: SpawnSyncReturns<string> = spawnSync(
          RequirementsSourcesTest.executable(),
          [
            'agent',
            'verdict-check',
            '-c',
            'needs.import_keys.toolkit_sources="../_build/two/needs.json"',
          ],
          { cwd, encoding: 'utf8', windowsHide: true },
        )
        assert.notEqual(expired.status, 0, expired.stdout + expired.stderr)
        assert.deepEqual(
          z.object({ outdated: z.array(z.string()) }).parse(JSON.parse(expired.stdout)).outdated,
          [RequirementsSourcesTest.id],
        )
      },
    )

    test('HEAD 与 index 的全文来自各自 blob，未暂存正文不能替代正式受检输入', async (): Promise<void> => {
      const rootFullPath: string = RequirementsSourcesTest.project()
      writeFileSync(join(rootFullPath, 'tools/value.ts'), '\nexport const result = 29\n\n')
      Git.run(rootFullPath, ['add', 'tools/value.ts'])
      const indexTree: string = Git.run(rootFullPath, ['write-tree'])
      writeFileSync(join(rootFullPath, 'tools/value.ts'), 'export const result = 43\n')
      await Promise.all([
        RequirementsSources.generate(rootFullPath, '_build/head/needs.json', Option.some('HEAD')),
        RequirementsSources.generate(
          rootFullPath,
          '_build/index/needs.json',
          Option.some(indexTree),
        ),
        RequirementsSources.generate(rootFullPath, '_build/worktree/needs.json', Option.none()),
      ])
      const head: SourceContext = RequirementsSourcesTest.context(
        rootFullPath,
        '_build/head/needs.json',
      )
      const index: SourceContext = RequirementsSourcesTest.context(
        rootFullPath,
        '_build/index/needs.json',
      )
      const worktree: SourceContext = RequirementsSourcesTest.context(
        rootFullPath,
        '_build/worktree/needs.json',
      )
      assert.ok(head.anchor.body.includes('return 17'))
      assert.ok(index.anchor.body.includes('\n   \n   export const result = 29\n   \n'))
      assert.ok(worktree.anchor.body.includes('export const result = 43'))
      assert.notEqual(head.fingerprint, index.fingerprint)
      assert.notEqual(index.fingerprint, worktree.fingerprint)
      assert.equal(await RequirementsCli.run(rootFullPath, ['verify', 'HEAD']), 1)
      assert.equal(await RequirementsCli.run(rootFullPath, ['verify', 'index']), 1)
      assert.equal(Git.run(rootFullPath, ['write-tree']), indexTree)
    })
  }
}
