import { spawn } from 'node:child_process'
import { Option } from 'effect'
import type { ChildProcess } from 'node:child_process'
import { Git } from './Git.ts'

/** @anchor process-boundary */
export interface HostLaunch {
  readonly file: string
  readonly args: readonly string[]
  readonly windowsHide: true
}

/** @anchor process-boundary */
export class HostProcess {
  private constructor() {}

  /** @anchor process-boundary */
  public static pnpm(platform: NodeJS.Platform, args: readonly string[]): HostLaunch {
    switch (platform) {
      case 'win32':
        return HostProcess.windows(args)
      case 'aix':
      case 'android':
      case 'cygwin':
      case 'darwin':
      case 'freebsd':
      case 'haiku':
      case 'linux':
      case 'netbsd':
      case 'openbsd':
      case 'sunos':
        return HostProcess.posix(args)
    }
  }

  /** @anchor process-boundary */
  public static run(
    launch: HostLaunch,
    cwd: string,
    environment: NodeJS.ProcessEnv = Git.cleanEnv(),
  ): Promise<number> {
    const child: ChildProcess = spawn(launch.file, [...launch.args], {
      cwd,
      stdio: 'inherit',
      windowsHide: launch.windowsHide,
      env: {
        ...Git.cleanEnv(),
        UV_PROJECT_ENVIRONMENT: environment.UV_PROJECT_ENVIRONMENT,
        UV_CACHE_DIR: environment.UV_CACHE_DIR,
        UV_PYTHON_INSTALL_DIR: environment.UV_PYTHON_INSTALL_DIR,
      },
    })
    return new Promise((resolveResult: (code: number) => void): void => {
      child.once('error', (): void => {
        resolveResult(HostProcess.status(Option.none()))
      })
      child.once('close', (code: number | null): void => {
        resolveResult(HostProcess.status(code === null ? Option.none() : Option.some(code)))
      })
    })
  }

  /** @anchor process-boundary */
  private static windows(args: readonly string[]): HostLaunch {
    return {
      file: 'cmd.exe',
      args: ['/d', '/s', '/c', 'pnpm.cmd', ...args],
      windowsHide: true,
    }
  }

  /** @anchor process-boundary */
  private static posix(args: readonly string[]): HostLaunch {
    return { file: 'pnpm', args: [...args], windowsHide: true }
  }

  /**
   * @anchor process-boundary
   * @fallback 子进程未能启动或没有退出码时按检查失败返回 1，不把缺失的退出码当成成功。
   */
  private static status(code: Option.Option<number>): number {
    return Option.getOrElse(code, (): number => 1)
  }
}
