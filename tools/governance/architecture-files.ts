import { posix } from 'node:path'
import { Option } from 'effect'
import ts from 'typescript'
import { TypeScriptSource } from './typescript-source.ts'
import type {
  ArchitectureContainer,
  ArchitectureDeclarations,
  ArchitectureElement,
} from './derive-rules.ts'
import type { ReadonlyDeep } from 'type-fest'

/** @anchor architecture-files */
export interface ArchitectureFileReport {
  violations: string[]
}

/** @anchor architecture-files */
export class ArchitectureFiles {
  private constructor() {}

  /** @anchor architecture-files */
  public static codeReferences(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    sources: ReadonlyMap<string, string>,
  ): string[] {
    const violations: string[] = []
    const declarations: ReadonlySet<string> = new Set(
      [...sources].flatMap(([relativePath, content]: [string, string]): string[] => [
        ...TypeScriptSource.declarations(content, relativePath),
      ]),
    )
    const byId: ReadonlyMap<string, ReadonlyDeep<ArchitectureElement>> = new Map(
      model.elements.map(
        (
          element: ReadonlyDeep<ArchitectureElement>,
        ): [string, ReadonlyDeep<ArchitectureElement>] => [element.id, element],
      ),
    )
    const isWithinElement: (elementId: string, ownerId: string) => boolean = (
      elementId: string,
      ownerId: string,
    ): boolean => {
      let current: Option.Option<ReadonlyDeep<ArchitectureElement>> = Option.fromNullishOr(
        byId.get(elementId),
      )
      while (Option.isSome(current)) {
        if (current.value.id === ownerId) return true
        current = Option.isSome(current.value.parentId)
          ? Option.fromNullishOr(byId.get(current.value.parentId.value))
          : Option.none()
      }
      return false
    }
    for (const element of model.elements) {
      for (const reference of element.code) {
        const [relativePath, symbol]: string[] = reference.split('#')
        if (
          relativePath === undefined ||
          symbol === undefined ||
          reference.split('#').length !== 2
        ) {
          violations.push(`${element.id}: 非法代码引用: ${reference}`)
          continue
        }
        const owner: Option.Option<string> = Option.fromNullishOr(
          model.containers.find((container: ReadonlyDeep<ArchitectureContainer>): boolean =>
            container.files.some((declaration: string): boolean =>
              ArchitectureFiles.matches(relativePath, declaration),
            ),
          )?.id,
        )
        if (!declarations.has(relativePath + '#/' + symbol.replaceAll('.', '/'))) {
          violations.push(`${element.id}: 代码对应不存在: ${reference}`)
        } else if (Option.isNone(owner) || !isWithinElement(element.id, owner.value)) {
          violations.push(`${element.id}: 代码引用越出自身架构边界: ${reference}`)
        }
      }
    }
    return violations
  }

  /**
   * @anchor architecture-files
   * @format-exempt architecture-path-annotation
   */
  public static suspectedPaths(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    text: string,
    relativePath: string,
  ): string[] {
    const roots: Set<string> = new Set()
    for (const element of model.elements) {
      for (const declaration of element.files) {
        const segments: string[] = declaration.split('/')
        const root: Option.Option<string> = Option.fromNullishOr(segments[0])
        if (segments.length > 1 && Option.isSome(root)) {
          roots.add(root.value)
        }
      }
    }
    const source: ts.SourceFile = ts.createSourceFile(
      relativePath,
      text,
      ts.ScriptTarget.Latest,
      true,
    )
    const lineTagRanges: readonly (readonly [number, number])[] = ArchitectureFiles.lineTagRanges(
      source,
      text,
      '@non-manual-path',
    )
    const violations: string[] = []
    const pathCallNames: ReadonlySet<string> = new Set([
      'readFileSync',
      'writeFileSync',
      'readdirSync',
      'mkdirSync',
      'rmSync',
      'join',
      'resolve',
      'relative',
      'treeFile',
      'file',
      'glob',
      'globSync',
    ])
    const isPathArgument: (node: ts.Expression) => boolean = (node: ts.Expression): boolean => {
      const parent: ts.Node = node.parent
      if (!ts.isCallExpression(parent)) {
        return false
      }
      const expression: ts.Expression = parent.expression
      const name: Option.Option<string> = ts.isIdentifier(expression)
        ? Option.some(expression.text)
        : ts.isPropertyAccessExpression(expression)
          ? Option.some(expression.name.text)
          : Option.none()
      return (
        Option.isSome(name) &&
        pathCallNames.has(name.value) &&
        parent.arguments.some((argument: ts.Expression): boolean => argument === node)
      )
    }
    const visit: (node: ts.Node) => void = (node: ts.Node): void => {
      if (ts.isStringLiteralLike(node) || ts.isTemplateHead(node)) {
        const value: string = node.text.startsWith('./') ? node.text.slice(2) : node.text
        const candidate: boolean = [...roots].some(
          (root: string): boolean => value === root || value.startsWith(root + '/'),
        )
        if (candidate && ts.isStringLiteralLike(node) && isPathArgument(node)) {
          const exempt: boolean = lineTagRanges.some(
            ([start, end]: readonly [number, number]): boolean =>
              node.getStart(source) >= start && node.end <= end,
          )
          if (!exempt) {
            const line: number =
              source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1
            violations.push(
              `${relativePath}:${String(line)}: 潜在手动枚举路径 ${node.text}；扫描范围须来自架构模型，其他用途需 @non-manual-path 理由`,
            )
          }
        }
      }
      ts.forEachChild(node, visit)
    }
    visit(source)
    return violations
  }

  /** @anchor architecture-files */
  private static lineTagRanges(
    source: ts.SourceFile,
    text: string,
    tag: string,
  ): readonly (readonly [number, number])[] {
    const ranges: (readonly [number, number])[] = []
    const visit: (node: ts.Node) => void = (node: ts.Node): void => {
      if (ts.isStatement(node)) {
        const comments: Option.Option<readonly ts.CommentRange[]> = Option.fromNullishOr(
          ts.getLeadingCommentRanges(text, node.pos),
        )
        if (Option.isSome(comments)) {
          const startLine: number = source.getLineAndCharacterOfPosition(node.getStart(source)).line
          if (
            comments.value.some((comment: ts.CommentRange): boolean => {
              const commentLine: number = source.getLineAndCharacterOfPosition(comment.pos).line
              const raw: string = text.slice(comment.pos, comment.end)
              const body: string = raw.startsWith('//')
                ? raw.slice(2)
                : raw.startsWith('/*') && raw.endsWith('*/')
                  ? raw.slice(2, -2)
                  : raw
              let index: number = 0
              while (index < body.length && (body[index] === ' ' || body[index] === '\t'))
                index += 1
              return commentLine + 1 === startLine && body.slice(index).startsWith(tag)
            })
          ) {
            ranges.push([node.getStart(source), node.end])
          }
        }
      }
      ts.forEachChild(node, visit)
    }
    visit(source)
    return ranges
  }

  /** @anchor architecture-files */
  public static matches(relativePath: string, fileRelativePath: string): boolean {
    return fileRelativePath.endsWith('/')
      ? relativePath.startsWith(fileRelativePath)
      : posix.matchesGlob(relativePath, fileRelativePath)
  }

  /** @anchor architecture-files */
  public static check(
    model: ReadonlyDeep<ArchitectureDeclarations>,
    files: readonly string[],
  ): ArchitectureFileReport {
    const report: ArchitectureFileReport = { violations: [] }
    const owners: Map<string, string> = new Map<string, string>()
    for (const element of model.elements) {
      for (const entry of element.entries) {
        if (!files.includes(entry)) {
          report.violations.push(`${element.id}: 入口文件不存在或未登记: ${entry}`)
        } else if (
          !element.files.some((declaration: string): boolean =>
            ArchitectureFiles.matches(entry, declaration),
          )
        ) {
          report.violations.push(`${element.id}: 入口文件越出自身文件边界: ${entry}`)
        }
      }
      for (const fileRelativePath of element.files) {
        const matches: string[] = files.filter((relativePath: string): boolean =>
          ArchitectureFiles.matches(relativePath, fileRelativePath),
        )
        if (matches.length === 0) {
          report.violations.push(`${element.id}: 文件声明未命中任何文件: ${fileRelativePath}`)
        }
        for (const relativePath of matches) {
          const previous: Option.Option<string> = Option.fromNullishOr(owners.get(relativePath))
          if (Option.isSome(previous)) {
            report.violations.push(
              `${relativePath}: 重复归属 ${previous.value} 与 ${element.id} (${fileRelativePath})`,
            )
          }
          owners.set(relativePath, element.id)
        }
      }
    }
    for (const relativePath of files) {
      if (!owners.has(relativePath)) {
        report.violations.push(`${relativePath}: 未在架构模型中登记`)
      }
      const ownerId: Option.Option<string> = Option.fromNullishOr(owners.get(relativePath))
      if (
        Option.isSome(ownerId) &&
        (relativePath.endsWith('.ts') || relativePath.endsWith('.tsx'))
      ) {
        const owner: ReadonlyDeep<ArchitectureElement> = Option.getOrThrow(
          Option.fromNullishOr(
            model.elements.find(
              (element: ReadonlyDeep<ArchitectureElement>): boolean => element.id === ownerId.value,
            ),
          ),
        )
        if (
          owner.kind !== 'document' &&
          owner.purpose !== 'doc' &&
          (owner.id !== model.rootElementId ||
            relativePath.startsWith(model.sourceRelativePath + '/')) &&
          !model.containers.some(
            (container: ReadonlyDeep<ArchitectureContainer>): boolean => container.id === owner.id,
          )
        ) {
          report.violations.push(`${owner.id}: 源码文件必须登记 sourcePath: ${relativePath}`)
        }
      }
      for (const container of model.containers) {
        if (
          relativePath.startsWith(container.sourceRelativePath + '/') &&
          (relativePath.endsWith('.ts') || relativePath.endsWith('.tsx')) &&
          Option.isSome(ownerId) &&
          ownerId.value !== container.id
        ) {
          report.violations.push(
            `${relativePath}: sourcePath 归属 ${container.id} 与文件归属 ${ownerId.value} 冲突`,
          )
        }
      }
    }
    report.violations.sort()
    return report
  }
}
