import { isAbsolute, join, posix, relative, sep, win32 } from 'node:path'

/**
 * 工程根相对路径。拒绝绝对路径、反斜杠、空段和点段。
 * @anchor frontend-workspace-path
 */
export class WorkspacePath {
  private constructor() {}

  /**
   * 校验工程根相对路径。单独的 `.` 表示工程根。
   * @anchor frontend-workspace-path
   */
  public static requireRelative(input: string): string {
    WorkspacePath.rejectEmpty(input)
    WorkspacePath.rejectBackslash(input)
    WorkspacePath.rejectAbsolute(input)
    if (input === '.') {
      return input
    }
    WorkspacePath.rejectDotPrefix(input)
    WorkspacePath.rejectSegments(input)
    return input
  }

  /**
   * 把工程内绝对路径收成 `/` 分隔的相对路径。
   * @fallback Node path.relative 用空字符串表示工程根，按工程路径合同转换为 `.`。
   * @anchor frontend-workspace-path
   */
  public static toRelative(absoluteFullPath: string, workspaceRootFullPath: string): string {
    if (!isAbsolute(absoluteFullPath)) {
      throw new Error('仅接受绝对路径')
    }
    const relativePath: string = relative(workspaceRootFullPath, absoluteFullPath)
    if (relativePath === '..' || relativePath.startsWith('..' + sep) || isAbsolute(relativePath)) {
      throw new Error('路径不在工作区内')
    }
    if (relativePath === '') {
      return '.'
    }
    return WorkspacePath.requireRelative(relativePath.replaceAll('\\', '/'))
  }

  /**
   * 把已校验的相对路径拼成文件系统绝对路径，不再判断是否合法。
   * @anchor frontend-workspace-path
   */
  public static resolveFull(relativePath: string, workspaceRootFullPath: string): string {
    return join(workspaceRootFullPath, relativePath.replaceAll('/', sep))
  }

  private static rejectEmpty(input: string): void {
    if (input === '') {
      throw new Error('路径不能为空')
    }
  }

  private static rejectBackslash(input: string): void {
    if (input.includes('\\')) {
      throw new Error("路径分隔符必须使用 '/'")
    }
  }

  private static rejectAbsolute(input: string): void {
    if (posix.isAbsolute(input) || win32.isAbsolute(input) || win32.parse(input).root !== '') {
      throw new Error('不允许绝对路径')
    }
  }

  private static rejectDotPrefix(input: string): void {
    if (input.startsWith('./')) {
      throw new Error('路径不允许以 "./" 开头')
    }
  }

  private static rejectSegments(input: string): void {
    const segments: readonly string[] = input.split('/')
    for (const segment of segments) {
      if (segment.length === 0) {
        throw new Error("路径不允许出现空段或以 '/' 结尾")
      }
      if (segment === '.' || segment === '..') {
        throw new Error('路径不允许包含 "." 或 ".." 段')
      }
    }
  }
}
