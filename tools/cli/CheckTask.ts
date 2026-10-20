import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { WorkspaceFiles } from './WorkspaceFiles.ts'
import { EnvironmentVariables } from './EnvironmentVariables.ts'
import { EnvironmentScan } from '../governance/environment.ts'
import { ArchitectureFiles } from '../governance/architecture-files.ts'
import { StructuralRules } from '../lint/style/StructuralRules.ts'
import type { ArchitectureDeclarations } from '../governance/derive-rules.ts'
import type { ReadonlyDeep } from 'type-fest'

/** @anchor selfcheck-gate */
export class CheckTask {
  private constructor() {}

  /** @anchor selfcheck-gate */
  public static run(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
  ): number {
    const implementation: readonly string[] = WorkspaceFiles.implementationTypeScript(
      model,
      repositoryFullPath,
    )
    const tests: readonly string[] = WorkspaceFiles.testTypeScript(model, repositoryFullPath)
    const violations: string[] = [...implementation, ...tests].flatMap(
      (relativePath: string): string[] => {
        const source: string = readFileSync(join(repositoryFullPath, relativePath), 'utf8')
        if (tests.includes(relativePath)) {
          return StructuralRules.testQuality(source, relativePath)
        }
        return [
          ...StructuralRules.check(source, relativePath),
          ...EnvironmentScan.check(
            source,
            relativePath,
            Object.values(EnvironmentVariables),
            new Map(),
          ),
          ...ArchitectureFiles.suspectedPaths(model, source, relativePath),
        ]
      },
    )
    for (const violation of violations) {
      process.stderr.write(violation + '\n')
    }
    return violations.length === 0 ? 0 : 1
  }
}
