import ts from 'typescript'
import { posix } from 'node:path'
import { Option } from 'effect'

/** @anchor environment-boundary */
interface EnvironmentReference {
  readonly name: string
  readonly line: number
  readonly position: number
}

/** @anchor environment-boundary */
export class EnvironmentScan {
  private constructor() {}

  /** @anchor environment-boundary */
  public static check(
    sourceText: string,
    relativePath: string,
    registered: readonly string[],
    constants: ReadonlyMap<string, string>,
  ): string[] {
    const references: readonly EnvironmentReference[] = EnvironmentScan.apiReferences(
      sourceText,
      relativePath,
      constants,
    )
    const violations: string[] = references
      .filter((reference: EnvironmentReference): boolean => !registered.includes(reference.name))
      .map((reference: EnvironmentReference): string =>
        reference.name === '*'
          ? `${relativePath}:${String(reference.line)}: 环境变量访问未明确名称，必须消除动态读取或登记具体访问用途`
          : `${relativePath}:${String(reference.line)}: 未登记环境变量 ${reference.name}`,
      )
    return [
      ...violations,
      ...EnvironmentScan.suspected(
        sourceText,
        relativePath,
        registered,
        true,
        references.map((reference: EnvironmentReference): number => reference.position),
      ),
    ]
  }

  /**
   * @anchor environment-text
   * @format-exempt environment-text-patterns
   */
  public static suspected(
    text: string,
    relativePath: string,
    registered: readonly string[],
    typescript: boolean,
    exactPositions: readonly number[],
  ): string[] {
    const violations: string[] = []
    const report: (content: string, line: number) => void = (
      content: string,
      line: number,
    ): void => {
      const pattern: RegExp = typescript
        ? /^[A-Z][A-Z0-9]*_[A-Z0-9_]*$/g
        : /\b[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+\b/g
      for (const match of content.matchAll(pattern)) {
        if (!registered.includes(match[0])) {
          violations.push(
            `${relativePath}:${String(line)}: 疑似环境变量 ${match[0]}，需登记、删除或标记 @non-environment-variable`,
          )
        }
      }
    }
    if (!typescript) {
      text.split('\n').forEach((line: string, index: number): void => {
        // eslint-disable-next-line no-restricted-properties -- 外部文本的缩进注释由首个非空白字符 # 标记。
        const content: string = line.trimStart()
        if (!content.startsWith('#') && !/@non-environment-variable\s+\S/.test(content)) {
          report(line, index + 1)
        }
      })
      return violations
    }
    const source: ts.SourceFile = ts.createSourceFile(
      relativePath,
      text,
      ts.ScriptTarget.Latest,
      true,
      ts.ScriptKind.TS,
    )
    const lineTagRanges: readonly (readonly [number, number])[] = EnvironmentScan.lineTagRanges(
      source,
      text,
      '@non-environment-variable',
    )
    const visit: (node: ts.Node) => void = (node: ts.Node): void => {
      if (
        ts.isStringLiteralLike(node) ||
        ts.isTemplateHead(node) ||
        ts.isTemplateMiddle(node) ||
        ts.isTemplateTail(node)
      ) {
        const parent: ts.Node = node.parent
        const key: boolean =
          (ts.isPropertyAssignment(parent) ||
            ts.isMethodDeclaration(parent) ||
            ts.isPropertyDeclaration(parent) ||
            ts.isPropertySignature(parent)) &&
          parent.name === node
        const module: boolean = ts.isImportDeclaration(parent) || ts.isExportDeclaration(parent)
        const exempt: boolean = lineTagRanges.some(
          ([start, end]: readonly [number, number]): boolean =>
            node.getStart(source) >= start && node.end <= end,
        )
        if (!key && !module && !exempt && !exactPositions.includes(node.getStart(source))) {
          report(node.text, source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1)
        }
      }
      ts.forEachChild(node, visit)
    }
    visit(source)
    return violations
  }

  /** @anchor environment-text */
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

  /** @anchor environment-boundary */
  public static apiReferences(
    sourceText: string,
    relativePath: string,
    constants: ReadonlyMap<string, string>,
  ): readonly EnvironmentReference[] {
    const source: ts.SourceFile = ts.createSourceFile(
      relativePath,
      sourceText,
      ts.ScriptTarget.Latest,
      true,
      ts.ScriptKind.TS,
    )
    const references: EnvironmentReference[] = []
    const options: ts.CompilerOptions = { noLib: true, noResolve: true, types: [] }
    const host: ts.CompilerHost = ts.createCompilerHost(options)
    host.getSourceFile = (fileName: string): ts.SourceFile => {
      if (fileName !== source.fileName) {
        throw new Error(`Unexpected source file: ${fileName}`)
      }
      return source
    }
    const checker: ts.TypeChecker = ts
      .createProgram([source.fileName], options, host)
      .getTypeChecker()
    const processes: Set<ts.Identifier> = new Set()
    const environments: Set<ts.Identifier> = new Set()
    const registries: Set<ts.Identifier> = new Set()
    const launches: Set<ts.Identifier> = new Set()
    const childProcesses: Set<ts.Identifier> = new Set()
    const launchNames: readonly string[] = [
      'spawn',
      'spawnSync',
      'exec',
      'execSync',
      'execFile',
      'execFileSync',
      'fork',
    ]
    const boundTo: (identifier: ts.Identifier, bindings: ReadonlySet<ts.Identifier>) => boolean = (
      identifier: ts.Identifier,
      bindings: ReadonlySet<ts.Identifier>,
    ): boolean =>
      checker
        .getSymbolAtLocation(identifier)
        ?.declarations?.some(
          (declaration: ts.Declaration): boolean =>
            (ts.isImportSpecifier(declaration) ||
              ts.isImportClause(declaration) ||
              ts.isNamespaceImport(declaration)) &&
            declaration.name !== undefined &&
            bindings.has(declaration.name),
        ) === true
    // noLib 下 TS 仍合成 globalThis 符号（declarations 为空），未绑定判定须看声明有无。
    const unboundGlobal: (identifier: ts.Identifier) => boolean = (
      identifier: ts.Identifier,
    ): boolean => {
      const symbol: Option.Option<ts.Symbol> = Option.fromNullishOr(
        checker.getSymbolAtLocation(identifier),
      )
      return (
        Option.isNone(symbol) ||
        symbol.value.declarations === undefined ||
        symbol.value.declarations.length === 0
      )
    }
    for (const statement of source.statements) {
      if (
        ts.isImportDeclaration(statement) &&
        ts.isStringLiteral(statement.moduleSpecifier) &&
        ['child_process', 'node:child_process'].includes(statement.moduleSpecifier.text) &&
        statement.importClause !== undefined &&
        statement.importClause.phaseModifier !== ts.SyntaxKind.TypeKeyword
      ) {
        const clause: ts.ImportClause = statement.importClause
        if (clause.name !== undefined) {
          childProcesses.add(clause.name)
        }
        const bindings: Option.Option<ts.NamedImportBindings> = Option.fromNullishOr(
          clause.namedBindings,
        )
        if (Option.isSome(bindings) && ts.isNamespaceImport(bindings.value)) {
          childProcesses.add(bindings.value.name)
        }
        if (Option.isSome(bindings) && ts.isNamedImports(bindings.value)) {
          for (const binding of bindings.value.elements) {
            const imported: string = EnvironmentScan.importedName(binding)
            if (!binding.isTypeOnly && launchNames.includes(imported)) {
              launches.add(binding.name)
            }
          }
        }
      }
      // @non-manual-path 集中环境变量登记模块的导入身份
      if (
        ts.isImportDeclaration(statement) &&
        ts.isStringLiteral(statement.moduleSpecifier) &&
        posix.join(posix.dirname(relativePath), statement.moduleSpecifier.text) ===
          'tools/cli/EnvironmentVariables.ts'
      ) {
        const bindings: Option.Option<ts.NamedImportBindings> = Option.fromNullishOr(
          statement.importClause?.namedBindings,
        )
        if (
          Option.isSome(bindings) &&
          ts.isNamedImports(bindings.value) &&
          statement.importClause?.phaseModifier !== ts.SyntaxKind.TypeKeyword
        ) {
          for (const binding of bindings.value.elements) {
            const imported: string = EnvironmentScan.importedName(binding)
            if (!binding.isTypeOnly && imported === 'EnvironmentVariables') {
              registries.add(binding.name)
            }
          }
        }
      }
      if (
        !ts.isImportDeclaration(statement) ||
        !ts.isStringLiteral(statement.moduleSpecifier) ||
        !['process', 'node:process'].includes(statement.moduleSpecifier.text)
      ) {
        continue
      }
      const clause: Option.Option<ts.ImportClause> = Option.fromNullishOr(statement.importClause)
      if (Option.isNone(clause) || clause.value.phaseModifier === ts.SyntaxKind.TypeKeyword) {
        continue
      }
      if (clause.value.name !== undefined) {
        processes.add(clause.value.name)
      }
      const bindings: Option.Option<ts.NamedImportBindings> = Option.fromNullishOr(
        clause.value.namedBindings,
      )
      if (Option.isSome(bindings) && ts.isNamespaceImport(bindings.value)) {
        processes.add(bindings.value.name)
      }
      if (Option.isSome(bindings) && ts.isNamedImports(bindings.value)) {
        for (const binding of bindings.value.elements) {
          const imported: string = EnvironmentScan.importedName(binding)
          if (!binding.isTypeOnly && imported === 'env') {
            environments.add(binding.name)
          }
          if (!binding.isTypeOnly && imported === 'default') {
            processes.add(binding.name)
          }
        }
      }
    }
    const constantName: (node: ts.Node) => string = (node: ts.Node): string => {
      if (ts.isStringLiteralLike(node)) {
        return node.text
      }
      if (
        ts.isPropertyAccessExpression(node) &&
        ts.isIdentifier(node.expression) &&
        boundTo(node.expression, registries)
      ) {
        const name: Option.Option<string> = Option.fromNullishOr(
          constants.get('EnvironmentVariables.' + node.name.text),
        )
        if (Option.isSome(name)) {
          return name.value
        }
      }
      return '*'
    }
    const report: (node: ts.Node, name: string) => void = (node: ts.Node, name: string): void => {
      references.push({
        name,
        line: source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1,
        position: node.getStart(source),
      })
    }
    const objectProperties: (
      expression: ts.Expression,
      visited: ReadonlySet<ts.Symbol>,
    ) => readonly (ts.ObjectLiteralElementLike | ts.Expression)[] = (
      expression: ts.Expression,
      visited: ReadonlySet<ts.Symbol>,
    ): readonly (ts.ObjectLiteralElementLike | ts.Expression)[] => {
      if (
        ts.isParenthesizedExpression(expression) ||
        ts.isAsExpression(expression) ||
        ts.isSatisfiesExpression(expression)
      ) {
        return objectProperties(expression.expression, visited)
      }
      if (ts.isIdentifier(expression)) {
        const symbol: Option.Option<ts.Symbol> = Option.fromNullishOr(
          ts.isShorthandPropertyAssignment(expression.parent)
            ? checker.getShorthandAssignmentValueSymbol(expression.parent)
            : checker.getSymbolAtLocation(expression),
        )
        const declaration: Option.Option<ts.Declaration> = Option.flatMap(
          symbol,
          (resolved: ts.Symbol): Option.Option<ts.Declaration> =>
            Option.fromNullishOr(resolved.valueDeclaration),
        )
        if (
          Option.isSome(symbol) &&
          !visited.has(symbol.value) &&
          Option.isSome(declaration) &&
          ts.isVariableDeclaration(declaration.value) &&
          declaration.value.initializer !== undefined
        ) {
          return objectProperties(
            declaration.value.initializer,
            new Set([...visited, symbol.value]),
          )
        }
      }
      if (ts.isObjectLiteralExpression(expression)) {
        return expression.properties.flatMap(
          (
            property: ts.ObjectLiteralElementLike,
          ): readonly (ts.ObjectLiteralElementLike | ts.Expression)[] =>
            ts.isSpreadAssignment(property)
              ? objectProperties(property.expression, visited)
              : [property],
        )
      }
      return [expression]
    }
    const visit: (node: ts.Node) => void = (node: ts.Node): void => {
      if (
        ts.isCallExpression(node) &&
        ((ts.isIdentifier(node.expression) && boundTo(node.expression, launches)) ||
          (ts.isPropertyAccessExpression(node.expression) &&
            ts.isIdentifier(node.expression.expression) &&
            boundTo(node.expression.expression, childProcesses) &&
            launchNames.includes(node.expression.name.text)))
      ) {
        for (const argument of node.arguments) {
          for (const option of objectProperties(argument, new Set())) {
            if (
              !(ts.isPropertyAssignment(option) || ts.isShorthandPropertyAssignment(option)) ||
              (ts.isIdentifier(option.name)
                ? option.name.text
                : constantName(
                    ts.isComputedPropertyName(option.name) ? option.name.expression : option.name,
                  )) !== 'env'
            ) {
              continue
            }
            const environment: ts.Expression = ts.isShorthandPropertyAssignment(option)
              ? option.name
              : option.initializer
            if (
              ts.isCallExpression(environment) &&
              ts.isPropertyAccessExpression(environment.expression) &&
              environment.expression.name.text === 'cleanEnv'
            ) {
              continue
            }
            for (const entry of objectProperties(environment, new Set())) {
              if (
                ts.isCallExpression(entry) &&
                ts.isPropertyAccessExpression(entry.expression) &&
                entry.expression.name.text === 'cleanEnv'
              ) {
                continue
              }
              if (
                ts.isSpreadAssignment(entry) &&
                ts.isCallExpression(entry.expression) &&
                ts.isPropertyAccessExpression(entry.expression.expression) &&
                entry.expression.expression.name.text === 'cleanEnv'
              ) {
                continue
              }
              if (ts.isPropertyAssignment(entry) || ts.isShorthandPropertyAssignment(entry)) {
                const name: string = ts.isIdentifier(entry.name)
                  ? entry.name.text
                  : constantName(
                      ts.isComputedPropertyName(entry.name) ? entry.name.expression : entry.name,
                    )
                report(entry.name, name)
              } else {
                report(entry, '*')
              }
            }
          }
        }
      }
      if (
        ((ts.isPropertyAccessExpression(node) || ts.isElementAccessExpression(node)) &&
          ((ts.isIdentifier(node.expression) &&
            (boundTo(node.expression, processes) ||
              (node.expression.text === 'process' &&
                checker.getSymbolAtLocation(node.expression) === undefined))) ||
            (ts.isPropertyAccessExpression(node.expression) &&
              node.expression.name.text === 'process' &&
              ts.isIdentifier(node.expression.expression) &&
              node.expression.expression.text === 'globalThis' &&
              unboundGlobal(node.expression.expression))) &&
          (ts.isPropertyAccessExpression(node)
            ? node.name.text === 'env'
            : ts.isStringLiteral(node.argumentExpression) &&
              node.argumentExpression.text === 'env')) ||
        (ts.isIdentifier(node) &&
          boundTo(node, environments) &&
          !ts.isImportSpecifier(node.parent) &&
          !(ts.isPropertyAccessExpression(node.parent) && node.parent.name === node))
      ) {
        const access: ts.Node = node.parent
        const line: number = source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1
        const position: number = ts.isElementAccessExpression(access)
          ? access.argumentExpression.getStart(source)
          : node.getStart(source)
        if (ts.isPropertyAccessExpression(access) && access.expression === node) {
          references.push({ name: access.name.text, line, position })
        } else if (ts.isElementAccessExpression(access) && access.expression === node) {
          references.push({ name: constantName(access.argumentExpression), line, position })
        } else {
          references.push({ name: '*', line, position })
        }
      }
      ts.forEachChild(node, visit)
    }
    visit(source)
    return [
      ...new Map(
        references.map((reference: EnvironmentReference): [string, EnvironmentReference] => [
          String(reference.position) + ':' + reference.name,
          reference,
        ]),
      ).values(),
    ]
  }
  /**
   * @anchor environment-boundary
   * @fallback TypeScript ImportSpecifier 未起别名时 name 即原始导入名。
   */
  private static importedName(binding: ts.ImportSpecifier): string {
    const original: Option.Option<ts.ModuleExportName> = Option.fromNullishOr(binding.propertyName)
    return Option.isSome(original) ? original.value.text : binding.name.text
  }
}
