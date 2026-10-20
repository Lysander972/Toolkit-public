import assert from 'node:assert/strict'
import { Option } from 'effect'
import { test } from 'vitest'
import {
  ChangePlan,
  CanvasChangePlanRequest,
  DeviceChangePlanRequest,
  ScriptChangePlanRequest,
  OperationChangePlanRequest,
  RefactorChangePlanRequest,
  type ChangePlanInput,
  type ChangePlanRequest,
  type ChangePlanResult,
} from '../../../src/toolkit/change_plan/ChangePlan.ts'
import { RenameRefactorRequest } from '../../../src/toolkit/change_plan/RefactorRequest.ts'
import {
  LogicalEntitySnapshot,
  LogicalRoot,
  LogicalGuidAsset,
  LogicalDevice,
  UpdateBytes,
} from '../../../src/toolkit/project_model/LogicalEntities.ts'
import { CanvasEntity } from '../../../src/toolkit/entities/CanvasEntity.ts'
import { DeviceEntity } from '../../../src/toolkit/entities/DeviceEntity.ts'
import { ScriptEntity } from '../../../src/toolkit/entities/ScriptEntity.ts'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import { BescriptConverter } from '../../../src/toolkit/script/conversion/BescriptConverter.ts'
import { TsViewFixture } from '../toolkit-script-fixtures/TsViewFixture.ts'
import { CanvasTestWorkspace } from '../toolkit-file-support/CanvasTestWorkspace.ts'

/**
 * @anchor change-plan-contract
 * @anchor change-plan-refactor-input
 */
export class ChangePlanTests {
  private constructor() {}

  static {
    test('格式编辑、重构意图与显式操作的纯计划空壳明确失败并保持完整输入', (): void => {
      const original: CanvasEntity = CanvasTestWorkspace.entity(
        CanvasConverter.fromHtml(
          '<BeCanvas name="原布局" width="1080" height="2160"><BeUILabel name="文本" text="旧值"></BeUILabel></BeCanvas>',
        ),
      )
      const updated: CanvasEntity = new CanvasEntity(
        original.declaration,
        CanvasConverter.fromHtml(
          '<BeCanvas name="新布局" width="1080" height="2160"><BeUILabel name="文本" text="新值"></BeUILabel></BeCanvas>',
        ),
        original.originals,
        original.baseline,
      )
      const root: LogicalRoot = new LogicalRoot(
        {
          primaryRelativePath: '.',
          files: [],
          directories: [{ relativePath: '.', entries: ['ui.canvas', 'ui.canvas.guid'] }],
        },
        Option.none(),
      )
      const canvas: LogicalGuidAsset = new LogicalGuidAsset({
        kind: 'canvas',
        primaryRelativePath: original.sourceRelativePath,
        parentId: root.id,
        guid: original.id,
        files: [
          {
            relativePath: original.declaration.relativePath,
            revision: original.declaration.revision,
          },
          {
            relativePath: original.declaration.guidRelativePath,
            revision: original.declaration.guidRevision,
          },
        ],
        directories: [],
      })
      const snapshot: LogicalEntitySnapshot = new LogicalEntitySnapshot([root, canvas], [])
      const device: DeviceEntity = new DeviceEntity(
        new LogicalDevice({
          primaryRelativePath: TsViewFixture.basis().identity.deviceRelativePath,
          parentId: 'mech:机械/测试.mech',
          files: [],
          directories: [
            { relativePath: TsViewFixture.basis().identity.deviceRelativePath, entries: [] },
          ],
          deviceId: 0,
          deviceType: TsViewFixture.context('旧值').metadata.deviceType,
          fields: TsViewFixture.context('旧值').metadata,
          variables: Option.none(),
        }),
        TsViewFixture.context('旧值'),
        [],
        TsViewFixture.basis(),
        original.baseline,
      )
      const script: ScriptEntity = new ScriptEntity(
        'code-repo-script:公共.code',
        '公共.code',
        original.baseline,
        BescriptConverter.parse(
          'begincodeV2\nname:first\nlines:\nfun echo(String 旧值)\nendcode\n',
        ),
        [],
      )
      const updatedScript: ScriptEntity = new ScriptEntity(
        script.id,
        script.sourceRelativePath,
        script.baseline,
        BescriptConverter.parse(
          'begincodeV2\nname:first\nlines:\nfun echo(String 新值)\nendcode\n',
        ),
        script.originals,
      )
      const requests: readonly ChangePlanRequest[] = [
        new CanvasChangePlanRequest(original, updated),
        new DeviceChangePlanRequest(device, device.withContext(TsViewFixture.context('新值'))),
        new ScriptChangePlanRequest(script, updatedScript),
        new OperationChangePlanRequest(
          new UpdateBytes(canvas.id, CanvasConverter.toBinary(updated.document)),
        ),
        new RefactorChangePlanRequest(
          new RenameRefactorRequest(script.id, original.baseline, 'local-variable', [
            { oldName: '旧值', newName: '新值' },
          ]),
        ),
      ]
      for (const request of requests) {
        const input: ChangePlanInput = {
          snapshot,
          baseline: original.baseline,
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
          request,
        }
        const before: ChangePlanInput = structuredClone(input)
        assert.throws((): ChangePlanResult => ChangePlan.plan(input), {
          name: 'Error',
          message: 'TODO: 按原始实体、反向转换结果与分析生成统一操作集合及实际依赖基准',
        })
        assert.deepEqual(structuredClone(input), before)
      }
    })
  }
}
