import { Option } from 'effect'
import ts from 'typescript'
import { z } from 'zod'
import {
  BescriptVocabulary,
  type ScriptParameter,
  type VarTypeName,
} from '../vocabulary/Vocabulary.ts'
import type { CallArgument, CallSignature, CallKind } from '../local_ast/LocalAst.ts'
import {
  ConversionBasis,
  type ActTarget,
  type ParameterSemantics,
  type PlatformMethod,
  type TargetMethod,
} from './ConversionBasis.ts'
import { VariableMapping } from './VariableMapping.ts'
import { ConstantMapping, type InlineCanvasCodec } from './ConstantMapping.ts'
import { SourceComments } from './SourceComments.ts'
import { TsSyntax } from './TsSyntax.ts'

/** @anchor bescript-ts-calls */
export interface CallOrigin {
  readonly kind: CallKind
  readonly ownerType: Option.Option<string>
  readonly ownerNameIsAlias: boolean
  readonly saveName: string
}

/** @anchor bescript-ts-calls */
export interface ArgumentOrigin {
  readonly sourceRef: Option.Option<number>
  readonly rawText: string
  readonly decodedText: string
}

interface VariableArgumentSource {
  readonly sourceRef: Option.Option<number>
}

/** @anchor bescript-ts-calls */
export class CallMapping {
  public static readonly source: z.ZodType<CallOrigin> = z.strictObject({
    kind: z.enum(['fun', 'toolf', 'varf', 'act', 'dvf']),
    ownerType: BescriptVocabulary.option(z.string()),
    ownerNameIsAlias: z.boolean(),
    saveName: z.string(),
  })
  private readonly constants: ConstantMapping
  /** @anchor bescript-ts-calls */
  public constructor(
    private readonly basis: ConversionBasis,
    private readonly variables: VariableMapping,
    private readonly sourceFile: ts.SourceFile,
    canvas: InlineCanvasCodec,
  ) {
    this.constants = ConstantMapping.create(basis, sourceFile, canvas)
  }

  /**
   * @anchor bescript-ts-calls
   * @fallback act 目标没有自定义脚本时使用已注册的平台方法映射。
   */
  public signature(call: CallSignature): PlatformMethod {
    if (call.kind !== 'act') return this.basis.method(call)
    const target: ActTarget = this.basis.target(call)
    const custom: Option.Option<TargetMethod> = Option.fromNullishOr(
      target.methods.find((method: TargetMethod): boolean => method.besName === call.functionName),
    )
    if (Option.isNone(custom)) return this.basis.method(call)
    if (custom.value.isPrivate && !this.basis.isCurrent(target))
      throw new Error('不能跨零件调用私有脚本')
    return {
      kind: 'act',
      ownerType: call.ownerType,
      besName: call.functionName,
      tsName: custom.value.tsName,
      receiver: 'Act',
      typeArguments: [],
      parameters: custom.value.parameters.map((parameter: ScriptParameter): ParameterSemantics =>
        CallMapping.parameter(parameter.typeName),
      ),
      argumentOrder: custom.value.parameters.map(
        (_parameter: ScriptParameter, index: number): number => index,
      ),
      returnType: custom.value.returnType,
      asynchronous: custom.value.asynchronous,
    }
  }

  /** @anchor bescript-ts-calls */
  public toTs(call: CallSignature): ts.CallExpression {
    const method: PlatformMethod = this.signature(call)
    if (call.arguments.length !== method.parameters.length)
      throw new Error('调用参数数量不匹配: ' + call.saveName)
    const argumentsList: ts.Expression[] = method.argumentOrder.map(
      (index: number): ts.Expression => {
        const argument: CallArgument = Option.getOrThrow(
          Option.fromNullishOr(call.arguments[index]),
        )
        const semantic: ParameterSemantics = Option.getOrThrow(
          Option.fromNullishOr(method.parameters[index]),
        )
        if (!argument.variable) return this.constants.toTs(argument, semantic)
        this.basis.assignable(this.variables.type(argument.decodedText), semantic.valueType)
        return SourceComments.attach(
          this.variables.expression(argument.decodedText),
          'variable-argument',
          { sourceRef: argument.sourceRef },
        )
      },
    )
    const expression: ts.Expression = this.receiver(call, method)
    return SourceComments.attach(
      ts.factory.createCallExpression(
        expression,
        method.typeArguments.length === 0
          ? undefined
          : method.typeArguments.map((type: VarTypeName): ts.TypeNode =>
              ts.factory.createTypeReferenceNode(this.basis.type(type)),
            ),
        argumentsList,
      ),
      'call',
      {
        kind: call.kind,
        ownerType: call.ownerType,
        ownerNameIsAlias: call.ownerNameIsAlias,
        saveName: call.saveName,
      },
    )
  }

  /**
   * @anchor bescript-ts-calls
   * @fallback 调用来源优先使用语句元数据，缺少时读取官方 AST 注释。
   * @fallback 官方调用节点没有泛型数组时按空数组校验固定泛型合同。
   * @fallback 变量参数来源优先使用参数来源元数据，其次读取节点注释，最后表示没有来源。
   * @fallback 调用名称优先保留来源拼写，缺少来源时使用解析出的平台名称。
   */
  public fromTs(
    expression: ts.Expression,
    origin: Option.Option<CallOrigin>,
    argumentOrigins: readonly ArgumentOrigin[],
  ): CallSignature {
    const call: ts.CallExpression = TsSyntax.call(expression)
    const source: Option.Option<CallOrigin> = Option.isSome(origin)
      ? origin
      : SourceComments.read(call, this.sourceFile, 'call', CallMapping.source)
    const typeArguments: readonly ts.TypeNode[] = Option.getOrElse(
      Option.fromNullishOr(call.typeArguments),
      (): readonly ts.TypeNode[] => [],
    )
    const genericTypes: readonly VarTypeName[] = typeArguments.map(
      (type: ts.TypeNode): VarTypeName => this.basis.fromType(type),
    )
    const decoded: CallSignature = this.decodeReceiver(call.expression, source, genericTypes)
    const method: PlatformMethod = this.signature(decoded)
    if (
      typeArguments.length !== method.typeArguments.length ||
      typeArguments.some(
        (type: ts.TypeNode, index: number): boolean =>
          this.basis.fromType(type) !== method.typeArguments[index],
      )
    )
      throw new Error('调用泛型与平台映射不一致')
    if (call.arguments.length !== method.argumentOrder.length)
      throw new Error('调用参数数量不匹配: ' + decoded.functionName)
    const argumentsList: CallArgument[] = []
    for (let index: number = 0; index < method.parameters.length; index += 1) {
      const expressionArgument: ts.Expression = Option.getOrThrow(
        Option.fromNullishOr(call.arguments[method.argumentOrder.indexOf(index)]),
      )
      const semantic: ParameterSemantics = Option.getOrThrow(
        Option.fromNullishOr(method.parameters[index]),
      )
      const node: ts.Expression = TsSyntax.unwrap(expressionArgument)
      if (
        ts.isIdentifier(node) ||
        ((ts.isPropertyAccessExpression(node) || ts.isElementAccessExpression(node)) &&
          node.expression.kind === ts.SyntaxKind.ThisKeyword)
      ) {
        const variable: ScriptParameter = this.variables.fromExpression(node)
        this.basis.assignable(variable.typeName, semantic.valueType)
        const reference: Option.Option<VariableArgumentSource> = SourceComments.read(
          expressionArgument,
          this.sourceFile,
          'variable-argument',
          z.strictObject({ sourceRef: BescriptVocabulary.option(z.number().int().min(0)) }),
        )
        const argumentOrigin: Option.Option<ArgumentOrigin> = Option.fromNullishOr(
          argumentOrigins[index],
        )
        argumentsList.push({
          sourceRef: Option.isSome(argumentOrigin)
            ? argumentOrigin.value.sourceRef
            : Option.isSome(reference)
              ? reference.value.sourceRef
              : Option.none(),
          variable: true,
          typeName: 'var',
          rawText: variable.name,
          decodedText: variable.name,
          varType: Option.none(),
        })
      } else
        argumentsList.push(
          this.constants.fromTs(
            expressionArgument,
            semantic,
            Option.fromNullishOr(argumentOrigins[index]),
          ),
        )
    }
    return {
      ...decoded,
      arguments: argumentsList,
      saveName: Option.isSome(source) ? source.value.saveName : decoded.functionName,
    }
  }

  /** @anchor bescript-ts-calls */
  private receiver(call: CallSignature, method: PlatformMethod): ts.Expression {
    if (call.kind === 'fun' || call.kind === 'toolf')
      return TsSyntax.path(method.receiver + '.' + method.tsName)
    if (call.kind === 'act') {
      const target: ActTarget = this.basis.target(call)
      const name: string = Option.getOrThrow(call.ownerName)
      const receiver: ts.Expression = ts.factory.createCallExpression(
        TsSyntax.path(
          name === 'this' ? 'Act.self' : call.ownerNameIsAlias ? 'Act.byName' : 'Act.byId',
        ),
        [ts.factory.createTypeReferenceNode(target.className)],
        name === 'this' ? [] : [ts.factory.createStringLiteral(name)],
      )
      return TsSyntax.access(receiver, method.tsName)
    }
    const ownerType: VarTypeName = BescriptVocabulary.varType.parse(
      Option.getOrThrow(call.ownerType),
    )
    const owner: ts.Expression = this.variables.expression(Option.getOrThrow(call.ownerName))
    this.basis.assignable(this.variables.type(Option.getOrThrow(call.ownerName)), ownerType)
    return TsSyntax.access(
      ts.factory.createParenthesizedExpression(
        ts.factory.createAsExpression(
          owner,
          ts.factory.createTypeReferenceNode(this.basis.type(ownerType)),
        ),
      ),
      method.tsName,
    )
  }

  /** @anchor bescript-ts-calls */
  private decodeReceiver(
    expression: ts.Expression,
    source: Option.Option<CallOrigin>,
    genericTypes: readonly VarTypeName[],
  ): CallSignature {
    const node: ts.Expression = TsSyntax.unwrap(expression)
    if (!ts.isPropertyAccessExpression(node) && !ts.isElementAccessExpression(node))
      throw new Error('调用必须指定平台属主')
    const name: string = ts.isPropertyAccessExpression(node)
      ? node.name.text
      : TsSyntax.name(node.argumentExpression)
    const owner: ts.Expression = TsSyntax.unwrap(node.expression)
    if (ts.isCallExpression(owner)) return this.decodeAct(owner, name, genericTypes)
    if (ts.isAsExpression(owner)) {
      const variable: ScriptParameter = this.variables.fromExpression(owner.expression)
      const type: VarTypeName = this.basis.fromType(owner.type)
      this.basis.assignable(variable.typeName, type)
      if (Option.isNone(source) || (source.value.kind !== 'varf' && source.value.kind !== 'dvf'))
        throw new Error('变量调用来源种类错误')
      const kind: CallKind = source.value.kind
      const method: PlatformMethod = this.reverseMethod(kind, Option.some(type), name, genericTypes)
      return CallMapping.empty(
        kind,
        method.besName,
        Option.some(type),
        Option.some(variable.name),
        Option.isSome(source) && source.value.ownerNameIsAlias,
      )
    }
    const path: string = TsSyntax.pathName(expression)
    const matches: PlatformMethod[] = this.basis.data.methods.filter(
      (method: PlatformMethod): boolean =>
        (method.kind === 'fun' || method.kind === 'toolf') &&
        method.receiver + '.' + method.tsName === path &&
        JSON.stringify(method.typeArguments) === JSON.stringify(genericTypes),
    )
    if (matches.length !== 1) throw new Error('TS 平台调用映射必须唯一: ' + path)
    const method: PlatformMethod = Option.getOrThrow(Option.fromNullishOr(matches[0]))
    return CallMapping.empty(method.kind, method.besName, Option.none(), Option.none(), false)
  }

  /**
   * @anchor bescript-ts-calls
   * @fallback 官方 act 节点没有泛型数组时按空数组校验唯一目标类型。
   * @fallback 目标没有自定义方法时使用显式注册的平台 act 方法映射。
   */
  private decodeAct(
    owner: ts.CallExpression,
    name: string,
    genericTypes: readonly VarTypeName[],
  ): CallSignature {
    const path: string = TsSyntax.pathName(owner.expression)
    if (!['Act.self', 'Act.byName', 'Act.byId'].includes(path))
      throw new Error('不支持的 act 选择器')
    const typeArguments: readonly ts.TypeNode[] = Option.getOrElse(
      Option.fromNullishOr(owner.typeArguments),
      (): readonly ts.TypeNode[] => [],
    )
    if (typeArguments.length !== 1) throw new Error('act 必须携带唯一目标类型')
    const type: ts.TypeNode = Option.getOrThrow(Option.fromNullishOr(typeArguments[0]))
    if (!ts.isTypeReferenceNode(type) || !ts.isIdentifier(type.typeName))
      throw new Error('act 目标类型错误')
    const targetClass: string = type.typeName.text
    const target: ActTarget = Option.getOrThrow(
      Option.fromNullishOr(
        this.basis.data.targets.find((item: ActTarget): boolean => item.className === targetClass),
      ),
    )
    let ownerName: string = 'this'
    if (path === 'Act.self') {
      if (owner.arguments.length !== 0 || !this.basis.isCurrent(target))
        throw new Error('Act.self 只能指向当前零件')
    } else {
      const argument: ts.Expression = Option.getOrThrow(Option.fromNullishOr(owner.arguments[0]))
      if (owner.arguments.length !== 1 || !ts.isStringLiteral(argument))
        throw new Error('act 选择器必须使用字符串 ID 或逻辑名')
      ownerName = argument.text
    }
    const stub: CallSignature = CallMapping.empty(
      'act',
      '',
      Option.some(target.deviceType),
      Option.some(ownerName),
      path === 'Act.byName',
    )
    const resolved: ActTarget = this.basis.target(stub)
    if (resolved.className !== targetClass) throw new Error('act 选择器与目标类型不一致')
    const custom: Option.Option<TargetMethod> = Option.fromNullishOr(
      target.methods.find((method: TargetMethod): boolean => method.tsName === name),
    )
    const besName: string = Option.isSome(custom)
      ? custom.value.besName
      : this.reverseMethod('act', Option.some(target.deviceType), name, genericTypes).besName
    return { ...stub, functionName: besName, saveName: besName }
  }

  /** @anchor bescript-ts-calls */
  private reverseMethod(
    kind: CallKind,
    ownerType: Option.Option<string>,
    name: string,
    genericTypes: readonly VarTypeName[],
  ): PlatformMethod {
    const matches: PlatformMethod[] = this.basis.data.methods.filter(
      (method: PlatformMethod): boolean =>
        method.kind === kind &&
        method.tsName === name &&
        JSON.stringify(method.typeArguments) === JSON.stringify(genericTypes) &&
        Option.makeEquivalence<string>((left: string, right: string): boolean => left === right)(
          method.ownerType,
          ownerType,
        ),
    )
    if (matches.length !== 1) throw new Error('TS 方法映射必须唯一: ' + name)
    return Option.getOrThrow(Option.fromNullishOr(matches[0]))
  }

  /** @anchor bescript-ts-calls */
  public static parameter(typeName: VarTypeName): ParameterSemantics {
    return {
      typeName,
      valueType: typeName,
      constant: ['Float', 'Long', 'Number', 'String', 'Bool', 'Vector3', 'Color', 'Enum'].includes(
        typeName,
      )
        ? 'value'
        : 'empty',
      enumName: Option.none(),
    }
  }

  /** @anchor bescript-ts-calls */
  public static empty(
    kind: CallKind,
    functionName: string,
    ownerType: Option.Option<string>,
    ownerName: Option.Option<string>,
    ownerNameIsAlias: boolean,
  ): CallSignature {
    return {
      kind,
      functionName,
      saveName: functionName,
      ownerType,
      ownerName,
      ownerNameIsAlias,
      arguments: [],
      returnVariable: Option.none(),
      discardsReturn: false,
    }
  }
}
