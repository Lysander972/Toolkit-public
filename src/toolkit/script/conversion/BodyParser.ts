import { CstParser, EOF, tokenMatcher } from 'chevrotain'
import type { CstNode, ILexingResult, IRecognitionException } from 'chevrotain'
import { BodyLexer } from './BodyLexer.ts'

/** @anchor bescript-body-grammar */
export class BodyParser extends CstParser {
  private static readonly parser: BodyParser = new BodyParser()
  public readonly body: () => CstNode = this.RULE('body', (): void => {
    this.MANY((): void => {
      this.SUBRULE(this.line)
    })
    this.CONSUME(EOF)
  })

  public readonly line: () => CstNode = this.RULE('line', (): void => {
    this.OR([
      {
        ALT: (): void => {
          this.OPTION((): void => {
            this.CONSUME(BodyLexer.number)
          })
          this.CONSUME(BodyLexer.newline)
        },
      },
      {
        ALT: (): void => {
          this.SUBRULE(this.statement)
        },
      },
    ])
  })

  public readonly prefix: () => CstNode = this.RULE('prefix', (): void => {
    this.OPTION((): void => {
      this.CONSUME(BodyLexer.number)
    })
    this.OPTION2((): void => {
      this.CONSUME(BodyLexer.disabled)
    })
  })

  public readonly statement: () => CstNode = this.RULE('statement', (): void => {
    this.SUBRULE(this.prefix)
    this.OR([
      {
        ALT: (): void => {
          this.CONSUME(BodyLexer.comment)
        },
      },
      {
        ALT: (): void => {
          this.CONSUME(BodyLexer.condition)
          this.CONSUME(BodyLexer.openBrace)
          this.SUBRULE(this.call)
          this.CONSUME(BodyLexer.newline)
          this.MANY({
            GATE: (): boolean => !this.atClose(),
            DEF: (): void => {
              this.SUBRULE(this.line)
            },
          })
          this.SUBRULE2(this.prefix, { LABEL: 'closePrefix' })
          this.CONSUME(BodyLexer.closeBrace)
        },
      },
      {
        ALT: (): void => {
          this.OPTION((): void => {
            this.CONSUME(BodyLexer.modifier)
          })
          this.SUBRULE2(this.call)
        },
      },
    ])
    this.OR2([
      {
        ALT: (): void => {
          this.CONSUME2(BodyLexer.newline)
        },
      },
      {
        ALT: (): void => {
          this.CONSUME(EOF)
        },
      },
    ])
  })

  public readonly call: () => CstNode = this.RULE('call', (): void => {
    this.OPTION((): void => {
      this.OPTION2((): void => {
        this.CONSUME(BodyLexer.word, { LABEL: 'returnType' })
        this.CONSUME2(BodyLexer.word, { LABEL: 'returnName' })
      })
      this.CONSUME(BodyLexer.assign)
    })
    this.CONSUME(BodyLexer.callKind)
    this.SUBRULE(this.signature)
    this.CONSUME(BodyLexer.open)
    this.OPTION3((): void => {
      this.SUBRULE(this.argument)
      this.MANY((): void => {
        this.CONSUME(BodyLexer.separator)
        this.SUBRULE2(this.argument)
      })
    })
    this.CONSUME(BodyLexer.close)
  })

  public readonly signature: () => CstNode = this.RULE('signature', (): void => {
    this.CONSUME(BodyLexer.word)
    this.MANY((): void => {
      this.CONSUME(BodyLexer.dot)
      this.CONSUME2(BodyLexer.word)
    })
  })

  public readonly argument: () => CstNode = this.RULE('argument', (): void => {
    this.CONSUME(BodyLexer.argument)
    this.OR([
      {
        ALT: (): void => {
          this.CONSUME(BodyLexer.canvasStart)
          this.MANY((): void => {
            this.CONSUME(BodyLexer.canvasRaw)
          })
          this.CONSUME(BodyLexer.canvasEnd)
        },
      },
      {
        ALT: (): void => {
          this.OPTION((): void => {
            this.CONSUME(BodyLexer.raw)
          })
        },
      },
    ])
  })

  /** @anchor bescript-body-grammar */
  private constructor() {
    super([...BodyLexer.vocabulary()], { recoveryEnabled: false, nodeLocationTracking: 'full' })
    this.performSelfAnalysis()
  }

  /** @anchor bescript-body-grammar */
  public static parse(sourceText: string): CstNode {
    const lexed: ILexingResult = BodyLexer.lex(sourceText)
    const parser: BodyParser = BodyParser.parser
    parser.input = lexed.tokens
    try {
      const tree: CstNode = parser.body()
      if (parser.errors.length !== 0) {
        throw new Error(
          'BEScript 正文语法错误: ' +
            parser.errors
              .map(
                (error: IRecognitionException): string =>
                  String(error.token.startLine) +
                  ':' +
                  String(error.token.startColumn) +
                  ' ' +
                  error.message,
              )
              .join('\n'),
        )
      }
      return tree
    } finally {
      parser.input = []
    }
  }

  /** @anchor bescript-body-grammar */
  private atClose(): boolean {
    let offset: number = 1
    if (tokenMatcher(this.LA(offset), BodyLexer.number)) offset += 1
    if (tokenMatcher(this.LA(offset), BodyLexer.disabled)) offset += 1
    return tokenMatcher(this.LA(offset), BodyLexer.closeBrace)
  }
}
