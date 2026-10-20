import { test } from 'vitest'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { Option } from 'effect'
import {
  ScriptListSchema,
  type ScriptEntry,
  type ScriptGroup,
} from '../../../src/toolkit/script/device_context/ScriptList.ts'

/** @anchor bescript-script-list */
export class ScriptListTest {
  private constructor() {}

  static {
    test('真实清单保留仓库 GUID、引用私有标记和内联条目', async (): Promise<void> => {
      const group: ScriptGroup<ScriptEntry> = ScriptListSchema.read({
        directoryName: 'scripts',
        listText: await readFile(
          'test/data/projects/姓氏玩法/map.map_/54/scripts/list.json',
          'utf8',
        ),
      })
      const linked: readonly ScriptEntry[] = group.entries.filter(
        (entry: ScriptEntry): boolean => entry.name === "玩家结构'更新_completeness",
      )
      assert.deepEqual(linked, [
        {
          origin: 'repository',
          name: "玩家结构'更新_completeness",
          guid: Option.some('8ee0aade18b64e94929778e83858a6ef'),
          isPrivate: true,
        },
      ])
      assert.equal(group.kind, 'custom')
      assert.deepEqual(group.eventName, Option.none())
      assert.equal(
        group.entries.some((entry: ScriptEntry): boolean => entry.origin === 'inline'),
        true,
      )
    })

    test('事件目录与清单顺序保留，非法目录与 GUID 不接受', async (): Promise<void> => {
      const group: ScriptGroup<ScriptEntry> = ScriptListSchema.read({
        directoryName: 'event_clientStart',
        listText: await readFile(
          'test/data/projects/姓氏玩法/test.map_/2/event_clientStart/list.json',
          'utf8',
        ),
      })
      assert.deepEqual(group.eventName, Option.some('clientStart'))
      assert.deepEqual(
        group.entries.map((entry: ScriptEntry): string => entry.name),
        ['初始化基础设定', '方法1', '方法2'],
      )
      assert.throws((): ScriptGroup<ScriptEntry> =>
        ScriptListSchema.read({ directoryName: 'notes', listText: '[]' }),
      )
      assert.throws((): ScriptGroup<ScriptEntry> =>
        ScriptListSchema.read({ directoryName: 'event_', listText: '[]' }),
      )
      assert.throws((): ScriptGroup<ScriptEntry> =>
        ScriptListSchema.read({
          directoryName: 'scripts',
          listText: JSON.stringify([{ name: 'A', guid: 'abc' }]),
        }),
      )
      assert.throws((): ScriptGroup<ScriptEntry> =>
        ScriptListSchema.read({
          directoryName: 'scripts',
          listText: JSON.stringify([
            { name: 'A', guid: '0123456789abcdef0123456789abcdef', unknown: 1 },
          ]),
        }),
      )
    })
  }
}
