import { execFileSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { Option } from 'effect'
import { z } from 'zod'
import { HostProcess } from './HostProcess.ts'
import type { HostLaunch } from './HostProcess.ts'
import { WorkspaceFiles } from './WorkspaceFiles.ts'
import { ArchitectureFiles } from '../governance/architecture-files.ts'
import type { ArchitectureDeclarations } from '../governance/derive-rules.ts'
import type { ReadonlyDeep } from 'type-fest'

/** @anchor deps-config-inheritance */
export interface ManifestBoundary {
  readonly relativePath: string
  readonly packageManager: Option.Option<string>
  readonly bypassKeys: readonly string[]
}

/** @anchor deps-config-inheritance */
export interface TypeScriptConfigBoundary {
  readonly relativePath: string
  readonly extendsReference: Option.Option<string>
  readonly ownCompilerOptions: boolean
}

/** @anchor deps-config-inheritance */
interface WorkspaceMember {
  readonly path: string
}

/** @anchor deps-config-inheritance */
interface ManifestShape {
  readonly packageManager: Option.Option<string>
  readonly resolutions: Option.Option<unknown>
  readonly overrides: Option.Option<unknown>
  readonly pnpm: Option.Option<unknown>
}

/** @anchor deps-config-inheritance */
interface TypeScriptConfigShape {
  readonly extends: Option.Option<string>
  readonly compilerOptions: Option.Option<unknown>
}

/** @anchor deps-config-inheritance */
export class CheckWorkspaceHygiene {
  private static readonly rootManifest: string = 'package.json'
  private static readonly rootLockFile: string = 'pnpm-lock.yaml'
  private static readonly authority: string = 'tsconfig.base.json'
  private static readonly activePrefix: string = 'src/'
  private static readonly lockFiles: readonly string[] = [
    'pnpm-lock.yaml',
    'npm-shrinkwrap.json',
    'package-lock.json',
    'yarn.lock',
    'bun.lockb',
    'bun.lock',
  ]
  private static readonly npmHiddenLock: string = 'node_modules/.package-lock.json'

  private constructor() {}

  /** @anchor deps-config-inheritance */
  public static verify(
    entries: readonly string[],
    manifests: readonly ManifestBoundary[],
    typeScriptConfigs: readonly TypeScriptConfigBoundary[],
  ): readonly string[] {
    return [
      ...CheckWorkspaceHygiene.npmrc(entries),
      ...CheckWorkspaceHygiene.locks(entries),
      ...CheckWorkspaceHygiene.nodeModules(entries),
      ...CheckWorkspaceHygiene.manifestBoundary(manifests),
      ...CheckWorkspaceHygiene.typeScriptConfigBoundary(typeScriptConfigs),
    ]
  }

  /** @anchor deps-config-inheritance */
  public static check(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
  ): number {
    const entries: readonly string[] = WorkspaceFiles.all(repositoryFullPath)
    const maintained: readonly string[] = entries.filter((relativePath: string): boolean =>
      model.elements.some(
        (element: ReadonlyDeep<ArchitectureDeclarations>['elements'][number]): boolean =>
          element.purpose !== 'doc' &&
          element.files.some((declaration: string): boolean =>
            ArchitectureFiles.matches(relativePath, declaration),
          ),
      ),
    )
    const violations: readonly string[] = [
      ...CheckWorkspaceHygiene.verify(
        entries,
        maintained
          .filter((relativePath: string): boolean => relativePath.endsWith('package.json'))
          .map((relativePath: string): ManifestBoundary =>
            CheckWorkspaceHygiene.manifest(repositoryFullPath, relativePath),
          ),
        CheckWorkspaceHygiene.typeScriptConfigs(model, repositoryFullPath, entries),
      ),
      ...CheckWorkspaceHygiene.forkedInstallArtifacts(repositoryFullPath, maintained),
      ...CheckWorkspaceHygiene.danglingPackages(repositoryFullPath, entries),
    ]
    for (const violation of violations) {
      process.stderr.write(violation + '\n')
    }
    return violations.length === 0 ? 0 : 1
  }

  /** @anchor deps-config-inheritance */
  private static manifest(repositoryFullPath: string, relativePath: string): ManifestBoundary {
    const parsed: ManifestShape = z
      .object({
        packageManager: z.string().optional().transform(Option.fromNullishOr),
        resolutions: z.unknown().optional().transform(Option.fromNullishOr),
        overrides: z.unknown().optional().transform(Option.fromNullishOr),
        pnpm: z.unknown().optional().transform(Option.fromNullishOr),
      })
      .parse(JSON.parse(readFileSync(join(repositoryFullPath, relativePath), 'utf8')))
    const present: string[] = []
    if (Option.isSome(parsed.resolutions)) present.push('resolutions')
    if (Option.isSome(parsed.overrides)) present.push('overrides')
    if (Option.isSome(parsed.pnpm)) present.push('pnpm')
    return { relativePath, packageManager: parsed.packageManager, bypassKeys: present }
  }

  /** @anchor deps-config-inheritance */
  private static npmrc(entries: readonly string[]): readonly string[] {
    return entries
      .filter((relativePath: string): boolean => relativePath.endsWith('.npmrc'))
      .map(
        (relativePath: string): string =>
          `${relativePath}: 禁止任何层级 .npmrc；registry 等配置唯一写根 pnpm-workspace.yaml`,
      )
  }

  /** @anchor deps-config-inheritance */
  private static locks(entries: readonly string[]): readonly string[] {
    return entries
      .filter((relativePath: string): boolean =>
        CheckWorkspaceHygiene.lockFiles.some(
          (lockFile: string): boolean =>
            relativePath === lockFile || relativePath.endsWith('/' + lockFile),
        ),
      )
      .filter(
        (relativePath: string): boolean => relativePath !== CheckWorkspaceHygiene.rootLockFile,
      )
      .map(
        (relativePath: string): string =>
          `${relativePath}: 锁文件唯一为根 pnpm-lock.yaml，禁止第二锁文件`,
      )
  }

  /** @anchor deps-artifacts-quarantine */
  private static nodeModules(entries: readonly string[]): readonly string[] {
    return entries
      .filter((relativePath: string): boolean => relativePath.split('/').includes('node_modules'))
      .map(
        (relativePath: string): string =>
          `${relativePath}: node_modules 为安装产物，禁止出现在工作区文件清单`,
      )
  }

  /** @anchor deps-config-inheritance */
  private static manifestBoundary(manifests: readonly ManifestBoundary[]): readonly string[] {
    const violations: string[] = manifests
      .filter(
        (manifest: ManifestBoundary): boolean =>
          manifest.relativePath !== CheckWorkspaceHygiene.rootManifest &&
          Option.isSome(manifest.packageManager),
      )
      .map(
        (manifest: ManifestBoundary): string =>
          `${manifest.relativePath}: packageManager 只允许根 package.json 声明，成员包必须继承`,
      )
    for (const manifest of manifests) {
      for (const key of manifest.bypassKeys) {
        violations.push(`${manifest.relativePath}: 禁止 ${key} 旁路依赖解析；依赖约束唯一写根配置`)
      }
    }
    return violations
  }

  /** @anchor deps-config-inheritance */
  private static typeScriptConfigBoundary(
    configs: readonly TypeScriptConfigBoundary[],
  ): readonly string[] {
    const violations: string[] = []
    for (const config of configs) {
      if (Option.isNone(config.extendsReference)) {
        violations.push(
          `${config.relativePath}: tsconfig 必须显式 extends 继承 ${CheckWorkspaceHygiene.authority}，禁止自立门户`,
        )
      }
      if (config.ownCompilerOptions) {
        violations.push(
          `${config.relativePath}: 禁止局部 compilerOptions 旁路；编译选项唯一属于 ${CheckWorkspaceHygiene.authority}`,
        )
      }
    }
    return violations
  }

  /** @anchor deps-config-inheritance */
  private static typeScriptConfigs(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
    entries: readonly string[],
  ): readonly TypeScriptConfigBoundary[] {
    const active: ReadonlySet<string> = new Set(
      model.elements
        .filter(
          (element: ReadonlyDeep<ArchitectureDeclarations>['elements'][number]): boolean =>
            element.kind !== 'document',
        )
        .flatMap(
          (
            element: ReadonlyDeep<ArchitectureDeclarations>['elements'][number],
          ): readonly string[] => element.files,
        ),
    )
    return entries
      .filter(
        (relativePath: string): boolean =>
          active.has(relativePath) &&
          relativePath.endsWith('tsconfig.json') &&
          relativePath !== CheckWorkspaceHygiene.authority,
      )
      .map((relativePath: string): TypeScriptConfigBoundary => {
        const parsed: TypeScriptConfigShape = z
          .object({
            extends: z.string().optional().transform(Option.fromNullishOr),
            compilerOptions: z.unknown().optional().transform(Option.fromNullishOr),
          })
          .parse(JSON.parse(readFileSync(join(repositoryFullPath, relativePath), 'utf8')))
        return {
          relativePath,
          extendsReference: parsed.extends,
          ownCompilerOptions: Option.isSome(parsed.compilerOptions),
        }
      })
  }

  /** @anchor deps-artifacts-quarantine */
  public static forkedInstallArtifacts(
    repositoryFullPath: string,
    entries: readonly string[],
  ): readonly string[] {
    const violations: string[] = []
    for (const relativePath of entries.filter((entry: string): boolean =>
      entry.endsWith('package.json'),
    )) {
      for (const lockFile of CheckWorkspaceHygiene.lockFiles) {
        const candidate: string = CheckWorkspaceHygiene.besideManifest(relativePath, lockFile)
        if (
          candidate !== CheckWorkspaceHygiene.rootLockFile &&
          existsSync(join(repositoryFullPath, candidate))
        ) {
          violations.push(`${candidate}: 分裂锁文件；锁文件唯一为根 pnpm-lock.yaml`)
        }
      }
      const hiddenLock: string = CheckWorkspaceHygiene.besideManifest(
        relativePath,
        CheckWorkspaceHygiene.npmHiddenLock,
      )
      if (existsSync(join(repositoryFullPath, hiddenLock))) {
        violations.push(`${hiddenLock}: npm 手搓隐藏锁；包管理唯一 pnpm`)
      }
    }
    return violations
  }

  /**
   * manifest 路径以 package.json 结尾，砍掉尾部即得目录前缀（根为空段，成员包带 /），直接拼接。
   * @anchor deps-artifacts-quarantine
   */
  private static besideManifest(relativePath: string, fileName: string): string {
    return relativePath.slice(0, -'package.json'.length) + fileName
  }

  /** @anchor deps-config-inheritance */
  private static danglingPackages(
    repositoryFullPath: string,
    entries: readonly string[],
  ): readonly string[] {
    const memberSchema: z.ZodType<readonly WorkspaceMember[]> = z.array(
      z.object({ path: z.string().min(1) }),
    )
    const launch: HostLaunch = HostProcess.pnpm(process.platform, [
      'ls',
      '-r',
      '--depth',
      '-1',
      '--json',
    ])
    const members: readonly WorkspaceMember[] = memberSchema.parse(
      JSON.parse(
        execFileSync(launch.file, launch.args, {
          cwd: repositoryFullPath,
          encoding: 'utf8',
          windowsHide: launch.windowsHide,
        }),
      ),
    )
    const memberPaths: ReadonlySet<string> = new Set(
      members.map((member: WorkspaceMember): string =>
        relative(repositoryFullPath, member.path).replaceAll('\\', '/'),
      ),
    )
    return entries
      .filter(
        (relativePath: string): boolean =>
          relativePath.startsWith(CheckWorkspaceHygiene.activePrefix) &&
          relativePath.endsWith('package.json'),
      )
      .filter(
        (relativePath: string): boolean =>
          !memberPaths.has(relativePath.slice(0, relativePath.lastIndexOf('/'))),
      )
      .map(
        (relativePath: string): string =>
          `${relativePath}: 不在 pnpm-workspace 注册范围内，禁止游离包`,
      )
  }
}
