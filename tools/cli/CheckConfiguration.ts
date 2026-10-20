import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { pathToFileURL } from 'node:url'
import { Option } from 'effect'
import ts from 'typescript'
import { z } from 'zod'
import { getFileInfo } from 'prettier'
import { ArchitectureFiles } from '../governance/architecture-files.ts'
import { WorkspaceFiles } from './WorkspaceFiles.ts'
import type { ArchitectureDeclarations, ArchitectureElement } from '../governance/derive-rules.ts'
import type { ReadonlyDeep } from 'type-fest'

/** @anchor configuration-model */
interface PackageManifest {
  readonly scripts: Readonly<Record<string, string>>
}

/** @anchor configuration-model */
interface StandardConfiguration {
  readonly testInclude: readonly string[]
  readonly testExclude: Option.Option<readonly string[]>
  readonly knipEntry: readonly string[]
  readonly knipProject: readonly string[]
  readonly eslintFiles: readonly string[]
}

/** @anchor configuration-model */
interface VitestTestConfiguration {
  readonly include: readonly string[]
  readonly exclude: Option.Option<readonly string[]>
}

/** @anchor configuration-model */
interface VitestConfiguration {
  readonly test: VitestTestConfiguration
}
/** @anchor configuration-model */
interface KnipWorkspaceConfiguration {
  readonly entry: readonly string[]
  readonly project: readonly string[]
}

/** @anchor configuration-model */
interface KnipConfiguration {
  readonly workspaces: Readonly<Record<string, KnipWorkspaceConfiguration>>
}

/** @anchor configuration-model */
interface ModuleExport<T> {
  readonly default: T
}

/** @anchor configuration-model */
interface EslintConfiguration {
  readonly files: Option.Option<readonly string[]>
}

/** @anchor configuration-model */
export class CheckConfiguration {
  private constructor() {}

  /** @anchor configuration-model */
  public static async run(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
  ): Promise<number> {
    const violations: string[] = []
    const allTypeScript: readonly string[] = WorkspaceFiles.domainTypeScript(
      model,
      repositoryFullPath,
    )
    const testTypeScript: readonly string[] = WorkspaceFiles.testTypeScript(
      model,
      repositoryFullPath,
    )
    const rootJavaScript: readonly string[] = WorkspaceFiles.select(repositoryFullPath, ['*.mjs'])
    const jsonFiles: readonly string[] = CheckConfiguration.sorted(
      WorkspaceFiles.all(repositoryFullPath).filter(
        (relativePath: string): boolean =>
          relativePath.endsWith('.json') &&
          (relativePath.startsWith('src/') ||
            relativePath.startsWith('res/') ||
            dirname(relativePath) === '.'),
      ),
    )
    const prettierCandidates: readonly string[] = CheckConfiguration.sorted([
      ...allTypeScript,
      ...rootJavaScript,
      ...jsonFiles,
      ...WorkspaceFiles.select(repositoryFullPath, ['README.md', '.github/**/*.md']),
      'knip.config.ts',
    ])
    const prettierFiles: string[] = []
    const ignoredFiles: Set<string> = new Set()
    const documentFiles: ReadonlySet<string> = new Set(
      WorkspaceFiles.select(
        repositoryFullPath,
        model.elements
          .filter(
            (element: ReadonlyDeep<ArchitectureElement>): boolean => element.kind === 'document',
          )
          .flatMap(
            (element: ReadonlyDeep<ArchitectureElement>): readonly string[] => element.files,
          ),
      ),
    )
    for (const relativePath of prettierCandidates) {
      const info: Awaited<ReturnType<typeof getFileInfo>> = await getFileInfo(
        join(repositoryFullPath, relativePath),
        {
          ignorePath: join(repositoryFullPath, '.prettierignore'),
        },
      )
      if (info.ignored) {
        ignoredFiles.add(relativePath)
        if (!documentFiles.has(relativePath))
          violations.push(`Prettier: 非文档文件不得忽略: ${relativePath}`)
      } else prettierFiles.push(relativePath)
    }
    const lintFiles: readonly string[] = CheckConfiguration.sorted([
      ...allTypeScript,
      'knip.config.ts',
    ])
    const packageManifest: PackageManifest = z
      .object({ scripts: z.record(z.string(), z.string()) })
      .parse(JSON.parse(readFileSync(join(repositoryFullPath, 'package.json'), 'utf8')))
    violations.push(
      ...CheckConfiguration.script(
        packageManifest,
        'check:format',
        'prettier --check --cache --cache-location _build/prettier/cache "tools/**/*.ts" "src/toolkit/**/*.ts" "src/canvas_render/**/*.ts" "test/modules/**/*.{ts,tsx}" "src/frontend/**/*.{ts,tsx}" "src/preview/**/*.{ts,tsx}" "res/**/*.json" "src/**/*.json" "*.json" "*.mjs" "README.md" ".github/**/*.md" knip.config.ts',
      ),
    )
    violations.push(
      ...CheckConfiguration.script(
        packageManifest,
        'check:lint',
        'TSX_DISABLE_CACHE=1 eslint tools src/toolkit src/canvas_render test/modules src/frontend src/preview knip.config.ts --cache --cache-strategy content --cache-location _build/eslint/cache --max-warnings 0',
      ),
    )
    violations.push(
      ...CheckConfiguration.script(
        packageManifest,
        'check:types',
        'tsc --noEmit -p tools/tsconfig.json --incremental --tsBuildInfoFile _build/tsc/check.tsbuildinfo',
      ),
    )
    violations.push(
      ...CheckConfiguration.script(
        packageManifest,
        'check',
        'tsx --no-cache tools/cli/main.ts check',
      ),
    )
    violations.push(
      ...CheckConfiguration.script(
        packageManifest,
        'check:commit',
        'tsx --no-cache tools/cli/main.ts commit-check',
      ),
    )
    violations.push(
      ...CheckConfiguration.patterns(
        'Prettier',
        [
          'tools/**/*.ts',
          'src/toolkit/**/*.ts',
          'src/canvas_render/**/*.ts',
          'test/modules/**/*.{ts,tsx}',
          'src/frontend/**/*.{ts,tsx}',
          'src/preview/**/*.{ts,tsx}',
          'res/**/*.json',
          'src/**/*.json',
          '*.json',
          '*.mjs',
          'README.md',
          '.github/**/*.md',
          'knip.config.ts',
        ],
        repositoryFullPath,
        prettierFiles,
        WorkspaceFiles.all(repositoryFullPath).filter(
          (relativePath: string): boolean => !ignoredFiles.has(relativePath),
        ),
      ),
    )
    violations.push(...CheckConfiguration.json(jsonFiles, repositoryFullPath))
    const tsConfig: ts.ParsedCommandLine = CheckConfiguration.typescriptConfig(repositoryFullPath)
    violations.push(
      ...CheckConfiguration.equal(
        'TypeScript',
        CheckConfiguration.relativeFiles(repositoryFullPath, tsConfig.fileNames),
        CheckConfiguration.sorted([...allTypeScript, 'knip.config.ts']),
      ),
    )
    const standard: StandardConfiguration = await CheckConfiguration.modules(repositoryFullPath)
    const configuredTestTypeScript: readonly string[] = testTypeScript.filter(
      (relativePath: string): boolean =>
        !Option.exists(standard.testExclude, (patterns: readonly string[]): boolean =>
          patterns.some((pattern: string): boolean =>
            ArchitectureFiles.matches(relativePath, pattern),
          ),
        ),
    )
    const declaredEntries: readonly string[] = CheckConfiguration.sorted(
      model.elements.flatMap(
        (element: ReadonlyDeep<ArchitectureElement>): readonly string[] => element.entries,
      ),
    )
    violations.push(
      ...CheckConfiguration.equal(
        '构建入口',
        declaredEntries.filter((relativePath: string): boolean =>
          allTypeScript.includes(relativePath),
        ),
        declaredEntries,
      ),
    )
    violations.push(
      ...CheckConfiguration.patterns(
        'ESLint',
        standard.eslintFiles,
        repositoryFullPath,
        lintFiles,
        lintFiles,
      ),
    )
    violations.push(
      ...CheckConfiguration.patterns(
        'Vitest',
        standard.testInclude,
        repositoryFullPath,
        configuredTestTypeScript,
        WorkspaceFiles.all(repositoryFullPath).filter(
          (relativePath: string): boolean =>
            !Option.exists(standard.testExclude, (patterns: readonly string[]): boolean =>
              patterns.some((pattern: string): boolean =>
                ArchitectureFiles.matches(relativePath, pattern),
              ),
            ),
        ),
      ),
    )
    violations.push(
      ...CheckConfiguration.patterns(
        'Knip entry',
        standard.knipEntry,
        repositoryFullPath,
        CheckConfiguration.sorted([...configuredTestTypeScript, ...declaredEntries]),
        WorkspaceFiles.all(repositoryFullPath),
      ),
    )
    violations.push(
      ...CheckConfiguration.patterns(
        'Knip project',
        standard.knipProject,
        repositoryFullPath,
        allTypeScript,
        WorkspaceFiles.all(repositoryFullPath),
      ),
    )
    for (const violation of violations) {
      process.stderr.write(violation + '\n')
    }
    return violations.length === 0 ? 0 : 1
  }

  /**
   * @anchor configuration-model
   * @fallback ESLint 配置项没有 files 时以 Option.none 表达无范围，不制造隐式路径。
   */
  private static async modules(repositoryFullPath: string): Promise<StandardConfiguration> {
    const vitestTestSchema: z.ZodType<VitestTestConfiguration> = z.object({
      include: z.array(z.string()),
      exclude: z.array(z.string()).optional().transform(Option.fromNullishOr),
    })
    const vitestSchema: z.ZodType<VitestConfiguration> = z.object({
      test: vitestTestSchema,
    })
    const knipWorkspaceSchema: z.ZodType<KnipWorkspaceConfiguration> = z.object({
      entry: z.array(z.string()),
      project: z.array(z.string()),
    })
    const knipSchema: z.ZodType<KnipConfiguration> = z.object({
      workspaces: z.record(z.string(), knipWorkspaceSchema),
    })
    const eslintEntrySchema: z.ZodType<EslintConfiguration> = z.object({
      files: z.array(z.string()).optional().transform(Option.fromNullishOr),
    })
    const eslintSchema: z.ZodType<readonly EslintConfiguration[]> = z.array(eslintEntrySchema)
    const moduleSchema: z.ZodType<ModuleExport<unknown>> = z.object({
      default: z.unknown(),
    })
    const vitestModule: unknown = await import(
      pathToFileURL(join(repositoryFullPath, 'vitest.config.mjs')).href
    )
    const knipModule: unknown = await import(
      pathToFileURL(join(repositoryFullPath, 'knip.config.ts')).href
    )
    const eslintModule: unknown = await import(
      pathToFileURL(join(repositoryFullPath, 'eslint.config.mjs')).href
    )
    const vitestConfig: VitestConfiguration = vitestSchema.parse(
      moduleSchema.parse(vitestModule).default,
    )
    const knipConfig: KnipConfiguration = knipSchema.parse(moduleSchema.parse(knipModule).default)
    const eslintConfig: readonly EslintConfiguration[] = eslintSchema.parse(
      moduleSchema.parse(eslintModule).default,
    )
    const eslintFiles: readonly string[] = eslintConfig.flatMap(
      (configuration: EslintConfiguration): readonly string[] =>
        Option.match(configuration.files, {
          onSome: (files: readonly string[]): readonly string[] => files,
          onNone: (): readonly string[] => [],
        }),
    )
    return {
      testInclude: vitestConfig.test.include,
      testExclude: vitestConfig.test.exclude,
      knipEntry: Object.entries(knipConfig.workspaces).flatMap(
        ([workspace, configuration]: [string, KnipWorkspaceConfiguration]): readonly string[] =>
          configuration.entry.map((pattern: string): string =>
            workspace === '.' ? pattern : workspace + '/' + pattern,
          ),
      ),
      knipProject: Object.entries(knipConfig.workspaces).flatMap(
        ([workspace, configuration]: [string, KnipWorkspaceConfiguration]): readonly string[] =>
          configuration.project.map((pattern: string): string =>
            workspace === '.' ? pattern : workspace + '/' + pattern,
          ),
      ),
      eslintFiles,
    }
  }

  /** @anchor configuration-model */
  private static typescriptConfig(repositoryFullPath: string): ts.ParsedCommandLine {
    // @non-manual-path 架构模型固定的 TypeScript 配置入口
    const configPath: string = join(repositoryFullPath, 'tools', 'tsconfig.json')
    const readFile: typeof ts.sys.readFile = ts.sys.readFile.bind(ts.sys)
    const loaded: ReturnType<typeof ts.readConfigFile> = ts.readConfigFile(configPath, readFile)
    if (loaded.error !== undefined) {
      throw new Error(ts.flattenDiagnosticMessageText(loaded.error.messageText, '\n'))
    }
    // @non-manual-path 架构模型固定的 TypeScript 配置入口
    return ts.parseJsonConfigFileContent(loaded.config, ts.sys, join(repositoryFullPath, 'tools'))
  }

  /** @anchor configuration-model */
  private static patterns(
    name: string,
    patterns: readonly string[],
    repositoryFullPath: string,
    expected: readonly string[],
    candidates: readonly string[] = WorkspaceFiles.all(repositoryFullPath),
  ): string[] {
    const actual: readonly string[] = CheckConfiguration.sorted(
      candidates.filter((relativePath: string): boolean =>
        patterns.some((pattern: string): boolean =>
          ArchitectureFiles.matches(relativePath, pattern),
        ),
      ),
    )
    return CheckConfiguration.equal(name, actual, expected)
  }

  /** @anchor configuration-model */
  private static script(manifest: PackageManifest, name: string, expected: string): string[] {
    const violations: string[] = []
    if (manifest.scripts[name] !== expected) {
      violations.push(`${name}: 必须使用标准范围声明 ${expected}`)
    }
    return violations
  }

  /** @anchor configuration-json-input */
  private static json(relativePaths: readonly string[], repositoryFullPath: string): string[] {
    const violations: string[] = []
    for (const relativePath of relativePaths) {
      try {
        const value: unknown = z
          .unknown()
          .parse(JSON.parse(readFileSync(join(repositoryFullPath, relativePath), 'utf8')))
        if (typeof value !== 'object' || value === null || Array.isArray(value)) continue
        const schema: unknown = z.object({ $schema: z.unknown().optional() }).parse(value).$schema
        if (typeof schema !== 'string' || schema.includes('://')) continue
        const schemaPath: string =
          schema.startsWith('../') || schema.startsWith('./')
            ? join(repositoryFullPath, dirname(relativePath), schema)
            : join(repositoryFullPath, schema)
        readFileSync(schemaPath)
      } catch (error: unknown) {
        violations.push(`${relativePath}: JSON 或本地 schema 无法读取: ${String(error)}`)
      }
    }
    return violations
  }

  /** @anchor configuration-model */
  private static equal(
    name: string,
    actual: readonly string[],
    expected: readonly string[],
  ): string[] {
    const violations: string[] = []
    const actualText: string = actual.join('|')
    const expectedText: string = expected.join('|')
    if (actualText !== expectedText) {
      violations.push(`${name}: 配置范围与架构模型不一致；实际=${actualText}；期望=${expectedText}`)
    }
    return violations
  }

  /** @anchor configuration-model */
  private static relativeFiles(repositoryFullPath: string, files: readonly string[]): string[] {
    return CheckConfiguration.sorted(
      files.map((fileFullPath: string): string =>
        fileFullPath.slice(repositoryFullPath.length + 1).replaceAll('\\', '/'),
      ),
    )
  }

  /** @anchor configuration-model */
  private static sorted(files: readonly string[]): string[] {
    return [...new Set(files)].sort()
  }
}
