import { Option } from 'effect'
import { Base64 } from 'effect/encoding'
import { CanvasBinary } from './CanvasBinary.ts'
import { CanvasDocument, CanvasNode, ListVar, StringVar, Var } from './CanvasValues.ts'
import { CanvasHtml } from './CanvasHtml.ts'
import { CanvasHjson, HjsonWriter } from './CanvasHjson.ts'

/** @anchor canvas-convert */
export class CanvasConverter {
  private constructor() {}

  /** @anchor canvas-convert */
  public static fromBinary(bytes: Uint8Array): CanvasDocument {
    return CanvasBinary.fromBytes(bytes, '')
  }

  /** @anchor canvas-convert */
  public static toBinary(document: CanvasDocument): Uint8Array {
    CanvasConverter.requireRoot(document)
    return CanvasBinary.toFileBytes(document)
  }

  /** @anchor canvas-convert */
  public static fromInline(text: string): CanvasDocument {
    if (text.startsWith('canvas:')) return CanvasHjson.base64(text)
    if (text.startsWith('==hjson:')) return CanvasHjson.hjson(text)
    throw new Error('内联 Canvas 必须以 canvas: 或 ==hjson: 开始')
  }

  /** @anchor canvas-convert */
  public static toInlineBase64(document: CanvasDocument): string {
    CanvasConverter.requireRoot(document)
    return `canvas:${document.name}=${Base64.encode(CanvasBinary.toInlineBytes(document.root))}`
  }

  /** @anchor canvas-convert */
  public static toInlineHjson(document: CanvasDocument): string {
    CanvasConverter.requireRoot(document)
    return `==hjson:${document.name}:\n${HjsonWriter.write(document.root)}==`
  }

  /** @anchor canvas-convert */
  public static fromHtml(html: string): CanvasDocument {
    const document: CanvasDocument = CanvasHtml.fromHtml(html)
    CanvasConverter.requireRoot(document)
    return document
  }

  /** @anchor canvas-convert */
  public static toHtml(document: CanvasDocument): string {
    CanvasConverter.requireRoot(document)
    return CanvasHtml.toHtml(document)
  }

  /** @anchor canvas-convert */
  public static toHtmlInline(document: CanvasDocument): string {
    CanvasConverter.requireRoot(document)
    return CanvasHtml.toInline(document)
  }

  /** @anchor canvas-resource-dependencies */
  public static resourceGuids(document: CanvasDocument): readonly string[] {
    return Object.freeze([...new Set(CanvasConverter.collectResources(document.root))].sort())
  }

  /** @anchor canvas-resource-dependencies */
  private static collectResources(node: CanvasNode): readonly string[] {
    const guids: string[] = []
    for (const name of [
      'slicedPicGuid',
      'fileGuid',
      'softMaskGuid',
      'atlasGuid',
      'skelGuid',
      'jsonGuid',
    ]) {
      const field: Option.Option<Var> = node.field(name)
      if (Option.isSome(field) && field.value instanceof StringVar && field.value.value.length > 0)
        guids.push(field.value.value)
    }
    const textures: Option.Option<Var> = node.field('pngGuids')
    if (Option.isSome(textures) && textures.value instanceof ListVar)
      for (const item of textures.value.items)
        if (item instanceof StringVar && item.value.length > 0) guids.push(item.value)
    for (const child of node.children()) guids.push(...CanvasConverter.collectResources(child))
    return guids
  }

  private static requireRoot(document: CanvasDocument): void {
    if (document.root.typeName !== 'Canvas') throw new Error('Canvas 根节点类型必须是 Canvas')
  }
}
