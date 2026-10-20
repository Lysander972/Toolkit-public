import { Option } from 'effect'
import ts from 'typescript'

/** @anchor bescript-ts-syntax */
export class TsSyntax {
  /** @anchor bescript-ts-syntax */
  private constructor() {}

  /** @anchor bescript-ts-syntax */
  public static name(node: ts.Node): string {
    if (ts.isIdentifier(node) || ts.isStringLiteral(node)) return node.text
    throw new Error('只支持标识符或字符串成员名')
  }

  /** @anchor bescript-ts-syntax */
  public static access(owner: ts.Expression, name: string): ts.Expression {
    return ts.factory.createElementAccessExpression(owner, ts.factory.createStringLiteral(name))
  }

  /** @anchor bescript-ts-syntax */
  public static path(name: string): ts.Expression {
    const parts: string[] = name.split('.')
    let expression: ts.Expression = ts.factory.createIdentifier(
      Option.getOrThrow(Option.fromNullishOr(parts.shift())),
    )
    for (const part of parts)
      expression = ts.factory.createPropertyAccessExpression(expression, part)
    return expression
  }

  /** @anchor bescript-ts-syntax */
  public static pathName(expression: ts.Expression): string {
    const node: ts.Expression = TsSyntax.unwrap(expression)
    if (ts.isIdentifier(node)) return node.text
    if (node.kind === ts.SyntaxKind.ThisKeyword) return 'this'
    if (ts.isPropertyAccessExpression(node))
      return TsSyntax.pathName(node.expression) + '.' + node.name.text
    if (ts.isElementAccessExpression(node) && ts.isStringLiteral(node.argumentExpression))
      return TsSyntax.pathName(node.expression) + '.' + node.argumentExpression.text
    throw new Error('不支持的调用属主或成员表达式')
  }

  /** @anchor bescript-ts-syntax */
  public static unwrap(expression: ts.Expression): ts.Expression {
    let current: ts.Expression = expression
    while (ts.isParenthesizedExpression(current)) current = current.expression
    return current
  }

  /** @anchor bescript-ts-syntax */
  public static call(expression: ts.Expression): ts.CallExpression {
    const node: ts.Expression = TsSyntax.unwrap(expression)
    if (!ts.isCallExpression(node)) throw new Error('此位置必须是调用')
    return node
  }

  /**
   * @anchor bescript-ts-syntax
   * @fallback 官方转译结果没有诊断集合时按无诊断处理。
   */
  public static parse(text: string): ts.SourceFile {
    const result: ts.TranspileOutput = ts.transpileModule(text, {
      reportDiagnostics: true,
      compilerOptions: { target: ts.ScriptTarget.ES2022, experimentalDecorators: true },
    })
    for (const diagnostic of Option.getOrElse(
      Option.fromNullishOr(result.diagnostics),
      (): readonly ts.Diagnostic[] => [],
    )) {
      if (diagnostic.category === ts.DiagnosticCategory.Error)
        throw new Error(ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n'))
    }
    return ts.createSourceFile('view.ts', text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  }

  /**
   * @anchor bescript-ts-syntax
   * @fallback 官方节点没有修饰符集合时按空集合处理。
   */
  public static modifiers(node: ts.Node): readonly ts.Modifier[] {
    return ts.canHaveModifiers(node)
      ? Option.getOrElse(
          Option.fromNullishOr(ts.getModifiers(node)),
          (): readonly ts.Modifier[] => [],
        )
      : []
  }

  /** @anchor bescript-ts-syntax */
  public static hasModifier(node: ts.Node, kind: ts.SyntaxKind): boolean {
    return TsSyntax.modifiers(node).some((modifier: ts.Modifier): boolean => modifier.kind === kind)
  }

  /** @anchor bescript-ts-syntax */
  public static print(node: ts.Node): string {
    return ts
      .createPrinter()
      .printNode(
        ts.EmitHint.Unspecified,
        node,
        ts.createSourceFile('view.ts', '', ts.ScriptTarget.Latest, false, ts.ScriptKind.TS),
      )
  }
}
