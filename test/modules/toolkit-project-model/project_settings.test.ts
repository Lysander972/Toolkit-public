import { test } from 'vitest'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { ProjectSettingsParser } from '../../../src/toolkit/project_model/ProjectSettings.ts'
import type { ProjectSettings } from '../../../src/toolkit/project_model/ProjectSettings.ts'

/** @anchor project-settings */
export class ProjectSettingsTest {
  private constructor() {}

  static {
    test('真实 CreatAI Hjson 工程设置可读取，空名称保持原值', async (): Promise<void> => {
      const source: string = await readFile('test/data/projects/方法汇总/setting.json', 'utf8')
      const settings: ProjectSettings = ProjectSettingsParser.parse(source)
      assert.deepEqual(settings, {
        guid: 'd767593340204fb48414787ffd23e624',
        name: '',
        mainMap: '',
      })
      assert.equal(ProjectSettingsParser.parse(source).guid, settings.guid)
    })

    test('缺失工程身份或字段类型错误直接失败', (): void => {
      assert.throws((): ProjectSettings => ProjectSettingsParser.parse('name: 工程\nmainMap: ""'))
      assert.throws((): ProjectSettings =>
        ProjectSettingsParser.parse('{"guid":12,"name":"","mainMap":""}'),
      )
    })
  }
}
