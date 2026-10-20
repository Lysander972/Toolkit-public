import assert from 'node:assert/strict'
import { test } from 'vitest'
import { Option } from 'effect'
import { CanvasConverter } from '@toolkit/canvas'
import type { CanvasKit, Surface, Image, ImageInfo } from 'canvaskit-wasm'
import { CanvasFieldValues } from '../../../src/canvas_render/CanvasFieldValues.ts'
import { CanvasNodeDispatch } from '../../../src/canvas_render/CanvasNodeDispatch.ts'
import { CpuCanvasNodes } from '../../../src/canvas_render/CpuCanvasNodes.ts'
import { CanvasImageComposition } from '../../../src/canvas_render/CanvasImageComposition.ts'
import { CpuCanvasLayout } from '../../../src/canvas_render/CpuCanvasLayout.ts'
import { CpuRenderSession } from '../../../src/canvas_render/CpuRenderSession.ts'
import { CpuRasterSurface } from '../../../src/canvas_render/CpuRasterSurface.ts'
import { CpuTestBackend } from '../canvas-render-support/CpuTestBackend.ts'

type RawImageInput = Parameters<typeof CanvasFieldValues.resolve<'UIRawImage'>>[0]

/** @anchor canvas-render-stage-baseline */
export class CpuStagesTest {
  private constructor() {}

  static {
    test('内部阶段空壳明确失败并保持节点、默认值及独占 surface 不变', async (): Promise<void> => {
      const backend: CanvasKit = await CpuTestBackend.initialize()
      const input: CpuRenderSession['input'] = {
        document: CanvasConverter.fromHtml(
          '<BeCanvas name="阶段端口" width="2" height="2"><BeUIRawImage name="图片" fileguid="11111111111111111111111111111111"></BeUIRawImage></BeCanvas>',
        ),
        viewport: { width: 2, height: 2 },
        resources: [],
        palette: new Map(),
        fonts: [],
        timeSeconds: 0.5,
      }
      const effective: RawImageInput = {
        name: '图片',
        guid: Option.none(),
        pos: Option.some({ x: 1, y: 2, z: 3 }),
        legacyPos: Option.none(),
        size: Option.some({ x: 2, y: 2, z: 0 }),
        parentAnchor: Option.none(),
        anchor: Option.none(),
        widthExtend: Option.none(),
        heightExtend: Option.none(),
        rot: Option.none(),
        color: Option.none(),
        shadowColor: Option.none(),
        shadewDistance: Option.none(),
        outlineColor: Option.none(),
        outlineDistance: Option.none(),
        fileGuid: Option.some('11111111111111111111111111111111'),
        softMaskGuid: Option.none(),
      }
      const defaults: RawImageInput = {
        ...effective,
        name: '默认图片',
        pos: Option.none(),
        fileGuid: Option.none(),
      }
      const enabled: ReadonlySet<keyof RawImageInput> = new Set(['pos', 'fileGuid'])
      const node: Parameters<typeof CanvasNodeDispatch.draw>[0] = {
        source: Option.getOrThrow(Option.fromNullishOr(input.document.root.children()[0])),
        effective,
        textRuns: [],
        nodeIndices: [0],
        layout: {
          left: 0,
          top: 0,
          width: 2,
          height: 2,
          rot: 0,
          transformOriginX: 0,
          transformOriginY: 0,
        },
        resolvedLayoutSize: { x: 2, y: 2, z: 0 },
        children: [],
      }
      const before: unknown = structuredClone({ input, node, defaults, enabled })
      const surface: Surface = Option.getOrThrow(Option.fromNullishOr(backend.MakeSurface(2, 2)))
      try {
        surface.getCanvas().clear(backend.Color(0, 0, 255, 1))
        const session: CpuRenderSession = {
          input,
          surface: {
            canvas: surface.getCanvas(),
            encodePng(): Promise<Uint8Array> {
              return CpuRasterSurface.prototype.encodePng.call(this)
            },
            close(): void {
              CpuRasterSurface.prototype.close.call(this)
            },
          },
          finish(): ReturnType<CpuRenderSession['finish']> {
            return CpuRenderSession.prototype.finish.call(this)
          },
          close(): Promise<void> {
            return CpuRenderSession.prototype.close.call(this)
          },
        }
        assert.throws(
          (): RawImageInput =>
            CanvasFieldValues.resolve<'UIRawImage'>(effective, defaults, enabled),
          (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
        )
        assert.throws(
          (): ReturnType<typeof CpuCanvasLayout.layout> =>
            CpuCanvasLayout.layout(input.document, input.viewport, session),
          (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
        )
        assert.throws(
          (): ReturnType<typeof CpuCanvasLayout.diagnostic> => CpuCanvasLayout.diagnostic(node),
          (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
        )
        await assert.rejects(
          CanvasNodeDispatch.draw(node, session),
          (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
        )
        await assert.rejects(
          CpuCanvasNodes.draw(node, session),
          (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
        )
        await assert.rejects(
          CanvasImageComposition.draw(node, session),
          (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
        )
        await assert.rejects(
          session.finish(),
          (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
        )
        await assert.rejects(
          session.surface.encodePng(),
          (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
        )
        await assert.rejects(
          session.close(),
          (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
        )
        assert.throws(
          (): void => {
            session.surface.close()
          },
          (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
        )
        assert.deepEqual(structuredClone({ input, node, defaults, enabled }), before)
        surface.flush()
        const snapshot: Image = surface.makeImageSnapshot()
        try {
          const info: ImageInfo = {
            width: 2,
            height: 2,
            colorType: backend.ColorType.RGBA_8888,
            alphaType: backend.AlphaType.Unpremul,
            colorSpace: backend.ColorSpace.SRGB,
          }
          assert.deepEqual(
            Array.from(Option.getOrThrow(Option.fromNullishOr(snapshot.readPixels(0, 0, info)))),
            [0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255],
          )
        } finally {
          snapshot.delete()
        }
      } finally {
        surface.dispose()
      }
    })
  }
}
