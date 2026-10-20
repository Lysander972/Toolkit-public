import { Context, Effect, Layer } from 'effect'
import { ProjectEntitiesService, type ProjectEntities } from '../entities/ProjectEntities.ts'
import type { EntityReadBaseline } from '../entities/EntityReadBaseline.ts'
import { DiagnosticsService, type Diagnostics } from '../diagnostics/Diagnostics.ts'

/** @anchor language-query-input */
export type LanguageNavigationKind = 'definition' | 'references' | 'hover'

/** @anchor language-query-input */
export interface LanguagePosition {
  readonly line: number
  readonly character: number
}

/** @anchor language-query-input */
export interface LanguageDocument {
  readonly entityId: string
  readonly documentRelativePath: string
  readonly text: string
  readonly textVersion: number
  readonly baseline: EntityReadBaseline
}

/** @anchor language-query-input */
export abstract class LanguageQueryRequest {
  declare private readonly nominal: never

  /** @anchor language-query-input */
  protected constructor(
    public readonly document: LanguageDocument,
    public readonly signal: AbortSignal,
  ) {}
}

/** @anchor language-query-input */
export class NavigationQueryRequest extends LanguageQueryRequest {
  declare private readonly navigationNominal: never

  /** @anchor language-query-input */
  public constructor(
    public readonly kind: LanguageNavigationKind,
    public readonly position: LanguagePosition,
    document: LanguageDocument,
    signal: AbortSignal,
  ) {
    super(document, signal)
  }
}

/** @anchor language-query-input */
export class DocumentDiagnosticsRequest extends LanguageQueryRequest {
  declare private readonly diagnosticsNominal: never

  /** @anchor language-query-input */
  public constructor(document: LanguageDocument, signal: AbortSignal) {
    super(document, signal)
  }
}

/** @anchor language-query-output */
abstract class LanguageQueryResult {
  declare private readonly nominal: never

  /** @anchor language-query-output */
  protected constructor(
    public readonly documentRelativePath: string,
    public readonly textVersion: number,
    public readonly baseline: EntityReadBaseline,
  ) {}
}

/** @anchor language-query-contract */
export interface LanguageQueries {
  /** @anchor language-query-contract */
  query(request: LanguageQueryRequest): Effect.Effect<LanguageQueryResult, unknown>
}

/** @anchor language-query-contract */
class PendingLanguageQueries implements LanguageQueries {
  declare private readonly nominal: never

  /** @anchor language-query-contract */
  public constructor(
    public readonly entities: ProjectEntities,
    public readonly diagnostics: Diagnostics,
  ) {}

  /** @anchor language-query-contract */
  public query(_request: LanguageQueryRequest): ReturnType<LanguageQueries['query']> {
    return Effect.fail(new Error('TODO: 按实体基准与文本版本查询定义、引用、悬停及诊断'))
  }
}

/** @anchor language-query-contract */
export class LanguageQueriesService extends Context.Service<
  LanguageQueriesService,
  LanguageQueries
>()('toolkit/LanguageQueries') {
  private constructor(service: never) {
    super(service)
  }

  /** @anchor language-query-contract */
  public static layer(
    entities: Layer.Layer<ProjectEntitiesService, unknown>,
    diagnostics: Layer.Layer<DiagnosticsService, unknown>,
  ): Layer.Layer<LanguageQueriesService, unknown> {
    return Layer.effect(
      LanguageQueriesService,
      Effect.map(
        Effect.all([ProjectEntitiesService, DiagnosticsService]),
        ([projectEntities, projectDiagnostics]: readonly [
          ProjectEntities,
          Diagnostics,
        ]): LanguageQueries => new PendingLanguageQueries(projectEntities, projectDiagnostics),
      ),
    ).pipe(Layer.provide(Layer.mergeAll(entities, diagnostics)))
  }
}
