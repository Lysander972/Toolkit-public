import { Option } from 'effect'
import {
  LogicalCarrier,
  LogicalDevice,
  LogicalInlineScript,
  LogicalScriptGroup,
  LogicalGuidAsset,
} from '@toolkit/project-model/logical-entities'
import type {
  LogicalEntity,
  LogicalEntitySnapshot,
  LogicalVariable,
} from '@toolkit/project-model/logical-entities'
import { LocalAstSchema, type LocalAst, type VariableBinding } from '@toolkit/script/local_ast'

export type AnalysisGroupKind = 'custom' | 'event'

/** @anchor analysis-input */
export interface AnalysisInput {
  readonly entities: LogicalEntitySnapshot
  readonly asts: ReadonlyMap<string, LocalAst>
  readonly coreDeviceTypes: ReadonlyMap<LogicalCarrier['kind'], string>
}

/** @anchor analysis-symbol-table */
export interface SymbolDiagnostic {
  readonly kind: 'missing-source'
  readonly key: string
  readonly candidates: readonly string[]
}

/** @anchor analysis-symbol-table */
export interface SymbolMethod {
  readonly key: string
  readonly deviceKey: string
  readonly groupKind: AnalysisGroupKind
  readonly eventName: Option.Option<string>
  readonly bindingName: string
  readonly declaredPrivate: boolean
  readonly analysis: Option.Option<MethodAnalysis>
}

/** @anchor analysis-symbol-table */
export interface MethodAnalysis {
  readonly sourceKey: string
  readonly sourceGuid: Option.Option<string>
  readonly sourceRelativePath: string
  readonly ast: LocalAst
  readonly effectivePrivate: boolean
  readonly localVariables: readonly VariableBinding[]
}

/** @anchor analysis-symbol-table */
export interface SymbolTable {
  readonly carriers: readonly LogicalCarrier[]
  readonly devices: readonly LogicalDevice[]
  readonly coreDeviceTypes: AnalysisInput['coreDeviceTypes']
  readonly globalVariables: ReadonlyMap<string, readonly LogicalVariable[]>
  readonly methods: readonly SymbolMethod[]
  readonly diagnostics: readonly SymbolDiagnostic[]
}

/** @anchor analysis-symbol-table */
export class SymbolTableBuilder {
  private constructor() {}

  /**
   * @anchor analysis-symbol-table
   * @fallback 实体层的 variables=None 表示零件没有 vars.json，全局变量表为空。
   * @fallback GUID 或内联路径没有来源时报告 missing-source。
   */
  public static build(input: AnalysisInput): SymbolTable {
    input.entities.requireUniqueGuids()
    const carriers: readonly LogicalCarrier[] = input.entities.entities.flatMap(
      (entity: LogicalEntity): LogicalCarrier[] =>
        entity instanceof LogicalCarrier ? [entity] : [],
    )
    const devices: readonly LogicalDevice[] = input.entities.entities.flatMap(
      (entity: LogicalEntity): LogicalDevice[] => (entity instanceof LogicalDevice ? [entity] : []),
    )
    const globalVariables: ReadonlyMap<string, readonly LogicalVariable[]> = new Map(
      devices.map((device: LogicalDevice): [string, readonly LogicalVariable[]] => [
        device.id,
        Option.getOrElse(device.variables, (): readonly LogicalVariable[] => []),
      ]),
    )
    const sources: ReadonlyMap<string, LogicalInlineScript> = new Map(
      input.entities.entities.flatMap((entity: LogicalEntity): [string, LogicalInlineScript][] =>
        entity instanceof LogicalInlineScript
          ? [[JSON.stringify([entity.parentEntityId, entity.primaryRelativePath]), entity]]
          : [],
      ),
    )
    const bindings: Map<LocalAst, readonly VariableBinding[]> = new Map()
    const globalsByDevice: ReadonlyMap<string, ReadonlySet<string>> = new Map(
      [...globalVariables].map(
        ([key, variables]: [string, readonly LogicalVariable[]]): [string, ReadonlySet<string>] => [
          key,
          new Set(variables.map((variable: LogicalVariable): string => variable.name)),
        ],
      ),
    )
    const diagnostics: SymbolDiagnostic[] = []
    const methods: SymbolMethod[] = []
    for (const group of input.entities.entities.flatMap(
      (entity: LogicalEntity): LogicalScriptGroup[] =>
        entity instanceof LogicalScriptGroup ? [entity] : [],
    )) {
      const globals: ReadonlySet<string> = Option.getOrThrow(
        Option.fromNullishOr(globalsByDevice.get(group.parentEntityId)),
      )
      for (const [index, binding] of group.entries.entries()) {
        const key: string = JSON.stringify([group.id, index])
        const method: SymbolMethod = {
          key,
          deviceKey: group.parentEntityId,
          groupKind: Option.isSome(group.eventName) ? 'event' : 'custom',
          eventName: group.eventName,
          bindingName: binding.name,
          declaredPrivate: binding.private,
          analysis: Option.none(),
        }
        const source: Option.Option<LogicalEntity> =
          binding.origin === 'repository'
            ? Option.flatMap(binding.guid, (guid: string): Option.Option<LogicalEntity> =>
                Option.fromNullishOr(
                  input.entities.query({
                    kind: Option.some('code-repo-script'),
                    primaryRelativePath: Option.none(),
                    parentId: Option.none(),
                    guid: Option.some(guid),
                  })[0],
                ),
              )
            : Option.flatMap(
                binding.relativePath,
                (relativePath: string): Option.Option<LogicalInlineScript> =>
                  Option.fromNullishOr(sources.get(JSON.stringify([group.id, relativePath]))),
              )
        if (Option.isNone(source)) {
          diagnostics.push({
            kind: 'missing-source',
            key,
            candidates: [],
          })
          methods.push(method)
          continue
        }
        const ast: Option.Option<LocalAst> = Option.fromNullishOr(
          input.asts.get(source.value.primaryRelativePath),
        )
        if (Option.isNone(ast)) {
          diagnostics.push({ kind: 'missing-source', key, candidates: [source.value.id] })
          methods.push(method)
          continue
        }
        if (!bindings.has(ast.value)) bindings.set(ast.value, LocalAstSchema.bindings(ast.value))
        methods.push({
          ...method,
          analysis: Option.some({
            sourceKey: source.value.id,
            sourceGuid:
              source.value instanceof LogicalGuidAsset
                ? Option.some(source.value.guid)
                : Option.none(),
            sourceRelativePath: source.value.primaryRelativePath,
            ast: ast.value,
            effectivePrivate:
              Option.isSome(group.eventName) || binding.private || ast.value.header.isPrivate,
            localVariables: Option.getOrThrow(Option.fromNullishOr(bindings.get(ast.value))).filter(
              (variable: VariableBinding): boolean => !globals.has(variable.name),
            ),
          }),
        })
      }
    }
    return {
      carriers,
      devices,
      coreDeviceTypes: input.coreDeviceTypes,
      globalVariables,
      methods,
      diagnostics,
    }
  }
}
