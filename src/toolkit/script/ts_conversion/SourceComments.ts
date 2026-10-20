import { Option } from 'effect'
import ts from 'typescript'
import { z } from 'zod'
import { TsSyntax } from './TsSyntax.ts'

interface CommentEnvelope {
  readonly kind: string
  readonly data: unknown
}

interface NoneOption {
  readonly _id: 'Option'
  readonly _tag: 'None'
}

interface SomeOption {
  readonly _id: 'Option'
  readonly _tag: 'Some'
  readonly value: unknown
}

/** @anchor bescript-ts-source */
export class SourceComments {
  private static readonly envelope: z.ZodType<CommentEnvelope> = z.strictObject({
    kind: z.string(),
    data: z.unknown(),
  })
  private static readonly option: z.ZodType = z.union([
    z.strictObject({ _id: z.literal('Option'), _tag: z.literal('None') }),
    z.strictObject({ _id: z.literal('Option'), _tag: z.literal('Some'), value: z.unknown() }),
  ])
  private static readonly noneOption: z.ZodType<NoneOption> = z.strictObject({
    _id: z.literal('Option'),
    _tag: z.literal('None'),
  })
  private static readonly someOption: z.ZodType<SomeOption> = z.strictObject({
    _id: z.literal('Option'),
    _tag: z.literal('Some'),
    value: z.unknown(),
  })
  /** @anchor bescript-ts-source */
  private constructor() {}

  /** @anchor bescript-ts-source */
  public static attach<T extends ts.Node>(node: T, kind: string, data: unknown): T {
    return ts.addSyntheticLeadingComment(
      node,
      ts.SyntaxKind.MultiLineCommentTrivia,
      '@bescript:' + Buffer.from(JSON.stringify({ kind, data }), 'utf8').toString('base64'),
      true,
    )
  }

  /**
   * @anchor bescript-ts-source
   * @fallback 只有未携带结构化来源标记时才返回 None；这是来源合同的缺席语义，不补造业务数据。
   */
  public static read<T>(
    node: ts.Node,
    sourceFile: ts.SourceFile,
    kind: string,
    schema: z.ZodType<T>,
  ): Option.Option<T> {
    const values: T[] = []
    for (const comment of SourceComments.leading(node, sourceFile)) {
      if (!comment.startsWith('@bescript:')) continue
      const envelope: CommentEnvelope = SourceComments.envelope.parse(
        JSON.parse(
          Buffer.from(comment.slice('@bescript:'.length), 'base64').toString('utf8'),
          SourceComments.revive,
        ),
      )
      if (envelope.kind === kind) values.push(schema.parse(envelope.data))
    }
    if (values.length > 1) throw new Error('重复的来源标记: ' + kind)
    return Option.fromNullishOr(values[0])
  }

  /**
   * @anchor bescript-ts-source
   * @fallback 没有合成注释时使用官方节点的空来源集合。
   * @fallback 没有前置注释范围时使用官方节点的空范围集合。
   * @fallback 没有尾随注释范围时使用官方节点的空范围集合。
   */
  public static leading(node: ts.Node, sourceFile: ts.SourceFile): readonly string[] {
    const synthetic: readonly ts.SynthesizedComment[] = Option.getOrElse(
      Option.fromNullishOr(ts.getSyntheticLeadingComments(node)),
      (): readonly ts.SynthesizedComment[] => [],
    )
    const result: string[] = synthetic.map((comment: ts.SynthesizedComment): string => comment.text)
    if (node.pos >= 0) {
      const leading: readonly ts.CommentRange[] = Option.getOrElse(
        Option.fromNullishOr(ts.getLeadingCommentRanges(sourceFile.text, node.pos)),
        (): readonly ts.CommentRange[] => [],
      )
      const trailing: readonly ts.CommentRange[] = Option.getOrElse(
        Option.fromNullishOr(ts.getTrailingCommentRanges(sourceFile.text, node.pos)),
        (): readonly ts.CommentRange[] => [],
      )
      const ranges: readonly ts.CommentRange[] = [
        ...leading,
        ...trailing.filter(
          (range: ts.CommentRange): boolean =>
            !leading.some((item: ts.CommentRange): boolean => item.pos === range.pos),
        ),
      ]
      for (const range of ranges)
        result.push(
          sourceFile.text.slice(
            range.pos + 2,
            range.end - (range.kind === ts.SyntaxKind.MultiLineCommentTrivia ? 2 : 0),
          ),
        )
    }
    return result
  }

  /** @anchor bescript-ts-source */
  public static payload(node: ts.Node, sourceFile: ts.SourceFile): string {
    const comments: readonly string[] = SourceComments.leading(node, sourceFile).filter(
      (comment: string): boolean => !comment.startsWith('@bescript:'),
    )
    if (comments.length !== 1) throw new Error('注释或禁用壳必须有且仅有一个正文')
    return Option.getOrThrow(Option.fromNullishOr(comments[0]))
  }

  /** @anchor bescript-ts-source */
  public static disabled(node: ts.Statement, opening: boolean): ts.EmptyStatement {
    const text: string = ts
      .createPrinter({ removeComments: true })
      .printNode(
        ts.EmitHint.Unspecified,
        node,
        ts.createSourceFile('view.ts', '', ts.ScriptTarget.Latest, false, ts.ScriptKind.TS),
      )
    let payload: string = text
    if (opening) {
      const parsed: ts.Statement = Option.getOrThrow(
        Option.fromNullishOr(TsSyntax.parse(text).statements[0]),
      )
      if (!ts.isIfStatement(parsed) && !ts.isWhileStatement(parsed))
        throw new Error('开壳只能是控制语句')
      const body: ts.Statement = ts.isIfStatement(parsed) ? parsed.thenStatement : parsed.statement
      payload = text.slice(0, body.end - 1)
    }
    if (payload.includes('*/')) throw new Error('禁用块注释正文不能含有块注释结束符')
    return ts.addSyntheticLeadingComment(
      ts.factory.createEmptyStatement(),
      ts.SyntaxKind.MultiLineCommentTrivia,
      ' ' + payload + ' ',
      true,
    )
  }

  /** @anchor bescript-ts-source */
  private static revive(this: void, _key: string, input: unknown): unknown {
    const parsed: z.ZodSafeParseResult<unknown> = SourceComments.option.safeParse(input)
    if (!parsed.success) return input
    const none: z.ZodSafeParseResult<NoneOption> = SourceComments.noneOption.safeParse(parsed.data)
    if (none.success) return Option.none()
    return Option.some(SourceComments.someOption.parse(parsed.data).value)
  }
}
