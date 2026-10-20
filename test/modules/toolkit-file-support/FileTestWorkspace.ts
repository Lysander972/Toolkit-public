import fs from 'node:fs'
import { mkdir, mkdtemp, rename, rm } from 'node:fs/promises'
import { dirname, resolve, join } from 'node:path'
import { vi } from 'vitest'
import type { MockInstance } from 'vitest'
import { z } from 'zod'
import { Effect, Option } from 'effect'
import { NodeFileSystem } from '@effect/platform-node'
import writeFileAtomic from 'write-file-atomic'
import { PlatformFiles, PlatformFilesService } from '../../../src/toolkit/files/PlatformFiles.ts'
import { ProjectFiles } from '../../../src/toolkit/files/ProjectFiles.ts'

/** @anchor workspace-file-test-resources */
interface WriteCallback {
  (error: NodeJS.ErrnoException | null, written: number, buffer: Buffer): void
}

/** @anchor workspace-file-test-resources */
interface CloseCallback {
  (error: NodeJS.ErrnoException | null): void
}

/** @anchor workspace-file-test-resources */
export class FileTestWorkspace {
  private static readonly realWrite: typeof fs.write = fs.write
  private static readonly realClose: typeof fs.close = fs.close
  private static readonly active: Set<FileTestWorkspace> = new Set<FileTestWorkspace>()
  private static readonly writeArguments: z.ZodType<
    [number, Buffer, number, number, number, WriteCallback]
  > = z.tuple([
    z.number(),
    z.instanceof(Buffer),
    z.number(),
    z.number(),
    z.number(),
    z.custom<WriteCallback>((value: unknown): boolean => typeof value === 'function'),
  ])
  private static readonly closeArguments: z.ZodType<[number, CloseCallback]> = z.tuple([
    z.number(),
    z.custom<CloseCallback>((value: unknown): boolean => typeof value === 'function'),
  ])
  private static writeSpy: Option.Option<MockInstance<typeof fs.write>> = Option.none()
  private static closeSpy: Option.Option<MockInstance<typeof fs.close>> = Option.none()
  private static cumulativeBytes: number = 0
  private fileCount: number = 0
  private bytes: number = 0
  private maximumBytes: number = 0
  private readonly descriptorBytes: Map<number, number> = new Map<number, number>()

  private constructor(
    public readonly fullPath: string,
    public readonly files: ProjectFiles,
  ) {
    FileTestWorkspace.install()
    FileTestWorkspace.active.add(this)
  }

  /** @anchor workspace-file-test-resources */
  public static async make(): Promise<FileTestWorkspace> {
    await mkdir(resolve('_build/tests'), { recursive: true })
    const fullPath: string = await mkdtemp(resolve('_build/tests/files-'))
    try {
      const platform: PlatformFiles = await Effect.runPromise(
        PlatformFiles.make(fullPath).pipe(Effect.provide(NodeFileSystem.layer)),
      )
      const files: ProjectFiles = await Effect.runPromise(
        ProjectFiles.make().pipe(Effect.provideService(PlatformFilesService, platform)),
      )
      return new FileTestWorkspace(fullPath, files)
    } catch (cause: unknown) {
      await rm(fullPath, { recursive: true })
      throw cause
    }
  }

  /** @anchor workspace-file-test-resources */
  public async write(relativePath: string, content: string | Uint8Array): Promise<void> {
    const bytes: Buffer = Buffer.from(content)
    const stagingDirectory: string = await mkdtemp(join(dirname(this.fullPath), 'write-'))
    try {
      const stagingFullPath: string = join(stagingDirectory, 'payload')
      await writeFileAtomic(stagingFullPath, bytes)
      await rename(stagingFullPath, join(this.fullPath, relativePath))
    } finally {
      await rm(stagingDirectory, { recursive: true, force: true })
    }
  }

  /** @anchor workspace-file-test-resources */
  public writtenBytes(): number {
    return this.bytes
  }

  /** @anchor workspace-file-test-resources */
  public async close(): Promise<void> {
    FileTestWorkspace.active.delete(this)
    FileTestWorkspace.release()
    try {
      await rm(this.fullPath, { recursive: true })
    } finally {
      FileTestWorkspace.cumulativeBytes += this.bytes
      process.stdout.write(
        JSON.stringify({
          fileCount: this.fileCount,
          bytes: this.bytes,
          maximumBytes: this.maximumBytes,
          cumulativeBytes: FileTestWorkspace.cumulativeBytes,
        }) + '\n',
      )
    }
  }

  /** @anchor workspace-file-test-resources */
  private static install(): void {
    if (Option.isSome(FileTestWorkspace.writeSpy)) return
    FileTestWorkspace.closeSpy = Option.some(
      vi.spyOn(fs, 'close').mockImplementation((...parameters: readonly unknown[]): void => {
        const [descriptor, callback]: [number, CloseCallback] =
          FileTestWorkspace.closeArguments.parse(parameters)
        FileTestWorkspace.realClose(descriptor, (error: NodeJS.ErrnoException | null): void => {
          for (const workspace of FileTestWorkspace.active)
            workspace.descriptorBytes.delete(descriptor)
          callback(error)
        })
      }),
    )
    FileTestWorkspace.writeSpy = Option.some(
      vi.spyOn(fs, 'write').mockImplementation((...parameters: readonly unknown[]): void => {
        const [descriptor, buffer, offset, length, position, callback]: [
          number,
          Buffer,
          number,
          number,
          number,
          WriteCallback,
        ] = FileTestWorkspace.writeArguments.parse(parameters)
        FileTestWorkspace.realWrite(
          descriptor,
          buffer,
          offset,
          length,
          position,
          (error: NodeJS.ErrnoException | null, written: number, result: Buffer): void => {
            if (written > 0) FileTestWorkspace.recordWrite(descriptor, written)
            callback(error, written, result)
          },
        )
      }),
    )
  }

  /** @anchor workspace-file-test-resources */
  private static recordWrite(descriptor: number, written: number): void {
    for (const workspace of FileTestWorkspace.active) {
      const previous: number = workspace.descriptorBytes.has(descriptor)
        ? z.number().parse(workspace.descriptorBytes.get(descriptor))
        : 0
      workspace.record(written, previous === 0, previous + written)
      workspace.descriptorBytes.set(descriptor, previous + written)
    }
  }

  /** @anchor workspace-file-test-resources */
  private static release(): void {
    if (FileTestWorkspace.active.size > 0) return
    if (Option.isSome(FileTestWorkspace.writeSpy)) FileTestWorkspace.writeSpy.value.mockRestore()
    if (Option.isSome(FileTestWorkspace.closeSpy)) FileTestWorkspace.closeSpy.value.mockRestore()
    FileTestWorkspace.writeSpy = Option.none()
    FileTestWorkspace.closeSpy = Option.none()
  }

  /** @anchor workspace-file-test-resources */
  private record(bytes: number, first: boolean, total: number): void {
    if (first) this.fileCount += 1
    this.bytes += bytes
    this.maximumBytes = Math.max(this.maximumBytes, total)
  }
}
