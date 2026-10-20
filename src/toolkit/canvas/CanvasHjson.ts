import Hjson from 'hjson'
import { Array as EffectArray, Option, Result } from 'effect'
import { Base64 } from 'effect/encoding'
import { z } from 'zod'
import { CanvasSchema } from './CanvasSchema.ts'
import { FloatText } from './FloatText.ts'
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
import { CanvasBinary } from './CanvasBinary.ts'

/** @anchor canvas-hjson */
class JsonField {
  private constructor(
    public readonly key: string,
    public readonly value: JsonValue,
  ) {}

  /** @anchor canvas-hjson */
  public static of(key: string, value: JsonValue): JsonField {
    return new JsonField(key, value)
  }
}

/** @anchor canvas-hjson */
class JsonValue {
  private constructor(
    private readonly kind: number,
    private readonly textValue: string,
    private readonly numberValue: number,
    private readonly flagValue: boolean,
    private readonly items: readonly JsonValue[],
    private readonly fields: readonly JsonField[],
  ) {}

  /** @anchor canvas-hjson */
  public static fromText(value: string): JsonValue {
    return new JsonValue(0, value, 0, false, [], [])
  }

  /** @anchor canvas-hjson */
  public static fromNumber(value: number): JsonValue {
    return new JsonValue(1, '', value, false, [], [])
  }

  /** @anchor canvas-hjson */
  public static fromFlag(value: boolean): JsonValue {
    return new JsonValue(2, '', 0, value, [], [])
  }

  /** @anchor canvas-hjson */
  public static fromList(items: readonly JsonValue[]): JsonValue {
    return new JsonValue(3, '', 0, false, items, [])
  }

  /** @anchor canvas-hjson */
  public static fromObject(fields: readonly JsonField[]): JsonValue {
    return new JsonValue(4, '', 0, false, [], fields)
  }

  /** @anchor canvas-hjson */
  public isText(): boolean {
    return this.kind === 0
  }

  /** @anchor canvas-hjson */
  public text(): string {
    if (this.kind !== 0) throw new Error('Hjson 值不是字符串')
    return this.textValue
  }

  /** @anchor canvas-hjson */
  public number(): number {
    if (this.kind !== 1) throw new Error('Hjson 值不是数值')
    return this.numberValue
  }

  /** @anchor canvas-hjson */
  public flag(): boolean {
    if (this.kind !== 2) throw new Error('Hjson 值不是布尔')
    return this.flagValue
  }

  /** @anchor canvas-hjson */
  public list(): readonly JsonValue[] {
    if (this.kind !== 3) throw new Error('Hjson 值不是数组')
    return this.items
  }

  /** @anchor canvas-hjson */
  public mapping(): readonly JsonField[] {
    if (this.kind !== 4) throw new Error('Hjson 值不是对象')
    return this.fields
  }
}

/** @anchor canvas-hjson */
export class CanvasHjson {
  private static readonly schema: z.ZodType<JsonValue> = z.lazy((): z.ZodType<JsonValue> =>
    z.union([
      z.string().transform((value: string): JsonValue => JsonValue.fromText(value)),
      z.number().transform((value: number): JsonValue => JsonValue.fromNumber(value)),
      z.boolean().transform((value: boolean): JsonValue => JsonValue.fromFlag(value)),
      z
        .array(CanvasHjson.schema)
        .transform((items: readonly JsonValue[]): JsonValue => JsonValue.fromList(items)),
      z
        .record(z.string(), CanvasHjson.schema)
        .transform((fields: Readonly<Record<string, JsonValue>>): JsonValue =>
          JsonValue.fromObject(CanvasHjson.entries(fields)),
        ),
    ]),
  )

  private constructor() {}

  /** @anchor canvas-hjson */
  public static base64(text: string): CanvasDocument {
    const marker: string = 'canvas:'
    const splitAt: number = text.indexOf('=', marker.length)
    if (splitAt < 0 || splitAt === text.length - 1)
      throw new Error('Base64 内联 Canvas 缺少名称或内容')
    const name: string = text.slice(marker.length, splitAt)
    const payload: string = text.slice(splitAt + 1)
    const bytes: Uint8Array = Result.getOrThrow(Base64.decode(payload))
    if (Base64.encode(bytes) !== payload) throw new Error('Base64 不是标准编码')
    return CanvasBinary.fromBytes(bytes, name)
  }

  /** @anchor canvas-hjson */
  public static hjson(text: string): CanvasDocument {
    const marker: string = '==hjson:'
    const suffix: string = '=='
    if (!text.endsWith(suffix)) throw new Error('Hjson 内联 Canvas 缺少结束标记')
    const nameEnd: number = text.indexOf(':', marker.length)
    if (nameEnd < 0) throw new Error('Hjson 内联 Canvas 缺少名称')
    const name: string = text.slice(marker.length, nameEnd)
    const payload: string = text.slice(nameEnd + 1, text.length - suffix.length)
    return CanvasDocument.of(name, CanvasHjson.root(CanvasHjson.parse(payload)))
  }

  /** @anchor canvas-hjson */
  private static parse(text: string): JsonValue {
    return CanvasHjson.schema.parse(Hjson.parse(text))
  }

  private static entries(fields: Readonly<Record<string, JsonValue>>): readonly JsonField[] {
    return Object.entries(fields).map(([key, value]: [string, JsonValue]): JsonField =>
      JsonField.of(key, value),
    )
  }

  /** @anchor canvas-hjson */
  private static root(value: JsonValue): CanvasNode {
    const node: CanvasNode = CanvasNode.create('Canvas')
    for (const child of value.list()) node.addChild(CanvasHjson.node(child))
    return node
  }

  /** @anchor canvas-hjson */
  private static node(value: JsonValue): CanvasNode {
    const fields: readonly JsonField[] = value.mapping()
    const typeValue: JsonValue = CanvasHjson.required(fields, 'type')
    const typeName: string = typeValue.text()
    if (!CanvasSchema.supported(typeName)) throw new Error(`不支持的 UI 节点类型: ${typeName}`)
    const node: CanvasNode = CanvasNode.create(typeName)
    for (const field of fields) {
      if (field.key === 'type') {
        node.setField('type', StringVar.of(typeName))
        continue
      }
      if (field.key === 'sons') {
        for (const child of field.value.list()) node.addChild(CanvasHjson.node(child))
        continue
      }
      node.setField(field.key, CanvasHjson.field(typeName, field.key, field.value))
    }
    if (Option.isNone(node.field('name'))) throw new Error('UI 节点缺少 name 字段')
    return node
  }

  /** @anchor canvas-hjson */
  private static field(typeName: string, fieldName: string, value: JsonValue): Var {
    const kind: FieldKind = CanvasSchema.kind(fieldName)
    const parsed: Var = CanvasHjson.typed(kind, fieldName, value)
    if (!CanvasSchema.accepts(typeName, fieldName, parsed.typeCode())) {
      throw new Error(`节点类型 '${typeName}' 不允许字段 '${fieldName}'`)
    }
    return parsed
  }

  private static typed(kind: FieldKind, fieldName: string, value: JsonValue): Var {
    switch (kind) {
      case 'string':
        return StringVar.of(value.text())
      case 'bool':
        return BoolVar.of(value.flag())
      case 'float':
        return FloatVar.of(FloatText.parse(FloatText.format(value.number()), fieldName))
      case 'long':
        return LongVar.of(CanvasHjson.long(fieldName, value.number()))
      case 'vector':
        return CanvasHjson.vector(fieldName, value)
      case 'color':
        return CanvasHjson.color(fieldName, value)
      case 'list':
        return CanvasHjson.strings(fieldName, value)
      default:
        return CanvasHjson.unknownKind(kind)
    }
  }

  private static vector(fieldName: string, value: JsonValue): Vector3Var {
    const items: readonly JsonValue[] = value.list()
    if (items.length !== 3) throw new Error(`字段 ${fieldName} 必须为 3 元数值数组`)
    return Vector3Var.of(
      CanvasHjson.component(items, 0, fieldName),
      CanvasHjson.component(items, 1, fieldName),
      CanvasHjson.component(items, 2, fieldName),
    )
  }

  private static component(items: readonly JsonValue[], index: number, fieldName: string): number {
    return FloatText.parse(
      FloatText.format(EffectArray.getUnsafe(items, index).number()),
      `${fieldName}[${String(index)}]`,
    )
  }

  private static color(fieldName: string, value: JsonValue): ColorVar {
    if (value.isText()) {
      if (value.text().length === 0) throw new Error(`字段 ${fieldName} 调色板名称不能为空`)
      return ColorVar.named(value.text())
    }
    const items: readonly JsonValue[] = value.list()
    if (items.length !== 4) throw new Error(`字段 ${fieldName} 必须为 4 元数值数组或调色板字符串`)
    return ColorVar.rgba(
      CanvasHjson.component(items, 0, fieldName),
      CanvasHjson.component(items, 1, fieldName),
      CanvasHjson.component(items, 2, fieldName),
      CanvasHjson.component(items, 3, fieldName),
    )
  }

  private static strings(fieldName: string, value: JsonValue): ListVar {
    return ListVar.of(
      value.list().map((item: JsonValue): StringVar => {
        if (!item.isText()) throw new Error(`字段 ${fieldName} 的列表项必须为字符串`)
        return StringVar.of(item.text())
      }),
    )
  }

  private static long(fieldName: string, value: number): bigint {
    if (!Number.isSafeInteger(value)) throw new Error(`字段 ${fieldName} 必须为安全整数`)
    return BigInt(value)
  }

  private static required(fields: readonly JsonField[], key: string): JsonValue {
    return Option.getOrThrowWith(
      Option.map(
        Option.fromNullishOr(fields.find((field: JsonField): boolean => field.key === key)),
        (field: JsonField): JsonValue => field.value,
      ),
      (): Error => new Error(`新版内联 Canvas 节点缺少 ${key} 字段`),
    )
  }

  private static unknownKind(kind: FieldKind): never {
    throw new Error(`不支持的字段种类 ${kind}`)
  }
}

/** @anchor canvas-hjson */
export class HjsonWriter {
  private readonly parts: string[]
  private tabs: number

  private constructor() {
    this.parts = []
    this.tabs = 0
  }

  /** @anchor canvas-hjson */
  public static write(root: CanvasNode): string {
    const writer: HjsonWriter = new HjsonWriter()
    writer.line('[')
    writer.tabs += 1
    for (const child of root.children()) writer.node(child)
    writer.tabs -= 1
    writer.line(']')
    return writer.parts.join('')
  }

  private node(node: CanvasNode): void {
    if (!CanvasSchema.supported(node.typeName)) {
      throw new Error(`不支持的 UI 节点类型: ${node.typeName}`)
    }
    this.line('{')
    this.tabs += 1
    this.scalar('type', node.typeName)
    for (const fieldName of node.orderedFields()) {
      if (fieldName === 'type' || fieldName === 'sons') continue
      const value: Var = Option.getOrThrow(node.field(fieldName))
      if (!CanvasSchema.accepts(node.typeName, fieldName, value.typeCode())) {
        throw new Error(`节点类型 '${node.typeName}' 不允许字段 '${fieldName}'`)
      }
      this.value(fieldName, value)
    }
    if (node.children().length > 0) this.nodes(node.children())
    this.tabs -= 1
    this.line('}')
  }

  private nodes(nodes: readonly CanvasNode[]): void {
    this.line('sons:')
    this.line('[')
    this.tabs += 1
    for (const node of nodes) this.node(node)
    this.tabs -= 1
    this.line(']')
  }

  private value(fieldName: string, value: Var): void {
    if (value instanceof Vector3Var) {
      this.inline(fieldName, [value.x, value.y, value.z])
      return
    }
    if (value instanceof ColorVar) {
      if (Option.isSome(value.palette)) {
        if (value.palette.value.length === 0) throw new Error('调色板名称不能为空')
        this.scalar(fieldName, value.palette.value)
      } else {
        this.inline(fieldName, [value.red, value.green, value.blue, value.alpha])
      }
      return
    }
    if (value instanceof ListVar) {
      this.list(fieldName, value.items)
      return
    }
    if (value instanceof StringVar) {
      this.scalar(fieldName, value.value)
      return
    }
    if (value instanceof BoolVar) {
      this.raw(fieldName, value.value ? 'true' : 'false')
      return
    }
    if (value instanceof LongVar) {
      this.raw(fieldName, value.value.toString())
      return
    }
    if (value instanceof FloatVar) {
      this.raw(fieldName, FloatText.format(value.value))
      return
    }
    throw new Error(`不支持的 Canvas 值类型 ${String(value.typeCode())}`)
  }

  private list(fieldName: string, items: readonly Var[]): void {
    this.line(`${fieldName}:`)
    this.line('[')
    this.tabs += 1
    for (const item of items) this.item(item)
    this.tabs -= 1
    this.line(']')
  }

  private item(value: Var): void {
    if (value instanceof Vector3Var) {
      this.line(HjsonWriter.inline([value.x, value.y, value.z]))
      return
    }
    if (value instanceof ColorVar) {
      if (Option.isSome(value.palette)) {
        if (value.palette.value.length === 0) throw new Error('调色板名称不能为空')
        this.line(Hjson.stringify(value.palette.value, { multiline: 'off', eol: '\n' }))
      } else this.line(HjsonWriter.inline([value.red, value.green, value.blue, value.alpha]))
      return
    }
    if (value instanceof StringVar)
      this.line(Hjson.stringify(value.value, { multiline: 'off', eol: '\n' }))
    else if (value instanceof BoolVar) this.line(value.value ? 'true' : 'false')
    else if (value instanceof LongVar) this.line(value.value.toString())
    else if (value instanceof FloatVar) this.line(FloatText.format(value.value))
    else throw new Error(`不支持的 Hjson 列表值类型 ${String(value.typeCode())}`)
  }

  private scalar(fieldName: string, value: string): void {
    this.line(`${fieldName}:${Hjson.stringify(value, { multiline: 'off', eol: '\n' })}`)
  }

  private raw(fieldName: string, value: string): void {
    this.line(`${fieldName}:${value}`)
  }

  private inline(fieldName: string, values: readonly number[]): void {
    this.line(`${fieldName}:${HjsonWriter.inline(values)}`)
  }

  private line(value: string): void {
    if (this.parts.length > 0) this.parts.push('\n')
    this.parts.push(' '.repeat(this.tabs), value)
  }

  private static inline(values: readonly number[]): string {
    return `[${values.map((value: number): string => FloatText.format(value)).join(',')}]`
  }
}
