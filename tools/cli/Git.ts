import { execFileSync, spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import type { SpawnSyncReturns } from 'node:child_process'
import { z } from 'zod'
import { join, resolve } from 'node:path'

/** @anchor review-propagation */
export class Git {
  /** @anchor review-propagation */
  public static cleanEnv(inheritContext: boolean = true): NodeJS.ProcessEnv {
    return {
      PATH: process.env.PATH,
      SystemRoot: process.env.SystemRoot,
      ProgramData: process.env.ProgramData,
      COMSPEC: process.env.COMSPEC,
      PATHEXT: process.env.PATHEXT,
      HOME: process.env.HOME,
      USERPROFILE: process.env.USERPROFILE,
      APPDATA: process.env.APPDATA,
      LOCALAPPDATA: process.env.LOCALAPPDATA,
      TEMP: process.env.TEMP,
      TMP: process.env.TMP,
      TSX_DISABLE_CACHE: '1',
      ...(inheritContext
        ? {
            GIT_DIR: process.env.GIT_DIR,
            GIT_COMMON_DIR: process.env.GIT_COMMON_DIR,
            GIT_WORK_TREE: process.env.GIT_WORK_TREE,
            GIT_INDEX_FILE: process.env.GIT_INDEX_FILE,
            GIT_PREFIX: process.env.GIT_PREFIX,
          }
        : {}),
    }
  }

  private constructor() {}

  /** @anchor process-boundary */
  public static repositoryRoot(): string {
    return Git.run(process.cwd(), ['rev-parse', '--show-toplevel'])
  }

  /** @anchor mutation-incremental */
  public static incrementalFile(repositoryFullPath: string): string {
    return join(
      Git.run(
        repositoryFullPath,
        ['rev-parse', '--path-format=absolute', '--git-common-dir'],
        false,
      ),
      'stryker-incremental.json',
    )
  }

  /**
   * @anchor process-boundary
   */
  public static run(
    repositoryFullPath: string,
    args: readonly string[],
    inheritContext: boolean = true,
  ): string {
    const output: string = execFileSync('git', ['-c', 'core.quotepath=false', ...args], {
      cwd: repositoryFullPath,
      env: Git.cleanEnv(inheritContext),
      encoding: 'utf8',
      maxBuffer: 16 * 1024 * 1024,
      windowsHide: true,
      timeout: 60000,
    })
    // eslint-disable-next-line no-restricted-properties -- Git 机器协议输出需去除命令行定界空白。
    return output.trim()
  }

  /** @anchor hook-boundary */
  public static succeeds(repositoryFullPath: string, args: readonly string[]): boolean {
    const result: SpawnSyncReturns<string> = spawnSync('git', args, {
      cwd: repositoryFullPath,
      env: Git.cleanEnv(),
      encoding: 'utf8',
      windowsHide: true,
      timeout: 60000,
    })
    if (result.error !== undefined) {
      throw result.error
    }
    return result.status === 0
  }

  /** @anchor review-ledger */
  public static indexFingerprint(repositoryFullPath: string): string {
    const output: Buffer = execFileSync('git', ['ls-files', '-s', '-z'], {
      cwd: repositoryFullPath,
      env: Git.cleanEnv(),
      maxBuffer: 64 * 1024 * 1024,
      windowsHide: true,
      timeout: 60000,
    })
    return createHash('sha256').update(output).digest('hex')
  }

  /** @anchor review-ledger */
  public static privatePath(repositoryFullPath: string, name: string): string {
    return Git.run(repositoryFullPath, ['rev-parse', '--path-format=absolute', '--git-path', name])
  }

  /** @anchor execution-snapshot */
  public static alternateIndex(repositoryFullPath: string): boolean {
    const gitDirectoryFullPath: string = Git.run(repositoryFullPath, [
      'rev-parse',
      '--absolute-git-dir',
    ])
    return (
      resolve(Git.privatePath(repositoryFullPath, 'index')) !==
      resolve(gitDirectoryFullPath, 'index')
    )
  }

  /**
   * 受检树文件**原文**唯一封装：不经 trim（`Git.run` 会剪掉行尾，用于文本内容会失真），
   * 已枚举对象必须可读取；缺席或 Git 失败直接报错，不伪造空文件。
   * @anchor review-notes-io
   */
  public static treeFile(
    repositoryFullPath: string,
    revision: string,
    relativePath: string,
  ): string {
    const result: SpawnSyncReturns<string> = spawnSync(
      'git',
      ['-C', repositoryFullPath, 'show', revision + ':' + relativePath],
      {
        encoding: 'utf8',
        env: Git.cleanEnv(),
        maxBuffer: 16 * 1024 * 1024,
        windowsHide: true,
        timeout: 60000,
      },
    )
    if (result.error !== undefined) {
      throw result.error
    }
    if (result.status !== 0) {
      throw new Error(`读取受检文件 ${relativePath} 失败: ${result.stderr}`)
    }
    return result.stdout
  }

  /**
   * @fallback 空对象集合不产生 cat-file 输入，返回空映射。
   * @anchor review-notes-io
   */
  public static blobs(
    repositoryFullPath: string,
    objects: readonly string[],
  ): ReadonlyMap<string, string> {
    const unique: string[] = [...new Set(objects)]
    const output: Buffer = execFileSync('git', ['cat-file', '--batch', '-Z'], {
      cwd: repositoryFullPath,
      env: Git.cleanEnv(),
      input: unique.length === 0 ? '' : unique.join('\0') + '\0',
      maxBuffer: 64 * 1024 * 1024,
      windowsHide: true,
      timeout: 60000,
    })
    const contents: Map<string, string> = new Map()
    let offset: number = 0
    for (const object of unique) {
      const headerEnd: number = output.indexOf(0, offset)
      const [, , size]: [string, 'blob', number] = z
        .tuple([z.string().length(40), z.literal('blob'), z.coerce.number().int().nonnegative()])
        .parse(output.toString('utf8', offset, headerEnd).split(' '))
      const end: number = headerEnd + 1 + size
      if (headerEnd < offset || output[end] !== 0) {
        throw new Error('git cat-file 输出不符合 batch 协议: ' + object)
      }
      contents.set(object, output.toString('utf8', headerEnd + 1, end))
      offset = end + 1
    }
    if (offset !== output.length) throw new Error('git cat-file 输出包含未消费内容')
    return contents
  }
}
