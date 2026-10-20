import { test, vi } from 'vitest'
import assert from 'node:assert/strict'
import { link, mkdir, readFile, readdir, stat, symlink, utimes } from 'node:fs/promises'
import type { Stats } from 'node:fs'
import { join } from 'node:path'
import { Effect } from 'effect'
import type { MockInstance } from 'vitest'
import { ProjectFiles } from '../../../src/toolkit/files/ProjectFiles.ts'
import { PlatformFiles } from '../../../src/toolkit/files/PlatformFiles.ts'
import type { FileMetadata } from '../../../src/toolkit/files/FileContracts.ts'
import { FileTestWorkspace } from '../toolkit-file-support/FileTestWorkspace.ts'

/**
 * @anchor workspace-file-cache
 * @anchor workspace-file-cache-inventory
 * @anchor workspace-computation-shortcuts
 */
export class FileCacheTests {
  private constructor() {}

  static {
    test('并发读取合并加载并返回独立字节副本', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await workspace.write('source.code', 'first')
        const spy: MockInstance<PlatformFiles['read']> = vi.spyOn(PlatformFiles.prototype, 'read')
        const contents: Uint8Array[] = await Promise.all(
          Array.from({ length: 8 }, (): Promise<Uint8Array> =>
            Effect.runPromise(files.read('source.code')),
          ),
        )
        assert.equal(spy.mock.calls.length, 1)
        for (const content of contents) assert.equal(new TextDecoder().decode(content), 'first')
        for (const content of contents) content.fill(0)
        assert.equal(
          new TextDecoder().decode(await Effect.runPromise(files.read('source.code'))),
          'first',
        )
        assert.equal(spy.mock.calls.length, 1)
      } finally {
        await workspace.close()
      }
    })

    test('等长修改并恢复 mtime 后仍重新读取', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await workspace.write('source.code', 'first')
        const before: FileMetadata = await Effect.runPromise(files.metadata('source.code'))
        const info: Stats = await stat(join(workspace.fullPath, 'source.code'))
        assert.equal(
          new TextDecoder().decode(await Effect.runPromise(files.read('source.code'))),
          'first',
        )
        await workspace.write('source.code', 'other')
        await utimes(join(workspace.fullPath, 'source.code'), info.atime, info.mtime)
        const after: FileMetadata = await Effect.runPromise(files.metadata('source.code'))
        assert.notEqual(before.revision, after.revision)
        assert.equal(
          new TextDecoder().decode(await Effect.runPromise(files.read('source.code'))),
          'other',
        )
      } finally {
        await workspace.close()
      }
    })

    test('读取期间变化失败并失效该次缓存', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await workspace.write('source.code', 'old')
        vi.spyOn(PlatformFiles.prototype, 'read').mockImplementationOnce(function (
          this: PlatformFiles,
          relativePath: string,
        ): Effect.Effect<Uint8Array, unknown> {
          return Effect.flatMap(
            Effect.tryPromise({
              try: (): Promise<Buffer> => readFile(join(workspace.fullPath, relativePath)),
              catch: (cause: unknown): unknown => cause,
            }),
            (bytes: Uint8Array): Effect.Effect<Uint8Array, unknown> =>
              Effect.as(
                Effect.promise((): Promise<void> => workspace.write(relativePath, 'new-content')),
                bytes,
              ),
          )
        })
        await assert.rejects(Effect.runPromise(files.read('source.code')), /读取期间文件变化/u)
        assert.equal(
          new TextDecoder().decode(await Effect.runPromise(files.read('source.code'))),
          'new-content',
        )
      } finally {
        await workspace.close()
      }
    })

    test('失败加载不缓存，超过单项预算的文件完整读取且不复用', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await workspace.write('source.code', 'value')
        const spy: MockInstance<PlatformFiles['read']> = vi.spyOn(PlatformFiles.prototype, 'read')
        spy.mockReturnValueOnce(Effect.fail(new Error('read failed')))
        await assert.rejects(Effect.runPromise(files.read('source.code')), /read failed/u)
        assert.equal(
          new TextDecoder().decode(await Effect.runPromise(files.read('source.code'))),
          'value',
        )
        await workspace.write('large.bin', 'x'.repeat(1048577))
        const first: Uint8Array = await Effect.runPromise(files.read('large.bin'))
        const second: Uint8Array = await Effect.runPromise(files.read('large.bin'))
        assert.equal(first.byteLength, 1048577)
        assert.deepEqual(first, second)
        assert.equal(spy.mock.calls.length, 4)
      } finally {
        await workspace.close()
      }
    })

    test('内容容量有界并由 Effect Cache 淘汰', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        for (let index: number = 0; index < 129; index += 1) {
          await workspace.write(`file-${String(index)}.code`, 'x')
          await Effect.runPromise(files.read(`file-${String(index)}.code`))
        }
        const spy: MockInstance<PlatformFiles['read']> = vi.spyOn(PlatformFiles.prototype, 'read')
        assert.equal(
          new TextDecoder().decode(await Effect.runPromise(files.read('file-0.code'))),
          'x',
        )
        assert.equal(spy.mock.calls.length, 1)
      } finally {
        await workspace.close()
      }
    })
  }
}

/** @anchor workspace-file-paths */
export class FilePathTests {
  private constructor() {}

  static {
    test('目录直接枚举不缓存，硬链接按路径保留', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await workspace.write('z.code', 'z')
        await link(join(workspace.fullPath, 'z.code'), join(workspace.fullPath, 'a.code'))
        let entries: readonly FileMetadata[] = await Effect.runPromise(files.directory('.'))
        assert.deepEqual(
          entries.map((entry: FileMetadata): string => entry.relativePath),
          ['a.code', 'z.code'],
        )
        assert.equal(
          entries.every((entry: FileMetadata): boolean => entry.kind === 'file'),
          true,
        )
        await mkdir(join(workspace.fullPath, 'nested'))
        entries = await Effect.runPromise(files.directory('.'))
        assert.deepEqual(
          entries.map((entry: FileMetadata): string => entry.relativePath),
          ['a.code', 'nested', 'z.code'],
        )
        await assert.rejects(Effect.runPromise(files.directory('z.code')), /不是工程目录/u)
      } finally {
        await workspace.close()
      }
    })

    test('非法路径和工程根修改直接失败', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        for (const relativePath of [
          '',
          '../outside',
          'a//b',
          'a\\b',
          './a',
          'a/./b',
          'a/../b',
          'a\0b',
          workspace.fullPath,
        ]) {
          await assert.rejects(Effect.runPromise(files.metadata(relativePath)))
        }
        const root: FileMetadata = await Effect.runPromise(files.metadata('.'))
        assert.equal(root.kind, 'directory')
        await assert.rejects(Effect.runPromise(files.remove('.')))
        await assert.rejects(Effect.runPromise(files.removeDirectory('.')))
        await assert.rejects(Effect.runPromise(files.makeDirectory('.')))
      } finally {
        await workspace.close()
      }
    })

    test('工程内链接使用原生目标，工程根外链接拒绝', async (): Promise<void> => {
      const outside: FileTestWorkspace = await FileTestWorkspace.make()
      try {
        await outside.write('external.code', 'outside')
        const workspace: FileTestWorkspace = await FileTestWorkspace.make()
        const files: ProjectFiles = workspace.files
        try {
          await mkdir(join(workspace.fullPath, 'nested'))
          await workspace.write('nested/source.code', 'inside')
          await symlink(
            join(workspace.fullPath, 'nested'),
            join(workspace.fullPath, 'alias'),
            'junction',
          )
          assert.equal(
            new TextDecoder().decode(await Effect.runPromise(files.read('alias/source.code'))),
            'inside',
          )
          await symlink(outside.fullPath, join(workspace.fullPath, 'outside'), 'junction')
          await assert.rejects(
            Effect.runPromise(files.read('outside/external.code')),
            /路径不在工程根内/u,
          )
          await assert.rejects(
            Effect.runPromise(files.write('outside/new.code', new TextEncoder().encode('x'))),
            /路径不在工程根内/u,
          )
        } finally {
          await workspace.close()
        }
      } finally {
        await outside.close()
      }
    })

    test('工程内链接写入原生目标而不替换链接', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await mkdir(join(workspace.fullPath, 'nested'))
        await workspace.write('nested/source.code', 'inside')
        await symlink(
          join(workspace.fullPath, 'nested'),
          join(workspace.fullPath, 'alias'),
          'junction',
        )
        await Effect.runPromise(
          files.write('alias/source.code', new TextEncoder().encode('updated')),
        )
        assert.equal(
          await readFile(join(workspace.fullPath, 'nested/source.code'), 'utf8'),
          'updated',
        )
      } finally {
        await workspace.close()
      }
    })
  }
}

/** @anchor workspace-file-writes */
export class FileWriteTests {
  private constructor() {}

  static {
    test('同内容零写入，原子替换保留原始字节并刷新缓存', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await workspace.write('source.code', 'old')
        await Effect.runPromise(files.read('source.code'))
        const writtenBefore: number = workspace.writtenBytes()
        assert.equal(
          await Effect.runPromise(files.write('source.code', new TextEncoder().encode('old'))),
          false,
        )
        assert.equal(workspace.writtenBytes(), writtenBefore)
        const bytes: Uint8Array = new TextEncoder().encode('new\r\nvalue\r')
        assert.equal(await Effect.runPromise(files.write('source.code', bytes)), true)
        assert.deepEqual(
          new Uint8Array(await readFile(join(workspace.fullPath, 'source.code'))),
          bytes,
        )
        assert.deepEqual(await Effect.runPromise(files.read('source.code')), bytes)
        assert.equal(workspace.writtenBytes() - writtenBefore, bytes.byteLength)
        assert.deepEqual(await readdir(workspace.fullPath), ['source.code'])
      } finally {
        await workspace.close()
      }
    })

    test('四类原语支持实体层自行编排，失败保留已完成结果', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await Effect.runPromise(files.makeDirectory('nested'))
        await Effect.runPromise(files.write('nested/value.code', new TextEncoder().encode('value')))
        await assert.rejects(Effect.runPromise(files.removeDirectory('nested')))
        assert.equal(await readFile(join(workspace.fullPath, 'nested/value.code'), 'utf8'), 'value')
        await assert.rejects(Effect.runPromise(files.remove('nested')))
        await Effect.runPromise(files.remove('nested/value.code'))
        await Effect.runPromise(files.removeDirectory('nested'))
        await assert.rejects(stat(join(workspace.fullPath, 'nested')))
        await assert.rejects(Effect.runPromise(files.remove('missing.code')))
        await assert.rejects(Effect.runPromise(files.removeDirectory('missing-directory')))
        await assert.rejects(
          Effect.runPromise(files.write('missing-parent/value.code', new Uint8Array(1))),
        )
      } finally {
        await workspace.close()
      }
    })

    test('元数据错误自然传播，不能当作不存在而覆盖', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      const files: ProjectFiles = workspace.files
      try {
        await workspace.write('source.code', 'original')
        vi.spyOn(PlatformFiles.prototype, 'metadata').mockReturnValueOnce(
          Effect.fail(new Error('metadata failed')),
        )
        await assert.rejects(
          Effect.runPromise(files.write('source.code', new TextEncoder().encode('new'))),
          /metadata failed/u,
        )
        assert.equal(await readFile(join(workspace.fullPath, 'source.code'), 'utf8'), 'original')
      } finally {
        await workspace.close()
      }
    })
  }
}

/** @anchor workspace-file-test-resources */
export class FileResourceTests {
  private constructor() {}

  static {
    test('独占资源在成功和失败后释放，另一实例保持可读', async (): Promise<void> => {
      const other: FileTestWorkspace = await FileTestWorkspace.make()
      try {
        await other.write('retained.code', 'retained')
        for (const fails of [false, true]) {
          const workspace: FileTestWorkspace = await FileTestWorkspace.make()
          const failure: Error = new Error('资源内操作失败')
          try {
            await workspace.write('source.code', 'source')
            assert.notEqual(workspace.fullPath, other.fullPath)
            if (fails) throw failure
          } catch (cause: unknown) {
            assert.equal(cause, failure)
            assert.equal(fails, true)
          } finally {
            await workspace.close()
          }
          await assert.rejects(stat(workspace.fullPath), { code: 'ENOENT' })
          assert.equal(await readFile(join(other.fullPath, 'retained.code'), 'utf8'), 'retained')
        }
      } finally {
        await other.close()
      }
      await assert.rejects(stat(other.fullPath), { code: 'ENOENT' })
    })
  }
}
