import assert from 'node:assert/strict'
import { test, vi } from 'vitest'
import type { MockInstance } from 'vitest'
import { Context, Effect, Layer, Option, Scope, Result } from 'effect'
import pino from 'pino'
import ts from 'typescript'
import {
  ProjectEntities,
  ProjectEntitiesService,
} from '../../../src/toolkit/entities/ProjectEntities.ts'
import type { ProjectFiles } from '../../../src/toolkit/files/ProjectFiles.ts'
import { WorkspaceService } from '../../../src/toolkit/host/WorkspaceServices.ts'
import { CanvasConversionService } from '../../../src/toolkit/conversion/canvas/CanvasConversion.ts'
import type { CanvasConversion } from '../../../src/toolkit/conversion/canvas/CanvasConversion.ts'
import type { WorkspaceModule } from '../../../src/toolkit/host/WorkspaceServices.ts'
import type { ViewMode } from '../../../src/toolkit/views/ProjectViews.ts'
import { Host } from '../../../src/toolkit/host/Host.ts'
import { TsViewEntity } from '../../../src/toolkit/entities/ToolkitEntity.ts'
import { FileTestWorkspace } from '../toolkit-file-support/FileTestWorkspace.ts'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import type { DiagnosticRequest } from '../../../src/toolkit/diagnostics/Diagnostics.ts'
import { ProjectWatcher, ProjectWatcherService } from '../../../src/toolkit/files/ProjectWatcher.ts'
import { ConfirmAssetTask } from '../../../src/toolkit/assets/AssetTaskContracts.ts'

/**
 * @anchor workspace-service-stack
 * @anchor workspace-state-cache-inventory
 */
export class WorkspaceServicesTest {
  private constructor() {}

  static {
    test('业务 Layer 共享唯一实体与监听，未实现服务明确失败且不写文件', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await workspace.write('setting.json', 'guid: a\nname: 测试\nmainMap: ""')
        const starts: MockInstance<typeof ProjectEntities.make> = vi.spyOn(ProjectEntities, 'make')
        let releasedCanvas: Option.Option<CanvasConversion> = Option.none()
        let canvasStarts: number = 0
        let canvasStops: number = 0
        const canvasLayer: typeof CanvasConversionService.layer =
          CanvasConversionService.layer.bind(CanvasConversionService)
        const canvas: MockInstance<typeof CanvasConversionService.layer> = vi
          .spyOn(CanvasConversionService, 'layer')
          .mockImplementation(
            (
              entities: Layer.Layer<ProjectEntitiesService, unknown>,
            ): Layer.Layer<CanvasConversionService, unknown> =>
              Layer.effect(
                CanvasConversionService,
                Effect.flatMap(
                  CanvasConversionService,
                  (
                    service: CanvasConversion,
                  ): Effect.Effect<CanvasConversion, never, Scope.Scope> =>
                    Effect.acquireRelease(
                      Effect.sync((): CanvasConversion => {
                        canvasStarts += 1
                        releasedCanvas = Option.some(service)
                        return service
                      }),
                      (): Effect.Effect<void> =>
                        Effect.sync((): void => {
                          canvasStops += 1
                        }),
                    ),
                ),
              ).pipe(Layer.provide(canvasLayer(entities))),
          )
        try {
          await Effect.runPromise(
            Effect.scoped(
              Effect.gen(function* (): Generator<
                Effect.Effect<unknown, unknown, Scope.Scope>,
                void,
                never
              > {
                const entities: Layer.Layer<ProjectEntitiesService, unknown> = Layer.effect(
                  ProjectEntitiesService,
                  ProjectEntities.make(files),
                )
                const context: Context.Context<WorkspaceService> = yield* Layer.build(
                  WorkspaceService.layer(
                    'memory',
                    entities,
                    Layer.effect(ProjectWatcherService, ProjectWatcher.make(workspace.fullPath)),
                    Option.none(),
                  ),
                )
                const service: WorkspaceModule = Context.get(context, WorkspaceService)
                assert.equal(starts.mock.calls.length, 1)
                assert.equal(canvasStarts, 1)
                assert.equal(canvasStops, 0)
                const html: string = '<BeCanvas name="共享" width="1080" height="2160"></BeCanvas>'
                const expectedCanvas: string = CanvasConverter.toInlineBase64(
                  CanvasConverter.fromHtml(html),
                )
                const conversion: CanvasConversion = Option.getOrThrow(releasedCanvas)
                const parser: MockInstance<typeof CanvasConverter.fromHtml> = vi.spyOn(
                  CanvasConverter,
                  'fromHtml',
                )
                assert.equal(yield* conversion.htmlToBase64(html), expectedCanvas)
                assert.equal(yield* conversion.htmlToBase64(html), expectedCanvas)
                assert.equal(parser.mock.calls.length, 1)
                parser.mockRestore()
                assert.equal((yield* service.status()).settings.name, '测试')
                const written: number = workspace.writtenBytes()
                const diagnosticRequest: DiagnosticRequest = {
                  entityIds: ['device'],
                  baseline: { files: [], directories: [], fixedInputs: [] },
                }
                const calls: readonly Effect.Effect<unknown, unknown>[] = [
                  service.views.readTs('device'),
                  service.views.readHtml('canvas'),
                  service.views.refresh({
                    baseline: { files: [], directories: [], fixedInputs: [] },
                    updatedEntityIds: ['device'],
                    removedEntityIds: ['canvas'],
                    derivedViewRelativePaths: ['views/零件.ts'],
                    previewDependencies: [],
                  }),
                  service.diagnostics.inspect(diagnosticRequest),
                  service.assets.confirm(
                    new ConfirmAssetTask('task', 'artifact', 'assets/image.png', '确认入库'),
                  ),
                ]
                for (const call of calls) {
                  const result: Result.Result<unknown, unknown> = yield* Effect.result(call)
                  assert.equal(Result.isFailure(result), true)
                  if (Result.isFailure(result))
                    assert.equal(String(result.failure).includes('TODO:'), true)
                }
                assert.equal(workspace.writtenBytes(), written)
              }),
            ),
          )
          assert.equal(canvasStops, 1)
          await assert.rejects(
            Effect.runPromise(
              Option.getOrThrow(releasedCanvas).htmlToBase64(
                '<BeCanvas name="共享" width="1080" height="2160"></BeCanvas>',
              ),
            ),
            /Canvas 转换服务已关闭/u,
          )
        } finally {
          canvas.mockRestore()
          starts.mockRestore()
        }
      } finally {
        await workspace.close()
      }
    })

    test('视图实体编码交付真实 UTF-8 内容', (): void => {
      const text: string = '// 中文\nnamespace 测试 {}\n'
      const syntax: ts.SourceFile = ts.createSourceFile('view.ts', text, ts.ScriptTarget.Latest)
      const view: TsViewEntity = new TsViewEntity(
        'device',
        'views/零件.ts',
        { files: [], directories: [], fixedInputs: [] },
        text,
        syntax,
      )
      assert.equal(view.sourceEntityId, 'device')
      assert.deepEqual(view.encode(), [
        { fileRelativePath: 'views/零件.ts', bytes: new TextEncoder().encode(text) },
      ])
    })

    test('两种 Host 模式拒绝未实现视图，关闭后拒绝查询', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      try {
        await workspace.write('setting.json', 'guid: a\nname: 测试\nmainMap: ""')
        const modes: readonly ViewMode[] = ['files', 'memory']
        for (const mode of modes) {
          const host: Host = await Host.open(workspace.fullPath, mode, pino({ enabled: false }))
          try {
            assert.equal((await host.status()).viewMode, mode)
            await assert.rejects(host.readTs('device'), /TODO:/u)
            await assert.rejects(host.readHtml('canvas'), /TODO:/u)
          } finally {
            await host.close()
          }
          await assert.rejects(host.status(), /Host 已关闭/u)
        }
      } finally {
        await workspace.close()
      }
    })
  }
}
