import assert from 'node:assert/strict'
import { Option } from 'effect'
import { test } from 'vitest'
import {
  LogicalRoot,
  LogicalCarrier,
  LogicalDevice,
  LogicalScriptGroup,
  LogicalInlineScript,
  LogicalGuidAsset,
  LogicalEntitySnapshot,
} from '../../../src/toolkit/project_model/LogicalEntities.ts'
import type {
  LogicalEntity,
  LogicalScriptEntry,
  LogicalVariable,
  EntityFileDeclaration,
  ScriptOrigin,
} from '../../../src/toolkit/project_model/LogicalEntities.ts'
import { BescriptConverter } from '../../../src/toolkit/script/conversion/BescriptConverter.ts'
import type { LocalAst } from '../../../src/toolkit/script/local_ast/LocalAst.ts'
import type { VariableBinding } from '../../../src/toolkit/script/local_ast/LocalAst.ts'
import { SymbolTableBuilder } from '../../../src/toolkit/analysis/SymbolTable.ts'
import type {
  AnalysisInput,
  SymbolTable,
  SymbolMethod,
  MethodAnalysis,
  SymbolDiagnostic,
} from '../../../src/toolkit/analysis/SymbolTable.ts'
import { CallGraphBuilder } from '../../../src/toolkit/analysis/CallGraph.ts'
import type {
  CallGraph,
  CallEdge,
  UnresolvedCall,
  CallbackRule,
  PlatformCall,
} from '../../../src/toolkit/analysis/CallGraph.ts'

/** @anchor analysis-input */
export class AnalysisFixture {
  private constructor() {}

  /** @anchor analysis-input */
  public static root(): LogicalRoot {
    return new LogicalRoot(
      { primaryRelativePath: '.', files: [], directories: [{ relativePath: '.', entries: [] }] },
      Option.none(),
    )
  }

  /** @anchor analysis-input */
  public static carrier(
    name: string,
    kind: 'map' | 'mech' = 'map',
    guid: string = 'a'.repeat(32),
  ): LogicalCarrier {
    const relativePath: string = name + '.' + kind
    return new LogicalCarrier({
      kind,
      primaryRelativePath: relativePath,
      parentId: 'root',
      guid,
      files: [
        { relativePath, revision: '1' },
        { relativePath: relativePath + '.guid', revision: '1' },
      ],
      directories: [{ relativePath: relativePath + '_', entries: [] }],
    })
  }

  /** @anchor analysis-input */
  public static device(
    carrier: LogicalCarrier,
    id: number,
    type: number = 54,
    variables: Option.Option<readonly LogicalVariable[]> = Option.some([]),
    name: Option.Option<string> = Option.none(),
    imported: Option.Option<string> = Option.none(),
  ): LogicalDevice {
    const relativePath: string = carrier.derivedDirectoryRelativePath + '/' + String(id)
    return new LogicalDevice({
      primaryRelativePath: relativePath,
      parentId: carrier.id,
      deviceId: id,
      deviceType: type,
      fields: { logicName: name, mechFileGuid: imported, stickToBone: Option.none() },
      variables,
      files: [],
      directories: [{ relativePath, entries: [] }],
    })
  }

  /** @anchor analysis-input */
  public static group(
    device: LogicalDevice,
    entries: readonly LogicalScriptEntry[],
    eventName: Option.Option<string> = Option.none(),
    name: string = 'scripts',
  ): LogicalScriptGroup {
    const relativePath: string = device.primaryRelativePath + '/' + name
    return new LogicalScriptGroup({
      primaryRelativePath: relativePath,
      parentId: device.id,
      entries,
      eventName,
      files: [],
      directories: [{ relativePath, entries: [] }],
    })
  }

  /** @anchor analysis-input */
  public static entry(
    name: string,
    relativePath: string,
    guid: Option.Option<string> = Option.none(),
    isPrivate: boolean = false,
  ): LogicalScriptEntry {
    return {
      name,
      origin: Option.isSome(guid) ? 'repository' : 'inline',
      guid,
      private: isPrivate,
      relativePath: Option.isSome(guid) ? Option.none() : Option.some(relativePath),
    }
  }

  /** @anchor analysis-input */
  public static inline(group: LogicalScriptGroup, name: string): LogicalInlineScript {
    const relativePath: string = group.primaryRelativePath + '/' + name + '.code'
    return new LogicalInlineScript({
      primaryRelativePath: relativePath,
      parentId: group.id,
      files: [{ relativePath, revision: '1' }],
      directories: [],
    })
  }

  /** @anchor analysis-input */
  public static repository(name: string, guid: string): LogicalGuidAsset {
    const relativePath: string = name + '.code'
    const files: readonly EntityFileDeclaration[] = [
      { relativePath, revision: '1' },
      { relativePath: relativePath + '.guid', revision: '1' },
    ]
    return new LogicalGuidAsset({
      kind: 'code-repo-script',
      primaryRelativePath: relativePath,
      parentId: 'root',
      guid,
      files,
      directories: [],
    })
  }

  /** @anchor analysis-input */
  public static ast(body: string = '', header: string = ''): LocalAst {
    return BescriptConverter.parse(`begincodeV2\nname:Source\n${header}lines:\n${body}endcode\n`)
  }

  /** @anchor analysis-input */
  public static input(
    entities: readonly LogicalEntity[],
    asts: ReadonlyMap<string, LocalAst>,
  ): AnalysisInput {
    return {
      entities: new LogicalEntitySnapshot([AnalysisFixture.root(), ...entities], []),
      asts,
      coreDeviceTypes: new Map([
        ['map', '0'],
        ['mech', '1'],
      ]),
    }
  }

  /** @anchor analysis-input */
  public static graph(
    input: AnalysisInput,
    platformMethods: readonly PlatformCall[] = [],
    callbackRules: readonly CallbackRule[] = [],
  ): CallGraph {
    return CallGraphBuilder.build({
      table: SymbolTableBuilder.build(input),
      platformMethods,
      callbackRules,
    })
  }

  /** @anchor analysis-symbol-table */
  public static method(table: SymbolTable, device: LogicalDevice, name: string): SymbolMethod {
    return Option.getOrThrow(
      Option.fromNullishOr(
        table.methods.find(
          (method: SymbolMethod): boolean =>
            method.deviceKey === device.id && method.bindingName === name,
        ),
      ),
    )
  }

  /** @anchor analysis-input */
  public static calls(
    body: string,
    names: readonly string[],
    eventName: Option.Option<string> = Option.none(),
    header: string = '',
  ): AnalysisInput {
    const carrier: LogicalCarrier = AnalysisFixture.carrier('scene')
    const device: LogicalDevice = AnalysisFixture.device(carrier, 1)
    const entries: readonly LogicalScriptEntry[] = names.map((name: string): LogicalScriptEntry =>
      AnalysisFixture.entry(name, `scene.map_/1/scripts/${name}.code`),
    )
    const group: LogicalScriptGroup = AnalysisFixture.group(device, entries, eventName)
    const scripts: readonly LogicalInlineScript[] = names.map((name: string): LogicalInlineScript =>
      AnalysisFixture.inline(group, name),
    )
    return AnalysisFixture.input(
      [carrier, device, group, ...scripts],
      new Map(
        scripts.map((script: LogicalInlineScript, index: number): [string, LocalAst] => [
          script.primaryRelativePath,
          AnalysisFixture.ast(index === 0 ? body : '', index === 0 ? header : ''),
        ]),
      ),
    )
  }
}

/** @anchor analysis-symbol-table */
export class SymbolTableTests {
  private constructor() {}

  static {
    test('未被脚本绑定引用的重复GUID也由实体查询阻止分析', (): void => {
      const carrier: LogicalCarrier = AnalysisFixture.carrier('scene')
      const device: LogicalDevice = AnalysisFixture.device(carrier, 1)
      const first: LogicalGuidAsset = AnalysisFixture.repository('first', 'b'.repeat(32))
      const second: LogicalGuidAsset = AnalysisFixture.repository('second', first.guid)
      const input: AnalysisInput = AnalysisFixture.input(
        [carrier, device, first, second],
        new Map(),
      )
      assert.throws((): SymbolTable => SymbolTableBuilder.build(input), /GUID 重复/)
    })

    test.each<ScriptOrigin>(['inline', 'repository'])(
      '%s 绑定缺失来源标识保留诊断，不选取任意源码',
      (origin: ScriptOrigin): void => {
        const carrier: LogicalCarrier = AnalysisFixture.carrier('scene')
        const device: LogicalDevice = AnalysisFixture.device(carrier, 1)
        const source: LogicalGuidAsset = AnalysisFixture.repository('available', 'b'.repeat(32))
        const group: LogicalScriptGroup = AnalysisFixture.group(device, [
          {
            name: 'Missing',
            origin,
            guid: Option.none(),
            private: false,
            relativePath: Option.none(),
          },
        ])
        const table: SymbolTable = SymbolTableBuilder.build(
          AnalysisFixture.input([carrier, device, group, source], new Map()),
        )
        assert.equal(table.methods.length, 1)
        assert.deepEqual(AnalysisFixture.method(table, device, 'Missing').analysis, Option.none())
        assert.deepEqual(table.diagnostics, [
          { kind: 'missing-source', key: JSON.stringify([group.id, 0]), candidates: [] },
        ])
      },
    )

    test('错误数据中的仓库和载体GUID重叠由实体查询明确报错', (): void => {
      const carrier: LogicalCarrier = AnalysisFixture.carrier('scene')
      const device: LogicalDevice = AnalysisFixture.device(carrier, 1)
      const source: LogicalGuidAsset = AnalysisFixture.repository('one', 'b'.repeat(32))
      const other: LogicalGuidAsset = AnalysisFixture.repository('two', source.guid)
      const group: LogicalScriptGroup = AnalysisFixture.group(device, [
        AnalysisFixture.entry('Call', source.primaryRelativePath, Option.some(source.guid)),
      ])
      const input: AnalysisInput = AnalysisFixture.input(
        [carrier, device, group, source, other],
        new Map([[source.primaryRelativePath, AnalysisFixture.ast()]]),
      )
      assert.throws(
        (): void => {
          SymbolTableBuilder.build(input)
        },
        { message: 'GUID 重复: ' + source.guid },
      )
      const invalidCarrier: LogicalCarrier = AnalysisFixture.carrier('other', 'mech', carrier.guid)
      assert.throws(
        (): void => {
          SymbolTableBuilder.build(AnalysisFixture.input([carrier, invalidCarrier], new Map()))
        },
        { message: 'GUID 重复: ' + carrier.guid },
      )
    })

    test('实体快照关联共享源码，全局表和方法局部表按零件分离且互斥', (): void => {
      const carrier: LogicalCarrier = AnalysisFixture.carrier('scene')
      const variable: LogicalVariable = { name: 'Global', typeName: 'Long', comment: Option.none() }
      const first: LogicalDevice = AnalysisFixture.device(carrier, 1, 54, Option.some([variable]))
      const second: LogicalDevice = AnalysisFixture.device(carrier, 2)
      const source: LogicalGuidAsset = AnalysisFixture.repository('shared', 'b'.repeat(32))
      const entries: readonly LogicalScriptEntry[] = [
        AnalysisFixture.entry('Alias', source.primaryRelativePath, Option.some(source.guid)),
      ]
      const firstGroup: LogicalScriptGroup = AnalysisFixture.group(first, entries)
      const secondGroup: LogicalScriptGroup = AnalysisFixture.group(second, entries)
      const inlineGroup: LogicalScriptGroup = AnalysisFixture.group(
        first,
        [AnalysisFixture.entry('Inline', 'scene.map_/1/inline/Inline.code')],
        Option.none(),
        'inline',
      )
      const inline: LogicalInlineScript = AnalysisFixture.inline(inlineGroup, 'Inline')
      const ast: LocalAst = AnalysisFixture.ast(
        'Long Global=fun value()\nLong Local=fun value()\nLong Local=fun value()\n',
        'params:Long Parameter\nreturn:Long Returned\n',
      )
      const input: AnalysisInput = AnalysisFixture.input(
        [carrier, first, second, source, firstGroup, secondGroup, inlineGroup, inline],
        new Map([
          [source.primaryRelativePath, ast],
          [inline.primaryRelativePath, ast],
        ]),
      )
      const before: string = JSON.stringify(input.entities)
      const table: SymbolTable = SymbolTableBuilder.build(input)
      const firstMethod: SymbolMethod = AnalysisFixture.method(table, first, 'Alias')
      const secondMethod: SymbolMethod = AnalysisFixture.method(table, second, 'Alias')
      const firstAnalysis: MethodAnalysis = Option.getOrThrow(firstMethod.analysis)
      const secondAnalysis: MethodAnalysis = Option.getOrThrow(secondMethod.analysis)
      assert.notEqual(firstMethod.key, secondMethod.key)
      assert.equal(firstAnalysis.sourceKey, source.id)
      assert.equal(secondAnalysis.sourceKey, source.id)
      assert.deepEqual(firstAnalysis.sourceGuid, Option.some(source.guid))
      const inlineMethod: SymbolMethod = AnalysisFixture.method(table, first, 'Inline')
      const inlineAnalysis: MethodAnalysis = Option.getOrThrow(inlineMethod.analysis)
      assert.equal(inlineAnalysis.sourceKey, inline.id)
      assert.deepEqual(inlineAnalysis.sourceGuid, Option.none())
      assert.equal(firstAnalysis.ast, secondAnalysis.ast)
      assert.deepEqual(
        firstAnalysis.localVariables.map((item: VariableBinding): string => item.name),
        ['Parameter', 'Local'],
      )
      assert.deepEqual(
        secondAnalysis.localVariables.map((item: VariableBinding): string => item.name),
        ['Parameter', 'Global', 'Local'],
      )
      assert.deepEqual(inlineAnalysis.localVariables, firstAnalysis.localVariables)
      assert.equal(table.carriers[0], carrier)
      assert.equal(table.devices[0], first)
      assert.deepEqual(table.globalVariables.get(first.id), [variable])
      assert.deepEqual(SymbolTableBuilder.build(input), table)
      assert.equal(JSON.stringify(input.entities), before)
      assert.equal(Object.isFrozen(ast), false)
    })

    test('variables 缺失对应空全局表', (): void => {
      const carrier: LogicalCarrier = AnalysisFixture.carrier('scene')
      const device: LogicalDevice = AnalysisFixture.device(carrier, 1, 54, Option.none())
      const table: SymbolTable = SymbolTableBuilder.build(
        AnalysisFixture.input([carrier, device], new Map()),
      )
      assert.deepEqual(table.globalVariables.get(device.id), [])
    })

    test('缺失仓库来源和第一层AST保留来源诊断', (): void => {
      const carrier: LogicalCarrier = AnalysisFixture.carrier('scene')
      const device: LogicalDevice = AnalysisFixture.device(carrier, 1)
      const source: LogicalGuidAsset = AnalysisFixture.repository('one', 'b'.repeat(32))
      const group: LogicalScriptGroup = AnalysisFixture.group(device, [
        AnalysisFixture.entry('Call', source.primaryRelativePath, Option.some(source.guid)),
      ])
      const absent: SymbolTable = SymbolTableBuilder.build(
        AnalysisFixture.input([carrier, device, group], new Map()),
      )
      assert.equal(absent.methods.length, 1)
      assert.deepEqual(AnalysisFixture.method(absent, device, 'Call').analysis, Option.none())
      assert.deepEqual(absent.diagnostics, [
        { kind: 'missing-source', key: JSON.stringify([group.id, 0]), candidates: [] },
      ])
      const missing: SymbolTable = SymbolTableBuilder.build(
        AnalysisFixture.input([carrier, device, group, source], new Map()),
      )
      assert.equal(missing.methods.length, 1)
      assert.deepEqual(AnalysisFixture.method(missing, device, 'Call').analysis, Option.none())
      assert.deepEqual(
        missing.diagnostics.map((item: SymbolDiagnostic): string => item.kind),
        ['missing-source'],
      )
    })
  }
}

/** @anchor analysis-call-graph @anchor analysis-call-scope */
export class CallGraphTests {
  private constructor() {}

  static {
    test('内联与仓库声明缺失源码或AST时阻止同名平台调用及回调', (): void => {
      const origins: readonly ScriptOrigin[] = ['inline', 'repository']
      const omissions: readonly ('source' | 'ast')[] = ['source', 'ast']
      for (const origin of origins) {
        for (const missing of omissions) {
          const carrier: LogicalCarrier = AnalysisFixture.carrier('scene')
          const device: LogicalDevice = AnalysisFixture.device(carrier, 1)
          const callerGroup: LogicalScriptGroup = AnalysisFixture.group(device, [
            AnalysisFixture.entry('Call', 'scene.map_/1/scripts/Call.code'),
          ])
          const caller: LogicalInlineScript = AnalysisFixture.inline(callerGroup, 'Call')
          const repository: LogicalGuidAsset = AnalysisFixture.repository('shared', 'b'.repeat(32))
          const targetGroup: LogicalScriptGroup = AnalysisFixture.group(
            device,
            [
              AnalysisFixture.entry(
                'Fire',
                'scene.map_/1/target/Fire.code',
                origin === 'repository' ? Option.some(repository.guid) : Option.none(),
              ),
            ],
            Option.none(),
            'target',
          )
          const target: LogicalEntity =
            origin === 'repository' ? repository : AnalysisFixture.inline(targetGroup, 'Fire')
          const input: AnalysisInput = AnalysisFixture.input(
            [
              carrier,
              device,
              callerGroup,
              caller,
              targetGroup,
              ...(missing === 'source' ? [] : [target]),
            ],
            new Map([
              [caller.primaryRelativePath, AnalysisFixture.ast('act 54.this.Fire(String Call)\n')],
            ]),
          )
          const graph: CallGraph = AnalysisFixture.graph(
            input,
            [{ kind: 'act', ownerType: Option.some('54'), methodName: 'Fire' }],
            [
              {
                kind: 'act',
                ownerType: Option.some('54'),
                methodName: 'Fire',
                argumentIndex: 0,
                targetScope: 'caller',
              },
            ],
          )
          assert.deepEqual(graph.edges, [])
          assert.deepEqual(
            graph.unresolved.map((call: UnresolvedCall): [string, readonly string[]] => [
              call.reason,
              call.candidates,
            ]),
            [['missing-source', [JSON.stringify([targetGroup.id, 0])]]],
          )
        }
      }
    })

    test('同名声明中缺失AST的绑定仍构成静态调用歧义', (): void => {
      const baseline: AnalysisInput = AnalysisFixture.calls('act 54.this.Fire()\n', [
        'Call',
        'Fire',
      ])
      const device: LogicalDevice = baseline.entities
        .entity('device:scene.map_/1')
        .require(LogicalDevice)
      const source: LogicalGuidAsset = AnalysisFixture.repository('missing', 'b'.repeat(32))
      const group: LogicalScriptGroup = AnalysisFixture.group(
        device,
        [AnalysisFixture.entry('Fire', source.primaryRelativePath, Option.some(source.guid))],
        Option.none(),
        'other',
      )
      const graph: CallGraph = AnalysisFixture.graph({
        ...baseline,
        entities: new LogicalEntitySnapshot([...baseline.entities.entities, source, group], []),
      })
      assert.deepEqual(graph.edges, [])
      assert.deepEqual(
        graph.unresolved.map((call: UnresolvedCall): [string, readonly string[]] => [
          call.reason,
          call.candidates,
        ]),
        [
          [
            'ambiguous-method',
            [
              JSON.stringify(['script-group:scene.map_/1/scripts', 1]),
              JSON.stringify([group.id, 0]),
            ],
          ],
        ],
      )
    })

    test('唯一缺失回调候选保留身份，补齐AST后重新计算且旧结果不变', (): void => {
      const baseline: AnalysisInput = AnalysisFixture.calls('fun register(String Fire)\n', [
        'Call',
        'Fire',
      ])
      const asts: Map<string, LocalAst> = new Map(baseline.asts)
      const sourceRelativePath: string = 'scene.map_/1/scripts/Fire.code'
      asts.delete(sourceRelativePath)
      const input: AnalysisInput = { ...baseline, asts }
      const rules: readonly CallbackRule[] = [
        {
          kind: 'fun',
          ownerType: Option.none(),
          methodName: 'register',
          argumentIndex: 0,
          targetScope: 'caller',
        },
      ]
      const graph: CallGraph = AnalysisFixture.graph(input, [], rules)
      const unavailable: string = JSON.stringify(['script-group:scene.map_/1/scripts', 1])
      assert.deepEqual(graph.edges, [])
      assert.deepEqual(
        graph.unresolved.map((call: UnresolvedCall): [string, readonly string[]] => [
          call.reason,
          call.candidates,
        ]),
        [['missing-source', [unavailable]]],
      )
      const before: string = JSON.stringify(graph)
      asts.set(sourceRelativePath, AnalysisFixture.ast())
      const resolved: CallGraph = AnalysisFixture.graph(input, [], rules)
      assert.deepEqual(resolved.unresolved, [])
      assert.deepEqual(
        resolved.edges.map((edge: CallEdge): [string, string] => [edge.to, edge.kind]),
        [[unavailable, 'dynamic']],
      )
      assert.equal(JSON.stringify(graph), before)
    })

    test('缺失分析的明确私有声明阻止外部访问，未知源码头保留未确认回调候选', (): void => {
      const baseline: AnalysisInput = AnalysisFixture.calls(
        'act 54.2.Secret()\nact 54.2.Unknown()\nact 54.2.Hidden()\nfun register(var callback)\n',
        ['Call'],
      )
      const carrier: LogicalCarrier = baseline.entities
        .entity('map:scene.map')
        .require(LogicalCarrier)
      const target: LogicalDevice = AnalysisFixture.device(carrier, 2)
      const group: LogicalScriptGroup = AnalysisFixture.group(target, [
        AnalysisFixture.entry('Secret', 'scene.map_/2/scripts/Secret.code', Option.none(), true),
        AnalysisFixture.entry('Unknown', 'scene.map_/2/scripts/Unknown.code'),
        AnalysisFixture.entry('Hidden', 'scene.map_/2/scripts/Hidden.code'),
      ])
      const scripts: readonly LogicalInlineScript[] = ['Secret', 'Unknown', 'Hidden'].map(
        (name: string): LogicalInlineScript => AnalysisFixture.inline(group, name),
      )
      const graph: CallGraph = AnalysisFixture.graph(
        {
          ...baseline,
          entities: new LogicalEntitySnapshot(
            [...baseline.entities.entities, target, group, ...scripts],
            [],
          ),
          asts: new Map([
            ...baseline.asts,
            ['scene.map_/2/scripts/Hidden.code', AnalysisFixture.ast('', 'private:1\n')],
          ]),
        },
        [{ kind: 'act', ownerType: Option.some('54'), methodName: 'Secret' }],
        [
          {
            kind: 'fun',
            ownerType: Option.none(),
            methodName: 'register',
            argumentIndex: 0,
            targetScope: 'visible',
          },
        ],
      )
      const caller: string = JSON.stringify(['script-group:scene.map_/1/scripts', 0])
      const secret: string = JSON.stringify([group.id, 0])
      const unknown: string = JSON.stringify([group.id, 1])
      const hidden: string = JSON.stringify([group.id, 2])
      assert.deepEqual(
        graph.edges.map((edge: CallEdge): string => edge.to),
        [caller],
      )
      assert.deepEqual(
        graph.unresolved.map((call: UnresolvedCall): [string, readonly string[]] => [
          call.reason,
          call.candidates,
        ]),
        [
          ['private-target', [secret]],
          ['missing-source', [unknown]],
          ['private-target', [hidden]],
          ['dynamic-target', [caller, unknown]],
          ['missing-source', [unknown]],
        ],
      )
    })

    test('回调保留缺失AST候选且仅向已解析绑定生成候选边', (): void => {
      const baseline: AnalysisInput = AnalysisFixture.calls(
        'fun register(String Fire)\nfun register(var callback)\n',
        ['Call', 'Fire'],
      )
      const device: LogicalDevice = baseline.entities
        .entity('device:scene.map_/1')
        .require(LogicalDevice)
      const source: LogicalGuidAsset = AnalysisFixture.repository('missing', 'b'.repeat(32))
      const group: LogicalScriptGroup = AnalysisFixture.group(
        device,
        [AnalysisFixture.entry('Fire', source.primaryRelativePath, Option.some(source.guid))],
        Option.none(),
        'other',
      )
      const graph: CallGraph = AnalysisFixture.graph(
        {
          ...baseline,
          entities: new LogicalEntitySnapshot([...baseline.entities.entities, source, group], []),
        },
        [],
        [
          {
            kind: 'fun',
            ownerType: Option.none(),
            methodName: 'register',
            argumentIndex: 0,
            targetScope: 'caller',
          },
        ],
      )
      const caller: string = AnalysisFixture.method(graph.table, device, 'Call').key
      const ready: string = AnalysisFixture.method(graph.table, device, 'Fire').key
      const unavailable: string = JSON.stringify([group.id, 0])
      assert.deepEqual(
        graph.edges.map((edge: CallEdge): [number, string, string] => [
          edge.location.line,
          edge.to,
          edge.kind,
        ]),
        [
          [1, ready, 'potential-static'],
          [2, caller, 'dynamic'],
          [2, ready, 'dynamic'],
        ],
      )
      assert.deepEqual(
        graph.unresolved.map((call: UnresolvedCall): [number, string, readonly string[]] => [
          call.location.line,
          call.reason,
          call.candidates,
        ]),
        [
          [1, 'ambiguous-method', [ready, unavailable]],
          [1, 'missing-source', [unavailable]],
          [2, 'dynamic-target', [caller, ready, unavailable]],
          [2, 'missing-source', [unavailable]],
        ],
      )
    })

    test('旧版延迟调用和动画事件通过显式参数语义生成回调边', (): void => {
      const input: AnalysisInput = AnalysisFixture.calls(
        'fun laterCall(String Fire:Long 1)\ndvf anim.Animator.setAnimEvent(String clip:Long 0:String Fire)\n',
        ['Call', 'Fire'],
      )
      const rules: readonly CallbackRule[] = [
        {
          kind: 'fun',
          ownerType: Option.none(),
          methodName: 'laterCall',
          argumentIndex: 0,
          targetScope: 'caller',
        },
        {
          kind: 'dvf',
          ownerType: Option.some('Animator'),
          methodName: 'setAnimEvent',
          argumentIndex: 2,
          targetScope: 'caller',
        },
      ]
      const table: SymbolTable = SymbolTableBuilder.build(input)
      const before: string = JSON.stringify(table)
      const graph: CallGraph = CallGraphBuilder.build({
        table,
        platformMethods: [],
        callbackRules: rules,
      })
      const target: SymbolMethod = Option.getOrThrow(Option.fromNullishOr(table.methods[1]))
      assert.deepEqual(
        graph.edges.map((edge: CallEdge): [string, string, number] => [
          edge.to,
          edge.kind,
          edge.location.line,
        ]),
        [
          [target.key, 'dynamic', 1],
          [target.key, 'dynamic', 2],
        ],
      )
      assert.deepEqual(graph.unresolved, [])
      assert.equal(JSON.stringify(table), before)
      assert.deepEqual(
        CallGraphBuilder.build({ table, platformMethods: [], callbackRules: rules }),
        graph,
      )
      const changed: CallGraph = CallGraphBuilder.build({
        table,
        platformMethods: [],
        callbackRules: [],
      })
      assert.deepEqual(changed.edges, [])
      assert.deepEqual(
        changed.unresolved.map((call: UnresolvedCall): string => call.reason),
        ['unknown-platform', 'unknown-platform'],
      )
    })

    test('逻辑名歧义保留同类型全部候选，数字ID和逻辑名遵循类型约束', (): void => {
      const baseline: AnalysisInput = AnalysisFixture.calls(
        'act 54./Target.Fire()\nact 55./Target.Fire()\nact 54.4.Fire()\nact 55.0004.Fire()\n',
        ['Call'],
      )
      const carrier: LogicalCarrier = baseline.entities
        .entity('map:scene.map')
        .require(LogicalCarrier)
      const first: LogicalDevice = AnalysisFixture.device(
        carrier,
        2,
        54,
        Option.some([]),
        Option.some('Target'),
      )
      const second: LogicalDevice = AnalysisFixture.device(
        carrier,
        3,
        54,
        Option.some([]),
        Option.some('Target'),
      )
      const third: LogicalDevice = AnalysisFixture.device(
        carrier,
        4,
        55,
        Option.some([]),
        Option.some('Target'),
      )
      const group: LogicalScriptGroup = AnalysisFixture.group(third, [
        AnalysisFixture.entry('Fire', 'scene.map_/4/scripts/Fire.code'),
      ])
      const source: LogicalInlineScript = AnalysisFixture.inline(group, 'Fire')
      const graph: CallGraph = AnalysisFixture.graph({
        ...baseline,
        entities: new LogicalEntitySnapshot(
          [...baseline.entities.entities, first, second, third, group, source],
          [],
        ),
        asts: new Map([...baseline.asts, [source.primaryRelativePath, AnalysisFixture.ast()]]),
      })
      assert.deepEqual(
        graph.unresolved.map((call: UnresolvedCall): [string, readonly string[]] => [
          call.reason,
          call.candidates,
        ]),
        [
          ['ambiguous-target', [first.id, second.id]],
          ['missing-target', []],
        ],
      )
      assert.equal(graph.edges.length, 2)
      assert.equal(
        Option.getOrThrow(Option.fromNullishOr(graph.edges[0])).to,
        AnalysisFixture.method(graph.table, third, 'Fire').key,
      )
    })

    test('实体绑定构成静态调用边，正文位置保持第一层来源', (): void => {
      const input: AnalysisInput = AnalysisFixture.calls(
        'act 54.this.Fire()\nact 54.9.Missing()\n',
        ['Call', 'Fire'],
      )
      const graph: CallGraph = AnalysisFixture.graph(input)
      assert.equal(graph.edges.length, 1)
      const edge: CallEdge = Option.getOrThrow(Option.fromNullishOr(graph.edges[0]))
      assert.equal(edge.to, Option.getOrThrow(Option.fromNullishOr(graph.table.methods[1])).key)
      assert.equal(edge.location.line, 1)
      assert.deepEqual(
        graph.unresolved.map((call: UnresolvedCall): string => call.reason),
        ['missing-target'],
      )
    })

    test('跨零件私有绑定和事件绑定不可作为普通方法调用，同名绑定保留歧义', (): void => {
      const carrier: LogicalCarrier = AnalysisFixture.carrier('scene')
      const caller: LogicalDevice = AnalysisFixture.device(carrier, 1)
      const target: LogicalDevice = AnalysisFixture.device(
        carrier,
        2,
        54,
        Option.some([]),
        Option.some('Target'),
      )
      const source: LogicalGuidAsset = AnalysisFixture.repository('shared', 'b'.repeat(32))
      const group: LogicalScriptGroup = AnalysisFixture.group(caller, [
        AnalysisFixture.entry('Call', source.primaryRelativePath, Option.some(source.guid)),
      ])
      const targetGroup: LogicalScriptGroup = AnalysisFixture.group(target, [
        AnalysisFixture.entry('Secret', source.primaryRelativePath, Option.some(source.guid), true),
        AnalysisFixture.entry('Fire', source.primaryRelativePath, Option.some(source.guid)),
        AnalysisFixture.entry('Fire', source.primaryRelativePath, Option.some(source.guid)),
      ])
      const event: LogicalScriptGroup = AnalysisFixture.group(
        target,
        [AnalysisFixture.entry('Event', source.primaryRelativePath, Option.some(source.guid))],
        Option.some('start'),
        'event',
      )
      const graph: CallGraph = AnalysisFixture.graph(
        AnalysisFixture.input(
          [carrier, caller, target, source, group, targetGroup, event],
          new Map([
            [
              source.primaryRelativePath,
              AnalysisFixture.ast('act 54./Target.Secret()\nact 54.2.Fire()\nact 54.2.Event()\n'),
            ],
          ]),
        ),
      )
      assert.deepEqual(
        graph.unresolved
          .filter(
            (call: UnresolvedCall): boolean =>
              call.from === Option.getOrThrow(Option.fromNullishOr(graph.table.methods[0])).key,
          )
          .map((call: UnresolvedCall): string => call.reason),
        ['private-target', 'ambiguous-method', 'missing-method'],
      )
    })

    test('导入机械的this使用调用者绑定闭包，机械核心物理绑定保持独立', (): void => {
      const map: LogicalCarrier = AnalysisFixture.carrier('scene')
      const mech: LogicalCarrier = AnalysisFixture.carrier('machine', 'mech', 'c'.repeat(32))
      const caller: LogicalDevice = AnalysisFixture.device(
        map,
        1,
        1110,
        Option.some([]),
        Option.none(),
        Option.some(mech.guid),
      )
      const core: LogicalDevice = AnalysisFixture.device(mech, 0, 1)
      const source: LogicalGuidAsset = AnalysisFixture.repository('shared', 'b'.repeat(32))
      const entries: readonly LogicalScriptEntry[] = [
        AnalysisFixture.entry('Fire', source.primaryRelativePath, Option.some(source.guid), true),
      ]
      const callerGroup: LogicalScriptGroup = AnalysisFixture.group(caller, entries)
      const coreGroup: LogicalScriptGroup = AnalysisFixture.group(core, entries)
      const input: AnalysisInput = AnalysisFixture.input(
        [map, mech, caller, core, source, callerGroup, coreGroup],
        new Map([[source.primaryRelativePath, AnalysisFixture.ast('act 1.this.Fire()\n')]]),
      )
      const graph: CallGraph = AnalysisFixture.graph(input)
      const method: SymbolMethod = AnalysisFixture.method(graph.table, caller, 'Fire')
      assert.ok(
        graph.edges.some(
          (edge: CallEdge): boolean => edge.from === method.key && edge.to === method.key,
        ),
      )
      const missing: CallGraph = AnalysisFixture.graph({
        ...input,
        entities: new LogicalEntitySnapshot(
          input.entities.entities.filter(
            (entity: LogicalEntity): boolean => entity.id !== core.id && entity.id !== coreGroup.id,
          ),
          [],
        ),
      })
      assert.deepEqual(missing.edges, [])
      assert.equal(
        Option.getOrThrow(Option.fromNullishOr(missing.unresolved[0])).reason,
        'missing-target',
      )
    })

    test.each<boolean>([false, true])(
      '导入机械缺失时保留作用域诊断，引用地图GUID=%s',
      (referenceMap: boolean): void => {
        const map: LogicalCarrier = AnalysisFixture.carrier('scene')
        const guid: string = referenceMap ? map.guid : 'c'.repeat(32)
        const caller: LogicalDevice = AnalysisFixture.device(
          map,
          1,
          1110,
          Option.some([]),
          Option.none(),
          Option.some(guid),
        )
        const source: LogicalGuidAsset = AnalysisFixture.repository('shared', 'b'.repeat(32))
        const entries: readonly LogicalScriptEntry[] = [
          AnalysisFixture.entry('Fire', source.primaryRelativePath, Option.some(source.guid), true),
        ]
        const group: LogicalScriptGroup = AnalysisFixture.group(caller, entries)
        const input: AnalysisInput = AnalysisFixture.input(
          [map, caller, source, group],
          new Map([[source.primaryRelativePath, AnalysisFixture.ast('act 1.this.Fire()\n')]]),
        )
        const graph: CallGraph = AnalysisFixture.graph(input)
        assert.deepEqual(graph.edges, [])
        const unresolved: UnresolvedCall = Option.getOrThrow(
          Option.fromNullishOr(graph.unresolved[0]),
        )
        assert.equal(unresolved.reason, 'missing-scope')
        assert.deepEqual(unresolved.candidates, [])
      },
    )

    test('生成器绑定仅属于自身核心闭包，外部act和可见回调只见内置动作', (): void => {
      const map: LogicalCarrier = AnalysisFixture.carrier('scene')
      const mech: LogicalCarrier = AnalysisFixture.carrier('machine', 'mech', 'c'.repeat(32))
      const caller: LogicalDevice = AnalysisFixture.device(map, 1)
      const generator: LogicalDevice = AnalysisFixture.device(
        map,
        2,
        1110,
        Option.some([]),
        Option.some('Generator'),
        Option.some(mech.guid),
      )
      const core: LogicalDevice = AnalysisFixture.device(mech, 0, 1)
      const callerGroup: LogicalScriptGroup = AnalysisFixture.group(caller, [
        AnalysisFixture.entry('Call', 'scene.map_/1/scripts/Call.code'),
      ])
      const callerScript: LogicalInlineScript = AnalysisFixture.inline(callerGroup, 'Call')
      const generatorGroup: LogicalScriptGroup = AnalysisFixture.group(generator, [
        AnalysisFixture.entry('Fire', 'scene.map_/2/scripts/Fire.code'),
      ])
      const generatorScript: LogicalInlineScript = AnalysisFixture.inline(generatorGroup, 'Fire')
      const input: AnalysisInput = AnalysisFixture.input(
        [
          map,
          mech,
          caller,
          generator,
          core,
          callerGroup,
          callerScript,
          generatorGroup,
          generatorScript,
        ],
        new Map([
          [
            callerScript.primaryRelativePath,
            AnalysisFixture.ast(
              'act 1110.2.Fire()\nact 1110./Generator.Fire()\nfun register(String Fire)\nfun register(var callback)\n',
            ),
          ],
          [
            generatorScript.primaryRelativePath,
            AnalysisFixture.ast('act 1.this.Fire()\nfun register(String Fire)\n'),
          ],
        ]),
      )
      const rule: CallbackRule = {
        kind: 'fun',
        ownerType: Option.none(),
        methodName: 'register',
        argumentIndex: 0,
        targetScope: 'visible',
      }
      const graph: CallGraph = AnalysisFixture.graph(input, [], [rule])
      const callerMethod: SymbolMethod = AnalysisFixture.method(graph.table, caller, 'Call')
      const generatorMethod: SymbolMethod = AnalysisFixture.method(graph.table, generator, 'Fire')
      assert.deepEqual(
        graph.edges
          .filter((edge: CallEdge): boolean => edge.from === callerMethod.key)
          .map((edge: CallEdge): string => edge.to),
        [callerMethod.key],
      )
      assert.deepEqual(
        graph.unresolved
          .filter((call: UnresolvedCall): boolean => call.from === callerMethod.key)
          .map((call: UnresolvedCall): string => call.reason),
        ['missing-method', 'missing-method', 'missing-method', 'dynamic-target'],
      )
      assert.deepEqual(
        graph.edges
          .filter((edge: CallEdge): boolean => edge.from === generatorMethod.key)
          .map((edge: CallEdge): [string, string] => [edge.to, edge.kind]),
        [
          [generatorMethod.key, 'static'],
          [generatorMethod.key, 'dynamic'],
        ],
      )
      const platform: CallGraph = AnalysisFixture.graph(
        input,
        [{ kind: 'act', ownerType: Option.some('1110'), methodName: 'Fire' }],
        [rule],
      )
      assert.deepEqual(
        platform.unresolved
          .filter((call: UnresolvedCall): boolean => call.from === callerMethod.key)
          .map((call: UnresolvedCall): string => call.reason),
        ['missing-method', 'dynamic-target'],
      )
    })

    test('自定义act优先于同签名平台方法，平台登记保持目标校验', (): void => {
      const input: AnalysisInput = AnalysisFixture.calls('act 54.this.Fire()\nact 54.9.Fire()\n', [
        'Call',
        'Fire',
      ])
      const graph: CallGraph = AnalysisFixture.graph(input, [
        { kind: 'act', ownerType: Option.some('54'), methodName: 'Fire' },
      ])
      assert.equal(graph.edges.length, 1)
      assert.equal(Option.getOrThrow(Option.fromNullishOr(graph.edges[0])).kind, 'static')
      assert.equal(
        Option.getOrThrow(Option.fromNullishOr(graph.unresolved[0])).reason,
        'missing-target',
      )
    })

    test('显式回调规则区分常量、变量、歧义、错误参数和未知平台签名', (): void => {
      const input: AnalysisInput = AnalysisFixture.calls(
        'fun register(String Fire)\nfun register(var callback)\nfun register(Long 1)\nfun register(String Missing)\ntoolf register(String Fire)\n',
        ['Call', 'Fire'],
      )
      const rule: CallbackRule = {
        kind: 'fun',
        ownerType: Option.none(),
        methodName: 'register',
        argumentIndex: 0,
        targetScope: 'caller',
      }
      const graph: CallGraph = AnalysisFixture.graph(input, [], [rule])
      assert.deepEqual(
        graph.edges.map((edge: CallEdge): string => edge.kind),
        ['dynamic', 'dynamic', 'dynamic'],
      )
      assert.deepEqual(
        graph.unresolved.map((call: UnresolvedCall): string => call.reason),
        ['dynamic-target', 'invalid-callback', 'missing-method', 'unknown-platform'],
      )
    })

    test('可见回调保留同名候选，排除跨零件私有和事件绑定', (): void => {
      const baseline: AnalysisInput = AnalysisFixture.calls(
        'fun register(String Fire)\nfun register(var callback)\nfun register(String Fire.Left)\n',
        ['Call', 'Fire'],
      )
      const carrier: LogicalCarrier = baseline.entities
        .entity('map:scene.map')
        .require(LogicalCarrier)
      const target: LogicalDevice = AnalysisFixture.device(carrier, 2)
      const source: LogicalGuidAsset = AnalysisFixture.repository('callbacks', 'b'.repeat(32))
      const group: LogicalScriptGroup = AnalysisFixture.group(target, [
        AnalysisFixture.entry('Fire', source.primaryRelativePath, Option.some(source.guid)),
        AnalysisFixture.entry('Secret', source.primaryRelativePath, Option.some(source.guid), true),
      ])
      const event: LogicalScriptGroup = AnalysisFixture.group(
        target,
        [AnalysisFixture.entry('Event', source.primaryRelativePath, Option.some(source.guid))],
        Option.some('start'),
        'event',
      )
      const input: AnalysisInput = {
        ...baseline,
        entities: new LogicalEntitySnapshot(
          [...baseline.entities.entities, target, source, group, event],
          [],
        ),
        asts: new Map([...baseline.asts, [source.primaryRelativePath, AnalysisFixture.ast()]]),
      }
      const rule: CallbackRule = {
        kind: 'fun',
        ownerType: Option.none(),
        methodName: 'register',
        argumentIndex: 0,
        targetScope: 'visible',
      }
      const graph: CallGraph = AnalysisFixture.graph(input, [], [rule])
      const ambiguous: UnresolvedCall = Option.getOrThrow(Option.fromNullishOr(graph.unresolved[0]))
      assert.equal(ambiguous.reason, 'ambiguous-method')
      assert.equal(ambiguous.candidates.length, 2)
      assert.deepEqual(
        graph.edges
          .filter((edge: CallEdge): boolean => edge.location.line === 1)
          .map((edge: CallEdge): string => edge.kind),
        ['potential-static', 'potential-static'],
      )
      const dynamic: UnresolvedCall = Option.getOrThrow(Option.fromNullishOr(graph.unresolved[1]))
      assert.equal(dynamic.reason, 'dynamic-target')
      assert.deepEqual(
        graph.table.methods
          .filter((method: SymbolMethod): boolean => dynamic.candidates.includes(method.key))
          .map((method: SymbolMethod): string => method.bindingName),
        ['Call', 'Fire', 'Fire'],
      )
      const dotted: UnresolvedCall = Option.getOrThrow(Option.fromNullishOr(graph.unresolved[2]))
      assert.equal(dotted.reason, 'missing-method')
      assert.deepEqual(dotted.candidates, [])
      const local: CallGraph = AnalysisFixture.graph(
        input,
        [],
        [{ ...rule, targetScope: 'caller' }],
      )
      assert.equal(
        local.edges.filter((edge: CallEdge): boolean => edge.location.line === 1).length,
        1,
      )
      assert.equal(Option.getOrThrow(Option.fromNullishOr(local.edges[0])).kind, 'dynamic')
    })

    test('禁用控制壳内启用子调用保留，skip脚本不生成调用边', (): void => {
      const input: AnalysisInput = AnalysisFixture.calls(
        '/*if{fun condition()\n  act 54.this.Fire()\n/*}\n/*act 54.this.Unused()\n',
        ['Start', 'Fire', 'Unused'],
        Option.some('start'),
      )
      const graph: CallGraph = AnalysisFixture.graph(input, [
        { kind: 'fun', ownerType: Option.none(), methodName: 'condition' },
      ])
      assert.equal(graph.edges.length, 0)
      assert.equal(
        Option.getOrThrow(Option.fromNullishOr(graph.unresolved[0])).reason,
        'missing-method',
      )
      const selfCall: CallGraph = AnalysisFixture.graph(
        AnalysisFixture.calls('act 54.this.Call()\n', ['Call', 'Unused']),
      )
      const edge: CallEdge = Option.getOrThrow(Option.fromNullishOr(selfCall.edges[0]))
      assert.equal(edge.from, edge.to)
      const skipped: CallGraph = AnalysisFixture.graph(
        AnalysisFixture.calls('act 54.this.Start()\n', ['Start'], Option.some('start'), 'skip:1\n'),
      )
      assert.deepEqual(skipped.edges, [])
      assert.deepEqual(skipped.unresolved, [])
    })
  }
}
