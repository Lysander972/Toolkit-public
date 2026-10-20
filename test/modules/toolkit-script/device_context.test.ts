import { test } from 'vitest'
import assert from 'node:assert/strict'
import { Option } from 'effect'
import {
  CodeFileReader,
  type CodeFileDocument,
} from '../../../src/toolkit/script/code_file/CodeFile.ts'
import {
  DeviceContextSchema,
  type BescriptDeviceContext,
  type DeviceFunction,
} from '../../../src/toolkit/script/device_context/DeviceContext.ts'
import { DeviceMetadataSchema } from '../../../src/toolkit/script/device_context/DeviceMetadata.ts'
import {
  ScriptListSchema,
  type ScriptEntry,
  type ScriptGroup,
} from '../../../src/toolkit/script/device_context/ScriptList.ts'
import { LocalAstSchema, type LocalAst } from '../../../src/toolkit/script/local_ast/LocalAst.ts'

/** @anchor bescript-device-context */
export class DeviceContextTest {
  private constructor() {}

  static {
    test('不同引用名共享源码 AST，引用私有标记与源码私有标记分别保留', (): void => {
      const source: BescriptDeviceContext = DeviceContextTest.context()
      const context: BescriptDeviceContext = DeviceContextSchema.parse(source)
      assert.deepEqual(context, source)
      const entries: readonly DeviceFunction[] = DeviceContextTest.group(context).entries
      const first: DeviceFunction = Option.getOrThrow(Option.fromNullishOr(entries[0]))
      const second: DeviceFunction = Option.getOrThrow(Option.fromNullishOr(entries[1]))
      assert.equal(first.binding.name, 'Hello')
      assert.equal(second.binding.name, 'Tick')
      assert.equal(first.ast, second.ast)
      assert.equal(second.ast.header.name, 'Shared')
      assert.equal(second.binding.isPrivate, true)
      assert.equal(second.ast.header.isPrivate, false)
      assert.equal(second.ast.nodes.length, 1)
      assert.deepEqual(DeviceContextSchema.parse(context), context)
    })

    test('脚本组和条目顺序由唯一数组表达，空事件组保留', (): void => {
      const source: BescriptDeviceContext = DeviceContextTest.context()
      const group: ScriptGroup<DeviceFunction> = DeviceContextTest.group(source)
      const event: ScriptGroup<DeviceFunction> = {
        kind: 'event',
        eventName: Option.some('clientStart'),
        entries: [],
      }
      const context: BescriptDeviceContext = DeviceContextSchema.parse({
        ...source,
        scriptGroups: [event, { ...group, entries: [...group.entries].reverse() }],
      })
      assert.deepEqual(context.scriptGroups[0], event)
      const reordered: ScriptGroup<DeviceFunction> = Option.getOrThrow(
        Option.fromNullishOr(context.scriptGroups[1]),
      )
      assert.deepEqual(
        reordered.entries.map((item: DeviceFunction): string => item.binding.name),
        ['Tick', 'Hello'],
      )
    })

    test('重复组、组内重名、错误组种类和错误绑定直接失败', (): void => {
      const source: BescriptDeviceContext = DeviceContextTest.context()
      const group: ScriptGroup<DeviceFunction> = DeviceContextTest.group(source)
      assert.throws((): BescriptDeviceContext =>
        DeviceContextSchema.parse({ ...source, scriptGroups: [group, group] }),
      )
      assert.throws((): BescriptDeviceContext =>
        DeviceContextSchema.parse({
          ...source,
          scriptGroups: [{ ...group, entries: [...group.entries, ...group.entries] }],
        }),
      )
      const eventContext: BescriptDeviceContext = DeviceContextSchema.parse({
        ...source,
        scriptGroups: [
          {
            kind: 'event',
            eventName: Option.some('clientStart'),
            entries: DeviceContextTest.group(source).entries,
          },
        ],
      })
      const eventGroup: ScriptGroup<DeviceFunction> = DeviceContextTest.group(eventContext)
      assert.equal(eventGroup.entries.length, 2)
      const eventFunction: DeviceFunction = Option.getOrThrow(
        Option.fromNullishOr(eventGroup.entries[0]),
      )
      assert.equal(eventFunction.binding.isPrivate, false)
      assert.equal(eventFunction.ast.header.isPrivate, false)
      assert.equal(eventFunction.ast, Option.getOrThrow(Option.fromNullishOr(group.entries[0])).ast)
      assert.throws((): BescriptDeviceContext =>
        DeviceContextSchema.parse({
          ...source,
          scriptGroups: [{ ...group, kind: 'event' }],
        }),
      )
      assert.throws((): BescriptDeviceContext =>
        DeviceContextSchema.parse({ ...source, deviceText: 'type:1\n' }),
      )
      assert.throws((): BescriptDeviceContext =>
        DeviceContextSchema.parse({
          ...source,
          scriptGroups: [
            {
              ...group,
              entries: [
                {
                  binding: {
                    origin: 'repository',
                    name: 'Hello',
                    guid: Option.none(),
                    isPrivate: false,
                  },
                  ast: DeviceContextTest.ast(),
                },
              ],
            },
          ],
        }),
      )
    })
  }

  private static context(): BescriptDeviceContext {
    const ast: LocalAst = DeviceContextTest.ast()
    const group: ScriptGroup<ScriptEntry> = ScriptListSchema.read({
      directoryName: 'scripts',
      listText: JSON.stringify([
        { name: 'Hello', guid: '0123456789abcdef0123456789abcdef' },
        { name: 'Tick', guid: '0123456789abcdef0123456789abcdef', private: true },
      ]),
    })
    return {
      metadata: DeviceMetadataSchema.read('type:1\n'),
      globalVariables: [],
      scriptGroups: [
        {
          ...group,
          entries: group.entries.map((binding: ScriptEntry): DeviceFunction => ({ binding, ast })),
        },
      ],
    }
  }

  private static ast(): LocalAst {
    const document: CodeFileDocument = CodeFileReader.read(
      'begincodeV2\nguid:0123456789abcdef0123456789abcdef\nname:Shared\nlines:\nfun debug.enable(Bool 1)\nendcode\n',
    )
    return LocalAstSchema.parse({
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
          call: Option.some({
            kind: 'fun',
            functionName: 'debug.enable',
            saveName: 'debug.enable',
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
            returnVariable: Option.none(),
            discardsReturn: false,
          }),
        },
      ],
    })
  }

  private static group(context: BescriptDeviceContext): ScriptGroup<DeviceFunction> {
    return Option.getOrThrow(Option.fromNullishOr(context.scriptGroups[0]))
  }
}
