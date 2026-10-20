import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { Option } from 'effect'
import { Git } from './Git.ts'
import { WorkspaceFiles } from './WorkspaceFiles.ts'
import { WorkspaceTree } from './WorkspaceTree.ts'
import { ArchitectureRules } from '../governance/derive-rules.ts'
import type { ArchitectureDeclarations } from '../governance/derive-rules.ts'
import type { WorkspaceTreeEntry } from './WorkspaceTree.ts'

/** @anchor architecture-project */
export class ArchitectureProject {
  private constructor() {}

  /**
   * LikeC4 项目由 tools 目录树内的多个源文件组成。model.c4 在前，纯函数骨架其次，其余按路径排序。
   * @anchor architecture-project
   */
  public static async load(
    repositoryFullPath: string,
    revision: Option.Option<string>,
  ): Promise<ArchitectureDeclarations> {
    return ArchitectureRules.declarations(ArchitectureProject.text(repositoryFullPath, revision))
  }

  /** @anchor architecture-project */
  public static text(repositoryFullPath: string, revision: Option.Option<string>): string {
    const paths: readonly string[] = ArchitectureProject.paths(repositoryFullPath, revision)
    if (Option.isNone(revision))
      return paths
        .map((relativePath: string): string =>
          readFileSync(join(repositoryFullPath, relativePath), 'utf8'),
        )
        .join('\n')
    const objects: readonly string[] = paths.map(
      (relativePath: string): string => revision.value + ':' + relativePath,
    )
    const contents: ReadonlyMap<string, string> = Git.blobs(repositoryFullPath, objects)
    return objects
      .map((object: string): string =>
        Option.getOrThrow(Option.fromNullishOr(contents.get(object))),
      )
      .join('\n')
  }

  /**
   * @fallback revision 为 none 时读取工作区源文件，为 some 时读取该提交 blob。这是架构合并规定的两种来源。
   */
  private static paths(
    repositoryFullPath: string,
    revision: Option.Option<string>,
  ): readonly string[] {
    const discovered: readonly string[] = Option.match(revision, {
      onSome: (commit: string): readonly string[] =>
        WorkspaceTree.list(repositoryFullPath, commit, [])
          .map((entry: WorkspaceTreeEntry): string => entry.relativePath)
          .filter((relativePath: string): boolean => ArchitectureProject.isSource(relativePath)),
      onNone: (): readonly string[] =>
        WorkspaceFiles.all(repositoryFullPath).filter((relativePath: string): boolean =>
          ArchitectureProject.isSource(relativePath),
        ),
    })
    return [...discovered].sort((left: string, right: string): number =>
      ArchitectureProject.rank(left).localeCompare(ArchitectureProject.rank(right), 'en'),
    )
  }

  private static isSource(relativePath: string): boolean {
    return relativePath.startsWith('tools/') && relativePath.endsWith('.c4')
  }

  private static rank(relativePath: string): string {
    if (relativePath === 'tools/model.c4') return '0'
    if (relativePath === 'tools/pure.c4') return '1'
    return '2' + relativePath
  }
}
