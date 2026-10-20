import { Context, Effect, Layer } from 'effect'
import { ProjectEntitiesService, type ProjectEntities } from '../entities/ProjectEntities.ts'
import type { EntityReadBaseline } from '../entities/EntityReadBaseline.ts'
import type { CallGraph } from '@toolkit/analysis/call-graph'
import type { ScriptLint, LintSeverity } from '../lint/ScriptLint.ts'

/** @anchor diagnostics-service-contract */
export interface DiagnosticRequest {
  readonly entityIds: readonly string[]
  readonly baseline: EntityReadBaseline
}

/** @anchor diagnostics-service-contract */
interface Diagnostic {
  readonly entityId: string
  readonly sourceRelativePath: string
  readonly message: string
  readonly severity: LintSeverity
}

/** @anchor diagnostics-service-contract */
interface DiagnosticResult {
  readonly baseline: EntityReadBaseline
  readonly analysis: CallGraph
  readonly diagnostics: readonly Diagnostic[]
  readonly scriptDiagnostics: ReturnType<typeof ScriptLint.inspect>
}

/** @anchor diagnostics-service-contract */
export interface Diagnostics {
  /** @anchor diagnostics-service-contract */
  inspect(request: DiagnosticRequest): Effect.Effect<DiagnosticResult, unknown>
}

/** @anchor diagnostics-service-contract */
class PendingDiagnostics implements Diagnostics {
  declare private readonly nominal: never

  /** @anchor diagnostics-service-contract */
  public constructor(public readonly entities: ProjectEntities) {}

  /** @anchor diagnostics-service-contract */
  public inspect(_request: DiagnosticRequest): Effect.Effect<DiagnosticResult, unknown> {
    return Effect.fail(new Error('TODO: 按同次实体读取基准准备 Lint 并交付具名诊断结果'))
  }
}

/** @anchor diagnostics-service-contract */
export class DiagnosticsService extends Context.Service<DiagnosticsService, Diagnostics>()(
  'toolkit/Diagnostics',
) {
  private constructor(service: never) {
    super(service)
  }

  /** @anchor diagnostics-service-contract */
  public static layer(
    entities: Layer.Layer<ProjectEntitiesService, unknown>,
  ): Layer.Layer<DiagnosticsService, unknown> {
    return Layer.effect(
      DiagnosticsService,
      Effect.map(
        ProjectEntitiesService,
        (projectEntities: ProjectEntities): Diagnostics => new PendingDiagnostics(projectEntities),
      ),
    ).pipe(Layer.provide(entities))
  }
}
