import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { beforeAll, test } from 'vitest'
import { Option } from 'effect'
import type { CanvasKit, Surface, Canvas, Image, Paint, ImageInfo } from 'canvaskit-wasm'
import { TextureAtlas } from '@pixi-spine/base'
import { AtlasAttachmentLoader, Skeleton, SkeletonJson } from '@pixi-spine/runtime-3.8'
import type { Bone, SkeletonData } from '@pixi-spine/runtime-3.8'
import { CanvasConverter } from '@toolkit/canvas'
import { CanvasRenderer } from '../../../src/canvas_render/CanvasRenderer.ts'
import type { CanvasRenderInput } from '../../../src/canvas_render/RenderContracts.ts'
import { CanvasDocumentValidator } from '../../../src/canvas_render/CanvasDocumentValidator.ts'
import { CpuRenderSession } from '../../../src/canvas_render/CpuRenderSession.ts'
import { CpuRasterSurface } from '../../../src/canvas_render/CpuRasterSurface.ts'
import { RichTextParser } from '../../../src/canvas_render/CanvasRuntimeUtils.ts'
import { CpuTestBackend } from '../canvas-render-support/CpuTestBackend.ts'

/**
 * @anchor canvas-render-test-baseline
 * @anchor canvas-render-persistence-engine-convergence
 * @anchor asset-editing-capabilities
 */
export class CpuBoundaryTest {
  private static backend: Option.Option<CanvasKit> = Option.none()

  private constructor() {}

  static {
    beforeAll(async (): Promise<void> => {
      CpuBoundaryTest.backend = Option.some(await CpuTestBackend.initialize())
    })

    test('Spine 3.8 JSON 在 Node 内解析并产生 Y 向下的父子骨骼姿态', (): void => {
      const atlas: TextureAtlas = new TextureAtlas()
      try {
        const parser: SkeletonJson = new SkeletonJson(new AtlasAttachmentLoader(atlas))
        const data: SkeletonData = parser.readSkeletonData({
          skeleton: { spine: '3.8.99' },
          bones: [
            { name: 'root', x: 3, y: 4, rotation: 90 },
            { name: 'child', parent: 'root', x: 10 },
          ],
        })
        const skeleton: Skeleton = new Skeleton(data)
        skeleton.updateWorldTransform()
        const transformed: Bone = Option.getOrThrow(Option.fromNullishOr(skeleton.bones[1]))
        assert.ok(Math.abs(transformed.worldX - 3) < 0.0001)
        assert.ok(Math.abs(transformed.worldY + 14) < 0.0001)
      } finally {
        atlas.dispose()
      }
    })

    test('独立软件 surface 的 PNG 尺寸、像素和重复编码一致', (): void => {
      const backend: CanvasKit = Option.getOrThrow(CpuBoundaryTest.backend)
      const encoded: Uint8Array[] = []
      for (let renderIndex: number = 0; renderIndex < 2; renderIndex += 1) {
        const surface: Surface = Option.getOrThrow(Option.fromNullishOr(backend.MakeSurface(2, 2)))
        try {
          const canvas: Canvas = surface.getCanvas()
          canvas.clear(backend.Color(0, 0, 255, 1))
          const paint: Paint = new backend.Paint()
          try {
            paint.setColor(backend.Color(255, 0, 0, 1))
            canvas.drawRect(backend.XYWHRect(0, 0, 1, 1), paint)
          } finally {
            paint.delete()
          }
          surface.flush()
          const snapshot: Image = surface.makeImageSnapshot()
          try {
            const png: Uint8Array = Option.getOrThrow(
              Option.fromNullishOr(snapshot.encodeToBytes(backend.ImageFormat.PNG)),
            )
            encoded.push(png)
            const decoded: Image = Option.getOrThrow(
              Option.fromNullishOr(backend.MakeImageFromEncoded(png)),
            )
            try {
              assert.equal(decoded.width(), 2)
              assert.equal(decoded.height(), 2)
              const info: ImageInfo = {
                width: 2,
                height: 2,
                colorType: backend.ColorType.RGBA_8888,
                alphaType: backend.AlphaType.Unpremul,
                colorSpace: backend.ColorSpace.SRGB,
              }
              assert.deepEqual(
                Array.from(Option.getOrThrow(Option.fromNullishOr(decoded.readPixels(0, 0, info)))),
                [255, 0, 0, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255],
              )
            } finally {
              decoded.delete()
            }
          } finally {
            snapshot.delete()
          }
        } finally {
          surface.dispose()
        }
      }
      assert.deepEqual(
        Option.getOrThrow(Option.fromNullishOr(encoded[0])),
        Option.getOrThrow(Option.fromNullishOr(encoded[1])),
      )
    })

    test('软件 PNG 编解码保留透明、半透明和不透明像素', (): void => {
      const backend: CanvasKit = Option.getOrThrow(CpuBoundaryTest.backend)
      const surface: Surface = Option.getOrThrow(Option.fromNullishOr(backend.MakeSurface(3, 1)))
      try {
        const canvas: Canvas = surface.getCanvas()
        canvas.clear(backend.Color(0, 0, 0, 0))
        const paint: Paint = new backend.Paint()
        try {
          paint.setColor(backend.Color(255, 0, 0, 0.5))
          canvas.drawRect(backend.XYWHRect(1, 0, 1, 1), paint)
          paint.setColor(backend.Color(0, 255, 0, 1))
          canvas.drawRect(backend.XYWHRect(2, 0, 1, 1), paint)
        } finally {
          paint.delete()
        }
        surface.flush()
        const snapshot: Image = surface.makeImageSnapshot()
        try {
          const png: Uint8Array = Option.getOrThrow(
            Option.fromNullishOr(snapshot.encodeToBytes(backend.ImageFormat.PNG)),
          )
          const decoded: Image = Option.getOrThrow(
            Option.fromNullishOr(backend.MakeImageFromEncoded(png)),
          )
          try {
            assert.equal(decoded.width(), 3)
            assert.equal(decoded.height(), 1)
            const info: ImageInfo = {
              width: 3,
              height: 1,
              colorType: backend.ColorType.RGBA_8888,
              alphaType: backend.AlphaType.Unpremul,
              colorSpace: backend.ColorSpace.SRGB,
            }
            assert.deepEqual(
              Array.from(Option.getOrThrow(Option.fromNullishOr(decoded.readPixels(0, 0, info)))),
              [0, 0, 0, 0, 255, 0, 0, 128, 0, 255, 0, 255],
            )
          } finally {
            decoded.delete()
          }
        } finally {
          snapshot.delete()
        }
      } finally {
        surface.dispose()
      }
    })

    test('业务空壳拒绝交付 PNG，失败保持文档及请求输入不变', async (): Promise<void> => {
      const backend: CanvasKit = Option.getOrThrow(CpuBoundaryTest.backend)
      const input: CanvasRenderInput = {
        document: CanvasConverter.fromHtml(
          '<BeCanvas name="空壳基线" width="2" height="2"></BeCanvas>',
        ),
        viewport: Object.freeze({ width: 2, height: 2 }),
        resources: [],
        palette: new Map(),
        fonts: [],
        timeSeconds: 0,
      }
      const original: Uint8Array = CanvasConverter.toBinary(input.document)
      const before: CanvasRenderInput = structuredClone(input)
      await assert.rejects(
        CanvasRenderer.render(input, backend),
        (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
      )
      assert.deepEqual(CanvasConverter.toBinary(input.document), original)
      assert.deepEqual(input.viewport, { width: 2, height: 2 })
      assert.deepEqual(input.resources, [])
      assert.deepEqual(input.fonts, [])
      assert.equal(input.palette.size, 0)
      assert.equal(input.timeSeconds, 0)
      await assert.rejects(
        CpuRenderSession.create(input, backend),
        (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
      )
      await assert.rejects(
        CpuRasterSurface.create(backend, input.viewport),
        (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
      )
      assert.throws(
        (): ReturnType<typeof CanvasDocumentValidator.validate> =>
          CanvasDocumentValidator.validate(input.document),
        (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
      )
      assert.throws(
        (): ReturnType<typeof RichTextParser.parse> => RichTextParser.parse('<size=12>正文</size>'),
        (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
      )
      assert.deepEqual(structuredClone(input), before)
    })

    test('非空文档和资源请求重复失败后保留字节、标识、调色板与显式时间', async (): Promise<void> => {
      const backend: CanvasKit = Option.getOrThrow(CpuBoundaryTest.backend)
      const [imageBytes, fontBytes]: readonly [Buffer, Buffer] = await Promise.all([
        readFile(
          new URL('../../../res/frontend/assets/setup-icons/typescript.png', import.meta.url),
        ),
        readFile(new URL('../../../res/fonts/Alibaba-Puhui_Medium.ttf', import.meta.url)),
      ])
      const input: CanvasRenderInput = {
        document: CanvasConverter.fromHtml(
          '<BeCanvas name="非空请求" width="16" height="12"><BeUIRawImage name="图片" fileguid="11111111111111111111111111111111"></BeUIRawImage><BeUILabel name="文本" text="保真正文"></BeUILabel></BeCanvas>',
        ),
        viewport: { width: 16, height: 12 },
        resources: [
          {
            guid: '11111111111111111111111111111111',
            sourceRelativePath: '图片/图标.png',
            revision: 'image-1',
            kind: 'image',
            bytes: imageBytes,
          },
        ],
        palette: new Map([['主色', { r: 0.25, g: 0.5, b: 0.75, a: 0.8, palette: Option.none() }]]),
        fonts: [
          {
            name: 'Alibaba-Puhui_Medium',
            revision: 'font-1',
            bytes: fontBytes,
          },
        ],
        timeSeconds: 1.25,
      }
      const original: Uint8Array = CanvasConverter.toBinary(input.document)
      const before: CanvasRenderInput = structuredClone(input)
      for (let requestIndex: number = 0; requestIndex < 2; requestIndex += 1) {
        await assert.rejects(
          CanvasRenderer.render(input, backend),
          (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
        )
        assert.deepEqual(CanvasConverter.toBinary(input.document), original)
        assert.deepEqual(structuredClone(input), before)
      }
    })
  }
}
