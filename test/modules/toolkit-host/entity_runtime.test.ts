import { test, vi } from 'vitest'
import assert from 'node:assert/strict'
import { Cause, Deferred, Effect, Exit, Fiber, Option, Result, Scope } from 'effect'
import type {
  EntityDiagnostic,
  EntitySnapshot,
  EntityDirectoryListing,
  FileEntity,
} from '../../../src/toolkit/project_model/FileEntities.ts'
import type { LogicalEntitySnapshot } from '../../../src/toolkit/project_model/LogicalEntities.ts'
import { UpdateBytes } from '../../../src/toolkit/project_model/LogicalEntities.ts'
import { ProjectEntities } from '../../../src/toolkit/entities/ProjectEntities.ts'
import { EntityChange } from '../../../src/toolkit/entities/EntityChange.ts'
import { ProjectWatcher } from '../../../src/toolkit/files/ProjectWatcher.ts'
import type { ProjectFileChanges } from '../../../src/toolkit/files/ProjectWatcher.ts'
import { ProjectFiles } from '../../../src/toolkit/files/ProjectFiles.ts'
import { FileTestWorkspace } from '../toolkit-file-support/FileTestWorkspace.ts'

interface CommitFailureCase {
  readonly name: string
  readonly writeFails: boolean
  readonly rereadFails: boolean
}

/**
 * @anchor entity-runtime
 * @anchor entity-watch
 * @anchor view-source-classification
 * @anchor derived-view-entity-reconciliation
 */
export class EntityRuntimeTests {
  private constructor() {}

  /** @anchor entity-runtime */
  private static observe(
    service: ProjectEntities,
    predicate: (snapshot: EntitySnapshot) => boolean,
    operation: () => Promise<void>,
  ): Effect.Effect<EntitySnapshot, unknown> {
    return Effect.gen(function* (): Generator<
      Effect.Effect<unknown, unknown>,
      EntitySnapshot,
      never
    > {
      yield* Effect.tryPromise({ try: operation, catch: (cause: unknown): unknown => cause })
      yield* service.refresh(['.'])
      const snapshot: EntitySnapshot = yield* service.snapshot()
      assert.equal(predicate(snapshot), true)
      return snapshot
    })
  }

  static {
    test.each<CommitFailureCase>([
      { name: '提交成功但重读失败', writeFails: false, rereadFails: true },
      { name: '部分写入后失败并成功重读', writeFails: true, rereadFails: false },
      { name: '提交和重读同时失败', writeFails: true, rereadFails: true },
    ])('$name 保留落盘结果与失败原因', async (scenario: CommitFailureCase): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        const guid: string = 'a'.repeat(32)
        await workspace.write('resource.bin', Uint8Array.of(1))
        await workspace.write('resource.bin.guid', guid)
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const service: ProjectEntities = yield* ProjectEntities.make(files)
              const entity: FileEntity = Option.getOrThrow(yield* service.resolve(guid))
              const candidate: EntityChange = yield* service.prepare(entity, Uint8Array.of(2))
              candidate.write('resource.bin.guid', 'b'.repeat(32))
              const writeFailure: Error = new Error('候选伴生文件写入失败')
              const rereadFailure: Error = new Error('提交后目录重读失败')
              if (scenario.writeFails) {
                const write: ProjectFiles['write'] = files.write.bind(files)
                vi.spyOn(files, 'write').mockImplementation(
                  (relativePath: string, bytes: Uint8Array): Effect.Effect<boolean, unknown> =>
                    relativePath === 'resource.bin.guid'
                      ? Effect.fail(writeFailure)
                      : write(relativePath, bytes),
                )
              }
              if (scenario.rereadFails)
                vi.spyOn(files, 'directory').mockImplementationOnce(
                  (): Effect.Effect<never, unknown> => Effect.fail(rereadFailure),
                )
              const outcome: Exit.Exit<boolean, unknown> = yield* Effect.exit(
                service.commit(candidate),
              )
              assert.ok(Exit.isFailure(outcome))
              const failures: readonly unknown[] = outcome.cause.reasons.map(
                (reason: Cause.Reason<unknown>): unknown => {
                  assert.ok(Cause.isFailReason(reason))
                  return reason.error
                },
              )
              assert.deepEqual(failures, [
                ...(scenario.writeFails ? [writeFailure] : []),
                ...(scenario.rereadFails ? [rereadFailure] : []),
              ])
              assert.deepEqual(yield* files.read('resource.bin'), Uint8Array.of(2))
              const actualGuid: string = scenario.writeFails ? guid : 'b'.repeat(32)
              assert.equal(
                new TextDecoder().decode(yield* files.read('resource.bin.guid')),
                actualGuid,
              )
              if (scenario.rereadFails) {
                const snapshot: Result.Result<EntitySnapshot, unknown> = yield* Effect.result(
                  service.snapshot(),
                )
                const logical: Result.Result<LogicalEntitySnapshot, unknown> = yield* Effect.result(
                  service.logicalSnapshot(),
                )
                assert.ok(Result.isFailure(snapshot))
                assert.ok(Result.isFailure(logical))
                assert.equal(snapshot.failure, rereadFailure)
                assert.equal(logical.failure, rereadFailure)
                const watcher: ProjectWatcher = yield* ProjectWatcher.make(workspace.fullPath)
                yield* Effect.tryPromise({
                  try: (): Promise<void> => workspace.write('trigger.code', '实际后续事件'),
                  catch: (cause: unknown): unknown => cause,
                })
                const changes: ProjectFileChanges = yield* watcher.take()
                yield* service.refresh(changes.directoryRelativePaths)
              }
              const current: FileEntity = Option.getOrThrow(yield* service.resolve(actualGuid))
              assert.deepEqual(yield* service.read(current), Uint8Array.of(2))
              assert.equal(current.relativePath, 'resource.bin')
            }),
          ),
        )
      } finally {
        await workspace.close()
      }
    })

    test('提交取消后重读已落盘内容并保持中断结果', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        const guid: string = 'a'.repeat(32)
        await workspace.write('resource.bin', Uint8Array.of(1))
        await workspace.write('resource.bin.guid', guid)
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const service: ProjectEntities = yield* ProjectEntities.make(files)
              const entity: FileEntity = Option.getOrThrow(yield* service.resolve(guid))
              const candidate: EntityChange = yield* service.prepare(entity, Uint8Array.of(2))
              const written: Deferred.Deferred<boolean> = yield* Deferred.make<boolean>()
              const write: ProjectFiles['write'] = files.write.bind(files)
              vi.spyOn(files, 'write').mockImplementation(
                (relativePath: string, bytes: Uint8Array): Effect.Effect<boolean, unknown> =>
                  write(relativePath, bytes).pipe(
                    Effect.andThen(Deferred.succeed(written, true)),
                    Effect.andThen(Effect.never),
                  ),
              )
              const committing: Fiber.Fiber<boolean, unknown> = yield* service
                .commit(candidate)
                .pipe(Effect.forkChild)
              yield* Deferred.await(written)
              yield* Fiber.interrupt(committing)
              const outcome: Exit.Exit<boolean, unknown> = yield* Fiber.await(committing)
              assert.ok(Exit.isFailure(outcome))
              assert.ok(Cause.hasInterrupts(outcome.cause))
              const current: FileEntity = Option.getOrThrow(yield* service.resolve(guid))
              assert.notEqual(current.revision, entity.revision)
              assert.deepEqual(yield* service.read(current), Uint8Array.of(2))
              assert.deepEqual(yield* files.read('resource.bin'), Uint8Array.of(2))
            }),
          ),
        )
      } finally {
        await workspace.close()
      }
    })

    test('take 交付实际变化，实体只在主动刷新后更新', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const watcher: ProjectWatcher = yield* ProjectWatcher.make(workspace.fullPath)
              const entities: ProjectEntities = yield* ProjectEntities.make(files)
              const baseline: EntitySnapshot = yield* entities.snapshot()
              const taken: Fiber.Fiber<ProjectFileChanges, unknown> = yield* watcher
                .take()
                .pipe(Effect.forkChild)
              yield* Effect.tryPromise({
                try: (): Promise<void> => workspace.write('source.code', 'content'),
                catch: (cause: unknown): unknown => cause,
              })
              const changes: ProjectFileChanges = yield* Fiber.join(taken)
              assert.deepEqual(changes.changedRelativePaths, ['source.code'])
              assert.deepEqual(changes.directoryRelativePaths, ['.'])
              assert.equal(yield* entities.snapshot(), baseline)
              const refresh: Effect.Success<ReturnType<ProjectEntities['refresh']>> =
                yield* entities.refresh(changes.directoryRelativePaths)
              assert.ok(refresh.updatedEntityIds.includes('raw-file:source.code'))
              assert.deepEqual(refresh.removedEntityIds, [])
              const current: EntitySnapshot = yield* entities.snapshot()
              assert.deepEqual(
                current.directories.flatMap(
                  (directory: EntityDirectoryListing): readonly string[] =>
                    directory.entries.map(
                      (entry: EntityDirectoryListing['entries'][number]): string =>
                        entry.relativePath,
                    ),
                ),
                ['source.code'],
              )
            }),
          ),
        )
      } finally {
        await workspace.close()
      }
    })

    test('扫描期间的实际 GUID 变化发布错误，下一次真实事件重新建立当前状态', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        const beforeGuid: string = 'a'.repeat(32)
        const afterGuid: string = 'b'.repeat(32)
        await workspace.write('resource.bin', Uint8Array.of(1))
        await workspace.write('resource.bin.guid', beforeGuid)
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const service: ProjectEntities = yield* ProjectEntities.make(files)
              const read: ProjectFiles['read'] = files.read.bind(files)
              vi.spyOn(files, 'read').mockImplementationOnce(
                (relativePath: string): Effect.Effect<Uint8Array, unknown> =>
                  Effect.gen(function* (): Generator<
                    Effect.Effect<unknown, unknown>,
                    Uint8Array,
                    never
                  > {
                    const bytes: Uint8Array = yield* read(relativePath)
                    yield* Effect.tryPromise({
                      try: (): Promise<void> => workspace.write(relativePath, afterGuid),
                      catch: (cause: unknown): unknown => cause,
                    })
                    return bytes
                  }),
              )
              yield* Effect.tryPromise({
                try: (): Promise<void> => workspace.write('resource.bin', Uint8Array.of(2)),
                catch: (cause: unknown): unknown => cause,
              })
              const failure: Result.Result<unknown, unknown> = yield* Effect.result(
                service.refresh(['.']),
              )
              assert.equal(failure._tag, 'Failure')
              assert.match(String(Option.getOrThrow(Result.getFailure(failure))), /GUID 文件变化/)
              assert.equal(Result.isFailure(yield* Effect.result(service.snapshot())), true)
              yield* EntityRuntimeTests.observe(
                service,
                (snapshot: EntitySnapshot): boolean =>
                  snapshot.entities.some(
                    (entity: FileEntity): boolean => entity.guid === afterGuid,
                  ),
                (): Promise<void> => workspace.write('trigger.png', Uint8Array.of(3)),
              )
              assert.equal(Option.isNone(yield* service.resolve(beforeGuid)), true)
              assert.equal(
                Option.getOrThrow(yield* service.resolve(afterGuid)).relativePath,
                'resource.bin',
              )
            }),
          ),
        )
      } finally {
        await workspace.close()
      }
    })

    test('主动刷新取得新实体与正文，历史快照与过期候选保持隔离', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        const guid: string = 'a'.repeat(32)
        await workspace.write('resource.bin', Uint8Array.of(1, 2, 3))
        await workspace.write('resource.bin.guid', guid)
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const service: ProjectEntities = yield* ProjectEntities.make(files)
              const original: EntitySnapshot = yield* service.snapshot()
              const entity: FileEntity = Option.getOrThrow(yield* service.resolve(guid))
              yield* EntityRuntimeTests.observe(
                service,
                (snapshot: EntitySnapshot): boolean =>
                  snapshot.entities.some(
                    (current: FileEntity): boolean =>
                      current.guid === guid && current.revision !== entity.revision,
                  ),
                (): Promise<void> => workspace.write('resource.bin', Uint8Array.of(255, 0, 9)),
              )
              const current: FileEntity = Option.getOrThrow(yield* service.resolve(guid))
              assert.deepEqual(yield* service.read(current), Uint8Array.of(255, 0, 9))
              const written: number = workspace.writtenBytes()
              const stale: Result.Result<EntityChange, unknown> = yield* Effect.result(
                service.prepare(entity, Uint8Array.of(7)),
              )
              assert.equal(stale._tag, 'Failure')
              assert.equal(
                String(Option.getOrThrow(Result.getFailure(stale))).includes(
                  '候选文件基准已变化: resource.bin',
                ),
                true,
              )
              assert.equal(workspace.writtenBytes(), written)
              assert.deepEqual(yield* files.read('resource.bin'), Uint8Array.of(255, 0, 9))
              yield* service.commit(yield* service.prepare(current, Uint8Array.of(7)))
              assert.deepEqual(yield* files.read('resource.bin'), Uint8Array.of(7))
              assert.equal(
                Option.getOrThrow(Option.fromNullishOr(original.entities[0])).revision,
                entity.revision,
              )
            }),
          ),
        )
      } finally {
        await workspace.close()
      }
    })

    test('正文读取期间伴生身份变化必须失败，不返回旧身份下的内容', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        const guid: string = 'a'.repeat(32)
        await workspace.write('resource.bin', Uint8Array.of(1))
        await workspace.write('resource.bin.guid', guid)
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const service: ProjectEntities = yield* ProjectEntities.make(files)
              const entity: FileEntity = Option.getOrThrow(yield* service.resolve(guid))
              const read: ProjectFiles['read'] = files.read.bind(files)
              vi.spyOn(files, 'read').mockImplementationOnce(
                (relativePath: string): Effect.Effect<Uint8Array, unknown> =>
                  Effect.gen(function* (): Generator<
                    Effect.Effect<unknown, unknown>,
                    Uint8Array,
                    never
                  > {
                    const bytes: Uint8Array = yield* read(relativePath)
                    yield* Effect.tryPromise({
                      try: (): Promise<void> =>
                        workspace.write('resource.bin.guid', 'b'.repeat(32)),
                      catch: (cause: unknown): unknown => cause,
                    })
                    return bytes
                  }),
              )
              const result: Result.Result<Uint8Array, unknown> = yield* Effect.result(
                service.read(entity),
              )
              assert.equal(result._tag, 'Failure')
              assert.equal(
                String(Option.getOrThrow(Result.getFailure(result))).includes('实体身份版本已变化'),
                true,
              )
            }),
          ),
        )
      } finally {
        await workspace.close()
      }
    })

    test.each([
      { relativePath: 'resource.bin', content: 'changed' },
      { relativePath: 'resource.bin.guid', content: 'b'.repeat(32) },
    ])(
      '监听尚未刷新时原始资源读写拒绝过期 $relativePath',
      async (scenario: {
        readonly relativePath: string
        readonly content: string
      }): Promise<void> => {
        const workspace: FileTestWorkspace = await FileTestWorkspace.make()
        const files: ProjectFiles = workspace.files
        try {
          const guid: string = 'a'.repeat(32)
          await workspace.write('resource.bin', Uint8Array.of(1))
          await workspace.write('resource.bin.guid', guid)
          await Effect.runPromise(
            Effect.scoped(
              Effect.gen(function* (): Generator<
                Effect.Effect<unknown, unknown, Scope.Scope>,
                void,
                never
              > {
                const service: ProjectEntities = yield* ProjectEntities.make(files)
                const baseline: EntitySnapshot = yield* service.snapshot()
                const entity: FileEntity = Option.getOrThrow(yield* service.resolve(guid))
                assert.deepEqual(yield* service.read(entity), Uint8Array.of(1))
                yield* Effect.tryPromise({
                  try: (): Promise<void> =>
                    workspace.write(scenario.relativePath, scenario.content),
                  catch: (cause: unknown): unknown => cause,
                })
                assert.equal(yield* service.snapshot(), baseline)
                const written: number = workspace.writtenBytes()
                const read: Result.Result<Uint8Array, unknown> = yield* Effect.result(
                  service.read(entity),
                )
                assert.equal(read._tag, 'Failure')
                const prepared: Result.Result<EntityChange, unknown> = yield* Effect.result(
                  service.prepare(entity, Uint8Array.of(7)),
                )
                assert.equal(prepared._tag, 'Failure')
                assert.equal(
                  String(Option.getOrThrow(Result.getFailure(prepared))).includes(
                    '候选文件基准已变化: ' + scenario.relativePath,
                  ),
                  true,
                )
                assert.equal(workspace.writtenBytes(), written)
                assert.equal(
                  new TextDecoder().decode(yield* files.read(scenario.relativePath)),
                  scenario.content,
                )
              }),
            ),
          )
        } finally {
          await workspace.close()
        }
      },
    )
    test('主动刷新新目录、GUID 冲突与删除得到实际实体和诊断', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        const guid: string = 'a'.repeat(32)
        await workspace.write('first.bin', 'first')
        await workspace.write('first.bin.guid', guid)
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const service: ProjectEntities = yield* ProjectEntities.make(files)
              yield* EntityRuntimeTests.observe(
                service,
                (snapshot: EntitySnapshot): boolean =>
                  snapshot.diagnostics.some(
                    (diagnostic: EntityDiagnostic): boolean => diagnostic.kind === 'duplicate-guid',
                  ),
                async (): Promise<void> => {
                  await Effect.runPromise(files.makeDirectory('new'))
                  await workspace.write('new/second.bin', 'second')
                  await workspace.write('new/second.bin.guid', guid)
                },
              )
              const resolved: Result.Result<
                Option.Option<FileEntity>,
                unknown
              > = yield* Effect.result(service.resolve(guid))
              assert.equal(resolved._tag, 'Failure')
              yield* EntityRuntimeTests.observe(
                service,
                (snapshot: EntitySnapshot): boolean =>
                  snapshot.entities.length === 1 &&
                  snapshot.diagnostics.length === 0 &&
                  !snapshot.directories.some(
                    (directory: EntityDirectoryListing): boolean =>
                      directory.relativePath === 'new',
                  ),
                async (): Promise<void> => {
                  await Effect.runPromise(files.remove('new/second.bin'))
                  await Effect.runPromise(files.remove('new/second.bin.guid'))
                  await Effect.runPromise(files.removeDirectory('new'))
                },
              )
              assert.equal(
                Option.getOrThrow(yield* service.resolve(guid)).relativePath,
                'first.bin',
              )
            }),
          ),
        )
      } finally {
        await workspace.close()
      }
    })

    test('第二层候选提交主动刷新，关闭作用域终止监听和实体访问', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        const guid: string = 'a'.repeat(32)
        const metadata: string = `guid: ${guid}\nquality: 80\n`
        await workspace.write('resource.bin', Uint8Array.of(1, 2))
        await workspace.write('resource.bin.guid', metadata)
        const [service, candidate, entity, baseline]: readonly [
          ProjectEntities,
          EntityChange,
          FileEntity,
          LogicalEntitySnapshot,
        ] = await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              readonly [ProjectEntities, EntityChange, FileEntity, LogicalEntitySnapshot],
              never
            > {
              const entities: ProjectEntities = yield* ProjectEntities.make(files)
              const candidate: EntityChange = yield* entities.prepare(
                Option.getOrThrow(yield* entities.resolve(guid)),
                Uint8Array.of(255, 0),
              )
              const written: number = workspace.writtenBytes()
              const classified: Result.Result<unknown, unknown> = yield* Effect.result(
                entities.classifyChanges(['resource.bin', 'views/零件.ts']),
              )
              assert.ok(Result.isFailure(classified))
              assert.ok(classified.failure instanceof Error)
              assert.equal(
                classified.failure.message,
                'TODO: 按唯一派生归属划分源重读目录与派生视图纠偏输入',
              )
              assert.equal(workspace.writtenBytes(), written)
              assert.equal(yield* entities.commit(candidate), true)
              const current: FileEntity = Option.getOrThrow(yield* entities.resolve(guid))
              assert.deepEqual(yield* entities.read(current), Uint8Array.of(255, 0))
              assert.equal(
                new TextDecoder().decode(yield* files.read('resource.bin.guid')),
                metadata,
              )
              return [
                entities,
                yield* entities.prepare(current, Uint8Array.of(7)),
                current,
                yield* entities.logicalSnapshot(),
              ]
            }),
          ),
        )
        await assert.rejects(Effect.runPromise(service.snapshot()), (cause: unknown): boolean =>
          String(cause).includes('已关闭'),
        )
        await assert.rejects(
          Effect.runPromise(service.logicalSnapshot()),
          (cause: unknown): boolean => String(cause).includes('已关闭'),
        )
        const before: number = workspace.writtenBytes()
        await assert.rejects(Effect.runPromise(service.read(entity)), (cause: unknown): boolean =>
          String(cause).includes('已关闭'),
        )
        await assert.rejects(
          Effect.runPromise(service.prepare(entity, Uint8Array.of(9))),
          (cause: unknown): boolean => String(cause).includes('已关闭'),
        )
        await assert.rejects(
          Effect.runPromise(
            service.prepareOperation(
              baseline,
              new UpdateBytes('resource:resource.bin', Uint8Array.of(9)),
            ),
          ),
          (cause: unknown): boolean => String(cause).includes('已关闭'),
        )
        await assert.rejects(
          Effect.runPromise(service.commit(candidate)),
          (cause: unknown): boolean => String(cause).includes('已关闭'),
        )
        assert.equal(workspace.writtenBytes(), before)
        assert.deepEqual(await Effect.runPromise(files.read('resource.bin')), Uint8Array.of(255, 0))
      } finally {
        await workspace.close()
      }
    })
  }
}
