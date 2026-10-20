import { Context, Effect, Layer, Option } from 'effect'
import { ProjectEntitiesService, type ProjectEntities } from '../entities/ProjectEntities.ts'
import type {
  EntityFileChanges,
  EntityReadBaseline,
  EntityRefreshResult,
} from '../entities/EntityReadBaseline.ts'
import type { DeviceEntity } from '../entities/DeviceEntity.ts'
import type { HtmlViewEntity, ToolkitEntity, TsViewEntity } from '../entities/ToolkitEntity.ts'
import {
  TsViewConversionService,
  type TsViewConversion,
} from '../conversion/ts/TsViewConversion.ts'
import {
  HtmlViewConversionService,
  type HtmlViewConversion,
} from '../conversion/html/HtmlViewConversion.ts'

/** @anchor view-service-contract */
export type ViewMode = 'files' | 'memory'

/** @anchor view-refresh-data-contract */
export interface ViewPreviewDependency {
  readonly sourceEntityId: string
  readonly baseline: EntityReadBaseline
}

/** @anchor view-refresh-data-contract */
export interface ViewRefreshInput extends EntityRefreshResult {
  readonly baseline: EntityReadBaseline
  readonly derivedViewRelativePaths: EntityFileChanges['derivedViewRelativePaths']
  readonly previewDependencies: readonly ViewPreviewDependency[]
}

/** @anchor view-refresh-data-contract */
export interface ViewRefreshResult {
  readonly baseline: EntityReadBaseline
  readonly views: readonly ToolkitEntity[]
  readonly removedViewRelativePaths: readonly string[]
  readonly affectedPreviews: readonly ViewPreviewDependency[]
  readonly removedPreviewSourceEntityIds: readonly string[]
}

/** @anchor view-ts-refresh */
abstract class TsViewUpdate {
  declare private readonly nominal: never
}

/** @anchor view-ts-refresh */
export class UpdateTsView extends TsViewUpdate {
  declare private readonly updateNominal: never
  /** @anchor view-ts-refresh */
  public constructor(
    public readonly device: DeviceEntity,
    public readonly viewRelativePath: string,
  ) {
    super()
  }
}

/** @anchor view-ts-refresh */
export class RemoveTsView extends TsViewUpdate {
  declare private readonly removeNominal: never
  /** @anchor view-ts-refresh */
  public constructor(public readonly deviceRelativePath: string) {
    super()
  }
}

/** @anchor view-service-contract */
export interface ProjectViews {
  readonly mode: ViewMode
  /** @anchor view-ts-refresh */
  refreshTs(update: TsViewUpdate): Effect.Effect<Option.Option<TsViewEntity>, unknown>
  /** @anchor view-service-contract */
  readTs(deviceId: string): Effect.Effect<TsViewEntity, unknown>
  /**
   * @anchor view-service-contract
   * @anchor canvas-render-view-isolation
   */
  readHtml(canvasId: string): ReturnType<HtmlViewConversion['toHtml']>
  /** @anchor view-service-contract */
  publish(view: ToolkitEntity): Effect.Effect<boolean, unknown>
  /** @anchor view-service-contract */
  refresh(input: ViewRefreshInput): Effect.Effect<ViewRefreshResult, unknown>
}

/** @anchor view-service-contract */
class WorkspaceProjectViews implements ProjectViews {
  declare private readonly nominal: never

  /**
   * @anchor view-service-contract
   * @anchor workspace-view-mode-binding
   */
  public constructor(
    public readonly mode: ViewMode,
    private readonly ts: TsViewConversion,
    public readonly entities: ProjectEntities,
    public readonly html: HtmlViewConversion,
  ) {}

  /** @anchor view-ts-refresh */
  public refreshTs(update: TsViewUpdate): Effect.Effect<Option.Option<TsViewEntity>, unknown> {
    if (update instanceof UpdateTsView)
      return Effect.map(this.ts.toTs(update.device, update.viewRelativePath), Option.some)
    if (update instanceof RemoveTsView)
      return Effect.as(this.ts.remove(update.deviceRelativePath), Option.none<TsViewEntity>())
    return Effect.fail(new Error('未知 TS 视图更新消息'))
  }

  /** @anchor view-service-contract */
  public readTs(_deviceId: string): Effect.Effect<TsViewEntity, unknown> {
    return Effect.fail(new Error('TODO: 基于实体更新与 TS 转换服务供给零件视图'))
  }

  /**
   * @anchor view-service-contract
   * @anchor canvas-render-view-isolation
   */
  public readHtml(_canvasId: string): Effect.Effect<HtmlViewEntity, unknown> {
    return Effect.fail(new Error('TODO: 基于实体更新与 HTML 转换服务供给 Canvas 视图'))
  }

  /**
   * @anchor view-service-contract
   * @anchor derived-view-delivery
   */
  public publish(_view: ToolkitEntity): Effect.Effect<boolean, unknown> {
    return Effect.fail(new Error('TODO: 经实体层发布派生视图文件，隔离源文件事件'))
  }

  /**
   * @anchor view-service-contract
   * @anchor derived-view-delivery
   * @anchor preview-view-impact
   */
  public refresh(_input: ViewRefreshInput): Effect.Effect<ViewRefreshResult, unknown> {
    return Effect.fail(new Error('TODO: 按实体读取基准主动计算视图影响并交付更新与删除结果'))
  }
}

/** @anchor view-service-contract */
export class ProjectViewsService extends Context.Service<ProjectViewsService, ProjectViews>()(
  'toolkit/ProjectViews',
) {
  private constructor(service: never) {
    super(service)
  }

  /** @anchor view-service-contract */
  public static layer(
    mode: ViewMode,
    entities: Layer.Layer<ProjectEntitiesService, unknown>,
    ts: Layer.Layer<TsViewConversionService, unknown>,
    html: Layer.Layer<HtmlViewConversionService, unknown>,
  ): Layer.Layer<ProjectViewsService, unknown> {
    return Layer.effect(
      ProjectViewsService,
      Effect.map(
        Effect.all([ProjectEntitiesService, TsViewConversionService, HtmlViewConversionService]),
        ([projectEntities, tsConversion, htmlConversion]: readonly [
          ProjectEntities,
          TsViewConversion,
          HtmlViewConversion,
        ]): ProjectViews =>
          new WorkspaceProjectViews(mode, tsConversion, projectEntities, htmlConversion),
      ),
    ).pipe(Layer.provide(Layer.mergeAll(entities, ts, html)))
  }
}
