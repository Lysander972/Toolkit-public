import { readFile, readdir } from 'node:fs/promises'
import { join, extname } from 'node:path'
import { createHash } from 'node:crypto'
import { Option } from 'effect'
import { z } from 'zod'
import Hjson from 'hjson'
import { readSheet } from 'read-excel-file/node'
import type { Row } from 'read-excel-file/node'
import { CanvasConverter } from '../../toolkit/canvas/CanvasConverter.ts'
import { CanvasNode, ColorVar, Var } from '../../toolkit/canvas/CanvasValues.ts'
import type { CanvasDocument } from '../../toolkit/canvas/CanvasValues.ts'
import type { CanvasRenderInput } from '../../canvas_render/RenderContracts.ts'
import type { CanvasColor } from '../../canvas_render/CanvasTypesBase.ts'

/** @anchor preview-assets */
interface PaletteSettings {
  readonly paletteExcel: string
}
/** @anchor preview-assets */
interface GuidMetadata {
  readonly guid: string
}
/** @anchor preview-assets */
interface PaletteColor {
  readonly r: number
  readonly g: number
  readonly b: number
  readonly a: number
}
/** @anchor preview-assets */
interface CanvasSelection {
  readonly sourceRelativePath: string
  readonly outputRelativePath: string
}

/** @anchor preview-assets */
export class CanvasAssets {
  private static readonly selections: readonly CanvasSelection[] = [
    { sourceRelativePath: 'UI_Menu.canvas', outputRelativePath: 'menu' },
    { sourceRelativePath: 'UI_GameOver.canvas', outputRelativePath: 'game_over' },
  ]
  private readonly guids: Set<string> = new Set()
  private readonly colors: Set<string> = new Set()
  private readonly resourceIndex: Map<string, string> = new Map()

  /** @anchor preview-assets */
  public constructor(private readonly projectFullPath: string) {}

  /** @anchor preview-assets */
  public async build(): Promise<void> {
    await this.prepare()
    throw new Error('TODO: Canvas CPU 预览产物生成')
  }

  /** @anchor preview-assets */
  public async prepare(): Promise<readonly PreparedCanvas[]> {
    const entries: readonly string[] = await readdir(this.projectFullPath, { recursive: true })
    for (const entry of entries.filter((entry: string): boolean => entry.endsWith('.guid'))) {
      const source: unknown = Hjson.parse(await readFile(join(this.projectFullPath, entry), 'utf8'))
      const metadataSchema: z.ZodType<GuidMetadata> = z.object({ guid: z.string() })
      const guid: string = typeof source === 'string' ? source : metadataSchema.parse(source).guid
      if (this.resourceIndex.has(guid)) throw new Error(`工程资源 GUID 重复：${guid}`)
      this.resourceIndex.set(guid, entry.slice(0, -'.guid'.length).replaceAll('\\', '/'))
    }
    const documents: CanvasDocument[] = []
    for (const selection of CanvasAssets.selections) {
      const bytes: Buffer = await readFile(join(this.projectFullPath, selection.sourceRelativePath))
      const document: CanvasDocument = CanvasConverter.fromBinary(bytes)
      for (const guid of CanvasConverter.resourceGuids(document)) this.guids.add(guid)
      this.collect(document.root)
      documents.push(document)
    }
    const resources: CanvasRenderInput['resources'][number][] = []
    for (const guid of this.guids) {
      const sourceRelativePath: string = Option.getOrThrowWith(
        Option.fromNullishOr(this.resourceIndex.get(guid)),
        (): Error => new Error(`展示资源 GUID 未命中：${guid}`),
      )
      const extension: string = extname(sourceRelativePath)
      if (!['.jpg', '.jpeg', '.png', '.webp'].includes(extension))
        throw new Error(`展示图片格式不受支持：${sourceRelativePath}`)
      const bytes: Buffer = await readFile(join(this.projectFullPath, sourceRelativePath))
      resources.push({
        guid,
        sourceRelativePath,
        kind: 'image',
        bytes,
        revision: createHash('sha256').update(bytes).digest('hex'),
      })
    }
    const palette: ReadonlyMap<string, CanvasColor> = await this.palette()
    const fontBytes: Buffer = await readFile('res/fonts/Alibaba-Puhui_Medium.ttf')
    return documents.map((document: CanvasDocument, index: number): PreparedCanvas => ({
      outputRelativePath: Option.getOrThrow(Option.fromNullishOr(CanvasAssets.selections[index]))
        .outputRelativePath,
      xml: CanvasConverter.toHtml(document),
      input: {
        document,
        viewport: { width: 1080, height: 2160 },
        resources,
        palette,
        fonts: [
          {
            name: 'ToolkitAlibabaPuHuiTiMedium',
            revision: createHash('sha256').update(fontBytes).digest('hex'),
            bytes: fontBytes,
          },
        ],
        timeSeconds: 0,
      },
    }))
  }

  private collect(node: CanvasNode): void {
    for (const name of node.orderedFields()) {
      const field: Var = Option.getOrThrow(node.field(name))
      if (field instanceof ColorVar && Option.isSome(field.palette))
        this.colors.add(field.palette.value)
    }
    for (const child of node.children()) this.collect(child)
  }

  private async palette(): Promise<ReadonlyMap<string, CanvasColor>> {
    const settingsSchema: z.ZodType<PaletteSettings> = z.object({ paletteExcel: z.string() })
    const settings: PaletteSettings = settingsSchema.parse(
      Hjson.parse(await readFile(join(this.projectFullPath, 'setting.json'), 'utf8')),
    )
    const paletteRelativePath: string = Option.getOrThrow(
      Option.fromNullishOr(this.resourceIndex.get(settings.paletteExcel)),
    )
    const rows: readonly Row[] = await readSheet(
      join(this.projectFullPath, paletteRelativePath),
      1,
      { trim: false },
    )
    const headers: Row = Option.getOrThrow(Option.fromNullishOr(rows[0]))
    const nameColumn: number = headers.indexOf('name')
    const colorColumn: number = headers.indexOf('color')
    if (nameColumn < 0 || colorColumn < 0) throw new Error('颜色表缺少 name 或 color 列')
    const palette: Record<string, PaletteColor> = {}
    const channelsSchema: z.ZodType<readonly number[]> = z
      .array(z.coerce.number().int().min(0).max(255))
      .length(4)
    for (const row of rows.slice(3)) {
      const cell: Option.Option<unknown> = Option.fromNullishOr(row[nameColumn])
      if (Option.isNone(cell)) continue
      const name: string = z.string().parse(cell.value)
      if (this.colors.has(name)) {
        const channels: readonly number[] = channelsSchema.parse(
          z.string().parse(row[colorColumn]).split(';'),
        )
        const [r, g, b, a]: readonly number[] = channels
        palette[name] = {
          r: Option.getOrThrow(Option.fromNullishOr(r)),
          g: Option.getOrThrow(Option.fromNullishOr(g)),
          b: Option.getOrThrow(Option.fromNullishOr(b)),
          a: Option.getOrThrow(Option.fromNullishOr(a)),
        }
      }
    }
    for (const name of this.colors)
      if (!Object.hasOwn(palette, name)) throw new Error(`展示颜色未命中：${name}`)
    return new Map(
      Object.entries(palette).map(
        ([name, color]: [string, PaletteColor]): [string, CanvasColor] => [
          name,
          { ...color, palette: Option.none() },
        ],
      ),
    )
  }
}

/** @anchor preview-assets */
export interface PreparedCanvas {
  readonly outputRelativePath: string
  readonly xml: string
  readonly input: CanvasRenderInput
}
