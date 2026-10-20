import { test } from 'vitest'
import assert from 'node:assert/strict'
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { Option } from 'effect'
import { CheckWorkspaceHygiene } from '../../../tools/cli/CheckWorkspaceHygiene.ts'
import type {
  ManifestBoundary,
  TypeScriptConfigBoundary,
} from '../../../tools/cli/CheckWorkspaceHygiene.ts'

/** @anchor deps-artifacts-quarantine */
export class WorkspaceHygieneHiddenLockFixture {
  private constructor() {}

  public static readonly path: string = 'node_modules/.package-lock.json'
}

/** @anchor deps-config-inheritance */
export class WorkspaceHygieneTest {
  private constructor() {}

  /** @anchor deps-config-inheritance */
  private static cleanManifests(): readonly ManifestBoundary[] {
    return [
      {
        relativePath: 'package.json',
        packageManager: Option.some('pnpm@11.1.2'),
        bypassKeys: [],
      },
      {
        relativePath: 'src/toolkit/config/package.json',
        packageManager: Option.none(),
        bypassKeys: [],
      },
    ]
  }

  /** @anchor deps-config-inheritance */
  private static cleanTypeScriptConfigs(): readonly TypeScriptConfigBoundary[] {
    return [
      {
        relativePath: 'tools/tsconfig.json',
        extendsReference: Option.some('../tsconfig.base.json'),
        ownCompilerOptions: false,
      },
      {
        relativePath: 'src/toolkit/mcp/tsconfig.json',
        extendsReference: Option.some('../tsconfig.json'),
        ownCompilerOptions: false,
      },
    ]
  }

  /** @anchor deps-config-inheritance-positive */
  public static fullInheritancePasses(this: void): void {
    const entries: readonly string[] = [
      'package.json',
      'pnpm-lock.yaml',
      'pnpm-workspace.yaml',
      'tsconfig.base.json',
      'tools/tsconfig.json',
      'src/toolkit/config/package.json',
      'src/toolkit/config/src/ConfigSource.ts',
    ]
    assert.deepEqual(
      CheckWorkspaceHygiene.verify(
        entries,
        WorkspaceHygieneTest.cleanManifests(),
        WorkspaceHygieneTest.cleanTypeScriptConfigs(),
      ),
      [],
    )
  }

  /** @anchor deps-config-inheritance */
  public static localNpmrcRejected(this: void): void {
    const violations: readonly string[] = CheckWorkspaceHygiene.verify(
      ['src/toolkit/config/.npmrc'],
      [],
      [],
    )
    assert.deepEqual(violations, [
      'src/toolkit/config/.npmrc: 禁止任何层级 .npmrc；registry 等配置唯一写根 pnpm-workspace.yaml',
    ])
  }

  /** @anchor deps-config-inheritance */
  public static forkedLockRejected(this: void): void {
    const violations: readonly string[] = CheckWorkspaceHygiene.verify(
      ['pnpm-lock.yaml', 'src/toolkit/host/pnpm-lock.yaml', 'src/toolkit/mcp/package-lock.json'],
      [],
      [],
    )
    assert.deepEqual(violations, [
      'src/toolkit/host/pnpm-lock.yaml: 锁文件唯一为根 pnpm-lock.yaml，禁止第二锁文件',
      'src/toolkit/mcp/package-lock.json: 锁文件唯一为根 pnpm-lock.yaml，禁止第二锁文件',
    ])
  }

  /** @anchor deps-artifacts-quarantine */
  public static nodeModulesRejected(this: void): void {
    const violations: readonly string[] = CheckWorkspaceHygiene.verify(
      ['node_modules/zod/package.json', 'src/toolkit/config/node_modules/zod/index.js'],
      [],
      [],
    )
    assert.deepEqual(violations, [
      'node_modules/zod/package.json: node_modules 为安装产物，禁止出现在工作区文件清单',
      'src/toolkit/config/node_modules/zod/index.js: node_modules 为安装产物，禁止出现在工作区文件清单',
    ])
  }

  /** @anchor deps-config-inheritance */
  public static localPackageManagerRejected(this: void): void {
    const violations: readonly string[] = CheckWorkspaceHygiene.verify(
      [],
      [
        ...WorkspaceHygieneTest.cleanManifests(),
        {
          relativePath: 'src/toolkit/host/package.json',
          packageManager: Option.some('pnpm@11.1.2'),
          bypassKeys: [],
        },
      ],
      [],
    )
    assert.deepEqual(violations, [
      'src/toolkit/host/package.json: packageManager 只允许根 package.json 声明，成员包必须继承',
    ])
  }

  /** @anchor deps-config-inheritance */
  public static manifestBypassKeysRejected(this: void): void {
    const violations: readonly string[] = CheckWorkspaceHygiene.verify(
      [],
      [
        {
          relativePath: 'src/toolkit/host/package.json',
          packageManager: Option.none(),
          bypassKeys: ['resolutions', 'pnpm'],
        },
      ],
      [],
    )
    assert.deepEqual(violations, [
      'src/toolkit/host/package.json: 禁止 resolutions 旁路依赖解析；依赖约束唯一写根配置',
      'src/toolkit/host/package.json: 禁止 pnpm 旁路依赖解析；依赖约束唯一写根配置',
    ])
  }

  /** @anchor deps-config-inheritance */
  public static rootManifestBypassKeysStillRejected(this: void): void {
    const violations: readonly string[] = CheckWorkspaceHygiene.verify(
      [],
      [
        {
          relativePath: 'package.json',
          packageManager: Option.some('pnpm@11.1.2'),
          bypassKeys: ['overrides'],
        },
      ],
      [],
    )
    assert.deepEqual(violations, [
      'package.json: 禁止 overrides 旁路依赖解析；依赖约束唯一写根配置',
    ])
  }

  /** @anchor deps-artifacts-quarantine */
  public static hiddenNpmLockRejected(this: void): void {
    mkdirSync(resolve('_build/tests'), { recursive: true })
    const root: string = mkdtempSync(resolve('_build/tests', 'hygiene-'))
    try {
      writeFileSync(join(root, 'package.json'), '{}')
      writeFileSync(join(root, 'pnpm-lock.yaml'), '')
      mkdirSync(join(root, 'node_modules'))
      writeFileSync(join(root, WorkspaceHygieneHiddenLockFixture.path), '')
      assert.deepEqual(CheckWorkspaceHygiene.forkedInstallArtifacts(root, ['package.json']), [
        'node_modules/.package-lock.json: npm 手搓隐藏锁；包管理唯一 pnpm',
      ])
    } finally {
      rmSync(root, { recursive: true, force: true })
    }
  }

  /** @anchor deps-artifacts-quarantine */
  public static ignoredForkedLockRejected(this: void): void {
    mkdirSync(resolve('_build/tests'), { recursive: true })
    const root: string = mkdtempSync(resolve('_build/tests', 'hygiene-'))
    try {
      writeFileSync(join(root, 'package.json'), '{}')
      writeFileSync(join(root, 'pnpm-lock.yaml'), '')
      writeFileSync(join(root, 'package-lock.json'), '')
      assert.deepEqual(CheckWorkspaceHygiene.forkedInstallArtifacts(root, ['package.json']), [
        'package-lock.json: 分裂锁文件；锁文件唯一为根 pnpm-lock.yaml',
      ])
    } finally {
      rmSync(root, { recursive: true, force: true })
    }
  }

  /** @anchor deps-config-inheritance */
  public static standaloneTypeScriptConfigRejected(this: void): void {
    const violations: readonly string[] = CheckWorkspaceHygiene.verify(
      [],
      [],
      [
        {
          relativePath: 'src/toolkit/host/tsconfig.json',
          extendsReference: Option.none(),
          ownCompilerOptions: false,
        },
      ],
    )
    assert.deepEqual(violations, [
      'src/toolkit/host/tsconfig.json: tsconfig 必须显式 extends 继承 tsconfig.base.json，禁止自立门户',
    ])
  }

  /** @anchor deps-config-inheritance */
  public static localCompilerOptionsRejected(this: void): void {
    const violations: readonly string[] = CheckWorkspaceHygiene.verify(
      [],
      [],
      [
        {
          relativePath: 'src/toolkit/mcp/tsconfig.json',
          extendsReference: Option.some('../tsconfig.json'),
          ownCompilerOptions: true,
        },
      ],
    )
    assert.deepEqual(violations, [
      'src/toolkit/mcp/tsconfig.json: 禁止局部 compilerOptions 旁路；编译选项唯一属于 tsconfig.base.json',
    ])
  }

  static {
    test('全部继承根权威的完整清单通过', WorkspaceHygieneTest.fullInheritancePasses)
    test('局部 .npmrc 被拒绝', WorkspaceHygieneTest.localNpmrcRejected)
    test('分叉锁文件被拒绝', WorkspaceHygieneTest.forkedLockRejected)
    test('node_modules 出现在工作区清单被拒绝', WorkspaceHygieneTest.nodeModulesRejected)
    test('成员包声明 packageManager 被拒绝', WorkspaceHygieneTest.localPackageManagerRejected)
    test('成员包 resolutions/pnpm 旁路键被拒绝', WorkspaceHygieneTest.manifestBypassKeysRejected)
    test(
      '根 manifest overrides 旁路键同样被拒绝',
      WorkspaceHygieneTest.rootManifestBypassKeysStillRejected,
    )
    test(
      '无 extends 的分叉 tsconfig 被拒绝',
      WorkspaceHygieneTest.standaloneTypeScriptConfigRejected,
    )
    test('局部 compilerOptions 旁路被拒绝', WorkspaceHygieneTest.localCompilerOptionsRejected)
    test('npm 隐藏锁被拒绝', WorkspaceHygieneTest.hiddenNpmLockRejected)
    test('被藏分裂锁文件被拒绝', WorkspaceHygieneTest.ignoredForkedLockRejected)
  }
}
