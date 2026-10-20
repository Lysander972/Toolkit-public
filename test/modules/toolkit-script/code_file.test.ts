import { test } from 'vitest'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { Option } from 'effect'
import { CodeFileReader } from '../../../src/toolkit/script/code_file/CodeFile.ts'
import type { CodeFileDocument } from '../../../src/toolkit/script/code_file/CodeFile.ts'

/** @anchor bescript-code-file */
export class CodeFileTest {
  private constructor() {}

  static {
    test('真实脚本保留头部、参数和正文', async (): Promise<void> => {
      const score: CodeFileDocument = CodeFileReader.read(
        await readFile(
          "test/data/projects/姓氏玩法/map.map_/54/scripts/队伍结构'读取_score.code",
          'utf8',
        ),
      )
      assert.equal(score.version, 'v2')
      assert.equal(score.header.name, "队伍结构'读取_score")
      assert.equal(Option.isNone(score.header.comment), true)
      assert.equal(Option.isNone(score.header.guid), true)
      assert.equal(Option.isSome(score.header.color), true)
      if (Option.isSome(score.header.color)) {
        assert.deepEqual(score.header.color.value, { red: 255, green: 255, blue: 255 })
      }
      assert.deepEqual(score.header.parameters, [{ typeName: 'Struct', name: '结构' }])
      assert.equal(Option.isSome(score.header.returnParameter), true)
      if (Option.isSome(score.header.returnParameter)) {
        assert.deepEqual(score.header.returnParameter.value, { typeName: 'Long', name: 'ret' })
      }
      assert.equal(score.sourceText, 'Long ret=varf Struct.结构.getLong(String score:Bool 0)\n')

      const wrapped: CodeFileDocument = CodeFileReader.read(
        await readFile(
          "test/data/projects/姓氏玩法/map.map_/54/scripts/队伍'施加效果_封装.code",
          'utf8',
        ),
      )
      assert.equal(wrapped.header.isPrivate, true)
      assert.equal(Option.isSome(wrapped.header.comment), true)
      if (Option.isSome(wrapped.header.comment)) {
        assert.equal(wrapped.header.comment.value, '在此处完成类型转换')
      }
      assert.equal(wrapped.header.parameters.length, 9)
      assert.equal(Option.isSome(wrapped.header.color), true)
      if (Option.isSome(wrapped.header.color)) {
        assert.deepEqual(wrapped.header.color.value, { red: 123, green: 255, blue: 0 })
      }

      const repository: CodeFileDocument = CodeFileReader.read(
        await readFile('test/data/projects/方法汇总/全局方法_地图123.code', 'utf8'),
      )
      assert.equal(Option.isSome(repository.header.guid), true)
      if (Option.isSome(repository.header.guid)) {
        assert.equal(repository.header.guid.value, '772291a5de9342869a39f38064072c36')
      }
      assert.equal(repository.sourceText.includes('fun map.InitAstarPathFinder'), true)
    })

    test('参数续行在下一个头部字段结束，多行正文逐字保留', (): void => {
      const body: string = '  //注释\n/*fun create.canvas(Canvas {\n  nodes: []\n})\n'
      const document: CodeFileDocument = CodeFileReader.read(
        'begincode\nname:方法\nparams:Long 数量\nBool 启用\ncomment:说明\nlines:\n' +
          body +
          'endcode\n',
      )
      assert.equal(document.version, 'v1')
      assert.deepEqual(document.header.parameters, [
        { typeName: 'Long', name: '数量' },
        { typeName: 'Bool', name: '启用' },
      ])
      assert.equal(document.sourceText, body)
      assert.throws(
        (): CodeFileDocument =>
          CodeFileReader.read(
            'begincodeV2\nname:方法\nparams:Long 数量\ncomment:说明\nBool 启用\nlines:\nendcode\n',
          ),
        { message: 'BEScript 头部存在无法识别的行' },
      )
    })

    test('缺结束行、重复名称或非法颜色直接失败', (): void => {
      assert.throws((): CodeFileDocument =>
        CodeFileReader.read('begincodeV2\nname:方法\nlines:\nfun a()\n'),
      )
      assert.throws((): CodeFileDocument =>
        CodeFileReader.read('begincodeV2\nname:方法\nname:另一\nlines:\nendcode\n'),
      )
      assert.throws((): CodeFileDocument =>
        CodeFileReader.read('begincode\nname:方法\ncolor:1,2\nlines:\nendcode\n'),
      )
      assert.throws((): CodeFileDocument =>
        CodeFileReader.read('begincodeV2\nname:a:b\nlines:\nendcode\n'),
      )
    })
  }
}
