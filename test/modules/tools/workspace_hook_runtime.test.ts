import assert from 'node:assert/strict'
import { execFile } from 'node:child_process'
import type { ExecFileException } from 'node:child_process'
import { createHash } from 'node:crypto'
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
  watch,
  writeFileSync,
} from 'node:fs'
import type { FSWatcher } from 'node:fs'
import { join, resolve } from 'node:path'
import { test } from 'vitest'
import type { TestContext } from 'vitest'
import { Option } from 'effect'
import { z } from 'zod'
import { Git } from '../../../tools/cli/Git.ts'
import { HostProcess } from '../../../tools/cli/HostProcess.ts'
import type { HostLaunch } from '../../../tools/cli/HostProcess.ts'
import { GitFixture } from '../../../tools/testing/GitFixture.ts'
import type { GitResult as ReviewGitResult } from '../../../tools/testing/GitFixture.ts'

/** @anchor workspace-hook-runtime */
interface PackageManagerBoundary {
  readonly packageManager: string
}

/** @anchor workspace-hook-runtime */
class WorkspaceHookFixture {
  private readonly observed: Map<string, string> = new Map()
  private readonly watcher: FSWatcher
  private files: number = 0
  private bytes: number = 0
  private maximum: number = 0

  /** @anchor workspace-hook-runtime */
  public constructor(
    private readonly repositoryFullPath: string,
    private readonly signal: AbortSignal,
  ) {
    this.watcher = watch(
      repositoryFullPath,
      { recursive: true },
      (_eventType: string, filename: string | Buffer | null): void => {
        if (filename !== null) this.observeEvent(join(repositoryFullPath, filename.toString()))
      },
    )
  }

  /** @anchor workspace-hook-runtime */
  public write(relativePath: string, text: string): void {
    const fileFullPath: string = join(this.repositoryFullPath, relativePath)
    writeFileSync(fileFullPath, text)
    this.observed.set(
      fileFullPath,
      createHash('sha256').update(readFileSync(fileFullPath)).digest('hex'),
    )
    this.files += 1
    this.bytes += Buffer.byteLength(text)
    this.maximum = Math.max(this.maximum, Buffer.byteLength(text))
    this.measure()
  }

  /** @anchor workspace-hook-runtime */
  public manifest(dependencyRelativePath: string): void {
    const schema: z.ZodType<PackageManagerBoundary> = z.object({ packageManager: z.string() })
    const root: PackageManagerBoundary = schema.parse(
      JSON.parse(readFileSync('package.json', 'utf8')),
    )
    this.write(
      'package.json',
      JSON.stringify({
        name: 'toolkit-workspace-hook-fixture',
        private: true,
        type: 'module',
        packageManager: root.packageManager,
        scripts: {
          prepare: 'pnpm workspace:install',
          'workspace:install':
            'node --import=tsx "' +
            resolve('tools/cli/main.ts').replaceAll('\\', '/') +
            '" install',
          'check:workspace':
            'node --import=tsx "' +
            resolve('tools/cli/main.ts').replaceAll('\\', '/') +
            '" workspace',
        },
        dependencies: { 'hook-dependency': 'link:' + dependencyRelativePath },
      }),
    )
  }

  /** @anchor workspace-hook-runtime */
  public async pnpm(args: readonly string[]): Promise<ReviewGitResult> {
    const launch: HostLaunch = HostProcess.pnpm(process.platform, args)
    return await this.execute(launch.file, launch.args)
  }

  /** @anchor workspace-hook-runtime */
  public async hook(name: string): Promise<ReviewGitResult> {
    const head: string = Git.run(this.repositoryFullPath, ['rev-parse', 'HEAD'])
    const args: readonly string[] = name === 'post-checkout' ? [head, head, '1'] : ['0']
    return await this.execute('git', [
      '-c',
      'core.hooksPath=tools/git-hooks',
      'hook',
      'run',
      name,
      '--',
      ...args,
    ])
  }

  /** @anchor workspace-hook-runtime */
  private async execute(file: string, args: readonly string[]): Promise<ReviewGitResult> {
    const started: number = performance.now()
    const result: ReviewGitResult = await new Promise<ReviewGitResult>(
      (completed: (result: ReviewGitResult) => void, failed: (error: Error) => void): void => {
        execFile(
          file,
          [...args],
          {
            cwd: this.repositoryFullPath,
            env: Git.cleanEnv(),
            encoding: 'utf8',
            windowsHide: true,
            signal: this.signal,
          },
          (error: ExecFileException | null, stdout: string, stderr: string): void => {
            if (error === null) completed({ status: 0, stdout, stderr })
            else if (typeof error.code === 'number')
              completed({ status: error.code, stdout, stderr })
            else failed(new Error(error.message, { cause: error }))
          },
        )
      },
    )
    this.measure()
    process.stdout.write(
      JSON.stringify({ command: [file, ...args], milliseconds: performance.now() - started }) +
        '\n',
    )
    return result
  }

  /** @anchor workspace-hook-runtime */
  public measure(): void {
    for (const entry of readdirSync(this.repositoryFullPath, {
      withFileTypes: true,
      recursive: true,
    })) {
      if (!entry.isFile()) continue
      this.observeEvent(join(entry.parentPath, entry.name))
    }
  }

  /** @anchor workspace-hook-runtime */
  public async report(): Promise<void> {
    await new Promise<void>((completed: () => void): void => {
      setImmediate(completed)
    })
    this.measure()
    process.stdout.write(
      'workspace hook 写入统计（含覆盖与删除前文件）：文件数=' +
        String(this.files) +
        '，总字节=' +
        String(this.bytes) +
        '，最大文件=' +
        String(this.maximum) +
        '；累计写入=' +
        String(this.bytes) +
        '\n',
    )
  }

  /** @anchor workspace-hook-runtime */
  public close(): void {
    this.watcher.close()
  }

  /** @anchor workspace-hook-runtime */
  private observeEvent(fileFullPath: string): void {
    try {
      if (!existsSync(fileFullPath)) return
      const statistics: ReturnType<typeof statSync> = statSync(fileFullPath)
      if (!statistics.isFile()) return
      const identity: string = createHash('sha256').update(readFileSync(fileFullPath)).digest('hex')
      const previous: Option.Option<string> = Option.fromNullishOr(this.observed.get(fileFullPath))
      if (Option.isSome(previous) && previous.value === identity) return
      this.observed.set(fileFullPath, identity)
      this.files += 1
      this.bytes += statistics.size
      this.maximum = Math.max(this.maximum, statistics.size)
    } catch (error: unknown) {
      if (error instanceof Error && 'code' in error && error.code === 'ENOENT') return
      throw error
    }
  }
}

/** @anchor workspace-hook-runtime */
export class WorkspaceHookRuntimeTest {
  private constructor() {}

  static {
    test('生产 workspace hooks 自动确立依赖环境并保留 Git 终态', async ({
      signal,
    }: TestContext): Promise<void> => {
      mkdirSync(resolve('_build/requirements/tests'), { recursive: true })
      const repositoryFullPath: string = mkdtempSync(resolve('_build/requirements/tests/hooks-'))
      const fixture: WorkspaceHookFixture = new WorkspaceHookFixture(repositoryFullPath, signal)
      try {
        fixture.measure()
        GitFixture.create(repositoryFullPath)
        const dependencies: readonly (readonly [string, string])[] = [
          ['dependency-first', '1.0.0'],
          ['dependency-second', '2.0.0'],
        ]
        for (const [name, version] of dependencies) {
          mkdirSync(join(repositoryFullPath, name))
          fixture.write(
            name + '/package.json',
            JSON.stringify({ name: 'hook-dependency', version }),
          )
        }
        fixture.write('pnpm-workspace.yaml', readFileSync('pnpm-workspace.yaml', 'utf8'))
        fixture.manifest('dependency-first')
        mkdirSync(join(repositoryFullPath, 'tools/git-hooks'), { recursive: true })
        fixture.write(
          'tools/git-hooks/post-checkout',
          readFileSync('tools/git-hooks/post-checkout', 'utf8'),
        )
        fixture.write(
          'tools/git-hooks/post-merge',
          readFileSync('tools/git-hooks/post-merge', 'utf8'),
        )
        chmodSync(join(repositoryFullPath, 'tools/git-hooks/post-checkout'), 0o755)
        chmodSync(join(repositoryFullPath, 'tools/git-hooks/post-merge'), 0o755)
        const installed: ReviewGitResult = await fixture.pnpm([
          'install',
          '--no-frozen-lockfile',
          '--offline',
          '--ignore-scripts',
        ])
        assert.equal(installed.status, 0, installed.stdout + installed.stderr)
        const head: string = Git.run(repositoryFullPath, ['rev-parse', 'HEAD'])
        const index: string = Git.indexFingerprint(repositoryFullPath)
        for (const name of ['post-checkout', 'post-merge']) {
          if (name === 'post-checkout')
            rmSync(join(repositoryFullPath, 'node_modules'), { recursive: true })
          const result: ReviewGitResult = await fixture.hook(name)
          assert.equal(result.status, 0, result.stdout + result.stderr)
        }
        assert.equal(
          Git.run(repositoryFullPath, ['config', '--local', '--get', 'core.hooksPath']),
          'tools/git-hooks',
        )
        assert.equal(
          readFileSync(
            join(repositoryFullPath, 'node_modules/hook-dependency/package.json'),
            'utf8',
          ),
          readFileSync(join(repositoryFullPath, 'dependency-first/package.json'), 'utf8'),
        )
        fixture.manifest('dependency-second')
        const locked: ReviewGitResult = await fixture.pnpm([
          'install',
          '--lockfile-only',
          '--no-frozen-lockfile',
          '--offline',
          '--ignore-scripts',
        ])
        assert.equal(locked.status, 0, locked.stdout + locked.stderr)
        const wantedLock: Buffer = readFileSync(join(repositoryFullPath, 'pnpm-lock.yaml'))
        const manifest: Buffer = readFileSync(join(repositoryFullPath, 'package.json'))
        for (const name of ['post-checkout', 'post-merge']) {
          const recovered: ReviewGitResult = await fixture.hook(name)
          assert.equal(recovered.status, 0, recovered.stdout + recovered.stderr)
          assert.equal(Git.run(repositoryFullPath, ['rev-parse', 'HEAD']), head)
          assert.equal(Git.indexFingerprint(repositoryFullPath), index)
          assert.deepEqual(readFileSync(join(repositoryFullPath, 'package.json')), manifest)
          assert.deepEqual(readFileSync(join(repositoryFullPath, 'pnpm-lock.yaml')), wantedLock)
        }
        const modulesFullPath: string = join(repositoryFullPath, 'node_modules/.modules.yaml')
        const modulesModified: number = statSync(modulesFullPath).mtimeMs
        const repeated: ReviewGitResult = await fixture.pnpm(['check:workspace'])
        assert.equal(repeated.status, 0, repeated.stdout + repeated.stderr)
        assert.equal(statSync(modulesFullPath).mtimeMs, modulesModified)
        assert.equal(
          readFileSync(
            join(repositoryFullPath, 'node_modules/hook-dependency/package.json'),
            'utf8',
          ),
          readFileSync(join(repositoryFullPath, 'dependency-second/package.json'), 'utf8'),
        )
        fixture.manifest('dependency-first')
        for (const name of ['post-checkout', 'post-merge']) {
          const rejected: ReviewGitResult = await fixture.hook(name)
          assert.notEqual(rejected.status, 0, rejected.stdout + rejected.stderr)
          const diagnostic: string = rejected.stdout + rejected.stderr
          assert.ok(diagnostic.includes('ERR_PNPM_OUTDATED_LOCKFILE'), diagnostic)
          assert.ok(!diagnostic.includes('node --import=tsx'), diagnostic)
          assert.deepEqual(readFileSync(join(repositoryFullPath, 'pnpm-lock.yaml')), wantedLock)
          assert.equal(Git.run(repositoryFullPath, ['rev-parse', 'HEAD']), head)
          assert.equal(Git.indexFingerprint(repositoryFullPath), index)
        }
      } finally {
        try {
          await fixture.report()
        } finally {
          fixture.close()
          rmSync(repositoryFullPath, { recursive: true })
        }
      }
    })
  }
}
