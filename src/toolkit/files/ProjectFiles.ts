import { Cache, Context, Duration, Effect, Exit, Option } from 'effect'
import { posix } from 'node:path'
import { PlatformFilesService, PlatformFiles } from './PlatformFiles.ts'
import type { FileMetadata } from './FileContracts.ts'

/**
 * @anchor workspace-file-cache
 * @anchor workspace-file-cache-inventory
 */
export class ProjectFilesService extends Context.Service<ProjectFilesService, ProjectFiles>()(
  'toolkit/ProjectFiles',
) {
  private constructor(service: never) {
    super(service)
  }
}

/**
 * @anchor workspace-file-cache
 * @anchor workspace-file-cache-inventory
 */
export class ProjectFiles {
  private constructor(
    private readonly platform: PlatformFiles,
    private readonly contents: Cache.Cache<FileMetadata, Uint8Array, unknown>,
  ) {}

  /**
   * @anchor workspace-file-cache
   * @anchor workspace-file-cache-inventory
   * @anchor workspace-computation-shortcuts
   */
  public static make(): Effect.Effect<ProjectFiles, unknown, PlatformFilesService> {
    return Effect.gen(function* (): Generator<
      Effect.Effect<unknown, unknown, PlatformFilesService>,
      ProjectFiles,
      never
    > {
      const platform: PlatformFiles = yield* PlatformFilesService
      const contents: Cache.Cache<FileMetadata, Uint8Array, unknown> = yield* Cache.makeWith(
        (key: FileMetadata): Effect.Effect<Uint8Array, unknown> => platform.read(key.relativePath),
        {
          capacity: 128,
          timeToLive: (exit: Exit.Exit<Uint8Array, unknown>): Duration.Duration =>
            Exit.isSuccess(exit) && exit.value.byteLength <= 1048576
              ? Duration.infinity
              : Duration.zero,
        },
      )
      return new ProjectFiles(platform, contents)
    })
  }

  /**
   * @anchor workspace-file-cache
   * @anchor workspace-file-cache-inventory
   * @anchor workspace-computation-shortcuts
   */
  public read(relativePath: string): Effect.Effect<Uint8Array, unknown> {
    return Effect.gen({ self: this }, function* (this: ProjectFiles): Generator<
      Effect.Effect<unknown, unknown>,
      Uint8Array,
      never
    > {
      const before: FileMetadata = yield* this.platform.metadata(relativePath)
      if (before.kind === 'directory')
        return yield* Effect.fail(new Error(`不是工程文件: ${relativePath}`))
      const bytes: Uint8Array =
        before.size <= 1048576n
          ? yield* Cache.get(this.contents, before)
          : yield* this.platform.read(relativePath)
      const after: FileMetadata = yield* this.platform.metadata(relativePath)
      if (before.revision !== after.revision) {
        yield* Cache.invalidate(this.contents, before)
        return yield* Effect.fail(new Error(`读取期间文件变化: ${relativePath}`))
      }
      return Uint8Array.from(bytes)
    })
  }

  /**
   * @anchor workspace-file-paths
   * @passthrough-exempt 文件服务直接暴露平台层元数据原语
   */
  public metadata(relativePath: string): Effect.Effect<FileMetadata, unknown> {
    return this.platform.metadata(relativePath)
  }

  /** @anchor workspace-file-paths */
  public directory(relativePath: string): Effect.Effect<readonly FileMetadata[], unknown> {
    return Effect.gen({ self: this }, function* (this: ProjectFiles): Generator<
      Effect.Effect<unknown, unknown>,
      readonly FileMetadata[],
      never
    > {
      const metadata: FileMetadata = yield* this.platform.metadata(relativePath)
      if (metadata.kind !== 'directory')
        return yield* Effect.fail(new Error(`不是工程目录: ${relativePath}`))
      const names: readonly string[] = yield* this.platform.directory(relativePath)
      const entries: FileMetadata[] = []
      for (const name of [...names].sort()) {
        entries.push(yield* this.platform.metadata(posix.join(relativePath, name)))
      }
      return entries
    })
  }

  /**
   * @anchor workspace-file-writes
   * @fallback 仅将原生 ENOENT 转为创建状态；权限及其他平台错误继续失败。
   * @fallback 同大小文件仍需实际字节比较，避免元数据相同掩盖内容差异。
   */
  public write(relativePath: string, bytes: Uint8Array): Effect.Effect<boolean, unknown> {
    return Effect.gen({ self: this }, function* (this: ProjectFiles): Generator<
      Effect.Effect<unknown, unknown>,
      boolean,
      never
    > {
      const content: Uint8Array = Uint8Array.from(bytes)
      const current: Option.Option<FileMetadata> = yield* Effect.catch(
        this.platform.metadata(relativePath).pipe(Effect.map(Option.some)),
        (cause: unknown): Effect.Effect<Option.Option<FileMetadata>, unknown> =>
          typeof cause === 'object' && cause !== null && 'code' in cause && cause.code === 'ENOENT'
            ? Effect.succeedNone
            : Effect.fail(cause),
      )
      if (Option.isNone(current)) {
        yield* this.platform.write(relativePath, content)
        return true
      }
      if (current.value.kind === 'directory')
        return yield* Effect.fail(new Error(`不能写入目录: ${relativePath}`))
      if (current.value.size === BigInt(content.byteLength)) {
        const previous: Uint8Array = yield* this.platform.read(relativePath)
        if (
          previous.byteLength === content.byteLength &&
          previous.every((byte: number, index: number): boolean => byte === content[index])
        )
          return false
      }
      yield* this.platform.write(relativePath, content)
      return true
    })
  }

  /** @anchor workspace-file-writes */
  public remove(relativePath: string): Effect.Effect<void, unknown> {
    if (relativePath === '.') return Effect.fail(new Error('不能删除工程根目录'))
    return this.platform.remove(relativePath)
  }

  /** @anchor workspace-file-writes */
  public makeDirectory(relativePath: string): Effect.Effect<void, unknown> {
    if (relativePath === '.') return Effect.fail(new Error('不能重复创建工程根目录'))
    return this.platform.makeDirectory(relativePath)
  }

  /** @anchor workspace-file-writes */
  public removeDirectory(relativePath: string): Effect.Effect<void, unknown> {
    if (relativePath === '.') return Effect.fail(new Error('不能删除工程根目录'))
    return this.platform.removeDirectory(relativePath)
  }
}
