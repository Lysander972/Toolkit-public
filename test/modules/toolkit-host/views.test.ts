import assert from 'node:assert/strict'
import { test } from 'vitest'
import { Context, Effect, Layer, Result, Scope } from 'effect'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import { CanvasConversionService } from '../../../src/toolkit/conversion/canvas/CanvasConversion.ts'
import { TsViewConversionService } from '../../../src/toolkit/conversion/ts/TsViewConversion.ts'
import { HtmlViewConversionService } from '../../../src/toolkit/conversion/html/HtmlViewConversion.ts'
import { ProjectEntitiesService } from '../../../src/toolkit/entities/ProjectEntities.ts'
import { HtmlViewEntity } from '../../../src/toolkit/entities/ToolkitEntity.ts'
import {
  ProjectViewsService,
  type ProjectViews,
  type ViewMode,
  type ViewRefreshInput,
  type ViewRefreshResult,
  type ViewPreviewDependency,
} from '../../../src/toolkit/views/ProjectViews.ts'
import { CanvasTestWorkspace } from '../toolkit-file-support/CanvasTestWorkspace.ts'

/**
 * @anchor view-service-contract
 * @anchor view-refresh-data-contract
 * @anchor preview-view-impact
 * @anchor derived-view-delivery
 */
export class ProjectViewsTests {
  private constructor() {}

  static {
    test('两种模式的派生纠偏及预览影响空壳明确失败，保留输入且零写入', async (): Promise<void> => {
      const modes: readonly ViewMode[] = ['files', 'memory']
      for (const mode of modes) {
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
              const canvas: Layer.Layer<CanvasConversionService> = Layer.succeed(
                CanvasConversionService,
                fixture.service,
              )
              const built: Context.Context<ProjectViewsService> = yield* Layer.build(
                ProjectViewsService.layer(
                  mode,
                  Layer.succeed(ProjectEntitiesService, fixture.entities),
                  TsViewConversionService.layer(canvas),
                  HtmlViewConversionService.layer(canvas),
                ),
              )
              const service: ProjectViews = Context.get(built, ProjectViewsService)
              const source: ReturnType<typeof CanvasTestWorkspace.entity> =
                CanvasTestWorkspace.entity(
                  CanvasConverter.fromHtml(
                    '<BeCanvas name="展示" width="1080" height="2160"></BeCanvas>',
                  ),
                )
              const preview: ViewPreviewDependency = {
                sourceEntityId: source.id,
                baseline: source.baseline,
              }
              const inputs: readonly ViewRefreshInput[] = [
                {
                  baseline: source.baseline,
                  updatedEntityIds: [],
                  removedEntityIds: [],
                  derivedViewRelativePaths: ['views/展示.html'],
                  previewDependencies: [preview],
                },
                {
                  baseline: source.baseline,
                  updatedEntityIds: ['resource:图片.png'],
                  removedEntityIds: ['canvas:已删除.canvas'],
                  derivedViewRelativePaths: [],
                  previewDependencies: [preview],
                },
              ]
              const written: number = fixture.workspace.writtenBytes()
              for (const input of inputs) {
                const before: ViewRefreshInput = structuredClone(input)
                const result: Result.Result<ViewRefreshResult, unknown> = yield* Effect.result(
                  service.refresh(input),
                )
                assert.ok(Result.isFailure(result))
                assert.ok(result.failure instanceof Error)
                assert.equal(
                  result.failure.message,
                  'TODO: 按实体读取基准主动计算视图影响并交付更新与删除结果',
                )
                assert.deepEqual(input, before)
              }
              const view: HtmlViewEntity = new HtmlViewEntity(
                source.id,
                'views/展示.html',
                source.baseline,
                CanvasConverter.toHtml(source.document),
                source.document,
              )
              const original: string = JSON.stringify(view)
              const calls: readonly Effect.Effect<unknown, unknown>[] = [
                service.readTs('device:机械/测试.mech_/0'),
                service.readHtml(source.id),
                service.publish(view),
              ]
              for (const call of calls) {
                const result: Result.Result<unknown, unknown> = yield* Effect.result(call)
                assert.ok(Result.isFailure(result))
                assert.ok(result.failure instanceof Error)
                assert.match(result.failure.message, /^TODO:/u)
              }
              assert.equal(JSON.stringify(view), original)
              assert.equal(fixture.workspace.writtenBytes(), written)
            }),
          ),
        )
      }
    })
  }
}
