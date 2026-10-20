import { DOMParser, onErrorStopParsing } from '@xmldom/xmldom'
import { Array as EffectArray, Option } from 'effect'
import { CanvasEnums } from './CanvasEnums.ts'
import { CanvasSchema } from './CanvasSchema.ts'
import { CanvasTags, HtmlAttribute, HtmlKind, HtmlTag } from './CanvasTags.ts'
import { FloatText } from './FloatText.ts'
import type { LiveNodeList } from '@xmldom/xmldom'
import {
  BoolVar,
  CanvasDocument,
  CanvasNode,
  ColorVar,
  FloatVar,
  ListVar,
  LongVar,
  StringVar,
  Var,
  Vector3Var,
} from './CanvasValues.ts'
import type { FieldKind } from './CanvasSchema.ts'
import type { Document, Element as XmlElement } from '@xmldom/xmldom'

/** @anchor canvas-html */
export class CanvasHtml {
  private constructor() {}

  /**
   * @anchor canvas-html
   * @anchor canvas-render-xml-grammar
   */
  public static fromHtml(html: string): CanvasDocument {
    const root: XmlElement = CanvasHtml.rootElement(html)
    const tag: HtmlTag = CanvasHtml.requireTag(root.tagName)
    if (tag.tagName !== 'BeCanvas') throw new Error('HTML 根元素必须为 BeCanvas')
    const fields: HtmlFields = CanvasHtml.readAttributes(tag, root, true)
    CanvasHtml.requireMeta(tag, fields.present)
    CanvasHtml.dimension(fields.width, 'width')
    CanvasHtml.dimension(fields.height, 'height')
    const node: CanvasNode = CanvasNode.create('Canvas')
    CanvasHtml.children(root, node)
    return CanvasDocument.of(fields.canvasName, node)
  }

  /** @anchor canvas-html */
  public static toHtml(document: CanvasDocument): string {
    return `${CanvasHtml.head()}${CanvasHtml.toInline(document)}  </body>\n</html>\n`
  }

  /** @anchor canvas-html */
  public static toInline(document: CanvasDocument): string {
    const opening: string = `${CanvasHtml.indent(1)}<BeCanvas name="${CanvasHtml.escape(document.name)}" width="1080" height="2160"`
    const children: readonly CanvasNode[] = document.root.children()
    const parts: string[] = [opening]
    if (children.length === 0) parts.push('></BeCanvas>\n')
    else {
      parts.push('>\n')
      for (const child of children) parts.push(CanvasHtml.writeNode(child, 2))
      parts.push(`${CanvasHtml.indent(1)}</BeCanvas>\n`)
    }
    return parts.join('')
  }

  /** @anchor canvas-html */
  private static rootElement(html: string): XmlElement {
    const source: string = CanvasHtml.withoutDoctype(html)
    const parser: DOMParser = new DOMParser({ onError: onErrorStopParsing })
    const document: Document = parser.parseFromString(source, 'application/xml')
    const nodes: LiveNodeList<XmlElement> = document.getElementsByTagName('BeCanvas')
    if (nodes.length === 0) throw new Error('HTML 缺少 BeCanvas')
    if (nodes.length !== 1) throw new Error('HTML 视图只允许包含 1 个 BeCanvas')
    return Option.getOrThrow(Option.fromNullishOr(nodes.item(0)))
  }

  /**
   * @anchor canvas-html
   * @anchor canvas-html-attributes
   * @anchor canvas-mapping-roundtrip
   */
  private static readAttributes(tag: HtmlTag, element: XmlElement, root: boolean): HtmlFields {
    const present: string[] = []
    const seen: string[] = []
    const ordered: HtmlField[] = []
    let canvasName: string = ''
    let width: string = ''
    let height: string = ''
    let index: number = 0
    while (index < element.attributes.length) {
      const attribute: NonNullable<ReturnType<XmlElement['attributes']['item']>> =
        Option.getOrThrowWith(
          Option.fromNullishOr(element.attributes.item(index)),
          (): Error => new Error('HTML 属性缺失'),
        )
      const schema: HtmlAttribute = CanvasHtml.requireAttribute(tag, attribute.name)
      present.push(schema.htmlName)
      if (schema.kind === HtmlKind.meta) {
        if (!root)
          throw new Error(`HTML 视图不允许元属性出现在子节点: ${tag.tagName}.${schema.htmlName}`)
        if (schema.htmlName === 'width') width = attribute.value
        if (schema.htmlName === 'height') height = attribute.value
      } else if (schema.kind === HtmlKind.name) {
        canvasName = CanvasHtml.decode(attribute.value)
      } else if (!seen.includes(schema.fieldName)) {
        seen.push(schema.fieldName)
        ordered.push(HtmlField.of(schema.fieldName, CanvasHtml.decode(attribute.value)))
      }
      index += 1
    }
    return HtmlFields.of(canvasName, width, height, present, ordered)
  }

  /** @anchor canvas-html */
  private static children(element: XmlElement, parent: CanvasNode): void {
    let index: number = 0
    while (index < element.children.length) {
      parent.addChild(
        CanvasHtml.element(
          Option.getOrThrowWith(
            Option.fromNullishOr(element.children.item(index)),
            (): Error => new Error('HTML 子节点缺失'),
          ),
        ),
      )
      index += 1
    }
  }

  /** @anchor canvas-html */
  private static element(element: XmlElement): CanvasNode {
    const tag: HtmlTag = CanvasHtml.requireTag(element.tagName)
    if (tag.tagName === 'BeCanvas') throw new Error('HTML 视图禁止嵌套 BeCanvas')
    const fields: HtmlFields = CanvasHtml.readAttributes(tag, element, false)
    const node: CanvasNode = CanvasNode.create(tag.canvasType)
    node.setField('type', StringVar.of(tag.canvasType))
    for (const field of fields.ordered) {
      node.setField(field.name, CanvasHtml.value(tag.canvasType, field.name, field.text))
    }
    CanvasHtml.children(element, node)
    if (Option.isNone(node.field('name'))) throw new Error(`节点 ${tag.canvasType} 缺少 name`)
    return node
  }

  /** @anchor canvas-html */
  private static value(typeName: string, fieldName: string, text: string): Var {
    if (CanvasEnums.isEnum(fieldName)) {
      const named: Option.Option<bigint> = CanvasEnums.toValue(fieldName, text)
      if (Option.isNone(named)) throw new Error(`字段 '${fieldName}' 需要有效的枚举名称: ${text}`)
      return LongVar.of(named.value)
    }
    const kind: FieldKind = CanvasSchema.kind(fieldName)
    const parsed: Var = CanvasHtml.typed(kind, fieldName, text)
    if (!CanvasSchema.accepts(typeName, fieldName, parsed.typeCode())) {
      throw new Error(`节点类型 '${typeName}' 不允许字段 '${fieldName}'`)
    }
    return parsed
  }

  /** @anchor canvas-html */
  private static typed(kind: FieldKind, fieldName: string, text: string): Var {
    switch (kind) {
      case 'string':
        return StringVar.of(text)
      case 'bool':
        return CanvasHtml.bool(fieldName, text)
      case 'float':
        return FloatVar.of(FloatText.parse(text, fieldName))
      case 'long':
        return LongVar.of(CanvasHtml.long(fieldName, text))
      case 'vector':
        return CanvasHtml.vector(fieldName, text)
      case 'color':
        return CanvasHtml.color(fieldName, text)
      case 'list':
        return CanvasHtml.list(text)
      default:
        return CanvasHtml.unknownKind(kind)
    }
  }

  /** @anchor canvas-html */
  private static writeNode(node: CanvasNode, level: number): string {
    const tag: HtmlTag = CanvasHtml.requireType(node.typeName)
    if (tag.tagName === 'BeCanvas') throw new Error('HTML 视图禁止嵌套 BeCanvas')
    const opening: string =
      `${CanvasHtml.indent(level)}<${tag.tagName}` +
      node
        .orderedFields()
        .map((fieldName: string): string => CanvasHtml.writeField(tag, node, fieldName))
        .join('')
    const children: readonly CanvasNode[] = node.children()
    const parts: string[] = [opening]
    if (children.length === 0) parts.push(`></${tag.tagName}>\n`)
    else {
      parts.push('>\n')
      for (const child of children) parts.push(CanvasHtml.writeNode(child, level + 1))
      parts.push(`${CanvasHtml.indent(level)}</${tag.tagName}>\n`)
    }
    return parts.join('')
  }

  /** @anchor canvas-html-attributes */
  private static writeField(tag: HtmlTag, node: CanvasNode, fieldName: string): string {
    if (fieldName === 'type' || fieldName === 'sons') return ''
    const attribute: Option.Option<HtmlAttribute> = CanvasTags.nodeField(tag, fieldName)
    if (Option.isNone(attribute)) {
      throw new Error(`字段没有 HTML 属性: ${tag.canvasType}.${fieldName}`)
    }
    const stored: Option.Option<Var> = node.field(fieldName)
    if (Option.isNone(stored)) throw new Error(`字段缺少值: ${fieldName}`)
    return ` ${attribute.value.htmlName}="${CanvasHtml.escape(CanvasHtml.attributeText(fieldName, stored.value))}"`
  }

  /**
   * @anchor canvas-html
   * @fallback 颜色没有调色板名称时按四个通道写成 rgba。这是颜色的两种规定形态，不是缺值补色。
   */
  private static attributeText(fieldName: string, value: Var): string {
    if (value instanceof LongVar && CanvasEnums.isEnum(fieldName)) {
      const named: Option.Option<string> = CanvasEnums.toName(fieldName, value.value)
      if (Option.isNone(named)) {
        throw new Error(`字段 '${fieldName}' 没有枚举名称: ${value.value.toString()}`)
      }
      return named.value
    }
    if (value instanceof StringVar) return value.value
    if (value instanceof LongVar) return value.value.toString()
    if (value instanceof FloatVar) return FloatText.format(value.value)
    if (value instanceof BoolVar) return value.value ? '1' : '0'
    if (value instanceof Vector3Var) {
      return `${FloatText.format(value.x)},${FloatText.format(value.y)},${FloatText.format(value.z)}`
    }
    if (value instanceof ColorVar) {
      if (Option.isSome(value.palette)) {
        if (value.palette.value.length === 0) throw new Error('调色板名称不能为空')
        return `palette(${value.palette.value})`
      }
      return `rgba(${FloatText.formatChannel(value.red)},${FloatText.formatChannel(value.green)},${FloatText.formatChannel(value.blue)},${FloatText.formatChannel(value.alpha)})`
    }
    if (value instanceof ListVar) return CanvasHtml.listText(value)
    throw new Error(`不支持的 Canvas 值类型 ${String(value.typeCode())}`)
  }

  private static listText(value: ListVar): string {
    if (value.items.length === 0) throw new Error('HTML 不能表示空列表')
    const parts: string[] = []
    for (const item of value.items) {
      if (!(item instanceof StringVar)) throw new Error('HTML 列表只支持字符串')
      if (item.value.includes(',')) throw new Error('HTML 列表项不能包含逗号')
      parts.push(item.value)
    }
    return parts.join(',')
  }

  private static bool(fieldName: string, text: string): BoolVar {
    if (text === '1') return BoolVar.of(true)
    if (text === '0') return BoolVar.of(false)
    throw new Error(`字段 ${fieldName} 必须为 0/1: ${text}`)
  }

  private static long(fieldName: string, text: string): bigint {
    let index: number = 0
    if (text.startsWith('+') || text.startsWith('-')) index = 1
    if (index >= text.length) throw new Error(`字段 ${fieldName} 必须为整数: ${text}`)
    while (index < text.length) {
      const code: number = Option.getOrThrowWith(
        Option.fromNullishOr(text.codePointAt(index)),
        (): Error => new Error(`字段 ${fieldName} 必须为整数: ${text}`),
      )
      if (code < 48 || code > 57) throw new Error(`字段 ${fieldName} 必须为整数: ${text}`)
      index += 1
    }
    return BigInt(text)
  }

  private static vector(fieldName: string, text: string): Vector3Var {
    const parts: readonly string[] = text.split(',')
    if (parts.length !== 3) throw new Error(`字段 ${fieldName} 需要向量格式 x,y,z: ${text}`)
    return Vector3Var.of(
      FloatText.parse(EffectArray.getUnsafe(parts, 0), `${fieldName}.x`),
      FloatText.parse(EffectArray.getUnsafe(parts, 1), `${fieldName}.y`),
      FloatText.parse(EffectArray.getUnsafe(parts, 2), `${fieldName}.z`),
    )
  }

  private static color(fieldName: string, text: string): ColorVar {
    if (text.startsWith('palette(') && text.endsWith(')')) {
      const palette: string = text.slice('palette('.length, text.length - 1)
      if (palette.length === 0) throw new Error(`字段 ${fieldName} 调色板名称不能为空`)
      return ColorVar.named(palette)
    }
    if (text.startsWith('rgba(') && text.endsWith(')')) {
      const parts: readonly string[] = text.slice('rgba('.length, text.length - 1).split(',')
      if (parts.length !== 4)
        throw new Error(`字段 ${fieldName} 的 rgba 格式需要 4 个分量: ${text}`)
      const red: number = FloatText.parseDecimal(EffectArray.getUnsafe(parts, 0), fieldName)
      const green: number = FloatText.parseDecimal(EffectArray.getUnsafe(parts, 1), fieldName)
      const blue: number = FloatText.parseDecimal(EffectArray.getUnsafe(parts, 2), fieldName)
      const alpha: number = FloatText.parseDecimal(EffectArray.getUnsafe(parts, 3), fieldName)
      if (
        red < 0 ||
        red > 255 ||
        green < 0 ||
        green > 255 ||
        blue < 0 ||
        blue > 255 ||
        alpha < 0 ||
        alpha > 255
      ) {
        throw new Error(`字段 ${fieldName} 的 rgba 分量范围必须是 0..255: ${text}`)
      }
      return ColorVar.rgba(red / 255, green / 255, blue / 255, alpha / 255)
    }
    throw new Error(
      `字段 ${fieldName} 颜色格式错误，仅支持 rgba(r,g,b,a) 或 palette(name): ${text}`,
    )
  }

  private static list(text: string): ListVar {
    return ListVar.of(text.split(',').map((item: string): StringVar => StringVar.of(item)))
  }

  private static dimension(text: string, fieldName: string): void {
    if (text.length === 0) throw new Error(`Canvas.${fieldName} 必须为数值`)
    const parsed: number = Number(text)
    if (!Number.isFinite(parsed)) throw new Error(`Canvas.${fieldName} 必须为数值： ${text}`)
  }

  private static requireMeta(tag: HtmlTag, present: readonly string[]): void {
    for (const attribute of tag.attributes) {
      if (
        attribute.required &&
        attribute.kind === HtmlKind.meta &&
        !present.includes(attribute.htmlName)
      ) {
        throw new Error(`HTML 视图缺少必要属性: ${tag.tagName}.${attribute.htmlName}`)
      }
    }
  }

  private static requireTag(tagName: string): HtmlTag {
    const tag: Option.Option<HtmlTag> = CanvasTags.tag(tagName)
    if (Option.isNone(tag)) throw new Error(`HTML 视图不支持标签: ${tagName}`)
    return tag.value
  }

  private static requireType(typeName: string): HtmlTag {
    const tag: Option.Option<HtmlTag> = CanvasTags.byType(typeName)
    if (Option.isNone(tag)) throw new Error(`HTML 视图不支持 UI 类型: ${typeName}`)
    return tag.value
  }

  private static requireAttribute(tag: HtmlTag, htmlName: string): HtmlAttribute {
    const attribute: Option.Option<HtmlAttribute> = CanvasTags.htmlAttribute(tag, htmlName)
    if (Option.isNone(attribute))
      throw new Error(`HTML 视图存在未知属性: ${tag.tagName}.${htmlName}`)
    return attribute.value
  }

  private static withoutDoctype(html: string): string {
    const marker: string = '<!DOCTYPE html>'
    const index: number = html.indexOf(marker)
    if (index < 0) return html
    return html.slice(0, index) + html.slice(index + marker.length)
  }

  private static head(): string {
    return (
      '<!--\n' +
      '  Canvas HTML 视图。width 与 height 只供视图使用，不进入节点。\n' +
      '-->\n' +
      '<!DOCTYPE html>\n' +
      '<html lang="zh-CN">\n' +
      '  <head>\n' +
      '    <meta charset="UTF-8" />\n' +
      '  </head>\n' +
      '  <body>\n'
    )
  }

  private static indent(level: number): string {
    return ' '.repeat(level * 2)
  }

  private static escape(value: string): string {
    const quoted: string = value.split('“').join('\\u201c').split('”').join('\\u201d')

    return quoted
      .split('&')
      .join('&amp;')
      .split('<')
      .join('&lt;')
      .split('>')
      .join('&gt;')
      .split('"')
      .join('&quot;')
      .split("'")
      .join('&apos;')
      .split('\r')
      .join('&#xD;')
      .split('\n')
      .join('&#xA;')
      .split('\t')
      .join('&#x9;')
  }

  private static decode(value: string): string {
    return value.split('\\u201c').join('“').split('\\u201d').join('”')
  }

  private static unknownKind(kind: FieldKind): never {
    throw new Error(`不支持的字段种类 ${kind}`)
  }
}

/** @anchor canvas-html */
class HtmlField {
  private constructor(
    public readonly name: string,
    public readonly text: string,
  ) {}

  /** @anchor canvas-html */
  public static of(name: string, text: string): HtmlField {
    return new HtmlField(name, text)
  }
}

/** @anchor canvas-html */
class HtmlFields {
  private constructor(
    public readonly canvasName: string,
    public readonly width: string,
    public readonly height: string,
    public readonly present: readonly string[],
    public readonly ordered: readonly HtmlField[],
  ) {}

  /** @anchor canvas-html */
  public static of(
    canvasName: string,
    width: string,
    height: string,
    present: readonly string[],
    ordered: readonly HtmlField[],
  ): HtmlFields {
    return new HtmlFields(canvasName, width, height, present, ordered)
  }
}
