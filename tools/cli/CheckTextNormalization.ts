import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { WorkspaceFiles } from './WorkspaceFiles.ts'
import type { ArchitectureDeclarations } from '../governance/derive-rules.ts'
import type { ReadonlyDeep } from 'type-fest'

/** @anchor text-convergence */
export interface TextViolation {
  relativePath: string
  line: number
  detail: string
}

/** @anchor text-convergence */
export class CheckTextNormalization {
  private static readonly bom: readonly number[] = [0xef, 0xbb, 0xbf]
  private static readonly nul: number = 0x00

  private constructor() {}

  /** @anchor text-convergence */
  public static scope(model: ReadonlyDeep<ArchitectureDeclarations>): readonly string[] {
    return WorkspaceFiles.scope(model)
      .filter(
        (element: ReadonlyDeep<ArchitectureDeclarations>['elements'][number]): boolean =>
          element.kind !== 'document' && !element.textExempt,
      )
      .flatMap(
        (element: ReadonlyDeep<ArchitectureDeclarations>['elements'][number]): readonly string[] =>
          element.files,
      )
  }

  /** @anchor text-convergence */
  public static verify(relativePath: string, content: Buffer): TextViolation[] {
    if (content.includes(CheckTextNormalization.nul)) {
      return []
    }
    const violations: TextViolation[] = []
    if (
      CheckTextNormalization.bom.every(
        (byte: number, index: number): boolean => content[index] === byte,
      )
    ) {
      violations.push({ relativePath, line: 1, detail: '存在 UTF-8 BOM' })
    }
    const text: string = content.toString('utf8')
    text.split('\n').forEach((raw: string, index: number): void => {
      const line: string = raw.endsWith('\r') ? raw.slice(0, -1) : raw
      if (line.endsWith(' ') || line.endsWith('\t')) {
        violations.push({ relativePath, line: index + 1, detail: '行尾存在尾随空白' })
      }
    })
    return violations
  }

  /** @anchor text-convergence */
  public static check(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    repositoryFullPath: string,
  ): number {
    const declarations: readonly string[] = CheckTextNormalization.scope(model)
    const relativePaths: readonly string[] = WorkspaceFiles.select(repositoryFullPath, declarations)
    const violations: TextViolation[] = relativePaths.flatMap(
      (relativePath: string): TextViolation[] =>
        CheckTextNormalization.verify(
          relativePath,
          readFileSync(join(repositoryFullPath, relativePath)),
        ),
    )
    for (const violation of violations) {
      process.stderr.write(
        `${violation.relativePath}:${String(violation.line)}: ${violation.detail}\n`,
      )
    }
    return violations.length === 0 ? 0 : 1
  }
}
