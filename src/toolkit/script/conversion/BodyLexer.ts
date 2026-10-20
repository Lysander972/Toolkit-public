import { createToken, Lexer } from 'chevrotain'
import type { TokenType, ILexingResult } from 'chevrotain'

/**
 * @anchor bescript-body-lexer
 * @format-exempt bescript-lexer-patterns
 */
export class BodyLexer {
  public static readonly word: TokenType = createToken({ name: 'Word', pattern: Lexer.NA })
  public static readonly identifier: TokenType = createToken({
    name: 'Identifier',
    pattern: /[^\s.=():{}]+/,
    categories: BodyLexer.word,
  })
  public static readonly space: TokenType = createToken({
    name: 'Space',
    pattern: /[ \t]+/,
    group: 'trivia',
  })
  public static readonly newline: TokenType = createToken({
    name: 'Newline',
    pattern: /\n/,
    line_breaks: true,
  })
  public static readonly number: TokenType = createToken({ name: 'LineNumber', pattern: /[0-9]+:/ })
  public static readonly disabled: TokenType = createToken({ name: 'Disabled', pattern: '/*' })
  public static readonly comment: TokenType = createToken({
    name: 'Comment',
    pattern: /\/\/[^\n]*/,
  })
  public static readonly condition: TokenType = createToken({
    name: 'Condition',
    pattern: /(?:if|while)(?=\s*\{)/,
  })
  public static readonly modifier: TokenType = createToken({
    name: 'Modifier',
    pattern: /(?:delay|async)(?=\s)/,
  })
  public static readonly callKind: TokenType = createToken({
    name: 'CallKind',
    pattern: /(?:fun|toolf|varf|act|dvf)(?=\s)/,
  })
  public static readonly assign: TokenType = createToken({ name: 'Assign', pattern: '=' })
  public static readonly dot: TokenType = createToken({ name: 'Dot', pattern: '.' })
  public static readonly openBrace: TokenType = createToken({ name: 'OpenBrace', pattern: '{' })
  public static readonly closeBrace: TokenType = createToken({ name: 'CloseBrace', pattern: '}' })
  public static readonly open: TokenType = createToken({
    name: 'Open',
    pattern: '(',
    push_mode: 'argument',
  })
  public static readonly close: TokenType = createToken({
    name: 'Close',
    pattern: ')',
    pop_mode: true,
  })
  public static readonly argument: TokenType = createToken({
    name: 'ArgumentHead',
    pattern: /[^\s:()]+[ \t]/,
    pop_mode: true,
    push_mode: 'value',
  })
  public static readonly separator: TokenType = createToken({
    name: 'Separator',
    pattern: ':',
    pop_mode: true,
    push_mode: 'argument',
  })
  public static readonly valueClose: TokenType = createToken({
    name: 'ValueClose',
    pattern: ')',
    pop_mode: true,
    categories: BodyLexer.close,
  })
  public static readonly raw: TokenType = createToken({ name: 'RawValue', pattern: /[^:)\n]+/ })
  public static readonly canvasStart: TokenType = createToken({
    name: 'CanvasStart',
    pattern: '==hjson@1',
    push_mode: 'canvas',
  })
  public static readonly canvasEnd: TokenType = createToken({
    name: 'CanvasEnd',
    pattern: /==(?=[:)])/,
    pop_mode: true,
  })
  public static readonly canvasRaw: TokenType = createToken({
    name: 'CanvasRaw',
    pattern: /(?:[^=]|=(?!=[:)]))+/,
    line_breaks: true,
  })

  private static readonly lexer: Lexer = new Lexer({
    defaultMode: 'command',
    modes: {
      command: [
        BodyLexer.word,
        BodyLexer.space,
        BodyLexer.newline,
        BodyLexer.number,
        BodyLexer.disabled,
        BodyLexer.comment,
        BodyLexer.condition,
        BodyLexer.modifier,
        BodyLexer.callKind,
        BodyLexer.assign,
        BodyLexer.dot,
        BodyLexer.openBrace,
        BodyLexer.closeBrace,
        BodyLexer.open,
        BodyLexer.identifier,
      ],
      argument: [BodyLexer.space, BodyLexer.argument, BodyLexer.close],
      value: [BodyLexer.canvasStart, BodyLexer.separator, BodyLexer.valueClose, BodyLexer.raw],
      canvas: [BodyLexer.canvasEnd, BodyLexer.canvasRaw],
    },
  })

  private constructor() {}

  /** @anchor bescript-body-lexer */
  public static lex(text: string): ILexingResult {
    const lexed: ILexingResult = BodyLexer.lexer.tokenize(text)
    if (lexed.errors.length !== 0)
      throw new Error('BEScript 正文词法错误: ' + JSON.stringify(lexed.errors))
    return lexed
  }

  /** @anchor bescript-body-lexer */
  public static vocabulary(): readonly TokenType[] {
    return [
      BodyLexer.word,
      BodyLexer.identifier,
      BodyLexer.newline,
      BodyLexer.number,
      BodyLexer.disabled,
      BodyLexer.comment,
      BodyLexer.condition,
      BodyLexer.modifier,
      BodyLexer.callKind,
      BodyLexer.assign,
      BodyLexer.dot,
      BodyLexer.openBrace,
      BodyLexer.closeBrace,
      BodyLexer.open,
      BodyLexer.close,
      BodyLexer.argument,
      BodyLexer.separator,
      BodyLexer.valueClose,
      BodyLexer.raw,
      BodyLexer.canvasStart,
      BodyLexer.canvasEnd,
      BodyLexer.canvasRaw,
    ]
  }
}
