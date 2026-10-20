import { Option } from 'effect'
import ts from 'typescript'
import { z } from 'zod'
import type { DeviceFunction } from '../device_context/DeviceContext.ts'
import type { DeviceMetadata } from '../device_context/DeviceMetadata.ts'
import {
  ScriptListSchema,
  type ScriptEntry,
  type ScriptGroup,
} from '../device_context/ScriptList.ts'
import type { GlobalVariable } from '../device_context/GlobalVariables.ts'
import { LocalAstSchema, type LocalAst, type LocalAstNode } from '../local_ast/LocalAst.ts'
import type { ScriptSource, NodeSource, SourceField } from '../local_ast/SourceLayout.ts'
import { CallText } from '../conversion/CallText.ts'
import { BescriptVocabulary, type ScriptParameter } from '../vocabulary/Vocabulary.ts'
import {
  ConversionBasis,
  type ConversionBasisData,
  type PlatformEvent,
  type ScriptAssociation,
  type ActTarget,
  type TargetMethod,
} from './ConversionBasis.ts'
import { SourceComments } from './SourceComments.ts'
import { VariableMapping } from './VariableMapping.ts'
import { StatementMapping } from './StatementMapping.ts'
import { TsSyntax } from './TsSyntax.ts'
import type { InlineCanvasCodec } from './ConstantMapping.ts'

/** @anchor bescript-ts-methods */
export interface MethodGroup {
  readonly kind: ScriptGroup<DeviceFunction>['kind']
  readonly eventName: Option.Option<string>
}

/** @anchor bescript-ts-methods */
export interface MethodSource {
  readonly binding: ScriptEntry
  readonly group: MethodGroup
  readonly ast: LocalAst
  readonly scriptRelativePath: string
}

/** @anchor bescript-ts-methods */
interface MethodVisibility {
  readonly binding: ScriptEntry
  readonly isPrivate: boolean
}

/** @anchor bescript-ts-methods */
export class MethodMapping {
  public static readonly group: z.ZodType<MethodGroup> = z.strictObject({
    kind: z.enum(['custom', 'event']),
    eventName: BescriptVocabulary.option(z.string()),
  })

  private static readonly methodSource: z.ZodType<MethodSource> = z.strictObject({
    binding: z
      .unknown()
      .transform((input: unknown): ScriptEntry => ScriptListSchema.parseEntry(input)),
    group: MethodMapping.group,
    ast: z.unknown().transform((input: unknown): LocalAst => LocalAstSchema.parse(input)),
    scriptRelativePath: z.string(),
  })

  private constructor() {}

  /** @anchor bescript-ts-methods */
  public static source(method: ts.MethodDeclaration, sourceFile: ts.SourceFile): MethodSource {
    return Option.getOrThrow(
      SourceComments.read(method, sourceFile, 'method', MethodMapping.methodSource),
    )
  }

  /**
   * @anchor bescript-ts-methods
   * @fallback 事件方法没有返回参数时生成 void 返回类型。
   */
  public static toTs(
    item: DeviceFunction,
    group: ScriptGroup<DeviceFunction>,
    globals: readonly GlobalVariable[],
    basis: ConversionBasis,
    sourceFile: ts.SourceFile,
    canvas: InlineCanvasCodec,
  ): ts.MethodDeclaration {
    const association: ScriptAssociation = MethodMapping.association(item, group, basis)
    const parameters: readonly ScriptParameter[] = MethodMapping.parameters(item.ast, group, basis)
    const returned: Option.Option<ScriptParameter> =
      group.kind === 'event' ? Option.none() : item.ast.header.returnParameter
    const variables: VariableMapping = new VariableMapping(basis, globals, parameters)
    const statements: StatementMapping = new StatementMapping(
      basis,
      variables,
      sourceFile,
      returned,
      canvas,
    )
    const block: ts.Block = statements.toTs({
      ...item.ast,
      header: { ...item.ast.header, parameters, returnParameter: returned },
    })
    const modifier:
      ts.SyntaxKind.PrivateKeyword | ts.SyntaxKind.ProtectedKeyword | ts.SyntaxKind.PublicKeyword =
      group.kind === 'event'
        ? ts.SyntaxKind.PrivateKeyword
        : item.binding.isPrivate || item.ast.header.isPrivate
          ? ts.SyntaxKind.ProtectedKeyword
          : ts.SyntaxKind.PublicKeyword
    const asynchronous: boolean = MethodMapping.hasAwait(block)
    const returnType: ts.TypeNode = Option.isSome(returned)
      ? ts.factory.createTypeReferenceNode(basis.type(returned.value.typeName))
      : ts.factory.createKeywordTypeNode(ts.SyntaxKind.VoidKeyword)
    const method: ts.MethodDeclaration = ts.factory.createMethodDeclaration(
      [
        ts.factory.createModifier(modifier),
        ...(asynchronous ? [ts.factory.createModifier(ts.SyntaxKind.AsyncKeyword)] : []),
      ],
      undefined,
      association.tsName,
      undefined,
      undefined,
      parameters.map((parameter: ScriptParameter): ts.ParameterDeclaration =>
        ts.factory.createParameterDeclaration(
          undefined,
          undefined,
          basis.identifier(parameter.name),
          undefined,
          ts.factory.createTypeReferenceNode(basis.type(parameter.typeName)),
          undefined,
        ),
      ),
      asynchronous ? ts.factory.createTypeReferenceNode('Promise', [returnType]) : returnType,
      block,
    )
    return SourceComments.attach(method, 'method', {
      binding: item.binding,
      group: { kind: group.kind, eventName: group.eventName },
      ast: { ...item.ast, nodes: [] },
      scriptRelativePath: association.scriptRelativePath,
    })
  }

  /** @anchor bescript-ts-methods */
  public static fromTs(
    method: ts.MethodDeclaration,
    meta: MethodSource,
    globals: readonly GlobalVariable[],
    basis: ConversionBasis,
    sourceFile: ts.SourceFile,
    canvas: InlineCanvasCodec,
  ): DeviceFunction {
    const item: DeviceFunction = { binding: meta.binding, ast: meta.ast }
    const association: ScriptAssociation = MethodMapping.association(
      item,
      { ...meta.group, entries: [] },
      basis,
    )
    if (
      TsSyntax.name(method.name) !== association.tsName ||
      association.scriptRelativePath !== meta.scriptRelativePath
    )
      throw new Error('方法身份与来源关联不一致')
    if (
      Option.isSome(Option.fromNullishOr(method.typeParameters)) ||
      Option.isSome(Option.fromNullishOr(method.questionToken)) ||
      Option.isSome(Option.fromNullishOr(method.asteriskToken))
    )
      throw new Error('脚本方法不能是泛型、可选或生成器')
    const parameters: readonly ScriptParameter[] = MethodMapping.readParameters(method, basis)
    const returned: Option.Option<ScriptParameter> =
      meta.group.kind === 'event' ? Option.none() : meta.ast.header.returnParameter
    MethodMapping.checkReturnType(method, returned, basis)
    if (
      meta.group.kind === 'event' &&
      JSON.stringify(parameters) !==
        JSON.stringify(MethodMapping.parameters(meta.ast, { ...meta.group, entries: [] }, basis))
    )
      throw new Error('事件签名由平台决定，不能修改')
    const visibility: MethodVisibility = MethodMapping.visibility(method, meta)
    const variables: VariableMapping = new VariableMapping(basis, globals, parameters)
    const block: ts.Block = Option.getOrThrow(Option.fromNullishOr(method.body))
    if (MethodMapping.hasAwait(block) !== TsSyntax.hasModifier(method, ts.SyntaxKind.AsyncKeyword))
      throw new Error('方法 async 修饰符必须与实际等待一致')
    const nodes: readonly LocalAstNode[] = new StatementMapping(
      basis,
      variables,
      sourceFile,
      returned,
      canvas,
    ).fromTs(block)
    return {
      binding: visibility.binding,
      ast: LocalAstSchema.parse({
        ...meta.ast,
        header: {
          ...meta.ast.header,
          parameters: meta.group.kind === 'event' ? meta.ast.header.parameters : parameters,
          isPrivate: visibility.isPrivate,
        },
        nodes: MethodMapping.restoreText(nodes, meta.ast),
      }),
    }
  }

  /** @anchor bescript-ts-targets */
  public static targets(
    methods: readonly ts.MethodDeclaration[],
    metadata: DeviceMetadata,
    basis: ConversionBasisData,
    sourceFile: ts.SourceFile,
  ): ConversionBasis {
    const lookup: ConversionBasis = ConversionBasis.create(basis)
    const entries: DeviceFunction[] = []
    const asynchronous: Set<string> = new Set()
    for (const method of methods) {
      const meta: MethodSource = MethodMapping.source(method, sourceFile)
      if (meta.group.kind !== 'custom') continue
      const visibility: MethodVisibility = MethodMapping.visibility(method, meta)
      entries.push({
        binding: visibility.binding,
        ast: {
          ...meta.ast,
          header: {
            ...meta.ast.header,
            parameters: MethodMapping.readParameters(method, lookup),
            isPrivate: visibility.isPrivate,
          },
        },
      })
      if (TsSyntax.hasModifier(method, ts.SyntaxKind.AsyncKeyword))
        asynchronous.add(meta.binding.name)
    }
    const targets: readonly ActTarget[] = ConversionBasis.currentTargets(
      {
        metadata,
        globalVariables: [],
        scriptGroups: [{ kind: 'custom', eventName: Option.none(), entries }],
      },
      basis,
    )
    const own: ActTarget = Option.getOrThrow(Option.fromNullishOr(targets.at(-1)))
    return ConversionBasis.create({
      ...basis,
      targets: [
        ...targets.slice(0, -1),
        {
          ...own,
          methods: own.methods.map((method: TargetMethod): TargetMethod => ({
            ...method,
            asynchronous: asynchronous.has(method.besName),
          })),
        },
      ],
    })
  }

  /** @anchor bescript-ts-methods */
  public static sameGroup(left: MethodGroup, right: MethodGroup): boolean {
    return (
      left.kind === right.kind &&
      Option.makeEquivalence<string>((first: string, second: string): boolean => first === second)(
        left.eventName,
        right.eventName,
      )
    )
  }

  /** @anchor bescript-ts-methods */
  private static readParameters(
    method: ts.MethodDeclaration,
    basis: ConversionBasis,
  ): readonly ScriptParameter[] {
    return method.parameters.map((parameter: ts.ParameterDeclaration): ScriptParameter => {
      if (
        !ts.isIdentifier(parameter.name) ||
        Option.isSome(Option.fromNullishOr(parameter.initializer)) ||
        Option.isSome(Option.fromNullishOr(parameter.dotDotDotToken)) ||
        Option.isSome(Option.fromNullishOr(parameter.questionToken))
      )
        throw new Error('脚本参数必须是必填命名参数')
      return {
        name: basis.rawIdentifier(parameter.name.text),
        typeName: basis.fromType(Option.getOrThrow(Option.fromNullishOr(parameter.type))),
      }
    })
  }

  /**
   * @anchor bescript-ts-methods
   * @fallback 非调用语句没有参数来源，比较来源形状时其参数数量为零。
   */
  private static restoreText(
    nodes: readonly LocalAstNode[],
    ast: LocalAst,
  ): readonly LocalAstNode[] {
    return nodes.map((node: LocalAstNode): LocalAstNode => {
      const source: Option.Option<NodeSource> = Option.flatMap(
        ast.source,
        (layout: ScriptSource): Option.Option<NodeSource> =>
          Option.fromNullishOr(
            layout.nodes.find((item: NodeSource): boolean =>
              Option.contains(item.id)(node.sourceRef),
            ),
          ),
      )
      const fields: ReadonlyMap<string, string> = CallText.fields(node)
      const unchanged: boolean =
        Option.isSome(source) &&
        source.value.argumentCount ===
          Option.match(node.call, {
            onNone: (): number => 0,
            onSome: (call: import('../local_ast/LocalAst.ts').CallSignature): number =>
              call.arguments.length,
          }) &&
        source.value.fields.every(
          (field: SourceField): boolean => fields.get(field.key) === field.value,
        )
      return {
        ...node,
        text: unchanged && Option.isSome(source) ? source.value.command : CallText.command(node),
        children: MethodMapping.restoreText(node.children, ast),
      }
    })
  }

  /** @anchor bescript-ts-methods */
  private static visibility(method: ts.MethodDeclaration, meta: MethodSource): MethodVisibility {
    const privateMethod: boolean = TsSyntax.hasModifier(method, ts.SyntaxKind.PrivateKeyword)
    const protectedMethod: boolean = TsSyntax.hasModifier(method, ts.SyntaxKind.ProtectedKeyword)
    const publicMethod: boolean = TsSyntax.hasModifier(method, ts.SyntaxKind.PublicKeyword)
    const allowed: readonly ts.SyntaxKind[] = [
      ts.SyntaxKind.PrivateKeyword,
      ts.SyntaxKind.ProtectedKeyword,
      ts.SyntaxKind.PublicKeyword,
      ts.SyntaxKind.AsyncKeyword,
    ]
    if (
      [privateMethod, protectedMethod, publicMethod].filter((value: boolean): boolean => value)
        .length !== 1 ||
      TsSyntax.modifiers(method).some(
        (modifier: ts.Modifier): boolean => !allowed.includes(modifier.kind),
      )
    )
      throw new Error('脚本修饰符不属于允许集合')
    if (meta.group.kind === 'event') {
      if (!privateMethod) throw new Error('事件必须是 private')
      return { binding: meta.binding, isPrivate: meta.ast.header.isPrivate }
    }
    if (privateMethod) throw new Error('自定义脚本只能是 public 或 protected')
    const original: boolean = meta.ast.header.isPrivate || meta.binding.isPrivate
    if (original === protectedMethod)
      return { binding: meta.binding, isPrivate: meta.ast.header.isPrivate }
    if (meta.binding.origin === 'inline')
      return { binding: meta.binding, isPrivate: protectedMethod }
    if (!protectedMethod && meta.ast.header.isPrivate)
      throw new Error('源码 private 不能通过公开仓库引用绕过')
    return {
      binding: { ...meta.binding, isPrivate: protectedMethod },
      isPrivate: meta.ast.header.isPrivate,
    }
  }

  /** @anchor bescript-ts-methods */
  private static parameters(
    ast: LocalAst,
    group: ScriptGroup<DeviceFunction>,
    basis: ConversionBasis,
  ): readonly ScriptParameter[] {
    if (group.kind === 'custom') return ast.header.parameters
    return Option.getOrThrow(
      Option.fromNullishOr(
        basis.data.events.find((event: PlatformEvent): boolean =>
          Option.contains(event.name)(group.eventName),
        ),
      ),
    ).parameters
  }

  /** @anchor bescript-ts-methods */
  private static association(
    item: DeviceFunction,
    group: ScriptGroup<DeviceFunction>,
    basis: ConversionBasis,
  ): ScriptAssociation {
    const matches: ScriptAssociation[] = basis.data.scripts.filter(
      (script: ScriptAssociation): boolean =>
        MethodMapping.sameGroup(script, group) && script.bindingName === item.binding.name,
    )
    if (matches.length !== 1) throw new Error('脚本来源关联必须唯一: ' + item.binding.name)
    const association: ScriptAssociation = Option.getOrThrow(Option.fromNullishOr(matches[0]))
    if (
      !Option.makeEquivalence<string>((left: string, right: string): boolean => left === right)(
        association.guid,
        item.binding.guid,
      )
    )
      throw new Error('仓库 GUID 与关联依据不一致')
    basis.identifier(association.tsName)
    return association
  }

  /**
   * @anchor bescript-ts-methods
   * @fallback 异步方法没有泛型返回类型数组时按空数组校验 Promise 合同。
   * @fallback 没有脚本返回参数时校验 void 返回类型。
   */
  private static checkReturnType(
    method: ts.MethodDeclaration,
    returned: Option.Option<ScriptParameter>,
    basis: ConversionBasis,
  ): void {
    let type: ts.TypeNode = Option.getOrThrow(Option.fromNullishOr(method.type))
    if (TsSyntax.hasModifier(method, ts.SyntaxKind.AsyncKeyword)) {
      if (
        !ts.isTypeReferenceNode(type) ||
        !ts.isIdentifier(type.typeName) ||
        type.typeName.text !== 'Promise'
      )
        throw new Error('异步方法必须返回 Promise')
      const argumentsList: readonly ts.TypeNode[] = Option.getOrElse(
        Option.fromNullishOr(type.typeArguments),
        (): readonly ts.TypeNode[] => [],
      )
      if (argumentsList.length !== 1) throw new Error('Promise 必须携带唯一返回类型')
      type = Option.getOrThrow(Option.fromNullishOr(argumentsList[0]))
    }
    if (
      Option.isSome(returned)
        ? basis.fromType(type) !== returned.value.typeName
        : type.kind !== ts.SyntaxKind.VoidKeyword
    )
      throw new Error('方法返回类型与脚本头不一致')
  }

  private static hasAwait(node: ts.Node): boolean {
    if (ts.isAwaitExpression(node)) return true
    let result: boolean = false
    ts.forEachChild(node, (child: ts.Node): void => {
      if (MethodMapping.hasAwait(child)) result = true
    })
    return result
  }
}
