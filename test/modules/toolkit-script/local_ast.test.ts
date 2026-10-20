import { test } from 'vitest'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { Option } from 'effect'
import {
  CodeFileReader,
  type CodeFileDocument,
} from '../../../src/toolkit/script/code_file/CodeFile.ts'
import { LocalAstSchema } from '../../../src/toolkit/script/local_ast/LocalAst.ts'
import type {
  CallSignature,
  LocalAst,
  LocalAstNode,
} from '../../../src/toolkit/script/local_ast/LocalAst.ts'
import type { CallArgument } from '../../../src/toolkit/script/local_ast/LocalAst.ts'
import type { VarTypeName } from '../../../src/toolkit/script/vocabulary/Vocabulary.ts'

/** @anchor bescript-local-ast */
export class LocalAstTest {
  private constructor() {}

  static {
    test('块语句只接纳深一层的子调用', (): void => {
      const document: LocalAst = LocalAstTest.document()
      const ast: LocalAst = LocalAstSchema.parse(document)
      const root: LocalAstNode = Option.getOrThrow(Option.fromNullishOr(ast.nodes[0]))
      assert.equal(root.children.length, 1)
      assert.equal(ast.sourceText, document.sourceText)
      assert.throws(
        (): LocalAst =>
          LocalAstSchema.parse({
            ...document,
            nodes: [{ ...root, depth: 1, children: [LocalAstTest.call(2, Option.none())] }],
          }),
        { message: '根节点深度必须为 0' },
      )
      assert.throws((): LocalAst =>
        LocalAstSchema.parse({
          ...document,
          nodes: [
            {
              ...root,
              call: Option.some({
                ...Option.getOrThrow(root.call),
                kind: 'varf',
                ownerType: Option.some('NotAType'),
                ownerName: Option.some('对象'),
              }),
            },
          ],
        }),
      )
      assert.throws((): LocalAst =>
        LocalAstSchema.parse({
          ...document,
          nodes: [{ ...root, children: [LocalAstTest.call(3, Option.none())] }],
        }),
      )
    })

    test('属主、注释、返回值和参数结构按合同校验', (): void => {
      const document: LocalAst = LocalAstTest.document()
      const node: LocalAstNode = LocalAstTest.call(0, Option.none())
      const signature: CallSignature = LocalAstTest.signature(Option.none())
      assert.throws((): LocalAst =>
        LocalAstSchema.parse({
          ...document,
          nodes: [{ ...node, call: Option.some({ ...signature, functionName: 'a:b' }) }],
        }),
      )
      assert.throws((): LocalAst =>
        LocalAstSchema.parse({
          ...document,
          nodes: [
            {
              ...node,
              call: Option.some({
                ...signature,
                kind: 'varf',
                ownerType: Option.some('Struct'),
                ownerName: Option.some('a=b'),
              }),
            },
          ],
        }),
      )
      const platformCall: CallSignature = {
        ...signature,
        kind: 'dvf',
        ownerType: Option.some('PlatformType'),
        ownerName: Option.some('零件'),
        saveName: '零件.PlatformType.create@1bool',
      }
      assert.equal(
        LocalAstSchema.parse({ ...document, nodes: [{ ...node, call: Option.some(platformCall) }] })
          .nodes.length,
        1,
      )
      assert.throws((): LocalAst =>
        LocalAstSchema.parse({
          ...document,
          nodes: [{ ...node, call: Option.some({ ...signature, ownerName: Option.some('对象') }) }],
        }),
      )
      assert.throws((): LocalAst =>
        LocalAstSchema.parse({
          ...document,
          nodes: [{ ...node, kind: 'comment' }],
        }),
      )
      assert.throws((): LocalAst =>
        LocalAstSchema.parse({
          ...document,
          header: {
            ...document.header,
            parameters: [
              { typeName: 'Long', name: '数量' },
              { typeName: 'Long', name: '数量' },
            ],
          },
        }),
      )
      assert.throws((): LocalAst =>
        LocalAstSchema.parse({
          ...document,
          nodes: [
            {
              ...node,
              call: Option.some({
                ...signature,
                returnVariable: Option.some({ name: '结果', typeName: 'Bool' }),
                discardsReturn: true,
              }),
            },
          ],
        }),
      )
      const act: CallSignature = {
        ...signature,
        kind: 'act',
        ownerType: Option.some('1101'),
        ownerName: Option.some('this'),
      }
      assert.equal(
        LocalAstSchema.parse({ ...document, nodes: [{ ...node, call: Option.some(act) }] }).nodes
          .length,
        1,
      )
      assert.throws((): LocalAst =>
        LocalAstSchema.parse({
          ...document,
          nodes: [{ ...node, call: Option.some({ ...act, ownerType: Option.some('Canvas') }) }],
        }),
      )
    })

    test('真实返回脚本由正文绑定返回变量，不另造独立声明', async (): Promise<void> => {
      const document: CodeFileDocument = CodeFileReader.read(
        await readFile(
          "test/data/projects/姓氏玩法/map.map_/54/scripts/队伍结构'读取_score.code",
          'utf8',
        ),
      )
      const call: CallSignature = {
        kind: 'varf',
        functionName: 'getLong',
        saveName: 'Struct.结构.getLong',
        ownerType: Option.some('Struct'),
        ownerName: Option.some('结构'),
        ownerNameIsAlias: false,
        arguments: [
          {
            sourceRef: Option.none(),
            variable: false,
            typeName: 'String',
            rawText: 'score',
            decodedText: 'score',
            varType: Option.some('String'),
          },
          {
            sourceRef: Option.none(),
            variable: false,
            typeName: 'Bool',
            rawText: '0',
            decodedText: '0',
            varType: Option.some('Bool'),
          },
        ],
        returnVariable: Option.some({ name: 'ret', typeName: 'Long' }),
        discardsReturn: false,
      }
      const ast: LocalAst = LocalAstSchema.parse({
        source: Option.none(),
        version: document.version,
        header: document.header,
        sourceText: document.sourceText,
        nodes: [
          {
            sourceRef: Option.none(),
            kind: 'call',
            line: 1,
            depth: 0,
            disabled: false,
            text: document.sourceText.trimEnd(),
            children: [],
            call: Option.some(call),
          },
        ],
      })
      assert.deepEqual(LocalAstSchema.bindings(ast), [
        { name: '结构', typeName: 'Struct' },
        { name: 'ret', typeName: 'Long' },
      ])
      assert.deepEqual(ast.header.returnParameter, call.returnVariable)
      assert.equal(ast.version, document.version)
    })

    test('形参可作为返回来源，同类型重复赋值只形成一个绑定', (): void => {
      const document: LocalAst = LocalAstTest.document()
      const parameterReturn: LocalAst = LocalAstSchema.parse({
        ...document,
        nodes: [],
        header: {
          ...document.header,
          returnParameter: Option.some({ name: '数量', typeName: 'Long' }),
        },
      })
      assert.deepEqual(LocalAstSchema.bindings(parameterReturn), [
        { name: '数量', typeName: 'Long' },
      ])
      const repeated: LocalAst = LocalAstSchema.parse({
        ...document,
        nodes: [
          LocalAstTest.call(0, Option.some({ name: '结果', typeName: 'Bool' })),
          LocalAstTest.call(0, Option.some({ name: '结果', typeName: 'Bool' })),
        ],
        header: {
          ...document.header,
          returnParameter: Option.some({ name: '结果', typeName: 'Bool' }),
        },
      })
      assert.equal(LocalAstSchema.bindings(repeated).length, 2)
      const reassigned: LocalAst = LocalAstSchema.parse({
        ...repeated,
        nodes: [
          ...repeated.nodes,
          LocalAstTest.call(0, Option.some({ name: '结果', typeName: 'Long' })),
        ],
      })
      assert.deepEqual(LocalAstSchema.bindings(reassigned), LocalAstSchema.bindings(repeated))
      const returned: LocalAst = LocalAstSchema.parse({
        ...repeated,
        header: {
          ...repeated.header,
          returnParameter: Option.some({ name: '结果', typeName: 'Long' }),
        },
      })
      assert.deepEqual(LocalAstSchema.bindings(returned), LocalAstSchema.bindings(repeated))
      assert.throws((): LocalAst => LocalAstSchema.parse({ ...document, declaredVariables: [] }))
    })

    test('来源正文保留空行、多行内联内容、禁用前缀和块尾', (): void => {
      const sourceText: string =
        '\n/*if{fun create.bool(Bool 1)\n  fun create.canvas(Canvas {\n    nodes: []\n  })\n}\n'
      const ast: LocalAst = LocalAstSchema.parse({ ...LocalAstTest.document(), sourceText })
      assert.equal(ast.sourceText, sourceText)
      assert.throws((): LocalAst => LocalAstSchema.parse({ ...ast, sourceText: null }))
    })

    test('具体 UI 绑定接受以通用 UI 方法返回类型表达的再次赋值', (): void => {
      const document: LocalAst = LocalAstTest.document()
      const values: readonly (readonly [VarTypeName, string])[] = [
        ['Vector3', '0.2,0.6,0'],
        ['Vector3', '100,60,0'],
        ['Float', '10'],
        ['Float', '5'],
        ['Color', '255,255,255,255'],
      ]
      const callArguments: readonly CallArgument[] = values.map(
        ([typeName, rawText]: readonly [VarTypeName, string]): CallArgument => ({
          sourceRef: Option.none(),
          variable: false,
          typeName,
          rawText,
          decodedText: rawText,
          varType: Option.some(typeName),
        }),
      )
      const created: LocalAstNode = {
        ...LocalAstTest.call(0, Option.some({ name: 'b', typeName: 'UIRect' })),
        line: 1,
        text: 'UIRect b=fun ui.rect(Vector3 0.2,0.6,0:Vector3 100,60,0:Float 10:Float 5:Color 255,255,255,255)',
        call: Option.some({
          ...LocalAstTest.signature(Option.some({ name: 'b', typeName: 'UIRect' })),
          functionName: 'ui.rect',
          saveName: 'ui.rect',
          arguments: callArguments,
        }),
      }
      const copied: LocalAstNode = {
        ...LocalAstTest.call(0, Option.some({ name: 'b', typeName: 'UI' })),
        line: 31,
        text: 'UI b=varf UIRect.b.copy()',
        call: Option.some({
          ...LocalAstTest.signature(Option.some({ name: 'b', typeName: 'UI' })),
          kind: 'varf',
          functionName: 'copy',
          saveName: 'UIRect.b.copy',
          ownerType: Option.some('UIRect'),
          ownerName: Option.some('b'),
          arguments: [],
        }),
      }
      const ast: LocalAst = LocalAstSchema.parse({ ...document, nodes: [created, copied] })
      assert.deepEqual(LocalAstSchema.bindings(ast), [
        { name: '数量', typeName: 'Long' },
        { name: 'b', typeName: 'UIRect' },
      ])
      assert.equal(Option.getOrThrow(Option.getOrThrow(copied.call).returnVariable).typeName, 'UI')
    })
  }

  private static document(): LocalAst {
    return {
      source: Option.none(),
      version: 'v2',
      sourceText: 'if{fun create.bool(Bool 1)\n  fun create.bool(Bool 1)',
      header: {
        name: '方法',
        comment: Option.none(),
        debug: false,
        skip: false,
        isPrivate: false,
        guid: Option.none(),
        color: Option.none(),
        parameters: [{ typeName: 'Long', name: '数量' }],
        returnParameter: Option.none(),
      },
      nodes: [
        {
          sourceRef: Option.none(),
          kind: 'if',
          line: 1,
          depth: 0,
          disabled: false,
          text: 'if{fun create.bool(Bool 1)',
          children: [LocalAstTest.call(1, Option.none())],
          call: Option.some(LocalAstTest.signature(Option.none())),
        },
      ],
    }
  }

  private static call(depth: number, returned: CallSignature['returnVariable']): LocalAstNode {
    return {
      sourceRef: Option.none(),
      kind: 'call',
      line: 2,
      depth,
      disabled: false,
      text: 'fun create.bool(Bool 1)',
      children: [],
      call: Option.some(LocalAstTest.signature(returned)),
    }
  }

  private static signature(returned: CallSignature['returnVariable']): CallSignature {
    return {
      kind: 'fun',
      functionName: 'create.bool',
      saveName: 'create.bool',
      ownerType: Option.none(),
      ownerName: Option.none(),
      ownerNameIsAlias: false,
      arguments: [
        {
          sourceRef: Option.none(),
          variable: false,
          typeName: 'Bool',
          rawText: '1',
          decodedText: '1',
          varType: Option.some('Bool'),
        },
      ],
      returnVariable: returned,
      discardsReturn: false,
    }
  }
}
