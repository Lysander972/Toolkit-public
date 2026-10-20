import { Option } from 'effect'
import ts from 'typescript'
import { z } from 'zod'
import {
  BescriptVocabulary,
  type ScriptParameter,
  type VarTypeName,
} from '../vocabulary/Vocabulary.ts'
import type { LocalAst, LocalAstNode, CallSignature, StatementKind } from '../local_ast/LocalAst.ts'
import { ConversionBasis, type PlatformMethod } from './ConversionBasis.ts'
import { VariableMapping } from './VariableMapping.ts'
import { CallMapping, type CallOrigin, type ArgumentOrigin } from './CallMapping.ts'
import { ConstantMapping, type InlineCanvasCodec } from './ConstantMapping.ts'
import { SourceComments } from './SourceComments.ts'
import { TsSyntax } from './TsSyntax.ts'

type StatementMode =
  | 'enabled'
  | 'disabled'
  | 'open'
  | 'close'
  | 'comment'
  | 'disabled-comment'
  | 'skip'
  | 'disabled-skip'
type JumpKind = 'break' | 'continue'

/** @anchor bescript-ts-statements */
interface StatementSource {
  readonly sourceRef: Option.Option<number>
  readonly line: number
  readonly mode: StatementMode
  readonly originalKind: StatementKind
  readonly resultType: Option.Option<VarTypeName>
  readonly call: Option.Option<CallOrigin>
  readonly arguments: readonly ArgumentOrigin[]
}

/** @anchor bescript-ts-statements */
interface ReadStatements {
  readonly nodes: readonly LocalAstNode[]
  readonly next: number
}

/** @anchor bescript-ts-statements */
interface ExpressionMapping {
  readonly call: CallSignature
  readonly kind: StatementKind
}

/** @anchor bescript-ts-statements */
export class StatementMapping {
  private static readonly source: z.ZodType<StatementSource> = z.strictObject({
    sourceRef: BescriptVocabulary.option(z.number().int().min(0)),
    line: z.number().int().min(1),
    mode: z.enum([
      'enabled',
      'disabled',
      'open',
      'close',
      'comment',
      'disabled-comment',
      'skip',
      'disabled-skip',
    ]),
    originalKind: z.enum([
      'if',
      'while',
      'delay',
      'async',
      'comment',
      'skip',
      'break',
      'continue',
      'call',
    ]),
    call: BescriptVocabulary.option(CallMapping.source),
    resultType: BescriptVocabulary.option(BescriptVocabulary.varType),
    arguments: z.array(ConstantMapping.source),
  })
  private readonly calls: CallMapping
  private readonly used: Set<number> = new Set()
  /** @anchor bescript-ts-statements */
  public constructor(
    private readonly basis: ConversionBasis,
    private readonly variables: VariableMapping,
    private readonly sourceFile: ts.SourceFile,
    private readonly returned: Option.Option<ScriptParameter>,
    canvas: InlineCanvasCodec,
  ) {
    this.calls = new CallMapping(basis, variables, sourceFile, canvas)
  }

  /**
   * @anchor bescript-ts-statements
   * @fallback 没有返回绑定时生成无返回值的 terminal return。
   */
  public toTs(ast: LocalAst): ts.Block {
    this.variables.collect(ast)
    const statements: ts.Statement[] = []
    if (Option.isSome(this.returned)) {
      const declaration: Option.Option<ts.VariableStatement> = this.variables.declaration(
        this.returned.value.name,
      )
      if (Option.isSome(declaration))
        statements.push(SourceComments.attach(declaration.value, 'return-binding', true))
    }
    statements.push(...this.emitNodes(ast.nodes, 0))
    statements.push(
      SourceComments.attach(
        ts.factory.createReturnStatement(
          Option.isSome(this.returned)
            ? this.variables.expression(this.returned.value.name)
            : undefined,
        ),
        'terminal-return',
        true,
      ),
    )
    return ts.factory.createBlock(statements, true)
  }

  /** @anchor bescript-ts-statements */
  public fromTs(block: ts.Block): readonly LocalAstNode[] {
    return this.readNodes(block.statements, 0, 0, 0, Option.none()).nodes
  }

  /** @anchor bescript-ts-statements */
  public static disabledSyntax(
    statement: ts.EmptyStatement,
    sourceFile: ts.SourceFile,
  ): Option.Option<ts.Statement> {
    const source: Option.Option<StatementSource> = SourceComments.read(
      statement,
      sourceFile,
      'statement',
      StatementMapping.source,
    )
    if (Option.isNone(source) || (source.value.mode !== 'open' && source.value.mode !== 'disabled'))
      return Option.none()
    return Option.some(
      StatementMapping.disabled(statement, sourceFile, source.value.mode === 'open'),
    )
  }

  private emitNodes(nodes: readonly LocalAstNode[], loopDepth: number): readonly ts.Statement[] {
    const statements: ts.Statement[] = []
    for (const node of nodes) {
      let mode: StatementMode = node.disabled ? 'disabled' : 'enabled'
      if (node.kind === 'comment' || node.kind === 'skip') {
        mode =
          node.kind === 'comment'
            ? node.disabled
              ? 'disabled-comment'
              : 'comment'
            : node.disabled
              ? 'disabled-skip'
              : 'skip'
        const comment: ts.EmptyStatement = ts.addSyntheticLeadingComment(
          ts.factory.createEmptyStatement(),
          ts.SyntaxKind.SingleLineCommentTrivia,
          node.kind === 'comment' && node.text.startsWith('//') ? node.text.slice(2) : node.text,
          true,
        )
        statements.push(this.mark(comment, node, mode))
        continue
      }
      const call: CallSignature = Option.getOrThrow(node.call)
      if (Option.isSome(call.returnVariable)) {
        const declaration: Option.Option<ts.VariableStatement> = this.variables.declaration(
          call.returnVariable.value.name,
        )
        if (Option.isSome(declaration))
          statements.push(SourceComments.attach(declaration.value, 'binding', true))
      }
      if (node.disabled && (node.kind === 'if' || node.kind === 'while')) {
        const opening: ts.Statement = this.emit(node, loopDepth, [])
        statements.push(this.mark(SourceComments.disabled(opening, true), node, 'open'))
        statements.push(...this.emitNodes(node.children, loopDepth))
        statements.push(
          this.mark(
            ts.addSyntheticLeadingComment(
              ts.factory.createEmptyStatement(),
              ts.SyntaxKind.MultiLineCommentTrivia,
              ' } ',
              true,
            ),
            node,
            'close',
          ),
        )
      } else {
        const children: readonly ts.Statement[] = this.emitNodes(
          node.children,
          loopDepth + (node.kind === 'while' ? 1 : 0),
        )
        const statement: ts.Statement = this.emit(node, loopDepth, children)
        statements.push(
          this.mark(
            node.disabled ? SourceComments.disabled(statement, false) : statement,
            node,
            mode,
          ),
        )
      }
    }
    return statements
  }

  /**
   * @anchor bescript-ts-statements
   * @fallback break 位于循环外时映射为方法终止返回。
   * @fallback 没有返回绑定时终止返回不携带表达式。
   */
  private emit(
    node: LocalAstNode,
    loopDepth: number,
    children: readonly ts.Statement[],
  ): ts.Statement {
    if (node.kind === 'break')
      return loopDepth > 0
        ? ts.factory.createBreakStatement()
        : ts.factory.createReturnStatement(
            Option.isSome(this.returned)
              ? this.variables.expression(this.returned.value.name)
              : undefined,
          )
    if (node.kind === 'continue') {
      if (loopDepth === 0 && !node.disabled) throw new Error('continue 必须在启用的循环内')
      return ts.factory.createContinueStatement()
    }
    const call: CallSignature = Option.getOrThrow(node.call)
    const method: PlatformMethod = this.calls.signature(call)
    this.checkCondition(node.kind, method.returnType, call.discardsReturn)
    let expression: ts.Expression = this.calls.toTs(call)
    const waits: boolean =
      node.kind === 'async' ||
      (method.asynchronous &&
        (Option.isSome(method.returnType) ||
          node.kind === 'if' ||
          node.kind === 'while' ||
          node.kind === 'delay'))
    if (waits) expression = ts.factory.createAwaitExpression(expression)
    if (node.kind === 'delay')
      expression = ts.factory.createAwaitExpression(
        ts.factory.createCallExpression(ts.factory.createIdentifier('delay'), undefined, [
          expression,
        ]),
      )
    if (Option.isSome(call.returnVariable)) {
      this.basis.assignable(
        Option.getOrThrow(method.returnType),
        call.returnVariable.value.typeName,
      )
      this.basis.assignable(
        call.returnVariable.value.typeName,
        this.variables.type(call.returnVariable.value.name),
      )
      expression = ts.factory.createBinaryExpression(
        this.variables.expression(call.returnVariable.value.name),
        ts.SyntaxKind.EqualsToken,
        expression,
      )
    } else if (call.discardsReturn) expression = ts.factory.createVoidExpression(expression)
    if (node.kind === 'if')
      return ts.factory.createIfStatement(expression, ts.factory.createBlock(children, true))
    if (node.kind === 'while')
      return ts.factory.createWhileStatement(expression, ts.factory.createBlock(children, true))
    return ts.factory.createExpressionStatement(expression)
  }

  private mark<T extends ts.Statement>(statement: T, node: LocalAstNode, mode: StatementMode): T {
    const carriesSyntax: boolean = mode === 'disabled' || mode === 'open'
    return SourceComments.attach(statement, 'statement', {
      sourceRef: node.sourceRef,
      line: node.line,
      mode,
      originalKind: node.kind,
      resultType: Option.flatMap(node.call, (call: CallSignature): Option.Option<VarTypeName> =>
        Option.map(
          call.returnVariable,
          (parameter: ScriptParameter): VarTypeName => parameter.typeName,
        ),
      ),
      call: carriesSyntax
        ? Option.map(node.call, (call: CallSignature): CallOrigin => ({
            kind: call.kind,
            ownerType: call.ownerType,
            ownerNameIsAlias: call.ownerNameIsAlias,
            saveName: call.saveName,
          }))
        : Option.none(),
      arguments:
        carriesSyntax && Option.isSome(node.call)
          ? node.call.value.arguments.map(
              (argument: import('../local_ast/LocalAst.ts').CallArgument): ArgumentOrigin => ({
                sourceRef: argument.sourceRef,
                rawText: argument.rawText,
                decodedText: argument.decodedText,
              }),
            )
          : [],
    })
  }

  private readNodes(
    statements: readonly ts.Statement[],
    start: number,
    depth: number,
    loopDepth: number,
    opening: Option.Option<StatementSource>,
  ): ReadStatements {
    const nodes: LocalAstNode[] = []
    let cursor: number = start
    while (cursor < statements.length) {
      const statement: ts.Statement = Option.getOrThrow(Option.fromNullishOr(statements[cursor]))
      cursor += 1
      const source: Option.Option<StatementSource> = SourceComments.read(
        statement,
        this.sourceFile,
        'statement',
        StatementMapping.source,
      )
      if (Option.isSome(source) && source.value.mode === 'close') {
        if (
          Option.isNone(opening) ||
          !Option.makeEquivalence<number>((left: number, right: number): boolean => left === right)(
            opening.value.sourceRef,
            source.value.sourceRef,
          ) ||
          !ts.isEmptyStatement(statement)
        )
          throw new Error('禁用控制壳不配对')
        const payload: string = SourceComments.payload(statement, this.sourceFile)
        if (payload !== '}' && payload !== ' } ') throw new Error('禁用结束壳必须是右花括号')
        return { nodes, next: cursor }
      }
      if (this.bookkeeping(statement)) continue
      this.claim(source)
      if (Option.isSome(source) && source.value.mode === 'open') {
        const disabled: ts.Statement = StatementMapping.disabled(statement, this.sourceFile, true)
        if (!ts.isIfStatement(disabled) && !ts.isWhileStatement(disabled))
          throw new Error('禁用控制壳必须是 if 或 while')
        const body: ReadStatements = this.readNodes(
          statements,
          cursor,
          depth + 1,
          loopDepth,
          source,
        )
        cursor = body.next
        nodes.push(this.readNode(disabled, source, depth, loopDepth, body.nodes, true))
      } else if (Option.isSome(source) && source.value.mode === 'disabled')
        nodes.push(
          this.readNode(
            StatementMapping.disabled(statement, this.sourceFile, false),
            source,
            depth,
            loopDepth,
            [],
            true,
          ),
        )
      else
        nodes.push(
          this.readNode(
            statement,
            source,
            depth,
            loopDepth,
            [],
            Option.isSome(source) &&
              (source.value.mode === 'disabled-comment' || source.value.mode === 'disabled-skip'),
          ),
        )
    }
    if (Option.isSome(opening)) throw new Error('禁用控制壳缺少结束标记')
    return { nodes, next: cursor }
  }

  /**
   * @anchor bescript-ts-statements
   * @fallback 没有来源标记的官方语句使用默认来源行和空来源引用。
   */
  private readNode(
    statement: ts.Statement,
    source: Option.Option<StatementSource>,
    depth: number,
    loopDepth: number,
    shellChildren: readonly LocalAstNode[],
    disabled: boolean,
  ): LocalAstNode {
    const line: number = Option.isSome(source) ? source.value.line : 1
    const sourceRef: Option.Option<number> = Option.isSome(source)
      ? source.value.sourceRef
      : Option.none()
    if (ts.isEmptyStatement(statement))
      return this.readEmptyNode(statement, source, line, depth, sourceRef, disabled)
    if (
      ts.isBreakStatement(statement) ||
      ts.isContinueStatement(statement) ||
      ts.isReturnStatement(statement)
    )
      return this.readJumpNode(statement, line, depth, loopDepth, sourceRef, disabled)
    if (ts.isIfStatement(statement) || ts.isWhileStatement(statement))
      return this.readControlNode(
        statement,
        source,
        line,
        depth,
        loopDepth,
        sourceRef,
        shellChildren,
        disabled,
      )
    if (!ts.isExpressionStatement(statement))
      throw new Error('TS 语句不属于 BES 子集: ' + ts.SyntaxKind[statement.kind])
    const decoded: ExpressionMapping = this.readExpression(statement.expression, 'call', source)
    return {
      sourceRef,
      line,
      depth,
      disabled,
      kind: decoded.kind,
      text: '',
      children: shellChildren,
      call: Option.some(decoded.call),
    }
  }

  /**
   * @anchor bescript-ts-statements
   * @format-exempt bescript-statement-category-split 空语句承载注释与禁用节点的独立来源校验。
   */
  private readEmptyNode(
    statement: ts.EmptyStatement,
    source: Option.Option<StatementSource>,
    line: number,
    depth: number,
    sourceRef: Option.Option<number>,
    disabled: boolean,
  ): LocalAstNode {
    if (
      Option.isNone(source) ||
      !['comment', 'skip', 'disabled-comment', 'disabled-skip'].includes(source.value.mode)
    )
      throw new Error('无语义空语句不能转换为 BEScript')
    const comment: boolean =
      source.value.mode === 'comment' || source.value.mode === 'disabled-comment'
    return {
      sourceRef,
      line,
      depth,
      disabled,
      kind: comment ? 'comment' : 'skip',
      text: (comment ? '//' : '') + SourceComments.payload(statement, this.sourceFile),
      children: [],
      call: Option.none(),
    }
  }

  private readJumpNode(
    statement: ts.BreakStatement | ts.ContinueStatement | ts.ReturnStatement,
    line: number,
    depth: number,
    loopDepth: number,
    sourceRef: Option.Option<number>,
    disabled: boolean,
  ): LocalAstNode {
    if (
      (ts.isBreakStatement(statement) || ts.isContinueStatement(statement)) &&
      Option.isSome(Option.fromNullishOr(statement.label))
    )
      throw new Error('BES 不支持标签跳转')
    if (ts.isReturnStatement(statement)) {
      if (loopDepth > 0) throw new Error('循环内 return 无法映射为 BEScript break')
      this.checkReturn(statement)
    } else if (ts.isBreakStatement(statement) && loopDepth === 0 && !disabled)
      throw new Error('TS break 必须位于启用循环内')
    const kind: JumpKind = ts.isContinueStatement(statement) ? 'continue' : 'break'
    if (kind === 'continue' && loopDepth === 0 && !disabled)
      throw new Error('continue 必须在启用循环内')
    return {
      sourceRef,
      line,
      depth,
      disabled,
      kind,
      text: '',
      children: [],
      call: Option.some(CallMapping.empty('fun', kind, Option.none(), Option.none(), false)),
    }
  }

  private readControlNode(
    statement: ts.IfStatement | ts.WhileStatement,
    source: Option.Option<StatementSource>,
    line: number,
    depth: number,
    loopDepth: number,
    sourceRef: Option.Option<number>,
    shellChildren: readonly LocalAstNode[],
    disabled: boolean,
  ): LocalAstNode {
    const kind: StatementKind = ts.isIfStatement(statement) ? 'if' : 'while'
    if (ts.isIfStatement(statement) && Option.isSome(Option.fromNullishOr(statement.elseStatement)))
      throw new Error('BES 不支持 else')
    const body: ts.Statement = ts.isIfStatement(statement)
      ? statement.thenStatement
      : statement.statement
    if (!ts.isBlock(body)) throw new Error('控制语句必须使用块')
    let children: readonly LocalAstNode[] = shellChildren
    if (Option.isSome(source) && source.value.mode === 'open') {
      if (body.statements.length !== 0) throw new Error('禁用开壳不能内藏子语句')
    } else
      children = this.readNodes(
        body.statements,
        0,
        depth + 1,
        loopDepth + (kind === 'while' && !disabled ? 1 : 0),
        Option.none(),
      ).nodes
    const decoded: ExpressionMapping = this.readExpression(statement.expression, kind, source)
    return {
      sourceRef,
      line,
      depth,
      disabled,
      kind: decoded.kind,
      text: '',
      children,
      call: Option.some(decoded.call),
    }
  }

  /**
   * @anchor bescript-ts-statements
   * @fallback 没有来源语句参数元数据时使用空参数来源集合。
   * @fallback 新增赋值没有结果类型来源时采用显式平台返回类型，变量声明类型仍独立校验。
   */
  private readExpression(
    expression: ts.Expression,
    kind: StatementKind,
    source: Option.Option<StatementSource>,
  ): ExpressionMapping {
    let current: ts.Expression = TsSyntax.unwrap(expression)
    let returnVariable: Option.Option<ScriptParameter> = Option.none()
    let discardsReturn: boolean = false
    if (
      ts.isBinaryExpression(current) &&
      current.operatorToken.kind === ts.SyntaxKind.EqualsToken
    ) {
      returnVariable = Option.some(this.variables.fromExpression(current.left))
      current = TsSyntax.unwrap(current.right)
    } else if (ts.isVoidExpression(current)) {
      discardsReturn = true
      current = TsSyntax.unwrap(current.expression)
    }
    let awaited: boolean = false
    if (ts.isAwaitExpression(current)) {
      awaited = true
      current = TsSyntax.unwrap(current.expression)
    }
    let mappedKind: StatementKind = kind
    let callAwaited: boolean = awaited
    if (
      ts.isCallExpression(current) &&
      ts.isIdentifier(current.expression) &&
      current.expression.text === 'delay'
    ) {
      if (!awaited || current.arguments.length !== 1)
        throw new Error('delay 必须等待且携带唯一调用')
      mappedKind = 'delay'
      current = TsSyntax.unwrap(Option.getOrThrow(Option.fromNullishOr(current.arguments[0])))
      callAwaited = ts.isAwaitExpression(current)
      if (ts.isAwaitExpression(current)) current = TsSyntax.unwrap(current.expression)
    }
    const call: CallSignature = this.calls.fromTs(
      current,
      Option.flatMap(source, (origin: StatementSource): Option.Option<CallOrigin> => origin.call),
      Option.isSome(source) ? source.value.arguments : [],
    )
    const signature: PlatformMethod = this.calls.signature(call)
    if (
      signature.asynchronous &&
      (Option.isSome(signature.returnType) ||
        kind === 'if' ||
        kind === 'while' ||
        mappedKind === 'delay') &&
      !callAwaited
    )
      throw new Error('异步结果与控制调用必须等待')
    if (Option.isSome(returnVariable)) {
      const resultType: VarTypeName = Option.getOrElse(
        Option.flatMap(
          source,
          (origin: StatementSource): Option.Option<VarTypeName> => origin.resultType,
        ),
        (): VarTypeName => Option.getOrThrow(signature.returnType),
      )
      this.basis.assignable(Option.getOrThrow(signature.returnType), resultType)
      this.basis.assignable(resultType, returnVariable.value.typeName)
      returnVariable = Option.some({ ...returnVariable.value, typeName: resultType })
    }
    if (kind === 'call' && mappedKind !== 'delay') {
      if (awaited && !(signature.asynchronous && Option.isSome(signature.returnType)))
        mappedKind = 'async'
      if (Option.isSome(source) && source.value.originalKind === 'async' && awaited)
        mappedKind = 'async'
    }
    this.checkCondition(kind, signature.returnType, discardsReturn || mappedKind === 'delay')
    return { call: { ...call, returnVariable, discardsReturn }, kind: mappedKind }
  }

  private checkCondition(
    kind: StatementKind,
    returnType: Option.Option<VarTypeName>,
    discardsResult: boolean,
  ): void {
    if (kind !== 'if' && kind !== 'while') return
    if (discardsResult || Option.isNone(returnType))
      throw new Error('控制调用必须产生 Bool 结果且不能丢弃')
    this.basis.assignable(returnType.value, 'Bool')
  }

  private bookkeeping(statement: ts.Statement): boolean {
    const terminal: Option.Option<boolean> = SourceComments.read(
      statement,
      this.sourceFile,
      'terminal-return',
      z.literal(true),
    )
    if (Option.isSome(terminal)) {
      if (!ts.isReturnStatement(statement)) throw new Error('终止返回标记必须附在 return 上')
      this.checkReturn(statement)
      return true
    }
    if (!ts.isVariableStatement(statement)) return false
    if (
      (statement.declarationList.flags & ts.NodeFlags.BlockScoped) !== 0 ||
      statement.declarationList.declarations.length !== 1
    )
      throw new Error('方法级绑定必须是单个 var 声明')
    const declaration: ts.VariableDeclaration = Option.getOrThrow(
      Option.fromNullishOr(statement.declarationList.declarations[0]),
    )
    if (!ts.isIdentifier(declaration.name) || Option.isNone(Option.fromNullishOr(declaration.type)))
      throw new Error('局部声明必须指定平台类型')
    const parameter: ScriptParameter = {
      name: this.basis.rawIdentifier(declaration.name.text),
      typeName: this.basis.fromType(Option.getOrThrow(Option.fromNullishOr(declaration.type))),
    }
    this.variables.declare(parameter)
    if (Option.isSome(Option.fromNullishOr(declaration.initializer)))
      throw new Error('赋值请使用独立的调用语句')
    return true
  }

  private static disabled(
    statement: ts.Statement,
    sourceFile: ts.SourceFile,
    opening: boolean,
  ): ts.Statement {
    if (!ts.isEmptyStatement(statement)) throw new Error('禁用语法必须放在注释壳内')
    const text: string = SourceComments.payload(statement, sourceFile)
    const parsed: ts.SourceFile = TsSyntax.parse(text + (opening ? '}' : ''))
    if (parsed.statements.length !== 1) throw new Error('禁用语法必须是唯一官方 TS 语句')
    return Option.getOrThrow(Option.fromNullishOr(parsed.statements[0]))
  }

  private checkReturn(statement: ts.ReturnStatement): void {
    const expression: Option.Option<ts.Expression> = Option.fromNullishOr(statement.expression)
    if (Option.isSome(this.returned) !== Option.isSome(expression))
      throw new Error('返回表达式与脚本头不一致')
    if (
      Option.isSome(expression) &&
      Option.isSome(this.returned) &&
      this.variables.fromExpression(expression.value).name !== this.returned.value.name
    )
      throw new Error('返回变量与脚本头不一致')
  }

  private claim(source: Option.Option<StatementSource>): void {
    if (Option.isNone(source) || Option.isNone(source.value.sourceRef)) return
    const id: number = source.value.sourceRef.value
    if (this.used.has(id)) throw new Error('同一语句来源不能重复使用')
    this.used.add(id)
  }
}
