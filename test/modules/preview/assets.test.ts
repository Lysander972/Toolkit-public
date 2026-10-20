import assert from 'node:assert/strict'
import { mkdir, mkdtemp, rm } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import { test } from 'vitest'
import { Option } from 'effect'
import { readSheet } from 'read-excel-file/node'
import type { Row } from 'read-excel-file/node'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import { CanvasAssets } from '../../../src/preview/build/CanvasAssets.ts'
import type { PreparedCanvas } from '../../../src/preview/build/CanvasAssets.ts'
import { SiteFiles } from '../../../src/preview/build/SiteFiles.ts'

/** @anchor preview-assets */
interface AssetCase {
  readonly name: string
  readonly extension: string
  readonly metadata: boolean
  readonly color: string
  readonly error: Option.Option<string>
}

/** @anchor preview-assets */
export class PreviewAssetsTest {
  private constructor() {}

  static {
    test('Canvas 资源最小集合、GUID 元数据、调色板与严格失败路径', async (): Promise<void> => {
      // @non-manual-path 只读颜色表夹具，用于验证真实 XLSX 列与通道解析。
      const paletteFullPath: string = resolve('test/data/projects/水果切切切/表格/颜色表.xlsx')
      const rows: readonly Row[] = await readSheet(paletteFullPath, 1, { trim: false })
      const header: Row = Option.getOrThrow(Option.fromNullishOr(rows[0]))
      const row: Row = Option.getOrThrow(Option.fromNullishOr(rows[3]))
      const color: string = String(row[header.indexOf('name')])
      const channels: number[] = String(row[header.indexOf('color')]).split(';').map(Number)
      const cases: readonly AssetCase[] = [
        { name: 'string', extension: '.png', metadata: true, color, error: Option.none() },
        { name: 'object', extension: '.png', metadata: true, color, error: Option.none() },
        {
          name: 'missing-guid',
          extension: '.png',
          metadata: false,
          color,
          error: Option.some('展示资源 GUID 未命中：image'),
        },
        {
          name: 'format',
          extension: '.txt',
          metadata: true,
          color,
          error: Option.some('展示图片格式不受支持：image.txt'),
        },
        {
          name: 'color',
          extension: '.png',
          metadata: true,
          color: 'missing-preview-color',
          error: Option.some('展示颜色未命中：missing-preview-color'),
        },
      ]
      await mkdir(resolve('_build/tests'), { recursive: true })
      for (const scenario of cases) {
        const directoryFullPath: string = await mkdtemp(resolve('_build/tests/preview-assets-'))
        const input: SiteFiles = new SiteFiles(join(directoryFullPath, 'project'))
        try {
          await input.copy('palette.xlsx', paletteFullPath)
          await input.write('palette.xlsx.guid', JSON.stringify('palette'))
          await input.write('setting.json', JSON.stringify({ paletteExcel: 'palette' }))
          await input.write('unused.png', 'unused asset')
          await input.write('unused.png.guid', JSON.stringify('unused'))
          await input.write('image' + scenario.extension, 'selected asset')
          if (scenario.metadata)
            await input.write(
              'image' + scenario.extension + '.guid',
              JSON.stringify(scenario.name === 'object' ? { guid: 'image' } : 'image'),
            )
          const bytes: Uint8Array = CanvasConverter.toBinary(
            CanvasConverter.fromHtml(
              `<BeCanvas name="preview" width="1080" height="2160"><BeUIRawImage name="image" fileguid="image"></BeUIRawImage><BeUIRect name="color" color="palette(${scenario.color})"></BeUIRect></BeCanvas>`,
            ),
          )
          for (const name of ['UI_Menu.canvas', 'UI_GameOver.canvas'])
            await input.bytes(name, bytes)
          const prepared: Promise<readonly PreparedCanvas[]> = new CanvasAssets(
            join(directoryFullPath, 'project'),
          ).prepare()
          if (Option.isSome(scenario.error)) {
            await assert.rejects(prepared, { message: scenario.error.value })
          } else {
            const canvases: readonly PreparedCanvas[] = await prepared
            assert.deepEqual(
              canvases.map((canvas: PreparedCanvas): string => canvas.outputRelativePath),
              ['menu', 'game_over'],
            )
            for (const canvas of canvases) {
              assert.deepEqual(canvas.input.viewport, { width: 1080, height: 2160 })
              assert.equal(canvas.input.timeSeconds, 0)
              assert.equal(canvas.input.resources.length, 1)
              const resource: (typeof canvas.input.resources)[number] = Option.getOrThrow(
                Option.fromNullishOr(canvas.input.resources[0]),
              )
              assert.equal(resource.guid, 'image')
              assert.equal(resource.sourceRelativePath, 'image.png')
              assert.equal(Buffer.from(resource.bytes).toString(), 'selected asset')
              assert.deepEqual(
                [...canvas.input.palette],
                [
                  [
                    color,
                    {
                      r: channels[0],
                      g: channels[1],
                      b: channels[2],
                      a: channels[3],
                      palette: Option.none(),
                    },
                  ],
                ],
              )
              assert.deepEqual(
                CanvasConverter.toBinary(CanvasConverter.fromHtml(canvas.xml)),
                bytes,
              )
              assert.equal(canvas.input.fonts.length, 1)
              assert.ok(
                Option.getOrThrow(Option.fromNullishOr(canvas.input.fonts[0])).bytes.byteLength > 0,
              )
            }
            await assert.rejects(new CanvasAssets(join(directoryFullPath, 'project')).build(), {
              message: 'TODO: Canvas CPU 预览产物生成',
            })
          }
        } finally {
          input.report()
          await rm(directoryFullPath, { recursive: true })
        }
      }
    })
  }
}
