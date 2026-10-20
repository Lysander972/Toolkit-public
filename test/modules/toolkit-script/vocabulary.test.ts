import { test } from 'vitest'
import assert from 'node:assert/strict'
import { GuidFormat } from '../../../src/toolkit/project_model/GuidFormat.ts'
import { BescriptVocabulary } from '../../../src/toolkit/script/vocabulary/Vocabulary.ts'
import type { VarTypeName } from '../../../src/toolkit/script/vocabulary/Vocabulary.ts'

/** @anchor bescript-vocabulary */
export class BescriptVocabularyTest {
  private constructor() {}

  static {
    test('转义与 GUID 引用按 BEScript 约定往返', (): void => {
      assert.equal(BescriptVocabulary.decode('a@1b@3c@@d'), 'a:b=c@d')
      assert.equal(BescriptVocabulary.decode('@2@6@8@9'), ';/ .')
      assert.equal(BescriptVocabulary.decode('@@1@x@'), '@1@x@')
      assert.equal(BescriptVocabulary.encode('a:b=c@d()'), 'a@1b@3c@@d@4@5')
      assert.equal(BescriptVocabulary.encode('a\r\nb'), 'ab')
      const guid: string = '0123456789abcdef0123456789abcdef'
      assert.equal(BescriptVocabulary.encode('==guid:' + guid + '=='), '==guid@1' + guid + '==')
      assert.equal(GuidFormat.validate(guid), true)
      assert.equal(GuidFormat.validate(guid.toUpperCase()), false)
      assert.equal(BescriptVocabulary.rejectsEscaped('队伍'), false)
      assert.equal(BescriptVocabulary.rejectsEscaped('a:b'), true)
      assert.throws((): unknown =>
        BescriptVocabulary.parameter.parse({ typeName: 'Long', name: 'x', extra: 'lost' }),
      )
      assert.throws((): unknown =>
        BescriptVocabulary.color.parse({ red: 1, green: 2, blue: 3, extra: 'lost' }),
      )
    })

    test('变量类型只接受词表内的名称', (): void => {
      const parsed: VarTypeName = BescriptVocabulary.varType.parse('Canvas')
      assert.equal(parsed, 'Canvas')
      assert.equal(BescriptVocabulary.globalVarType.parse('Long'), 'Long')
      assert.throws((): VarTypeName => BescriptVocabulary.varType.parse('Widget'))
      assert.throws((): unknown => BescriptVocabulary.globalVarType.parse('Void'))
    })
  }
}
