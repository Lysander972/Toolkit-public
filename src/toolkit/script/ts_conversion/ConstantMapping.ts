import { CanvasConverter } from '@toolkit/canvas'
import { Option } from 'effect'
import ts from 'typescript'
import { z } from 'zod'
import { GuidFormat } from '@toolkit/project-model/guid'
import { BescriptVocabulary, type VarTypeName } from '../vocabulary/Vocabulary.ts'
import type { CallArgument } from '../local_ast/LocalAst.ts'
import {
  ConversionBasis,
  type ParameterSemantics,
  type PlatformEnum,
  type PlatformEnumValue,
} from './ConversionBasis.ts'
import { SourceComments } from './SourceComments.ts'
import { TsSyntax } from './TsSyntax.ts'

/** @anchor bescript-ts-constants */
interface ArgumentSource {
  readonly sourceRef: Option.Option<number>
  readonly rawText: string
  readonly decodedText: string
}

/** @anchor bescript-ts-constants */
export interface InlineCanvasCodec {
  /** @anchor bescript-ts-constants */
  toHtml(source: string): string
  /** @anchor bescript-ts-constants */
  fromHtml(html: string, nodeMode: boolean): string
  /** @anchor bescript-ts-constants */
  equivalent(current: string, original: string): boolean
}

/** @anchor bescript-ts-constants */
export class NativeInlineCanvas implements InlineCanvasCodec {
  declare private readonly nominal: never
  /** @anchor bescript-ts-constants */
  public toHtml(source: string): string {
    return CanvasConverter.toHtmlInline(CanvasConverter.fromInline(source))
  }

  /** @anchor bescript-ts-constants */
  public fromHtml(html: string, nodeMode: boolean): string {
    const document: ReturnType<typeof CanvasConverter.fromHtml> = CanvasConverter.fromHtml(html)
    return nodeMode
      ? CanvasConverter.toInlineHjson(document)
      : CanvasConverter.toInlineBase64(document)
  }

  /** @anchor bescript-ts-constants */
  public equivalent(current: string, original: string): boolean {
    return this.toHtml(current) === this.toHtml(original)
  }
}

/** @anchor bescript-ts-constants */
export class ConstantMapping {
  public static readonly source: z.ZodType<ArgumentSource> = z.strictObject({
    sourceRef: BescriptVocabulary.option(z.number().int().min(0)),
    rawText: z.string(),
    decodedText: z.string(),
  })
  /** @anchor bescript-ts-constants */
  private constructor(
    private readonly basis: ConversionBasis,
    private readonly sourceFile: ts.SourceFile,
    private readonly canvas: InlineCanvasCodec,
  ) {}

  /** @anchor bescript-ts-constants */
  public static create(
    basis: ConversionBasis,
    sourceFile: ts.SourceFile,
    canvas: InlineCanvasCodec,
  ): ConstantMapping {
    return new ConstantMapping(basis, sourceFile, canvas)
  }

  /** @anchor bescript-ts-constants */
  public toTs(argument: CallArgument, semantic: ParameterSemantics): ts.Expression {
    if (argument.variable) throw new Error('常量映射不能接收变量')
    if (argument.typeName !== semantic.typeName)
      throw new Error('常量类型与平台参数不一致: ' + argument.typeName)
    ConstantMapping.validate(argument.decodedText, semantic)
    const expression: ts.Expression = this.expression(argument.decodedText, semantic)
    return SourceComments.attach(expression, 'argument', {
      sourceRef: argument.sourceRef,
      rawText: argument.rawText,
      decodedText: argument.decodedText,
    })
  }

  /**
   * @anchor bescript-ts-constants
   * @fallback 参数来源优先使用调用方传入的来源，缺少时读取官方 AST 注释。
   */
  public fromTs(
    expression: ts.Expression,
    semantic: ParameterSemantics,
    origin: Option.Option<ArgumentSource>,
  ): CallArgument {
    const source: Option.Option<ArgumentSource> = Option.isSome(origin)
      ? origin
      : SourceComments.read(expression, this.sourceFile, 'argument', ConstantMapping.source)
    let decodedText: string = this.value(expression, semantic)
    if (Option.isSome(source) && this.equivalent(decodedText, source.value.decodedText, semantic))
      decodedText = source.value.decodedText
    ConstantMapping.validate(decodedText, semantic)
    const parsedType: z.ZodSafeParseResult<VarTypeName> = BescriptVocabulary.varType.safeParse(
      semantic.typeName,
    )
    return {
      sourceRef: Option.isSome(source) ? source.value.sourceRef : Option.none(),
      variable: false,
      typeName: semantic.typeName,
      rawText:
        Option.isSome(source) && decodedText === source.value.decodedText
          ? source.value.rawText
          : BescriptVocabulary.encode(decodedText),
      decodedText,
      varType: parsedType.success ? Option.some(parsedType.data) : Option.none(),
    }
  }

  /** @anchor bescript-ts-constants */
  private expression(value: string, semantic: ParameterSemantics): ts.Expression {
    if (semantic.constant === 'enum') {
      const enumeration: PlatformEnum = this.enumeration(semantic)
      const member: PlatformEnumValue = Option.getOrThrow(
        Option.fromNullishOr(
          enumeration.values.find((item: PlatformEnumValue): boolean => item.value === value),
        ),
      )
      return TsSyntax.path(enumeration.tsName + '.' + member.tsName)
    }
    const type: string = this.basis.type(semantic.valueType)
    if (semantic.constant === 'canvas' && value.length > 0)
      return this.canvasExpression(value, type)
    return ts.factory.createCallExpression(
      TsSyntax.path(type + '.fromBeConst'),
      undefined,
      value.length === 0 ? [] : [ts.factory.createStringLiteral(value)],
    )
  }

  /**
   * @anchor bescript-ts-constants
   * @fallback 无参数的固定常量调用表示显式空常量，而不是缺失参数。
   */
  private value(expression: ts.Expression, semantic: ParameterSemantics): string {
    if (semantic.constant === 'enum') {
      const enumeration: PlatformEnum = this.enumeration(semantic)
      const name: string = TsSyntax.pathName(expression)
      return Option.getOrThrow(
        Option.fromNullishOr(
          enumeration.values.find(
            (item: PlatformEnumValue): boolean => enumeration.tsName + '.' + item.tsName === name,
          ),
        ),
      ).value
    }
    const call: ts.CallExpression = TsSyntax.call(expression)
    if (Option.isSome(Option.fromNullishOr(call.typeArguments))) throw new Error('常量不接受泛型')
    const type: string = this.basis.type(semantic.valueType)
    const name: string = TsSyntax.pathName(call.expression)
    if (semantic.constant === 'canvas' && name !== type + '.fromBeConst')
      return this.canvasValue(call, type)
    if (name !== type + '.fromBeConst' || call.arguments.length > 1)
      throw new Error('不支持的常量表达式')
    if (call.arguments.length === 0) return ''
    const value: ts.Expression = Option.getOrThrow(Option.fromNullishOr(call.arguments[0]))
    if (!ts.isStringLiteral(value)) throw new Error('BES 常量必须使用字符串字面量')
    return value.text
  }

  /** @anchor bescript-ts-constants */
  private canvasExpression(value: string, type: string): ts.Expression {
    if (value.startsWith('canvas:') || value.startsWith('==hjson:')) {
      const html: string = this.canvas.toHtml(value)
      const template: ts.NoSubstitutionTemplateLiteral =
        ts.factory.createNoSubstitutionTemplateLiteral('\n' + html + '\n')
      return ts.factory.createCallExpression(
        TsSyntax.path(type + (value.startsWith('canvas:') ? '.fromBase64Const' : '.fromNodeConst')),
        undefined,
        value.startsWith('canvas:')
          ? [template]
          : [ts.factory.createStringLiteral(CanvasConverter.fromHtml(html).name), template],
      )
    }
    const legacy: boolean = value.startsWith('guid:')
    const guid: string = legacy ? value.slice(5) : value.slice(7, -2)
    if (
      (!legacy && (!value.startsWith('==guid:') || !value.endsWith('=='))) ||
      !GuidFormat.validate(guid)
    )
      throw new Error('Canvas GUID 格式错误')
    return ts.factory.createCallExpression(
      TsSyntax.path(type + (legacy ? '.fromGuidConstLegacy' : '.fromGuidConst')),
      undefined,
      [ts.factory.createStringLiteral(guid)],
    )
  }

  /** @anchor bescript-ts-constants */
  private canvasValue(call: ts.CallExpression, type: string): string {
    const name: string = TsSyntax.pathName(call.expression)
    if (name === type + '.fromGuidConstLegacy' || name === type + '.fromGuidConst')
      return this.canvasGuidValue(call, name)
    if (name !== type + '.fromBase64Const' && name !== type + '.fromNodeConst')
      throw new Error('不支持的 Canvas 构造器')
    const nodeMode: boolean = name === type + '.fromNodeConst'
    return this.canvasInlineValue(call, nodeMode)
  }

  /**
   * @anchor bescript-ts-constants
   * @format-exempt bescript-canvas-path-split Canvas GUID 与 HTML 内联格式必须分别校验。
   */
  private canvasGuidValue(call: ts.CallExpression, name: string): string {
    const guid: ts.Expression = Option.getOrThrow(Option.fromNullishOr(call.arguments[0]))
    if (call.arguments.length !== 1 || !ts.isStringLiteral(guid) || !GuidFormat.validate(guid.text))
      throw new Error('Canvas GUID 参数错误')
    return name.endsWith('Legacy') ? 'guid:' + guid.text : '==guid:' + guid.text + '=='
  }

  /**
   * @anchor bescript-ts-constants
   * @format-exempt bescript-canvas-path-split Canvas GUID 与 HTML 内联格式必须分别校验。
   */
  private canvasInlineValue(call: ts.CallExpression, nodeMode: boolean): string {
    if (call.arguments.length !== (nodeMode ? 2 : 1)) throw new Error('Canvas 构造器参数数量错误')
    const template: ts.Expression = Option.getOrThrow(
      Option.fromNullishOr(call.arguments[nodeMode ? 1 : 0]),
    )
    if (!ts.isNoSubstitutionTemplateLiteral(template))
      throw new Error('Canvas 只能使用无插值 HTML 模板')
    const document: ReturnType<typeof CanvasConverter.fromHtml> = CanvasConverter.fromHtml(
      template.text,
    )
    if (nodeMode) {
      const nameArgument: ts.Expression = Option.getOrThrow(Option.fromNullishOr(call.arguments[0]))
      if (!ts.isStringLiteral(nameArgument) || nameArgument.text !== document.name)
        throw new Error('Canvas 名称必须与 HTML 一致')
    }
    return this.canvas.fromHtml(template.text, nodeMode)
  }

  /** @anchor bescript-ts-constants */
  private equivalent(current: string, original: string, semantic: ParameterSemantics): boolean {
    if (current === original) return true
    if (
      semantic.constant !== 'canvas' ||
      !(original.startsWith('canvas:') || original.startsWith('==hjson:')) ||
      !(current.startsWith('canvas:') || current.startsWith('==hjson:'))
    )
      return false
    return this.canvas.equivalent(current, original)
  }

  /** @anchor bescript-ts-constants */
  private enumeration(semantic: ParameterSemantics): PlatformEnum {
    return Option.getOrThrow(
      Option.fromNullishOr(
        this.basis.data.enums.find((item: PlatformEnum): boolean =>
          Option.contains(item.name)(semantic.enumName),
        ),
      ),
    )
  }

  /** @anchor bescript-ts-constants */
  private static validate(value: string, semantic: ParameterSemantics): void {
    if (semantic.constant === 'empty' && value.length !== 0) throw new Error('此参数不允许非空常量')
    if (semantic.constant !== 'value') return
    if (semantic.valueType === 'Bool' && value !== '0' && value !== '1')
      throw new Error('Bool 常量只能是 0 或 1')
    if (
      semantic.valueType === 'Float' ||
      semantic.valueType === 'Long' ||
      semantic.valueType === 'Number'
    ) {
      z.string().min(1).parse(value)
      const number: number = Number(value)
      if (!Number.isFinite(number) || (semantic.valueType === 'Long' && !Number.isInteger(number)))
        throw new Error('非法数值常量: ' + value)
    }
  }
}
