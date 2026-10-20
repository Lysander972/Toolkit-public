import { posix, win32 } from 'node:path'

/** @anchor workspace-file-paths */
export class WorkspacePaths {
  private constructor() {}

  /** @anchor workspace-file-paths */
  public static require(relativePath: string): string {
    if (relativePath === '.') return relativePath
    if (
      relativePath.length === 0 ||
      posix.isAbsolute(relativePath) ||
      win32.isAbsolute(relativePath) ||
      relativePath.includes('\\') ||
      relativePath.includes(':') ||
      relativePath.includes('\0') ||
      relativePath
        .split('/')
        .some((segment: string): boolean => segment === '' || segment === '.' || segment === '..')
    ) {
      throw new Error(`非法工程相对路径: ${relativePath}`)
    }
    return relativePath
  }
}
