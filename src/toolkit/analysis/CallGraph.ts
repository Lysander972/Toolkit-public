import { Option, Result } from 'effect'
import type { CallArgument, CallKind, CallSignature, LocalAstNode } from '@toolkit/script/local_ast'
import type { LogicalCarrier, LogicalDevice } from '@toolkit/project-model/logical-entities'
import type { MethodAnalysis, SymbolMethod, SymbolTable } from './SymbolTable.ts'

export type CallbackTargetScope = 'caller' | 'visible'

/** @anchor analysis-call-input */
export interface PlatformCall {
  readonly kind: CallKind
  readonly ownerType: Option.Option<string>
  readonly methodName: string
}

/** @anchor analysis-call-input */
export interface CallbackRule extends PlatformCall {
  readonly argumentIndex: number
  readonly targetScope: CallbackTargetScope
}

/** @anchor analysis-call-input */
export interface CallGraphInput {
  readonly table: SymbolTable
  readonly platformMethods: readonly PlatformCall[]
  readonly callbackRules: readonly CallbackRule[]
}

/** @anchor analysis-call-graph */
export type CallEdgeKind = 'static' | 'potential-static' | 'dynamic'

/** @anchor analysis-call-graph */
export type CallDiagnosticKind =
  | 'missing-scope'
  | 'missing-target'
  | 'ambiguous-target'
  | 'missing-method'
  | 'missing-source'
  | 'ambiguous-method'
  | 'private-target'
  | 'dynamic-target'
  | 'invalid-callback'
  | 'unknown-platform'

/** @anchor analysis-call-graph */
export interface CallLocation {
  readonly relativePath: string
  readonly line: number
  readonly sourceRef: Option.Option<number>
}

/** @anchor analysis-call-graph */
export interface CallEdge {
  readonly from: string
  readonly to: string
  readonly kind: CallEdgeKind
  readonly location: CallLocation
  readonly call: CallSignature
}

/** @anchor analysis-call-graph */
export interface UnresolvedCall {
  readonly from: string
  readonly call: CallSignature
  readonly location: CallLocation
  readonly reason: CallDiagnosticKind
  readonly candidates: readonly string[]
}

/** @anchor analysis-call-graph */
export interface CallGraph {
  readonly table: SymbolTable
  readonly edges: readonly CallEdge[]
  readonly unresolved: readonly UnresolvedCall[]
}

/** @anchor analysis-call-graph */
interface CallSite {
  readonly method: SymbolMethod
  readonly device: LogicalDevice
  readonly call: CallSignature
  readonly location: CallLocation
}

/** @anchor analysis-input */
interface CallGraphContext extends CallGraphInput {
  readonly devicesByKey: ReadonlyMap<string, LogicalDevice>
  readonly devicesByCarrier: ReadonlyMap<string, readonly LogicalDevice[]>
  readonly carriersById: ReadonlyMap<string, LogicalCarrier>
  readonly mechsByGuid: ReadonlyMap<string, LogicalCarrier>
  readonly coreDevicesByCarrier: ReadonlyMap<string, readonly LogicalDevice[]>
  readonly methodsByDevice: ReadonlyMap<string, readonly SymbolMethod[]>
  readonly platformBySignature: ReadonlyMap<string, readonly PlatformCall[]>
  readonly callbacksBySignature: ReadonlyMap<string, readonly CallbackRule[]>
  readonly callsByAst: ReadonlyMap<MethodAnalysis['ast'], readonly LocalAstNode[]>
}

/** @anchor analysis-call-graph */
export class CallGraphBuilder {
  private constructor() {}

  /** @anchor analysis-call-graph */
  public static build(input: CallGraphInput): CallGraph {
    const devicesByCarrier: ReadonlyMap<string, readonly LogicalDevice[]> = Map.groupBy(
      input.table.devices,
      (device: LogicalDevice): string => device.parentEntityId,
    )
    const coreDevicesByCarrier: ReadonlyMap<string, readonly LogicalDevice[]> = new Map(
      input.table.carriers.map((carrier: LogicalCarrier): [string, readonly LogicalDevice[]] => [
        carrier.id,
        Option.getOrElse(
          Option.fromNullishOr(devicesByCarrier.get(carrier.id)),
          (): readonly LogicalDevice[] => [],
        ).filter(
          (device: LogicalDevice): boolean =>
            String(device.deviceType) ===
            Option.getOrThrow(Option.fromNullishOr(input.table.coreDeviceTypes.get(carrier.kind))),
        ),
      ]),
    )
    const callsByAst: Map<MethodAnalysis['ast'], readonly LocalAstNode[]> = new Map()
    for (const method of input.table.methods) {
      if (
        Option.isNone(method.analysis) ||
        method.analysis.value.ast.header.skip ||
        callsByAst.has(method.analysis.value.ast)
      )
        continue
      const calls: LocalAstNode[] = []
      const pending: LocalAstNode[] = [...method.analysis.value.ast.nodes].reverse()
      while (pending.length > 0) {
        const node: LocalAstNode = Option.getOrThrow(Option.fromNullishOr(pending.pop()))
        pending.push(...[...node.children].reverse())
        if (!node.disabled && Option.isSome(node.call)) calls.push(node)
      }
      callsByAst.set(method.analysis.value.ast, calls)
    }
    const context: CallGraphContext = {
      ...input,
      devicesByKey: new Map(
        input.table.devices.map((device: LogicalDevice): [string, LogicalDevice] => [
          device.id,
          device,
        ]),
      ),
      devicesByCarrier,
      carriersById: new Map(
        input.table.carriers.map((carrier: LogicalCarrier): [string, LogicalCarrier] => [
          carrier.id,
          carrier,
        ]),
      ),
      mechsByGuid: new Map(
        input.table.carriers
          .filter((carrier: LogicalCarrier): boolean => carrier.kind === 'mech')
          .map((carrier: LogicalCarrier): [string, LogicalCarrier] => [carrier.guid, carrier]),
      ),
      coreDevicesByCarrier,
      methodsByDevice: Map.groupBy(
        input.table.methods,
        (method: SymbolMethod): string => method.deviceKey,
      ),
      platformBySignature: Map.groupBy(input.platformMethods, (platform: PlatformCall): string =>
        CallGraphBuilder.platformSignatureKey(platform),
      ),
      callbacksBySignature: Map.groupBy(input.callbackRules, (rule: CallbackRule): string =>
        CallGraphBuilder.platformSignatureKey(rule),
      ),
      callsByAst,
    }
    const edges: CallEdge[] = []
    const unresolved: UnresolvedCall[] = []
    for (const method of input.table.methods) {
      if (Option.isNone(method.analysis) || method.analysis.value.ast.header.skip) continue
      const device: LogicalDevice = Option.getOrThrow(
        Option.fromNullishOr(context.devicesByKey.get(method.deviceKey)),
      )
      const nodes: readonly LocalAstNode[] = Option.getOrThrow(
        Option.fromNullishOr(context.callsByAst.get(method.analysis.value.ast)),
      )
      for (const node of nodes) {
        const site: CallSite = {
          method,
          device,
          call: Option.getOrThrow(node.call),
          location: {
            relativePath: method.analysis.value.sourceRelativePath,
            line: node.line,
            sourceRef: node.sourceRef,
          },
        }
        for (const resolution of CallGraphBuilder.resolve(site, context)) {
          if (Result.isFailure(resolution)) unresolved.push(resolution.failure)
          else edges.push(resolution.success)
        }
      }
    }
    return { table: input.table, edges, unresolved }
  }

  private static platformSignatureKey(call: PlatformCall): string {
    return JSON.stringify([call.kind, call.ownerType, call.methodName])
  }

  private static callSignatureKey(call: CallSignature): string {
    return JSON.stringify([call.kind, call.ownerType, call.functionName])
  }

  /** @anchor analysis-call-graph */
  private static diagnostic(
    site: CallSite,
    reason: CallDiagnosticKind,
    candidates: readonly string[],
  ): UnresolvedCall {
    return { from: site.method.key, call: site.call, location: site.location, reason, candidates }
  }

  /** @anchor analysis-call-graph */
  private static resolve(
    site: CallSite,
    input: CallGraphContext,
  ): readonly Result.Result<CallEdge, UnresolvedCall>[] {
    const platform: boolean = input.platformBySignature.has(
      CallGraphBuilder.callSignatureKey(site.call),
    )
    if (site.call.kind === 'act') {
      const custom: Option.Option<Result.Result<CallEdge, UnresolvedCall>> = CallGraphBuilder.act(
        site,
        input,
        platform,
      )
      if (Option.isSome(custom)) return [custom.value]
    }
    const callbacks: readonly CallbackRule[] = Option.getOrElse(
      Option.fromNullishOr(
        input.callbacksBySignature.get(CallGraphBuilder.callSignatureKey(site.call)),
      ),
      (): readonly CallbackRule[] => [],
    )
    return !platform && callbacks.length === 0
      ? [Result.fail(CallGraphBuilder.diagnostic(site, 'unknown-platform', []))]
      : callbacks.flatMap(
          (rule: CallbackRule): readonly Result.Result<CallEdge, UnresolvedCall>[] =>
            CallGraphBuilder.callback(site, rule, input),
        )
  }

  /**
   * @anchor analysis-call-scope
   * @fallback 没有导入关系时使用所属载体，显式导入缺失时返回诊断。
   */
  private static scope(
    site: CallSite,
    context: CallGraphContext,
  ): Result.Result<LogicalCarrier, UnresolvedCall> {
    const device: LogicalDevice = site.device
    const matchedCarrier: Option.Option<LogicalCarrier> = Option.isSome(device.fields.mechFileGuid)
      ? Option.fromNullishOr(context.mechsByGuid.get(device.fields.mechFileGuid.value))
      : Option.fromNullishOr(context.carriersById.get(device.parentEntityId))
    if (Option.isNone(matchedCarrier))
      return Result.fail(CallGraphBuilder.diagnostic(site, 'missing-scope', []))
    const carrier: LogicalCarrier = matchedCarrier.value
    if (Option.isSome(device.fields.mechFileGuid)) {
      const cores: readonly LogicalDevice[] = Option.getOrThrow(
        Option.fromNullishOr(context.coreDevicesByCarrier.get(carrier.id)),
      )
      if (cores.length !== 1)
        return Result.fail(
          CallGraphBuilder.diagnostic(
            site,
            cores.length === 0 ? 'missing-target' : 'ambiguous-target',
            cores.map((core: LogicalDevice): string => core.id),
          ),
        )
    }
    return Result.succeed(carrier)
  }

  /**
   * @anchor analysis-call-scope
   * @fallback 无导入关系的 this 指自身；显式导入的 this 指机械核心。
   */
  private static targets(
    site: CallSite,
    carrier: LogicalCarrier,
    context: CallGraphContext,
  ): readonly LogicalDevice[] {
    const device: LogicalDevice = site.device
    const coreDeviceType: string = Option.getOrThrow(
      Option.fromNullishOr(context.table.coreDeviceTypes.get(carrier.kind)),
    )
    const name: string = Option.getOrThrow(site.call.ownerName)
    const type: string = Option.getOrThrow(site.call.ownerType)
    const devices: readonly LogicalDevice[] =
      name === 'this' && !site.call.ownerNameIsAlias
        ? Option.isSome(device.fields.mechFileGuid)
          ? Option.getOrThrow(Option.fromNullishOr(context.coreDevicesByCarrier.get(carrier.id)))
          : [device]
        : Option.toArray(Option.fromNullishOr(context.devicesByCarrier.get(carrier.id))).flat()
    return devices.filter((candidate: LogicalDevice): boolean => {
      if (name === 'this' && !site.call.ownerNameIsAlias)
        return (
          String(candidate.deviceType) === type &&
          (Option.isSome(device.fields.mechFileGuid)
            ? String(candidate.deviceType) === coreDeviceType
            : candidate.id === device.id)
        )
      return (
        String(candidate.deviceType) === type &&
        (site.call.ownerNameIsAlias
          ? Option.contains(name)(candidate.fields.logicName)
          : BigInt(candidate.deviceId) === BigInt(name))
      )
    })
  }

  /**
   * @anchor analysis-call-graph
   * @fallback 目标无同名自定义绑定时才使用明确登记的平台方法；缺失或歧义保留诊断。
   */
  private static act(
    site: CallSite,
    context: CallGraphContext,
    platform: boolean,
  ): Option.Option<Result.Result<CallEdge, UnresolvedCall>> {
    const scope: Result.Result<LogicalCarrier, UnresolvedCall> = CallGraphBuilder.scope(
      site,
      context,
    )
    if (Result.isFailure(scope)) return Option.some(Result.fail(scope.failure))
    const targets: readonly LogicalDevice[] = CallGraphBuilder.targets(site, scope.success, context)
    if (targets.length !== 1)
      return Option.some(
        Result.fail(
          CallGraphBuilder.diagnostic(
            site,
            targets.length === 0 ? 'missing-target' : 'ambiguous-target',
            targets.map((target: LogicalDevice): string => target.id),
          ),
        ),
      )
    const target: LogicalDevice = Option.getOrThrow(Option.fromNullishOr(targets[0]))
    const caller: LogicalDevice = site.device
    const self: boolean =
      target.id === caller.id ||
      (Option.isSome(caller.fields.mechFileGuid) &&
        String(target.deviceType) ===
          Option.getOrThrow(
            Option.fromNullishOr(context.table.coreDeviceTypes.get(scope.success.kind)),
          ))
    const deviceKey: string = self ? caller.id : target.id
    const methods: readonly SymbolMethod[] = Option.toArray(
      Option.fromNullishOr(context.methodsByDevice.get(deviceKey)),
    )
      .flat()
      .filter(
        (method: SymbolMethod): boolean =>
          method.groupKind === 'custom' &&
          (self || Option.isNone(target.fields.mechFileGuid)) &&
          method.bindingName === site.call.functionName,
      )
    if (methods.length === 0 && platform) return Option.none()
    if (methods.length !== 1)
      return Option.some(
        Result.fail(
          CallGraphBuilder.diagnostic(
            site,
            methods.length === 0 ? 'missing-method' : 'ambiguous-method',
            methods.map((method: SymbolMethod): string => method.key),
          ),
        ),
      )
    const method: SymbolMethod = Option.getOrThrow(Option.fromNullishOr(methods[0]))
    if (
      !self &&
      (method.declaredPrivate ||
        Option.exists(
          method.analysis,
          (analysis: MethodAnalysis): boolean => analysis.effectivePrivate,
        ))
    )
      return Option.some(
        Result.fail(CallGraphBuilder.diagnostic(site, 'private-target', [method.key])),
      )
    if (Option.isNone(method.analysis))
      return Option.some(
        Result.fail(CallGraphBuilder.diagnostic(site, 'missing-source', [method.key])),
      )
    return Option.some(
      Result.succeed({
        from: site.method.key,
        to: method.key,
        kind: 'static',
        location: site.location,
        call: site.call,
      }),
    )
  }

  /**
   * @anchor analysis-callback
   * @fallback 零个回调候选报告 missing-method，多个常量候选报告 ambiguous-method，变量目标保留动态候选。
   */
  private static callback(
    site: CallSite,
    rule: CallbackRule,
    context: CallGraphContext,
  ): readonly Result.Result<CallEdge, UnresolvedCall>[] {
    const argument: Option.Option<CallArgument> = Option.fromNullishOr(
      site.call.arguments[rule.argumentIndex],
    )
    if (
      Option.isNone(argument) ||
      (!argument.value.variable && argument.value.typeName !== 'String')
    )
      return [Result.fail(CallGraphBuilder.diagnostic(site, 'invalid-callback', []))]
    const scope: Result.Result<LogicalCarrier, UnresolvedCall> = CallGraphBuilder.scope(
      site,
      context,
    )
    if (Result.isFailure(scope)) return [Result.fail(scope.failure)]
    const caller: LogicalDevice = site.device
    const coreDeviceType: string = Option.getOrThrow(
      Option.fromNullishOr(context.table.coreDeviceTypes.get(scope.success.kind)),
    )
    const methods: readonly SymbolMethod[] =
      rule.targetScope === 'caller'
        ? Option.toArray(Option.fromNullishOr(context.methodsByDevice.get(caller.id))).flat()
        : context.table.methods
    const candidates: readonly SymbolMethod[] = methods.filter((method: SymbolMethod): boolean => {
      const device: LogicalDevice = Option.getOrThrow(
        Option.fromNullishOr(context.devicesByKey.get(method.deviceKey)),
      )
      const self: boolean = method.deviceKey === caller.id
      const replacedCore: boolean =
        Option.isSome(caller.fields.mechFileGuid) &&
        device.parentEntityId === scope.success.id &&
        String(device.deviceType) === coreDeviceType
      return (
        method.groupKind === 'custom' &&
        (self ||
          (rule.targetScope === 'visible' &&
            device.parentEntityId === scope.success.id &&
            Option.isNone(device.fields.mechFileGuid) &&
            !method.declaredPrivate &&
            !Option.exists(
              method.analysis,
              (analysis: MethodAnalysis): boolean => analysis.effectivePrivate,
            ) &&
            !replacedCore)) &&
        (argument.value.variable || method.bindingName === argument.value.decodedText)
      )
    })
    const kind: CallEdgeKind =
      !argument.value.variable && candidates.length !== 1 ? 'potential-static' : 'dynamic'
    const resolutions: Result.Result<CallEdge, UnresolvedCall>[] = []
    const unavailable: string[] = []
    for (const method of candidates) {
      if (Option.isNone(method.analysis)) unavailable.push(method.key)
      else
        resolutions.push(
          Result.succeed({
            from: site.method.key,
            to: method.key,
            kind,
            location: site.location,
            call: site.call,
          }),
        )
    }
    if (argument.value.variable || candidates.length !== 1) {
      const reason: CallDiagnosticKind = argument.value.variable
        ? 'dynamic-target'
        : candidates.length === 0
          ? 'missing-method'
          : 'ambiguous-method'
      resolutions.push(
        Result.fail(
          CallGraphBuilder.diagnostic(
            site,
            reason,
            candidates.map((method: SymbolMethod): string => method.key),
          ),
        ),
      )
    }
    if (unavailable.length > 0)
      resolutions.push(
        Result.fail(CallGraphBuilder.diagnostic(site, 'missing-source', unavailable)),
      )
    return resolutions
  }
}
