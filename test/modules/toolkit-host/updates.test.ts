import assert from 'node:assert/strict'
import { readFile, readdir } from 'node:fs/promises'
import { join } from 'node:path'
import { test, vi } from 'vitest'
import { Effect, Scope } from 'effect'
import type { CanvasKit } from 'canvaskit-wasm'
import pino from 'pino'
import { Host } from '../../../src/toolkit/host/Host.ts'
import {
  WorkspaceUpdates,
  type WorkspaceRefreshRequest,
  type OpenCanvasPreviewRequest,
} from '../../../src/toolkit/host/WorkspaceUpdates.ts'
import { ProjectEntities } from '../../../src/toolkit/entities/ProjectEntities.ts'
import type { ViewMode } from '../../../src/toolkit/views/ProjectViews.ts'
import { FileTestWorkspace } from '../toolkit-file-support/FileTestWorkspace.ts'
import { CpuTestBackend } from '../canvas-render-support/CpuTestBackend.ts'

/**
 * @anchor runtime-convergence-contract
 * @anchor workspace-view-mode-loop
 * @anchor derived-view-reconciliation
 * @anchor preview-resource-refresh
 * @anchor workspace-refresh-phases
 * @anchor workspace-entity-refresh-phases
 */
export class WorkspaceUpdatesTest {
  private constructor() {}

  static {
    const modes: readonly ViewMode[] = ['files', 'memory']
    test.each(modes)(
      '%s Host 持续消费真实源事件，刷新 TODO 经注入日志暴露且零派生写入',
      async (mode: ViewMode): Promise<void> => {
        const workspace: FileTestWorkspace = await FileTestWorkspace.make()
        try {
          await workspace.write('setting.json', 'guid: watch\nname: 监听\nmainMap: ""')
          const records: string[] = []
          const host: Host = await Host.open(
            workspace.fullPath,
            mode,
            pino(
              { level: 'error', timestamp: false, base: null },
              {
                write(record: string): void {
                  records.push(record)
                },
              },
            ),
          )
          try {
            for (const sourceRelativePath of ['first.code', 'second.code']) {
              const previousCount: number = records.length
              await workspace.write(sourceRelativePath, sourceRelativePath)
              await vi.waitFor((): void => {
                assert.ok(records.length > previousCount)
              })
              assert.equal(
                await readFile(join(workspace.fullPath, sourceRelativePath), 'utf8'),
                sourceRelativePath,
              )
              assert.equal((await host.status()).settings.name, '监听')
            }
            for (const record of records) {
              assert.deepEqual(JSON.parse(record), {
                level: 50,
                component: 'effect',
                msg: 'Error: TODO: 按实际变化分类纠偏并交付已打开预览的同次刷新',
              })
            }
            assert.deepEqual(await readdir(workspace.fullPath), [
              'first.code',
              'second.code',
              'setting.json',
            ])
            assert.equal(
              workspace.writtenBytes(),
              Buffer.byteLength('guid: watch\nname: 监听\nmainMap: ""first.codesecond.code'),
            )
          } finally {
            await host.close()
          }
          await assert.rejects(host.status(), /Host 已关闭/u)
        } finally {
          await workspace.close()
        }
      },
    )

    test('实际源重读和纯派生变化均保留精确输入，刷新空壳失败且工程零写入', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      try {
        await workspace.write('setting.json', 'guid: refresh\nname: 刷新\nmainMap: ""')
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const entities: ProjectEntities = yield* ProjectEntities.make(workspace.files)
              yield* Effect.tryPromise({
                try: (): Promise<void> => workspace.write('source.code', '源输入'),
                catch: (cause: unknown): unknown => cause,
              })
              const request: WorkspaceRefreshRequest = {
                changes: {
                  changedRelativePaths: ['source.code', 'views/预览.html'],
                  directoryRelativePaths: ['.', 'views'],
                },
                reread: yield* entities.refresh(['.']),
              }
              assert.ok(request.reread.updatedEntityIds.includes('raw-file:source.code'))
              const derivedOnly: WorkspaceRefreshRequest = {
                changes: {
                  changedRelativePaths: ['views/预览.html'],
                  directoryRelativePaths: ['views'],
                },
                reread: { updatedEntityIds: [], removedEntityIds: [] },
              }
              const updates: WorkspaceUpdates = new WorkspaceUpdates()
              for (const input of [request, derivedOnly]) {
                const before: WorkspaceRefreshRequest = structuredClone(input)
                const written: number = workspace.writtenBytes()
                yield* Effect.tryPromise({
                  try: async (): Promise<void> => {
                    await assert.rejects(
                      Effect.runPromise(updates.refresh(input)),
                      /TODO: 按实际变化分类纠偏并交付已打开预览的同次刷新/u,
                    )
                  },
                  catch: (cause: unknown): unknown => cause,
                })
                assert.deepEqual(input, before)
                assert.equal(workspace.writtenBytes(), written)
              }
            }),
          ),
        )
        assert.equal(await readFile(join(workspace.fullPath, 'source.code'), 'utf8'), '源输入')
        assert.deepEqual(await readdir(workspace.fullPath), ['setting.json', 'source.code'])
      } finally {
        await workspace.close()
      }
    })
  }
}

/**
 * @anchor host-preview-display
 * @anchor host-preview-display-dispatch
 */
export class HostPreviewDisplayTest {
  private constructor() {}

  static {
    test('两种模式在同一 Host 请求展示明确失败，输入保真且关闭后拒绝', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      try {
        const setting: string = 'guid: display\nname: 展示\nmainMap: ""'
        await workspace.write('setting.json', setting)
        const backend: CanvasKit = await CpuTestBackend.initialize()
        const request: OpenCanvasPreviewRequest = {
          sourceEntityId: 'a'.repeat(32),
          viewport: { width: 2, height: 3 },
          fonts: [
            {
              name: 'Alibaba PuHuiTi',
              revision: 'fixture-font-v1',
              bytes: await readFile(
                new URL('../../../res/fonts/Alibaba-Puhui_Medium.ttf', import.meta.url),
              ),
            },
          ],
          timeSeconds: 1.25,
        }
        const before: OpenCanvasPreviewRequest = structuredClone(request)
        const written: number = workspace.writtenBytes()
        const modes: readonly ViewMode[] = ['files', 'memory']
        for (const mode of modes) {
          const host: Host = await Host.open(workspace.fullPath, mode, pino({ enabled: false }))
          const signal: AbortSignal = new AbortController().signal
          try {
            await assert.rejects(
              host.openPreview(request, backend, signal),
              /TODO: 在同一 Host 登记 Canvas 展示并交付视图与预览刷新/u,
            )
            assert.deepEqual(structuredClone(request), before)
            assert.equal(await readFile(join(workspace.fullPath, 'setting.json'), 'utf8'), setting)
            assert.deepEqual(await readdir(workspace.fullPath), ['setting.json'])
            assert.equal(workspace.writtenBytes(), written)
          } finally {
            await host.close()
          }
          await assert.rejects(host.openPreview(request, backend, signal), /Host 已关闭/u)
        }
      } finally {
        await workspace.close()
      }
    })
  }
}
