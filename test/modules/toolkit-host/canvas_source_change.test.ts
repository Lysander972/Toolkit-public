import assert from 'node:assert/strict'
import { readFile, readdir } from 'node:fs/promises'
import { join } from 'node:path'
import { test } from 'vitest'
import { Effect, Logger, Option, Queue, Scope } from 'effect'
import type { FileEntity } from '../../../src/toolkit/project_model/FileEntities.ts'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import { ProjectEntities } from '../../../src/toolkit/entities/ProjectEntities.ts'
import { ProjectWatcher } from '../../../src/toolkit/files/ProjectWatcher.ts'
import { WorkspaceUpdates } from '../../../src/toolkit/host/WorkspaceUpdates.ts'
import { FileTestWorkspace } from '../toolkit-file-support/FileTestWorkspace.ts'

/** @anchor entity-watch */
export class CanvasSourceChangeTest {
  private constructor() {}

  static {
    test('外部 Canvas 二进制变化经唯一监听重读，旧基准拒绝且派生交付明确失败', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      try {
        const guid: string = 'a'.repeat(32)
        const metadata: string = `guid: ${guid}\nquality: 80\n`
        const before: Uint8Array = CanvasConverter.toBinary(
          CanvasConverter.fromHtml('<BeCanvas name="修改前" width="320" height="240"></BeCanvas>'),
        )
        const after: Uint8Array = CanvasConverter.toBinary(
          CanvasConverter.fromHtml('<BeCanvas name="修改后" width="640" height="480"></BeCanvas>'),
        )
        await workspace.write('setting.json', 'guid: canvas\nname: 外部修改\nmainMap: ""')
        await workspace.write('ui.canvas', before)
        await workspace.write('ui.canvas.guid', metadata)
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const watcher: ProjectWatcher = yield* ProjectWatcher.make(workspace.fullPath)
              const entities: ProjectEntities = yield* ProjectEntities.make(workspace.files)
              const original: FileEntity = Option.getOrThrow(yield* entities.resolve(guid))
              const snapshot: Effect.Success<ReturnType<ProjectEntities['snapshot']>> =
                yield* entities.snapshot()
              assert.deepEqual(yield* entities.read(original), before)
              const errors: Queue.Queue<string> = yield* Queue.make<string>()
              yield* new WorkspaceUpdates().watch(watcher, entities).pipe(
                Effect.provide(
                  Logger.layer(
                    [
                      Logger.make((options: Logger.Options<unknown>): void => {
                        Queue.offerUnsafe(errors, String(options.message))
                      }),
                    ],
                    { mergeWithExisting: false },
                  ),
                ),
                Effect.forkChild,
              )
              yield* Effect.tryPromise({
                try: (): Promise<void> => workspace.write('ui.canvas', after),
                catch: (cause: unknown): unknown => cause,
              })
              const written: number = workspace.writtenBytes()
              assert.equal(
                yield* Queue.take(errors),
                'Error: TODO: 按实际变化分类纠偏并交付已打开预览的同次刷新',
              )
              const current: FileEntity = Option.getOrThrow(yield* entities.resolve(guid))
              assert.equal(current.guid, original.guid)
              assert.equal(current.relativePath, 'ui.canvas')
              assert.notEqual(current.revision, original.revision)
              assert.equal(current.guidRevision, original.guidRevision)
              assert.deepEqual(yield* entities.read(current), after)
              assert.deepEqual(snapshot.entities, [original])
              assert.notEqual(yield* entities.snapshot(), snapshot)
              yield* Effect.tryPromise({
                try: async (): Promise<void> => {
                  await assert.rejects(
                    Effect.runPromise(entities.read(original)),
                    /实体内容版本已变化: ui\.canvas/u,
                  )
                  await assert.rejects(
                    Effect.runPromise(entities.prepare(original, before)),
                    /候选文件基准已变化: ui\.canvas/u,
                  )
                  await assert.rejects(
                    Effect.runPromise(entities.canvas(guid)),
                    /TODO: 从权威 GUID 关联装配 Canvas 格式实体/u,
                  )
                },
                catch: (cause: unknown): unknown => cause,
              })
              assert.equal(workspace.writtenBytes(), written)
            }),
          ).pipe(Effect.timeout('3 seconds')),
        )
        assert.deepEqual(
          new Uint8Array(await readFile(join(workspace.fullPath, 'ui.canvas'))),
          after,
        )
        assert.equal(await readFile(join(workspace.fullPath, 'ui.canvas.guid'), 'utf8'), metadata)
        assert.deepEqual(await readdir(workspace.fullPath), [
          'setting.json',
          'ui.canvas',
          'ui.canvas.guid',
        ])
      } finally {
        await workspace.close()
      }
    })
  }
}
