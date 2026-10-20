import { test } from 'vitest'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { Option } from 'effect'
import { z } from 'zod'
import { GlobalVariableSchema } from '../../../src/toolkit/script/device_context/GlobalVariables.ts'
import type { GlobalVariable } from '../../../src/toolkit/script/device_context/GlobalVariables.ts'

/** @anchor bescript-global-variable */
export class GlobalVariableTest {
  private constructor() {}

  static {
    test('真实 vars.json 保留名称、类型和注释', async (): Promise<void> => {
      const source: string = await readFile(
        'test/data/projects/姓氏玩法/map.map_/54/vars.json',
        'utf8',
      )
      const variables: readonly GlobalVariable[] = GlobalVariableSchema.read(source)
      assert.deepEqual(GlobalVariableSchema.parse(variables), variables)
      const named: GlobalVariable = GlobalVariableTest.named(variables, "UI_局内'主布局_2D")
      assert.equal(named.typeName, 'Canvas')
      assert.equal(Option.isSome(named.comment), true)
      if (Option.isSome(named.comment)) {
        assert.equal(named.comment.value, '玩法页')
      }
      const frameRate: GlobalVariable = GlobalVariableTest.named(variables, "UI'动图_帧率表")
      assert.equal(frameRate.typeName, 'List')
      assert.equal(Option.isNone(frameRate.comment), true)
    })

    test('重复名称、未知字段或词表外类型直接失败', (): void => {
      assert.throws(
        (): readonly GlobalVariable[] =>
          GlobalVariableSchema.read(
            JSON.stringify([
              { name: 'a', type: 'Long' },
              { name: 'a', type: 'Float' },
            ]),
          ),
        { message: '全局变量名重复: a' },
      )
      assert.throws(
        (): readonly GlobalVariable[] =>
          GlobalVariableSchema.read(JSON.stringify([{ name: 'a', type: 'Long', extra: 1 }])),
        { message: '全局变量出现未定义字段: extra' },
      )
      assert.throws(
        (): readonly GlobalVariable[] =>
          GlobalVariableSchema.read(JSON.stringify([{ name: 'a', type: 'Void' }])),
        z.ZodError,
      )
      assert.throws(
        (): readonly GlobalVariable[] =>
          GlobalVariableSchema.read(JSON.stringify([{ name: 'a:b', type: 'Long' }])),
        z.ZodError,
      )
      assert.throws(
        (): readonly GlobalVariable[] =>
          GlobalVariableSchema.read(JSON.stringify([{ name: 'a', type: 'Long', comment: null }])),
        z.ZodError,
      )
    })
  }

  private static named(variables: readonly GlobalVariable[], name: string): GlobalVariable {
    for (const variable of variables) {
      if (variable.name === name) {
        return variable
      }
    }
    throw new Error('未找到全局变量 ' + name)
  }
}
