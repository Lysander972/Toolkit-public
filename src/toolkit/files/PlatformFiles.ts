import { stat, rmdir, unlink } from 'node:fs/promises'
import type { BigIntStats } from 'node:fs'
import { basename, dirname, isAbsolute, join, relative, resolve, sep } from 'node:path'
import { Context, Effect, FileSystem, PlatformError } from 'effect'
import writeFileAtomic from 'write-file-atomic'
import { WorkspacePaths } from '@toolkit/project-model/paths'
import type { EntityFileKind } from '@toolkit/project-model/entities'
import type { FileMetadata } from './FileContracts.ts'

/** @anchor workspace-file-paths */
export class PlatformFilesService extends Context.Service<PlatformFilesService, PlatformFiles>()(
  'toolkit/PlatformFiles',
) {
  private constructor(service: never) {
    super(service)
  }
}

/** @anchor workspace-file-paths */
export class PlatformFiles {
  private constructor(
    private readonly rootFullPath: string,
    private readonly fs: FileSystem.FileSystem,
  ) {}

  /** @anchor workspace-file-paths */
  public static make(
    rootFullPath: string,
  ): Effect.Effect<PlatformFiles, unknown, FileSystem.FileSystem> {
    return Effect.gen(function* (): Generator<
      Effect.Effect<unknown, unknown, FileSystem.FileSystem>,
      PlatformFiles,
      never
    > {
      const fs: FileSystem.FileSystem = yield* FileSystem.FileSystem
      const canonicalFullPath: string = yield* fs.realPath(rootFullPath)
      return new PlatformFiles(canonicalFullPath, fs)
    })
  }

  /** @anchor workspace-file-paths */
  public metadata(relativePath: string): Effect.Effect<FileMetadata, unknown> {
    return Effect.gen({ self: this }, function* (this: PlatformFiles): Generator<
      Effect.Effect<unknown, unknown>,
      FileMetadata,
      never
    > {
      const fullPath: string = yield* this.fullPath(relativePath)
      const info: BigIntStats = yield* Effect.tryPromise({
        try: (): Promise<BigIntStats> => stat(fullPath, { bigint: true }),
        catch: (cause: unknown): unknown => cause,
      })
      const kind: EntityFileKind = info.isFile()
        ? 'file'
        : info.isDirectory()
          ? 'directory'
          : yield* Effect.fail(new Error(`不支持的工程文件类型: ${relativePath}`))
      return {
        relativePath,
        kind,
        revision: `${info.dev.toString()}:${info.ino.toString()}:${info.mtimeNs.toString()}:${info.ctimeNs.toString()}:${info.size.toString()}`,
        size: info.size,
      }
    })
  }

  /** @anchor workspace-file-cache */
  public read(relativePath: string): Effect.Effect<Uint8Array, unknown> {
    return Effect.flatMap(
      this.fullPath(relativePath),
      (fullPath: string): Effect.Effect<Uint8Array, unknown> => this.fs.readFile(fullPath),
    )
  }

  /** @anchor workspace-file-paths */
  public directory(relativePath: string): Effect.Effect<readonly string[], unknown> {
    return Effect.flatMap(
      this.fullPath(relativePath),
      (fullPath: string): Effect.Effect<readonly string[], unknown> =>
        this.fs.readDirectory(fullPath),
    )
  }

  /** @anchor workspace-file-writes */
  public write(relativePath: string, bytes: Uint8Array): Effect.Effect<void, unknown> {
    return Effect.flatMap(
      this.fullPath(relativePath),
      (fullPath: string): Effect.Effect<void, unknown> =>
        Effect.tryPromise({
          try: (): Promise<void> => writeFileAtomic(fullPath, Buffer.from(bytes)),
          catch: (cause: unknown): unknown => cause,
        }),
    )
  }

  /** @anchor workspace-file-writes */
  public remove(relativePath: string): Effect.Effect<void, unknown> {
    return Effect.flatMap(
      this.fullPath(relativePath),
      (fullPath: string): Effect.Effect<void, unknown> =>
        Effect.tryPromise({
          try: (): Promise<void> => unlink(fullPath),
          catch: (cause: unknown): unknown => cause,
        }),
    )
  }

  /** @anchor workspace-file-writes */
  public makeDirectory(relativePath: string): Effect.Effect<void, unknown> {
    return Effect.flatMap(
      this.fullPath(relativePath),
      (fullPath: string): Effect.Effect<void, unknown> => this.fs.makeDirectory(fullPath),
    )
  }

  /** @anchor workspace-file-writes */
  public removeDirectory(relativePath: string): Effect.Effect<void, unknown> {
    return Effect.flatMap(
      this.fullPath(relativePath),
      (fullPath: string): Effect.Effect<void, unknown> =>
        Effect.tryPromise({
          try: (): Promise<void> => rmdir(fullPath),
          catch: (cause: unknown): unknown => cause,
        }),
    )
  }

  /**
   * @fallback 目标不存在时解析父目录，以校验新建路径仍位于工程根内。
   */
  private fullPath(relativePath: string): Effect.Effect<string, unknown> {
    return Effect.gen({ self: this }, function* (this: PlatformFiles): Generator<
      Effect.Effect<unknown, unknown>,
      string,
      never
    > {
      WorkspacePaths.require(relativePath)
      const fullPath: string = resolve(this.rootFullPath, relativePath)
      const canonicalFullPath: string = yield* Effect.catch(
        this.fs.realPath(fullPath),
        (cause: PlatformError.PlatformError): Effect.Effect<string, PlatformError.PlatformError> =>
          cause.reason._tag === 'NotFound'
            ? Effect.map(this.fs.realPath(dirname(fullPath)), (parentFullPath: string): string =>
                join(parentFullPath, basename(fullPath)),
              )
            : Effect.fail(cause),
      )
      const outsideRelativePath: string = relative(this.rootFullPath, canonicalFullPath)
      if (
        outsideRelativePath === '..' ||
        outsideRelativePath.startsWith(`..${sep}`) ||
        isAbsolute(outsideRelativePath)
      )
        return yield* Effect.fail(new Error(`路径不在工程根内: ${relativePath}`))
      return canonicalFullPath
    })
  }
}
