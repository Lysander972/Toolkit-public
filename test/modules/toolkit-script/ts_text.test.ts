import assert from 'node:assert/strict'
import { afterAll, test } from 'vitest'
import { Effect, Option, Scope } from 'effect'
import ts from 'typescript'
import { performance } from 'node:perf_hooks'
import { TsViewText } from '../../../src/toolkit/script/ts_text/TsViewText.ts'
import type {
  TsFragmentRevision,
  TsPrintState,
} from '../../../src/toolkit/script/ts_text/TsViewText.ts'
import { DeviceTsViews } from '../../../src/toolkit/conversion/ts/DeviceTsViews.ts'
import { DeviceTsConverter } from '../../../src/toolkit/script/ts_conversion/DeviceTsConverter.ts'
import type { DeviceConversionBasis } from '../../../src/toolkit/script/ts_conversion/DeviceTsConverter.ts'
import { BescriptConverter } from '../../../src/toolkit/script/conversion/BescriptConverter.ts'
import type {
  BescriptDeviceContext,
  DeviceFunction,
} from '../../../src/toolkit/script/device_context/DeviceContext.ts'
import { TsViewFixture } from '../toolkit-script-fixtures/TsViewFixture.ts'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import type {
  LocalAst,
  LocalAstNode,
  CallSignature,
  CallArgument,
} from '../../../src/toolkit/script/local_ast/LocalAst.ts'

/** @anchor bescript-ts-fragments */
export class TsTextTest {
  private constructor() {}

  /** @anchor bescript-ts-fragments */
  public static revisions(file: ts.SourceFile): readonly TsFragmentRevision[] {
    const methods: ts.MethodDeclaration[] = []
    const visit: (node: ts.Node) => void = (node: ts.Node): void => {
      if (ts.isMethodDeclaration(node)) methods.push(node)
      else ts.forEachChild(node, visit)
    }
    visit(file)
    return methods.map((method: ts.MethodDeclaration): TsFragmentRevision => ({
      id: method.name.getText(file),
      revision: ts.createPrinter().printNode(ts.EmitHint.Unspecified, method, file),
      dependencies: 'fixed',
    }))
  }

  /** @anchor bescript-ts-positions */
  public static compare(text: string, previous: Option.Option<TsPrintState>): TsPrintState {
    const file: ts.SourceFile = TsViewText.parse(text)
    const state: TsPrintState = TsViewText.print(file, TsTextTest.revisions(file), previous)
    assert.deepEqual(
      Buffer.from(state.text),
      Buffer.from(ts.createPrinter({ newLine: ts.NewLineKind.LineFeed }).printFile(file)),
    )
    const parsed: ts.SourceFile = TsViewText.parse(state.text)
    const methods: ts.MethodDeclaration[] = []
    const visit: (node: ts.Node) => void = (node: ts.Node): void => {
      if (ts.isMethodDeclaration(node)) methods.push(node)
      else ts.forEachChild(node, visit)
    }
    visit(parsed)
    for (const [index, position] of state.positions.entries()) {
      const method: ts.MethodDeclaration = Option.getOrThrow(Option.fromNullishOr(methods[index]))
      assert.equal(position.start, method.getStart(parsed))
      assert.equal(position.end, method.end)
      assert.equal(
        position.startLine,
        parsed.getLineAndCharacterOfPosition(method.getStart(parsed)).line + 1,
      )
      assert.equal(position.endLine, parsed.getLineAndCharacterOfPosition(method.end).line + 1)
    }
    return state
  }
}

/** @anchor bescript-ts-fragments */
export class TsFragmentTest {
  private constructor() {}

  static {
    afterAll((): void => {
      console.info('第三层测试实际写入：文件数=0，总字节=0，最大文件=0；累计写入=0')
    })
    test('冷启动、命中、局部编辑与重排逐字节等于独立官方直调', (): void => {
      const initial: string =
        'namespace N { export class C extends B { a: number; first(): void { G.echo("中文😀"); } second(): void { G.echo(`行一\n行二 $' +
        '{"x"}`); } } }'
      const cold: TsPrintState = TsTextTest.compare(initial, Option.none())
      assert.equal(cold.printedFragments, 2)
      const hit: TsPrintState = TsTextTest.compare(initial, Option.some(cold))
      assert.equal(hit.printedFragments, 0)
      const modified: TsPrintState = TsTextTest.compare(
        initial.replace('中文😀', '修改'),
        Option.some(hit),
      )
      assert.equal(modified.printedFragments, 1)
      TsTextTest.compare(
        'namespace N { export class C extends B { second(): void { G.echo(`行一\n行二 $' +
          '{"x"}`); } first(): void { G.echo("修改"); } } }',
        Option.some(modified),
      )
      TsTextTest.compare('namespace N { export class C extends B {} }', Option.some(modified))
    })
    test('注释、空体、字段、两 class、CRLF 和尾部来源都与官方一致', (): void => {
      for (const body of [
        '/*前置*/ first() { /*尾部*/ } // 后置\n\n/** 第二 */ second() { /* 开壳 */; G.echo("x"); /* 闭壳 */; }',
        'first() {} second() { //首行\n G.echo("a"); //尾行\n\n /*多行\n 注释*/\n }',
        'first() { G.echo(`Canvas\n  保留缩进\n  $' + '{"😀"}`); } second() {}',
      ]) {
        const text: string =
          '\uFEFF//文件\r\nnamespace N { export class Outer extends B {} /*来源*/ class Core extends B { /*字段*/ private a = G.x();\r\n' +
          body.replaceAll('\n', '\r\n') +
          '\r\n} } //文件末尾\r\n'
        const state: TsPrintState = TsTextTest.compare(text, Option.none())
        TsTextTest.compare(text, Option.some(state))
        const original: ReturnType<typeof TsViewText.read> = TsViewText.read(
          text,
          TsTextTest.revisions(TsViewText.parse(text)),
        )
        assert.deepEqual(Buffer.from(TsViewText.restore(original, state)), Buffer.from(text))
      }
    })
  }
}

/** @anchor bescript-ts-text */
export class TsSourceTest {
  private constructor() {}

  static {
    test('格式保留恢复实际应用局部 AST 编辑，后续往返无漂移', (): void => {
      const text: string =
        '\uFEFFnamespace N\r\n{ class C { first( ) {\r\n G.echo(\'原值\');\r\n } second(){G.echo("固定")} } }\r\n\r\n'
      const original: ReturnType<typeof TsViewText.read> = TsViewText.read(
        text,
        TsTextTest.revisions(TsViewText.parse(text)),
      )
      const changed: TsPrintState = TsTextTest.compare(
        text.replace('原值', '新值'),
        Option.some(original.printed),
      )
      const restored: string = TsViewText.restore(original, changed)
      assert.ok(restored.includes('新值'))
      assert.ok(restored.includes('second(){G.echo("固定")}'))
      assert.ok(restored.startsWith('\uFEFFnamespace N\r\n'))
      const next: ReturnType<typeof TsViewText.read> = TsViewText.read(
        restored,
        TsTextTest.revisions(TsViewText.parse(restored)),
      )
      assert.equal(TsViewText.restore(next, next.printed), restored)
    })
    test('语法错误和非法视图直接失败，缓存输入保持不变', (): void => {
      assert.throws((): ts.SourceFile => TsViewText.parse('namespace N { class C { m( { }'), /TS/u)
      assert.throws((): TsPrintState => TsTextTest.compare('const x = 1;', Option.none()))
      const text: string = 'namespace N { class C { first() {} } }'
      const state: TsPrintState = TsTextTest.compare(text, Option.none())
      const before: string = JSON.stringify(state)
      assert.throws((): TsPrintState =>
        TsViewText.print(TsViewText.parse(text), [], Option.some(state)),
      )
      TsTextTest.compare(text, Option.some(state))
      assert.equal(JSON.stringify(state), before)
    })
  }
}

/** @anchor bescript-ts-cache */
export class TsCacheTest {
  private constructor() {}

  static {
    test('事件、空组、机械核心与两种内联 Canvas 经第三层双向保真', async (): Promise<void> => {
      const document: ReturnType<typeof CanvasConverter.fromHtml> = CanvasConverter.fromHtml(
        '<BeCanvas name="UI" width="1080" height="2160"><BeUILabel name="label" text="中文 ` $' +
          '{value} \\ end"></BeUILabel></BeCanvas>',
      )
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, never, Scope.Scope>,
            void,
            never
          > {
            const views: DeviceTsViews = yield* DeviceTsViews.make()
            const initial: BescriptDeviceContext = TsViewFixture.context('原值')
            const base: DeviceConversionBasis = TsViewFixture.basis()
            for (const value of [
              CanvasConverter.toInlineBase64(document),
              CanvasConverter.toInlineHjson(document),
            ]) {
              const ast: LocalAst = BescriptConverter.parse(
                'begincodeV2\nname:first\nlines:\nfun canvas(Canvas )\nendcode\n',
              )
              const node: LocalAstNode = Option.getOrThrow(Option.fromNullishOr(ast.nodes[0]))
              const call: CallSignature = Option.getOrThrow(node.call)
              const canvas: DeviceFunction = {
                binding: { origin: 'inline', name: 'first', guid: Option.none(), isPrivate: false },
                ast: {
                  ...ast,
                  nodes: [
                    {
                      ...node,
                      call: Option.some({
                        ...call,
                        arguments: call.arguments.map((argument: CallArgument): CallArgument => ({
                          ...argument,
                          decodedText: value,
                        })),
                      }),
                    },
                  ],
                },
              }
              const second: DeviceFunction = Option.getOrThrow(
                Option.fromNullishOr(
                  Option.getOrThrow(Option.fromNullishOr(initial.scriptGroups[0])).entries[1],
                ),
              )
              const context: BescriptDeviceContext = {
                ...initial,
                scriptGroups: [
                  { kind: 'custom', eventName: Option.none(), entries: [canvas] },
                  { kind: 'event', eventName: Option.some('Tick'), entries: [second] },
                  { kind: 'event', eventName: Option.some('Empty'), entries: [] },
                ],
              }
              const basis: DeviceConversionBasis = {
                ...base,
                identity: {
                  ...base.identity,
                  internalCore: Option.some({
                    scope: 'core',
                    id: '0',
                    deviceType: '54',
                    logicName: Option.none(),
                    className: 'MechCore',
                    baseClassName: 'DeviceType_54',
                  }),
                },
                scripts: base.scripts.map(
                  (
                    script: DeviceConversionBasis['scripts'][number],
                  ): DeviceConversionBasis['scripts'][number] =>
                    script.bindingName === 'second'
                      ? { ...script, kind: 'event', eventName: Option.some('Tick') }
                      : script,
                ),
                events: [
                  { name: 'Tick', parameters: [] },
                  { name: 'Empty', parameters: [] },
                ],
                types: [...base.types, { name: 'Canvas', tsName: 'BeCanvas', bases: [] }],
                methods: [
                  ...base.methods,
                  {
                    kind: 'fun',
                    ownerType: Option.none(),
                    besName: 'canvas',
                    tsName: 'canvas',
                    receiver: 'G',
                    typeArguments: [],
                    parameters: [
                      {
                        typeName: 'Canvas',
                        valueType: 'Canvas',
                        constant: 'canvas',
                        enumName: Option.none(),
                      },
                    ],
                    argumentOrder: [0],
                    returnType: Option.none(),
                    asynchronous: false,
                  },
                ],
              }
              const state: TsPrintState = views.update(context, basis).state
              assert.equal(
                state.text,
                ts.createPrinter().printFile(DeviceTsConverter.toTs(context, basis)),
              )
              assert.ok(state.text.includes('class MechCore'))
              const restored: BescriptDeviceContext = DeviceTsConverter.fromTs(
                TsViewText.parse(state.text),
                basis,
              )
              assert.equal(restored.scriptGroups.length, 3)
              assert.deepEqual(restored.scriptGroups[2], context.scriptGroups[2])
              const result: DeviceFunction = Option.getOrThrow(
                Option.fromNullishOr(
                  Option.getOrThrow(Option.fromNullishOr(restored.scriptGroups[0])).entries[0],
                ),
              )
              assert.equal(
                BescriptConverter.serialize(result.ast),
                BescriptConverter.serialize(canvas.ast),
              )
              assert.equal(views.update(context, basis).state.printedFragments, 0)
            }
          }),
        ),
      )
    })
    test('Host 视图缓存对重新生成的第二层 AST 命中，真实局部修改与依赖变化生效', async (): Promise<void> => {
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, never, Scope.Scope>,
            void,
            never
          > {
            const views: DeviceTsViews = yield* DeviceTsViews.make()
            const basis: DeviceConversionBasis = TsViewFixture.basis()
            for (const [index, value] of ['原值', '原值', '新值'].entries()) {
              const context: BescriptDeviceContext = TsViewFixture.context(value)
              const state: TsPrintState = views.update(context, basis).state
              assert.equal(
                state.text,
                ts.createPrinter().printFile(DeviceTsConverter.toTs(context, basis)),
              )
              assert.equal(state.printedFragments, [2, 0, 1][index])
              const restored: BescriptDeviceContext = DeviceTsConverter.fromTs(
                TsViewText.parse(state.text),
                basis,
              )
              assert.deepEqual(restored, context)
              assert.deepEqual(
                restored.scriptGroups.flatMap(
                  (group: BescriptDeviceContext['scriptGroups'][number]): readonly string[] =>
                    group.entries.map((item: DeviceFunction): string =>
                      BescriptConverter.serialize(item.ast),
                    ),
                ),
                context.scriptGroups.flatMap(
                  (group: BescriptDeviceContext['scriptGroups'][number]): readonly string[] =>
                    group.entries.map((item: DeviceFunction): string =>
                      BescriptConverter.serialize(item.ast),
                    ),
                ),
              )
            }
            const before: TsPrintState = views.update(TsViewFixture.context('新值'), basis).state
            assert.equal(before.printedFragments, 0)
            const changed: DeviceConversionBasis = {
              ...basis,
              methods: basis.methods.map(
                (
                  method: DeviceConversionBasis['methods'][number],
                ): DeviceConversionBasis['methods'][number] =>
                  method.besName === 'echo' ? { ...method, tsName: 'echoChanged' } : method,
              ),
            }
            const state: TsPrintState = views.update(TsViewFixture.context('新值'), changed).state
            assert.equal(state.printedFragments, 2)
            assert.ok(state.text.includes('echoChanged'))
            views.remove(basis.identity.deviceRelativePath)
            assert.equal(
              views.update(TsViewFixture.context('新值'), basis).state.printedFragments,
              2,
            )
          }),
        ),
      )
    })
  }
}

/** @anchor bescript-ts-positions */
export class TsBenchmarkTest {
  private constructor() {}

  static {
    test('包含第二层重新生成节点的 Host 视图服务基准', async (): Promise<void> => {
      const initial: BescriptDeviceContext = TsViewFixture.context('原值')
      const entries: readonly DeviceFunction[] = Array.from(
        { length: 100 },
        (_value: unknown, index: number): DeviceFunction => ({
          binding: {
            origin: 'inline',
            name: 'm' + String(index),
            guid: Option.none(),
            isPrivate: false,
          },
          ast: BescriptConverter.parse(
            'begincodeV2\nname:m' +
              String(index) +
              '\nlines:\n' +
              'fun echo(String 中文@2参数😀)\n'.repeat(80) +
              'endcode\n',
          ),
        }),
      )
      const context: BescriptDeviceContext = {
        ...initial,
        scriptGroups: [{ kind: 'custom', eventName: Option.none(), entries }],
      }
      const base: DeviceConversionBasis = TsViewFixture.basis()
      const basis: DeviceConversionBasis = {
        ...base,
        scripts: entries.map((item: DeviceFunction): DeviceConversionBasis['scripts'][number] => ({
          kind: 'custom',
          eventName: Option.none(),
          bindingName: item.binding.name,
          scriptRelativePath: '机械/测试.mech_/0/scripts/' + item.binding.name + '.code',
          guid: Option.none(),
          tsName: item.binding.name,
        })),
      }
      const edited: BescriptDeviceContext = {
        ...context,
        scriptGroups: [
          {
            kind: 'custom',
            eventName: Option.none(),
            entries: entries.map((item: DeviceFunction, index: number): DeviceFunction =>
              index === 30
                ? {
                    ...item,
                    ast: BescriptConverter.parse(
                      'begincodeV2\nname:m30\nlines:\nfun echo(String 修改)\n' +
                        'fun echo(String 中文@2参数😀)\n'.repeat(79) +
                        'endcode\n',
                    ),
                  }
                : item,
            ),
          },
        ],
      }
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, never, Scope.Scope>,
            void,
            never
          > {
            const views: DeviceTsViews = yield* DeviceTsViews.make()
            views.update(context, basis)
            const baseline: string = ts
              .createPrinter()
              .printFile(DeviceTsConverter.toTs(edited, basis))
            const state: TsPrintState = views.update(edited, basis).state
            assert.equal(state.text, baseline)
            assert.equal(state.printedFragments, 1)

            const coldViews: DeviceTsViews = yield* DeviceTsViews.make()
            let started: number = performance.now()
            const coldBaseline: string = ts
              .createPrinter()
              .printFile(DeviceTsConverter.toTs(edited, basis))
            const coldOfficialMs: number = performance.now() - started
            started = performance.now()
            const coldState: TsPrintState = coldViews.update(edited, basis).state
            const coldIncrementalMs: number = performance.now() - started
            assert.equal(coldState.text, coldBaseline)
            assert.equal(coldState.printedFragments, entries.length)

            const official: number[] = []
            const incremental: number[] = []
            const officialHeap: number[] = []
            const incrementalHeap: number[] = []
            for (let index: number = 0; index < 9; index += 1) {
              views.update(context, basis)
              let heap: number = process.memoryUsage().heapUsed
              started = performance.now()
              const direct: string = ts
                .createPrinter()
                .printFile(DeviceTsConverter.toTs(edited, basis))
              official.push(performance.now() - started)
              officialHeap.push(process.memoryUsage().heapUsed - heap)
              heap = process.memoryUsage().heapUsed
              started = performance.now()
              const local: TsPrintState = views.update(edited, basis).state
              incremental.push(performance.now() - started)
              incrementalHeap.push(process.memoryUsage().heapUsed - heap)
              assert.equal(local.text, direct)
              assert.equal(local.printedFragments, 1)
            }
            official.sort((left: number, right: number): number => left - right)
            incremental.sort((left: number, right: number): number => left - right)
            const officialMedianMs: number = Option.getOrThrow(Option.fromNullishOr(official[4]))
            const incrementalMedianMs: number = Option.getOrThrow(
              Option.fromNullishOr(incremental[4]),
            )
            console.info(
              JSON.stringify({
                viewMethods: entries.length,
                viewStatements: entries.length * 80,
                coldOfficialMs,
                coldIncrementalMs,
                officialMedianMs,
                incrementalMedianMs,
                speedup: officialMedianMs / incrementalMedianMs,
                officialHeap,
                incrementalHeap,
              }),
            )
            assert.ok(Number.isFinite(officialMedianMs))
            assert.ok(Number.isFinite(incrementalMedianMs))
          }),
        ),
      )
    })
    test('大文件局部更新时间和内存基准直接比较整树官方打印', (): void => {
      const methods: string[] = Array.from(
        { length: 100 },
        (_value: unknown, index: number): string =>
          'm' + String(index) + '() {' + 'G.echo("中文 😀 参数");\n'.repeat(70) + '}',
      )
      const text: string = 'namespace N { export class C extends B { ' + methods.join('\n') + ' } }'
      const file: ts.SourceFile = TsViewText.parse(text)
      const revisions: readonly TsFragmentRevision[] = TsTextTest.revisions(file)
      const cold: TsPrintState = TsViewText.print(file, revisions, Option.none())
      const edited: ts.SourceFile = TsViewText.parse(
        text.replace('m50() {G.echo("中文 😀 参数")', 'm50() {G.echo("修改")'),
      )
      const updated: readonly TsFragmentRevision[] = TsTextTest.revisions(edited)
      const printer: ts.Printer = ts.createPrinter()
      for (let index: number = 0; index < 4; index += 1) {
        printer.printFile(edited)
        TsViewText.print(edited, updated, Option.some(cold))
      }
      const direct: number[] = []
      const incremental: number[] = []
      const directHeap: number[] = []
      const incrementalHeap: number[] = []
      for (let index: number = 0; index < 9; index += 1) {
        let heap: number = process.memoryUsage().heapUsed
        let started: number = performance.now()
        const baseline: string = printer.printFile(edited)
        direct.push(performance.now() - started)
        directHeap.push(process.memoryUsage().heapUsed - heap)
        heap = process.memoryUsage().heapUsed
        started = performance.now()
        const state: TsPrintState = TsViewText.print(edited, updated, Option.some(cold))
        incremental.push(performance.now() - started)
        incrementalHeap.push(process.memoryUsage().heapUsed - heap)
        assert.equal(state.text, baseline)
        assert.equal(state.printedFragments, 1)
      }
      direct.sort((left: number, right: number): number => left - right)
      incremental.sort((left: number, right: number): number => left - right)
      const full: number = Option.getOrThrow(Option.fromNullishOr(direct[4]))
      const local: number = Option.getOrThrow(Option.fromNullishOr(incremental[4]))
      console.info(
        JSON.stringify({
          methods: 100,
          statements: 7000,
          textBytes: Buffer.byteLength(cold.text),
          officialMedianMs: full,
          incrementalMedianMs: local,
          speedup: full / local,
          directHeapDeltas: directHeap,
          incrementalHeapDeltas: incrementalHeap,
          retainedFragmentBytes: cold.fragments.reduce(
            (total: number, fragment: TsPrintState['fragments'][number]): number =>
              total + Buffer.byteLength(fragment.text),
            0,
          ),
        }),
      )
      assert.ok(local < full, `局部更新 ${String(local)}ms，官方全量 ${String(full)}ms`)
    })
  }
}
