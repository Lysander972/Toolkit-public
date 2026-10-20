import assert from 'node:assert/strict'
import { readFile, readdir } from 'node:fs/promises'
import { join } from 'node:path'
import { test } from 'vitest'
import { Context, Effect, Layer, Result, Scope } from 'effect'
import type { CanvasKit } from 'canvaskit-wasm'
import { CanvasConverter } from '@toolkit/canvas'
import pino from 'pino'
import {
  CanvasPreviewService,
  type CanvasPreview,
  type CanvasPreviewRequest,
} from '../../../src/toolkit/preview/CanvasPreview.ts'
import {
  ProjectEntities,
  ProjectEntitiesService,
} from '../../../src/toolkit/entities/ProjectEntities.ts'
import { HtmlViewEntity } from '../../../src/toolkit/entities/ToolkitEntity.ts'
import { ProjectFiles } from '../../../src/toolkit/files/ProjectFiles.ts'
import { Host } from '../../../src/toolkit/host/Host.ts'
import type { ViewMode } from '../../../src/toolkit/views/ProjectViews.ts'
import { FileTestWorkspace } from '../toolkit-file-support/FileTestWorkspace.ts'
import { CpuTestBackend } from '../canvas-render-support/CpuTestBackend.ts'

/**
 * @anchor preview-service-contract
 * @anchor preview-cpu-render-boundary
 * @anchor preview-render-dependencies
 * @anchor host-preview-dispatch
 */
export class CanvasPreviewTest {
  private constructor() {}

  static {
    test('仅注入真实实体即可装配预览，空壳明确失败且请求及工程保持原状', async (): Promise<void> => {
      const backend: CanvasKit = await CpuTestBackend.initialize()
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      try {
        const files: ProjectFiles = workspace.files
        await workspace.write('setting.json', 'guid: preview\nname: 预览\nmainMap: ""')
        const text: string =
          '<BeCanvas name="预览" width="2" height="2"><BeUILabel name="正文" text="预览输入"></BeUILabel></BeCanvas>'
        const document: ReturnType<typeof CanvasConverter.fromHtml> = CanvasConverter.fromHtml(text)
        const sourceEntityId: string = 'a'.repeat(32)
        await workspace.write('ui.canvas', CanvasConverter.toBinary(document))
        await workspace.write('ui.canvas.guid', sourceEntityId)
        const revision: string = (await Effect.runPromise(files.metadata('ui.canvas'))).revision
        const guidRevision: string = (await Effect.runPromise(files.metadata('ui.canvas.guid')))
          .revision
        const entries: readonly string[] = await readdir(workspace.fullPath)
        const view: HtmlViewEntity = new HtmlViewEntity(
          sourceEntityId,
          'views/预览.html',
          {
            files: [
              { relativePath: 'ui.canvas', revision },
              { relativePath: 'ui.canvas.guid', revision: guidRevision },
            ],
            directories: [{ relativePath: '.', entries }],
            fixedInputs: [{ name: 'font:Alibaba-Puhui_Medium', revision: 'font-1' }],
          },
          text,
          document,
        )
        const request: CanvasPreviewRequest = Object.freeze({
          view,
          viewport: Object.freeze({ width: 2, height: 2 }),
          fonts: [
            {
              name: 'Alibaba-Puhui_Medium',
              revision: 'font-1',
              bytes: await readFile(
                new URL('../../../res/fonts/Alibaba-Puhui_Medium.ttf', import.meta.url),
              ),
            },
          ],
          timeSeconds: 1.25,
        })
        const original: Uint8Array = CanvasConverter.toBinary(view.document)
        const before: CanvasPreviewRequest = structuredClone(request)
        const written: number = workspace.writtenBytes()
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
              const context: Context.Context<CanvasPreviewService> = yield* Layer.build(
                CanvasPreviewService.layer(entities),
              )
              const preview: CanvasPreview = Context.get(context, CanvasPreviewService)
              const result: Result.Result<unknown, unknown> = yield* Effect.result(
                preview.render(request, backend),
              )
              assert.ok(Result.isFailure(result))
              assert.ok(result.failure instanceof Error)
              assert.equal(
                result.failure.message,
                'TODO: 按具名视图及实际实体资源基准交付 CPU PNG 预览',
              )
            }),
          ),
        )
        assert.deepEqual(CanvasConverter.toBinary(view.document), original)
        assert.equal(view.text, text)
        assert.equal(view.sourceEntityId, sourceEntityId)
        assert.equal(view.viewRelativePath, 'views/预览.html')
        assert.deepEqual(request.viewport, { width: 2, height: 2 })
        assert.deepEqual(structuredClone(request), before)
        const modes: readonly ViewMode[] = ['files', 'memory']
        for (const mode of modes) {
          const host: Host = await Host.open(workspace.fullPath, mode, pino({ enabled: false }))
          const controller: AbortController = new AbortController()
          try {
            await assert.rejects(host.render(request, backend, controller.signal), {
              message: 'TODO: 按具名视图及实际实体资源基准交付 CPU PNG 预览',
            })
            assert.deepEqual(structuredClone(request), before)
          } finally {
            await host.close()
          }
          await assert.rejects(host.render(request, backend, controller.signal), /Host 已关闭/u)
        }
        assert.deepEqual(await readdir(workspace.fullPath), entries)
        assert.deepEqual(
          new Uint8Array(await readFile(join(workspace.fullPath, 'ui.canvas'))),
          original,
        )
        assert.equal((await Effect.runPromise(files.metadata('ui.canvas'))).revision, revision)
        assert.equal(
          await readFile(join(workspace.fullPath, 'ui.canvas.guid'), 'utf8'),
          sourceEntityId,
        )
        assert.equal(
          (await Effect.runPromise(files.metadata('ui.canvas.guid'))).revision,
          guidRevision,
        )
        assert.equal(workspace.writtenBytes(), written)
        assert.equal(
          new TextDecoder('utf-8', { fatal: true }).decode(
            await Effect.runPromise(files.read('setting.json')),
          ),
          'guid: preview\nname: 预览\nmainMap: ""',
        )
      } finally {
        await workspace.close()
      }
    })
  }
}
