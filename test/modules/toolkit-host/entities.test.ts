import { test, vi } from 'vitest'
import type { MockInstance } from 'vitest'
import assert from 'node:assert/strict'
import { Effect, Option } from 'effect'
import type {
  EntityDiagnostic,
  EntitySnapshot,
  EntityDirectoryListing,
  FileEntity,
} from '../../../src/toolkit/project_model/FileEntities.ts'
import { EntityState } from '../../../src/toolkit/entities/EntityState.ts'
import { EntitySources } from '../../../src/toolkit/entities/EntitySources.ts'
import { EntityChange } from '../../../src/toolkit/entities/EntityChange.ts'
import { ProjectFiles } from '../../../src/toolkit/files/ProjectFiles.ts'
import { FileTestWorkspace } from '../toolkit-file-support/FileTestWorkspace.ts'

/** @anchor entity-state */
export class EntityStateTests {
  private constructor() {}

  static {
    test('MST 隔离历史快照，物理路径标识允许报告重复逻辑 GUID', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        const guid: string = 'a'.repeat(32)
        await workspace.write('first.bin', Uint8Array.of(255, 0, 1))
        await workspace.write('first.bin.guid', guid)
        const source: EntitySources = new EntitySources(files)
        const state: EntityState = new EntityState()
        try {
          const original: EntitySnapshot = state.replace(await Effect.runPromise(source.load()))
          const entity: FileEntity = Option.getOrThrow(state.resolve(guid))
          assert.equal(entity.relativePath, 'first.bin')
          await workspace.write('first.bin', Uint8Array.of(254, 0, 2))
          const updated: EntitySnapshot = state.replace(
            await Effect.runPromise(source.refresh(original, '.')),
          )
          assert.notEqual(
            Option.getOrThrow(Option.fromNullishOr(updated.entities[0])).revision,
            entity.revision,
          )
          assert.equal(
            Option.getOrThrow(Option.fromNullishOr(original.entities[0])).revision,
            entity.revision,
          )
          assert.equal(state.replace(updated), updated)
          await workspace.write('second.bin', 'second')
          await workspace.write('second.bin.guid', guid)
          const duplicate: EntitySnapshot = state.replace(
            await Effect.runPromise(source.refresh(updated, '.')),
          )
          assert.equal(
            duplicate.diagnostics.filter(
              (diagnostic: EntityDiagnostic): boolean => diagnostic.kind === 'duplicate-guid',
            ).length,
            2,
          )
          assert.throws((): Option.Option<FileEntity> => state.resolve(guid))
          assert.equal(Option.isNone(state.resolve('b'.repeat(32))), true)
          await Effect.runPromise(files.remove('second.bin.guid'))
          state.replace(await Effect.runPromise(source.refresh(duplicate, '.')))
          assert.equal(Option.getOrThrow(state.resolve(guid)).relativePath, 'first.bin')
        } finally {
          state.close()
        }
      } finally {
        await workspace.close()
      }
    })
  }
}

/**
 * @anchor entity-sources
 * @anchor entity-file-identity
 */
export class EntitySourceTests {
  private constructor() {}

  static {
    test('伴生文件装配只读取 GUID，Spine 文件不依赖特定目录且只诊断 GUID 问题', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await Effect.runPromise(files.makeDirectory('assets'))
        await Effect.runPromise(files.makeDirectory('scene.map_'))
        await workspace.write('scene.map_/inline.code', 'inline')
        for (const [index, extension] of [
          'atlas',
          'skel',
          'json',
          'png',
          'canvas',
          'code',
        ].entries()) {
          await workspace.write(`assets/asset.${extension}`, Uint8Array.of(255, index, 0))
          await workspace.write(`assets/asset.${extension}.guid`, String(index + 1).repeat(32))
        }
        await workspace.write('missing.png', 'missing')
        await workspace.write('orphan.bin.guid', 'a'.repeat(32))
        await workspace.write('invalid.bin', 'invalid')
        await workspace.write('invalid.bin.guid', 'guid: bad\nquality: 80')
        const reads: MockInstance<ProjectFiles['read']> = vi.spyOn(files, 'read')
        const source: EntitySources = new EntitySources(files)
        const snapshot: EntitySnapshot = await Effect.runPromise(source.load())
        assert.equal(snapshot.entities.length, 6)
        assert.deepEqual(
          snapshot.diagnostics
            .map((diagnostic: EntityDiagnostic): string => diagnostic.kind)
            .sort((left: string, right: string): number => left.localeCompare(right)),
          ['invalid-guid', 'missing-guid', 'orphan-guid'],
        )
        assert.equal(
          reads.mock.calls.every(([relativePath]: readonly [string]): boolean =>
            relativePath.endsWith('.guid'),
          ),
          true,
        )
        assert.equal(
          snapshot.entities.some((entity: FileEntity): boolean =>
            entity.relativePath.endsWith('.json'),
          ),
          true,
        )
        assert.equal(
          snapshot.directories.some(
            (directory: EntityDirectoryListing): boolean => directory.relativePath === 'scene.map_',
          ),
          true,
        )
        assert.equal(
          snapshot.entities.some(
            (entity: FileEntity): boolean => entity.relativePath === 'scene.map_/inline.code',
          ),
          false,
        )
        const untouched: FileEntity = Option.getOrThrow(Option.fromNullishOr(snapshot.entities[0]))
        await workspace.write('other.code', 'other')
        const refreshed: EntitySnapshot = await Effect.runPromise(source.refresh(snapshot, '.'))
        assert.equal(
          refreshed.entities.find(
            (entity: FileEntity): boolean => entity.relativePath === untouched.relativePath,
          ),
          untouched,
        )
        assert.equal(
          refreshed.diagnostics.some(
            (diagnostic: EntityDiagnostic): boolean => diagnostic.relativePath === 'other.code',
          ),
          true,
        )
      } finally {
        await workspace.close()
      }
    })

    test('目录创建和移除更新文件覆盖集合，删除目录中的文件通知归并到实际父目录', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        const source: EntitySources = new EntitySources(files)
        const empty: EntitySnapshot = await Effect.runPromise(source.load())
        await Effect.runPromise(files.makeDirectory('new'))
        await workspace.write('new/source.bin', 'value')
        await workspace.write('new/source.bin.guid', 'a'.repeat(32))
        const added: EntitySnapshot = await Effect.runPromise(source.refresh(empty, '.'))
        assert.equal(added.entities.length, 1)
        await Effect.runPromise(files.remove('new/source.bin'))
        await Effect.runPromise(files.remove('new/source.bin.guid'))
        await Effect.runPromise(files.removeDirectory('new'))
        const removed: EntitySnapshot = await Effect.runPromise(source.refresh(added, 'new'))
        assert.deepEqual(removed, empty)
      } finally {
        await workspace.close()
      }
    })
  }
}

/** @anchor entity-candidates */
export class EntityCandidateTests {
  private constructor() {}

  static {
    test('候选分支隔离与文件冲突使用 Schematics，落盘保持伴生元数据且相同内容零写入', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        const original: Uint8Array = Uint8Array.of(255, 0, 1)
        const replacement: Uint8Array = Uint8Array.of(254, 0, 2)
        const metadata: string = `guid: ${'a'.repeat(32)}\nquality: 80\nslice: thumbnail\n`
        await workspace.write('resource.bin', original)
        await workspace.write('resource.bin.guid', metadata)
        const source: EntitySources = new EntitySources(files)
        const entity: FileEntity = Option.getOrThrow(
          Option.fromNullishOr((await Effect.runPromise(source.load())).entities[0]),
        )
        const base: EntityChange = await Effect.runPromise(
          EntityChange.prepare(files, entity, original),
        )
        const first: EntityChange = base.branch()
        const second: EntityChange = base.branch()
        first.write('resource.bin', replacement)
        second.write('resource.bin', Uint8Array.of(253, 0, 3))
        assert.deepEqual(await Effect.runPromise(files.read('resource.bin')), original)
        base.merge(first)
        assert.throws((): void => {
          base.merge(second)
        })
        const before: number = workspace.writtenBytes()
        assert.equal(await Effect.runPromise(base.commit(files)), true)
        assert.equal(workspace.writtenBytes() - before, replacement.byteLength)
        assert.deepEqual(await Effect.runPromise(files.read('resource.bin')), replacement)
        assert.equal(
          new TextDecoder().decode(await Effect.runPromise(files.read('resource.bin.guid'))),
          metadata,
        )
        const current: FileEntity = Option.getOrThrow(
          Option.fromNullishOr((await Effect.runPromise(source.load())).entities[0]),
        )
        const same: EntityChange = await Effect.runPromise(
          EntityChange.prepare(files, current, replacement),
        )
        const unchanged: number = workspace.writtenBytes()
        assert.equal(await Effect.runPromise(same.commit(files)), false)
        assert.equal(workspace.writtenBytes(), unchanged)
        const rename: EntityChange = await Effect.runPromise(
          EntityChange.open(
            files,
            'renamed.bin',
            [{ relativePath: 'resource.bin', revision: current.revision }],
            [{ relativePath: '.', entries: ['resource.bin', 'resource.bin.guid'] }],
          ),
        )
        rename.write('resource.bin', Uint8Array.of(1, 2, 3, 4))
        rename.rename('resource.bin', 'renamed.bin')
        assert.equal(await Effect.runPromise(rename.commit(files)), true)
        assert.deepEqual(
          await Effect.runPromise(files.read('renamed.bin')),
          Uint8Array.of(1, 2, 3, 4),
        )
      } finally {
        await workspace.close()
      }
    })

    test('主体或伴生文件外部变化使候选失效，输入副本隔离且不会覆盖外部结果', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await workspace.write('resource.bin', 'original')
        await workspace.write('resource.bin.guid', 'a'.repeat(32))
        const source: EntitySources = new EntitySources(files)
        const entity: FileEntity = Option.getOrThrow(
          Option.fromNullishOr((await Effect.runPromise(source.load())).entities[0]),
        )
        const candidate: EntityChange = await Effect.runPromise(
          EntityChange.prepare(files, entity, Uint8Array.of(255)),
        )
        await workspace.write('resource.bin.guid', `guid: ${entity.guid}\nquality: 80`)
        const before: number = workspace.writtenBytes()
        await assert.rejects(
          Effect.runPromise(candidate.commit(files)),
          (cause: unknown): boolean => String(cause).includes('候选文件基准已变化'),
        )
        assert.equal(workspace.writtenBytes(), before)
        assert.equal(
          new TextDecoder().decode(await Effect.runPromise(files.read('resource.bin'))),
          'original',
        )
        const updated: FileEntity = Option.getOrThrow(
          Option.fromNullishOr((await Effect.runPromise(source.load())).entities[0]),
        )
        const bytes: Uint8Array = Uint8Array.of(9, 8, 7)
        const isolated: EntityChange = await Effect.runPromise(
          EntityChange.prepare(files, updated, bytes),
        )
        bytes.fill(0)
        await Effect.runPromise(isolated.commit(files))
        assert.deepEqual(
          await Effect.runPromise(files.read('resource.bin')),
          Uint8Array.of(9, 8, 7),
        )
        await assert.rejects(Effect.runPromise(isolated.commit(files)), (cause: unknown): boolean =>
          String(cause).includes('候选文件基准已变化'),
        )
        const latest: FileEntity = Option.getOrThrow(
          Option.fromNullishOr((await Effect.runPromise(source.load())).entities[0]),
        )
        const external: EntityChange = await Effect.runPromise(
          EntityChange.prepare(files, latest, Uint8Array.of(6)),
        )
        await workspace.write('resource.bin', Uint8Array.of(5))
        const externalBytes: number = workspace.writtenBytes()
        await assert.rejects(Effect.runPromise(external.commit(files)), (cause: unknown): boolean =>
          String(cause).includes('候选文件基准已变化'),
        )
        assert.equal(workspace.writtenBytes(), externalBytes)
        assert.deepEqual(await Effect.runPromise(files.read('resource.bin')), Uint8Array.of(5))
      } finally {
        await workspace.close()
      }
    })
  }
}
