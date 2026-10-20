import { test } from 'vitest'
import assert from 'node:assert/strict'
import { readFile, readdir } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import type { Dirent } from 'node:fs'
import { Option } from 'effect'
import { BescriptConverter } from '../../../src/toolkit/script/conversion/BescriptConverter.ts'
import { LocalAstSchema } from '../../../src/toolkit/script/local_ast/LocalAst.ts'
import type {
  ScriptSource,
  NodeSource,
  SourceField,
} from '../../../src/toolkit/script/local_ast/SourceLayout.ts'
import type { ScriptParameter } from '../../../src/toolkit/script/vocabulary/Vocabulary.ts'
import type {
  CallArgument,
  LocalAst,
  LocalAstNode,
  CallSignature,
} from '../../../src/toolkit/script/local_ast/LocalAst.ts'

/**
 * @anchor bescript-converter
 * @anchor bescript-source-layout
 * @anchor bescript-body-lexer
 * @anchor bescript-body-grammar
 * @anchor bescript-body-projection
 * @anchor bescript-call-projection
 * @anchor bescript-call-output
 * @anchor bescript-source-output
 * @anchor bescript-header-output
 * @anchor bescript-body-output
 * @anchor bescript-argument-output
 */
export class ConversionTest {
  private constructor() {}

  static {
    test('act 属主仅十进制数字和 this 省略别名标记', (): void => {
      const numericOwners: readonly string[] = ['0', '0010', '123', '9'.repeat(400), 'this']
      const aliasOwners: readonly string[] = ['0x10', '0b10', '0o10', '1e2', '-1', '+1', '目标']
      for (const owner of [...aliasOwners, ...numericOwners, '/123', '/this', '/0x10']) {
        const source: string =
          'begincodeV2\nname:测试\nlines:\nact 54.' + owner + '.计算()\nendcode\n'
        const ast: LocalAst = BescriptConverter.parse(source)
        const node: LocalAstNode = ConversionTest.node(ast)
        const call: CallSignature = Option.getOrThrow(node.call)
        assert.equal(call.ownerNameIsAlias, !numericOwners.includes(owner), owner)
        assert.deepEqual(
          call.ownerName,
          Option.some(owner.startsWith('/') ? owner.slice(1) : owner),
        )
        assert.equal(BescriptConverter.serialize(ast), source)
        const output: string = BescriptConverter.serialize({
          ...ast,
          source: Option.none(),
          nodes: [{ ...node, sourceRef: Option.none() }],
        })
        const signature: string =
          !numericOwners.includes(owner) && !owner.startsWith('/') ? '/' + owner : owner
        assert.equal(output, source.replace(owner + '.计算', signature + '.计算'))
        const reparsed: CallSignature = Option.getOrThrow(
          ConversionTest.node(BescriptConverter.parse(output)).call,
        )
        assert.deepEqual(reparsed.ownerName, call.ownerName)
        assert.equal(reparsed.ownerNameIsAlias, call.ownerNameIsAlias)
      }
    })

    test('正文实际生成调用、控制树和来源坐标', (): void => {
      const ast: LocalAst = BescriptConverter.parse(
        'begincodeV2\nname:测试\nlines:\n\n12:/*if{fun bool.not(var 值)\n  Long 结果=act 54./目标.计算(Long 2:String a@1b)\n/*}\n//尾注释\nendcode\n',
      )
      assert.equal(ast.nodes.length, 2)
      const root: LocalAstNode = ConversionTest.node(ast)
      assert.equal(root.kind, 'if')
      assert.equal(root.line, 2)
      assert.equal(root.disabled, true)
      assert.equal(root.children.length, 1)
      const call: CallSignature = Option.getOrThrow(
        Option.getOrThrow(Option.fromNullishOr(root.children[0])).call,
      )
      assert.equal(call.kind, 'act')
      assert.deepEqual(call.ownerType, Option.some('54'))
      assert.deepEqual(call.ownerName, Option.some('目标'))
      assert.equal(call.ownerNameIsAlias, true)
      assert.deepEqual(call.returnVariable, Option.some({ typeName: 'Long', name: '结果' }))
      assert.equal(Option.getOrThrow(Option.fromNullishOr(call.arguments[1])).decodedText, 'a:b')
      assert.equal(Option.getOrThrow(Option.fromNullishOr(ast.nodes[1])).text, '//尾注释')
    })

    test('头部排版、空白、行号、转义和结尾逐字节稳定', (): void => {
      const source: string =
        '\n\nbegincode\ncomment:a@2b@@1\n\nparams:Long 数量\nBool 开关\nname:测试\nlines:\n\n12:  \n13:fun create.string(String a@9b@8c)  \n\nendcode\n\n'
      ConversionTest.roundtrip(source)
      ConversionTest.roundtrip('begincodeV2\nname:空\nlines:\nendcode')
      ConversionTest.roundtrip('begincodeV2\nname:无换行\nlines:\nfun break()\nendcode')
    })

    test('方法和参数编辑使用语义字段，不回放旧正文', (): void => {
      const source: string =
        'begincodeV2\nname:测试\nlines:\n9:fun  create.string( String )\nendcode\n'
      const supported: string = source.replace('( String )', '(String a@9b:String 原样@2文本)')
      const ast: LocalAst = BescriptConverter.parse(supported)
      const node: LocalAstNode = ConversionTest.node(ast)
      const call: CallSignature = Option.getOrThrow(node.call)
      const changed: LocalAst = {
        ...ast,
        nodes: [
          {
            ...node,
            call: Option.some({
              ...call,
              functionName: 'string.add',
              arguments: call.arguments.map(
                (argument: CallArgument, index: number): CallArgument =>
                  index === 0 ? { ...argument, decodedText: '新:值' } : argument,
              ),
            }),
          },
        ],
      }
      const output: string = BescriptConverter.serialize(changed)
      assert.equal(
        output,
        'begincodeV2\nname:测试\nlines:\n9:fun  string.add(String 新@1值:String 原样@2文本)\nendcode\n',
      )
      assert.equal(
        Option.getOrThrow(ConversionTest.node(BescriptConverter.parse(output)).call).functionName,
        'string.add',
      )
      assert.equal(BescriptConverter.serialize(ast), supported)
    })

    test('节点插入、删除、重排和跨块移动保留独立来源', (): void => {
      const ast: LocalAst = BescriptConverter.parse(
        'begincodeV2\nname:测试\nlines:\n\n//第一个\nif{fun create.bool(Bool 1)\n  fun break()\n}\n//最后\nendcode\n',
      )
      const first: LocalAstNode = ConversionTest.node(ast)
      const block: LocalAstNode = Option.getOrThrow(Option.fromNullishOr(ast.nodes[1]))
      const last: LocalAstNode = Option.getOrThrow(Option.fromNullishOr(ast.nodes[2]))
      const moved: LocalAstNode = {
        ...Option.getOrThrow(Option.fromNullishOr(block.children[0])),
        depth: 0,
      }
      const added: LocalAstNode = { ...first, sourceRef: Option.none(), text: '//新增' }
      const changed: LocalAst = { ...ast, nodes: [last, moved, { ...block, children: [] }, added] }
      assert.equal(
        BescriptConverter.serialize(changed),
        'begincodeV2\nname:测试\nlines:\n//最后\nfun break()\nif{fun create.bool(Bool 1)\n}\n//新增\nendcode\n',
      )
      const parsed: LocalAst = BescriptConverter.parse(BescriptConverter.serialize(changed))
      assert.equal(parsed.nodes.length, 4)
      assert.equal(Option.getOrThrow(Option.fromNullishOr(parsed.nodes[1])).kind, 'break')
      assert.throws((): string => BescriptConverter.serialize({ ...ast, nodes: [first, first] }))
    })

    test('头部编辑保留其他头部位置，显式新建 AST 可独立序列化', (): void => {
      const ast: LocalAst = BescriptConverter.parse(
        'begincode\ncomment:保留@2注释\nname:旧名\nparams:Long 数量\nBool 开关\nlines:\nendcode',
      )
      const changed: LocalAst = {
        ...ast,
        version: 'v2',
        header: { ...ast.header, name: '新名', debug: true },
      }
      assert.equal(
        BescriptConverter.serialize(changed),
        'begincodeV2\ncomment:保留@2注释\nname:新名\nparams:Long 数量\nBool 开关\ndebug:1\nlines:\nendcode',
      )
      const generated: LocalAst = { ...changed, source: Option.none(), nodes: [] }
      ConversionTest.roundtrip(BescriptConverter.serialize(generated))
    })

    test('语法错误暴露，失败调用不污染后续转换', (): void => {
      const retainedSource: string =
        'begincodeV2\nname:保留\nlines:\nif{fun a()\n  fun b(String 初始)\n}\nendcode\n'
      const retained: LocalAst = BescriptConverter.parse(retainedSource)
      const badBodies: readonly string[] = [
        'fun a(',
        'if{fun a()\n',
        'fun a(Bool)\n',
        '}\n',
        'fun a() junk\n',
        'fun a(Canvas ==hjson@1@1\n[\n',
      ]
      for (const body of badBodies) {
        assert.throws((): LocalAst =>
          BescriptConverter.parse('begincodeV2\nname:测试\nlines:\n' + body + 'endcode\n'),
        )
        ConversionTest.roundtrip('begincodeV2\nname:正常\nlines:\nfun a()\nendcode\n')
        assert.equal(BescriptConverter.serialize(retained), retainedSource)
      }
      const next: LocalAst = BescriptConverter.parse(
        'begincodeV2\nname:后续\nlines:\nfun c(String 不同输入)\nendcode\n',
      )
      assert.equal(next.header.name, '后续')
      assert.equal(ConversionTest.node(next).line, 1)
      assert.equal(
        Option.getOrThrow(
          Option.fromNullishOr(Option.getOrThrow(ConversionTest.node(next).call).arguments[0]),
        ).decodedText,
        '不同输入',
      )
      assert.equal(BescriptConverter.serialize(retained), retainedSource)
    })

    test('五类调用及返回、参数和禁用编辑重新解析为新语义', (): void => {
      const signatures: readonly string[] = [
        'fun a()',
        'toolf a()',
        'varf Long.值.a()',
        'act 54./目标.a()',
        'dvf 值.Device.a()',
      ]
      for (const signature of signatures) {
        const ast: LocalAst = BescriptConverter.parse(
          'begincodeV2\nname:测试\nlines:\n8:' + signature + '\nendcode\n',
        )
        const node: LocalAstNode = ConversionTest.node(ast)
        const call: CallSignature = Option.getOrThrow(node.call)
        const edited: CallSignature = {
          ...call,
          functionName: '编辑',
          returnVariable: Option.some({ typeName: 'Long', name: '结果' }),
          arguments: [
            {
              sourceRef: Option.none(),
              variable: false,
              typeName: 'String',
              rawText: '不能使用旧值',
              decodedText: 'a:b@(c)',
              varType: Option.some('String'),
            },
          ],
        }
        const changed: LocalAst = {
          ...ast,
          nodes: [{ ...node, disabled: true, call: Option.some(edited) }],
        }
        const serialized: string = BescriptConverter.serialize(changed)
        const reparsed: LocalAstNode = ConversionTest.node(BescriptConverter.parse(serialized))
        const result: CallSignature = Option.getOrThrow(reparsed.call)
        assert.equal(reparsed.disabled, true)
        assert.equal(result.functionName, '编辑')
        assert.deepEqual(result.returnVariable, edited.returnVariable)
        assert.equal(
          Option.getOrThrow(Option.fromNullishOr(result.arguments[0])).decodedText,
          'a:b@(c)',
        )
        const discarded: LocalAst = {
          ...changed,
          nodes: [
            {
              ...node,
              call: Option.some({
                ...edited,
                returnVariable: Option.none(),
                discardsReturn: true,
                arguments: [],
              }),
            },
          ],
        }
        const discardedCall: CallSignature = Option.getOrThrow(
          ConversionTest.node(BescriptConverter.parse(BescriptConverter.serialize(discarded))).call,
        )
        assert.equal(discardedCall.discardsReturn, true)
        assert.deepEqual(discardedCall.returnVariable, Option.none())
        assert.equal(discardedCall.arguments.length, 0)
      }
    })

    test('属主及别名编辑只改变对应字段，忽略 saveName 与来源命令', (): void => {
      const ast: LocalAst = BescriptConverter.parse(
        'begincodeV2\nname:测试\nlines:\n4:act  54./旧名.执行( Long 2:String a@9b)\nendcode\n',
      )
      const node: LocalAstNode = ConversionTest.node(ast)
      const call: CallSignature = Option.getOrThrow(node.call)
      const changed: LocalAst = {
        ...ast,
        nodes: [
          {
            ...node,
            text: '不能回放',
            call: Option.some({
              ...call,
              ownerName: Option.some('this'),
              ownerNameIsAlias: false,
              ownerType: Option.some('55'),
              saveName: '不能回放',
            }),
          },
        ],
      }
      assert.equal(
        BescriptConverter.serialize(changed),
        'begincodeV2\nname:测试\nlines:\n4:act  55.this.执行( Long 2:String a@9b)\nendcode\n',
      )
      const asDvf: LocalAst = {
        ...ast,
        nodes: [
          {
            ...node,
            call: Option.some({
              ...call,
              kind: 'dvf',
              ownerType: Option.some('Device'),
              ownerName: Option.some('零件'),
              ownerNameIsAlias: false,
            }),
          },
        ],
      }
      assert.equal(
        Option.getOrThrow(
          ConversionTest.node(BescriptConverter.parse(BescriptConverter.serialize(asDvf))).call,
        ).kind,
        'dvf',
      )
    })

    test('所有头部语义可编辑，可选字段删除不复活', (): void => {
      const ast: LocalAst = BescriptConverter.parse(
        'begincode\nname:旧\ncomment:旧@2注释\nguid:0123456789abcdef0123456789abcdef\ncolor:001,2,3\nparams:Long 旧参数\nreturn:Long 旧参数\ndebug:1\nskip:1\nprivate:1\nlines:\nendcode\n',
      )
      const changed: LocalAst = {
        ...ast,
        header: {
          ...ast.header,
          name: '新',
          comment: Option.some('新:注释'),
          guid: Option.some('abcdef0123456789abcdef0123456789'),
          color: Option.some({ red: 7, green: 8, blue: 9 }),
          parameters: [{ typeName: 'String', name: '新参数' }],
          returnParameter: Option.some({ typeName: 'String', name: '新参数' }),
          debug: false,
          skip: false,
          isPrivate: false,
        },
      }
      assert.deepEqual(
        BescriptConverter.parse(BescriptConverter.serialize(changed)).header,
        changed.header,
      )
      const removed: LocalAst = {
        ...changed,
        header: {
          ...changed.header,
          comment: Option.none(),
          guid: Option.none(),
          color: Option.none(),
          parameters: [],
          returnParameter: Option.none(),
        },
      }
      assert.deepEqual(
        BescriptConverter.parse(BescriptConverter.serialize(removed)).header,
        removed.header,
      )
    })

    test('多行 Canvas 独立生成参数，修改后保持内联边界', (): void => {
      const source: string =
        'begincodeV2\nname:测试\nlines:\n=fun create.canvas(Canvas ==hjson@1画布@1\n[\n  { text@1a@4b@5 }\n]==)\nendcode\n'
      const ast: LocalAst = BescriptConverter.parse(source)
      const node: LocalAstNode = ConversionTest.node(ast)
      const call: CallSignature = Option.getOrThrow(node.call)
      const argument: CallArgument = Option.getOrThrow(Option.fromNullishOr(call.arguments[0]))
      assert.equal(argument.typeName, 'Canvas')
      assert.equal(argument.decodedText, '==hjson:画布:\n[\n  { text:a(b) }\n]==')
      ConversionTest.roundtrip(source)
      const decodedText: string = '==hjson:新名:\n[\n  { text:新@值 }\n]=='
      const changed: LocalAst = {
        ...ast,
        nodes: [
          { ...node, call: Option.some({ ...call, arguments: [{ ...argument, decodedText }] }) },
        ],
      }
      const result: CallArgument = Option.getOrThrow(
        Option.fromNullishOr(
          Option.getOrThrow(
            ConversionTest.node(BescriptConverter.parse(BescriptConverter.serialize(changed))).call,
          ).arguments[0],
        ),
      )
      assert.equal(result.decodedText, decodedText)
    })

    test('深层嵌套、禁用块尾和注释编辑保持来源特征', (): void => {
      const source: string =
        'begincodeV2\nname:测试\nlines:\n/*if{fun a()\n  while{fun b()\n/*    delay fun c()\n    async fun d()\n    fun continue()\n  }\n\n/*}\n//旧注释@2保留\nendcode\n'
      const ast: LocalAst = BescriptConverter.parse(source)
      ConversionTest.roundtrip(source)
      const root: LocalAstNode = ConversionTest.node(ast)
      const whileNode: LocalAstNode = Option.getOrThrow(Option.fromNullishOr(root.children[0]))
      assert.deepEqual(
        whileNode.children.map((node: LocalAstNode): string => node.kind),
        ['delay', 'async', 'continue'],
      )
      const comment: LocalAstNode = Option.getOrThrow(Option.fromNullishOr(ast.nodes[1]))
      const changed: LocalAst = {
        ...ast,
        nodes: [root, { ...comment, text: '//新注释', disabled: true }],
      }
      assert.equal(
        BescriptConverter.serialize(changed),
        source.replace('//旧注释@2保留', '/*//新注释'),
      )
    })

    test('共享和结构克隆 AST 不依赖对象身份，序列化不修改输入', (): void => {
      const ast: LocalAst = BescriptConverter.parse(
        'begincodeV2\nname:共享\nlines:\nfun a(String 原值)\nendcode\n',
      )
      const source: string = BescriptConverter.serialize(ast)
      const cloned: LocalAst = {
        ...ast,
        nodes: ast.nodes.map((node: LocalAstNode): LocalAstNode => ({ ...node })),
      }
      const before: LocalAst = structuredClone(ast)
      Object.freeze(ast)
      Object.freeze(ast.nodes)
      Object.freeze(ConversionTest.node(ast))
      assert.equal(BescriptConverter.serialize(ast), source)
      assert.equal(BescriptConverter.serialize(cloned), source)
      assert.deepEqual(structuredClone(ast), before)
      assert.throws((): LocalAst =>
        LocalAstSchema.parse({
          ...cloned,
          source: Option.some({ ...Option.getOrThrow(cloned.source), tail: null }),
        }),
      )
    })

    test('切换禁用状态不改变同层级的行号与原始空白', (): void => {
      const source: string =
        'begincodeV2\nname:测试\nlines:\nif{fun a()\n10: \t/* fun b()\n}\nendcode\n'
      const ast: LocalAst = BescriptConverter.parse(source)
      const root: LocalAstNode = ConversionTest.node(ast)
      const child: LocalAstNode = Option.getOrThrow(Option.fromNullishOr(root.children[0]))
      const changed: LocalAst = {
        ...ast,
        nodes: [{ ...root, children: [{ ...child, disabled: false }] }],
      }
      assert.equal(BescriptConverter.serialize(changed), source.replace('/*', ''))
      ConversionTest.roundtrip(BescriptConverter.serialize(changed))
    })

    test('单个头部参数编辑不改写其他参数与续行格式', (): void => {
      const source: string =
        'begincodeV2\nname:测试\nparams:Long  数量\n\nBool 开关\nlines:\nendcode\n'
      const ast: LocalAst = BescriptConverter.parse(source)
      const changed: LocalAst = {
        ...ast,
        header: {
          ...ast.header,
          parameters: ast.header.parameters.map(
            (parameter: ScriptParameter, index: number): ScriptParameter =>
              index === 0 ? { ...parameter, name: '新数量' } : parameter,
          ),
        },
      }
      assert.equal(BescriptConverter.serialize(changed), source.replace('数量', '新数量'))
      const returned: LocalAst = BescriptConverter.parse(
        'begincodeV2\nname:测试\nlines:\nLong  result =fun  a( String a@9b)\nendcode\n',
      )
      const node: LocalAstNode = ConversionTest.node(returned)
      const call: CallSignature = Option.getOrThrow(node.call)
      assert.equal(
        BescriptConverter.serialize({
          ...returned,
          nodes: [{ ...node, call: Option.some({ ...call, kind: 'toolf' }) }],
        }),
        'begincodeV2\nname:测试\nlines:\nLong  result =toolf  a( String a@9b)\nendcode\n',
      )
      assert.equal(
        BescriptConverter.serialize({
          ...returned,
          nodes: [
            {
              ...node,
              call: Option.some({
                ...call,
                returnVariable: Option.some({ typeName: 'Long', name: '新结果' }),
              }),
            },
          ],
        }),
        'begincodeV2\nname:测试\nlines:\nLong  新结果 =fun  a( String a@9b)\nendcode\n',
      )
    })

    test('参数新增删除重排保留每个未编辑参数的来源拼写', (): void => {
      const source: string =
        'begincodeV2\nname:测试\nlines:\n4:act  54./旧名.执行( Long 2: String a@9b)\nendcode\n'
      const ast: LocalAst = BescriptConverter.parse(source)
      const node: LocalAstNode = ConversionTest.node(ast)
      const call: CallSignature = Option.getOrThrow(node.call)
      const added: CallArgument = {
        sourceRef: Option.none(),
        variable: false,
        typeName: 'String',
        rawText: '',
        decodedText: '新增',
        varType: Option.some('String'),
      }
      const inserted: LocalAst = {
        ...ast,
        nodes: [{ ...node, call: Option.some({ ...call, arguments: [...call.arguments, added] }) }],
      }
      assert.equal(
        BescriptConverter.serialize(inserted),
        source.replace('a@9b)', 'a@9b:String 新增)'),
      )
      const reverse: LocalAst = {
        ...ast,
        nodes: [
          { ...node, call: Option.some({ ...call, arguments: [...call.arguments].reverse() }) },
        ],
      }
      assert.equal(
        BescriptConverter.serialize(reverse),
        source.replace('( Long 2: String a@9b)', '( String a@9b: Long 2)'),
      )
      const removed: LocalAst = {
        ...ast,
        nodes: [{ ...node, call: Option.some({ ...call, arguments: call.arguments.slice(1) }) }],
      }
      assert.equal(
        BescriptConverter.serialize(removed),
        source.replace('( Long 2: String a@9b)', '( String a@9b)'),
      )
      const dvf: LocalAst = {
        ...ast,
        nodes: [
          {
            ...node,
            call: Option.some({
              ...call,
              kind: 'dvf',
              ownerName: Option.some('零件'),
              ownerType: Option.some('Device'),
              ownerNameIsAlias: false,
            }),
          },
        ],
      }
      assert.equal(
        BescriptConverter.serialize(dvf),
        source.replace('act  54./旧名.执行', 'dvf  零件.Device.执行'),
      )
    })

    test('Canvas 中 endcode 原文不夺取文件末尾定界', (): void => {
      const source: string =
        'begincodeV2\nname:测试\nlines:\nfun a(Canvas ==hjson@1\n[\nendcode\n]==)\nendcode\n'
      ConversionTest.roundtrip(source)
      const ast: LocalAst = BescriptConverter.parse(source)
      assert.equal(
        Option.getOrThrow(
          Option.fromNullishOr(Option.getOrThrow(ConversionTest.node(ast).call).arguments[0]),
        ).decodedText,
        '==hjson:\n[\nendcode\n]==',
      )
    })

    test('非 act 的斜杠属主保留真实名称，非法编辑输入明确拒绝', (): void => {
      const ast: LocalAst = BescriptConverter.parse(
        'begincodeV2\nname:测试\nparams:Long /值\nlines:\nvarf Long./值.a()\ndvf /零件.Device.a()\nendcode\n',
      )
      assert.deepEqual(
        Option.getOrThrow(ConversionTest.node(ast).call).ownerName,
        Option.some('/值'),
      )
      assert.deepEqual(
        Option.getOrThrow(Option.getOrThrow(Option.fromNullishOr(ast.nodes[1])).call).ownerName,
        Option.some('/零件'),
      )
      const generated: LocalAst = {
        ...ast,
        source: Option.none(),
        nodes: ast.nodes.map((node: LocalAstNode): LocalAstNode => ({
          ...node,
          sourceRef: Option.none(),
        })),
      }
      assert.deepEqual(
        BescriptConverter.parse(BescriptConverter.serialize(generated)).nodes.map(
          (node: LocalAstNode): typeof node.call => node.call,
        ),
        ast.nodes.map((node: LocalAstNode): typeof node.call => node.call),
      )
      const simple: LocalAst = BescriptConverter.parse(
        'begincodeV2\nname:测试\nlines:\nact 54.this.a(String ab)\nendcode\n',
      )
      const node: LocalAstNode = ConversionTest.node(simple)
      const call: CallSignature = Option.getOrThrow(node.call)
      const dotted: LocalAst = {
        ...simple,
        nodes: [{ ...node, call: Option.some({ ...call, functionName: 'a.b' }) }],
      }
      assert.equal(
        Option.getOrThrow(
          ConversionTest.node(BescriptConverter.parse(BescriptConverter.serialize(dotted))).call,
        ).functionName,
        'a.b',
      )
      for (const functionName of ['a b', 'a..b'])
        assert.throws((): string =>
          BescriptConverter.serialize({
            ...simple,
            nodes: [{ ...node, call: Option.some({ ...call, functionName }) }],
          }),
        )
      assert.throws((): string =>
        BescriptConverter.serialize({
          ...simple,
          nodes: [{ ...node, call: Option.some({ ...call, functionName: 'a\u00A0b' }) }],
        }),
      )
      const argument: CallArgument = Option.getOrThrow(Option.fromNullishOr(call.arguments[0]))
      const badArguments: readonly CallArgument[] = [
        { ...argument, decodedText: 'a\nb' },
        { ...argument, variable: true, typeName: 'var', decodedText: '' },
      ]
      for (const bad of badArguments)
        assert.throws((): string =>
          BescriptConverter.serialize({
            ...simple,
            nodes: [{ ...node, call: Option.some({ ...call, arguments: [bad] }) }],
          }),
        )
      assert.throws((): string =>
        BescriptConverter.serialize({
          ...simple,
          nodes: [
            {
              ...node,
              call: Option.some({ ...call, arguments: [{ ...argument, typeName: 'String:bad' }] }),
            },
          ],
        }),
      )
      assert.throws((): string =>
        BescriptConverter.serialize({
          ...simple,
          nodes: [
            {
              ...node,
              call: Option.some({
                ...call,
                kind: 'dvf',
                ownerType: Option.some('Device:bad'),
              }),
            },
          ],
        }),
      )
    })

    test('来源字段缺失、重复 ID 与越界区间不能掩盖 AST 编辑', (): void => {
      const ast: LocalAst = BescriptConverter.parse(
        'begincodeV2\nname:测试\nlines:\nfun a()\nendcode\n',
      )
      const source: ScriptSource = Option.getOrThrow(ast.source)
      const layout: NodeSource = Option.getOrThrow(Option.fromNullishOr(source.nodes[0]))
      const node: LocalAstNode = ConversionTest.node(ast)
      const call: CallSignature = Option.getOrThrow(node.call)
      const changed: LocalAst = {
        ...ast,
        nodes: [{ ...node, call: Option.some({ ...call, functionName: 'b' }) }],
      }
      assert.throws((): string =>
        BescriptConverter.serialize({
          ...changed,
          source: Option.some({ ...source, nodes: [{ ...layout, fields: [] }] }),
        }),
      )
      assert.throws((): string =>
        BescriptConverter.serialize({
          ...changed,
          source: Option.some({ ...source, nodes: [layout, layout] }),
        }),
      )
      assert.throws((): string =>
        BescriptConverter.serialize({
          ...changed,
          source: Option.some({
            ...source,
            nodes: [
              {
                ...layout,
                fields: layout.fields.map((field: SourceField): SourceField => ({
                  ...field,
                  end: layout.command.length + 1,
                })),
              },
            ],
          }),
        }),
      )
    })

    test('删除或改变头部来源关联不能复活已删除的语义', (): void => {
      const ast: LocalAst = BescriptConverter.parse(
        'begincodeV2\nname:测试\ncomment:旧注释\nlines:\nendcode\n',
      )
      const source: ScriptSource = Option.getOrThrow(ast.source)
      const changed: LocalAst = {
        ...ast,
        header: { ...ast.header, comment: Option.none() },
      }
      assert.equal(
        BescriptConverter.serialize(changed),
        'begincodeV2\nname:测试\n\nlines:\nendcode\n',
      )
      assert.throws((): string =>
        BescriptConverter.serialize({
          ...changed,
          source: Option.some({
            ...source,
            headers: source.headers.filter(
              (field: SourceField): boolean => field.key !== 'comment',
            ),
          }),
        }),
      )
      assert.throws((): string =>
        BescriptConverter.serialize({
          ...changed,
          source: Option.some({
            ...source,
            headers: source.headers.map((field: SourceField): SourceField => ({
              ...field,
              end: field.end + 1,
            })),
          }),
        }),
      )
    })

    test('全部真实工程脚本连续两轮字节级无损', async (): Promise<void> => {
      const directoryFullPath: string = resolve('test/data/projects')
      const entries: Dirent[] = await readdir(directoryFullPath, {
        recursive: true,
        withFileTypes: true,
      })
      const files: readonly Dirent[] = entries.filter(
        (entry: Dirent): boolean => entry.isFile() && entry.name.endsWith('.code'),
      )
      assert.equal(files.length, 2775)
      for (const file of files) {
        const fileFullPath: string = join(file.parentPath, file.name)
        const original: Buffer = await readFile(fileFullPath)
        try {
          const first: Buffer = Buffer.from(
            BescriptConverter.serialize(BescriptConverter.parse(original.toString('utf8'))),
            'utf8',
          )
          const second: Buffer = Buffer.from(
            BescriptConverter.serialize(BescriptConverter.parse(first.toString('utf8'))),
            'utf8',
          )
          assert.deepEqual(first, original)
          assert.deepEqual(second, original)
        } catch (failure: unknown) {
          throw new Error(fileFullPath, { cause: failure })
        }
      }
      console.info('转换测试实际写入：文件数=0，总字节=0，最大文件=0；累计写入=0')
    }, 60000)
  }

  private static node(ast: LocalAst): LocalAstNode {
    return Option.getOrThrow(Option.fromNullishOr(ast.nodes[0]))
  }

  private static roundtrip(source: string): void {
    const first: string = BescriptConverter.serialize(BescriptConverter.parse(source))
    const second: string = BescriptConverter.serialize(BescriptConverter.parse(first))
    assert.deepEqual(Buffer.from(first, 'utf8'), Buffer.from(source, 'utf8'))
    assert.deepEqual(Buffer.from(second, 'utf8'), Buffer.from(source, 'utf8'))
  }
}
