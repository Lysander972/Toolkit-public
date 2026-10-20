import { test, vi } from 'vitest'
import assert from 'node:assert/strict'
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import { Effect, Scope } from 'effect'
import { ProjectWatcher } from '../../../src/toolkit/files/ProjectWatcher.ts'
import { ProjectEntities } from '../../../src/toolkit/entities/ProjectEntities.ts'
import type { ProjectFiles } from '../../../src/toolkit/files/ProjectFiles.ts'
import type { EntitySnapshot, FileEntity } from '../../../src/toolkit/project_model/FileEntities.ts'
import type { ViewMode } from '../../../src/toolkit/views/ProjectViews.ts'
import pino from 'pino'
import { Host, SnapshotContext, BackendRequestContext } from '../../../src/toolkit/host/Host.ts'
import type { WorkspaceStatus } from '../../../src/toolkit/host/WorkspaceServices.ts'

/**
 * @anchor host-lifecycle
 * @anchor workspace-view-mode-startup
 * @anchor workspace-view-mode-binding
 * @anchor workspace-cold-start
 */
export class HostTest {
  private constructor() {}

  static {
    test.each<ViewMode>(['files', 'memory'])(
      '%s 首次实体装配等待监听 ready，启动期间变化继续交付',
      async (mode: ViewMode): Promise<void> => {
        await mkdir('_build/tests', { recursive: true })
        const workspaceFullPath: string = await mkdtemp(resolve('_build/tests/host-ready-'))
        const release: PromiseWithResolvers<boolean> = Promise.withResolvers<boolean>()
        const watching: PromiseWithResolvers<boolean> = Promise.withResolvers<boolean>()
        const snapshots: EntitySnapshot[] = []
        const records: string[] = []
        const makeWatcher: typeof ProjectWatcher.make = ProjectWatcher.make.bind(ProjectWatcher)
        const makeEntities: typeof ProjectEntities.make = ProjectEntities.make.bind(ProjectEntities)
        vi.spyOn(ProjectWatcher, 'make').mockImplementation(
          (rootFullPath: string): Effect.Effect<ProjectWatcher, unknown, Scope.Scope> =>
            Effect.flatMap(
              makeWatcher(rootFullPath),
              (watcher: ProjectWatcher): Effect.Effect<ProjectWatcher, unknown> =>
                Effect.tryPromise({
                  try: async (): Promise<ProjectWatcher> => {
                    watching.resolve(true)
                    await release.promise
                    return watcher
                  },
                  catch: (cause: unknown): unknown => cause,
                }),
            ),
        )
        vi.spyOn(ProjectEntities, 'make').mockImplementation(
          (files: ProjectFiles): Effect.Effect<ProjectEntities, unknown, Scope.Scope> =>
            Effect.tap(
              makeEntities(files),
              (entities: ProjectEntities): Effect.Effect<void, unknown> =>
                Effect.map(entities.snapshot(), (snapshot: EntitySnapshot): void => {
                  snapshots.push(snapshot)
                }),
            ),
        )
        try {
          await writeFile(
            join(workspaceFullPath, 'setting.json'),
            'guid: ready\nname: 启动\nmainMap: ""',
          )
          const opening: Promise<Host> = Host.open(
            workspaceFullPath,
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
            await watching.promise
            assert.deepEqual(snapshots, [])
            await writeFile(join(workspaceFullPath, 'resource.bin'), Uint8Array.of(7))
            await writeFile(join(workspaceFullPath, 'resource.bin.guid'), 'a'.repeat(32))
            release.resolve(true)
            const host: Host = await opening
            assert.equal((await host.status()).settings.name, '启动')
            assert.deepEqual(
              snapshots.flatMap((snapshot: EntitySnapshot): readonly string[] =>
                snapshot.entities.map((entity: FileEntity): string => entity.relativePath),
              ),
              ['resource.bin'],
            )
            await vi.waitFor((): void => {
              assert.ok(records.length > 0)
            })
            const previous: number = records.length
            await writeFile(join(workspaceFullPath, 'next.code'), '后续变化')
            await vi.waitFor((): void => {
              assert.ok(records.length > previous)
            })
          } finally {
            release.resolve(true)
            const host: Host = await opening
            await host.close()
          }
        } finally {
          await rm(workspaceFullPath, { recursive: true })
        }
      },
    )

    test('两个 Host 独立持有工作区与视图模式，查询读到外部修改', async (): Promise<void> => {
      await mkdir('_build/tests', { recursive: true })
      const workspaceFullPath: string = await mkdtemp(resolve('_build/tests/host-'))
      try {
        const settingFullPath: string = join(workspaceFullPath, 'setting.json')
        await writeFile(settingFullPath, 'guid: first\nname: 工程\nmainMap: ""', 'utf8')
        const fileHost: Host = await Host.open(workspaceFullPath, 'files', pino({ enabled: false }))
        const memoryHost: Host = await Host.open(
          workspaceFullPath,
          'memory',
          pino({ enabled: false }),
        )
        try {
          assert.equal((await fileHost.status()).viewMode, 'files')
          assert.equal((await memoryHost.status()).viewMode, 'memory')
          await writeFile(settingFullPath, 'guid: changed\nname: 新名称\nmainMap: ""', 'utf8')
          assert.equal((await fileHost.status()).settings.name, '新名称')
          assert.equal((await memoryHost.status()).settings.guid, 'changed')
        } finally {
          await fileHost.close()
          await memoryHost.close()
        }
        await assert.rejects((): Promise<WorkspaceStatus> => fileHost.status(), /Host 已关闭/u)
      } finally {
        await rm(workspaceFullPath, { recursive: true })
      }
    })

    test('工程设置缺失时启动失败，不创建可用 Host', async (): Promise<void> => {
      await mkdir('_build/tests', { recursive: true })
      const workspaceFullPath: string = await mkdtemp(resolve('_build/tests/host-missing-'))
      try {
        await assert.rejects((): Promise<Host> =>
          Host.open(workspaceFullPath, 'memory', pino({ enabled: false })),
        )
      } finally {
        await rm(workspaceFullPath, { recursive: true })
      }
    })
  }
}

/**
 * @anchor backend-container-stack
 * @anchor workspace-computation-concurrency
 */
export class BackendContainerStackTest {
  private constructor() {}

  static {
    test('请求与快照上下文各自独立，原生 Scope 结束后释放', async (): Promise<void> => {
      const contexts: readonly (SnapshotContext | BackendRequestContext)[] =
        await Effect.runPromise(
          Effect.scoped(
            Effect.all([
              SnapshotContext.make(),
              SnapshotContext.make(),
              BackendRequestContext.make(),
              BackendRequestContext.make(),
            ]).pipe(
              Effect.tap(
                (
                  current: readonly (SnapshotContext | BackendRequestContext)[],
                ): Effect.Effect<void> =>
                  Effect.sync((): void => {
                    assert.equal(new Set(current).size, 4)
                    for (const entry of current) assert.equal(entry.isOpen(), true)
                  }),
              ),
            ),
          ),
        )
      for (const entry of contexts) assert.equal(entry.isOpen(), false)
    })

    test('Host 分派具名状态输入，消费取消信号，关闭后拒绝请求和快照', async (): Promise<void> => {
      await mkdir('_build/tests', { recursive: true })
      const workspaceFullPath: string = await mkdtemp(resolve('_build/tests/host-request-'))
      try {
        await writeFile(
          join(workspaceFullPath, 'setting.json'),
          'guid: named\nname: 具名\nmainMap: ""',
          'utf8',
        )
        const host: Host = await Host.open(workspaceFullPath, 'memory', pino({ enabled: false }))
        try {
          const signal: AbortSignal = new AbortController().signal
          assert.equal(
            (await host.runInRequest({ kind: 'workspace-status' }, signal)).settings.guid,
            'named',
          )
          assert.equal(
            (await host.withSnapshot({ kind: 'workspace-status' }, signal)).settings.name,
            '具名',
          )
          await assert.rejects(
            host.runInRequest({ kind: 'workspace-status' }, AbortSignal.abort('请求取消')),
          )
          assert.equal(
            (await host.runInRequest({ kind: 'workspace-status' }, signal)).settings.name,
            '具名',
          )
        } finally {
          await host.close()
        }
        await assert.rejects(
          host.runInRequest({ kind: 'workspace-status' }, new AbortController().signal),
          /Host 已关闭/u,
        )
        await assert.rejects(
          host.withSnapshot({ kind: 'workspace-status' }, new AbortController().signal),
          /Host 已关闭/u,
        )
      } finally {
        await rm(workspaceFullPath, { recursive: true })
      }
    })
  }
}
