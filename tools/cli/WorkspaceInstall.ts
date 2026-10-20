import { Git } from './Git.ts'

export class WorkspaceInstall {
  private constructor() {}

  public static run(repositoryFullPath: string): number {
    for (const args of [
      ['config', 'core.hooksPath', 'tools/git-hooks'],
      ['config', 'push.recurseSubmodules', 'on-demand'],
      ['config', 'fetch.recurseSubmodules', 'on-demand'],
    ])
      Git.run(repositoryFullPath, args)
    return 0
  }
}
