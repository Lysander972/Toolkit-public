import { mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { Option } from 'effect'
import { ArchitectureProject } from './ArchitectureProject.ts'
import { HostProcess } from './HostProcess.ts'
import { CheckArchitecture } from './CheckArchitecture.ts'
import { CheckConfiguration } from './CheckConfiguration.ts'
import { CheckGitlink } from './CheckGitlink.ts'
import { CheckLegacyFreeze } from './CheckLegacyFreeze.ts'
import { CheckTask } from './CheckTask.ts'
import { CheckTextNormalization } from './CheckTextNormalization.ts'
import { CheckWorkspaceHygiene } from './CheckWorkspaceHygiene.ts'
import { Git } from './Git.ts'
import { RequirementsCli } from './RequirementsCli.ts'
import { WorkspaceFiles } from './WorkspaceFiles.ts'
import { WorkspaceTree } from './WorkspaceTree.ts'
import { ArchitectureFiles } from '../governance/architecture-files.ts'
import type { ArchitectureDeclarations } from '../governance/derive-rules.ts'
import type { WorkspaceTreeEntry } from './WorkspaceTree.ts'
import type { ReadonlyDeep } from 'type-fest'

/** @anchor selfcheck-gate */
export interface CommitGateWork {
  /** @anchor selfcheck-gate */
  run(model: ReadonlyDeep<ArchitectureDeclarations>): Promise<number>
}

/** @anchor selfcheck-gate */
export class CommitCheck {
  private static readonly scripts: readonly string[] = [
    'check:format',
    'check:types',
    'check:lint',
    'check:dead-code',
    'requirements:check',
    'requirements:verify',
  ]

  private static readonly cacheRelativePaths: readonly string[] = ['_build/prettier', '_build/tsc']

  private constructor() {}

  /** @anchor selfcheck-gate */
  public static async check(repositoryFullPath: string): Promise<number> {
    return CommitCheck.checks(
      await ArchitectureProject.load(repositoryFullPath, Option.none()),
      repositoryFullPath,
    )
  }

  /**
   * 工作树检查通过后，对同一受检 index 执行需求图检视。
   * @anchor selfcheck-gate
   * @fallback 正式门禁未注入替代工作时执行已声明检查；测试注入工作以覆盖失败与指纹变化。
   */
  public static async gate(
    repositoryFullPath: string,
    work: Option.Option<CommitGateWork> = Option.none(),
  ): Promise<number> {
    const model: ReadonlyDeep<ArchitectureDeclarations> = await ArchitectureProject.load(
      repositoryFullPath,
      Option.none(),
    )
    WorkspaceFiles.requireStaged(model, repositoryFullPath)
    const fingerprint: string = Git.indexFingerprint(repositoryFullPath)
    const outcome: number = await Option.match(work, {
      onNone: (): Promise<number> => CommitCheck.checks(model, repositoryFullPath),
      onSome: (current: CommitGateWork): Promise<number> => current.run(model),
    })
    if (outcome !== 0) return outcome
    WorkspaceFiles.requireStaged(model, repositoryFullPath)
    if (Git.indexFingerprint(repositoryFullPath) !== fingerprint) {
      throw new Error('检查期间受检 index 已变化')
    }
    return CommitCheck.index(repositoryFullPath, Option.some(model))
  }

  /** @anchor selfcheck-gate */
  public static async checks(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
  ): Promise<number> {
    const toolCode: Promise<number> = CommitCheck.tools(repositoryFullPath)
    const governanceCode: Promise<number> = CommitCheck.governance(model, repositoryFullPath)
    const [toolResult, governanceResult]: readonly [number, number] = await Promise.all([
      toolCode,
      governanceCode,
    ])
    return governanceResult === 0 && toolResult === 0 ? 0 : 1
  }

  /** @anchor selfcheck-gate */
  public static tools(repositoryFullPath: string): Promise<number> {
    CommitCheck.cacheDirectories(repositoryFullPath)
    return Promise.all(
      CommitCheck.scripts.map((script: string): Promise<number> =>
        CommitCheck.script(repositoryFullPath, script),
      ),
    ).then((codes: readonly number[]): number =>
      codes.some((code: number): boolean => code !== 0) ? 1 : 0,
    )
  }

  /** @anchor selfcheck-gate */
  private static async governance(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
  ): Promise<number> {
    const results: readonly number[] = [
      CheckArchitecture.files(model, repositoryFullPath),
      CheckLegacyFreeze.check(model, repositoryFullPath, 'HEAD'),
      CheckWorkspaceHygiene.check(model, repositoryFullPath),
      CheckTextNormalization.check(model, repositoryFullPath),
      CheckTask.run(model, repositoryFullPath),
      ...(await Promise.all([
        CheckConfiguration.run(model, repositoryFullPath),
        CheckArchitecture.run(model, repositoryFullPath),
      ])),
    ]
    return results.some((code: number): boolean => code !== 0) ? 1 : 0
  }

  /**
   * @anchor selfcheck-gate
   * @fallback 单独的 index 入口没有前置模型时再加载工作树模型；门禁已加载时复用。
   */
  public static async index(
    repositoryFullPath: string,
    supplied: Option.Option<ReadonlyDeep<ArchitectureDeclarations>> = Option.none(),
  ): Promise<number> {
    const alternate: boolean = Git.alternateIndex(repositoryFullPath)
    const worktree: ReadonlyDeep<ArchitectureDeclarations> = Option.isSome(supplied)
      ? supplied.value
      : await ArchitectureProject.load(repositoryFullPath, Option.none())
    WorkspaceFiles.requireStaged(worktree, repositoryFullPath)
    const gitlink: number = CheckGitlink.maintain(repositoryFullPath)
    if (gitlink !== 0) return gitlink
    const revision: string = Git.run(repositoryFullPath, ['write-tree'])
    const model: ReadonlyDeep<ArchitectureDeclarations> = alternate
      ? await ArchitectureProject.load(repositoryFullPath, Option.some(revision))
      : worktree
    const paths: readonly string[] = WorkspaceTree.list(repositoryFullPath, revision, []).map(
      (entry: WorkspaceTreeEntry): string => entry.relativePath,
    )
    const violations: readonly string[] = ArchitectureFiles.check(model, paths).violations
    if (violations.length > 0) throw new Error(violations.join('\n'))
    const frozen: number = CheckLegacyFreeze.check(model, repositoryFullPath, revision)
    if (frozen !== 0) return frozen
    return RequirementsCli.run(repositoryFullPath, ['verify', 'index'])
  }

  /** @anchor selfcheck-gate */
  private static cacheDirectories(repositoryFullPath: string): void {
    for (const relativePath of CommitCheck.cacheRelativePaths) {
      mkdirSync(join(repositoryFullPath, relativePath), { recursive: true })
    }
  }

  /** @anchor selfcheck-gate */
  private static script(repositoryFullPath: string, script: string): Promise<number> {
    return HostProcess.run(
      HostProcess.pnpm(process.platform, ['--dir', repositoryFullPath, 'run', script]),
      repositoryFullPath,
    )
  }
}
