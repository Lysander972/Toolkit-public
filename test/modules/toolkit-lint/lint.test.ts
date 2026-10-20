import assert from 'node:assert/strict'
import { Option } from 'effect'
import { test } from 'vitest'
import {
  ScriptLint,
  type ScriptLintInput,
  type LintDiagnostic,
} from '../../../src/toolkit/lint/ScriptLint.ts'
import { SymbolTableBuilder, type SymbolMethod } from '../../../src/toolkit/analysis/SymbolTable.ts'
import { CallGraphBuilder, type CallGraph } from '../../../src/toolkit/analysis/CallGraph.ts'
import {
  LogicalRoot,
  LogicalCarrier,
  LogicalDevice,
  LogicalScriptGroup,
  LogicalGuidAsset,
  LogicalEntitySnapshot,
  type LogicalEntity,
} from '../../../src/toolkit/project_model/LogicalEntities.ts'
import {
  DeviceContextSchema,
  type BescriptDeviceContext,
  type DeviceFunction,
} from '../../../src/toolkit/script/device_context/DeviceContext.ts'
import type { LocalAst } from '../../../src/toolkit/script/local_ast/LocalAst.ts'
import { TsViewFixture } from '../toolkit-script-fixtures/TsViewFixture.ts'
import { CallGraphLint } from '../../../src/toolkit/lint/CallGraphLint.ts'

/**
 * @anchor script-lint-contract
 * @anchor lint-layered-pipeline
 */
export class ScriptLintTests {
  private constructor() {}

  static {
    test('共享源码的独立绑定与重复检查明确失败且保持完整结构化输入', (): void => {
      const parsed: BescriptDeviceContext = TsViewFixture.context('内容')
      const scriptGroup: BescriptDeviceContext['scriptGroups'][number] = Option.getOrThrow(
        Option.fromNullishOr(parsed.scriptGroups[0]),
      )
      const original: DeviceFunction = Option.getOrThrow(
        Option.fromNullishOr(scriptGroup.entries[0]),
      )
      const guid: string = 'a'.repeat(32)
      const sourceRelativePath: string = '公共.code'
      const root: LogicalRoot = new LogicalRoot(
        { primaryRelativePath: '.', files: [], directories: [{ relativePath: '.', entries: [] }] },
        Option.none(),
      )
      const carrier: LogicalCarrier = new LogicalCarrier({
        kind: 'mech',
        primaryRelativePath: '测试.mech',
        parentId: root.id,
        guid: 'b'.repeat(32),
        files: [
          { relativePath: '测试.mech', revision: '1' },
          { relativePath: '测试.mech.guid', revision: '1' },
        ],
        directories: [{ relativePath: '测试.mech_', entries: [] }],
      })
      const source: LogicalGuidAsset = new LogicalGuidAsset({
        kind: 'code-repo-script',
        primaryRelativePath: sourceRelativePath,
        parentId: root.id,
        guid,
        files: [
          { relativePath: sourceRelativePath, revision: '1' },
          { relativePath: sourceRelativePath + '.guid', revision: '1' },
        ],
        directories: [],
      })
      const contexts: Map<string, BescriptDeviceContext> = new Map()
      const entities: LogicalEntity[] = [root, carrier, source]
      for (const deviceId of [0, 1]) {
        const context: BescriptDeviceContext = DeviceContextSchema.parse({
          ...parsed,
          metadata: { ...parsed.metadata, logicName: Option.some('零件' + String(deviceId)) },
          scriptGroups: [
            {
              kind: 'custom',
              eventName: Option.none(),
              entries: [
                {
                  binding: {
                    origin: 'repository',
                    name: '检查',
                    guid: Option.some(guid),
                    isPrivate: false,
                  },
                  ast: original.ast,
                },
              ],
            },
          ],
        })
        const deviceRelativePath: string =
          carrier.derivedDirectoryRelativePath + '/' + String(deviceId)
        const device: LogicalDevice = new LogicalDevice({
          primaryRelativePath: deviceRelativePath,
          parentId: carrier.id,
          deviceId,
          deviceType: context.metadata.deviceType,
          fields: context.metadata,
          variables: Option.some(context.globalVariables),
          files: [],
          directories: [{ relativePath: deviceRelativePath, entries: [] }],
        })
        const group: LogicalScriptGroup = new LogicalScriptGroup({
          primaryRelativePath: deviceRelativePath + '/scripts',
          parentId: device.id,
          entries: [
            {
              name: '检查',
              origin: 'repository',
              guid: Option.some(guid),
              private: false,
              relativePath: Option.none(),
            },
          ],
          eventName: Option.none(),
          files: [],
          directories: [{ relativePath: deviceRelativePath + '/scripts', entries: [] }],
        })
        entities.push(device, group)
        contexts.set(device.id, context)
      }
      const asts: ReadonlyMap<string, LocalAst> = new Map([[sourceRelativePath, original.ast]])
      const analysis: CallGraph = CallGraphBuilder.build({
        table: SymbolTableBuilder.build({
          entities: new LogicalEntitySnapshot(entities, []),
          asts,
          coreDeviceTypes: new Map([['mech', '54']]),
        }),
        platformMethods: [],
        callbackRules: [],
      })
      assert.equal(analysis.table.methods.length, 2)
      for (const method of analysis.table.methods) {
        const input: ScriptLintInput = {
          methodKey: method.key,
          ast: Option.getOrThrow(method.analysis).ast,
          context: Option.getOrThrow(Option.fromNullishOr(contexts.get(method.deviceKey))),
          analysis,
        }
        assert.equal(input.ast, original.ast)
        const before: ScriptLintInput = structuredClone(input)
        for (const attempt of [1, 2]) {
          assert.throws(
            (): readonly LintDiagnostic[] => ScriptLint.inspect(input),
            {
              name: 'Error',
              message: 'TODO: 基于 LocalAst、完整 Context 与调用分析的脚本静态检查',
            },
            '绑定 ' + method.key + ' 第 ' + String(attempt) + ' 次检查',
          )
          assert.deepEqual(structuredClone(input), before)
        }
      }
      assert.equal(
        new Set(analysis.table.methods.map((method: SymbolMethod): string => method.key)).size,
        2,
      )
      const originalAnalysis: CallGraph = structuredClone(analysis)
      assert.throws((): readonly LintDiagnostic[] => CallGraphLint.inspect(analysis), {
        name: 'Error',
        message: 'TODO: 按完整调用图执行一次跨方法静态检查',
      })
      assert.deepEqual(structuredClone(analysis), originalAnalysis)
    })
  }
}
