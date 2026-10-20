import { Effect, Option } from 'effect'
import type { ProjectFileChanges, ProjectWatcher } from '../files/ProjectWatcher.ts'
import type { ProjectEntities } from '../entities/ProjectEntities.ts'
import type { EntityRefreshResult } from '../entities/EntityReadBaseline.ts'
import type { HtmlViewEntity } from '../entities/ToolkitEntity.ts'
import type { ViewRefreshResult } from '../views/ProjectViews.ts'
import type { CanvasPreview, CanvasPreviewRequest } from '../preview/CanvasPreview.ts'

/** @anchor runtime-convergence-contract */
export interface WorkspaceRefreshRequest {
  readonly changes: ProjectFileChanges
  readonly reread: EntityRefreshResult
}

/** @anchor host-preview-display */
export interface OpenCanvasPreviewRequest {
  readonly sourceEntityId: string
  readonly viewport: CanvasPreviewRequest['viewport']
  readonly fonts: CanvasPreviewRequest['fonts']
  readonly timeSeconds: number
}

/** @anchor host-preview-display */
export interface CanvasPreviewContent {
  readonly view: HtmlViewEntity
  readonly preview: Effect.Success<ReturnType<CanvasPreview['render']>>
}

/** @anchor host-preview-display */
export abstract class CanvasPreviewDisplay {
  declare private readonly nominal: never

  /** @anchor host-preview-display */
  public abstract take(signal: AbortSignal): Promise<Option.Option<CanvasPreviewContent>>

  /** @anchor host-preview-display */
  public abstract close(): Promise<void>
}

/**
 * @anchor runtime-convergence-contract
 * @anchor host-preview-display
 */
export class WorkspaceUpdates {
  declare private readonly nominal: never

  /**
   * @anchor runtime-convergence-contract
   * @anchor workspace-view-mode-loop
   * @anchor workspace-refresh-phases
   */
  public watch(watcher: ProjectWatcher, entities: ProjectEntities): Effect.Effect<void, unknown> {
    return watcher.take().pipe(
      Effect.flatMap((changes: ProjectFileChanges): Effect.Effect<void, unknown> =>
        entities.refresh(changes.directoryRelativePaths).pipe(
          Effect.flatMap((reread: EntityRefreshResult): Effect.Effect<ViewRefreshResult, unknown> =>
            this.refresh({ changes, reread }),
          ),
          Effect.asVoid,
          Effect.catch((cause: unknown): Effect.Effect<void> => Effect.logError(cause)),
        ),
      ),
      Effect.forever,
      Effect.tapError((cause: unknown): Effect.Effect<void> => entities.fail(cause)),
    )
  }

  /**
   * @anchor runtime-convergence-contract
   * @anchor derived-view-reconciliation
   * @anchor preview-resource-refresh
   */
  public refresh(_request: WorkspaceRefreshRequest): Effect.Effect<ViewRefreshResult, unknown> {
    return Effect.fail(new Error('TODO: 按实际变化分类纠偏并交付已打开预览的同次刷新'))
  }

  /** @anchor host-preview-display */
  public openPreview(
    _request: OpenCanvasPreviewRequest,
    _backend: Parameters<CanvasPreview['render']>[1],
  ): Effect.Effect<CanvasPreviewDisplay, unknown> {
    return Effect.fail(new Error('TODO: 在同一 Host 登记 Canvas 展示并交付视图与预览刷新'))
  }
}
