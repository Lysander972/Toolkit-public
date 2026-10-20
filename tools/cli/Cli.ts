import { Option } from 'effect'
import { z } from 'zod'
import { CommitMessageGuard } from './HookGuards.ts'
import { Git } from './Git.ts'
import type { ArchitectureDeclarations } from '../governance/derive-rules.ts'

/** @anchor cli-boundary */
export class Cli {
  private constructor() {}

  /** @anchor cli-boundary */
  public static async run(
    args: readonly string[],
    repositoryFullPath: string = Git.repositoryRoot(),
  ): Promise<number> {
    const command: string = z
      .enum([
        'check',
        'architecture',
        'check:schemas',
        'commit-check',
        'commit',
        'submodules',
        'gitlinks',
        'workspace',
        'install',
        'requirements',
        'message',
        'metrics',
        'pull-request-check',
      ])
      .parse(args[0])
    if (command === 'requirements') {
      const { RequirementsCli }: typeof import('./RequirementsCli.ts') =
        await import('./RequirementsCli.ts')
      return RequirementsCli.run(repositoryFullPath, args.slice(1))
    }
    if (command === 'install') {
      if (args.length !== 1) throw new Error('install 不接受额外参数')
      const { WorkspaceInstall }: typeof import('./WorkspaceInstall.ts') =
        await import('./WorkspaceInstall.ts')
      return WorkspaceInstall.run(repositoryFullPath)
    }
    if (command === 'metrics') {
      const { TestMetrics }: typeof import('./TestMetrics.ts') = await import('./TestMetrics.ts')
      return TestMetrics.run(repositoryFullPath, args.slice(1))
    }
    if (command === 'message') {
      if (args.length !== 2) throw new Error('message 需要消息文件路径')
      return CommitMessageGuard.run(repositoryFullPath, z.string().min(1).parse(args[1]))
    }
    if (command === 'pull-request-check') {
      if (args.length !== 2) throw new Error('pull-request-check 需要 GitHub event JSON 路径')
      const { PullRequestFormat }: typeof import('./PullRequestFormat.ts') =
        await import('./PullRequestFormat.ts')
      return PullRequestFormat.run(z.string().min(1).parse(args[1]))
    }
    if (args.length !== 1) throw new Error(command + ' 不接受额外参数')
    if (command === 'check:schemas') {
      const { CheckJsonSchemas }: typeof import('./CheckJsonSchemas.ts') =
        await import('./CheckJsonSchemas.ts')
      return CheckJsonSchemas.run(repositoryFullPath)
    }
    if (command === 'submodules' || command === 'gitlinks' || command === 'workspace') {
      const { CheckGitlink }: typeof import('./CheckGitlink.ts') = await import('./CheckGitlink.ts')
      if (command === 'submodules') return CheckGitlink.synchronize(repositoryFullPath)
      const result: number = CheckGitlink.maintain(repositoryFullPath)
      return result
    }
    if (command === 'check' || command === 'commit-check' || command === 'commit') {
      const { CommitCheck }: typeof import('./CommitCheck.ts') = await import('./CommitCheck.ts')
      if (command === 'check') return CommitCheck.check(repositoryFullPath)
      if (command === 'commit') return CommitCheck.index(repositoryFullPath)
      return CommitCheck.gate(repositoryFullPath)
    }
    const { ArchitectureProject }: typeof import('./ArchitectureProject.ts') =
      await import('./ArchitectureProject.ts')
    const model: ArchitectureDeclarations = await ArchitectureProject.load(
      repositoryFullPath,
      Option.none(),
    )
    if (command === 'architecture') {
      const { CheckArchitecture }: typeof import('./CheckArchitecture.ts') =
        await import('./CheckArchitecture.ts')
      const files: number = CheckArchitecture.files(model, repositoryFullPath)
      const dependencies: number = await CheckArchitecture.run(model, repositoryFullPath)
      return files === 0 && dependencies === 0 ? 0 : 1
    }
    throw new Error('未知命令：' + command)
  }
}
