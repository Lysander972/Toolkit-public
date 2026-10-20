import ts from 'typescript'
import { Lang, parse } from '@ast-grep/napi'
import { Option } from 'effect'
import { TypeScriptSource } from '../../governance/typescript-source.ts'
import type { Rule, SgNode } from '@ast-grep/napi'
import type { SourceRange as ReviewRange } from '../../governance/source-text.ts'

/** @anchor structural-rules */
interface FormatRule {
  id: string
  rule: Rule
  message: string
}

type ExemptionTag = 'format-exempt' | 'passthrough-exempt'

/**
 * @anchor java-style
 * @anchor structural-rules
 */
export class StructuralRules {
  private constructor() {}

  /**
   * @anchor structural-rules
   * @anchor runtime-validation
   */
  public static check(source: string, relativePath: string): string[] {
    const rules: FormatRule[] = [
      {
        id: 'TK023',
        rule: {
          kind: 'union_type',
          not: { inside: { kind: 'required_parameter', stopBy: 'end' } },
          any: [
            { has: { kind: 'type_identifier' } },
            { has: { kind: 'predefined_type' } },
            { has: { kind: 'literal_type', has: { kind: 'null' } } },
            { has: { kind: 'literal_type', has: { kind: 'undefined' } } },
          ],
        },
        message:
          '类型注解禁止语义联合与可空联合；可空经 Option 容器表达，枚举域用同类型字面量联合。',
      },
      {
        id: 'TK026',
        rule: {
          kind: 'object_type',
          inside: { kind: 'type_annotation', stopBy: 'end' },
        },
        message: '对象结构必须具名 interface 声明，禁止内联抄写成员。',
      },
      {
        id: 'TK028',
        rule: {
          pattern: 'JSON.parse($$$ARGS)',
          not: { inside: { pattern: '$SCHEMA.parse($$$MORE)', stopBy: 'end' } },
        },
        message: 'JSON.parse 结果必须直接经 schema parse 校验。',
      },
      {
        id: 'TK029',
        rule: {
          any: [{ pattern: 'z.object($$$FIELDS)' }, { pattern: 'z.strictObject($$$FIELDS)' }],
          not: {
            inside: {
              any: [
                {
                  kind: 'variable_declarator',
                  has: { field: 'type', kind: 'type_annotation', stopBy: 'neighbor' },
                },
                {
                  kind: 'public_field_definition',
                  has: { field: 'type', kind: 'type_annotation', stopBy: 'neighbor' },
                },
              ],
              stopBy: 'end',
            },
          },
        },
        message: '对象 schema 必须挂在显式注解的声明上，对象结构必须落到具名契约。',
      },
    ]
    const exemptableRules: FormatRule[] = [
      {
        id: 'TK035',
        rule: { kind: 'regex' },
        message: '禁止正则字面量；结构化解析优先，外部单行格式校验经 @format-exempt 登记。',
      },
      {
        id: 'TK036',
        rule: {
          any: [{ pattern: 'RegExp($$$ARGS)' }, { pattern: 'new RegExp($$$ARGS)' }],
        },
        message: '禁止构造正则匹配器；结构化解析优先，确需正则经 @format-exempt 登记。',
      },
    ]
    const root: SgNode = parse(
      relativePath.endsWith('.tsx') ? Lang.Tsx : Lang.TypeScript,
      source,
    ).root()
    const violations: string[] = []
    for (const rule of rules) {
      for (const node of root.findAll({ rule: rule.rule })) {
        violations.push(
          `${relativePath}:${String(node.range().start.line + 1)}: ${rule.id} ${rule.message}`,
        )
      }
    }
    const sourceFile: ts.SourceFile = ts.createSourceFile(
      relativePath,
      source,
      ts.ScriptTarget.Latest,
      true,
    )
    const formatExemptRanges: readonly ReviewRange[] = StructuralRules.exemptRanges(
      sourceFile,
      'format-exempt',
    )
    const passthroughExemptRanges: readonly ReviewRange[] = StructuralRules.exemptRanges(
      sourceFile,
      'passthrough-exempt',
    )
    for (const rule of exemptableRules) {
      for (const node of root.findAll({ rule: rule.rule })) {
        if (
          formatExemptRanges.some(
            (range: ReviewRange): boolean =>
              node.range().start.index >= range.start && node.range().start.index < range.end,
          )
        ) {
          continue
        }
        violations.push(
          `${relativePath}:${String(node.range().start.line + 1)}: ${rule.id} ${rule.message}`,
        )
      }
    }
    violations.push(
      ...StructuralRules.semanticViolations(
        sourceFile,
        relativePath,
        formatExemptRanges,
        passthroughExemptRanges,
      ),
    )
    return violations
  }

  /** @anchor test-antipattern-gate */
  public static testQuality(source: string, relativePath: string): string[] {
    const sourceFile: ts.SourceFile = ts.createSourceFile(
      relativePath,
      source,
      ts.ScriptTarget.Latest,
      true,
    )
    const violations: string[] = []
    const sourceReaders: Set<string> = new Set()
    const sourceVariables: Set<string> = new Set()
    for (const statement of sourceFile.statements) {
      if (
        ts.isImportDeclaration(statement) &&
        ts.isStringLiteral(statement.moduleSpecifier) &&
        ['node:fs', 'node:fs/promises'].includes(statement.moduleSpecifier.text) &&
        statement.importClause?.namedBindings !== undefined &&
        ts.isNamedImports(statement.importClause.namedBindings)
      ) {
        for (const binding of statement.importClause.namedBindings.elements) {
          if (
            ['readFile', 'readFileSync'].includes(binding.propertyName?.text ?? binding.name.text)
          )
            sourceReaders.add(binding.name.text)
        }
      }
    }
    const hasTypeScriptPath: (node: ts.Node) => boolean = (node: ts.Node): boolean => {
      if (ts.isStringLiteralLike(node))
        return node.text.endsWith('.ts') || node.text.endsWith('.tsx')
      return node.getChildren(sourceFile).some(hasTypeScriptPath)
    }
    const readsSource: (node: ts.Node) => boolean = (node: ts.Node): boolean => {
      if (ts.isIdentifier(node)) return sourceVariables.has(node.text)
      if (ts.isAwaitExpression(node) || ts.isParenthesizedExpression(node))
        return readsSource(node.expression)
      if (ts.isPropertyAccessExpression(node)) return readsSource(node.expression)
      if (ts.isCallExpression(node)) {
        if (ts.isIdentifier(node.expression) && sourceReaders.has(node.expression.text))
          return node.arguments.some(hasTypeScriptPath)
        if (ts.isPropertyAccessExpression(node.expression))
          return readsSource(node.expression.expression)
      }
      return false
    }
    const inspectsSource: (node: ts.Node) => boolean = (node: ts.Node): boolean => {
      if (
        ts.isCallExpression(node) &&
        ts.isPropertyAccessExpression(node.expression) &&
        ['includes', 'indexOf', 'search', 'match', 'matchAll', 'startsWith', 'endsWith'].includes(
          node.expression.name.text,
        ) &&
        readsSource(node.expression.expression)
      )
        return true
      return node.getChildren(sourceFile).some(inspectsSource)
    }
    const visit: (node: ts.Node) => void = (node: ts.Node): void => {
      if (
        ts.isVariableDeclaration(node) &&
        ts.isIdentifier(node.name) &&
        node.initializer !== undefined &&
        readsSource(node.initializer)
      )
        sourceVariables.add(node.name.text)
      if (ts.isCallExpression(node) && StructuralRules.isAssertion(node.expression)) {
        const first: Option.Option<ts.Expression> = Option.fromNullishOr(node.arguments[0])
        const second: Option.Option<ts.Expression> = Option.fromNullishOr(node.arguments[1])
        if (Option.isSome(first) && inspectsSource(first.value)) {
          violations.push(
            `${relativePath}:${String(sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile, true)).line + 1)}: TKTEST001 源码文本断言须改为执行行为或解析后的结构断言。`,
          )
        }
        if (
          Option.isSome(first) &&
          Option.isSome(second) &&
          first.value.getText(sourceFile) === second.value.getText(sourceFile)
        )
          violations.push(
            `${relativePath}:${String(sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile, true)).line + 1)}: TKTEST002 实际值和预期值是同一表达式；预期须独立于被测实现。`,
          )
      }
      if (
        ts.isCallExpression(node) &&
        ts.isPropertyAccessExpression(node.expression) &&
        [
          'toBe',
          'toEqual',
          'toStrictEqual',
          'toBeTruthy',
          'toBeFalsy',
          'toContain',
          'toMatch',
        ].includes(node.expression.name.text)
      ) {
        let receiver: ts.Expression = node.expression.expression
        while (ts.isPropertyAccessExpression(receiver)) receiver = receiver.expression
        if (
          ts.isCallExpression(receiver) &&
          ts.isIdentifier(receiver.expression) &&
          receiver.expression.text === 'expect'
        ) {
          const actual: Option.Option<ts.Expression> = Option.fromNullishOr(receiver.arguments[0])
          const expected: Option.Option<ts.Expression> = Option.fromNullishOr(node.arguments[0])
          if (
            Option.isSome(actual) &&
            (inspectsSource(actual.value) ||
              (['toContain', 'toMatch'].includes(node.expression.name.text) &&
                readsSource(actual.value)))
          )
            violations.push(
              `${relativePath}:${String(sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile, true)).line + 1)}: TKTEST001 源码文本断言须改为执行行为或解析后的结构断言。`,
            )
          if (
            Option.isSome(actual) &&
            Option.isSome(expected) &&
            actual.value.getText(sourceFile) === expected.value.getText(sourceFile)
          )
            violations.push(
              `${relativePath}:${String(sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile, true)).line + 1)}: TKTEST002 实际值和预期值是同一表达式；预期须独立于被测实现。`,
            )
        }
      }
      ts.forEachChild(node, visit)
    }
    visit(sourceFile)
    return violations
  }

  /** @anchor test-antipattern-gate */
  private static isAssertion(node: ts.Expression): boolean {
    return (
      ts.isPropertyAccessExpression(node) &&
      ts.isIdentifier(node.expression) &&
      node.expression.text === 'assert' &&
      ['ok', 'equal', 'strictEqual', 'deepEqual', 'deepStrictEqual'].includes(node.name.text)
    )
  }

  /**
   * @anchor forward-shell-usage
   * @anchor kv-type-contract
   */
  private static semanticViolations(
    sourceFile: ts.SourceFile,
    relativePath: string,
    formatExemptRanges: readonly ReviewRange[],
    passthroughExemptRanges: readonly ReviewRange[],
  ): string[] {
    const containerNames: ReadonlySet<string> = new Set<string>([
      'Record',
      'Map',
      'ReadonlyMap',
      'WeakMap',
      'ReadonlyWeakMap',
    ])
    const contractMembers: Set<string> = new Set<string>()
    const methods: ts.MethodDeclaration[] = []
    const exempted: (node: ts.Node) => boolean = (node: ts.Node): boolean => {
      const start: number = node.getStart(sourceFile, true)
      return formatExemptRanges.some(
        (range: ReviewRange): boolean => start >= range.start && start < range.end,
      )
    }
    const report: (node: ts.Node, id: string, message: string) => void = (
      node: ts.Node,
      id: string,
      message: string,
    ): void => {
      violations.push(
        `${relativePath}:${String(sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile, true)).line + 1)}: ${id} ${message}`,
      )
    }
    const violations: string[] = []
    const collect: (node: ts.Node) => void = (node: ts.Node): void => {
      if (ts.isInterfaceDeclaration(node) || ts.isTypeLiteralNode(node)) {
        for (const member of node.members) {
          if (member.name !== undefined && ts.isIdentifier(member.name)) {
            contractMembers.add(member.name.text)
          }
        }
      }
      if (ts.isClassDeclaration(node)) {
        const barrier: boolean = node.members.some((member: ts.ClassElement): boolean => {
          if (!ts.isConstructorDeclaration(member) && !ts.isPropertyDeclaration(member)) {
            return false
          }
          const privateMember: boolean =
            ts
              .getModifiers(member)
              ?.some(
                (modifier: ts.Modifier): boolean => modifier.kind === ts.SyntaxKind.PrivateKeyword,
              ) === true
          if (ts.isConstructorDeclaration(member)) {
            return privateMember
          }
          const staticMember: boolean =
            ts
              .getModifiers(member)
              ?.some(
                (modifier: ts.Modifier): boolean => modifier.kind === ts.SyntaxKind.StaticKeyword,
              ) === true
          return privateMember && !staticMember
        })
        if (!barrier) {
          report(
            node,
            'TK027',
            '类必须以私有构造器或私有实例成员维持名义屏障；同构类禁止结构兼容。',
          )
        }
      }
      if (ts.isMethodDeclaration(node)) {
        methods.push(node)
      }
      if (
        ts.isTypeReferenceNode(node) &&
        ts.isIdentifier(node.typeName) &&
        containerNames.has(node.typeName.text) &&
        node.typeArguments !== undefined &&
        !exempted(node)
      ) {
        for (const argument of node.typeArguments) {
          if (
            StructuralRules.isOpaque(argument) ||
            (ts.isTypeReferenceNode(argument) &&
              ts.isIdentifier(argument.typeName) &&
              containerNames.has(argument.typeName.text))
          ) {
            report(
              node,
              'TK032',
              'KV 容器禁止携带混沌类型参数或嵌套裸 KV；真字典用原始或具名键值，收窄入口使用 @format-exempt 豁免标签。',
            )
            break
          }
        }
      }
      if (
        ts.isIndexSignatureDeclaration(node) &&
        StructuralRules.isOpaque(node.type) &&
        !exempted(node)
      ) {
        report(node, 'TK032', '索引签名禁止混沌值类型；外部输入收窄入口经 @format-exempt 登记。')
      }
      if (
        ts.isTypeAliasDeclaration(node) &&
        !StructuralRules.isLiteralUnion(node.type) &&
        !exempted(node)
      ) {
        report(node, 'TK033', 'type 别名只允许同类型基本字面量联合，承载枚举域具名。')
      }
      if (
        ts.isUnionTypeNode(node) &&
        StructuralRules.holdsAnnotation(node.parent, node) &&
        node.types.some((member: ts.TypeNode): boolean => StructuralRules.isBasicLiteral(member)) &&
        !exempted(node)
      ) {
        report(node, 'TK034', '注解禁止内联字面量联合；必须具名 type 别名后引用。')
      }
      ts.forEachChild(node, collect)
    }
    collect(sourceFile)
    for (const method of methods) {
      if (!ts.isIdentifier(method.name)) {
        continue
      }
      const name: string = method.name.text
      const declaredModifiers = method.modifiers
      const override: boolean =
        (declaredModifiers !== undefined &&
          declaredModifiers.some(
            (modifier: ts.ModifierLike): boolean => modifier.kind === ts.SyntaxKind.OverrideKeyword,
          )) ||
        contractMembers.has(name)
      if (override) {
        continue
      }
      const body: Option.Option<readonly ts.Statement[]> = Option.map(
        Option.fromNullishOr(method.body),
        (block: ts.Block): readonly ts.Statement[] => block.statements,
      )
      const single: Option.Option<ts.Statement> = Option.flatMap(
        body,
        (statements: readonly ts.Statement[]): Option.Option<ts.Statement> =>
          statements.length === 1 ? Option.fromNullishOr(statements[0]) : Option.none(),
      )
      const forwarded: boolean = Option.exists(
        Option.flatMap(single, (statement: ts.Statement): Option.Option<ts.Expression> =>
          ts.isReturnStatement(statement)
            ? Option.fromNullishOr(statement.expression)
            : ts.isExpressionStatement(statement)
              ? Option.some(statement.expression)
              : Option.none(),
        ),
        (expression: ts.Expression): boolean =>
          StructuralRules.isPassthroughForward(expression, method) &&
          !passthroughExemptRanges.some((range: ReviewRange): boolean => {
            const start: number = method.getStart(sourceFile, true)
            return start >= range.start && start < range.end
          }),
      )
      if (forwarded) {
        report(
          method,
          'TK030',
          '纯转发壳必须消除：零加工转发没有承载行为；确需保留时使用方法级 @passthrough-exempt 理由标签。',
        )
      }
    }
    return violations
  }

  private static isPassthroughForward(
    expression: ts.Expression,
    method: ts.MethodDeclaration,
  ): boolean {
    let call: ts.Expression = expression
    while (ts.isAwaitExpression(call) || ts.isParenthesizedExpression(call)) {
      call = call.expression
    }
    if (
      !ts.isCallExpression(call) ||
      !ts.isPropertyAccessExpression(call.expression) ||
      !StructuralRules.isPlainReceiver(call.expression.expression)
    ) {
      return false
    }
    const parameterNames: ReadonlySet<string> = new Set<string>(
      method.parameters
        .map((parameter: ts.ParameterDeclaration): string =>
          ts.isIdentifier(parameter.name) ? parameter.name.text : '',
        )
        .filter((parameterName: string): boolean => parameterName !== ''),
    )
    return (
      call.arguments.length === method.parameters.length &&
      call.arguments.every(
        (argument: ts.Expression): boolean =>
          ts.isIdentifier(argument) && parameterNames.has(argument.text),
      )
    )
  }

  private static isPlainReceiver(receiver: ts.Expression): boolean {
    if (ts.isParenthesizedExpression(receiver) || ts.isPropertyAccessExpression(receiver)) {
      return StructuralRules.isPlainReceiver(receiver.expression)
    }
    return ts.isIdentifier(receiver) || receiver.kind === ts.SyntaxKind.ThisKeyword
  }

  private static holdsAnnotation(holder: ts.Node, typeNode: ts.TypeNode): boolean {
    if (
      !ts.isVariableDeclaration(holder) &&
      !ts.isParameter(holder) &&
      !ts.isPropertySignature(holder) &&
      !ts.isPropertyDeclaration(holder) &&
      !ts.isFunctionDeclaration(holder) &&
      !ts.isMethodDeclaration(holder) &&
      !ts.isGetAccessorDeclaration(holder) &&
      !ts.isSetAccessorDeclaration(holder)
    ) {
      return false
    }
    return holder.type === typeNode
  }

  private static isBasicLiteral(member: ts.TypeNode): boolean {
    if (!ts.isLiteralTypeNode(member)) {
      return false
    }
    const literal: ts.Expression = member.literal
    return (
      literal.kind === ts.SyntaxKind.StringLiteral ||
      literal.kind === ts.SyntaxKind.NumericLiteral ||
      literal.kind === ts.SyntaxKind.TrueKeyword ||
      literal.kind === ts.SyntaxKind.FalseKeyword
    )
  }

  private static isLiteralUnion(node: ts.TypeNode): boolean {
    if (!ts.isUnionTypeNode(node)) {
      return false
    }
    const families: Set<string> = new Set<string>()
    for (const member of node.types) {
      if (!ts.isLiteralTypeNode(member) || !StructuralRules.isBasicLiteral(member)) {
        return false
      }
      const literal: ts.Expression = member.literal
      if (literal.kind === ts.SyntaxKind.StringLiteral) {
        families.add('string')
      } else if (literal.kind === ts.SyntaxKind.NumericLiteral) {
        families.add('number')
      } else {
        families.add('boolean')
      }
    }
    return families.size <= 1
  }

  private static isOpaque(node: ts.TypeNode): boolean {
    if (
      node.kind === ts.SyntaxKind.AnyKeyword ||
      node.kind === ts.SyntaxKind.UnknownKeyword ||
      node.kind === ts.SyntaxKind.ObjectKeyword
    ) {
      return true
    }
    if (ts.isUnionTypeNode(node)) {
      return node.types.some((member: ts.TypeNode): boolean => StructuralRules.isOpaque(member))
    }
    return false
  }

  /** @anchor format-exemption */
  private static exemptRanges(
    sourceFile: ts.SourceFile,
    tagName: ExemptionTag,
  ): readonly ReviewRange[] {
    const ranges: ReviewRange[] = []
    const visit: (node: ts.Node) => void = (node: ts.Node): void => {
      if (
        TypeScriptSource.isDocHost(node) &&
        ts.getJSDocTags(node).some((tag: ts.JSDocTag): boolean => tag.tagName.text === tagName)
      ) {
        ranges.push({ start: node.getStart(sourceFile, true), end: node.end })
      }
      ts.forEachChild(node, visit)
    }
    visit(sourceFile)
    return ranges
  }
}
