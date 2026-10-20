import assert from 'node:assert/strict'
import { afterAll, test } from 'vitest'
import { readFileSync } from 'node:fs'
import { Option } from 'effect'
import ts from 'typescript'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import { BescriptConverter } from '../../../src/toolkit/script/conversion/BescriptConverter.ts'
import {
  DeviceTsConverter,
  type DeviceConversionBasis,
} from '../../../src/toolkit/script/ts_conversion/DeviceTsConverter.ts'
import type {
  PlatformMethod,
  ParameterSemantics,
} from '../../../src/toolkit/script/ts_conversion/ConversionBasis.ts'
import type {
  BescriptDeviceContext,
  DeviceFunction,
} from '../../../src/toolkit/script/device_context/DeviceContext.ts'
import type {
  LocalAst,
  LocalAstNode,
  CallSignature,
} from '../../../src/toolkit/script/local_ast/LocalAst.ts'
import type { VarTypeName } from '../../../src/toolkit/script/vocabulary/Vocabulary.ts'

/** @anchor bescript-ts-device */
export class TsConversionTest {
  private constructor() {}

  static {
    afterAll((): void => {
      console.info('第二层转换测试实际写入：文件数=0，总字节=0，最大文件=0；累计写入=0')
    })
    test('官方 AST 直接与打印解析两链均两轮字节无损且不修改输入', (): void => {
      const source: string =
        '\nbegincodeV2\ncomment:保留@2格式\nname:测试\nparams:Float 值\nlines:\n\n12:Float 结果=fun  create.float(Float 01.0)\nfun echo(String a@9b@2c)\n// 尾注释  \nendcode\n\n'
      const context: BescriptDeviceContext = TsConversionTest.context(source)
      const basis: DeviceConversionBasis = TsConversionTest.basis()
      const before: string = JSON.stringify([context, basis])
      for (const parsed of [false, true]) {
        let current: BescriptDeviceContext = context
        for (let round: number = 0; round < 2; round += 1) {
          const ast: ts.SourceFile = DeviceTsConverter.toTs(current, basis)
          const syntax: string = ts.createPrinter().printFile(ast)
          assert.ok(syntax.includes('G.createFloat'))
          assert.ok(syntax.includes('var 结果: BeFloat'))
          current = DeviceTsConverter.fromTs(parsed ? TsConversionTest.parse(syntax) : ast, basis)
          assert.equal(BescriptConverter.serialize(TsConversionTest.script(current)), source)
          assert.deepEqual(current, context)
        }
      }
      assert.equal(JSON.stringify([context, basis]), before)
    })
    test('TS 参数和调用真实编辑改变 BES 字段而非原文回放', (): void => {
      const context: BescriptDeviceContext = TsConversionTest.context(
        'begincodeV2\nname:测试\nlines:\n9:fun echo(String 原样@2文本)\nendcode\n',
      )
      const basis: DeviceConversionBasis = TsConversionTest.basis()
      const text: string = ts.createPrinter().printFile(DeviceTsConverter.toTs(context, basis))
      const transformed: ts.SourceFile = TsConversionTest.edit(
        TsConversionTest.parse(text),
        (node: ts.Node): ts.Node =>
          ts.isStringLiteral(node) && node.text === '原样;文本'
            ? ts.factory.createStringLiteral('新:值')
            : ts.isPropertyAccessExpression(node) && node.name.text === 'echo'
              ? ts.factory.updatePropertyAccessExpression(
                  node,
                  node.expression,
                  ts.factory.createIdentifier('echoAgain'),
                )
              : node,
      )
      const restored: BescriptDeviceContext = DeviceTsConverter.fromTs(transformed, basis)
      assert.equal(
        BescriptConverter.serialize(TsConversionTest.script(restored)),
        'begincodeV2\nname:测试\nlines:\n9:fun echoAgain(String 新@1值)\nendcode\n',
      )
    })
    test('控制、等待、变量调用、跨零件、枚举及禁用壳配对保留', (): void => {
      const source: string =
        'begincodeV2\nname:测试\nparams:Float 值\nlines:\nif{fun predicate()\n  Float 值=varf Float.值.add(Float 2)\n}\nwhile{fun predicate()\n  fun continue()\n  fun break()\n}\nasync fun pause()\ndelay fun create.float(Float 1)\ntoolf log(String x)\ndvf 值.Float.add(Float 3)\nact 54./目标.fire(Enum 1)\n/*if{fun predicate()\n  fun echo(String 启用)\n  /*while{fun predicate()\n    fun echo(String 深层启用)\n  /*}\n/*}\n/*fun echo(String 禁用)\nfun break()\nendcode\n'
      TsConversionTest.roundtrip(source)
      const printed: string = ts
        .createPrinter()
        .printFile(
          DeviceTsConverter.toTs(TsConversionTest.context(source), TsConversionTest.basis()),
        )
      assert.ok(
        TsConversionTest.nodes(TsConversionTest.parse(printed)).some(
          (node: ts.Node): boolean =>
            ts.isCallExpression(node) &&
            ts.isPropertyAccessExpression(node.expression) &&
            node.expression.name.text === 'byName' &&
            ts.isStringLiteral(Option.getOrThrow(Option.fromNullishOr(node.arguments[0]))) &&
            Option.getOrThrow(Option.fromNullishOr(node.arguments[0])).getText() !== '',
        ),
        printed,
      )
      assert.ok(printed.includes('E.One'))
      assert.ok(printed.includes('await delay('))
    })
    test('固定全局初始化与方法级遮蔽、重复赋值', (): void => {
      const base: BescriptDeviceContext = TsConversionTest.context(
        'begincodeV2\nname:测试\nparams:Float 同名\nlines:\nFloat 同名=fun create.float(Float 1)\nFloat 全局=fun create.float(Float 2)\nFloat 局部=fun create.float(Float 3)\nFloat 局部=fun create.float(Float 4)\nendcode\n',
      )
      const context: BescriptDeviceContext = {
        ...base,
        globalVariables: [
          { name: '同名', typeName: 'Float', comment: Option.none() },
          { name: '全局', typeName: 'Float', comment: Option.some('注释') },
        ],
      }
      const basis: DeviceConversionBasis = TsConversionTest.basis()
      const text: string = ts.createPrinter().printFile(DeviceTsConverter.toTs(context, basis))
      assert.ok(text.includes('BeFloat.GlobalVarInitialize()'))
      assert.equal(text.split('var 局部:').length, 2)
      assert.ok(
        TsConversionTest.nodes(TsConversionTest.parse(text)).some(
          (node: ts.Node): boolean =>
            ts.isElementAccessExpression(node) &&
            node.expression.kind === ts.SyntaxKind.ThisKeyword &&
            ts.isStringLiteral(node.argumentExpression) &&
            node.argumentExpression.text === '全局',
        ),
        text,
      )
      assert.deepEqual(DeviceTsConverter.fromTs(TsConversionTest.parse(text), basis), context)
      assert.throws(
        (): BescriptDeviceContext =>
          DeviceTsConverter.fromTs(
            TsConversionTest.parse(
              text.replace('BeFloat.GlobalVarInitialize()', 'BeFloat.fromBeConst("7")'),
            ),
            basis,
          ),
        /默认初始化/u,
      )
    })
    test('事件生效签名覆写但源码签名和两种私有标记独立保留', (): void => {
      const ast: LocalAst = BescriptConverter.parse(
        'begincode\nname:源码\nprivate:1\nparams:Long 原参数\nreturn:Long 原参数\nlines:\nfun echo(String 事件)\nendcode\n',
      )
      const context: BescriptDeviceContext = {
        metadata: {
          deviceType: 54,
          logicName: Option.none(),
          mechFileGuid: Option.none(),
          stickToBone: Option.none(),
        },
        globalVariables: [],
        scriptGroups: [
          {
            kind: 'event',
            eventName: Option.some('Tick'),
            entries: [
              {
                binding: {
                  origin: 'repository',
                  name: '引用',
                  guid: Option.some('a'.repeat(32)),
                  isPrivate: false,
                },
                ast,
              },
            ],
          },
          { kind: 'custom', eventName: Option.none(), entries: [] },
          { kind: 'event', eventName: Option.some('Empty'), entries: [] },
        ],
      }
      const base: DeviceConversionBasis = TsConversionTest.basis()
      const basis: DeviceConversionBasis = {
        ...base,
        scripts: [
          {
            kind: 'event',
            eventName: Option.some('Tick'),
            bindingName: '引用',
            scriptRelativePath: 'code/source.code',
            guid: Option.some('a'.repeat(32)),
            tsName: 'EventTick',
          },
        ],
        events: [
          { name: 'Tick', parameters: [{ name: '$value', typeName: 'Float' }] },
          { name: 'Empty', parameters: [] },
        ],
      }
      const syntax: string = ts.createPrinter().printFile(DeviceTsConverter.toTs(context, basis))
      assert.ok(syntax.includes('private EventTick($value: BeFloat): void'))
      assert.deepEqual(DeviceTsConverter.fromTs(TsConversionTest.parse(syntax), basis), context)
      assert.throws(
        (): BescriptDeviceContext =>
          DeviceTsConverter.fromTs(
            TsConversionTest.parse(syntax.replace('private EventTick', 'public EventTick')),
            basis,
          ),
        /事件必须/u,
      )
    })
    test('Canvas 两种内联、两种 GUID 与模板特殊字符经纯嵌套调用无损', (): void => {
      const document: ReturnType<typeof CanvasConverter.fromHtml> = CanvasConverter.fromHtml(
        '<BeCanvas name="UI" width="1080" height="2160"><BeUILabel name="label" text="` $' +
          '{value} \\ end"></BeUILabel></BeCanvas>',
      )
      const values: readonly string[] = [
        CanvasConverter.toInlineBase64(document),
        CanvasConverter.toInlineHjson(document),
        'guid:' + 'a'.repeat(32),
        '==guid:' + 'b'.repeat(32) + '==',
      ]
      for (const value of values) {
        const source: string = 'begincodeV2\nname:测试\nlines:\nfun canvas(Canvas )\nendcode\n'
        const initial: BescriptDeviceContext = TsConversionTest.context(source)
        const ast: LocalAst = TsConversionTest.script(initial)
        const node: LocalAstNode = Option.getOrThrow(Option.fromNullishOr(ast.nodes[0]))
        const call: CallSignature = Option.getOrThrow(node.call)
        const updated: LocalAst = {
          ...ast,
          nodes: [
            {
              ...node,
              call: Option.some({
                ...call,
                arguments: call.arguments.map(
                  (
                    argument: import('../../../src/toolkit/script/local_ast/LocalAst.ts').CallArgument,
                  ): typeof argument => ({ ...argument, decodedText: value }),
                ),
              }),
            },
          ],
        }
        const context: BescriptDeviceContext = TsConversionTest.context(
          BescriptConverter.serialize(updated),
        )
        const basis: DeviceConversionBasis = TsConversionTest.basis()
        const text: string = ts.createPrinter().printFile(DeviceTsConverter.toTs(context, basis))
        const restored: BescriptDeviceContext = DeviceTsConverter.fromTs(
          TsConversionTest.parse(text),
          basis,
        )
        assert.equal(
          BescriptConverter.serialize(TsConversionTest.script(restored)),
          BescriptConverter.serialize(TsConversionTest.script(context)),
        )
        if (value.startsWith('canvas:') || value.startsWith('==hjson:')) {
          const edited: BescriptDeviceContext = DeviceTsConverter.fromTs(
            TsConversionTest.edit(TsConversionTest.parse(text), (node: ts.Node): ts.Node =>
              ts.isNoSubstitutionTemplateLiteral(node)
                ? ts.factory.createNoSubstitutionTemplateLiteral(
                    node.text.replace('name="UI"', 'name="Changed"'),
                  )
                : ts.isStringLiteral(node) && node.text === 'UI'
                  ? ts.factory.createStringLiteral('Changed')
                  : node,
            ),
            basis,
          )
          const editedArgument: string = Option.getOrThrow(
            Option.fromNullishOr(
              Option.getOrThrow(
                Option.getOrThrow(Option.fromNullishOr(TsConversionTest.script(edited).nodes[0]))
                  .call,
              ).arguments[0],
            ),
          ).decodedText
          assert.notEqual(editedArgument, value)
          assert.equal(CanvasConverter.fromInline(editedArgument).name, 'Changed')
        }
      }
    })
    test('输入变化改变结果、错误隔离及空组不凭空生成', (): void => {
      const source: string = 'begincodeV2\nname:测试\nlines:\nfun echo(String x)\nendcode\n'
      const context: BescriptDeviceContext = TsConversionTest.context(source)
      const basis: DeviceConversionBasis = TsConversionTest.basis()
      const missing: DeviceConversionBasis = { ...basis, methods: [] }
      assert.throws((): ts.SourceFile => DeviceTsConverter.toTs(context, missing))
      TsConversionTest.roundtrip(source)
      const changed: DeviceConversionBasis = {
        ...basis,
        methods: basis.methods.map((method: PlatformMethod): PlatformMethod =>
          method.besName === 'echo' ? { ...method, tsName: 'renamed' } : method,
        ),
      }
      assert.ok(
        ts
          .createPrinter()
          .printFile(DeviceTsConverter.toTs(context, changed))
          .includes('G.renamed'),
      )
      assert.deepEqual(
        DeviceTsConverter.fromTs(
          DeviceTsConverter.toTs({ ...context, scriptGroups: [] }, basis),
          basis,
        ).scriptGroups,
        [],
      )
    })
  }

  /** @anchor bescript-ts-device */
  public static basis(): DeviceConversionBasis {
    const methods: PlatformMethod[] = [
      TsConversionTest.method('create.float', 'createFloat', ['Float'], Option.some('Float')),
      TsConversionTest.method('echo', 'echo', ['String'], Option.none()),
      TsConversionTest.method('echoAgain', 'echoAgain', ['String'], Option.none()),
      TsConversionTest.method('predicate', 'predicate', [], Option.some('Bool')),
      { ...TsConversionTest.method('pause', 'pause', [], Option.none()), asynchronous: true },
      {
        ...TsConversionTest.method('log', 'log', ['String'], Option.none()),
        kind: 'toolf',
        receiver: 'Tool',
      },
      {
        ...TsConversionTest.method('add', 'add', ['Float'], Option.some('Float')),
        kind: 'varf',
        ownerType: Option.some('Float'),
      },
      {
        ...TsConversionTest.method('add', 'add', ['Float'], Option.some('Float')),
        kind: 'dvf',
        ownerType: Option.some('Float'),
      },
      {
        ...TsConversionTest.method('fire', 'fire', ['Enum'], Option.none()),
        kind: 'act',
        ownerType: Option.some('54'),
        parameters: [
          { typeName: 'Enum', valueType: 'Enum', constant: 'enum', enumName: Option.some('Test') },
        ],
      },
      {
        ...TsConversionTest.method('canvas', 'canvas', ['Canvas'], Option.none()),
        parameters: [
          { typeName: 'Canvas', valueType: 'Canvas', constant: 'canvas', enumName: Option.none() },
        ],
      },
    ]
    return {
      identity: {
        deviceRelativePath: 'mech/0',
        id: '0',
        scope: 'mech',
        namespaceName: 'Mech_Test',
        className: 'Device_Test_0',
        baseClassName: 'DeviceType_54',
        internalCore: Option.none(),
      },
      scripts: [
        {
          kind: 'custom',
          eventName: Option.none(),
          bindingName: '测试',
          scriptRelativePath: 'mech/0/scripts/测试.code',
          guid: Option.none(),
          tsName: 'Test',
        },
      ],
      targets: [
        {
          scope: 'mech',
          id: '1',
          deviceType: '54',
          logicName: Option.some('目标'),
          className: 'Target',
          methods: [],
        },
      ],
      types: ['Float', 'Long', 'String', 'Bool', 'Canvas', 'Enum', 'UI', 'UILabel'].map(
        (name: string): DeviceConversionBasis['types'][number] => ({
          name: TsConversionTest.type(name),
          tsName: 'Be' + name,
          bases: name === 'UILabel' ? ['UI'] : [],
        }),
      ),
      methods,
      events: [],
      enums: [{ name: 'Test', tsName: 'E', values: [{ value: '1', tsName: 'One' }] }],
      identifiers: [],
    }
  }

  /** @anchor bescript-ts-device */
  private static type(name: string): VarTypeName {
    if (
      name === 'Float' ||
      name === 'Long' ||
      name === 'String' ||
      name === 'Bool' ||
      name === 'Canvas' ||
      name === 'Enum' ||
      name === 'UI' ||
      name === 'UILabel'
    )
      return name
    throw new Error('测试类型未定义')
  }

  /** @anchor bescript-ts-device */
  public static method(
    besName: string,
    tsName: string,
    types: readonly VarTypeName[],
    returnType: Option.Option<VarTypeName>,
  ): PlatformMethod {
    return {
      kind: 'fun',
      ownerType: Option.none(),
      besName,
      tsName,
      receiver: 'G',
      typeArguments: [],
      parameters: types.map((typeName: VarTypeName): ParameterSemantics => ({
        typeName,
        valueType: typeName,
        constant: typeName === 'Canvas' ? 'empty' : 'value',
        enumName: Option.none(),
      })),
      argumentOrder: types.map((_type: VarTypeName, index: number): number => index),
      returnType,
      asynchronous: false,
    }
  }

  /** @anchor bescript-ts-device */
  public static context(source: string): BescriptDeviceContext {
    return {
      metadata: {
        deviceType: 54,
        logicName: Option.none(),
        mechFileGuid: Option.none(),
        stickToBone: Option.none(),
      },
      globalVariables: [],
      scriptGroups: [
        {
          kind: 'custom',
          eventName: Option.none(),
          entries: [
            {
              binding: { origin: 'inline', name: '测试', guid: Option.none(), isPrivate: false },
              ast: BescriptConverter.parse(source),
            },
          ],
        },
      ],
    }
  }

  /** @anchor bescript-ts-device */
  public static script(context: BescriptDeviceContext): LocalAst {
    const group: readonly DeviceFunction[] = Option.getOrThrow(
      Option.fromNullishOr(context.scriptGroups[0]),
    ).entries
    return Option.getOrThrow(Option.fromNullishOr(group[0])).ast
  }

  /** @anchor bescript-ts-device */
  public static parse(text: string): ts.SourceFile {
    return ts.createSourceFile('view.ts', text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  }

  /** @anchor bescript-ts-device */
  public static nodes(node: ts.Node): readonly ts.Node[] {
    const nodes: ts.Node[] = [node]
    ts.forEachChild(node, (child: ts.Node): void => {
      nodes.push(...TsConversionTest.nodes(child))
    })
    return nodes
  }

  /** @anchor bescript-ts-device */
  public static edit(file: ts.SourceFile, edit: (node: ts.Node) => ts.Node): ts.SourceFile {
    const transform: ts.TransformationResult<ts.SourceFile> = ts.transform(file, [
      (context: ts.TransformationContext): ts.Transformer<ts.SourceFile> => {
        const visit: ts.Visitor = (node: ts.Node): ts.VisitResult<ts.Node> =>
          ts.visitEachChild(edit(node), visit, context)
        return (sourceFile: ts.SourceFile): ts.SourceFile =>
          ts.visitEachChild(sourceFile, visit, context)
      },
    ])
    try {
      return Option.getOrThrow(Option.fromNullishOr(transform.transformed[0]))
    } finally {
      transform.dispose()
    }
  }

  private static roundtrip(source: string): void {
    const context: BescriptDeviceContext = TsConversionTest.context(source)
    const basis: DeviceConversionBasis = TsConversionTest.basis()
    for (const parsed of [false, true]) {
      const file: ts.SourceFile = DeviceTsConverter.toTs(context, basis)
      const result: BescriptDeviceContext = DeviceTsConverter.fromTs(
        parsed ? TsConversionTest.parse(ts.createPrinter().printFile(file)) : file,
        basis,
      )
      assert.equal(BescriptConverter.serialize(TsConversionTest.script(result)), source)
    }
  }

  /** @anchor bescript-ts-device */
  public static verify(context: BescriptDeviceContext, basis: DeviceConversionBasis): void {
    const before: string = JSON.stringify([context, basis])
    for (const parsed of [false, true]) {
      let current: BescriptDeviceContext = context
      for (let round: number = 0; round < 2; round += 1) {
        const ast: ts.SourceFile = DeviceTsConverter.toTs(current, basis)
        current = DeviceTsConverter.fromTs(
          parsed ? TsConversionTest.parse(ts.createPrinter().printFile(ast)) : ast,
          basis,
        )
        assert.deepEqual(current, context)
        for (const [index, group] of current.scriptGroups.entries()) {
          for (const [position, item] of group.entries.entries()) {
            const original: DeviceFunction = Option.getOrThrow(
              Option.fromNullishOr(
                Option.getOrThrow(Option.fromNullishOr(context.scriptGroups[index])).entries[
                  position
                ],
              ),
            )
            assert.equal(
              BescriptConverter.serialize(item.ast),
              BescriptConverter.serialize(original.ast),
            )
          }
        }
      }
    }
    assert.equal(JSON.stringify([context, basis]), before)
  }
}

/**
 * @anchor bescript-ts-calls
 * @anchor bescript-ts-bindings
 * @anchor bescript-ts-basis
 * @anchor bescript-ts-targets
 * @anchor bescript-ts-async-dependencies
 * @anchor bescript-ts-statements
 */
export class TsCallSemanticsTest {
  private constructor() {}

  static {
    test('控制条件必须产生 Bool，正反向均拒绝无返回和丢弃结果', (): void => {
      const base: DeviceConversionBasis = TsConversionTest.basis()
      const basis: DeviceConversionBasis = {
        ...base,
        methods: [
          ...base.methods,
          TsConversionTest.method('noResult', 'noResult', [], Option.none()),
          TsConversionTest.method('number', 'number', [], Option.some('Float')),
        ],
      }
      for (const kind of ['if', 'while']) {
        for (const call of ['fun noResult()', 'fun number()', '=fun predicate()']) {
          const context: BescriptDeviceContext = TsConversionTest.context(
            'begincodeV2\nname:测试\nlines:\n' + kind + '{' + call + '\n}\nendcode\n',
          )
          assert.throws(
            (): ts.SourceFile => DeviceTsConverter.toTs(context, basis),
            /控制调用|平台类型不兼容/u,
          )
        }
        const file: ts.SourceFile = DeviceTsConverter.toTs(
          TsConversionTest.context(
            'begincodeV2\nname:测试\nlines:\n' + kind + '{fun predicate()\n}\nendcode\n',
          ),
          basis,
        )
        for (const replacement of ['noResult', 'number', 'discard']) {
          const edited: ts.SourceFile = TsConversionTest.edit(file, (node: ts.Node): ts.Node => {
            if (replacement === 'discard') {
              if (ts.isIfStatement(node))
                return ts.factory.updateIfStatement(
                  node,
                  ts.factory.createVoidExpression(node.expression),
                  node.thenStatement,
                  node.elseStatement,
                )
              if (ts.isWhileStatement(node))
                return ts.factory.updateWhileStatement(
                  node,
                  ts.factory.createVoidExpression(node.expression),
                  node.statement,
                )
              return node
            }
            if (
              !ts.isCallExpression(node) ||
              !ts.isPropertyAccessExpression(node.expression) ||
              node.expression.name.text !== 'predicate'
            )
              return node
            return ts.factory.updateCallExpression(
              node,
              ts.factory.updatePropertyAccessExpression(
                node.expression,
                node.expression.expression,
                ts.factory.createIdentifier(replacement),
              ),
              node.typeArguments,
              node.arguments,
            )
          })
          for (const current of [
            edited,
            TsConversionTest.parse(ts.createPrinter().printFile(edited)),
          ])
            assert.throws(
              (): BescriptDeviceContext => DeviceTsConverter.fromTs(current, basis),
              /控制调用|平台类型不兼容/u,
            )
        }
      }
    })

    test('官方泛型区分同一 TS 方法，参数排列完整双向且真实参数可编辑', (): void => {
      const base: DeviceConversionBasis = TsConversionTest.basis()
      const basis: DeviceConversionBasis = {
        ...base,
        methods: [
          ...base.methods,
          {
            ...TsConversionTest.method(
              'make.float',
              'make',
              ['Float', 'String'],
              Option.some('Float'),
            ),
            typeArguments: ['Float'],
            argumentOrder: [1, 0],
          },
          {
            ...TsConversionTest.method(
              'make.long',
              'make',
              ['Long', 'String'],
              Option.some('Long'),
            ),
            typeArguments: ['Long'],
            argumentOrder: [1, 0],
          },
        ],
      }
      const context: BescriptDeviceContext = TsConversionTest.context(
        'begincodeV2\nname:测试\nlines:\nFloat 值=fun make.float(Float 1:String 标签)\nLong 整数=fun make.long(Long 2:String 另一个)\nendcode\n',
      )
      TsConversionTest.verify(context, basis)
      const file: ts.SourceFile = DeviceTsConverter.toTs(context, basis)
      const calls: readonly ts.CallExpression[] = TsConversionTest.nodes(file).filter(
        ts.isCallExpression,
      )
      const made: ts.CallExpression = Option.getOrThrow(
        Option.fromNullishOr(
          calls.find(
            (call: ts.CallExpression): boolean =>
              ts.isPropertyAccessExpression(call.expression) &&
              call.expression.name.text === 'make',
          ),
        ),
      )
      assert.equal(Option.getOrThrow(Option.fromNullishOr(made.typeArguments)).length, 1)
      const first: ts.Expression = Option.getOrThrow(Option.fromNullishOr(made.arguments[0]))
      assert.ok(ts.isCallExpression(first))
      const firstValue: ts.Expression = Option.getOrThrow(Option.fromNullishOr(first.arguments[0]))
      assert.ok(ts.isStringLiteral(firstValue))
      assert.equal(firstValue.text, '标签')
      const edited: ts.SourceFile = TsConversionTest.edit(file, (node: ts.Node): ts.Node =>
        ts.isStringLiteral(node) && node.text === '标签'
          ? ts.factory.createStringLiteral('新标签')
          : node,
      )
      const restored: LocalAst = TsConversionTest.script(DeviceTsConverter.fromTs(edited, basis))
      const call: CallSignature = Option.getOrThrow(
        Option.getOrThrow(Option.fromNullishOr(restored.nodes[0])).call,
      )
      assert.equal(Option.getOrThrow(Option.fromNullishOr(call.arguments[1])).decodedText, '新标签')
    })

    test('结果类型独立于父类型变量，变量属主 as 必须遵守显式继承', (): void => {
      const base: DeviceConversionBasis = TsConversionTest.basis()
      const basis: DeviceConversionBasis = {
        ...base,
        methods: [
          ...base.methods,
          TsConversionTest.method('create.label', 'createLabel', [], Option.some('UILabel')),
          {
            ...TsConversionTest.method('touch', 'touch', [], Option.none()),
            kind: 'varf',
            ownerType: Option.some('UI'),
          },
        ],
      }
      const context: BescriptDeviceContext = TsConversionTest.context(
        'begincodeV2\nname:测试\nparams:UI 对象\nUILabel 标签\nlines:\nUILabel 对象=fun create.label()\nvarf UI.标签.touch()\nendcode\n',
      )
      TsConversionTest.verify(context, basis)
      const file: ts.SourceFile = DeviceTsConverter.toTs(context, basis)
      const edited: ts.SourceFile = TsConversionTest.edit(file, (node: ts.Node): ts.Node =>
        ts.isAsExpression(node)
          ? ts.factory.updateAsExpression(
              node,
              node.expression,
              ts.factory.createTypeReferenceNode('BeFloat'),
            )
          : node,
      )
      assert.throws(
        (): BescriptDeviceContext => DeviceTsConverter.fromTs(edited, basis),
        /平台类型不兼容/u,
      )
    })

    test('异步有返回的必需等待不能静默移除，无返回调用不自动等待', (): void => {
      const base: DeviceConversionBasis = TsConversionTest.basis()
      const basis: DeviceConversionBasis = {
        ...base,
        methods: base.methods.map((method: PlatformMethod): PlatformMethod =>
          method.besName === 'create.float' ? { ...method, asynchronous: true } : method,
        ),
      }
      const context: BescriptDeviceContext = TsConversionTest.context(
        'begincodeV2\nname:测试\nlines:\nFloat 值=fun create.float(Float 1)\nfun pause()\nasync fun pause()\nendcode\n',
      )
      TsConversionTest.verify(context, basis)
      const file: ts.SourceFile = DeviceTsConverter.toTs(context, basis)
      const edited: ts.SourceFile = TsConversionTest.edit(file, (node: ts.Node): ts.Node =>
        ts.isAwaitExpression(node) &&
        ts.isCallExpression(node.expression) &&
        ts.isPropertyAccessExpression(node.expression.expression) &&
        node.expression.expression.name.text === 'createFloat'
          ? node.expression
          : node,
      )
      assert.throws(
        (): BescriptDeviceContext => DeviceTsConverter.fromTs(edited, basis),
        /必须等待/u,
      )
      assert.equal(TsConversionTest.nodes(file).filter(ts.isAwaitExpression).length, 2)
    })

    test('当前方法依赖推导、跨零件闭包和机械核心私有调用独立往返', (): void => {
      const base: BescriptDeviceContext = TsConversionTest.context(
        'begincodeV2\nname:测试\nlines:\nFloat 值=act 54.this.慢速()\nFloat 外部=act 54.1.计算()\nendcode\n',
      )
      const slow: DeviceFunction = {
        binding: { origin: 'inline', name: '慢速', guid: Option.none(), isPrivate: false },
        ast: BescriptConverter.parse(
          'begincodeV2\nname:慢速\nprivate:1\nreturn:Float 值\nlines:\nFloat 值=fun create.float(Float 1)\nasync fun pause()\nendcode\n',
        ),
      }
      const context: BescriptDeviceContext = {
        ...base,
        scriptGroups: [
          {
            kind: 'custom',
            eventName: Option.none(),
            entries: [
              ...Option.getOrThrow(Option.fromNullishOr(base.scriptGroups[0])).entries,
              slow,
            ],
          },
        ],
      }
      const input: DeviceConversionBasis = TsConversionTest.basis()
      const basis: DeviceConversionBasis = {
        ...input,
        scripts: [
          ...input.scripts,
          {
            kind: 'custom',
            eventName: Option.none(),
            bindingName: '慢速',
            scriptRelativePath: 'mech/0/scripts/慢速.code',
            guid: Option.none(),
            tsName: 'Slow',
          },
        ],
        targets: input.targets.map(
          (
            target: DeviceConversionBasis['targets'][number],
          ): DeviceConversionBasis['targets'][number] => ({
            ...target,
            methods: [
              {
                besName: '计算',
                tsName: 'Calculate',
                parameters: [],
                returnType: Option.some('Float'),
                isPrivate: false,
                asynchronous: false,
              },
            ],
          }),
        ),
      }
      TsConversionTest.verify(context, basis)
      const coreBasis: DeviceConversionBasis = {
        ...basis,
        identity: {
          ...basis.identity,
          baseClassName: 'DeviceType_1110',
          internalCore: Option.some({
            scope: 'core',
            id: '5',
            deviceType: '54',
            logicName: Option.none(),
            className: 'InternalCore',
            baseClassName: 'DeviceType_54',
          }),
        },
        targets: basis.targets.map(
          (
            target: DeviceConversionBasis['targets'][number],
          ): DeviceConversionBasis['targets'][number] => ({ ...target, scope: 'core' }),
        ),
      }
      TsConversionTest.verify(
        { ...context, metadata: { ...context.metadata, deviceType: 1110 } },
        coreBasis,
      )
    })
  }
}

/**
 * @anchor bescript-ts-device
 * @anchor bescript-ts-source
 * @anchor bescript-ts-statements
 */
export class TsStructureTest {
  private constructor() {}

  static {
    test('来源明确的方法与语句可新增、删除和重排，重复与缺失来源报错', (): void => {
      const base: BescriptDeviceContext = TsConversionTest.context(
        'begincodeV2\nname:测试\nlines:\nfun echo(String 一)\nfun echo(String 二)\nendcode\n',
      )
      const second: DeviceFunction = {
        binding: { origin: 'inline', name: '新增', guid: Option.none(), isPrivate: false },
        ast: BescriptConverter.parse(
          'begincodeV2\nname:新增\nlines:\nfun echo(String 新)\nendcode\n',
        ),
      }
      const basis: DeviceConversionBasis = {
        ...TsConversionTest.basis(),
        scripts: [
          ...TsConversionTest.basis().scripts,
          {
            kind: 'custom',
            eventName: Option.none(),
            bindingName: '新增',
            scriptRelativePath: 'mech/0/scripts/新增.code',
            guid: Option.none(),
            tsName: 'New',
          },
        ],
      }
      const expanded: BescriptDeviceContext = {
        ...base,
        scriptGroups: [
          {
            kind: 'custom',
            eventName: Option.none(),
            entries: [
              ...Option.getOrThrow(Option.fromNullishOr(base.scriptGroups[0])).entries,
              second,
            ],
          },
        ],
      }
      const candidate: ts.MethodDeclaration = Option.getOrThrow(
        Option.fromNullishOr(
          TsConversionTest.nodes(DeviceTsConverter.toTs(expanded, basis)).find(
            ts.isMethodDeclaration,
          ),
        ),
      )
      const all: readonly ts.Node[] = TsConversionTest.nodes(
        DeviceTsConverter.toTs(expanded, basis),
      )
      const inserted: ts.Node = Option.getOrThrow(
        Option.fromNullishOr(
          all.find(
            (node: ts.Node): boolean =>
              ts.isMethodDeclaration(node) &&
              ts.isIdentifier(node.name) &&
              node.name.text === 'New',
          ),
        ),
      )
      assert.ok(ts.isMethodDeclaration(inserted))
      const initial: ts.SourceFile = DeviceTsConverter.toTs(base, basis)
      const added: ts.SourceFile = TsConversionTest.edit(initial, (node: ts.Node): ts.Node =>
        ts.isClassDeclaration(node)
          ? ts.factory.updateClassDeclaration(
              node,
              node.modifiers,
              node.name,
              node.typeParameters,
              node.heritageClauses,
              [inserted, ...node.members],
            )
          : node,
      )
      const restored: BescriptDeviceContext = DeviceTsConverter.fromTs(added, basis)
      assert.deepEqual(
        Option.getOrThrow(Option.fromNullishOr(restored.scriptGroups[0])).entries.map(
          (entry: DeviceFunction): string => entry.binding.name,
        ),
        ['新增', '测试'],
      )
      const removed: ts.SourceFile = TsConversionTest.edit(added, (node: ts.Node): ts.Node =>
        ts.isClassDeclaration(node)
          ? ts.factory.updateClassDeclaration(
              node,
              node.modifiers,
              node.name,
              node.typeParameters,
              node.heritageClauses,
              node.members.filter((member: ts.ClassElement): boolean => member !== inserted),
            )
          : node,
      )
      assert.deepEqual(DeviceTsConverter.fromTs(removed, basis), base)
      const duplicate: ts.SourceFile = TsConversionTest.edit(initial, (node: ts.Node): ts.Node =>
        ts.isClassDeclaration(node)
          ? ts.factory.updateClassDeclaration(
              node,
              node.modifiers,
              node.name,
              node.typeParameters,
              node.heritageClauses,
              [...node.members, candidate],
            )
          : node,
      )
      assert.throws(
        (): BescriptDeviceContext => DeviceTsConverter.fromTs(duplicate, basis),
        /来源重复/u,
      )
      const changed: ts.SourceFile = TsConversionTest.edit(initial, (node: ts.Node): ts.Node =>
        ts.isBlock(node)
          ? ts.factory.updateBlock(node, [
              Option.getOrThrow(Option.fromNullishOr(node.statements[1])),
              ts.factory.createExpressionStatement(
                ts.factory.createCallExpression(
                  ts.factory.createPropertyAccessExpression(
                    ts.factory.createIdentifier('G'),
                    'echo',
                  ),
                  undefined,
                  [
                    ts.factory.createCallExpression(
                      ts.factory.createPropertyAccessExpression(
                        ts.factory.createIdentifier('BeString'),
                        'fromBeConst',
                      ),
                      undefined,
                      [ts.factory.createStringLiteral('三')],
                    ),
                  ],
                ),
              ),
              Option.getOrThrow(Option.fromNullishOr(node.statements[2])),
            ])
          : node,
      )
      const changedAst: LocalAst = TsConversionTest.script(DeviceTsConverter.fromTs(changed, basis))
      assert.equal(changedAst.nodes.length, 2)
      assert.ok(
        BescriptConverter.serialize(changedAst).includes(
          'fun echo(String 二)\nfun echo(String 三)',
        ),
      )
      const repeated: ts.SourceFile = TsConversionTest.edit(initial, (node: ts.Node): ts.Node =>
        ts.isBlock(node)
          ? ts.factory.updateBlock(node, [
              Option.getOrThrow(Option.fromNullishOr(node.statements[0])),
              ...node.statements,
            ])
          : node,
      )
      assert.throws(
        (): BescriptDeviceContext => DeviceTsConverter.fromTs(repeated, basis),
        /语句来源不能重复/u,
      )
      const missing: ts.MethodDeclaration = ts.factory.createMethodDeclaration(
        [ts.factory.createModifier(ts.SyntaxKind.PublicKeyword)],
        undefined,
        'Unknown',
        undefined,
        undefined,
        [],
        ts.factory.createKeywordTypeNode(ts.SyntaxKind.VoidKeyword),
        ts.factory.createBlock([]),
      )
      const unknown: ts.SourceFile = TsConversionTest.edit(initial, (node: ts.Node): ts.Node =>
        ts.isClassDeclaration(node)
          ? ts.factory.updateClassDeclaration(
              node,
              node.modifiers,
              node.name,
              node.typeParameters,
              node.heritageClauses,
              [...node.members, missing],
            )
          : node,
      )
      assert.throws((): BescriptDeviceContext => DeviceTsConverter.fromTs(unknown, basis))
    })

    test('调用参数删除不能恢复旧命令，当前 AST 语义与正文一致', (): void => {
      const base: DeviceConversionBasis = TsConversionTest.basis()
      const method: PlatformMethod = TsConversionTest.method(
        'echo',
        'echo',
        ['String', 'String'],
        Option.none(),
      )
      const originalBasis: DeviceConversionBasis = {
        ...base,
        methods: base.methods.map((item: PlatformMethod): PlatformMethod =>
          item.besName === 'echo' ? method : item,
        ),
      }
      const context: BescriptDeviceContext = TsConversionTest.context(
        'begincodeV2\nname:测试\nlines:\nfun echo(String 一:String 二)\nendcode\n',
      )
      const file: ts.SourceFile = DeviceTsConverter.toTs(context, originalBasis)
      const edited: ts.SourceFile = TsConversionTest.edit(file, (node: ts.Node): ts.Node =>
        ts.isCallExpression(node) &&
        ts.isPropertyAccessExpression(node.expression) &&
        node.expression.name.text === 'echo'
          ? ts.factory.updateCallExpression(node, node.expression, node.typeArguments, [
              Option.getOrThrow(Option.fromNullishOr(node.arguments[0])),
            ])
          : node,
      )
      const ast: LocalAst = TsConversionTest.script(DeviceTsConverter.fromTs(edited, base))
      assert.equal(
        Option.getOrThrow(Option.getOrThrow(Option.fromNullishOr(ast.nodes[0])).call).arguments
          .length,
        1,
      )
      assert.equal(
        Option.getOrThrow(Option.fromNullishOr(ast.nodes[0])).text,
        'fun echo(String 一)',
      )
      assert.equal(
        BescriptConverter.serialize(ast),
        'begincodeV2\nname:测试\nlines:\nfun echo(String 一)\nendcode\n',
      )
    })

    test('禁用父壳不提供循环控制但不禁用子节点，skip 与注释空白独立保留', (): void => {
      const context: BescriptDeviceContext = TsConversionTest.context(
        'begincodeV2\nname:测试\nlines:\n/*while{fun predicate()\n  fun echo(String 启用)\n  while{fun predicate()\n    fun continue()\n    fun break()\n  }\n  fun break()\n/*}\nendcode\n',
      )
      const ast: LocalAst = TsConversionTest.script(context)
      const skip: LocalAstNode = {
        sourceRef: Option.none(),
        line: 1,
        depth: 0,
        disabled: false,
        kind: 'skip',
        text: ' 保留  ',
        children: [],
        call: Option.none(),
      }
      const amended: BescriptDeviceContext = {
        ...context,
        scriptGroups: [
          {
            kind: 'custom',
            eventName: Option.none(),
            entries: [
              {
                binding: Option.getOrThrow(
                  Option.fromNullishOr(
                    Option.getOrThrow(Option.fromNullishOr(context.scriptGroups[0])).entries[0],
                  ),
                ).binding,
                ast: { ...ast, nodes: [...ast.nodes, skip, { ...skip, disabled: true }] },
              },
            ],
          },
        ],
      }
      TsConversionTest.verify(amended, TsConversionTest.basis())
      const forbidden: BescriptDeviceContext = TsConversionTest.context(
        'begincodeV2\nname:测试\nlines:\n/*while{fun predicate()\n  fun continue()\n/*}\nendcode\n',
      )
      assert.throws(
        (): ts.SourceFile => DeviceTsConverter.toTs(forbidden, TsConversionTest.basis()),
        /启用的循环/u,
      )
      const enabled: ts.SourceFile = DeviceTsConverter.toTs(
        TsConversionTest.context(
          'begincodeV2\nname:测试\nlines:\nwhile{fun predicate()\n  fun break()\n}\nendcode\n',
        ),
        TsConversionTest.basis(),
      )
      const badReturn: ts.SourceFile = TsConversionTest.edit(enabled, (node: ts.Node): ts.Node =>
        ts.isBreakStatement(node) ? ts.factory.createReturnStatement() : node,
      )
      assert.throws(
        (): BescriptDeviceContext => DeviceTsConverter.fromTs(badReturn, TsConversionTest.basis()),
        /循环内 return/u,
      )
    })
  }
}

/** @anchor bescript-ts-methods */
export class TsVisibilityTest {
  private constructor() {}

  static {
    test('内联可见性只改源码，仓库多引用只改引用，不绕过 private 源码', (): void => {
      const initial: BescriptDeviceContext = TsConversionTest.context(
        'begincodeV2\nname:测试\nlines:\nfun echo(String 原文)\nendcode\n',
      )
      const basis: DeviceConversionBasis = TsConversionTest.basis()
      const syntax: string = ts.createPrinter().printFile(DeviceTsConverter.toTs(initial, basis))
      const hidden: BescriptDeviceContext = DeviceTsConverter.fromTs(
        TsConversionTest.parse(syntax.replace('public Test', 'protected Test')),
        basis,
      )
      assert.equal(TsConversionTest.script(hidden).header.isPrivate, true)
      assert.equal(
        Option.getOrThrow(
          Option.fromNullishOr(
            Option.getOrThrow(Option.fromNullishOr(hidden.scriptGroups[0])).entries[0],
          ),
        ).binding.isPrivate,
        false,
      )
      const shown: BescriptDeviceContext = DeviceTsConverter.fromTs(
        TsConversionTest.parse(
          ts
            .createPrinter()
            .printFile(DeviceTsConverter.toTs(hidden, basis))
            .replace('protected Test', 'public Test'),
        ),
        basis,
      )
      assert.equal(TsConversionTest.script(shown).header.isPrivate, false)
      const shared: LocalAst = TsConversionTest.script(initial)
      const guid: string = 'a'.repeat(32)
      const entries: readonly DeviceFunction[] = ['引用一', '引用二'].map(
        (name: string): DeviceFunction => ({
          binding: { origin: 'repository', name, guid: Option.some(guid), isPrivate: false },
          ast: shared,
        }),
      )
      const context: BescriptDeviceContext = {
        ...initial,
        scriptGroups: [{ kind: 'custom', eventName: Option.none(), entries }],
      }
      const repoBasis: DeviceConversionBasis = {
        ...basis,
        scripts: entries.map(
          (entry: DeviceFunction, index: number): DeviceConversionBasis['scripts'][number] => ({
            kind: 'custom',
            eventName: Option.none(),
            bindingName: entry.binding.name,
            scriptRelativePath: 'repo/shared.code',
            guid: Option.some(guid),
            tsName: 'Reference' + String(index),
          }),
        ),
      }
      TsConversionTest.verify(context, repoBasis)
      const changed: BescriptDeviceContext = DeviceTsConverter.fromTs(
        TsConversionTest.parse(
          ts
            .createPrinter()
            .printFile(DeviceTsConverter.toTs(context, repoBasis))
            .replace('public Reference0', 'protected Reference0'),
        ),
        repoBasis,
      )
      const modified: readonly DeviceFunction[] = Option.getOrThrow(
        Option.fromNullishOr(changed.scriptGroups[0]),
      ).entries
      assert.equal(Option.getOrThrow(Option.fromNullishOr(modified[0])).binding.isPrivate, true)
      assert.equal(Option.getOrThrow(Option.fromNullishOr(modified[1])).binding.isPrivate, false)
      assert.equal(shared.header.isPrivate, false)
      assert.deepEqual(
        modified.map((entry: DeviceFunction): LocalAst => entry.ast),
        [shared, shared],
      )
      const sourcePrivate: BescriptDeviceContext = {
        ...context,
        scriptGroups: [
          {
            kind: 'custom',
            eventName: Option.none(),
            entries: entries.map((entry: DeviceFunction): DeviceFunction => ({
              ...entry,
              ast: { ...shared, header: { ...shared.header, isPrivate: true } },
            })),
          },
        ],
      }
      assert.throws(
        (): BescriptDeviceContext =>
          DeviceTsConverter.fromTs(
            TsConversionTest.parse(
              ts
                .createPrinter()
                .printFile(DeviceTsConverter.toTs(sourcePrivate, repoBasis))
                .replace('protected Reference0', 'public Reference0'),
            ),
            repoBasis,
          ),
        /源码 private/u,
      )
    })

    test('结构化来源非法与重复注释直接失败，后续转换不被污染', (): void => {
      const basis: DeviceConversionBasis = TsConversionTest.basis()
      const context: BescriptDeviceContext = TsConversionTest.context(
        'begincodeV2\nname:测试\nlines:\nfun echo(String 正常)\nendcode\n',
      )
      const file: ts.SourceFile = DeviceTsConverter.toTs(context, basis)
      const bad: ts.SourceFile = TsConversionTest.edit(file, (node: ts.Node): ts.Node =>
        ts.isClassDeclaration(node)
          ? ts.addSyntheticLeadingComment(
              node,
              ts.SyntaxKind.MultiLineCommentTrivia,
              '@bescript:' + Buffer.from('{"kind":"device","data":{}}').toString('base64'),
              true,
            )
          : node,
      )
      assert.throws((): BescriptDeviceContext => DeviceTsConverter.fromTs(bad, basis))
      TsConversionTest.verify(context, basis)
    })
  }
}

/** @anchor bescript-ts-device */
export class TsCorpusTest {
  private constructor() {}

  static {
    test('真实脚本只读双链往返，平台签名依据已登记定义而非输入猜测', (): void => {
      const base: DeviceConversionBasis = TsConversionTest.basis()
      const fileRelativePath: string = 'test/data/projects/颜色对对碰/MechEditorCode/方法1.code'
      const original: Buffer = readFileSync(fileRelativePath)
      const context: BescriptDeviceContext = TsConversionTest.context(original.toString('utf8'))
      const basis: DeviceConversionBasis = {
        ...base,
        methods: [
          TsConversionTest.method('light', 'light', ['Float'], Option.none()),
          TsConversionTest.method(
            'HDRBloom',
            'HDRBloom',
            ['Bool', 'Float', 'Float'],
            Option.none(),
          ),
        ],
        scripts: [
          {
            ...Option.getOrThrow(Option.fromNullishOr(base.scripts[0])),
            scriptRelativePath: 'MechEditorCode/方法1.code',
          },
        ],
      }
      TsConversionTest.verify(context, basis)
      assert.deepEqual(
        Buffer.from(
          BescriptConverter.serialize(
            TsConversionTest.script(
              DeviceTsConverter.fromTs(DeviceTsConverter.toTs(context, basis), basis),
            ),
          ),
          'utf8',
        ),
        original,
      )
      assert.deepEqual(readFileSync(fileRelativePath), original)
    })
  }
}
