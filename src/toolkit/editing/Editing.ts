import { Context, Effect, Layer } from 'effect'
import { ProjectEntitiesService, type ProjectEntities } from '../entities/ProjectEntities.ts'
import { ProjectViewsService, type ProjectViews } from '../views/ProjectViews.ts'
import type { EntityChange } from '../entities/EntityChange.ts'
import type { EntityReadBaseline } from '../entities/EntityReadBaseline.ts'
import type { ChangePlan } from '../change_plan/ChangePlan.ts'
import type { RefactorRequest } from '../change_plan/RefactorRequest.ts'
import {
  TsViewConversionService,
  type TsViewConversion,
} from '../conversion/ts/TsViewConversion.ts'
import {
  HtmlViewConversionService,
  type HtmlViewConversion,
} from '../conversion/html/HtmlViewConversion.ts'

/** @anchor editing-request-contract */
export abstract class EditingRequest {
  declare private readonly nominal: never

  /** @anchor editing-request-contract */
  protected constructor(
    public readonly updatedText: string,
    public readonly baseline: EntityReadBaseline,
  ) {}
}

/** @anchor editing-request-contract */
export class ReplaceTsViewRequest extends EditingRequest {
  declare private readonly tsNominal: never

  /** @anchor editing-request-contract */
  public constructor(
    public readonly view: Parameters<TsViewConversion['fromTs']>[0],
    updatedText: string,
    baseline: EntityReadBaseline,
  ) {
    super(updatedText, baseline)
  }
}

/** @anchor editing-request-contract */
export class ReplaceHtmlViewRequest extends EditingRequest {
  declare private readonly htmlNominal: never

  /** @anchor editing-request-contract */
  public constructor(
    public readonly view: Parameters<HtmlViewConversion['fromHtml']>[0],
    updatedText: string,
    baseline: EntityReadBaseline,
  ) {
    super(updatedText, baseline)
  }
}

/** @anchor editing-service-contract */
export interface EditingCandidate {
  readonly change: EntityChange
  readonly baseline: EntityReadBaseline
  readonly diagnostics: ReturnType<typeof ChangePlan.plan>['diagnostics']
}

/** @anchor editing-service-contract */
export interface Editing {
  /** @anchor editing-service-contract */
  replace(request: EditingRequest): Effect.Effect<EditingCandidate, unknown>
  /** @anchor editing-service-contract */
  refactor(request: RefactorRequest): Effect.Effect<EditingCandidate, unknown>
  /** @anchor editing-service-contract */
  commit(candidate: EditingCandidate): Effect.Effect<boolean, unknown>
  /** @anchor asset-import-contract */
  importAsset(request: AssetImportRequest): Effect.Effect<EditingCandidate, unknown>
}

/** @anchor asset-import-contract */
interface AssetImportRequest {
  readonly content: AsyncIterable<Uint8Array>
  readonly targetRelativePath: string
  readonly confirmation: string
}

/** @anchor editing-service-contract */
class PendingEditing implements Editing {
  declare private readonly nominal: never

  /** @anchor editing-service-contract */
  public constructor(
    public readonly entities: ProjectEntities,
    public readonly views: ProjectViews,
    public readonly ts: TsViewConversion,
    public readonly html: HtmlViewConversion,
  ) {}

  /** @anchor editing-service-contract */
  public replace(_request: EditingRequest): Effect.Effect<EditingCandidate, unknown> {
    return Effect.fail(new Error('TODO: 按完整读取基准反向转换、计划与校验统一编辑候选'))
  }

  /** @anchor editing-service-contract */
  public commit(_candidate: EditingCandidate): Effect.Effect<boolean, unknown> {
    return Effect.fail(new Error('TODO: 复核完整候选基准并经实体层提交及实际重读'))
  }

  /** @anchor editing-service-contract */
  public refactor(_request: RefactorRequest): Effect.Effect<EditingCandidate, unknown> {
    return Effect.fail(new Error('TODO: 按重构意图读取完整依赖、计算计划并校验统一编辑候选'))
  }

  /** @anchor asset-import-contract */
  public importAsset(_request: AssetImportRequest): Effect.Effect<EditingCandidate, unknown> {
    return Effect.fail(new Error('TODO: 资产字节准备统一 EntityChange'))
  }
}

/** @anchor editing-service-contract */
export class EditingService extends Context.Service<EditingService, Editing>()('toolkit/Editing') {
  private constructor(service: never) {
    super(service)
  }

  /** @anchor editing-service-contract */
  public static layer(
    entities: Layer.Layer<ProjectEntitiesService, unknown>,
    views: Layer.Layer<ProjectViewsService, unknown>,
    ts: Layer.Layer<TsViewConversionService, unknown>,
    html: Layer.Layer<HtmlViewConversionService, unknown>,
  ): Layer.Layer<EditingService, unknown> {
    return Layer.effect(
      EditingService,
      Effect.map(
        Effect.all([
          ProjectEntitiesService,
          ProjectViewsService,
          TsViewConversionService,
          HtmlViewConversionService,
        ]),
        ([projectEntities, projectViews, tsConversion, htmlConversion]: readonly [
          ProjectEntities,
          ProjectViews,
          TsViewConversion,
          HtmlViewConversion,
        ]): Editing =>
          new PendingEditing(projectEntities, projectViews, tsConversion, htmlConversion),
      ),
    ).pipe(Layer.provide(Layer.mergeAll(entities, views, ts, html)))
  }
}
