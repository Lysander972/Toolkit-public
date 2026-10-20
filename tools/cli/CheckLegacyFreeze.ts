import { Option } from 'effect'
import { z } from 'zod'
import type { ReadonlyDeep } from 'type-fest'
import { Git } from './Git.ts'
import type { ArchitectureDeclarations } from '../governance/derive-rules.ts'

/** @anchor legacy-freeze */
type FreezeStatus = 'A' | 'D' | 'M' | 'T'

/** @anchor legacy-freeze */
interface FrozenChange {
  readonly status: FreezeStatus
  readonly path: string
}

/** @anchor legacy-freeze */
export class CheckLegacyFreeze {
  private static readonly scope: string = 'legacy'
  private static readonly statusSchema: z.ZodType<FreezeStatus> = z.enum(['A', 'D', 'M', 'T'])
  private static readonly verbs: Readonly<Record<FreezeStatus, string>> = {
    A: '新增',
    D: '删除',
    M: '修改',
    T: '修改',
  }

  private constructor() {}

  /**
   * @anchor legacy-freeze
   * @fallback 历史尚未包含参考区时，受检树本身就是首次引入的基线。
   */
  public static verify(repositoryFullPath: string, target: string): readonly string[] {
    const revision: string =
      Git.run(repositoryFullPath, [
        'rev-list',
        '--first-parent',
        '--reverse',
        'HEAD',
        '--',
        CheckLegacyFreeze.scope,
      ])
        .split('\n')
        .filter((entry: string): boolean => entry.length > 0)[0] ?? target
    const output: string = Git.run(repositoryFullPath, [
      'diff',
      '--name-status',
      '--no-renames',
      '-z',
      revision,
      target,
      '--',
      CheckLegacyFreeze.scope,
    ])
    const tokens: readonly string[] = output
      .split('\0')
      .filter((entry: string): boolean => entry.length > 0)
    const changes: FrozenChange[] = []
    for (let index: number = 0; index < tokens.length; index += 2) {
      changes.push({
        status: CheckLegacyFreeze.statusSchema.parse(tokens[index]),
        path: z
          .string()
          .min(1)
          .parse(tokens[index + 1]),
      })
    }
    return changes.map(
      (change: FrozenChange): string =>
        `${change.path}: 旧版参考区已冻结，禁止${CheckLegacyFreeze.verbs[change.status]}（基线 ${revision}）`,
    )
  }

  /**
   * @anchor legacy-freeze
   */
  public static check(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
    target: string,
  ): number {
    const violations: readonly string[] = Option.match(model.legacyFreeze, {
      onSome: (): readonly string[] => CheckLegacyFreeze.verify(repositoryFullPath, target),
      onNone: (): readonly string[] =>
        CheckLegacyFreeze.frozenPaths(repositoryFullPath, target).length > 0
          ? [
              `${CheckLegacyFreeze.scope}: 受检树存在旧版参考区文件但模型未声明冻结基线（metadata freeze）`,
            ]
          : [],
    })
    for (const violation of violations) {
      process.stderr.write(violation + '\n')
    }
    return violations.length === 0 ? 0 : 1
  }

  /** @anchor legacy-freeze */
  private static frozenPaths(repositoryFullPath: string, target: string): readonly string[] {
    return Git.run(repositoryFullPath, [
      'ls-tree',
      '-r',
      '--name-only',
      '-z',
      target,
      '--',
      CheckLegacyFreeze.scope,
    ])
      .split('\0')
      .filter((entry: string): boolean => entry.length > 0)
  }
}
