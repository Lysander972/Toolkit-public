import { Option } from 'effect'
import { CanvasSchema } from './CanvasSchema.ts'
import {
  BoolVar,
  CanvasDocument,
  CanvasNode,
  ColorVar,
  DictEntry,
  DictVar,
  FloatVar,
  ListVar,
  LongVar,
  StringVar,
  Var,
  Vector3Var,
} from './CanvasValues.ts'

/** @anchor canvas-binary */
export class CanvasBinary {
  private constructor() {}

  /** @anchor canvas-binary */
  public static fromBytes(bytes: Uint8Array, providedName: string): CanvasDocument {
    const reader: BinaryReader = BinaryReader.of(bytes)
    if (reader.remaining() === 0) throw new Error('文件内容为空')
    let name: string = providedName
    if (reader.startsWithMagic()) {
      reader.readByte()
      reader.readByte()
      reader.readByte()
      name = reader.readString()
    }
    const root: CanvasNode = CanvasBinary.readRoot(reader)
    if (reader.remaining() !== 0)
      throw new Error(`Canvas 解析后存在未消费字节: ${String(reader.remaining())}`)
    return CanvasDocument.of(name, root)
  }

  /** @anchor canvas-binary */
  public static toFileBytes(document: CanvasDocument): Uint8Array {
    const writer: BinaryWriter = BinaryWriter.create()
    writer.writeByte(0x02)
    writer.writeByte(0x76)
    writer.writeByte(0x31)
    writer.writeString(document.name)
    CanvasBinary.writeChildren(writer, document.root)
    return writer.toBytes()
  }

  /** @anchor canvas-binary */
  public static toInlineBytes(root: CanvasNode): Uint8Array {
    const writer: BinaryWriter = BinaryWriter.create()
    CanvasBinary.writeChildren(writer, root)
    return writer.toBytes()
  }

  /** @anchor canvas-binary */
  public static nodeFromDict(dict: DictVar): CanvasNode {
    const typeValue: Var = CanvasBinary.require(dict, 'type')
    if (!(typeValue instanceof StringVar)) throw new Error('节点 type 必须是字符串')
    const typeName: string = typeValue.value
    if (!CanvasSchema.supported(typeName)) throw new Error(`不支持的 UI 节点类型: ${typeName}`)
    const node: CanvasNode = CanvasNode.create(typeName)
    for (const entry of dict.entries) {
      node.setField(entry.key, entry.value)
      if (entry.key === 'type') continue
      if (entry.key === 'sons') {
        CanvasBinary.readSons(node, entry.value)
        continue
      }
      CanvasBinary.assignField(node, typeName, entry.key, entry.value)
    }
    if (Option.isNone(node.field('name'))) throw new Error('UI 节点缺少 name 字段')
    return node
  }

  /** @anchor canvas-binary */
  public static dictFromNode(node: CanvasNode): DictVar {
    if (!CanvasSchema.supported(node.typeName)) {
      throw new Error(`不支持的 UI 节点类型: ${node.typeName}`)
    }
    const entries: DictEntry[] = [DictEntry.of('type', StringVar.of(node.typeName))]
    for (const fieldName of node.orderedFields()) {
      if (fieldName === 'type' || fieldName === 'sons') continue
      const value: Option.Option<Var> = node.field(fieldName)
      if (Option.isNone(value)) throw new Error(`字段缺少值: ${fieldName}`)
      if (!CanvasSchema.accepts(node.typeName, fieldName, value.value.typeCode())) {
        throw new Error(`节点类型 '${node.typeName}' 不允许字段 '${fieldName}'`)
      }
      entries.push(DictEntry.of(fieldName, CanvasBinary.exportField(value.value)))
    }
    if (node.children().length > 0) {
      const sons: Var[] = node
        .children()
        .map((child: CanvasNode): Var => CanvasBinary.dictFromNode(child))
      entries.push(DictEntry.of('sons', ListVar.of(sons)))
    }
    return DictVar.of(entries)
  }

  private static readRoot(reader: BinaryReader): CanvasNode {
    const code: number = reader.readByte()
    if (code === 16 || code === 27) {
      const count: number = reader.readUint16()
      const root: CanvasNode = CanvasNode.create('Canvas')
      let index: number = 0
      while (index < count) {
        if (reader.remaining() === 0)
          throw new Error(
            `Canvas 根列表提前结束: 期望 ${String(count)} 项, 实际 ${String(index)} 项`,
          )
        root.addChild(CanvasBinary.readElement(reader, index))
        index += 1
      }
      return root
    }
    if (code === 17) return CanvasBinary.nodeFromDict(CanvasBinary.readDictPayload(reader))
    if (CanvasBinary.isUiMarker(code))
      return CanvasBinary.nodeFromDict(CanvasBinary.readMarkedDict(reader))
    throw new Error(`不支持的 Canvas 根类型: ${String(code)}`)
  }

  private static readElement(reader: BinaryReader, index: number): CanvasNode {
    const code: number = reader.readByte()
    if (code === 17) return CanvasBinary.nodeFromDict(CanvasBinary.readDictPayload(reader))
    if (CanvasBinary.isUiMarker(code))
      return CanvasBinary.nodeFromDict(CanvasBinary.readMarkedDict(reader))
    throw new Error(`Canvas 根列表第 ${String(index)} 项类型非法: ${String(code)}`)
  }

  private static readMarkedDict(reader: BinaryReader): DictVar {
    if (reader.remaining() === 0 || reader.peekByte() !== 17) {
      throw new Error('UI 类型标记后缺少 Dict payload')
    }
    reader.readByte()
    return CanvasBinary.readDictPayload(reader)
  }

  private static writeChildren(writer: BinaryWriter, root: CanvasNode): void {
    writer.writeByte(16)
    writer.writeUint16(root.children().length)
    for (const child of root.children())
      CanvasBinary.writeVar(writer, CanvasBinary.dictFromNode(child))
  }

  private static readSons(node: CanvasNode, value: Var): void {
    if (!(value instanceof ListVar)) throw new Error('sons 必须是列表')
    for (const child of value.items) {
      if (!(child instanceof DictVar))
        throw new Error(`sons 包含非法子项类型: ${String(child.typeCode())}`)
      node.addChild(CanvasBinary.nodeFromDict(child))
    }
  }

  private static assignField(
    node: CanvasNode,
    typeName: string,
    fieldName: string,
    value: Var,
  ): void {
    if (!CanvasSchema.accepts(typeName, fieldName, value.typeCode())) {
      throw new Error(`节点类型 '${typeName}' 不允许字段 '${fieldName}'`)
    }
    if (value instanceof StringVar && CanvasSchema.accepts(typeName, fieldName, 12)) {
      if (value.value.length === 0) throw new Error(`字段 ${fieldName} 调色板名称不能为空`)
      node.setField(fieldName, ColorVar.named(value.value))
      return
    }
    node.setField(fieldName, value)
  }

  private static exportField(value: Var): Var {
    if (value instanceof ColorVar && Option.isSome(value.palette)) {
      if (value.palette.value.length === 0) throw new Error('调色板名称不能为空')
      return StringVar.of(value.palette.value)
    }
    return value
  }

  private static require(dict: DictVar, key: string): Var {
    const found: Option.Option<Var> = dict.find(key)
    if (Option.isNone(found)) throw new Error(`字典缺少字段 ${key}`)
    return found.value
  }

  private static isUiMarker(code: number): boolean {
    return (
      code === 3 ||
      code === 8 ||
      code === 9 ||
      code === 10 ||
      code === 11 ||
      code === 14 ||
      code === 20
    )
  }

  private static writeVar(writer: BinaryWriter, value: Var): void {
    writer.writeByte(value.typeCode())
    if (value instanceof BoolVar) writer.writeByte(value.value ? 1 : 0)
    else if (value instanceof FloatVar) writer.writeFloat32(value.value)
    else if (value instanceof LongVar) writer.writeInt64(value.value)
    else if (value instanceof StringVar) writer.writeString(value.value)
    else if (value instanceof Vector3Var) {
      writer.writeFloat32(value.x)
      writer.writeFloat32(value.y)
      writer.writeFloat32(value.z)
    } else if (value instanceof ColorVar) CanvasBinary.writeColor(writer, value)
    else if (value instanceof ListVar) {
      writer.writeUint16(value.items.length)
      for (const item of value.items) CanvasBinary.writeVar(writer, item)
    } else if (value instanceof DictVar) {
      writer.writeUint16(value.entries.length)
      for (const entry of value.entries) {
        writer.writeString(entry.key)
        CanvasBinary.writeVar(writer, entry.value)
      }
    } else throw new Error(`不支持的变量类型 ${String(value.typeCode())}`)
  }

  private static writeColor(writer: BinaryWriter, value: ColorVar): void {
    if (Option.isSome(value.palette)) {
      if (value.palette.value.length === 0) throw new Error('调色板名称不能为空')
      writer.writeFloat32(Math.fround(-1))
      writer.writeString(value.palette.value)
      return
    }
    writer.writeFloat32(value.red)
    writer.writeFloat32(value.green)
    writer.writeFloat32(value.blue)
    writer.writeFloat32(value.alpha)
  }

  private static readDictPayload(reader: BinaryReader): DictVar {
    const count: number = reader.readUint16()
    const entries: DictEntry[] = []
    let index: number = 0
    while (index < count) {
      const key: string = reader.readString()
      entries.push(DictEntry.of(key, CanvasBinary.readVar(reader)))
      index += 1
    }
    return DictVar.of(entries)
  }

  private static readVar(reader: BinaryReader): Var {
    const code: number = reader.readByte()
    if (code === 5) return BoolVar.of(reader.readByte() !== 0)
    if (code === 1) return FloatVar.of(reader.readFloat32())
    if (code === 24) return LongVar.of(reader.readInt64())
    if (code === 6) return StringVar.of(reader.readString())
    if (code === 4)
      return Vector3Var.of(reader.readFloat32(), reader.readFloat32(), reader.readFloat32())
    if (code === 12) {
      const red: number = reader.readFloat32()
      if (red < 0) {
        const palette: string = reader.readString()
        if (palette.length === 0) throw new Error('调色板名称不能为空')
        return ColorVar.named(palette)
      }
      return ColorVar.rgba(red, reader.readFloat32(), reader.readFloat32(), reader.readFloat32())
    }
    if (code === 16) return CanvasBinary.readList(reader)
    if (code === 17) return CanvasBinary.readDictPayload(reader)
    if (code === 26) return CanvasBinary.readStruct(reader)
    throw new Error(`不支持的变量类型 ${String(code)}`)
  }

  private static readList(reader: BinaryReader): ListVar {
    const count: number = reader.readUint16()
    const items: Var[] = []
    let index: number = 0
    while (index < count) {
      items.push(CanvasBinary.readVar(reader))
      index += 1
    }
    return ListVar.of(items)
  }

  private static readStruct(reader: BinaryReader): ListVar {
    const count: number = reader.readUint16()
    const items: Var[] = []
    let index: number = 0
    while (index < count) {
      reader.readString()
      items.push(CanvasBinary.readVar(reader))
      index += 1
    }
    return ListVar.of(items)
  }
}

/** @anchor canvas-binary */
class BinaryReader {
  private readonly view: DataView
  private offset: number

  private constructor(private readonly bytes: Uint8Array) {
    this.view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
    this.offset = 0
  }

  /** @anchor canvas-binary */
  public static of(bytes: Uint8Array): BinaryReader {
    return new BinaryReader(bytes)
  }

  /** @anchor canvas-binary */
  public remaining(): number {
    return this.bytes.length - this.offset
  }

  /** @anchor canvas-binary */
  public startsWithMagic(): boolean {
    return (
      this.remaining() >= 3 &&
      this.bytes[this.offset] === 0x02 &&
      this.bytes[this.offset + 1] === 0x76 &&
      this.bytes[this.offset + 2] === 0x31
    )
  }

  /** @anchor canvas-binary */
  public peekByte(): number {
    return this.byteAt(this.offset)
  }

  /** @anchor canvas-binary */
  public readByte(): number {
    const value: number = this.byteAt(this.offset)
    this.offset += 1
    return value
  }

  /** @anchor canvas-binary */
  public readUint16(): number {
    this.require(2)
    const value: number = this.view.getUint16(this.offset, true)
    this.offset += 2
    return value
  }

  /** @anchor canvas-binary */
  public readFloat32(): number {
    this.require(4)
    const value: number = this.view.getFloat32(this.offset, true)
    this.offset += 4
    return value
  }

  /** @anchor canvas-binary */
  public readInt64(): bigint {
    this.require(8)
    const value: bigint = this.view.getBigInt64(this.offset, true)
    this.offset += 8
    return value
  }

  /** @anchor canvas-binary */
  public readString(): string {
    let length: number = 0
    let shift: number = 0
    let done: boolean = false
    while (!done) {
      const part: number = this.readByte()
      length += (part & 0x7f) << shift
      shift += 7
      done = (part & 0x80) === 0
    }
    if (length === 0) return ''
    this.require(length)
    const slice: Uint8Array = this.bytes.slice(this.offset, this.offset + length)
    this.offset += length
    return new TextDecoder().decode(slice)
  }

  private byteAt(index: number): number {
    if (index < 0 || index >= this.bytes.length) throw new Error('二进制读取超出末尾')
    return this.view.getUint8(index)
  }

  private require(length: number): void {
    if (this.offset + length > this.bytes.length) throw new Error('二进制读取超出末尾')
  }
}

/** @anchor canvas-binary */
class BinaryWriter {
  private readonly bytes: number[]

  private constructor() {
    this.bytes = []
  }

  /** @anchor canvas-binary */
  public static create(): BinaryWriter {
    return new BinaryWriter()
  }

  /** @anchor canvas-binary */
  public writeByte(value: number): void {
    this.bytes.push(value & 0xff)
  }

  /** @anchor canvas-binary */
  public writeUint16(value: number): void {
    this.writeByte(value & 0xff)
    this.writeByte((value >> 8) & 0xff)
  }

  /** @anchor canvas-binary */
  public writeFloat32(value: number): void {
    const view: DataView = new DataView(new ArrayBuffer(4))
    view.setFloat32(0, value, true)
    this.writeByte(view.getUint8(0))
    this.writeByte(view.getUint8(1))
    this.writeByte(view.getUint8(2))
    this.writeByte(view.getUint8(3))
  }

  /** @anchor canvas-binary */
  public writeInt64(value: bigint): void {
    const view: DataView = new DataView(new ArrayBuffer(8))
    view.setBigInt64(0, value, true)
    let index: number = 0
    while (index < 8) {
      this.writeByte(view.getUint8(index))
      index += 1
    }
  }

  /** @anchor canvas-binary */
  public writeString(value: string): void {
    const encoded: Uint8Array = new TextEncoder().encode(value)
    let rest: number = encoded.length
    while (rest >= 0x80) {
      this.writeByte((rest & 0x7f) | 0x80)
      rest = Math.floor(rest / 128)
    }
    this.writeByte(rest)
    const view: DataView = new DataView(encoded.buffer, encoded.byteOffset, encoded.byteLength)
    let index: number = 0
    while (index < encoded.length) {
      this.writeByte(view.getUint8(index))
      index += 1
    }
  }

  /** @anchor canvas-binary */
  public toBytes(): Uint8Array {
    return Uint8Array.from(this.bytes)
  }
}
