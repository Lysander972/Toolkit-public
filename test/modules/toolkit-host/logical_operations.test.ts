import { test, vi } from 'vitest'
import assert from 'node:assert/strict'
import Hjson from 'hjson'
import { Effect, Option, Result, Scope } from 'effect'
import {
  CreateCarrier,
  CreateContainer,
  CreateDevice,
  CreateFileEntity,
  DeleteEntity,
  LogicalDevice,
  LogicalRoot,
  LogicalScriptGroup,
  MoveEntity,
  UpdateBytes,
  UpdateDeviceFields,
  UpdateScriptList,
  UpdateSettings,
  UpdateVariables,
} from '../../../src/toolkit/project_model/LogicalEntities.ts'
import type {
  LogicalEntity,
  LogicalEntityOperation,
  LogicalEntitySnapshot,
  LogicalScriptEntry,
} from '../../../src/toolkit/project_model/LogicalEntities.ts'
import { EntityChange } from '../../../src/toolkit/entities/EntityChange.ts'
import { ProjectEntities } from '../../../src/toolkit/entities/ProjectEntities.ts'
import { ProjectFiles } from '../../../src/toolkit/files/ProjectFiles.ts'
import type { FileMetadata } from '../../../src/toolkit/files/FileContracts.ts'
import { FileTestWorkspace } from '../toolkit-file-support/FileTestWorkspace.ts'

/** @anchor logical-entity-operations @anchor logical-update-operations @anchor logical-script-model */
export class LogicalOperationTests {
  private constructor() {}

  private static entity(snapshot: LogicalEntitySnapshot, relativePath: string): LogicalEntity {
    return Option.getOrThrow(
      Option.fromNullishOr(
        snapshot.entities.find(
          (entity: LogicalEntity): boolean => entity.primaryRelativePath === relativePath,
        ),
      ),
    )
  }

  static {
    test('载体、脚本清单、仓库引用、变量和设置完成显式操作闭环', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await workspace.write(
          'setting.json',
          '# setting\nguid:project\nname:original\nmainMap:scene\nunknown:keep\n',
        )
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const service: ProjectEntities = yield* ProjectEntities.make(files)
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new CreateCarrier(
                    'root',
                    'mech',
                    'ship.mech',
                    Uint8Array.of(1),
                    '# index\nlist:[]\nunknown:keep\n',
                    Option.none(),
                  ),
                ),
              )
              const carrier: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                'ship.mech',
              )
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new CreateDevice(carrier.id, 2, 'type:1101'),
                ),
              )
              const device: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                'ship.mech_/2',
              )
              assert.ok(device instanceof LogicalDevice)
              assert.equal(Option.isNone(device.variables), true)
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new UpdateVariables(
                    device.id,
                    Option.some([
                      { name: 'count', typeName: 'Long', comment: Option.some('counter') },
                    ]),
                  ),
                ),
              )
              const before: number = workspace.writtenBytes()
              assert.equal(
                yield* service.commit(
                  yield* service.prepareOperation(
                    yield* service.logicalSnapshot(),
                    new UpdateVariables(
                      device.id,
                      Option.some([
                        { name: 'count', typeName: 'Long', comment: Option.some('counter') },
                      ]),
                    ),
                  ),
                ),
                false,
              )
              assert.equal(workspace.writtenBytes(), before)
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new CreateContainer(device.id, 'script-group', 'scripts'),
                ),
              )
              const group: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                'ship.mech_/2/scripts',
              )
              const source: string = 'begincode\nname:method\nlines:\nendcode\n'
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new CreateFileEntity(
                    group.id,
                    'inline-script',
                    'first',
                    new TextEncoder().encode(source),
                  ),
                ),
              )
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new CreateFileEntity(
                    group.id,
                    'inline-script',
                    'second',
                    new TextEncoder().encode(source),
                  ),
                ),
              )
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new CreateFileEntity(
                    'root',
                    'code-repo-script',
                    'shared.code',
                    new TextEncoder().encode(source),
                  ),
                ),
              )
              const second: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                'ship.mech_/2/scripts/second.code',
              )
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new MoveEntity(second.id, group.id, 'renamed'),
                ),
              )
              assert.equal(
                new TextDecoder().decode(yield* files.read('ship.mech_/2/scripts/renamed.code')),
                source,
              )
              const current: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                'ship.mech_/2/scripts',
              )
              assert.ok(current instanceof LogicalScriptGroup)
              const renamed: LogicalScriptEntry = Option.getOrThrow(
                Option.fromNullishOr(
                  current.entries.find(
                    (entry: LogicalScriptEntry): boolean => entry.name === 'renamed',
                  ),
                ),
              )
              const repository: LogicalScriptEntry = {
                name: 'shared',
                origin: 'repository',
                guid: Option.some(new TextDecoder().decode(yield* files.read('shared.code.guid'))),
                private: true,
                relativePath: Option.none(),
              }
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new UpdateScriptList(group.id, [repository, renamed]),
                ),
              )
              const reordered: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                'ship.mech_/2/scripts',
              )
              assert.ok(reordered instanceof LogicalScriptGroup)
              assert.deepEqual(
                reordered.entries.map((entry: LogicalScriptEntry): string => entry.name),
                ['shared', 'renamed'],
              )
              assert.equal(
                (yield* service.logicalSnapshot()).entities.some((entity: LogicalEntity): boolean =>
                  entity.primaryRelativePath.endsWith('/first.code'),
                ),
                false,
              )
              const shared: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                'shared.code',
              )
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new DeleteEntity(shared.id),
                ),
              )
              assert.equal(
                new TextDecoder()
                  .decode(yield* files.read('ship.mech_/2/scripts/list.json'))
                  .includes('shared'),
                true,
              )
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new UpdateVariables(device.id, Option.none()),
                ),
              )
              const withoutVariables: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                'ship.mech_/2',
              )
              assert.ok(withoutVariables instanceof LogicalDevice)
              assert.equal(Option.isNone(withoutVariables.variables), true)
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new UpdateSettings('root', 'renamed', 'newMap'),
                ),
              )
              const root: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                '.',
              )
              assert.ok(root instanceof LogicalRoot)
              assert.equal(Option.getOrThrow(root.settings).name, 'renamed')
              assert.equal(Option.getOrThrow(root.settings).guid, 'project')
              assert.equal(
                new TextDecoder()
                  .decode(yield* files.read('setting.json'))
                  .includes('unknown: keep'),
                true,
              )
            }),
          ),
        )
      } finally {
        await workspace.close()
      }
    })

    test('目录移动保留空目录与未知文件，过期目录基准阻止任何写入', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await Effect.runPromise(files.makeDirectory('assets'))
        await Effect.runPromise(files.makeDirectory('assets/empty'))
        await workspace.write('assets/unknown.dat', Uint8Array.of(9, 8))
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const service: ProjectEntities = yield* ProjectEntities.make(files)
              const directory: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                'assets',
              )
              const candidate: EntityChange = yield* service.prepareOperation(
                yield* service.logicalSnapshot(),
                new MoveEntity(directory.id, 'root', 'renamed'),
              )
              assert.throws((): void => {
                candidate.create('outside/extra.bin', Uint8Array.of(1))
              }, /超出实体范围/u)
              yield* service.commit(candidate)
              assert.deepEqual(yield* files.read('renamed/unknown.dat'), Uint8Array.of(9, 8))
              assert.deepEqual(yield* files.directory('renamed/empty'), [])
              const current: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                'renamed',
              )
              const stale: EntityChange = yield* service.prepareOperation(
                yield* service.logicalSnapshot(),
                new DeleteEntity(current.id),
              )
              yield* Effect.tryPromise({
                try: (): Promise<void> => workspace.write('renamed/added.dat', Uint8Array.of(7)),
                catch: (cause: unknown): unknown => cause,
              })
              const before: number = workspace.writtenBytes()
              const rejected: Result.Result<boolean, unknown> = yield* Effect.result(
                service.commit(stale),
              )
              assert.equal(rejected._tag, 'Failure')
              assert.equal(workspace.writtenBytes(), before)
              assert.deepEqual(yield* files.read('renamed/added.dat'), Uint8Array.of(7))
              const added: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                'renamed/added.dat',
              )
              assert.equal(added.primaryRelativePath, 'renamed/added.dat')
            }),
          ),
        )
      } finally {
        await workspace.close()
      }
    })

    test.each([
      {
        name: '创建的父目录成员',
        operation: new CreateContainer('directory:assets', 'directory', 'created'),
        changedRelativePath: 'assets/added.dat',
        content: 'external',
        conflict: '候选目录基准已变化: assets',
      },
      {
        name: '创建零件的载体索引',
        operation: new CreateDevice('mech:ship.mech', 3, 'type:1101'),
        changedRelativePath: 'ship.mech_/mech.json',
        content: 'list:["2-1101"]\nunknown:changed',
        conflict: '候选文件基准已变化: ship.mech_/mech.json',
      },
      {
        name: '删除零件的父级索引',
        operation: new DeleteEntity('device:ship.mech_/2'),
        changedRelativePath: 'ship.mech_/mech.json',
        content: 'list:["2-1101"]\nunknown:changed',
        conflict: '候选文件基准已变化: ship.mech_/mech.json',
      },
      {
        name: '删除目录的子树文件',
        operation: new DeleteEntity('directory:assets'),
        changedRelativePath: 'assets/image.bin',
        content: 'changed',
        conflict: '候选文件基准已变化: assets/image.bin',
      },
      {
        name: '移动的来源父目录',
        operation: new MoveEntity(
          'resource:assets/image.bin',
          'directory:destination',
          'image.bin',
        ),
        changedRelativePath: 'assets/added.dat',
        content: 'external',
        conflict: '候选目录基准已变化: assets',
      },
      {
        name: '移动的目标父目录',
        operation: new MoveEntity(
          'resource:assets/image.bin',
          'directory:destination',
          'image.bin',
        ),
        changedRelativePath: 'destination/added.dat',
        content: 'external',
        conflict: '候选目录基准已变化: destination',
      },
    ])(
      '监听尚未刷新时拒绝过期的 $name',
      async (scenario: {
        readonly name: string
        readonly operation: LogicalEntityOperation
        readonly changedRelativePath: string
        readonly content: string
        readonly conflict: string
      }): Promise<void> => {
        const workspace: FileTestWorkspace = await FileTestWorkspace.make()
        const files: ProjectFiles = workspace.files
        try {
          for (const relativePath of ['assets', 'destination', 'ship.mech_', 'ship.mech_/2'])
            await Effect.runPromise(files.makeDirectory(relativePath))
          await workspace.write('assets/image.bin', Uint8Array.of(1))
          await workspace.write('assets/image.bin.guid', 'a'.repeat(32))
          await workspace.write('ship.mech', Uint8Array.of(1))
          await workspace.write('ship.mech.guid', 'b'.repeat(32))
          await workspace.write('ship.mech_/mech.json', 'list:["2-1101"]')
          await workspace.write('ship.mech_/2/device.json', 'type:1101')
          await Effect.runPromise(
            Effect.scoped(
              Effect.gen(function* (): Generator<
                Effect.Effect<unknown, unknown, Scope.Scope>,
                void,
                never
              > {
                const service: ProjectEntities = yield* ProjectEntities.make(files)
                const baseline: LogicalEntitySnapshot = yield* service.logicalSnapshot()
                yield* Effect.tryPromise({
                  try: (): Promise<void> =>
                    workspace.write(scenario.changedRelativePath, scenario.content),
                  catch: (cause: unknown): unknown => cause,
                })
                assert.equal(yield* service.logicalSnapshot(), baseline)
                const directories: readonly string[] = baseline.entities.flatMap(
                  (entity: LogicalEntity): readonly string[] =>
                    entity.directories.map(
                      (directory: LogicalEntity['directories'][number]): string =>
                        directory.relativePath,
                    ),
                )
                const listings: readonly (readonly FileMetadata[])[] = yield* Effect.forEach(
                  directories,
                  (relativePath: string): Effect.Effect<readonly FileMetadata[], unknown> =>
                    files.directory(relativePath),
                )
                const written: number = workspace.writtenBytes()
                const rejected: Result.Result<EntityChange, unknown> = yield* Effect.result(
                  service.prepareOperation(baseline, scenario.operation),
                )
                assert.equal(rejected._tag, 'Failure')
                assert.equal(
                  String(Option.getOrThrow(Result.getFailure(rejected))).includes(
                    scenario.conflict,
                  ),
                  true,
                )
                assert.equal(workspace.writtenBytes(), written)
                assert.deepEqual(
                  yield* Effect.forEach(
                    directories,
                    (relativePath: string): Effect.Effect<readonly FileMetadata[], unknown> =>
                      files.directory(relativePath),
                  ),
                  listings,
                )
                assert.equal(
                  new TextDecoder().decode(yield* files.read(scenario.changedRelativePath)),
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

    test('目标写入失败时来源完整保留，状态从实际磁盘重新装配', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await workspace.write('image.png', Uint8Array.of(1, 2))
        await workspace.write('image.png.guid', 'a'.repeat(32))
        await Effect.runPromise(files.makeDirectory('assets'))
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const service: ProjectEntities = yield* ProjectEntities.make(files)
              const asset: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                'image.png',
              )
              const target: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                'assets',
              )
              const candidate: EntityChange = yield* service.prepareOperation(
                yield* service.logicalSnapshot(),
                new MoveEntity(asset.id, target.id, 'image.png'),
              )
              const write: ProjectFiles['write'] = files.write.bind(files)
              vi.spyOn(files, 'write').mockImplementation(
                (relativePath: string, bytes: Uint8Array): Effect.Effect<boolean, unknown> =>
                  relativePath === 'assets/image.png.guid'
                    ? Effect.fail(new Error('injected target failure'))
                    : write(relativePath, bytes),
              )
              const failed: Result.Result<boolean, unknown> = yield* Effect.result(
                service.commit(candidate),
              )
              assert.equal(failed._tag, 'Failure')
              assert.deepEqual(yield* files.read('image.png'), Uint8Array.of(1, 2))
              assert.deepEqual(yield* files.read('assets/image.png'), Uint8Array.of(1, 2))
              assert.equal(
                new TextDecoder().decode(yield* files.read('image.png.guid')),
                'a'.repeat(32),
              )
              assert.equal(
                (yield* service.logicalSnapshot()).diagnostics.some(
                  (diagnostic: LogicalEntitySnapshot['diagnostics'][number]): boolean =>
                    diagnostic.kind === 'missing-guid' &&
                    diagnostic.relativePath === 'assets/image.png',
                ),
                true,
              )
            }),
          ),
        )
      } finally {
        await workspace.close()
      }
    })

    test('主动刷新派生目录更新逻辑快照并保持历史隔离', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await Effect.runPromise(files.makeDirectory('ship.mech_'))
        await Effect.runPromise(files.makeDirectory('ship.mech_/2'))
        await workspace.write('ship.mech', Uint8Array.of(1))
        await workspace.write('ship.mech.guid', 'a'.repeat(32))
        await workspace.write('ship.mech_/mech.json', 'list:["2-1101"]')
        await workspace.write('image.bin', Uint8Array.of(1))
        await workspace.write('image.bin.guid', 'b'.repeat(32))
        await workspace.write(
          'ship.mech_/2/device.json',
          Hjson.stringify({ type: 1101, ext: { logicName: 'before' } }),
        )
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const service: ProjectEntities = yield* ProjectEntities.make(files)
              const original: LogicalEntitySnapshot = yield* service.logicalSnapshot()
              const initial: LogicalEntity = LogicalOperationTests.entity(original, 'ship.mech_/2')
              assert.ok(initial instanceof LogicalDevice)
              assert.equal(Option.getOrThrow(initial.fields.logicName), 'before')
              yield* Effect.tryPromise({
                try: (): Promise<void> =>
                  workspace.write(
                    'ship.mech_/2/device.json',
                    Hjson.stringify({ type: 1101, ext: { logicName: 'after' } }),
                  ),
                catch: (cause: unknown): unknown => cause,
              })
              assert.equal(yield* service.logicalSnapshot(), original)
              const refresh: Effect.Success<ReturnType<ProjectEntities['refresh']>> =
                yield* service.refresh(['ship.mech_/2'])
              assert.ok(refresh.updatedEntityIds.includes('device:ship.mech_/2'))
              assert.deepEqual(refresh.removedEntityIds, [])
              const updated: LogicalEntitySnapshot = yield* service.logicalSnapshot()
              const updatedDevice: LogicalEntity = LogicalOperationTests.entity(
                updated,
                'ship.mech_/2',
              )
              assert.ok(updatedDevice instanceof LogicalDevice)
              assert.equal(Option.getOrThrow(updatedDevice.fields.logicName), 'after')
              const before: LogicalEntity = LogicalOperationTests.entity(original, 'ship.mech_/2')
              assert.ok(before instanceof LogicalDevice)
              assert.equal(Option.getOrThrow(before.fields.logicName), 'before')
              const written: number = workspace.writtenBytes()
              const stale: Result.Result<EntityChange, unknown> = yield* Effect.result(
                service.prepareOperation(
                  original,
                  new UpdateDeviceFields(before.id, before.fields),
                ),
              )
              assert.equal(stale._tag, 'Failure')
              assert.equal(
                String(Option.getOrThrow(Result.getFailure(stale))).includes(
                  '候选文件基准已变化: ship.mech_/2/device.json',
                ),
                true,
              )
              assert.equal(workspace.writtenBytes(), written)
              assert.deepEqual(
                Hjson.parse(
                  new TextDecoder().decode(yield* files.read('ship.mech_/2/device.json')),
                ),
                { type: 1101, ext: { logicName: 'after' } },
              )
              yield* service.commit(
                yield* service.prepareOperation(
                  updated,
                  new UpdateDeviceFields(before.id, {
                    logicName: Option.some('edited'),
                    mechFileGuid: Option.none(),
                    stickToBone: Option.none(),
                  }),
                ),
              )
              const edited: LogicalEntity = LogicalOperationTests.entity(
                yield* service.logicalSnapshot(),
                'ship.mech_/2',
              )
              assert.ok(edited instanceof LogicalDevice)
              assert.equal(Option.getOrThrow(edited.fields.logicName), 'edited')
              const unrelated: LogicalEntity = LogicalOperationTests.entity(original, 'image.bin')
              yield* service.commit(
                yield* service.prepareOperation(
                  original,
                  new UpdateBytes(unrelated.id, Uint8Array.of(3)),
                ),
              )
              assert.deepEqual(yield* files.read('image.bin'), Uint8Array.of(3))
              const forbidden: Result.Result<EntityChange, unknown> = yield* Effect.result(
                service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new UpdateBytes('mech:ship.mech', Uint8Array.of(2)),
                ),
              )
              assert.equal(forbidden._tag, 'Failure')
            }),
          ),
        )
      } finally {
        await workspace.close()
      }
    })
  }
}
