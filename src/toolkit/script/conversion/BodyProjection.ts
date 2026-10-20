import { Option } from 'effect'
import type { CstNode, IToken } from 'chevrotain'
import type { LocalAstNode, StatementKind } from '../local_ast/LocalAst.ts'
import type { NodeSource, SourceField } from '../local_ast/SourceLayout.ts'
import { SyntaxTree } from './SyntaxTree.ts'
import { CallProjection, type ProjectedCall } from './CallProjection.ts'
import { CallText } from './CallText.ts'

/** @anchor bescript-body-projection */
export interface ProjectedBody {
  readonly nodes: readonly LocalAstNode[]
  readonly tail: string
}

/** @anchor bescript-body-projection */
export class BodyProjection {
  private readonly sources: NodeSource[] = []
  private nextId: number = 0

  /** @anchor bescript-body-projection */
  public constructor(private readonly text: string) {}

  /** @anchor bescript-body-projection */
  public layouts(): readonly NodeSource[] {
    return this.sources
  }

  /** @anchor bescript-body-projection */
  public body(tree: CstNode, depth: number, start: number, end: number): ProjectedBody {
    const nodes: LocalAstNode[] = []
    let cursor: number = start
    for (const line of SyntaxTree.nodes(tree, 'line')) {
      for (const statement of SyntaxTree.nodes(line, 'statement')) {
        const projected: LocalAstNode = this.statement(statement, depth, cursor)
        nodes.push(projected)
        const location: number =
          Option.getOrThrow(Option.fromNullishOr(statement.location?.endOffset)) + 1
        cursor = location
      }
    }
    return { nodes, tail: this.text.slice(cursor, end) }
  }

  /**
   * @anchor bescript-body-projection
   * @fallback comment 产生式没有调用，明确生成 None 而不制造调用。
   * @fallback 命令起点由 comment/condition/modifier/call 的文法分支确定。
   * @fallback condition 不存在时起点属于 modifier 或 call，不解释额外结构。
   * @fallback modifier 不存在时由 call token 提供必需起点。
   * @fallback 无行号的命令前缀从物理行首开始，行号缺席不补造行号。
   * @fallback 注释以 Comment token 结束，其余命令以必需的调用右括号结束。
   * @fallback 块的开头在首个换行结束，普通语句使用自己的完整区间。
   * @fallback 普通语句没有 CloseBrace，其子树区间为空。
   * @fallback 注释不带调用字段，只保存其正文作为编辑字段。
   * @fallback 注释来源不附调用种类，以空文本记录实际缺席。
   * @fallback 注释没有参数，以零记录实际数量。
   * @fallback 没有块尾的普通语句没有块尾前缀，其长度为零。
   * @fallback 没有块尾行号时其来源前缀为空，不合成行号。
   * @fallback 未禁用节点没有 Disabled token，禁用标记的新增位置是行号后的原始前缀起点。
   */
  private statement(tree: CstNode, depth: number, cursor: number): LocalAstNode {
    const comment: Option.Option<IToken> = Option.fromNullishOr(
      SyntaxTree.tokens(tree, 'Comment')[0],
    )
    const call: Option.Option<ProjectedCall> = Option.isSome(comment)
      ? Option.none()
      : Option.some(CallProjection.project(SyntaxTree.node(tree, 'call'), this.text))
    const condition: Option.Option<IToken> = Option.fromNullishOr(
      SyntaxTree.tokens(tree, 'Condition')[0],
    )
    const modifier: Option.Option<IToken> = Option.fromNullishOr(
      SyntaxTree.tokens(tree, 'Modifier')[0],
    )
    const first: IToken = Option.isSome(comment)
      ? comment.value
      : Option.isSome(condition)
        ? condition.value
        : Option.isSome(modifier)
          ? modifier.value
          : SyntaxTree.token(SyntaxTree.node(tree, 'call'), 'CallKind')
    const commandStart: number =
      Option.isSome(call) && Option.isNone(condition) && Option.isNone(modifier)
        ? call.value.start
        : first.startOffset
    const lineStart: number =
      first.startOffset - Option.getOrThrow(Option.fromNullishOr(first.startColumn)) + 1
    const prefixTree: CstNode = SyntaxTree.node(tree, 'prefix')
    const disabled: boolean = SyntaxTree.tokens(prefixTree, 'Disabled').length !== 0
    const prefix: string = this.text.slice(lineStart, commandStart)
    const number: Option.Option<IToken> = Option.fromNullishOr(
      SyntaxTree.tokens(prefixTree, 'LineNumber')[0],
    )
    const numberEnd: number = Option.isSome(number) ? SyntaxTree.end(number.value) : lineStart
    const spaces: number = this.text.slice(numberEnd, commandStart).split(' ').length - 1
    if (Math.floor(spaces / 2) !== depth)
      throw new Error(
        '正文 ' +
          String(first.startLine) +
          ':' +
          String(first.startColumn) +
          ' 缩进与控制结构不一致',
      )
    const newlines: readonly IToken[] = SyntaxTree.tokens(tree, 'Newline')
    const commandEnd: number = Option.isSome(comment)
      ? SyntaxTree.end(comment.value)
      : Option.getOrThrow(call).end
    const openingEnd: number = Option.isSome(condition)
      ? SyntaxTree.end(Option.getOrThrow(Option.fromNullishOr(newlines[0])))
      : Option.getOrThrow(Option.fromNullishOr(tree.location?.endOffset)) + 1
    const id: number = this.nextId++
    const kind: StatementKind = this.kind(comment, condition, modifier, call)
    const closing: Option.Option<IToken> = Option.fromNullishOr(
      SyntaxTree.tokens(tree, 'CloseBrace')[0],
    )
    const closeStart: number = Option.isSome(closing)
      ? closing.value.startOffset -
        Option.getOrThrow(Option.fromNullishOr(closing.value.startColumn)) +
        1
      : openingEnd
    const children: ProjectedBody = this.body(tree, depth + 1, openingEnd, closeStart)
    const node: LocalAstNode = {
      sourceRef: Option.some(id),
      kind,
      line: Option.getOrThrow(Option.fromNullishOr(first.startLine)),
      depth,
      disabled,
      text: this.text.slice(commandStart, commandEnd),
      children: children.nodes,
      call: Option.map(call, (projected: ProjectedCall): typeof projected.call => projected.call),
    }
    const fields: SourceField[] = Option.isSome(call)
      ? call.value.fields.map((field: SourceField): SourceField => ({
          ...field,
          start: field.start - commandStart,
          end: field.end - commandStart,
        }))
      : [{ key: 'comment', start: 0, end: commandEnd - commandStart, value: node.text }]
    if (Option.isSome(call))
      fields.unshift({
        key: 'wrapper',
        start: 0,
        end: call.value.start - commandStart,
        value: CallText.wrapper(node),
      })
    const closePrefix: Option.Option<CstNode> = Option.fromNullishOr(
      SyntaxTree.nodes(tree, 'closePrefix')[0],
    )
    const closeNumber: Option.Option<IToken> = Option.flatMap(
      closePrefix,
      (closingPrefix: CstNode): Option.Option<IToken> =>
        Option.fromNullishOr(SyntaxTree.tokens(closingPrefix, 'LineNumber')[0]),
    )
    this.sources.push({
      id,
      leading: this.text.slice(cursor, lineStart),
      prefix,
      numberPrefix: this.text.slice(lineStart, numberEnd),
      depth,
      disabled,
      command: node.text,
      disabledStart: Option.match(
        Option.fromNullishOr(SyntaxTree.tokens(prefixTree, 'Disabled')[0]),
        {
          onSome: (token: IToken): number => token.startOffset - lineStart,
          onNone: (): number => numberEnd - lineStart,
        },
      ),
      suffix: this.text.slice(commandEnd, openingEnd),
      fields,
      callKind: Option.isSome(call) ? call.value.call.kind : '',
      argumentCount: Option.isSome(call) ? call.value.call.arguments.length : 0,
      hasReturn: Option.isSome(call) && Option.isSome(call.value.call.returnVariable),
      close: Option.map(closing, (): string =>
        this.text.slice(
          closeStart,
          Option.getOrThrow(Option.fromNullishOr(tree.location?.endOffset)) + 1,
        ),
      ),
      closePrefixLength: Option.isSome(closing) ? closing.value.startOffset - closeStart : 0,
      closeNumberPrefix: Option.isSome(closeNumber)
        ? this.text.slice(closeStart, SyntaxTree.end(closeNumber.value))
        : '',
      closeDisabled:
        Option.isSome(closePrefix) && SyntaxTree.tokens(closePrefix.value, 'Disabled').length !== 0,
      tail: children.tail,
    })
    return node
  }

  /** @anchor bescript-body-projection */
  private kind(
    comment: Option.Option<IToken>,
    condition: Option.Option<IToken>,
    modifier: Option.Option<IToken>,
    call: Option.Option<ProjectedCall>,
  ): StatementKind {
    if (Option.isSome(comment)) return 'comment'
    if (Option.isSome(condition)) return condition.value.image === 'if' ? 'if' : 'while'
    if (Option.isSome(modifier)) return modifier.value.image === 'delay' ? 'delay' : 'async'
    const signature: ProjectedCall = Option.getOrThrow(call)
    if (signature.call.kind === 'fun' && signature.call.functionName === 'break') return 'break'
    if (signature.call.kind === 'fun' && signature.call.functionName === 'continue')
      return 'continue'
    return 'call'
  }
}
