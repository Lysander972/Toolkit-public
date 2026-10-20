import assert from 'node:assert/strict'
import { test } from 'vitest'
import { Context, Effect, Layer, Result, Scope } from 'effect'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import { CanvasConversionService } from '../../../src/toolkit/conversion/canvas/CanvasConversion.ts'
import {
  HtmlViewConversionService,
  type HtmlViewConversion,
} from '../../../src/toolkit/conversion/html/HtmlViewConversion.ts'
import { HtmlViewEntity } from '../../../src/toolkit/entities/ToolkitEntity.ts'
import type { CanvasEntity } from '../../../src/toolkit/entities/CanvasEntity.ts'
import { CanvasTestWorkspace } from '../toolkit-file-support/CanvasTestWorkspace.ts'

/** @anchor html-service-contract */
export class HtmlConversionTests {
  private constructor() {}

  static {
    test('独立 HTML 双向空壳明确失败，保留输入且零写入', async (): Promise<void> => {
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
            const built: Context.Context<HtmlViewConversionService> = yield* Layer.build(
              HtmlViewConversionService.layer(
                Layer.succeed(CanvasConversionService, fixture.service),
              ),
            )
            const service: HtmlViewConversion = Context.get(built, HtmlViewConversionService)
            const canvas: CanvasEntity = CanvasTestWorkspace.entity(
              CanvasConverter.fromHtml(
                '<BeCanvas name="原布局" width="1080" height="2160"><BeUILabel name="文本" text="旧值"></BeUILabel></BeCanvas>',
              ),
            )
            const view: HtmlViewEntity = new HtmlViewEntity(
              canvas.id,
              'views/界面.html',
              canvas.baseline,
              '<html lang="zh-CN"><body><BeCanvas name="新布局" width="1080" height="2160"><BeUILabel name="文本" text="新值"></BeUILabel></BeCanvas></body></html>',
              canvas.document,
            )
            const before: string = JSON.stringify([canvas, view])
            const bytes: Uint8Array = CanvasConverter.toBinary(canvas.document)
            const written: number = fixture.workspace.writtenBytes()
            const result: Result.Result<HtmlViewEntity, unknown> = yield* Effect.result(
              service.toHtml(canvas, view.viewRelativePath),
            )
            assert.ok(Result.isFailure(result))
            assert.ok(result.failure instanceof Error)
            assert.equal(result.failure.message, 'TODO: 独立 Canvas 实体到完整 HTML 视图的装配')
            const reversed: Result.Result<CanvasEntity, unknown> = yield* Effect.result(
              service.fromHtml(view, canvas),
            )
            assert.ok(Result.isFailure(reversed))
            assert.ok(reversed.failure instanceof Error)
            assert.equal(
              reversed.failure.message,
              'TODO: 完整 HTML 视图到独立 Canvas 实体的反向转换',
            )
            assert.equal(JSON.stringify([canvas, view]), before)
            assert.deepEqual(CanvasConverter.toBinary(canvas.document), bytes)
            assert.equal(fixture.workspace.writtenBytes(), written)
          }),
        ),
      )
    })
  }
}
