import { Option } from 'effect'
import type { CstNode, IToken, CstElement } from 'chevrotain'

/** @anchor bescript-body-projection */
export class SyntaxTree {
  private constructor() {}

  /**
   * @anchor bescript-body-projection
   * @fallback Chevrotain 的可选产生式没有 children 条目；此处明确投影为空集合。
   */
  private static elements(tree: CstNode, key: string): readonly CstElement[] {
    return Option.getOrElse(Option.fromNullishOr(tree.children[key]), (): CstElement[] => [])
  }

  /** @anchor bescript-body-projection */
  public static nodes(tree: CstNode, key: string): readonly CstNode[] {
    const nodes: CstNode[] = []
    for (const element of SyntaxTree.elements(tree, key)) {
      if ('children' in element) nodes.push(element)
    }
    return nodes
  }

  /** @anchor bescript-body-projection */
  public static tokens(tree: CstNode, key: string): readonly IToken[] {
    const tokens: IToken[] = []
    for (const element of SyntaxTree.elements(tree, key)) {
      if ('image' in element) tokens.push(element)
    }
    return tokens
  }

  /** @anchor bescript-body-projection */
  public static token(tree: CstNode, key: string): IToken {
    return Option.getOrThrow(Option.fromNullishOr(SyntaxTree.tokens(tree, key)[0]))
  }

  /** @anchor bescript-body-projection */
  public static node(tree: CstNode, key: string): CstNode {
    return Option.getOrThrow(Option.fromNullishOr(SyntaxTree.nodes(tree, key)[0]))
  }

  /** @anchor bescript-body-projection */
  public static end(token: IToken): number {
    return Option.getOrThrow(Option.fromNullishOr(token.endOffset)) + 1
  }
}
