import { Option } from 'effect'
import ts from 'typescript'
import { z } from 'zod'
import {
  DeviceContextSchema,
  type BescriptDeviceContext,
  type DeviceFunction,
} from '../device_context/DeviceContext.ts'
import { DeviceMetadataSchema, type DeviceMetadata } from '../device_context/DeviceMetadata.ts'
import { GlobalVariableSchema, type GlobalVariable } from '../device_context/GlobalVariables.ts'
import type { ScriptGroup } from '../device_context/ScriptList.ts'
import type { LocalAstNode } from '../local_ast/LocalAst.ts'
import { BescriptVocabulary, type VarTypeName } from '../vocabulary/Vocabulary.ts'
import {
  ConversionBasis,
  type ConversionBasisData,
  type ActTarget,
  type TargetMethod,
  type PlatformMethod,
  type ParameterSemantics,
} from './ConversionBasis.ts'
import { SourceComments } from './SourceComments.ts'
import { VariableMapping } from './VariableMapping.ts'
import { StatementMapping } from './StatementMapping.ts'
import { TsSyntax } from './TsSyntax.ts'
import { NativeInlineCanvas, type InlineCanvasCodec } from './ConstantMapping.ts'
import { CallMapping } from './CallMapping.ts'
import { MethodMapping, type MethodGroup, type MethodSource } from './MethodMapping.ts'

/** @anchor bescript-ts-device */
export interface DeviceConversionBasis {
  readonly identity: ConversionBasisData['identity']
  readonly scripts: ConversionBasisData['scripts']
  readonly targets: ConversionBasisData['targets']
  readonly types: ConversionBasisData['types']
  readonly methods: ConversionBasisData['methods']
  readonly events: ConversionBasisData['events']
  readonly enums: ConversionBasisData['enums']
  readonly identifiers: ConversionBasisData['identifiers']
}

/** @anchor bescript-ts-constants */
export interface InlineCanvasTemplate {
  readonly html: string
  readonly nodeMode: boolean
}

/** @anchor bescript-ts-device */
interface DeviceSource {
  readonly metadata: DeviceMetadata
  readonly groups: readonly MethodGroup[]
}

interface GlobalSource {
  readonly comment: Option.Option<string>
}

/**
 * @anchor bescript-ts-device
 * @anchor bescript-second-language-boundary
 */
export class DeviceTsConverter {
  private static readonly deviceSource: z.ZodType<DeviceSource> = z.strictObject({
    metadata: DeviceMetadataSchema.schema,
    groups: z.array(MethodMapping.group),
  })
  /** @anchor bescript-ts-device */
  private constructor() {}

  /** @anchor bescript-ts-constants */
  public static canvasSources(
    context: BescriptDeviceContext,
    basis: DeviceConversionBasis,
  ): readonly string[] {
    if (
      !basis.methods.some((method: PlatformMethod): boolean =>
        method.parameters.some(
          (parameter: ParameterSemantics): boolean => parameter.constant === 'canvas',
        ),
      )
    )
      return []
    const lookup: ConversionBasis = DeviceTsConverter.resolve(context, basis)
    const calls: CallMapping = new CallMapping(
      lookup,
      new VariableMapping(lookup, context.globalVariables, []),
      ts.createSourceFile('view.ts', '', ts.ScriptTarget.Latest),
      new NativeInlineCanvas(),
    )
    const nodes: LocalAstNode[] = context.scriptGroups.flatMap(
      (group: ScriptGroup<DeviceFunction>): readonly LocalAstNode[] =>
        group.entries.flatMap((entry: DeviceFunction): readonly LocalAstNode[] => entry.ast.nodes),
    )
    const sources: Set<string> = new Set()
    for (const node of nodes) {
      nodes.push(...node.children)
      if (Option.isNone(node.call) || node.kind === 'break' || node.kind === 'continue') continue
      const parameters: readonly ParameterSemantics[] = calls.signature(node.call.value).parameters
      for (const [index, argument] of node.call.value.arguments.entries()) {
        const semantic: ParameterSemantics = Option.getOrThrow(
          Option.fromNullishOr(parameters[index]),
        )
        if (
          semantic.constant === 'canvas' &&
          !argument.variable &&
          (argument.decodedText.startsWith('canvas:') ||
            argument.decodedText.startsWith('==hjson:'))
        )
          sources.add(argument.decodedText)
      }
    }
    return [...sources]
  }

  /** @anchor bescript-ts-constants */
  public static canvasTemplates(
    sourceFile: ts.SourceFile,
    basis: DeviceConversionBasis,
  ): readonly InlineCanvasTemplate[] {
    const lookup: ConversionBasis = ConversionBasis.create(basis)
    const names: Set<string> = new Set(
      basis.methods.flatMap((method: PlatformMethod): readonly string[] =>
        method.parameters
          .filter((parameter: ParameterSemantics): boolean => parameter.constant === 'canvas')
          .map((parameter: ParameterSemantics): string => lookup.type(parameter.valueType)),
      ),
    )
    const templates: Map<string, InlineCanvasTemplate> = new Map()
    if (names.size === 0) return []
    const nodes: ts.Node[] = [sourceFile]
    for (const node of nodes) {
      ts.forEachChild(node, (child: ts.Node): void => {
        nodes.push(child)
      })
      if (ts.isEmptyStatement(node))
        nodes.push(...Option.toArray(StatementMapping.disabledSyntax(node, node.getSourceFile())))
      if (!ts.isCallExpression(node) || !ts.isPropertyAccessExpression(node.expression)) continue
      const expression: ts.PropertyAccessExpression = node.expression
      if (!ts.isIdentifier(expression.expression) || !names.has(expression.expression.text))
        continue
      if (expression.name.text !== 'fromBase64Const' && expression.name.text !== 'fromNodeConst')
        continue
      const nodeMode: boolean = expression.name.text === 'fromNodeConst'
      const template: ts.Expression = Option.getOrThrow(
        Option.fromNullishOr(node.arguments[nodeMode ? 1 : 0]),
      )
      if (!ts.isNoSubstitutionTemplateLiteral(template))
        throw new Error('Canvas 只能使用无插值 HTML 模板')
      const entry: InlineCanvasTemplate = { html: template.text, nodeMode }
      templates.set(JSON.stringify([nodeMode, template.text]), entry)
    }
    return [...templates.values()]
  }

  /**
   * @anchor bescript-ts-device
   * @anchor bescript-conversion-boundary
   * @fallback 有机械核心时使用核心身份生成实现 class 名。
   * @fallback 没有机械核心时使用零件身份生成实现 class 名。
   */
  public static toTs(
    context: BescriptDeviceContext,
    basis: DeviceConversionBasis,
    canvas: InlineCanvasCodec = new NativeInlineCanvas(),
  ): ts.SourceFile {
    DeviceContextSchema.parse(context)
    return DeviceTsConverter.toTsWithReusedMethods(
      context,
      basis,
      DeviceTsConverter.resolve(context, basis),
      new Map(),
      canvas,
    )
  }

  /**
   * @anchor bescript-ts-device
   * @fallback 命中上一轮同名方法时复用官方方法节点。
   * @fallback 有机械核心时生成空核心 class。
   * @fallback 没有机械核心时生成零件 class。
   */
  public static toTsWithReusedMethods(
    context: BescriptDeviceContext,
    basis: DeviceConversionBasis,
    resolved: ConversionBasis,
    reusedMethods: ReadonlyMap<string, ts.MethodDeclaration>,
    canvas: InlineCanvasCodec = new NativeInlineCanvas(),
  ): ts.SourceFile {
    const sourceFile: ts.SourceFile = ts.createSourceFile(
      'view.ts',
      '',
      ts.ScriptTarget.Latest,
      false,
      ts.ScriptKind.TS,
    )
    const members: ts.ClassElement[] = context.globalVariables.map(
      (variable: GlobalVariable): ts.PropertyDeclaration =>
        DeviceTsConverter.global(variable, resolved),
    )
    for (const group of context.scriptGroups) {
      for (const item of group.entries) {
        const reused: Option.Option<ts.MethodDeclaration> = Option.fromNullishOr(
          reusedMethods.get(JSON.stringify([group.kind, group.eventName, item.binding.name])),
        )
        members.push(
          Option.isSome(reused)
            ? reused.value
            : MethodMapping.toTs(
                item,
                group,
                context.globalVariables,
                resolved,
                sourceFile,
                canvas,
              ),
        )
      }
    }
    const identity: DeviceConversionBasis['identity'] = basis.identity
    const classes: ts.ClassDeclaration[] = []
    const core: DeviceConversionBasis['identity']['internalCore'] = identity.internalCore
    if (Option.isSome(core))
      classes.push(
        DeviceTsConverter.classDeclaration(identity.className, identity.baseClassName, [], true),
      )
    const declaration: ts.ClassDeclaration = DeviceTsConverter.classDeclaration(
      Option.isSome(core) ? core.value.className : identity.className,
      Option.isSome(core) ? core.value.baseClassName : identity.baseClassName,
      members,
      Option.isNone(core),
    )
    classes.push(
      SourceComments.attach(declaration, 'device', {
        metadata: context.metadata,
        groups: context.scriptGroups.map((group: ScriptGroup<DeviceFunction>): MethodGroup => ({
          kind: group.kind,
          eventName: group.eventName,
        })),
      }),
    )
    return ts.factory.updateSourceFile(sourceFile, [
      ts.factory.createModuleDeclaration(
        undefined,
        ts.factory.createIdentifier(resolved.identifier(identity.namespaceName)),
        ts.factory.createModuleBlock(classes),
        ts.NodeFlags.Namespace,
      ),
    ])
  }

  /**
   * @anchor bescript-ts-device
   * @anchor bescript-conversion-boundary
   * @fallback 官方 namespace 语句没有首项时按结构错误暴露。
   * @fallback 有机械核心时按两 class 结构读取，其他情况按单 class 结构读取。
   * @fallback 有机械核心时从核心身份读取 class 名和基类名。
   * @fallback 没有机械核心时从零件身份读取 class 名和基类名。
   */
  public static fromTs(
    sourceFile: ts.SourceFile,
    basis: DeviceConversionBasis,
    canvas: InlineCanvasCodec = new NativeInlineCanvas(),
  ): BescriptDeviceContext {
    let lookup: ConversionBasis = ConversionBasis.create(basis)
    if (sourceFile.statements.length !== 1) throw new Error('零件视图必须只有一个 namespace')
    const namespace: ts.Statement = Option.getOrThrow(
      Option.fromNullishOr(sourceFile.statements[0]),
    )
    if (
      !ts.isModuleDeclaration(namespace) ||
      !ts.isIdentifier(namespace.name) ||
      namespace.name.text !== basis.identity.namespaceName ||
      Option.isNone(Option.fromNullishOr(namespace.body))
    )
      throw new Error('零件 namespace 与转换依据不一致')
    const body: ts.ModuleBody = Option.getOrThrow(Option.fromNullishOr(namespace.body))
    if (!ts.isModuleBlock(body)) throw new Error('零件 namespace 必须使用块')
    const core: DeviceConversionBasis['identity']['internalCore'] = basis.identity.internalCore
    if (body.statements.length !== (Option.isSome(core) ? 2 : 1))
      throw new Error('零件 class 数量不一致')
    if (Option.isSome(core)) {
      const external: ts.Statement = Option.getOrThrow(Option.fromNullishOr(body.statements[0]))
      if (!ts.isClassDeclaration(external) || external.members.length !== 0)
        throw new Error('机械产生器外部入口必须为空 class')
      DeviceTsConverter.checkClass(
        external,
        basis.identity.className,
        basis.identity.baseClassName,
        true,
      )
    }
    const declaration: ts.Statement = Option.getOrThrow(
      Option.fromNullishOr(body.statements[Option.isSome(core) ? 1 : 0]),
    )
    if (!ts.isClassDeclaration(declaration)) throw new Error('零件视图必须是 class')
    DeviceTsConverter.checkClass(
      declaration,
      Option.isSome(core) ? core.value.className : basis.identity.className,
      Option.isSome(core) ? core.value.baseClassName : basis.identity.baseClassName,
      Option.isNone(core),
    )
    const source: DeviceSource = Option.getOrThrow(
      SourceComments.read(declaration, sourceFile, 'device', DeviceTsConverter.deviceSource),
    )
    const globals: GlobalVariable[] = []
    const methods: ts.MethodDeclaration[] = []
    for (const member of declaration.members) {
      if (ts.isPropertyDeclaration(member))
        globals.push(DeviceTsConverter.readGlobal(member, lookup, sourceFile))
      else if (ts.isMethodDeclaration(member)) methods.push(member)
      else throw new Error('零件 class 只能有全局字段和脚本方法')
    }
    GlobalVariableSchema.parse(globals)
    lookup = MethodMapping.targets(methods, source.metadata, basis, sourceFile)
    const groups: ScriptGroup<DeviceFunction>[] = source.groups.map(
      (group: DeviceSource['groups'][number]): ScriptGroup<DeviceFunction> => ({
        ...group,
        entries: [],
      }),
    )
    const used: Set<string> = new Set()
    for (const method of methods) {
      const meta: MethodSource = MethodMapping.source(method, sourceFile)
      const index: number = groups.findIndex((group: ScriptGroup<DeviceFunction>): boolean =>
        MethodMapping.sameGroup(group, meta.group),
      )
      if (index < 0) throw new Error('脚本方法引用了不存在的组')
      const key: string = JSON.stringify([
        meta.group.kind,
        meta.group.eventName,
        meta.scriptRelativePath,
        meta.binding.name,
      ])
      if (used.has(key)) throw new Error('方法来源重复')
      used.add(key)
      const group: ScriptGroup<DeviceFunction> = Option.getOrThrow(
        Option.fromNullishOr(groups[index]),
      )
      groups[index] = {
        ...group,
        entries: [
          ...group.entries,
          MethodMapping.fromTs(method, meta, globals, lookup, sourceFile, canvas),
        ],
      }
    }
    return DeviceContextSchema.parse({
      metadata: source.metadata,
      globalVariables: globals,
      scriptGroups: groups,
    })
  }

  private static classDeclaration(
    name: string,
    base: string,
    members: readonly ts.ClassElement[],
    exported: boolean,
  ): ts.ClassDeclaration {
    return ts.factory.createClassDeclaration(
      exported ? [ts.factory.createModifier(ts.SyntaxKind.ExportKeyword)] : undefined,
      name,
      undefined,
      [
        ts.factory.createHeritageClause(ts.SyntaxKind.ExtendsKeyword, [
          ts.factory.createExpressionWithTypeArguments(TsSyntax.path(base), undefined),
        ]),
      ],
      members,
    )
  }

  private static global(variable: GlobalVariable, basis: ConversionBasis): ts.PropertyDeclaration {
    const type: string = basis.type(variable.typeName)
    const property: ts.PropertyDeclaration = ts.factory.createPropertyDeclaration(
      [ts.factory.createModifier(ts.SyntaxKind.PrivateKeyword)],
      ts.factory.createStringLiteral(variable.name),
      undefined,
      ts.factory.createTypeReferenceNode(type),
      ts.factory.createCallExpression(TsSyntax.path(type + '.GlobalVarInitialize'), undefined, []),
    )
    return SourceComments.attach(property, 'global', { comment: variable.comment })
  }

  private static readGlobal(
    property: ts.PropertyDeclaration,
    basis: ConversionBasis,
    sourceFile: ts.SourceFile,
  ): GlobalVariable {
    if (
      TsSyntax.modifiers(property).length !== 1 ||
      !TsSyntax.hasModifier(property, ts.SyntaxKind.PrivateKeyword)
    )
      throw new Error('全局字段必须是 private')
    const type: VarTypeName = basis.fromType(Option.getOrThrow(Option.fromNullishOr(property.type)))
    const initializer: ts.CallExpression = TsSyntax.call(
      Option.getOrThrow(Option.fromNullishOr(property.initializer)),
    )
    if (
      initializer.arguments.length !== 0 ||
      Option.isSome(Option.fromNullishOr(initializer.typeArguments)) ||
      TsSyntax.pathName(initializer.expression) !== basis.type(type) + '.GlobalVarInitialize'
    )
      throw new Error('全局变量只允许固定默认初始化')
    const meta: Option.Option<GlobalSource> = SourceComments.read(
      property,
      sourceFile,
      'global',
      z.strictObject({ comment: BescriptVocabulary.option(z.string()) }),
    )
    return {
      name: TsSyntax.name(property.name),
      typeName: BescriptVocabulary.globalVarType.parse(type),
      comment: Option.isSome(meta) ? meta.value.comment : Option.none(),
    }
  }

  /**
   * @anchor bescript-ts-device
   * @fallback 官方 class 没有 heritage 集合时按空集合校验继承合同。
   */
  private static checkClass(
    declaration: ts.ClassDeclaration,
    name: string,
    base: string,
    exported: boolean,
  ): void {
    if (
      Option.isNone(Option.fromNullishOr(declaration.name)) ||
      Option.getOrThrow(Option.fromNullishOr(declaration.name)).text !== name ||
      TsSyntax.hasModifier(declaration, ts.SyntaxKind.ExportKeyword) !== exported ||
      TsSyntax.modifiers(declaration).length !== (exported ? 1 : 0) ||
      Option.isSome(Option.fromNullishOr(declaration.typeParameters))
    )
      throw new Error('class 身份或修饰符不能改变')
    const heritage: readonly ts.HeritageClause[] = Option.getOrElse(
      Option.fromNullishOr(declaration.heritageClauses),
      (): readonly ts.HeritageClause[] => [],
    )
    const clause: ts.HeritageClause = Option.getOrThrow(Option.fromNullishOr(heritage[0]))
    if (
      heritage.length !== 1 ||
      clause.token !== ts.SyntaxKind.ExtendsKeyword ||
      clause.types.length !== 1 ||
      TsSyntax.pathName(Option.getOrThrow(Option.fromNullishOr(clause.types[0])).expression) !==
        base
    )
      throw new Error('零件继承类型不能改变')
  }

  /**
   * @anchor bescript-ts-async-dependencies
   * @fallback 没有机械核心时使用零件身份的目标 ID。
   * @fallback 没有机械核心时使用零件身份的 class 名。
   */
  public static resolve(
    context: BescriptDeviceContext,
    basis: DeviceConversionBasis,
  ): ConversionBasis {
    const targets: ActTarget[] = [...ConversionBasis.currentTargets(context, basis)]
    let lookup: ConversionBasis = ConversionBasis.create({ ...basis, targets })
    const ownId: string = Option.match(basis.identity.internalCore, {
      onNone: (): string => basis.identity.id,
      onSome: (
        core: DeviceConversionBasis['identity']['internalCore'] extends Option.Option<infer Value>
          ? Value
          : never,
      ): string => core.id,
    })
    const ownClassName: string = Option.match(basis.identity.internalCore, {
      onNone: (): string => basis.identity.className,
      onSome: (
        core: DeviceConversionBasis['identity']['internalCore'] extends Option.Option<infer Value>
          ? Value
          : never,
      ): string => core.className,
    })
    const ownIndex: number = targets.findIndex(
      (target: ActTarget): boolean => target.id === ownId && target.className === ownClassName,
    )
    let changed: boolean = true
    while (changed) {
      changed = false
      const own: ActTarget = Option.getOrThrow(Option.fromNullishOr(targets[ownIndex]))
      const methods: TargetMethod[] = own.methods.map((method: TargetMethod): TargetMethod => {
        if (method.asynchronous) return method
        const item: DeviceFunction = Option.getOrThrow(
          Option.fromNullishOr(
            context.scriptGroups
              .filter((group: ScriptGroup<DeviceFunction>): boolean => group.kind === 'custom')
              .flatMap(
                (group: ScriptGroup<DeviceFunction>): readonly DeviceFunction[] => group.entries,
              )
              .find((entry: DeviceFunction): boolean => entry.binding.name === method.besName),
          ),
        )
        if (!DeviceTsConverter.asyncNodes(item.ast.nodes, lookup)) return method
        changed = true
        return { ...method, asynchronous: true }
      })
      targets[ownIndex] = { ...own, methods }
      lookup = ConversionBasis.create({ ...basis, targets })
    }
    return ConversionBasis.create({ ...basis, targets })
  }

  /**
   * @anchor bescript-ts-async-dependencies
   * @fallback act 调用没有自定义方法时使用平台方法的异步语义。
   */
  private static asyncNodes(nodes: readonly LocalAstNode[], basis: ConversionBasis): boolean {
    for (const node of nodes) {
      if (!node.disabled && (node.kind === 'delay' || node.kind === 'async')) return true
      if (
        !node.disabled &&
        Option.isSome(node.call) &&
        node.kind !== 'break' &&
        node.kind !== 'continue'
      ) {
        const call: import('../local_ast/LocalAst.ts').CallSignature = node.call.value
        const custom: Option.Option<TargetMethod> =
          call.kind === 'act'
            ? Option.fromNullishOr(
                basis
                  .target(call)
                  .methods.find(
                    (method: TargetMethod): boolean => method.besName === call.functionName,
                  ),
              )
            : Option.none()
        if (Option.isSome(custom)) {
          if (
            custom.value.asynchronous &&
            (Option.isSome(custom.value.returnType) || node.kind === 'if' || node.kind === 'while')
          )
            return true
        } else {
          const method: PlatformMethod = basis.method(call)
          if (
            method.asynchronous &&
            (Option.isSome(method.returnType) || node.kind === 'if' || node.kind === 'while')
          )
            return true
        }
      }
      if (DeviceTsConverter.asyncNodes(node.children, basis)) return true
    }
    return false
  }
}
