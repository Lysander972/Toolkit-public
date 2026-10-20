import type {
  LogicalEntityOperation,
  LogicalEntitySnapshot,
} from '@toolkit/project-model/logical-entities'
import type { CallGraph } from '@toolkit/analysis/call-graph'
import type { CanvasEntity } from '../entities/CanvasEntity.ts'
import type { DeviceEntity } from '../entities/DeviceEntity.ts'
import type { ScriptEntity } from '../entities/ScriptEntity.ts'
import type { EntityReadBaseline } from '../entities/EntityReadBaseline.ts'
import type { RefactorRequest } from './RefactorRequest.ts'

/** @anchor change-plan-contract */
export abstract class ChangePlanRequest {
  declare private readonly nominal: never
}

/** @anchor change-plan-contract */
export class DeviceChangePlanRequest extends ChangePlanRequest {
  declare private readonly deviceNominal: never

  /** @anchor change-plan-contract */
  public constructor(
    public readonly original: DeviceEntity,
    public readonly updated: DeviceEntity,
  ) {
    super()
  }
}

/** @anchor change-plan-contract */
export class CanvasChangePlanRequest extends ChangePlanRequest {
  declare private readonly canvasNominal: never

  /** @anchor change-plan-contract */
  public constructor(
    public readonly original: CanvasEntity,
    public readonly updated: CanvasEntity,
  ) {
    super()
  }
}

/** @anchor change-plan-contract */
export class ScriptChangePlanRequest extends ChangePlanRequest {
  declare private readonly scriptNominal: never

  /** @anchor change-plan-contract */
  public constructor(
    public readonly original: ScriptEntity,
    public readonly updated: ScriptEntity,
  ) {
    super()
  }
}

/** @anchor change-plan-contract */
export class OperationChangePlanRequest extends ChangePlanRequest {
  declare private readonly operationNominal: never

  /** @anchor change-plan-contract */
  public constructor(public readonly operation: LogicalEntityOperation) {
    super()
  }
}

/** @anchor change-plan-contract */
export interface ChangePlanInput {
  readonly snapshot: LogicalEntitySnapshot
  readonly baseline: EntityReadBaseline
  readonly analysis: CallGraph
  readonly request: ChangePlanRequest
}

/** @anchor change-plan-contract */
export class RefactorChangePlanRequest extends ChangePlanRequest {
  declare private readonly refactorNominal: never

  /** @anchor change-plan-contract */
  public constructor(public readonly intent: RefactorRequest) {
    super()
  }
}

/** @anchor change-plan-contract */
interface ChangePlanDiagnostic {
  readonly subjectId: string
  readonly message: string
}

/** @anchor change-plan-contract */
export interface ChangePlanResult {
  readonly operations: readonly LogicalEntityOperation[]
  readonly diagnostics: readonly ChangePlanDiagnostic[]
  readonly baseline: EntityReadBaseline
}

/** @anchor change-plan-contract */
export class ChangePlan {
  private constructor() {}

  /** @anchor change-plan-contract */
  public static plan(_input: ChangePlanInput): ChangePlanResult {
    throw new Error('TODO: 按原始实体、反向转换结果与分析生成统一操作集合及实际依赖基准')
  }
}
