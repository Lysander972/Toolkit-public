import { mkdir, readFile } from 'node:fs/promises'
import { dirname } from 'node:path'
import { isDeepStrictEqual } from 'node:util'
import { Option } from 'effect'
import { modify, applyEdits, parseTree } from 'jsonc-parser'
import type { ParseError } from 'jsonc-parser'
import { Document, parseDocument } from 'yaml'
import { parse, stringify } from 'smol-toml'
import { DOMParser, XMLSerializer } from '@xmldom/xmldom'
import type { Element } from '@xmldom/xmldom'
import writeFileAtomic from 'write-file-atomic'
import { z } from 'zod'

/** @anchor frontend-configuration-editor */
export type ConfigurationFormat = 'jsonc' | 'yaml' | 'toml' | 'xml'

/** @anchor frontend-configuration-editor */
export interface ConfigurationField {
  readonly segments: readonly string[]
  readonly value: unknown
}

/** @anchor frontend-configuration-editor */
export interface ConfigurationPlan {
  readonly fileFullPath: string
  readonly previous: Option.Option<string>
  readonly next: string
  readonly changed: boolean
}

/**
 * @anchor frontend-configuration-editor
 * @format-exempt toml-object-boundary
 */
interface TomlObject {
  readonly [key: string]: unknown
}

/**
 * TODO: 产品级完整 Schema 校验随正式配置接入实现；当前只验证结构化编辑合同。
 * @anchor frontend-configuration-editor
 */
export class ConfigurationEditor {
  private constructor() {}

  /** @anchor frontend-configuration-editor */
  public static async read(fileFullPath: string): Promise<Option.Option<string>> {
    try {
      return Option.some(await readFile(fileFullPath, 'utf8'))
    } catch (error: unknown) {
      if (error instanceof Error && 'code' in error && error.code === 'ENOENT') return Option.none()
      throw error
    }
  }

  /**
   * @anchor frontend-configuration-editor
   * @fallback 缺少旧配置时以空文档生成首次配置。
   */
  public static plan(
    fileFullPath: string,
    format: ConfigurationFormat,
    previous: Option.Option<string>,
    fields: readonly ConfigurationField[],
  ): ConfigurationPlan {
    const text: string = Option.getOrElse(previous, (): string => '')
    let next: string
    switch (format) {
      case 'jsonc':
        next = ConfigurationEditor.jsonc(text, fields)
        break
      case 'yaml':
        next = ConfigurationEditor.yaml(text, fields)
        break
      case 'toml':
        next = ConfigurationEditor.toml(text, fields)
        if (isDeepStrictEqual(parse(text), parse(next))) next = text
        break
      case 'xml':
        next = ConfigurationEditor.xml(text, fields)
        break
    }
    return { fileFullPath, previous, next, changed: text !== next }
  }

  /** @anchor frontend-configuration-editor */
  public static async apply(plan: ConfigurationPlan): Promise<boolean> {
    if (!plan.changed) return false
    const current: Option.Option<string> = await ConfigurationEditor.read(plan.fileFullPath)
    if (
      !Option.makeEquivalence<string>((left: string, right: string): boolean => left === right)(
        current,
        plan.previous,
      )
    ) {
      throw new Error('配置已改变，请重新读取')
    }
    await mkdir(dirname(plan.fileFullPath), { recursive: true })
    await writeFileAtomic(plan.fileFullPath, plan.next, { encoding: 'utf8' })
    return true
  }

  /** @anchor frontend-configuration-editor */
  private static jsonc(text: string, fields: readonly ConfigurationField[]): string {
    const errors: ParseError[] = []
    if (text !== '') {
      const tree: ReturnType<typeof parseTree> = parseTree(text, errors)
      if (errors.length > 0 || tree?.type !== 'object') throw new Error('JSONC 配置必须是有效对象')
    }
    let result: string = text
    for (const field of fields) {
      result = applyEdits(
        result,
        modify(result, [...field.segments], field.value, {
          formattingOptions: { insertSpaces: true, tabSize: 2 },
        }),
      )
    }
    return result
  }

  /** @anchor frontend-configuration-editor */
  private static yaml(text: string, fields: readonly ConfigurationField[]): string {
    const document: Document = parseDocument(text)
    if (document.errors.length > 0) {
      throw new Error('YAML 配置解析失败')
    }
    for (const field of fields) document.setIn([...field.segments], field.value)
    return document.toString()
  }

  /** @anchor frontend-configuration-editor */
  private static toml(text: string, fields: readonly ConfigurationField[]): string {
    const document: Document = new Document(parse(text))
    for (const field of fields) document.setIn([...field.segments], field.value)
    const value: TomlObject = z.record(z.string(), z.unknown()).parse(document.toJS())
    return stringify(value)
  }

  /**
   * @anchor frontend-configuration-editor
   * @fallback 缺少路径节点时创建用户明确要求写入的新节点。
   */
  private static xml(text: string, fields: readonly ConfigurationField[]): string {
    const parser: DOMParser = new DOMParser({
      onError: (level: string, message: string): never => {
        throw new Error(level + ': ' + message)
      },
    })
    const document: ReturnType<DOMParser['parseFromString']> = parser.parseFromString(
      text,
      'application/xml',
    )
    for (const field of fields) {
      let element: Element = Option.getOrThrow(Option.fromNullishOr(document.documentElement))
      const [root, ...segments]: readonly string[] = field.segments
      if (element.tagName !== root) throw new Error('XML 管理字段根节点不匹配')
      for (const segment of segments) {
        const matches: Element[] = []
        for (let index: number = 0; index < element.children.length; index += 1) {
          const child: Option.Option<Element> = Option.fromNullishOr(element.children.item(index))
          if (Option.isSome(child) && child.value.tagName === segment) matches.push(child.value)
        }
        if (matches.length > 1) throw new Error('XML 管理字段必须唯一')
        const found: Option.Option<Element> = Option.fromNullishOr(matches[0])
        if (Option.isSome(found)) element = found.value
        else {
          const created: Element = document.createElement(segment)
          element.appendChild(created)
          element = created
        }
      }
      element.textContent = z.string().parse(field.value)
    }
    return new XMLSerializer().serializeToString(document)
  }
}
