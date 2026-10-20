import { z } from 'zod'
import { Git } from './Git.ts'

/** @anchor architecture-files */
export interface WorkspaceTreeEntry {
  readonly mode: string
  readonly kind: string
  readonly object: string
  readonly relativePath: string
}

/** @anchor architecture-files */
export class WorkspaceTree {
  private constructor() {}

  /**
   * @anchor architecture-files
   * @fallback Git 的空 revision 表示当前 index，使用 ls-files --stage 读取，供只读 inspect 使用。
   */
  public static list(
    repositoryFullPath: string,
    revision: string,
    relativePaths: readonly string[],
  ): readonly WorkspaceTreeEntry[] {
    if (revision.length === 0) return WorkspaceTree.index(repositoryFullPath, relativePaths)
    const fields: string[] = Git.run(repositoryFullPath, [
      'ls-tree',
      '-r',
      '-z',
      '--format=%(objectmode)%x00%(objecttype)%x00%(objectname)%x00%(path)',
      revision,
      '--',
      ...relativePaths,
    ]).split('\0')
    fields.pop()
    const entrySchema: z.ZodType<[string, 'blob' | 'commit', string, string]> = z.tuple([
      z.string().min(1),
      z.enum(['blob', 'commit']),
      z.string().min(1),
      z.string().min(1),
    ])
    const entries: WorkspaceTreeEntry[] = []
    for (let index: number = 0; index < fields.length; index += 4) {
      const [mode, kind, object, relativePath]: [string, 'blob' | 'commit', string, string] =
        entrySchema.parse(fields.slice(index, index + 4))
      entries.push({ mode, kind, object, relativePath })
    }
    return entries
  }

  /** @anchor architecture-files */
  private static index(
    repositoryFullPath: string,
    relativePaths: readonly string[],
  ): readonly WorkspaceTreeEntry[] {
    const entries: readonly string[] = Git.run(repositoryFullPath, [
      'ls-files',
      '--stage',
      '-z',
      '--',
      ...relativePaths,
    ])
      .split('\0')
      .filter(Boolean)
    return entries.map((entry: string): WorkspaceTreeEntry => {
      const [metadata, relativePath]: [string, string] = z
        .tuple([z.string(), z.string()])
        .parse(entry.split('\t'))
      const [mode, object, stage]: [string, string, number] = z
        .tuple([z.string(), z.string(), z.coerce.number().int()])
        .parse(metadata.split(' '))
      if (stage !== 0) throw new Error('index 存在未解决的冲突：' + relativePath)
      return { mode, object, kind: mode === '160000' ? 'commit' : 'blob', relativePath }
    })
  }
}
