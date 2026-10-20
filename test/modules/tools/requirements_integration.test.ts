import assert from 'node:assert/strict'
import { execFileSync, spawnSync } from 'node:child_process'
import type { SpawnSyncReturns } from 'node:child_process'
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { afterEach, test } from 'vitest'
import { z } from 'zod'
import { Git } from '../../../tools/cli/Git.ts'
import { RequirementsCli } from '../../../tools/cli/RequirementsCli.ts'

type ReviewMutation = '原文' | '关系' | '标准' | '低分'
type GraphMutation = '断链' | '无有效解读' | '非法 UUID'

/** @anchor cli-boundary */
export class RequirementsIntegrationTest {
  private constructor() {}

  private static readonly sourceId: string = '638c682e-d34a-4136-8e59-44ec8e540131'
  private static readonly interpretationId: string = 'c20c846a-557e-4fe7-8ed0-ba72f195689e'
  private static readonly projects: string[] = []

  private static project(): string {
    mkdirSync(resolve('_build/requirements/tests'), { recursive: true })
    const projectFullPath: string = mkdtempSync(resolve('_build/requirements/tests/native-'))
    RequirementsIntegrationTest.projects.push(projectFullPath)
    for (const name of ['ubproject.toml', 'schemas.json', 'quality.toml']) {
      copyFileSync(join('requirements', name), join(projectFullPath, name))
    }
    writeFileSync(
      join(projectFullPath, 'index.rst'),
      `Fixture\n=======\n\n.. source:: Original\n   :id: ${RequirementsIntegrationTest.sourceId}\n   :status: active\n\n   Original complete body.\n\n.. interpretation:: Interpretation\n   :id: ${RequirementsIntegrationTest.interpretationId}\n   :status: active\n   :implements: ${RequirementsIntegrationTest.sourceId}\n\n   Interpretation complete body.\n`,
    )
    return projectFullPath
  }

  private static executable(): string {
    return resolve(
      '_build/requirements',
      process.platform === 'win32' ? 'ubc-windows-x64-0.37.0.exe' : 'ubc-linux-x64-0.37.0',
    )
  }

  static {
    afterEach((): void => {
      for (const projectFullPath of RequirementsIntegrationTest.projects.splice(0)) {
        rmSync(projectFullPath, { recursive: true })
      }
    })

    test('同一工作区并发准备使用原生锁，保持依赖锁文件不变', async (): Promise<void> => {
      const repositoryFullPath: string = Git.repositoryRoot()
      const lockBefore: Buffer = readFileSync(join(repositoryFullPath, 'requirements/uv.lock'))
      const results: readonly number[] = await Promise.all([
        RequirementsCli.run(repositoryFullPath, ['prepare']),
        RequirementsCli.run(repositoryFullPath, ['prepare']),
      ])
      assert.deepEqual(results, [0, 0])
      assert.deepEqual(readFileSync(join(repositoryFullPath, 'requirements/uv.lock')), lockBefore)
    })

    test('Git 差异区分暂存原文与工作树变化，原生影响分析给出关联', async (): Promise<void> => {
      const projectFullPath: string = RequirementsIntegrationTest.project()
      const requirementsFullPath: string = join(projectFullPath, 'requirements')
      mkdirSync(requirementsFullPath)
      for (const name of ['index.rst', 'ubproject.toml', 'schemas.json', 'quality.toml']) {
        copyFileSync(join(projectFullPath, name), join(requirementsFullPath, name))
      }
      Git.run(projectFullPath, ['init', '--initial-branch=main'])
      Git.run(projectFullPath, ['config', 'user.name', 'Requirements Fixture'])
      Git.run(projectFullPath, ['config', 'user.email', 'requirements@example.invalid'])
      Git.run(projectFullPath, ['config', 'core.autocrlf', 'false'])
      Git.run(projectFullPath, ['add', '--', 'requirements'])
      Git.run(projectFullPath, [
        '-c',
        'commit.gpgsign=false',
        'commit',
        '-m',
        'test: original requirements',
      ])
      const sourceFullPath: string = join(requirementsFullPath, 'index.rst')
      const initial: string = readFileSync(sourceFullPath, 'utf8')
      writeFileSync(
        sourceFullPath,
        initial.replace('Original complete body.', 'Staged original correction.'),
      )
      Git.run(projectFullPath, ['add', '--', 'requirements/index.rst'])
      writeFileSync(
        sourceFullPath,
        initial.replace('Original complete body.', 'Unstaged original correction.'),
      )
      assert.equal(
        await RequirementsCli.run(projectFullPath, ['diff', '--staged', '--output=staged.diff']),
        0,
      )
      const difference: string = readFileSync(join(projectFullPath, 'staged.diff'), 'utf8')
      assert.ok(difference.includes('Staged original correction.'), difference)
      assert.ok(!difference.includes('Unstaged original correction.'), difference)
      const impact: { reached: { id: string }[] } = z
        .object({ reached: z.array(z.object({ id: z.string() })) })
        .parse(
          JSON.parse(
            execFileSync(
              RequirementsIntegrationTest.executable(),
              ['agent', 'impact', '--id', RequirementsIntegrationTest.sourceId],
              { cwd: requirementsFullPath, encoding: 'utf8', windowsHide: true },
            ),
          ),
        )
      assert.ok(
        impact.reached.some(
          (need: { id: string }): boolean =>
            need.id === RequirementsIntegrationTest.interpretationId,
        ),
      )
    })

    test('原文有且仅有一个有效解读，新增第二个有效解读被原生 schema 拒绝', (): void => {
      const projectFullPath: string = RequirementsIntegrationTest.project()
      const initial: SpawnSyncReturns<string> = spawnSync(
        RequirementsIntegrationTest.executable(),
        ['check', '--deny', 'warning'],
        { cwd: projectFullPath, encoding: 'utf8', windowsHide: true },
      )
      assert.equal(initial.status, 0, initial.stdout + initial.stderr)
      writeFileSync(
        join(projectFullPath, 'second.rst'),
        `.. interpretation:: Duplicate\n   :id: b616041f-b3d5-4fad-80eb-9de1d4b206ce\n   :status: active\n   :implements: ${RequirementsIntegrationTest.sourceId}\n\n   Conflicting interpretation.\n`,
      )
      const duplicated: SpawnSyncReturns<string> = spawnSync(
        RequirementsIntegrationTest.executable(),
        ['check', '--deny', 'warning'],
        { cwd: projectFullPath, encoding: 'utf8', windowsHide: true },
      )
      assert.notEqual(duplicated.status, 0)
      assert.ok((duplicated.stdout + duplicated.stderr).includes('one-active-interpretation'))
    })

    test.each<GraphMutation>(['断链', '无有效解读', '非法 UUID'])(
      '原生图检查拒绝%s',
      (change: GraphMutation): void => {
        const projectFullPath: string = RequirementsIntegrationTest.project()
        const sourceFullPath: string = join(projectFullPath, 'index.rst')
        const original: string = readFileSync(sourceFullPath, 'utf8')
        const changes: Record<GraphMutation, string> = {
          断链: original.replace(
            ':implements: ' + RequirementsIntegrationTest.sourceId,
            ':implements: 153c41b5-6127-4a42-91e0-5f3261d62a12',
          ),
          无有效解读: original.replace(
            ':status: active\n   :implements:',
            ':status: retired\n   :implements:',
          ),
          '非法 UUID': original.replaceAll(
            RequirementsIntegrationTest.interpretationId,
            'invalid-id',
          ),
        }
        writeFileSync(sourceFullPath, changes[change])
        const rejected: SpawnSyncReturns<string> = spawnSync(
          RequirementsIntegrationTest.executable(),
          ['check', '--deny', 'warning'],
          { cwd: projectFullPath, encoding: 'utf8', windowsHide: true },
        )
        assert.notEqual(rejected.status, 0, rejected.stdout + rejected.stderr)
      },
    )

    test('原生 context 如实返回原文关系 ID', (): void => {
      const projectFullPath: string = RequirementsIntegrationTest.project()
      const context: {
        links: { implements: string[] }
        related: { id: string; body: string }[]
        fingerprint: string
      } = z
        .object({
          links: z.object({ implements: z.array(z.string()) }),
          related: z.array(z.object({ id: z.string(), body: z.string() })),
          fingerprint: z.string(),
        })
        .parse(
          JSON.parse(
            execFileSync(
              RequirementsIntegrationTest.executable(),
              ['agent', 'context', '--id', RequirementsIntegrationTest.interpretationId],
              { cwd: projectFullPath, encoding: 'utf8', windowsHide: true },
            ),
          ),
        )
      assert.deepEqual(context.links.implements, [RequirementsIntegrationTest.sourceId])
      assert.deepEqual(context.related, [
        { id: RequirementsIntegrationTest.sourceId, body: 'Original complete body.' },
      ])
      const sourceFullPath: string = join(projectFullPath, 'index.rst')
      writeFileSync(
        sourceFullPath,
        readFileSync(sourceFullPath, 'utf8').replace(
          'Original complete body.',
          'Original corrected body.',
        ),
      )
      const changed: { fingerprint: string } = z
        .object({ fingerprint: z.string() })
        .parse(
          JSON.parse(
            execFileSync(
              RequirementsIntegrationTest.executable(),
              ['agent', 'context', '--id', RequirementsIntegrationTest.interpretationId],
              { cwd: projectFullPath, encoding: 'utf8', windowsHide: true },
            ),
          ),
        )
      assert.notEqual(changed.fingerprint, context.fingerprint)
    })

    test.each<ReviewMutation>(['原文', '关系', '标准', '低分'])(
      '原生评分门禁：%s变化',
      (change: ReviewMutation): void => {
        const projectFullPath: string = RequirementsIntegrationTest.project()
        const briefing: {
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
              execFileSync(
                RequirementsIntegrationTest.executable(),
                ['agent', 'review-brief', 'interpretation'],
                { cwd: projectFullPath, encoding: 'utf8', windowsHide: true },
              ),
            ),
          )
        const brief: { fingerprints: Record<string, string>; criteria: { fingerprint: string } } = z
          .object({
            fingerprints: z.record(z.string(), z.string()),
            criteria: z.object({ fingerprint: z.string() }),
          })
          .parse(briefing.briefs.at(0)?.review_brief)
        const fingerprint: string = z
          .string()
          .parse(brief.fingerprints[RequirementsIntegrationTest.interpretationId])
        const submission: string = JSON.stringify({
          axes: {
            parent_fit: {
              score: 1,
              reason: 'Fixture relates to its complete original.',
              suggestion: 'Fixture score is deliberately below full marks.',
            },
            reasoning: {
              score: 1,
              reason: 'Fixture records a direct interpretation.',
              suggestion: 'Add detailed reasoning in real records.',
            },
            evidence: {
              score: 1,
              reason: 'Fixture source and relation are present.',
              suggestion: 'Real implementation evidence is outside this fixture.',
            },
          },
        })
        const submitArguments: string[] = [
          'agent',
          'verdict-submit',
          RequirementsIntegrationTest.interpretationId,
          '--file',
          '-',
          '--fingerprint',
          fingerprint,
          '--criteria-fingerprint',
          brief.criteria.fingerprint,
        ]
        execFileSync(RequirementsIntegrationTest.executable(), submitArguments, {
          cwd: projectFullPath,
          encoding: 'utf8',
          input: submission,
          windowsHide: true,
        })
        execFileSync(RequirementsIntegrationTest.executable(), ['agent', 'verdict-check'], {
          cwd: projectFullPath,
          encoding: 'utf8',
          windowsHide: true,
        })
        const warnings: readonly string[] = RequirementsCli.warnings(projectFullPath)
        assert.equal(warnings.length, 3)
        assert.ok(
          warnings.some(
            (warning: string): boolean =>
              warning.includes('1/2') &&
              warning.includes('Add detailed reasoning in real records.'),
          ),
        )
        const sourceFullPath: string = join(projectFullPath, 'index.rst')
        switch (change) {
          case '原文':
            writeFileSync(
              sourceFullPath,
              readFileSync(sourceFullPath, 'utf8').replace(
                'Original complete body.',
                'Changed source obligation.',
              ),
            )
            break
          case '关系':
            writeFileSync(
              sourceFullPath,
              readFileSync(sourceFullPath, 'utf8').replaceAll(
                RequirementsIntegrationTest.sourceId,
                '153c41b5-6127-4a42-91e0-5f3261d62a12',
              ),
            )
            break
          case '标准': {
            const criteriaFullPath: string = join(projectFullPath, 'quality.toml')
            writeFileSync(
              criteriaFullPath,
              readFileSync(criteriaFullPath, 'utf8').replace('与上游一致', '与上游逐项完整一致'),
            )
            break
          }
          case '低分':
            execFileSync(RequirementsIntegrationTest.executable(), submitArguments, {
              cwd: projectFullPath,
              encoding: 'utf8',
              input: submission.replaceAll('"score":1', '"score":0'),
              windowsHide: true,
            })
            break
        }
        const expired: SpawnSyncReturns<string> = spawnSync(
          RequirementsIntegrationTest.executable(),
          ['agent', 'verdict-check'],
          { cwd: projectFullPath, encoding: 'utf8', windowsHide: true },
        )
        assert.notEqual(expired.status, 0)
        const report: { outdated: string[]; failing: string[] } = z
          .object({ outdated: z.array(z.string()), failing: z.array(z.string()) })
          .parse(JSON.parse(expired.stdout))
        assert.deepEqual(change === '低分' ? report.failing : report.outdated, [
          RequirementsIntegrationTest.interpretationId,
        ])
        if (change === '低分') return
        const staleSubmit: SpawnSyncReturns<string> = spawnSync(
          RequirementsIntegrationTest.executable(),
          submitArguments,
          { cwd: projectFullPath, encoding: 'utf8', input: submission, windowsHide: true },
        )
        assert.notEqual(staleSubmit.status, 0)
      },
    )
  }
}
