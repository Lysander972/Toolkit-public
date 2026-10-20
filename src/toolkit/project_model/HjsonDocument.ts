import Hjson from 'hjson'
import { Option } from 'effect'
import { z } from 'zod'

/**
 * @anchor logical-hjson-document
 * @format-exempt hjson-object-boundary
 */
export interface HjsonFields {
  readonly [key: string]: unknown
}

/** @anchor logical-hjson-document */
interface HjsonComments {
  /** @anchor logical-hjson-document */
  extract(value: unknown): unknown
  /** @anchor logical-hjson-document */
  merge(comments: unknown, value: unknown): void
}

/** @anchor logical-hjson-document */
export class HjsonDocument {
  private static readonly api: HjsonComments = z
    .object({
      comments: z.custom<HjsonComments>((input: unknown): boolean => {
        const fields: HjsonFields = z.record(z.string(), z.unknown()).parse(input)
        return typeof fields['extract'] === 'function' && typeof fields['merge'] === 'function'
      }),
    })
    .parse(Hjson).comments

  private constructor(
    public readonly fields: HjsonFields,
    private readonly comments: Option.Option<unknown>,
  ) {}

  /** @anchor logical-hjson-document */
  public static read(source: string): HjsonDocument {
    const fields: HjsonFields = z
      .custom<HjsonFields>(
        (input: unknown): boolean => z.record(z.string(), z.unknown()).safeParse(input).success,
      )
      .parse(Hjson.parse(source, { keepWsc: true }))
    return new HjsonDocument(fields, Option.fromNullishOr(HjsonDocument.api.extract(fields)))
  }

  /** @anchor logical-hjson-document @passthrough-exempt 对象与数组共用官方注释合并与序列化边界。 */
  public text(): string {
    return HjsonDocument.emit(this.fields, this.comments)
  }

  /** @anchor logical-hjson-document */
  public static sequence(source: string, entries: readonly unknown[]): string {
    const values: unknown[] = z
      .custom<unknown[]>((input: unknown): boolean => Array.isArray(input))
      .parse(Hjson.parse(source, { keepWsc: true }))
    const comments: Option.Option<unknown> = Option.fromNullishOr(HjsonDocument.api.extract(values))
    values.splice(0, values.length, ...entries)
    return HjsonDocument.emit(values, comments)
  }

  private static emit(value: unknown, comments: Option.Option<unknown>): string {
    if (Option.isSome(comments)) HjsonDocument.api.merge(comments.value, value)
    return Hjson.stringify(value, {
      keepWsc: true,
      space: 1,
      emitRootBraces: false,
      eol: '\n',
    })
  }
}
