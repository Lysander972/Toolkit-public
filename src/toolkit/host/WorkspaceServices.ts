import { Context, Effect, Layer, Option } from 'effect'
import { WorkspaceUpdates } from './WorkspaceUpdates.ts'
import type { ProjectSettings } from '@toolkit/project-model'
import { ProjectEntitiesService } from '../entities/ProjectEntities.ts'
import type { ProjectEntities } from '../entities/ProjectEntities.ts'
import { CanvasConversionService } from '../conversion/canvas/CanvasConversion.ts'
import { TsViewConversionService } from '../conversion/ts/TsViewConversion.ts'
import { HtmlViewConversionService } from '../conversion/html/HtmlViewConversion.ts'
import { ProjectViewsService } from '../views/ProjectViews.ts'
import type { ProjectViews, ViewMode } from '../views/ProjectViews.ts'
import { DiagnosticsService } from '../diagnostics/Diagnostics.ts'
import type { Diagnostics } from '../diagnostics/Diagnostics.ts'
import { LanguageQueriesService, type LanguageQueries } from '../language/LanguageQueries.ts'
import { EditingService } from '../editing/Editing.ts'
import type { Editing } from '../editing/Editing.ts'
import { CanvasPreviewService } from '../preview/CanvasPreview.ts'
import type { CanvasPreview } from '../preview/CanvasPreview.ts'
import { AssetTasksService } from '../assets/AssetTasks.ts'
import type { AssetTasks, AssetExecutionBindings } from '../assets/AssetTasks.ts'
import { ProjectWatcherService, type ProjectWatcher } from '../files/ProjectWatcher.ts'
import {
  InvocationRecordsService,
  type InvocationRecords,
} from '../invocations/InvocationRecords.ts'
import {
  AssetProcessingService,
  type AssetProcessing,
} from '../asset_processing/AssetProcessing.ts'
import {
  AssetCandidatesService,
  type AssetCandidates,
} from '../asset_candidates/AssetCandidates.ts'

/** @anchor workspace-service-stack */
export interface WorkspaceStatus {
  readonly viewMode: ViewMode
  readonly settings: ProjectSettings
}

/** @anchor workspace-service-stack */
export class WorkspaceModule {
  declare private readonly nominal: never

  public readonly updates: WorkspaceUpdates = new WorkspaceUpdates()

  /** @anchor workspace-service-stack */
  public constructor(
    public readonly entities: ProjectEntities,
    private readonly viewMode: ViewMode,
    public readonly views: ProjectViews,
    public readonly diagnostics: Diagnostics,
    public readonly editing: Editing,
    public readonly preview: CanvasPreview,
    public readonly assets: AssetTasks,
    public readonly language: LanguageQueries,
    public readonly watcher: ProjectWatcher,
    public readonly invocations: InvocationRecords,
    public readonly assetProcessing: AssetProcessing,
    public readonly assetCandidates: AssetCandidates,
  ) {}

  /** @anchor workspace-service-stack */
  public status(): Effect.Effect<WorkspaceStatus, unknown> {
    return Effect.map(this.entities.settings(), (settings: ProjectSettings): WorkspaceStatus => ({
      viewMode: this.viewMode,
      settings,
    }))
  }
}

/** @anchor workspace-service-stack */
export class WorkspaceService extends Context.Service<WorkspaceService, WorkspaceModule>()(
  'toolkit/Workspace',
) {
  private constructor(service: never) {
    super(service)
  }

  /**
   * @anchor workspace-service-stack
   * @anchor workspace-state-cache-inventory
   */
  public static layer(
    mode: ViewMode,
    entities: Layer.Layer<ProjectEntitiesService, unknown>,
    watcher: Layer.Layer<ProjectWatcherService, unknown>,
    execution: Option.Option<AssetExecutionBindings>,
  ): Layer.Layer<WorkspaceService, unknown> {
    const canvas: Layer.Layer<CanvasConversionService, unknown> =
      CanvasConversionService.layer(entities)
    const ts: Layer.Layer<TsViewConversionService, unknown> = TsViewConversionService.layer(canvas)
    const html: Layer.Layer<HtmlViewConversionService, unknown> =
      HtmlViewConversionService.layer(canvas)
    const views: Layer.Layer<ProjectViewsService, unknown> = ProjectViewsService.layer(
      mode,
      entities,
      ts,
      html,
    )
    const diagnostics: Layer.Layer<DiagnosticsService, unknown> = DiagnosticsService.layer(entities)
    const language: Layer.Layer<LanguageQueriesService, unknown> = LanguageQueriesService.layer(
      entities,
      diagnostics,
    )
    const editing: Layer.Layer<EditingService, unknown> = EditingService.layer(
      entities,
      views,
      ts,
      html,
    )
    const preview: Layer.Layer<CanvasPreviewService, unknown> = CanvasPreviewService.layer(entities)
    const assets: Layer.Layer<AssetTasksService, unknown> = AssetTasksService.layer(
      editing,
      AssetProcessingService.layer,
      AssetCandidatesService.layer,
      execution,
    )
    return Layer.effect(
      WorkspaceService,
      Effect.map(
        Effect.all([
          ProjectEntitiesService,
          ProjectViewsService,
          DiagnosticsService,
          EditingService,
          CanvasPreviewService,
          AssetTasksService,
          LanguageQueriesService,
          ProjectWatcherService,
          InvocationRecordsService,
          AssetProcessingService,
          AssetCandidatesService,
        ]),
        ([
          projectEntities,
          projectViews,
          projectDiagnostics,
          projectEditing,
          projectPreview,
          projectAssets,
          languageQueries,
          projectWatcher,
          invocationRecords,
          assetProcessing,
          assetCandidates,
        ]: readonly [
          ProjectEntities,
          ProjectViews,
          Diagnostics,
          Editing,
          CanvasPreview,
          AssetTasks,
          LanguageQueries,
          ProjectWatcher,
          InvocationRecords,
          AssetProcessing,
          AssetCandidates,
        ]): WorkspaceModule =>
          new WorkspaceModule(
            projectEntities,
            mode,
            projectViews,
            projectDiagnostics,
            projectEditing,
            projectPreview,
            projectAssets,
            languageQueries,
            projectWatcher,
            invocationRecords,
            assetProcessing,
            assetCandidates,
          ),
      ),
    ).pipe(
      Layer.provide(
        Layer.mergeAll(
          entities,
          html,
          views,
          diagnostics,
          language,
          editing,
          preview,
          assets,
          watcher,
          InvocationRecordsService.layer,
          AssetProcessingService.layer,
          AssetCandidatesService.layer,
        ),
      ),
    )
  }
}
