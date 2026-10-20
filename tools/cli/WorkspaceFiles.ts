import { existsSync } from 'node:fs'
import { join } from 'node:path'
import { Option } from 'effect'
import { ArchitectureFiles } from '../governance/architecture-files.ts'
import { Git } from './Git.ts'
import { WorkspaceTree } from './WorkspaceTree.ts'
import type { WorkspaceTreeEntry } from './WorkspaceTree.ts'
import type {
  ArchitectureDeclarations,
  ArchitectureElement,
  ArchitecturePurpose,
} from '../governance/derive-rules.ts'
import type { ReadonlyDeep } from 'type-fest'

/** @anchor selfcheck-gate */
export class WorkspaceFiles {
  private constructor() {}

  /** @anchor selfcheck-gate */
  public static scope(
    model: ReadonlyDeep<ArchitectureDeclarations>,
  ): readonly ReadonlyDeep<ArchitectureElement>[] {
    return model.elements.filter(
      (element: ReadonlyDeep<ArchitectureElement>): boolean =>
        element.kind !== 'document' &&
        (model.codeTargets.includes(element.id) ||
          (Option.isSome(element.parentId) && element.parentId.value === model.rootElementId)),
    )
  }

  /** @anchor selfcheck-gate */
  public static domainTypeScript(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
    files: readonly string[] = WorkspaceFiles.all(repositoryFullPath),
  ): readonly string[] {
    const declarations: readonly string[] = model.containers.flatMap(
      (element: ReadonlyDeep<ArchitectureElement>): readonly string[] => element.files,
    )
    return WorkspaceFiles.select(repositoryFullPath, declarations, files).filter(
      (relativePath: string): boolean =>
        relativePath.endsWith('.ts') || relativePath.endsWith('.tsx'),
    )
  }

  /** @anchor selfcheck-gate */
  public static purposeFiles(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
    purposes: readonly ArchitecturePurpose[],
    files: readonly string[] = WorkspaceFiles.all(repositoryFullPath),
  ): readonly string[] {
    const declarations: readonly string[] = WorkspaceFiles.scope(model)
      .filter((element: ReadonlyDeep<ArchitectureElement>): boolean =>
        purposes.includes(element.purpose),
      )
      .flatMap((element: ReadonlyDeep<ArchitectureElement>): readonly string[] => element.files)
    return WorkspaceFiles.select(repositoryFullPath, declarations, files)
  }

  /** @anchor selfcheck-gate */
  public static implementationTypeScript(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
  ): readonly string[] {
    const implementations: ReadonlySet<string> = new Set(
      WorkspaceFiles.purposeFiles(model, repositoryFullPath, ['tool', 'common']),
    )
    return WorkspaceFiles.domainTypeScript(model, repositoryFullPath).filter(
      (relativePath: string): boolean => implementations.has(relativePath),
    )
  }

  /** @anchor complexity-budget */
  public static testTypeScript(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
    files: readonly string[] = WorkspaceFiles.all(repositoryFullPath),
  ): readonly string[] {
    return WorkspaceFiles.purposeFiles(model, repositoryFullPath, ['test'], files).filter(
      (relativePath: string): boolean =>
        relativePath.endsWith('.ts') || relativePath.endsWith('.tsx'),
    )
  }

  /**
   * 指定 tree 中的测试用途 TypeScript 文件，供审计过滤绑定受检输入。
   * @anchor complexity-budget
   */
  public static testTypeScriptIn(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
    revision: string,
  ): readonly string[] {
    const declarations: readonly string[] = WorkspaceFiles.scope(model)
      .filter((element: ReadonlyDeep<ArchitectureElement>): boolean => element.purpose === 'test')
      .flatMap((element: ReadonlyDeep<ArchitectureElement>): readonly string[] => element.files)
    return WorkspaceTree.list(repositoryFullPath, revision, [])
      .map((entry: WorkspaceTreeEntry): string => entry.relativePath)
      .filter(
        (relativePath: string): boolean =>
          (relativePath.endsWith('.ts') || relativePath.endsWith('.tsx')) &&
          declarations.some((declaration: string): boolean =>
            ArchitectureFiles.matches(relativePath, declaration),
          ),
      )
  }

  /** @anchor architecture-files */
  public static all(repositoryFullPath: string): readonly string[] {
    const files: string[] = Git.run(repositoryFullPath, [
      'ls-files',
      '--cached',
      '--others',
      '--exclude-standard',
      '-z',
    ]).split('\0')
    return [...new Set(files)]
      .filter(
        (relativePath: string): boolean =>
          relativePath.length > 0 && existsSync(join(repositoryFullPath, relativePath)),
      )
      .sort()
  }

  /** @anchor architecture-files */
  public static select(
    repositoryFullPath: string,
    declarations: readonly string[],
    files: readonly string[] = WorkspaceFiles.all(repositoryFullPath),
  ): readonly string[] {
    return files.filter((relativePath: string): boolean =>
      declarations.some((declaration: string): boolean =>
        ArchitectureFiles.matches(relativePath, declaration),
      ),
    )
  }

  /** @anchor execution-snapshot */
  public static requireStaged(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
  ): void {
    const unstaged: string[] = Git.run(repositoryFullPath, ['diff', '--name-only', '-z']).split(
      '\0',
    )
    const untracked: string[] = Git.run(repositoryFullPath, [
      'ls-files',
      '--others',
      '--exclude-standard',
      '-z',
    ]).split('\0')
    const changes: string[] = [...unstaged, ...untracked].filter(
      (relativePath: string): boolean =>
        relativePath.length > 0 &&
        model.guardedRelativePaths.some((declaration: string): boolean =>
          ArchitectureFiles.matches(relativePath, declaration),
        ),
    )
    if (changes.length > 0) {
      throw new Error('治理输入尚未全部暂存: ' + changes.join(', '))
    }
  }
}
