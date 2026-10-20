import { Option } from 'effect'
import { z } from 'zod'
import { GuidFormat } from '@toolkit/project-model/guid'
import {
  BescriptVocabulary,
  type RgbColor,
  type ScriptHeader,
  type ScriptParameter,
  type VarTypeName,
} from '../vocabulary/Vocabulary.ts'

export type CodeFileVersion = 'v1' | 'v2'

/** @anchor bescript-code-file */
export interface CodeFileDocument {
  readonly version: CodeFileVersion
  readonly header: ScriptHeader
  readonly sourceText: string
  readonly layout: CodeFileLayout
}

/** @anchor bescript-code-file */
export interface HeaderSource {
  readonly key: string
  readonly start: number
  readonly end: number
}

/** @anchor bescript-code-file */
export interface CodeFileLayout {
  readonly text: string
  readonly beginStart: number
  readonly beginEnd: number
  readonly linesStart: number
  readonly bodyStart: number
  readonly bodyEnd: number
  readonly fields: readonly HeaderSource[]
  readonly parameters: readonly ParameterSource[]
}

/** @anchor bescript-code-file */
export interface ParameterSource {
  readonly typeStart: number
  readonly typeEnd: number
  readonly nameStart: number
  readonly nameEnd: number
}

interface LineSpan {
  readonly text: string
  readonly start: number
}

type HeaderKey =
  | 'name'
  | 'comment'
  | 'guid'
  | 'color'
  | 'params'
  | 'return'
  | 'lines'
  | 'debug'
  | 'skip'
  | 'private'

/** @anchor bescript-code-file */
export class CodeFileReader {
  private static readonly keys: readonly HeaderKey[] = [
    'name',
    'comment',
    'guid',
    'color',
    'params',
    'return',
    'lines',
    'debug',
    'skip',
    'private',
  ]

  private readonly lines: readonly LineSpan[]
  private name: Option.Option<string> = Option.none()
  private comment: Option.Option<string> = Option.none()
  private guid: Option.Option<string> = Option.none()
  private color: Option.Option<RgbColor> = Option.none()
  private readonly parameters: ScriptParameter[] = []
  private readonly fields: HeaderSource[] = []
  private readonly parameterSources: ParameterSource[] = []
  private bodyEnd: number = 0
  private paramsSeen: boolean = false
  private parametersOpen: boolean = false
  private returnParameter: Option.Option<ScriptParameter> = Option.none()
  private debug: boolean = false
  private skip: boolean = false
  private isPrivate: boolean = false

  /** @anchor bescript-code-file */
  private constructor(private readonly source: string) {
    this.lines = CodeFileReader.split(source)
  }

  /** @anchor bescript-code-file */
  public static read(source: string): CodeFileDocument {
    return new CodeFileReader(source).parse()
  }

  /** @anchor bescript-code-file */
  private parse(): CodeFileDocument {
    if (this.source.includes('\r')) {
      throw new Error('BEScript 文件只允许 LF 换行')
    }
    const begin: number = this.beginLine()
    const version: CodeFileVersion = this.versionAt(begin)
    const linesHeader: number = this.readHeader(begin + 1)
    const sourceText: string = this.sourceText(linesHeader)
    return {
      version,
      header: this.header(),
      sourceText,
      layout: {
        text: this.source,
        beginStart: this.lineAt(begin).start,
        beginEnd: this.lineAt(begin).start + this.lineAt(begin).text.length,
        linesStart: this.lineAt(linesHeader).start,
        bodyStart: this.lineAt(linesHeader).start + this.lineAt(linesHeader).text.length + 1,
        bodyEnd: this.bodyEnd,
        fields: this.fields,
        parameters: this.parameterSources,
      },
    }
  }

  /** @anchor bescript-code-file */
  private beginLine(): number {
    for (let index: number = 0; index < this.lines.length; index += 1) {
      const line: LineSpan = this.lineAt(index)
      if (line.text.length === 0) {
        continue
      }
      if (line.text === 'begincode' || line.text === 'begincodeV2') {
        return index
      }
      throw new Error('BEScript 文件必须以 begincode 或 begincodeV2 开始')
    }
    throw new Error('BEScript 文件缺少 begincode 起始行')
  }

  /** @anchor bescript-code-file */
  private versionAt(index: number): CodeFileVersion {
    const line: LineSpan = this.lineAt(index)
    if (line.text === 'begincodeV2') {
      return 'v2'
    }
    if (line.text === 'begincode') {
      return 'v1'
    }
    throw new Error('BEScript 文件版本无法识别')
  }

  /** @anchor bescript-code-file */
  private readHeader(start: number): number {
    let index: number = start
    while (index < this.lines.length) {
      const line: LineSpan = this.lineAt(index)
      if (line.text.length === 0) {
        index += 1
        continue
      }
      const key: Option.Option<HeaderKey> = CodeFileReader.headerKey(line.text)
      if (Option.isNone(key)) {
        if (this.parametersOpen) {
          this.appendParameters(line.text, line.start)
          const previous: HeaderSource = Option.getOrThrow(Option.fromNullishOr(this.fields.pop()))
          this.fields.push({ ...previous, end: line.start + line.text.length })
          index += 1
          continue
        }
        throw new Error('BEScript 头部存在无法识别的行')
      }
      const value: string = line.text.slice(key.value.length + 1)
      this.parametersOpen = key.value === 'params'
      if (key.value === 'lines') {
        this.requireEmpty(value, 'lines')
        return index
      }
      this.apply(key.value, value, line.start + key.value.length + 1)
      this.fields.push({ key: key.value, start: line.start, end: line.start + line.text.length })
      index += 1
    }
    throw new Error('BEScript 文件缺少 lines: 头')
  }

  /** @anchor bescript-code-file */
  private sourceText(linesHeader: number): string {
    const header: LineSpan = this.lineAt(linesHeader)
    const sourceStart: number = header.start + header.text.length + 1
    for (let index: number = this.lines.length - 1; index > linesHeader; index -= 1) {
      const line: LineSpan = this.lineAt(index)
      if (line.text.length === 0) continue
      if (line.text !== 'endcode') {
        throw new Error('endcode 之后不允许再有内容或文件缺少 endcode')
      }
      this.requireTailBlank(index + 1)
      this.bodyEnd = line.start
      return this.source.slice(sourceStart, line.start)
    }
    throw new Error('BEScript 文件缺少 endcode')
  }

  /** @anchor bescript-code-file */
  private requireTailBlank(start: number): void {
    for (let index: number = start; index < this.lines.length; index += 1) {
      if (this.lineAt(index).text.length !== 0) {
        throw new Error('endcode 之后不允许再有内容')
      }
    }
  }

  /** @anchor bescript-code-file */
  private header(): ScriptHeader {
    if (Option.isNone(this.name)) {
      throw new Error('BEScript 文件缺少 name')
    }
    return {
      name: this.name.value,
      comment: this.comment,
      debug: this.debug,
      skip: this.skip,
      isPrivate: this.isPrivate,
      guid: this.guid,
      color: this.color,
      parameters: this.parameters,
      returnParameter: this.returnParameter,
    }
  }

  /** @anchor bescript-code-file */
  private apply(key: HeaderKey, value: string, valueStart: number): void {
    switch (key) {
      case 'name':
        this.rejectDuplicate(this.name, 'name')
        this.name = Option.some(this.plainName(value, '脚本名'))
        return
      case 'comment':
        this.rejectDuplicate(this.comment, 'comment')
        this.comment = Option.some(BescriptVocabulary.decode(value))
        return
      case 'guid':
        this.guid = Option.some(this.guidValue(value))
        return
      case 'color':
        this.color = Option.some(this.colorValue(value))
        return
      case 'params':
        if (this.paramsSeen) {
          throw new Error('params 重复')
        }
        this.paramsSeen = true
        this.appendParameters(value, valueStart)
        return
      case 'return':
        this.rejectDuplicate(this.returnParameter, 'return')
        this.returnParameter = Option.some(this.parameter(value, '返回'))
        return
      case 'debug':
        this.debug = this.flag(value, 'debug')
        return
      case 'skip':
        this.skip = this.flag(value, 'skip')
        return
      case 'private':
        this.isPrivate = this.flag(value, 'private')
        return
      case 'lines':
        throw new Error('lines 头不能作为普通字段重复进入')
    }
  }

  /** @anchor bescript-code-file */
  private rejectDuplicate(current: Option.Option<unknown>, label: string): void {
    if (Option.isSome(current)) {
      throw new Error(label + ' 重复')
    }
  }

  /** @anchor bescript-code-file */
  private appendParameters(value: string, valueStart: number): void {
    let start: number = valueStart
    for (const segment of value.split(',')) {
      if (segment.length !== 0) {
        this.parameters.push(this.parameter(segment, '参数'))
        const offsets: number[] = []
        let offset: number = start
        for (const word of segment.split(' ')) {
          if (word.length !== 0) offsets.push(offset)
          offset += word.length + 1
        }
        const parameter: ScriptParameter = Option.getOrThrow(
          Option.fromNullishOr(this.parameters.at(-1)),
        )
        const typeStart: number = Option.getOrThrow(Option.fromNullishOr(offsets[0]))
        const nameStart: number = Option.getOrThrow(Option.fromNullishOr(offsets[1]))
        this.parameterSources.push({
          typeStart,
          typeEnd: typeStart + parameter.typeName.length,
          nameStart,
          nameEnd: nameStart + parameter.name.length,
        })
      }
      start += segment.length + 1
    }
  }

  /** @anchor bescript-code-file */
  private parameter(value: string, label: string): ScriptParameter {
    const words: readonly string[] = CodeFileReader.words(value)
    const typeName: Option.Option<string> = Option.fromNullishOr(words[0])
    const name: Option.Option<string> = Option.fromNullishOr(words[1])
    if (words.length !== 2 || Option.isNone(typeName) || Option.isNone(name)) {
      throw new Error(label + ' 必须是“类型 名称”')
    }
    const parsed: VarTypeName = BescriptVocabulary.varType.parse(typeName.value)
    return { typeName: parsed, name: this.plainName(name.value, label + '名') }
  }

  /** @anchor bescript-code-file */
  private plainName(value: string, label: string): string {
    if (value.length === 0 || BescriptVocabulary.rejectsEscaped(value)) {
      throw new Error(label + ' 为空或包含需要转义的字符')
    }
    return value
  }

  /** @anchor bescript-code-file */
  private guidValue(value: string): string {
    if (Option.isSome(this.guid)) {
      throw new Error('guid 重复')
    }
    if (!GuidFormat.validate(value)) {
      throw new Error('guid 必须是 32 位小写十六进制')
    }
    return value
  }

  /** @anchor bescript-code-file */
  private colorValue(value: string): RgbColor {
    if (Option.isSome(this.color)) {
      throw new Error('color 重复')
    }
    const parts: readonly string[] = value.split(',')
    if (parts.length !== 3) {
      throw new Error('color 必须是红,绿,蓝三个整数')
    }
    return {
      red: this.channel(this.partAt(parts, 0)),
      green: this.channel(this.partAt(parts, 1)),
      blue: this.channel(this.partAt(parts, 2)),
    }
  }

  /**
   * @anchor bescript-code-file
   * @format-exempt bescript-scalar-text
   */
  private channel(text: string): number {
    return z
      .string()
      .regex(/^[0-9]{1,3}$/u)
      .transform((value: string): number => Number(value))
      .pipe(z.number().int().min(0).max(255))
      .parse(text)
  }

  /** @anchor bescript-code-file */
  private flag(value: string, label: string): boolean {
    this.requireEmptyFlag(label)
    if (value !== '1') {
      throw new Error(label + ' 的值必须是 1')
    }
    return true
  }

  /** @anchor bescript-code-file */
  private requireEmptyFlag(label: string): void {
    if (
      (label === 'debug' && this.debug) ||
      (label === 'skip' && this.skip) ||
      (label === 'private' && this.isPrivate)
    ) {
      throw new Error(label + ' 重复')
    }
  }

  /** @anchor bescript-code-file */
  private requireEmpty(value: string, label: string): void {
    if (value.length !== 0) {
      throw new Error(label + ' 不允许携带值')
    }
  }

  /** @anchor bescript-code-file */
  private lineAt(index: number): LineSpan {
    const line: Option.Option<LineSpan> = Option.fromNullishOr(this.lines[index])
    if (Option.isNone(line)) {
      throw new Error('BEScript 行号超出文件')
    }
    return line.value
  }

  /** @anchor bescript-code-file */
  private partAt(parts: readonly string[], index: number): string {
    const part: Option.Option<string> = Option.fromNullishOr(parts[index])
    if (Option.isNone(part)) {
      throw new Error('颜色通道缺失')
    }
    return part.value
  }

  /** @anchor bescript-code-file */
  private static split(source: string): readonly LineSpan[] {
    const lines: LineSpan[] = []
    let start: number = 0
    for (let index: number = 0; index < source.length; index += 1) {
      if (source[index] !== '\n') {
        continue
      }
      lines.push({ text: source.slice(start, index), start })
      start = index + 1
    }
    if (start < source.length) {
      lines.push({ text: source.slice(start), start })
    }
    return lines
  }

  /** @anchor bescript-code-file */
  private static headerKey(line: string): Option.Option<HeaderKey> {
    for (const key of CodeFileReader.keys) {
      const prefix: string = key + ':'
      if (line.startsWith(prefix)) {
        return Option.some(key)
      }
    }
    return Option.none()
  }

  /** @anchor bescript-code-file */
  private static words(value: string): readonly string[] {
    return value.split(' ').filter((word: string): boolean => word.length > 0)
  }
}
