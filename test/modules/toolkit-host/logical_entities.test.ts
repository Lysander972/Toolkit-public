import { test, vi } from 'vitest'
import type { MockInstance } from 'vitest'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import Hjson from 'hjson'
import { Effect, Option, Result, Scope } from 'effect'
import type {
  LogicalDiagnostic,
  LogicalEntity,
  DeviceDeclaration,
  GuidEntityDeclaration,
  CarrierKind,
  LogicalEntityQuery,
} from '../../../src/toolkit/project_model/LogicalEntities.ts'
import type { EntitySnapshot } from '../../../src/toolkit/project_model/FileEntities.ts'
import { CarrierIndex } from '../../../src/toolkit/project_model/CarrierIndex.ts'
import {
  LogicalDevice,
  LogicalCarrier,
  LogicalRoot,
  LogicalDirectory,
  LogicalRawFile,
  LogicalEntitySnapshot,
  LogicalScriptGroup,
  LogicalGuidAsset,
  CreateFileEntity,
  CreateContainer,
  CreateDevice,
  MoveEntity,
  DeleteEntity,
  UpdateDeviceFields,
} from '../../../src/toolkit/project_model/LogicalEntities.ts'
import { ProjectEntities } from '../../../src/toolkit/entities/ProjectEntities.ts'
import { LogicalEntitySources } from '../../../src/toolkit/entities/LogicalEntitySources.ts'
import { EntitySources } from '../../../src/toolkit/entities/EntitySources.ts'
import { EntityFormats } from '../../../src/toolkit/entities/EntityFormats.ts'
import { EntityChange } from '../../../src/toolkit/entities/EntityChange.ts'
import { ProjectFiles } from '../../../src/toolkit/files/ProjectFiles.ts'
import { FileTestWorkspace } from '../toolkit-file-support/FileTestWorkspace.ts'

/** @anchor logical-entity-model @anchor logical-entity-shape @anchor entity-sources @anchor entity-runtime @anchor logical-entity-operations @anchor entity-candidates */
export class LogicalEntityTests {
  private constructor() {}

  private static entity(
    snapshot: LogicalEntitySnapshot,
    kind: LogicalEntity['kind'],
    relativePath: string,
  ): LogicalEntity {
    return Option.getOrThrow(
      Option.fromNullishOr(
        snapshot.entities.find(
          (entity: LogicalEntity): boolean =>
            entity.kind === kind && entity.primaryRelativePath === relativePath,
        ),
      ),
    )
  }

  static {
    test('逻辑快照GUID查询保留筛选和冲突语义，全局检查包含未引用身份', (): void => {
      const root: LogicalRoot = new LogicalRoot(
        { primaryRelativePath: '.', files: [], directories: [{ relativePath: '.', entries: [] }] },
        Option.none(),
      )
      const assets: readonly LogicalGuidAsset[] = ['first.code', 'second.bin'].map(
        (relativePath: string): LogicalGuidAsset =>
          new LogicalGuidAsset({
            kind: relativePath === 'first.code' ? 'code-repo-script' : 'resource',
            primaryRelativePath: relativePath,
            parentId: root.id,
            guid: 'b'.repeat(32),
            files: [
              { relativePath, revision: '1' },
              { relativePath: relativePath + '.guid', revision: '1' },
            ],
            directories: [],
          }),
      )
      const first: LogicalGuidAsset = Option.getOrThrow(Option.fromNullishOr(assets[0]))
      const query: LogicalEntityQuery = {
        guid: Option.some(first.guid),
        kind: Option.some('code-repo-script'),
        primaryRelativePath: Option.some(first.primaryRelativePath),
        parentId: Option.some(root.id),
      }
      const unique: LogicalEntitySnapshot = new LogicalEntitySnapshot([root, first], [])
      unique.requireUniqueGuids()
      assert.deepEqual(unique.query(query), [first])
      assert.deepEqual(unique.query({ ...query, guid: Option.some('c'.repeat(32)) }), [])
      assert.deepEqual(unique.query({ ...query, parentId: Option.some('missing') }), [])
      assert.deepEqual(
        unique.query({ ...query, primaryRelativePath: Option.some('missing.code') }),
        [],
      )
      assert.deepEqual(unique.query({ ...query, kind: Option.some('resource') }), [])
      const conflicting: LogicalEntitySnapshot = new LogicalEntitySnapshot([root, ...assets], [])
      assert.throws(
        (): void => {
          conflicting.requireUniqueGuids()
        },
        {
          message: 'GUID 重复: ' + first.guid,
        },
      )
      assert.throws((): readonly LogicalEntity[] => conflicting.query(query), {
        message: 'GUID 重复: ' + first.guid,
      })
      assert.deepEqual(
        conflicting.query({
          ...query,
          guid: Option.none(),
          kind: Option.none(),
          primaryRelativePath: Option.none(),
          parentId: Option.none(),
        }),
        [root, ...assets],
      )
    })

    test('实体声明限制文件覆盖与载体派生结构，零件目录对应索引 ID', (): void => {
      const asset: GuidEntityDeclaration = {
        primaryRelativePath: 'asset.bin',
        parentId: 'root',
        guid: 'a'.repeat(32),
        files: [
          { relativePath: 'asset.bin', revision: 'asset' },
          { relativePath: 'asset.bin.guid', revision: 'guid' },
        ],
        directories: [],
      }
      assert.deepEqual(new LogicalGuidAsset({ ...asset, kind: 'resource' }).ownedRelativePaths, [
        'asset.bin',
        'asset.bin.guid',
      ])
      for (const relativePath of ['asset.bin.guid', 'asset.bin/child', 'unrelated.bin'])
        assert.throws(
          (): LogicalRawFile =>
            new LogicalRawFile({
              ...asset,
              files: [
                { relativePath: 'asset.bin', revision: 'asset' },
                { relativePath, revision: 'invalid' },
              ],
            }),
          /主体范围/u,
        )
      for (const relativePath of ['asset.bin/child', 'unrelated.bin'])
        assert.throws(
          (): LogicalGuidAsset =>
            new LogicalGuidAsset({
              ...asset,
              kind: 'resource',
              files: [...asset.files, { relativePath, revision: 'invalid' }],
            }),
          /主体范围/u,
        )
      const carrierKinds: readonly CarrierKind[] = ['map', 'mech']
      for (const kind of carrierKinds) {
        const primaryRelativePath: string = 'scene.' + kind
        const carrier: GuidEntityDeclaration = {
          ...asset,
          primaryRelativePath,
          files: [
            { relativePath: primaryRelativePath, revision: 'carrier' },
            { relativePath: primaryRelativePath + '.guid', revision: 'guid' },
            { relativePath: primaryRelativePath + '_/mech.json', revision: 'index' },
            { relativePath: primaryRelativePath + '_/map.json', revision: 'metadata' },
          ],
          directories: [{ relativePath: primaryRelativePath + '_', entries: [] }],
        }
        assert.equal(new LogicalCarrier({ ...carrier, kind }).ownedRelativePaths.length, 5)
        for (const relativePath of ['unrelated.bin', primaryRelativePath + '_/2/device.json'])
          assert.throws(
            (): LogicalCarrier =>
              new LogicalCarrier({
                ...carrier,
                kind,
                files: [...carrier.files, { relativePath, revision: 'invalid' }],
              }),
            /主体范围/u,
          )
      }
      const device: DeviceDeclaration = {
        primaryRelativePath: 'scene.map_/2',
        parentId: 'map:scene.map',
        files: [],
        directories: [{ relativePath: 'scene.map_/2', entries: [] }],
        deviceId: 2,
        deviceType: 1101,
        fields: {
          logicName: Option.none(),
          mechFileGuid: Option.none(),
          stickToBone: Option.none(),
        },
        variables: Option.none(),
      }
      assert.equal(new LogicalDevice(device).deviceId, 2)
      assert.throws(
        (): LogicalDevice => new LogicalDevice({ ...device, deviceId: 3 }),
        /目录名称与索引/u,
      )
    })

    test('真实载体索引装配零件与有序脚本，全部文件只有一个归属', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        for (const relativePath of [
          'scene.map_',
          'scene.map_/2',
          'scene.map_/2/event_clientStart',
          'scene.map_/99',
        ])
          await Effect.runPromise(files.makeDirectory(relativePath))
        await workspace.write('scene.map', Uint8Array.of(255, 0))
        await workspace.write('scene.map.guid', 'a'.repeat(32))
        await workspace.write(
          'scene.map_/mech.json',
          await readFile('test/data/projects/颜色对对碰/test.map_/mech.json', 'utf8'),
        )
        await workspace.write(
          'scene.map_/map.json',
          await readFile('test/data/projects/颜色对对碰/test.map_/map.json', 'utf8'),
        )
        await workspace.write(
          'scene.map_/2/device.json',
          await readFile('test/data/projects/颜色对对碰/test.map_/2/device.json', 'utf8'),
        )
        await workspace.write(
          'scene.map_/2/event_clientStart/list.json',
          await readFile(
            'test/data/projects/颜色对对碰/test.map_/2/event_clientStart/list.json',
            'utf8',
          ),
        )
        await workspace.write('scene.map_/2/event_clientStart/初始化基础设定.code', 'raw-source')
        await workspace.write('scene.map_/99/device.json', 'type:99')
        await workspace.write('unknown.dat', 'preserved')
        const enumerations: MockInstance<ProjectFiles['directory']> = vi.spyOn(files, 'directory')
        const raw: EntitySnapshot = await Effect.runPromise(new EntitySources(files).load())
        const enumerated: number = enumerations.mock.calls.length
        const snapshot: LogicalEntitySnapshot = await Effect.runPromise(
          new LogicalEntitySources(files).load(raw),
        )
        assert.equal(enumerated, raw.directories.length)
        assert.equal(enumerations.mock.calls.length, enumerated)
        const carrier: LogicalEntity = LogicalEntityTests.entity(snapshot, 'map', 'scene.map')
        const device: LogicalEntity = LogicalEntityTests.entity(snapshot, 'device', 'scene.map_/2')
        assert.equal(Option.getOrThrow(device.parentId), carrier.id)
        assert.equal(device instanceof LogicalDevice && device.deviceType === 1101, true)
        const group: LogicalEntity = LogicalEntityTests.entity(
          snapshot,
          'script-group',
          'scene.map_/2/event_clientStart',
        )
        assert.equal(
          group instanceof LogicalScriptGroup &&
            Option.getOrThrow(Option.fromNullishOr(group.entries[0])).name,
          '初始化基础设定',
        )
        assert.equal(
          snapshot.diagnostics.some(
            (diagnostic: LogicalDiagnostic): boolean => diagnostic.kind === 'unindexed-device',
          ),
          true,
        )
        assert.equal(
          snapshot.diagnostics.filter(
            (diagnostic: LogicalDiagnostic): boolean => diagnostic.kind === 'missing-device',
          ).length,
          2,
        )
        const owned: readonly string[] = snapshot.entities.flatMap(
          (entity: LogicalEntity): readonly string[] => entity.ownedRelativePaths,
        )
        assert.equal(new Set(owned).size, owned.length)
        assert.equal(owned.includes('unknown.dat'), true)
        assert.equal(owned.includes('scene.map_/99/device.json'), true)
        assert.throws(
          (): LogicalEntitySnapshot =>
            new LogicalEntitySnapshot([...snapshot.entities, snapshot.entity('root')], []),
          /重复实体身份/u,
        )
        const conflicting: LogicalRawFile = new LogicalRawFile({
          primaryRelativePath: 'scene.map',
          parentId: 'root',
          files: [{ relativePath: 'scene.map', revision: 'conflict' }],
          directories: [],
        })
        assert.throws(
          (): LogicalEntitySnapshot =>
            new LogicalEntitySnapshot([...snapshot.entities, conflicting], []),
          /重复文件归属/u,
        )
        const invalidParent: LogicalRawFile = new LogicalRawFile({
          primaryRelativePath: 'extra.bin',
          parentId: 'missing',
          files: [{ relativePath: 'extra.bin', revision: 'missing' }],
          directories: [],
        })
        assert.throws(
          (): LogicalEntitySnapshot =>
            new LogicalEntitySnapshot([...snapshot.entities, invalidParent], []),
        )
        const actualDevice: LogicalDevice = device.require(LogicalDevice)
        const wrongParent: LogicalDevice = new LogicalDevice({
          primaryRelativePath: '2',
          files: [{ relativePath: '2/device.json', revision: 'invalid' }],
          directories: [{ relativePath: '2', entries: ['2/device.json'] }],
          deviceId: actualDevice.deviceId,
          deviceType: actualDevice.deviceType,
          fields: actualDevice.fields,
          variables: actualDevice.variables,
          parentId: 'root',
        })
        assert.throws(
          (): LogicalEntitySnapshot =>
            new LogicalEntitySnapshot([snapshot.entity('root'), wrongParent], []),
          /实体类型不符/u,
        )
        assert.throws(
          (): LogicalDirectory =>
            new LogicalDirectory({
              primaryRelativePath: 'unknown.dat',
              parentId: 'root',
              files: [{ relativePath: 'unknown.dat', revision: 'invalid' }],
              directories: [],
            }),
          /主体形状/u,
        )
        assert.throws(
          (): LogicalRoot =>
            new LogicalRoot(
              {
                primaryRelativePath: 'other',
                files: [],
                directories: [{ relativePath: 'other', entries: [] }],
              },
              Option.none(),
            ),
          /工程根/u,
        )
      } finally {
        await workspace.close()
      }
    })

    test('创建、移动、删除资源保留身份与伴生原文，候选准备零写入', async (): Promise<void> => {
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
              const service: ProjectEntities = yield* ProjectEntities.make(files)
              const before: number = workspace.writtenBytes()
              const candidate: EntityChange = yield* service.prepareOperation(
                yield* service.logicalSnapshot(),
                new CreateFileEntity('root', 'resource', 'asset.bin', Uint8Array.of(255, 0)),
              )
              assert.equal(workspace.writtenBytes(), before)
              yield* service.commit(candidate)
              const asset: LogicalEntity = LogicalEntityTests.entity(
                yield* service.logicalSnapshot(),
                'resource',
                'asset.bin',
              )
              assert.equal(asset instanceof LogicalGuidAsset && asset.guid.length, 32)
              const metadata: Uint8Array = yield* files.read('asset.bin.guid')
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new CreateContainer('root', 'directory', 'assets'),
                ),
              )
              const directory: LogicalEntity = LogicalEntityTests.entity(
                yield* service.logicalSnapshot(),
                'directory',
                'assets',
              )
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new MoveEntity(asset.id, directory.id, 'renamed.bin'),
                ),
              )
              assert.deepEqual(yield* files.read('assets/renamed.bin.guid'), metadata)
              const moved: LogicalEntity = LogicalEntityTests.entity(
                yield* service.logicalSnapshot(),
                'resource',
                'assets/renamed.bin',
              )
              assert.equal(
                moved instanceof LogicalGuidAsset && moved.guid,
                asset instanceof LogicalGuidAsset && asset.guid,
              )
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new DeleteEntity(directory.id),
                ),
              )
              assert.equal((yield* service.logicalSnapshot()).entities.length, 1)
            }),
          ),
        )
      } finally {
        await workspace.close()
      }
    })

    test('零件创建与删除显式更新索引，元数据保留未知字段，载体不可移动', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await Effect.runPromise(files.makeDirectory('scene.mech_'))
        await workspace.write('scene.mech', Uint8Array.of(1))
        await workspace.write('scene.mech.guid', 'b'.repeat(32))
        await workspace.write('scene.mech_/mech.json', '# 索引\nlist: []\nunknown: retained\n')
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const service: ProjectEntities = yield* ProjectEntities.make(files)
              const carrier: LogicalEntity = LogicalEntityTests.entity(
                yield* service.logicalSnapshot(),
                'mech',
                'scene.mech',
              )
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new CreateDevice(
                    carrier.id,
                    3,
                    'type:1101\next:{ logicName:"original", unknown:"keep" }\ncustom:keep\n',
                  ),
                ),
              )
              const device: LogicalEntity = LogicalEntityTests.entity(
                yield* service.logicalSnapshot(),
                'device',
                'scene.mech_/3',
              )
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new UpdateDeviceFields(device.id, {
                    logicName: Option.some('modified'),
                    mechFileGuid: Option.none(),
                    stickToBone: Option.none(),
                  }),
                ),
              )
              const updated: string = new TextDecoder().decode(
                yield* files.read('scene.mech_/3/device.json'),
              )
              assert.equal(updated.includes('custom: keep'), true)
              assert.equal(updated.includes('unknown: keep'), true)
              assert.deepEqual(
                CarrierIndex.read(
                  new TextDecoder().decode(yield* files.read('scene.mech_/mech.json')),
                ).entries(),
                [{ deviceId: 3, deviceType: 1101 }],
              )
              const forbidden: Result.Result<EntityChange, unknown> = yield* Effect.result(
                service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new MoveEntity(carrier.id, 'root', 'other.mech'),
                ),
              )
              assert.equal(forbidden._tag, 'Failure')
              yield* service.commit(
                yield* service.prepareOperation(
                  yield* service.logicalSnapshot(),
                  new DeleteEntity(device.id),
                ),
              )
              assert.deepEqual(
                CarrierIndex.read(
                  new TextDecoder().decode(yield* files.read('scene.mech_/mech.json')),
                ).entries(),
                [],
              )
            }),
          ),
        )
      } finally {
        await workspace.close()
      }
    })
  }
}

/** @anchor logical-entity-model @anchor logical-entity-formats */
export class EntityFormatTests {
  private constructor() {}
  static {
    test('分组索引保留未知字段，严格数值校验且不按目录重建', (): void => {
      const index: CarrierIndex = CarrierIndex.read(
        '# 说明\nlist:[{name:"group", list:["2-1101", "3-1110"]}]\nunknown:retained\n',
      )
      index.remove(2)
      index.add({ deviceId: 4, deviceType: 1100 })
      assert.deepEqual(CarrierIndex.read(index.text()).entries(), [
        { deviceId: 3, deviceType: 1110 },
        { deviceId: 4, deviceType: 1100 },
      ])
      assert.equal(index.text().includes('# 说明'), true)
      assert.equal(index.text().includes('unknown: retained'), true)
      assert.throws((): CarrierIndex => CarrierIndex.read('list:["0--1"]'))
      assert.throws((): CarrierIndex => CarrierIndex.read('list:["0-1", "0-2"]'))
      assert.throws((): CarrierIndex => CarrierIndex.read('missing:[]'))
    })

    test('Hjson 可选字段删除与注释往返，不变内容逐字保持', (): void => {
      const source: string =
        '# 零件\ntype:1101\next:\n{\n # 名称\n logicName:before\n unknown:keep\n}\nstickToBone:bone\ncustom:[1,2,3]\n'
      const same: string = EntityFormats.device(source, {
        logicName: Option.some('before'),
        mechFileGuid: Option.none(),
        stickToBone: Option.some('bone'),
      })
      assert.equal(same, source)
      const edited: string = EntityFormats.device(source, {
        logicName: Option.none(),
        mechFileGuid: Option.none(),
        stickToBone: Option.none(),
      })
      const parsed: unknown = Hjson.parse(edited)
      assert.deepEqual(parsed, { type: 1101, ext: { unknown: 'keep' }, custom: [1, 2, 3] })
      assert.equal(edited.includes('# 零件'), true)
    })
  }
}
