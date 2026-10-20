import { Context, Effect, Layer, type Option } from 'effect'
import { EditingService } from '../editing/Editing.ts'
import type { Editing } from '../editing/Editing.ts'
import type {
  SubmitAssetTask,
  ReadAssetTaskStatus,
  ReadAssetTaskResult,
  CancelAssetTask,
  DiscardAssetTask,
  RenderAssetSlice,
  ReviewAssetTask,
  ConfirmAssetTask,
  AssetTaskResult,
  AssetTaskStatusResult,
  AssetTaskArtifactsResult,
  AssetTaskCancelResult,
  AssetTaskSliceResult,
  AssetTaskReviewResult,
  AssetTaskConfirmResult,
} from './AssetTaskContracts.ts'
import type { AssetGeneration } from '../asset_generation/AssetGeneration.ts'
import {
  AssetProcessingService,
  type AssetProcessing,
} from '../asset_processing/AssetProcessing.ts'
import {
  AssetCandidatesService,
  type AssetCandidates,
} from '../asset_candidates/AssetCandidates.ts'

/** @anchor asset-service-contract */
export interface AssetExecutionBindings {
  readonly config: Parameters<typeof AssetGeneration.execute>[1]
  readonly credentials: Parameters<typeof AssetGeneration.execute>[2]
}

/** @anchor asset-service-contract */
interface AssetTaskDependencies {
  readonly execution: Option.Option<AssetExecutionBindings>
  readonly processing: AssetProcessing
  readonly candidates: AssetCandidates
  readonly editing: Editing
}

/** @anchor asset-service-contract */
export interface AssetTasks {
  /** @anchor asset-task-lifecycle-ports */
  submit(command: SubmitAssetTask): Effect.Effect<AssetTaskResult, unknown>
  /** @anchor asset-task-lifecycle-ports */
  status(command: ReadAssetTaskStatus): Effect.Effect<AssetTaskStatusResult, unknown>
  /** @anchor asset-task-lifecycle-ports */
  result(command: ReadAssetTaskResult): Effect.Effect<AssetTaskArtifactsResult, unknown>
  /** @anchor asset-task-lifecycle-ports */
  cancel(command: CancelAssetTask): Effect.Effect<AssetTaskCancelResult, unknown>
  /** @anchor asset-task-lifecycle-ports */
  discard(command: DiscardAssetTask): Effect.Effect<AssetTaskResult, unknown>
  /** @anchor asset-task-review-ports */
  renderSlice(command: RenderAssetSlice): Effect.Effect<AssetTaskSliceResult, unknown>
  /** @anchor asset-task-review-ports */
  review(command: ReviewAssetTask): Effect.Effect<AssetTaskReviewResult, unknown>
  /** @anchor asset-task-confirm-port */
  confirm(command: ConfirmAssetTask): Effect.Effect<AssetTaskConfirmResult, unknown>
}

/** @anchor asset-service-contract */
class PendingAssetTasks implements AssetTasks {
  declare private readonly nominal: never

  /** @anchor asset-service-contract */
  public constructor(public readonly dependencies: AssetTaskDependencies) {}

  /** @anchor asset-task-lifecycle-ports */
  public submit(_command: SubmitAssetTask): ReturnType<AssetTasks['submit']> {
    return Effect.fail(new Error('TODO: 资产任务提交'))
  }

  /** @anchor asset-task-lifecycle-ports */
  public status(_command: ReadAssetTaskStatus): ReturnType<AssetTasks['status']> {
    return Effect.fail(new Error('TODO: 资产任务状态'))
  }

  /** @anchor asset-task-lifecycle-ports */
  public result(_command: ReadAssetTaskResult): ReturnType<AssetTasks['result']> {
    return Effect.fail(new Error('TODO: 资产任务结果'))
  }

  /** @anchor asset-task-lifecycle-ports */
  public cancel(_command: CancelAssetTask): ReturnType<AssetTasks['cancel']> {
    return Effect.fail(new Error('TODO: 资产任务取消'))
  }

  /** @anchor asset-task-lifecycle-ports */
  public discard(_command: DiscardAssetTask): ReturnType<AssetTasks['discard']> {
    return Effect.fail(new Error('TODO: 资产候选丢弃'))
  }

  /** @anchor asset-task-review-ports */
  public renderSlice(_command: RenderAssetSlice): ReturnType<AssetTasks['renderSlice']> {
    return Effect.fail(new Error('TODO: 资产切片预览'))
  }

  /** @anchor asset-task-review-ports */
  public review(_command: ReviewAssetTask): ReturnType<AssetTasks['review']> {
    return Effect.fail(new Error('TODO: 资产候选评阅'))
  }

  /** @anchor asset-task-confirm-port */
  public confirm(_command: ConfirmAssetTask): ReturnType<AssetTasks['confirm']> {
    return Effect.fail(new Error('TODO: 资产任务确认与编辑业务入库候选'))
  }
}

/** @anchor asset-service-contract */
export class AssetTasksService extends Context.Service<AssetTasksService, AssetTasks>()(
  'toolkit/AssetTasks',
) {
  private constructor(service: never) {
    super(service)
  }

  /** @anchor asset-service-contract */
  public static layer(
    editing: Layer.Layer<EditingService, unknown>,
    processing: Layer.Layer<AssetProcessingService>,
    candidates: Layer.Layer<AssetCandidatesService>,
    execution: Option.Option<AssetExecutionBindings>,
  ): Layer.Layer<AssetTasksService, unknown> {
    return Layer.effect(
      AssetTasksService,
      Effect.map(
        Effect.all([EditingService, AssetProcessingService, AssetCandidatesService]),
        ([service, assetProcessing, assetCandidates]: readonly [
          Editing,
          AssetProcessing,
          AssetCandidates,
        ]): AssetTasks =>
          new PendingAssetTasks({
            editing: service,
            processing: assetProcessing,
            candidates: assetCandidates,
            execution,
          }),
      ),
    ).pipe(Layer.provide(Layer.mergeAll(editing, processing, candidates)))
  }
}
