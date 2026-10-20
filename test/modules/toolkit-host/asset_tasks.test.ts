import assert from 'node:assert/strict'
import { mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { Readable } from 'node:stream'
import pino from 'pino'
import { test } from 'vitest'
import { Host } from '../../../src/toolkit/host/Host.ts'
import type { AssetTaskCommand } from '../../../src/toolkit/assets/AssetTaskContracts.ts'
import {
  SubmitAssetTask,
  ReadAssetTaskStatus,
  ReadAssetTaskResult,
  CancelAssetTask,
  DiscardAssetTask,
  RenderAssetSlice,
  ReviewAssetTask,
  ConfirmAssetTask,
} from '../../../src/toolkit/assets/AssetTaskContracts.ts'
import { ImageRequirement } from '../../../src/toolkit/asset_processing/AssetProcessing.ts'
import {
  NumberGenerationParameter,
  TextGenerationParameter,
  BooleanGenerationParameter,
} from '../../../src/toolkit/asset_generation/GenerationContracts.ts'

/**
 * @anchor asset-service-contract
 * @anchor asset-task-lifecycle-ports
 * @anchor asset-task-review-ports
 * @anchor asset-task-confirm-port
 * @anchor asset-task-lifecycle-input
 * @anchor asset-task-artifact-input
 * @anchor host-asset-dispatch
 */
export class AssetTasksTest {
  private constructor() {}

  static {
    test('同一 Host 的八类资产命令逐项失败，保持工程且关闭后拒绝执行', async (): Promise<void> => {
      const pngBytes: Uint8Array = new Uint8Array(
        await readFile('res/frontend/assets/setup-icons/typescript.png'),
      )
      const originalBytes: Uint8Array = new Uint8Array(pngBytes)
      await mkdir('_build/tests', { recursive: true })
      const workspaceFullPath: string = await mkdtemp(resolve('_build/tests/asset-tasks-'))
      const content: Readable = Readable.from([pngBytes])
      try {
        const originalSettings: string = 'guid: asset-tasks\nname: 原始\nmainMap: ""'
        await writeFile(join(workspaceFullPath, 'setting.json'), originalSettings, 'utf8')
        const host: Host = await Host.open(workspaceFullPath, 'memory', pino({ enabled: false }))
        const signal: AbortSignal = new AbortController().signal
        const cases: readonly (readonly [AssetTaskCommand, string])[] = [
          [
            new SubmitAssetTask(
              {
                capability: 'image-generation',
                prompt: '生成贴图',
                seed: 42,
                parameters: {
                  strength: new NumberGenerationParameter(0.5),
                  caption: new TextGenerationParameter('原始构图'),
                  preserveAlpha: new BooleanGenerationParameter(true),
                },
                inputs: [
                  {
                    artifactId: 'source',
                    name: 'source.png',
                    mimeType: 'image/png',
                    format: 'png',
                    content,
                  },
                ],
              },
              [new ImageRequirement('png', 2, 2, true)],
            ),
            'TODO: 资产任务提交',
          ],
          [new ReadAssetTaskStatus('task'), 'TODO: 资产任务状态'],
          [new ReadAssetTaskResult('task'), 'TODO: 资产任务结果'],
          [new CancelAssetTask('task'), 'TODO: 资产任务取消'],
          [new DiscardAssetTask('task'), 'TODO: 资产候选丢弃'],
          [
            new RenderAssetSlice(
              'task',
              'selected',
              {
                slicedBorderLB: { x: 1, y: 2, z: 0 },
                slicedBorderRT: { x: 3, y: 4, z: 0 },
                slicedRectLB: { x: 5, y: 6, z: 0 },
                slicedRectRT: { x: 7, y: 8, z: 0 },
              },
              [{ width: 32, height: 24 }],
            ),
            'TODO: 资产切片预览',
          ],
          [new ReviewAssetTask('task', 'selected', '检查透明边缘'), 'TODO: 资产候选评阅'],
          [
            new ConfirmAssetTask('task', 'selected', 'image/selected.png', '确认入库选定产物'),
            'TODO: 资产任务确认与编辑业务入库候选',
          ],
        ]
        try {
          for (const [command, expectedError] of cases) {
            const original: string = JSON.stringify(command)
            Object.freeze(command)
            await assert.rejects(
              host.assetTask(command, signal),
              (error: unknown): boolean =>
                error instanceof Error && error.message === expectedError,
            )
            assert.equal(JSON.stringify(command), original)
          }
          const chunks: Uint8Array[] = []
          const bytes: AsyncIterable<Uint8Array> = content
          for await (const chunk of bytes) chunks.push(chunk)
          assert.deepEqual(chunks, [originalBytes])
          assert.equal(signal.aborted, false)
          assert.deepEqual(await readdir(workspaceFullPath), ['setting.json'])
          assert.equal(
            await readFile(join(workspaceFullPath, 'setting.json'), 'utf8'),
            originalSettings,
          )
        } finally {
          await host.close()
        }
        await assert.rejects(
          host.assetTask(new ReadAssetTaskStatus('task'), signal),
          /Host 已关闭/u,
        )
      } finally {
        content.destroy()
        await rm(workspaceFullPath, { recursive: true })
      }
    })
  }
}
