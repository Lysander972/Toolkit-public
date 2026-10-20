import { test } from 'vitest'
import assert from 'node:assert/strict'
import fc from 'fast-check'
import { WorkspacePaths } from '../../../src/toolkit/project_model/WorkspacePaths.ts'

/** @anchor workspace-file-paths */
export class WorkspacePathsPropertyTest {
  private constructor() {}

  static {
    test('任意合法路径分段按原文保留，插入越界分段即拒绝', (): void => {
      const segment: fc.Arbitrary<string> = fc
        .array(fc.constantFrom('a', 'Z', '0', '中', '_', '-', ' '), { minLength: 1, maxLength: 16 })
        .map((characters: readonly string[]): string => characters.join(''))
      fc.assert(
        fc.property(
          fc.array(segment, { minLength: 1, maxLength: 8 }),
          (segments: readonly string[]): void => {
            const relativePath: string = segments.join('/')
            assert.equal(WorkspacePaths.require(relativePath), relativePath)
            for (const invalid of ['', '.', '..']) {
              assert.throws((): string => WorkspacePaths.require(`${relativePath}/${invalid}/tail`))
            }
            for (const invalid of ['/', 'C:/', '\\', ':', '\0']) {
              assert.throws((): string => WorkspacePaths.require(invalid + relativePath))
            }
          },
        ),
        { numRuns: 500 },
      )
      assert.equal(WorkspacePaths.require('.'), '.')
    })
  }
}
