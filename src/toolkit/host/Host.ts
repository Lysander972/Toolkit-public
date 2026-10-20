import { Context, Effect, Fiber, Layer, ManagedRuntime, Option, Scope } from 'effect'
import type { Logger } from 'pino'
import { NodeFileSystem } from '@effect/platform-node'
import { PlatformFiles, PlatformFilesService } from '../files/PlatformFiles.ts'
import { ProjectFiles, ProjectFilesService } from '../files/ProjectFiles.ts'
import { BackendLog } from './BackendLog.ts'
import { ProjectWatcher, ProjectWatcherService } from '../files/ProjectWatcher.ts'
import { ProjectEntities, ProjectEntitiesService } from '../entities/ProjectEntities.ts'
import { WorkspaceService } from './WorkspaceServices.ts'
import type { WorkspaceModule, WorkspaceStatus } from './WorkspaceServices.ts'
import type { OpenCanvasPreviewRequest, CanvasPreviewDisplay } from './WorkspaceUpdates.ts'
import type { ViewMode } from '../views/ProjectViews.ts'
import type { TsViewEntity, HtmlViewEntity } from '../entities/ToolkitEntity.ts'
import type { AssetTaskCommand, AssetTaskResult } from '../assets/AssetTaskContracts.ts'
import {
  SubmitAssetTask,
  ReadAssetTaskStatus,
  ReadAssetTaskResult,
  CancelAssetTask,
  DiscardAssetTask,
  RenderAssetSlice,
  ReviewAssetTask,
  ConfirmAssetTask,
} from '../assets/AssetTaskContracts.ts'
import type { LanguageQueries } from '../language/LanguageQueries.ts'
import type { Diagnostics } from '../diagnostics/Diagnostics.ts'
import type { Editing } from '../editing/Editing.ts'
import type { CanvasPreview } from '../preview/CanvasPreview.ts'

/** @anchor backend-container-stack */
export interface WorkspaceStatusRequest {
  readonly kind: 'workspace-status'
}

/** @anchor backend-container-stack */
export class SnapshotContext {
  private active: boolean = true

  private constructor() {}

  /** @anchor backend-container-stack */
  public static make(): Effect.Effect<SnapshotContext, never, Scope.Scope> {
    return Effect.acquireRelease(
      Effect.sync((): SnapshotContext => new SnapshotContext()),
      (snapshot: SnapshotContext): Effect.Effect<void> =>
        Effect.sync((): void => {
          snapshot.release()
        }),
    )
  }

  /** @anchor backend-container-stack */
  public isOpen(): boolean {
    return this.active
  }

  private release(): void {
    this.active = false
  }
}

/** @anchor backend-container-stack */
export class BackendRequestContext {
  private active: boolean = true

  private constructor() {}

  /** @anchor backend-container-stack */
  public static make(): Effect.Effect<BackendRequestContext, never, Scope.Scope> {
    return Effect.acquireRelease(
      Effect.sync((): BackendRequestContext => new BackendRequestContext()),
      (request: BackendRequestContext): Effect.Effect<void> =>
        Effect.sync((): void => {
          request.release()
        }),
    )
  }

  /** @anchor backend-container-stack */
  public isOpen(): boolean {
    return this.active
  }

  private release(): void {
    this.active = false
  }
}

/** @anchor backend-container-stack */
class SnapshotService extends Context.Service<SnapshotService, SnapshotContext>()(
  'toolkit/Snapshot',
) {
  private constructor(service: never) {
    super(service)
  }
}

/** @anchor backend-container-stack */
class BackendRequestService extends Context.Service<BackendRequestService, BackendRequestContext>()(
  'toolkit/BackendRequest',
) {
  private constructor(service: never) {
    super(service)
  }
}

/** @anchor host-lifecycle */
export class Host {
  private closed: boolean = false

  private constructor(
    private readonly runtime: ManagedRuntime.ManagedRuntime<WorkspaceService, unknown>,
    private readonly logger: Logger,
  ) {}

  /**
   * @anchor host-lifecycle
   * @anchor workspace-view-mode-startup
   * @anchor workspace-cold-start
   */
  public static async open(
    workspaceFullPath: string,
    viewMode: ViewMode,
    logger: Logger,
    execution: Parameters<typeof WorkspaceService.layer>[3] = Option.none(),
  ): Promise<Host> {
    const platform: Layer.Layer<PlatformFilesService, unknown> = Layer.effect(
      PlatformFilesService,
      PlatformFiles.make(workspaceFullPath),
    ).pipe(Layer.provide(NodeFileSystem.layer))
    const files: Layer.Layer<ProjectFilesService, unknown> = Layer.effect(
      ProjectFilesService,
      ProjectFiles.make(),
    ).pipe(Layer.provide(platform))
    const watcher: Layer.Layer<ProjectWatcherService, unknown> = Layer.effect(
      ProjectWatcherService,
      ProjectWatcher.make(workspaceFullPath),
    )
    const entities: Layer.Layer<ProjectEntitiesService, unknown> = Layer.effect(
      ProjectEntitiesService,
      Effect.andThen(
        ProjectWatcherService,
        Effect.flatMap(
          ProjectFilesService,
          (projectFiles: ProjectFiles): Effect.Effect<ProjectEntities, unknown, Scope.Scope> =>
            ProjectEntities.make(projectFiles),
        ),
      ),
    ).pipe(Layer.provide(Layer.merge(files, watcher)))
    const workspace: Layer.Layer<WorkspaceService, unknown> = WorkspaceService.layer(
      viewMode,
      entities,
      watcher,
      execution,
    )
    const hostLogger: Logger = logger.child({ component: 'host' })
    const host: Host = new Host(
      ManagedRuntime.make(
        Layer.merge(workspace, BackendLog.layer(hostLogger.child({ component: 'effect' }))),
      ),
      hostLogger,
    )
    try {
      await host.status()
      await host.runtime.runPromise(
        Effect.flatMap(WorkspaceService, (service: WorkspaceModule): Effect.Effect<void> =>
          Effect.asVoid(
            Effect.forkIn(
              service.updates.watch(service.watcher, service.entities),
              host.runtime.scope,
            ),
          ),
        ),
      )
      hostLogger.info({ viewMode }, 'Host 已打开')
      return host
    } catch (cause: unknown) {
      hostLogger.error({ err: cause }, 'Host 打开失败')
      await host.close()
      throw cause
    }
  }

  /** @anchor host-lifecycle */
  public async status(): Promise<WorkspaceStatus> {
    this.assertOpen()
    return this.execute(
      Effect.flatMap(
        WorkspaceService,
        (workspace: WorkspaceModule): Effect.Effect<WorkspaceStatus, unknown> => workspace.status(),
      ),
    )
  }

  /** @anchor host-view-access */
  public async readTs(deviceId: string): Promise<TsViewEntity> {
    this.assertOpen()
    return this.execute(
      Effect.flatMap(
        WorkspaceService,
        (workspace: WorkspaceModule): Effect.Effect<TsViewEntity, unknown> =>
          workspace.views.readTs(deviceId),
      ),
    )
  }

  /** @anchor host-view-access */
  public async readHtml(canvasId: string): Promise<HtmlViewEntity> {
    this.assertOpen()
    return this.execute(
      Effect.flatMap(
        WorkspaceService,
        (workspace: WorkspaceModule): Effect.Effect<HtmlViewEntity, unknown> =>
          workspace.views.readHtml(canvasId),
      ),
    )
  }

  /** @anchor backend-container-stack */
  public async runInRequest(
    _request: WorkspaceStatusRequest,
    signal: AbortSignal,
  ): Promise<WorkspaceStatus> {
    this.assertOpen()
    const layer: Layer.Layer<BackendRequestService> = Layer.effect(
      BackendRequestService,
      BackendRequestContext.make(),
    )
    return this.execute(
      Effect.andThen(BackendRequestService, WorkspaceService).pipe(
        Effect.flatMap((workspace: WorkspaceModule): Effect.Effect<WorkspaceStatus, unknown> =>
          workspace.status(),
        ),
        Effect.provide(layer),
      ),
      Option.some(signal),
    )
  }

  /** @anchor backend-container-stack */
  public async withSnapshot(
    _request: WorkspaceStatusRequest,
    signal: AbortSignal,
  ): Promise<WorkspaceStatus> {
    this.assertOpen()
    const layer: Layer.Layer<SnapshotService> = Layer.effect(
      SnapshotService,
      SnapshotContext.make(),
    )
    return this.execute(
      Effect.andThen(SnapshotService, WorkspaceService).pipe(
        Effect.flatMap((workspace: WorkspaceModule): Effect.Effect<WorkspaceStatus, unknown> =>
          workspace.status(),
        ),
        Effect.provide(layer),
      ),
      Option.some(signal),
    )
  }

  /** @anchor host-language-dispatch */
  public async query(
    request: Parameters<LanguageQueries['query']>[0],
  ): Promise<Effect.Success<ReturnType<LanguageQueries['query']>>> {
    this.assertOpen()
    return this.execute(
      Effect.flatMap(
        WorkspaceService,
        (workspace: WorkspaceModule): ReturnType<LanguageQueries['query']> =>
          workspace.language.query(request),
      ),
      Option.some(request.signal),
    )
  }

  /** @anchor host-asset-dispatch */
  public async assetTask(command: AssetTaskCommand, signal: AbortSignal): Promise<AssetTaskResult> {
    this.assertOpen()
    return this.execute(
      Effect.flatMap(
        WorkspaceService,
        (workspace: WorkspaceModule): Effect.Effect<AssetTaskResult, unknown> => {
          if (command instanceof SubmitAssetTask) return workspace.assets.submit(command)
          if (command instanceof ReadAssetTaskStatus) return workspace.assets.status(command)
          if (command instanceof ReadAssetTaskResult) return workspace.assets.result(command)
          if (command instanceof CancelAssetTask) return workspace.assets.cancel(command)
          if (command instanceof DiscardAssetTask) return workspace.assets.discard(command)
          if (command instanceof RenderAssetSlice) return workspace.assets.renderSlice(command)
          if (command instanceof ReviewAssetTask) return workspace.assets.review(command)
          if (command instanceof ConfirmAssetTask) return workspace.assets.confirm(command)
          return Effect.fail(new Error('未知资产任务命令'))
        },
      ),
      Option.some(signal),
    )
  }

  /** @anchor host-diagnostics-dispatch */
  public async inspect(
    request: Parameters<Diagnostics['inspect']>[0],
    signal: AbortSignal,
  ): Promise<Effect.Success<ReturnType<Diagnostics['inspect']>>> {
    this.assertOpen()
    return this.execute(
      Effect.flatMap(
        WorkspaceService,
        (workspace: WorkspaceModule): ReturnType<Diagnostics['inspect']> =>
          workspace.diagnostics.inspect(request),
      ),
      Option.some(signal),
    )
  }

  /** @anchor host-editing-dispatch */
  public async replace(
    request: Parameters<Editing['replace']>[0],
    signal: AbortSignal,
  ): Promise<Effect.Success<ReturnType<Editing['replace']>>> {
    this.assertOpen()
    return this.execute(
      Effect.flatMap(
        WorkspaceService,
        (workspace: WorkspaceModule): ReturnType<Editing['replace']> =>
          workspace.editing.replace(request),
      ),
      Option.some(signal),
    )
  }

  /** @anchor host-editing-dispatch */
  public async refactor(
    request: Parameters<Editing['refactor']>[0],
    signal: AbortSignal,
  ): Promise<Effect.Success<ReturnType<Editing['refactor']>>> {
    this.assertOpen()
    return this.execute(
      Effect.flatMap(
        WorkspaceService,
        (workspace: WorkspaceModule): ReturnType<Editing['refactor']> =>
          workspace.editing.refactor(request),
      ),
      Option.some(signal),
    )
  }

  /** @anchor host-editing-dispatch */
  public async commit(
    candidate: Parameters<Editing['commit']>[0],
    signal: AbortSignal,
  ): Promise<Effect.Success<ReturnType<Editing['commit']>>> {
    this.assertOpen()
    return this.execute(
      Effect.flatMap(
        WorkspaceService,
        (workspace: WorkspaceModule): ReturnType<Editing['commit']> =>
          workspace.editing.commit(candidate),
      ),
      Option.some(signal),
    )
  }

  /** @anchor host-preview-dispatch */
  public async render(
    request: Parameters<CanvasPreview['render']>[0],
    backend: Parameters<CanvasPreview['render']>[1],
    signal: AbortSignal,
  ): Promise<Effect.Success<ReturnType<CanvasPreview['render']>>> {
    this.assertOpen()
    return this.execute(
      Effect.flatMap(
        WorkspaceService,
        (workspace: WorkspaceModule): ReturnType<CanvasPreview['render']> =>
          workspace.preview.render(request, backend),
      ),
      Option.some(signal),
    )
  }

  /** @anchor host-lifecycle */
  public async close(): Promise<void> {
    if (this.closed) return
    this.closed = true
    await this.runtime.dispose()
    this.logger.info('Host 已关闭')
  }

  /** @anchor host-preview-display-dispatch */
  public async openPreview(
    request: OpenCanvasPreviewRequest,
    backend: Parameters<CanvasPreview['render']>[1],
    signal: AbortSignal,
  ): Promise<CanvasPreviewDisplay> {
    this.assertOpen()
    return this.execute(
      Effect.flatMap(
        WorkspaceService,
        (workspace: WorkspaceModule): Effect.Effect<CanvasPreviewDisplay, unknown> =>
          workspace.updates.openPreview(request, backend),
      ),
      Option.some(signal),
    )
  }

  /** @anchor host-lifecycle */
  private assertOpen(): void {
    if (this.closed) throw new Error('Host 已关闭')
  }

  /**
   * @anchor host-lifecycle
   * @anchor workspace-computation-concurrency
   */
  private execute<T>(
    effect: Effect.Effect<T, unknown, WorkspaceService>,
    signal: Option.Option<AbortSignal> = Option.none(),
  ): Promise<T> {
    const scoped: Effect.Effect<T, unknown, WorkspaceService> = Effect.flatMap(
      Effect.forkIn(effect, this.runtime.scope),
      (fiber: Fiber.Fiber<T, unknown>): Effect.Effect<T, unknown> =>
        Fiber.join(fiber).pipe(
          Effect.onInterrupt((): Effect.Effect<void> => Fiber.interrupt(fiber)),
        ),
    )
    return this.runtime.runPromise(
      scoped,
      Option.match(signal, {
        onNone: (): Effect.RunOptions => ({}),
        onSome: (current: AbortSignal): Effect.RunOptions => ({ signal: current }),
      }),
    )
  }
}
