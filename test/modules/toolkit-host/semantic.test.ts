import assert from 'node:assert/strict'
import { Option } from 'effect'
import { test } from 'vitest'
import {
  LogicalEntitySnapshot,
  LogicalRoot,
} from '../../../src/toolkit/project_model/LogicalEntities.ts'
import {
  SemanticAssembly,
  type SemanticAssemblyInput,
  type SemanticDevice,
} from '../../../src/toolkit/semantic/SemanticAssembly.ts'

/** @anchor semantic-assembly-pending */
export class SemanticAssemblyTests {
  private constructor() {}

  static {
    test('语义空壳对空快照和工程声明明确失败，保持完整输入', (): void => {
      const root: LogicalRoot = new LogicalRoot(
        { primaryRelativePath: '.', files: [], directories: [{ relativePath: '.', entries: [] }] },
        Option.none(),
      )
      const snapshots: readonly LogicalEntitySnapshot[] = [
        new LogicalEntitySnapshot([], []),
        new LogicalEntitySnapshot([root], []),
      ]
      for (const entities of snapshots) {
        const input: SemanticAssemblyInput = {
          entities,
          analysis: {
            table: {
              carriers: [],
              devices: [],
              coreDeviceTypes: new Map(),
              globalVariables: new Map(),
              methods: [],
              diagnostics: [],
            },
            edges: [],
            unresolved: [],
          },
          platform: {
            types: [],
            methods: [],
            events: [],
            enums: [],
            identifiers: [{ besName: 'speed', tsName: 'speed' }],
          },
        }
        const original: SemanticAssemblyInput = structuredClone(input)
        assert.throws((): readonly SemanticDevice[] => SemanticAssembly.assemble(input), {
          name: 'Error',
          message: 'TODO: 从同次逻辑快照、分析与平台输入装配完整零件语义',
        })
        assert.deepEqual(structuredClone(input), original)
      }
    })
  }
}
