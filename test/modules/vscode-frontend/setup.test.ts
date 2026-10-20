import assert from 'node:assert/strict'
import { test } from 'vitest'
import { mkdir, mkdtemp, readFile, rm, stat, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { Option } from 'effect'
import { OneClickSetup } from '../../../src/frontend/host_client/OneClickSetup.ts'
import type { SetupPlan } from '../../../src/frontend/host_client/OneClickSetup.ts'
import { SetupTargets } from '../../../src/frontend/host_client/SetupTargets.ts'
import { ConfigurationEditor } from '../../../src/frontend/host_client/ConfigurationEditor.ts'
import type { SetupTarget } from '../../../src/frontend/host_client/SetupTargets.ts'

/** @anchor frontend-setup */
export class OneClickSetupTest {
  private constructor() {}

  static {
    test('真实安装只改管理字段和 Skill，重复执行不写入', async (): Promise<void> => {
      await mkdir('_build/tests', { recursive: true })
      const directoryFullPath: string = await mkdtemp(resolve('_build/tests/frontend-setup-'))
      const skillText: string = '# MechToolkit\n'
      const original: string = '{"unmanaged":{"enabled":true}}'
      let fileCount: number = 0
      let bytes: number = 0
      let maximum: number = 0
      try {
        const skillSourceFullPath: string = join(directoryFullPath, 'source')
        const homeFullPath: string = join(directoryFullPath, 'home')
        await mkdir(skillSourceFullPath)
        await mkdir(homeFullPath)
        await writeFile(join(skillSourceFullPath, 'SKILL.md'), skillText)
        fileCount += 1
        bytes += Buffer.byteLength(skillText)
        maximum = Buffer.byteLength(skillText)
        const target: SetupTarget = Option.getOrThrow(
          Option.fromNullishOr(
            SetupTargets.forEndpoint('http://127.0.0.1:5050/mcp').find(
              (entry: SetupTarget): boolean => entry.id === 'trae',
            ),
          ),
        )
        const configFullPath: string = join(directoryFullPath, target.configRelativePath)
        await mkdir(join(directoryFullPath, '.trae'))
        await writeFile(configFullPath, original)
        fileCount += 1
        bytes += Buffer.byteLength(original)
        maximum = Math.max(maximum, Buffer.byteLength(original))
        const setup: OneClickSetup = new OneClickSetup({
          workspaceFullPath: directoryFullPath,
          homeFullPath,
          skillSourceFullPath,
        })
        const plan: SetupPlan = await setup.plan(target)
        assert.equal(await setup.apply(plan), 2)
        fileCount += 2
        bytes += Buffer.byteLength(plan.configuration.next) + Buffer.byteLength(skillText)
        maximum = Math.max(maximum, Buffer.byteLength(plan.configuration.next))
        assert.equal(
          await readFile(join(homeFullPath, '.trae/skills/mechtoolkit/SKILL.md'), 'utf8'),
          skillText,
        )
        assert.deepEqual(JSON.parse(await readFile(configFullPath, 'utf8')), {
          unmanaged: { enabled: true },
          mcpServers: { mechtoolkit: { type: 'http', url: 'http://127.0.0.1:5050/mcp' } },
        })
        const modifiedAt: number = (await stat(configFullPath)).mtimeMs
        const again: SetupPlan = await setup.plan(target)
        assert.equal(await setup.apply(again), 0)
        assert.equal((await stat(configFullPath)).mtimeMs, modifiedAt)
        for (const pendingTarget of SetupTargets.forEndpoint('http://127.0.0.1:5050/mcp').filter(
          (entry: SetupTarget): boolean =>
            ['gemini', 'openCode', 'factoryDroid', 'kimiCode', 'cline', 'kiloCode'].includes(
              entry.id,
            ),
        )) {
          const pendingPlan: SetupPlan = await setup.plan(pendingTarget)
          await assert.rejects(setup.apply(pendingPlan), /TODO:.*正式 CLI/)
          assert.deepEqual(
            await ConfigurationEditor.read(pendingPlan.configuration.fileFullPath),
            Option.none(),
          )
          assert.deepEqual(
            await ConfigurationEditor.read(join(pendingPlan.skillDestinationFullPath, 'SKILL.md')),
            Option.none(),
          )
        }
      } finally {
        await rm(directoryFullPath, { recursive: true })
        process.stdout.write(
          `前端配置测试实际写入：${String(fileCount)} 文件，累计 ${String(bytes)} 字节，最大 ${String(maximum)} 字节；重复执行新增 0 字节\n`,
        )
      }
    })
  }
}
