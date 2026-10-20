import { Option } from 'effect'
import { z } from 'zod'
import { GuidFormat } from '@toolkit/project-model/guid'

/** @anchor bescript-vocabulary */
export type VarTypeName =
  | 'Void'
  | 'Float'
  | 'Datetime'
  | 'UI'
  | 'Vector3'
  | 'Bool'
  | 'String'
  | 'Enum'
  | 'UISpine'
  | 'UICircle'
  | 'UIRect'
  | 'UIButton'
  | 'UILabel'
  | 'Color'
  | 'Device'
  | 'UIInput'
  | 'UILine'
  | 'List'
  | 'Dict'
  | 'Any'
  | 'UIAvatar'
  | 'UIRawImage'
  | 'Mech'
  | 'Player'
  | 'Icon'
  | 'Long'
  | 'Number'
  | 'Struct'
  | 'Canvas'
  | 'Bytes'
  | 'Mesh'

/** @anchor bescript-vocabulary */
export type GlobalVarTypeName =
  | 'Float'
  | 'Datetime'
  | 'Vector3'
  | 'Bool'
  | 'String'
  | 'UISpine'
  | 'UIRect'
  | 'UIButton'
  | 'UILabel'
  | 'Color'
  | 'Device'
  | 'UIInput'
  | 'UILine'
  | 'List'
  | 'Dict'
  | 'UIAvatar'
  | 'UIRawImage'
  | 'Mech'
  | 'Player'
  | 'Long'
  | 'Struct'
  | 'Canvas'

/** @anchor bescript-vocabulary */
export interface ScriptParameter {
  readonly typeName: VarTypeName
  readonly name: string
}

/** @anchor bescript-vocabulary */
export interface RgbColor {
  readonly red: number
  readonly green: number
  readonly blue: number
}

/** @anchor bescript-vocabulary */
export interface ScriptHeader {
  readonly name: string
  readonly comment: Option.Option<string>
  readonly debug: boolean
  readonly skip: boolean
  readonly isPrivate: boolean
  readonly guid: Option.Option<string>
  readonly color: Option.Option<RgbColor>
  readonly parameters: readonly ScriptParameter[]
  readonly returnParameter: Option.Option<ScriptParameter>
}

/** @anchor bescript-vocabulary */
export class BescriptVocabulary {
  public static readonly varType: z.ZodType<VarTypeName> = z.enum([
    'Void',
    'Float',
    'Datetime',
    'UI',
    'Vector3',
    'Bool',
    'String',
    'Enum',
    'UISpine',
    'UICircle',
    'UIRect',
    'UIButton',
    'UILabel',
    'Color',
    'Device',
    'UIInput',
    'UILine',
    'List',
    'Dict',
    'Any',
    'UIAvatar',
    'UIRawImage',
    'Mech',
    'Player',
    'Icon',
    'Long',
    'Number',
    'Struct',
    'Canvas',
    'Bytes',
    'Mesh',
  ])

  public static readonly globalVarType: z.ZodType<GlobalVarTypeName> = z.enum([
    'Float',
    'Datetime',
    'Vector3',
    'Bool',
    'String',
    'UISpine',
    'UIRect',
    'UIButton',
    'UILabel',
    'Color',
    'Device',
    'UIInput',
    'UILine',
    'List',
    'Dict',
    'UIAvatar',
    'UIRawImage',
    'Mech',
    'Player',
    'Long',
    'Struct',
    'Canvas',
  ])

  public static readonly parameter: z.ZodType<ScriptParameter> = z.strictObject({
    typeName: BescriptVocabulary.varType,
    name: z
      .string()
      .min(1)
      .refine((name: string): boolean => !BescriptVocabulary.rejectsEscaped(name)),
  })

  public static readonly color: z.ZodType<RgbColor> = z.strictObject({
    red: z.number().int().min(0).max(255),
    green: z.number().int().min(0).max(255),
    blue: z.number().int().min(0).max(255),
  })

  private constructor() {}

  /**
   * @anchor bescript-vocabulary
   * @fallback None 明确表示可选字段缺席，校验接受缺席，不补造字段值。
   */
  public static option<T>(schema: z.ZodType<T>): z.ZodType<Option.Option<T>> {
    return z.custom<Option.Option<T>>(
      (input: unknown): boolean =>
        Option.isOption(input) &&
        Option.match(input, {
          onNone: (): boolean => true,
          onSome: (value: unknown): boolean => schema.safeParse(value).success,
        }),
    )
  }

  /**
   * 名称、参数名与全局变量名禁止出现会被 BEScript 转义的字符。
   * @anchor bescript-vocabulary
   */
  public static rejectsEscaped(raw: string): boolean {
    return raw.includes('\n') || raw.includes('\r') || BescriptVocabulary.encode(raw).includes('@')
  }

  /**
   * @anchor bescript-vocabulary
   */
  public static isBareIdentifier(this: void, raw: string): boolean {
    for (let index: number = 0; index < raw.length; index += 1) {
      const character: string = raw.charAt(index)
      // eslint-disable-next-line no-restricted-properties -- BEScript 裸标识符语法排除所有空白字符。
      if (character.trim().length === 0 || '.=():{}'.includes(character)) return false
    }
    return raw.length > 0
  }

  /**
   * @anchor bescript-vocabulary
   * @format-exempt bescript-escape-codec
   */
  public static decode(raw: string): string {
    return raw.replace(/@([1-9@])/gu, (_match: string, mark: string): string =>
      BescriptVocabulary.decodedMark(mark),
    )
  }

  /**
   * @anchor bescript-vocabulary
   * @format-exempt bescript-escape-codec
   */
  public static encode(raw: string): string {
    const wrapped: Option.Option<string> = BescriptVocabulary.encodeGuidReference(raw)
    if (Option.isSome(wrapped)) {
      return wrapped.value
    }
    return raw.replace(/[@:=()\r\n]/gu, (char: string): string =>
      BescriptVocabulary.encodedMark(char),
    )
  }

  /** @anchor bescript-vocabulary */
  private static decodedMark(mark: string): string {
    switch (mark) {
      case '1':
        return ':'
      case '2':
        return ';'
      case '3':
        return '='
      case '4':
        return '('
      case '5':
        return ')'
      case '6':
        return '/'
      case '7':
        return '\\'
      case '8':
        return ' '
      case '9':
        return '.'
      case '@':
        return '@'
      default:
        return '@' + mark
    }
  }

  /** @anchor bescript-vocabulary */
  private static encodedMark(char: string): string {
    switch (char) {
      case '@':
        return '@@'
      case ':':
        return '@1'
      case '=':
        return '@3'
      case '(':
        return '@4'
      case ')':
        return '@5'
      case '\r':
      case '\n':
        return ''
      default:
        return char
    }
  }

  /** @anchor bescript-vocabulary */
  private static encodeGuidReference(raw: string): Option.Option<string> {
    const prefix: string = '==guid:'
    const suffix: string = '=='
    if (!raw.startsWith(prefix) || !raw.endsWith(suffix)) {
      return Option.none()
    }
    if (raw.length !== prefix.length + 32 + suffix.length) {
      return Option.none()
    }
    const guid: string = raw.slice(prefix.length, raw.length - suffix.length)
    if (!GuidFormat.validate(guid)) {
      return Option.none()
    }
    return Option.some('==guid@1' + guid + '==')
  }
}
