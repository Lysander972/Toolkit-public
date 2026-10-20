import assert from 'node:assert/strict'
import { test } from 'vitest'
import { Option } from 'effect'
import { ArchitectureProject } from '../../../tools/cli/ArchitectureProject.ts'
import type { ArchitectureDeclarations } from '../../../tools/governance/derive-rules.ts'
import type { ArchitectureElement } from '../../../tools/governance/derive-rules.ts'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { GitFixture } from '../../../tools/testing/GitFixture.ts'

/** @anchor architecture-project */
export class ArchitectureProjectTest {
  private constructor() {}

  static {
    test('纯函数源文件和产品骨架进入同一次模型', async (): Promise<void> => {
      await ArchitectureProjectTest.loads()
    })
  }

  /** @anchor architecture-project */
  private static async loads(): Promise<void> {
    mkdirSync(resolve('_build/tests'), { recursive: true })
    const repositoryFullPath: string = mkdtempSync(resolve('_build/tests', 'architecture-project-'))
    try {
      GitFixture.create(repositoryFullPath)
      mkdirSync(join(repositoryFullPath, 'src/pure'), { recursive: true })
      writeFileSync(join(repositoryFullPath, 'src/pure/value.ts'), 'export const value = 1\n')
      writeFileSync(
        join(repositoryFullPath, 'tools/pure.c4'),
        "model { pure = system 'pure' { description '纯计算' } }",
      )
      mkdirSync(join(repositoryFullPath, 'tools/architecture'), { recursive: true })
      writeFileSync(
        join(repositoryFullPath, 'tools/architecture/value.c4'),
        "model { extend pure { logic = container 'logic' { #tool description '值计算' metadata { sourcePath 'src/pure' files ['src/pure/*.ts'] } } } }",
      )
      const current: ArchitectureDeclarations = await ArchitectureProject.load(
        repositoryFullPath,
        Option.none(),
      )
      const committed: ArchitectureDeclarations = await ArchitectureProject.load(
        repositoryFullPath,
        Option.some('HEAD'),
      )
      assert.deepEqual(
        current.elements
          .filter((element: ArchitectureElement): boolean => element.id === 'pure.logic')
          .map((element: ArchitectureElement): readonly string[] => element.files),
        [['src/pure/*.ts']],
      )
      assert.equal(
        committed.elements.some(
          (element: ArchitectureElement): boolean => element.id === 'pure.logic',
        ),
        false,
      )
      assert.equal(current.rootElementId, committed.rootElementId)
    } finally {
      rmSync(repositoryFullPath, { recursive: true })
    }
  }
}
