import { posix, sep } from 'node:path'
import { watch } from 'chokidar'
import type { FSWatcher } from 'chokidar'
import { Cause, Context, Effect, Queue, Scope } from 'effect'
import { WorkspacePaths } from '@toolkit/project-model/paths'

/** @anchor entity-watch */
export interface ProjectFileChanges {
  readonly changedRelativePaths: readonly string[]
  readonly directoryRelativePaths: readonly string[]
}

/** @anchor entity-watch */
export class ProjectWatcherService extends Context.Service<ProjectWatcherService, ProjectWatcher>()(
  'toolkit/ProjectWatcher',
) {
  private constructor(service: never) {
    super(service)
  }
}

/** @anchor entity-watch */
export class ProjectWatcher {
  private readonly watcher: FSWatcher
  private readonly pending: Set<string> = new Set<string>()
  private readonly ready: Promise<void>

  private constructor(
    rootFullPath: string,
    private readonly wake: Queue.Queue<boolean, unknown>,
  ) {
    this.watcher = watch('.', {
      cwd: rootFullPath,
      ignoreInitial: true,
      usePolling: false,
      awaitWriteFinish: false,
      atomic: true,
    })
    this.ready = new Promise<void>(
      (resolve: () => void, reject: (cause: unknown) => void): void => {
        this.watcher.once('ready', resolve)
        this.watcher.once('error', reject)
      },
    )
    this.watcher.on('all', (_event: string, sourceRelativePath: string): void => {
      const relativePath: string = sourceRelativePath.split(sep).join('/')
      WorkspacePaths.require(relativePath)
      this.pending.add(relativePath)
      Queue.offerUnsafe(this.wake, true)
    })
    this.watcher.on('error', (cause: unknown): void => {
      Queue.failCauseUnsafe(this.wake, Cause.fail(cause))
    })
  }

  /** @anchor entity-watch */
  public static make(rootFullPath: string): Effect.Effect<ProjectWatcher, unknown, Scope.Scope> {
    return Effect.gen(function* (): Generator<
      Effect.Effect<unknown, unknown, Scope.Scope>,
      ProjectWatcher,
      never
    > {
      const wake: Queue.Queue<boolean, unknown> = yield* Queue.make<boolean, unknown>({
        capacity: 1,
        strategy: 'sliding',
      })
      const watcher: ProjectWatcher = yield* Effect.acquireRelease(
        Effect.sync((): ProjectWatcher => new ProjectWatcher(rootFullPath, wake)),
        (resource: ProjectWatcher): Effect.Effect<void> =>
          Effect.tryPromise({
            try: (): Promise<void> => resource.close(),
            catch: (cause: unknown): unknown => cause,
          }).pipe(Effect.orDie),
      )
      yield* Effect.tryPromise({
        try: (): Promise<void> => watcher.ready,
        catch: (cause: unknown): unknown => cause,
      })
      return watcher
    })
  }

  /** @anchor entity-watch */
  public take(): Effect.Effect<ProjectFileChanges, unknown> {
    return Effect.map(Queue.take(this.wake), (): ProjectFileChanges => {
      const changedRelativePaths: readonly string[] = [...this.pending]
      this.pending.clear()
      return {
        changedRelativePaths,
        directoryRelativePaths: [
          ...new Set(
            changedRelativePaths.map((relativePath: string): string => posix.dirname(relativePath)),
          ),
        ],
      }
    })
  }

  /** @anchor entity-watch */
  private async close(): Promise<void> {
    await this.watcher.close()
    this.pending.clear()
    Queue.shutdownUnsafe(this.wake)
  }
}
