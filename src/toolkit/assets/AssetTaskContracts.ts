import type { Effect, Option } from 'effect'
import type { GenerationReview } from '../asset_generation/GenerationContracts.ts'
import type { Editing } from '../editing/Editing.ts'
import type { AssetGeneration } from '../asset_generation/AssetGeneration.ts'
import type { AssetProcessing } from '../asset_processing/AssetProcessing.ts'
import type { AssetCandidates } from '../asset_candidates/AssetCandidates.ts'

/** @anchor asset-task-lifecycle-input */
export abstract class AssetTaskCommand {
  declare private readonly nominal: never
}

/** @anchor asset-task-lifecycle-input */
export class SubmitAssetTask extends AssetTaskCommand {
  declare private readonly submitNominal: never
  /** @anchor asset-task-lifecycle-input */
  public constructor(
    public readonly request: Parameters<typeof AssetGeneration.execute>[0],
    public readonly requirements: readonly Parameters<AssetProcessing['inspect']>[1][],
  ) {
    super()
  }
}

/** @anchor asset-task-lifecycle-input */
export class ReadAssetTaskStatus extends AssetTaskCommand {
  declare private readonly statusNominal: never
  /** @anchor asset-task-lifecycle-input */
  public constructor(public readonly taskId: string) {
    super()
  }
}

/** @anchor asset-task-lifecycle-input */
export class ReadAssetTaskResult extends AssetTaskCommand {
  declare private readonly resultNominal: never
  /** @anchor asset-task-lifecycle-input */
  public constructor(public readonly taskId: string) {
    super()
  }
}

/** @anchor asset-task-lifecycle-input */
export class CancelAssetTask extends AssetTaskCommand {
  declare private readonly cancelNominal: never
  /** @anchor asset-task-lifecycle-input */
  public constructor(public readonly taskId: string) {
    super()
  }
}

/** @anchor asset-task-lifecycle-input */
export class DiscardAssetTask extends AssetTaskCommand {
  declare private readonly discardNominal: never
  /** @anchor asset-task-lifecycle-input */
  public constructor(public readonly taskId: string) {
    super()
  }
}

/** @anchor asset-task-artifact-input */
export class RenderAssetSlice extends AssetTaskCommand {
  declare private readonly sliceNominal: never
  /** @anchor asset-task-artifact-input */
  public constructor(
    public readonly taskId: string,
    public readonly artifactId: string,
    public readonly slice: Parameters<AssetProcessing['renderSlice']>[0]['slice'],
    public readonly sizes: Parameters<AssetProcessing['renderSlice']>[0]['sizes'],
  ) {
    super()
  }
}

/** @anchor asset-task-artifact-input */
export class ReviewAssetTask extends AssetTaskCommand {
  declare private readonly reviewNominal: never
  /** @anchor asset-task-artifact-input */
  public constructor(
    public readonly taskId: string,
    public readonly artifactId: string,
    public readonly instruction: string,
  ) {
    super()
  }
}

/** @anchor asset-task-artifact-input */
export class ConfirmAssetTask extends AssetTaskCommand {
  declare private readonly confirmNominal: never
  /** @anchor asset-task-artifact-input */
  public constructor(
    public readonly taskId: string,
    public readonly artifactId: string,
    public readonly targetRelativePath: string,
    public readonly confirmation: Parameters<Editing['importAsset']>[0]['confirmation'],
  ) {
    super()
  }
}

/** @anchor asset-task-output-contract */
type AssetTaskState =
  'pending' | 'running' | 'ready' | 'failed' | 'canceled' | 'confirmed' | 'discarded'

/** @anchor asset-task-output-contract */
interface AssetTaskStatus {
  readonly taskId: string
  readonly status: AssetTaskState
  readonly diagnostics: readonly string[]
}

/** @anchor asset-task-output-contract */
interface AssetImportResult {
  readonly entityId: string
  readonly resourceRelativePath: string
  readonly guid: string
}

/** @anchor asset-task-output-contract */
export interface AssetTaskResult {
  readonly taskId: string
}

/** @anchor asset-task-output-contract */
export interface AssetTaskStatusResult extends AssetTaskResult {
  readonly task: AssetTaskStatus
}

/** @anchor asset-task-output-contract */
export interface AssetTaskArtifactsResult extends AssetTaskResult {
  readonly artifacts: readonly Effect.Success<ReturnType<AssetCandidates['store']>>[]
}

/** @anchor asset-task-output-contract */
export interface AssetTaskCancelResult extends AssetTaskResult {
  readonly localStopped: boolean
  readonly remoteStopped: Option.Option<boolean>
}

/** @anchor asset-task-output-contract */
export interface AssetTaskSliceResult extends AssetTaskResult {
  readonly preview: Effect.Success<ReturnType<AssetProcessing['renderSlice']>>
}

/** @anchor asset-task-output-contract */
export interface AssetTaskReviewResult extends AssetTaskResult {
  readonly review: GenerationReview
}

/** @anchor asset-task-output-contract */
export interface AssetTaskConfirmResult extends AssetTaskResult {
  readonly resource: AssetImportResult
}
