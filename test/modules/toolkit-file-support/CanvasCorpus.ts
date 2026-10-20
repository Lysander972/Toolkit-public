import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'

type InlineCanvasFormat = 'base64' | 'hjson'

/** @anchor canvas-convert */
export interface InlineCanvasFixture {
  readonly relativePath: string
  readonly occurrence: number
  readonly format: InlineCanvasFormat
  readonly text: string
}

/** @anchor canvas-convert */
export interface BinaryCanvasFixture {
  readonly relativePath: string
  readonly bytes: Uint8Array
}

/** @anchor canvas-convert */
export class CanvasCorpusFixtures {
  private constructor() {}
  /** @anchor canvas-convert */
  public static binaryCanvasFixtures(): readonly BinaryCanvasFixture[] {
    // @non-manual-path 测试语料固定根目录
    const projectsRoot: string = join(process.cwd(), 'test', 'data', 'projects')
    const fixtures: BinaryCanvasFixture[] = []
    for (const fullPath of CanvasCorpusFixtures.files(projectsRoot, '.canvas')) {
      fixtures.push({
        relativePath: relative(process.cwd(), fullPath).replaceAll('\\', '/'),
        bytes: Uint8Array.from(readFileSync(fullPath)),
      })
    }
    return fixtures
  }
  /** @anchor canvas-convert */
  public static inlineCanvasFixtures(): readonly InlineCanvasFixture[] {
    // @non-manual-path 测试语料固定根目录
    const projectsRoot: string = join(process.cwd(), 'test', 'data', 'projects')
    const fixtures: InlineCanvasFixture[] = []
    for (const fullPath of CanvasCorpusFixtures.files(projectsRoot, '.code')) {
      const source: string = readFileSync(fullPath, 'utf8')
      const relativePath: string = relative(process.cwd(), fullPath).replaceAll('\\', '/')
      let occurrence: number = 0
      let index: number = 0
      while (index < source.length) {
        const legacyStart: number = source.indexOf('canvas@1', index)
        const hjsonStart: number = source.indexOf('==hjson@1', index)
        if (legacyStart < 0 && hjsonStart < 0) break
        if (legacyStart >= 0 && (hjsonStart < 0 || legacyStart < hjsonStart)) {
          const end: number = CanvasCorpusFixtures.nextTokenEnd(source, legacyStart)
          const text: string = CanvasCorpusFixtures.recover(source.slice(legacyStart, end))
          if (text.startsWith('canvas:')) {
            occurrence += 1
            fixtures.push({ relativePath, occurrence, format: 'base64', text })
          }
          index = end
          continue
        }
        const end: number = source.indexOf('==)', hjsonStart)
        if (end < 0) throw new Error(`内联 Hjson 缺少结束标记: ${relativePath}`)
        const text: string = CanvasCorpusFixtures.recover(source.slice(hjsonStart, end + 2))
        occurrence += 1
        fixtures.push({
          relativePath,
          occurrence,
          format: 'hjson',
          text: text.replaceAll('\r\n', '\n').replaceAll('\r', '\n'),
        })
        index = end + 3
      }
    }
    return fixtures
  }
  private static nextTokenEnd(source: string, start: number): number {
    let index: number = start
    while (index < source.length) {
      const value: string = source.charAt(index)
      if (value === ')' || value === '\r' || value === '\n' || value === '\t' || value === ' ')
        return index
      index += 1
    }
    return source.length
  }
  /** @fallback 未知转义保持原文以便后续解析失败自然暴露 */
  private static recover(value: string): string {
    const parts: string[] = []
    let index: number = 0
    while (index < value.length) {
      const current: string = value.charAt(index)
      if (current !== '@' || index === value.length - 1) {
        parts.push(current)
        index += 1
        continue
      }
      const escaped: string = value.charAt(index + 1)
      const decoded: string = CanvasCorpusFixtures.escape(escaped)
      parts.push(decoded === '' ? `@${escaped}` : decoded)
      index += 2
    }
    return parts.join('')
  }
  private static escape(value: string): string {
    switch (value) {
      case '1':
        return ':'
      case '2':
        return ';'
      case '3':
        return '='
      case '4':
        return '('
      case '5':
        return ')'
      case '6':
        return '/'
      case '7':
        return '\\'
      case '8':
        return ' '
      case '9':
        return '.'
      case '@':
        return '@'
      default:
        return ''
    }
  }
  private static files(directory: string, extension: string): readonly string[] {
    const found: string[] = []
    for (const name of readdirSync(directory)) {
      const fullPath: string = join(directory, name)
      if (statSync(fullPath).isDirectory())
        found.push(...CanvasCorpusFixtures.files(fullPath, extension))
      else if (name.endsWith(extension)) found.push(fullPath)
    }
    return found.sort()
  }
}
