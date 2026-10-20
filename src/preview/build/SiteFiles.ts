import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'

/** @anchor preview-assets */
export class SiteFiles {
  private fileCount: number = 0
  private totalBytes: number = 0
  private maximumBytes: number = 0

  /** @anchor preview-assets */
  public constructor(private readonly outputFullPath: string) {}

  /** @anchor preview-assets */
  public async write(relativePath: string, contents: string): Promise<void> {
    const bytes: Buffer = Buffer.from(contents)
    await this.bytes(relativePath, bytes)
  }

  /** @anchor preview-assets */
  public async copy(relativePath: string, sourceFullPath: string): Promise<void> {
    const bytes: Buffer = await readFile(sourceFullPath)
    await this.bytes(relativePath, bytes)
  }

  /** @anchor preview-assets */
  public async bytes(relativePath: string, bytes: Uint8Array): Promise<void> {
    const fullPath: string = join(this.outputFullPath, relativePath)
    await mkdir(dirname(fullPath), { recursive: true })
    await writeFile(fullPath, bytes)
    this.fileCount += 1
    this.totalBytes += bytes.byteLength
    this.maximumBytes = Math.max(this.maximumBytes, bytes.byteLength)
  }

  /** @anchor preview-assets */
  public report(): void {
    process.stdout.write(
      `预览资源实际写入：${String(this.fileCount)} 文件，累计 ${String(this.totalBytes)} 字节，最大 ${String(this.maximumBytes)} 字节\n`,
    )
  }
}
