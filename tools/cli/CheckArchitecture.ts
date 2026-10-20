import { join } from 'node:path'
import { readFileSync, statSync } from 'node:fs'
import { cruise } from 'dependency-cruiser'
import { Option } from 'effect'
import { z } from 'zod'
import { ArchitectureFiles } from '../governance/architecture-files.ts'
import { WorkspaceFiles } from './WorkspaceFiles.ts'
import type { IReporterOutput } from 'dependency-cruiser'
import type {
  ArchitectureContainer,
  ArchitectureDeclarations,
  ArchitectureGap,
} from '../governance/derive-rules.ts'
import type { ArchitectureFileReport } from '../governance/architecture-files.ts'
import type { ReadonlyDeep } from 'type-fest'
import type { Stats } from 'node:fs'

/** @anchor architecture-dependencies */
interface CruiseDependency {
  readonly resolved: string
}

/** @anchor architecture-dependencies */
interface CruiseModule {
  readonly source: string
  readonly dependencies: readonly CruiseDependency[]
}

/** @anchor architecture-dependencies */
interface CruiseRule {
  readonly name: string
}

/** @anchor architecture-dependencies */
interface CruiseViolation {
  readonly from: string
  readonly to: string
  readonly rule: CruiseRule
}

/** @anchor architecture-dependencies */
interface CruiseSummary {
  readonly violations: readonly CruiseViolation[]
}

/** @anchor architecture-dependencies */
interface CruiseResult {
  readonly modules: readonly CruiseModule[]
  readonly summary: CruiseSummary
}

/** @anchor architecture-model */
export class CheckArchitecture {
  private static readonly cruiseResultSchema: z.ZodType<CruiseResult> = z.object({
    modules: z.array(
      z.object({
        source: z.string(),
        dependencies: z.array(z.object({ resolved: z.string() })),
      }),
    ),
    summary: z.object({
      violations: z.array(
        z.object({
          from: z.string(),
          to: z.string(),
          rule: z.object({ name: z.string() }),
        }),
      ),
    }),
  })

  private constructor() {}

  /** @anchor architecture-files */
  public static files(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
  ): number {
    const files: readonly string[] = WorkspaceFiles.all(repositoryFullPath)
    const report: ArchitectureFileReport = ArchitectureFiles.check(model, files)
    for (const todo of model.gaps.filter(
      (gap: ReadonlyDeep<ArchitectureGap>): boolean => gap.kind === 'todo',
    )) {
      process.stderr.write(`${todo.target}: 执行待办 #todo: ${todo.reason}\n`)
    }
    report.violations.unshift(
      ...model.gaps
        .filter((gap: ReadonlyDeep<ArchitectureGap>): boolean => gap.kind === 'gap')
        .map(
          (gap: ReadonlyDeep<ArchitectureGap>): string =>
            `${gap.target}: 架构未收敛 #gap: ${gap.reason}`,
        ),
    )
    for (const container of [
      { id: model.rootElementId, sourceRelativePath: model.sourceRelativePath },
      ...model.containers,
    ]) {
      const directoryFullPath: string = join(repositoryFullPath, container.sourceRelativePath)
      const directory: Option.Option<Stats> = Option.fromNullishOr(
        statSync(directoryFullPath, { throwIfNoEntry: false }),
      )
      if (Option.isNone(directory) || !directory.value.isDirectory()) {
        report.violations.push(
          `${container.id}: sourcePath 必须是实际目录: ${container.sourceRelativePath}`,
        )
      }
    }
    report.violations.push(
      ...ArchitectureFiles.codeReferences(
        model,
        new Map(
          WorkspaceFiles.domainTypeScript(model, repositoryFullPath).map(
            (relativePath: string): [string, string] => [
              relativePath,
              readFileSync(join(repositoryFullPath, relativePath), 'utf8'),
            ],
          ),
        ),
      ),
    )
    for (const line of report.violations) {
      process.stderr.write(line + '\n')
    }
    return report.violations.length === 0 ? 0 : 1
  }

  /** @anchor architecture-dependencies */
  public static async run(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
  ): Promise<number> {
    const relativeFiles: readonly string[] = WorkspaceFiles.domainTypeScript(
      model,
      repositoryFullPath,
    )
    const workspaceFiles: readonly string[] = WorkspaceFiles.all(repositoryFullPath)
    const missingTargetDeclarations: readonly string[] = model.containers.flatMap(
      (container: ReadonlyDeep<ArchitectureContainer>): string[] =>
        container.files
          .filter(
            (declaration: string): boolean =>
              !workspaceFiles.some((file: string): boolean =>
                ArchitectureFiles.matches(file, declaration),
              ),
          )
          .map(
            (declaration: string): string =>
              `${container.id}: 目标源码声明未命中实际文件: ${declaration}`,
          ),
    )
    for (const violation of missingTargetDeclarations) process.stderr.write(violation + '\n')
    if (relativeFiles.length === 0) {
      throw new Error('架构扫描范围没有 TS 源码')
    }
    const ownerOf: Map<string, string> = CheckArchitecture.ownerOf(model, relativeFiles)
    const result: IReporterOutput = await cruise(
      [...relativeFiles],
      {
        baseDir: repositoryFullPath,
        ruleSet: { forbidden: model.dependencyRules() },
        validate: true,
        outputType: 'json',
        doNotFollow: { dependencyTypes: ['npm', 'core', 'npm-dev', 'npm-optional', 'npm-peer'] },
        tsConfig: { fileName: join(repositoryFullPath, model.sourceRelativePath, 'tsconfig.json') },
        tsPreCompilationDeps: true,
      },
      { exportsFields: ['exports'], conditionNames: ['import', 'node', 'default'] },
    )
    const output: CruiseResult = CheckArchitecture.cruiseResultSchema.parse(
      typeof result.output === 'string' ? JSON.parse(result.output) : result.output,
    )
    for (const violation of output.summary.violations) {
      process.stderr.write(`${violation.rule.name}: ${violation.from} -> ${violation.to}\n`)
    }
    const alignment: readonly string[] = CheckArchitecture.alignmentViolations(
      model,
      ownerOf,
      output,
    )
    for (const line of alignment) {
      process.stderr.write(line + '\n')
    }
    return output.summary.violations.length > 0 ||
      result.exitCode !== 0 ||
      missingTargetDeclarations.length > 0 ||
      alignment.length > 0
      ? 1
      : 0
  }

  /** @anchor architecture-dependencies */
  private static ownerOf(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    relativeFiles: readonly string[],
  ): Map<string, string> {
    const ownerOf: Map<string, string> = new Map<string, string>()
    for (const relativePath of relativeFiles) {
      const owners: readonly ReadonlyDeep<ArchitectureContainer>[] = model.containers.filter(
        (container: ReadonlyDeep<ArchitectureContainer>): boolean =>
          container.files.some((fileRelativePath: string): boolean =>
            ArchitectureFiles.matches(relativePath, fileRelativePath),
          ),
      )
      if (owners.length !== 1) {
        throw new Error(`${relativePath}: 必须且只能属于一个架构容器`)
      }
      ownerOf.set(relativePath, Option.getOrThrow(Option.fromNullishOr(owners[0])).id)
    }
    return ownerOf
  }

  /** @anchor edge-alignment */
  private static realEdges(
    ownerOf: ReadonlyMap<string, string>,
    output: CruiseResult,
  ): ReadonlySet<string> {
    const realEdges: Set<string> = new Set<string>()
    for (const entry of output.modules) {
      const from: Option.Option<string> = Option.fromNullishOr(ownerOf.get(entry.source))
      if (Option.isNone(from)) {
        continue
      }
      for (const dependency of entry.dependencies) {
        const to: Option.Option<string> = Option.fromNullishOr(ownerOf.get(dependency.resolved))
        if (Option.isSome(to) && from.value !== to.value) {
          realEdges.add(`${from.value}->${to.value}`)
        }
      }
    }
    return realEdges
  }

  /** @anchor edge-alignment */
  private static alignmentViolations(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    ownerOf: ReadonlyMap<string, string>,
    output: CruiseResult,
  ): readonly string[] {
    const realEdges: ReadonlySet<string> = CheckArchitecture.realEdges(ownerOf, output)
    const violations: string[] = []
    for (const edge of model.edges) {
      if (
        !model.containers.some(
          (container: ReadonlyDeep<ArchitectureContainer>): boolean => container.id === edge.source,
        ) ||
        !model.containers.some(
          (container: ReadonlyDeep<ArchitectureContainer>): boolean => container.id === edge.target,
        )
      )
        continue
      const pair: string = `${edge.source}->${edge.target}`
      if (edge.kind !== 'runtime' && !realEdges.has(pair))
        violations.push(`${pair}: 声明依赖边缺少真实代码依赖；pending 不能豁免缺失实现`)
      if (edge.kind === 'runtime' && realEdges.has(pair))
        violations.push(`${pair}: 已有静态依赖，runtime 交互形式与源码不一致`)
    }
    return violations
  }
}
