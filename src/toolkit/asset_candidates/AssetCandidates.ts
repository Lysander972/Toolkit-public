import { Context, Effect, Layer } from 'effect'
import type { AssetCandidate, CandidateStoreRequest } from './CandidateContracts.ts'

/** @anchor asset-candidates-contract */
export interface AssetCandidates {
  /** @anchor asset-candidates-contract */
  store(request: CandidateStoreRequest, signal: AbortSignal): Effect.Effect<AssetCandidate, unknown>
  /** @anchor asset-candidates-contract */
  read(
    taskId: string,
    artifactId: string,
    signal: AbortSignal,
  ): Effect.Effect<AsyncIterable<Uint8Array>, unknown>
  /** @anchor asset-candidates-contract */
  release(taskId: string): Effect.Effect<void, unknown>
}

/** @anchor asset-candidates-contract */
class PendingAssetCandidates implements AssetCandidates {
  declare private readonly nominal: never

  /** @anchor asset-candidates-contract */
  public store(
    _request: CandidateStoreRequest,
    _signal: AbortSignal,
  ): Effect.Effect<AssetCandidate, unknown> {
    return Effect.fail(new Error('TODO: Host Scope 独占候选流式存储及校验结果关联'))
  }

  /** @anchor asset-candidates-contract */
  public read(
    _taskId: string,
    _artifactId: string,
    _signal: AbortSignal,
  ): Effect.Effect<AsyncIterable<Uint8Array>, unknown> {
    return Effect.fail(new Error('TODO: 按任务和具名产物读取候选字节'))
  }

  /** @anchor asset-candidates-contract */
  public release(_taskId: string): Effect.Effect<void, unknown> {
    return Effect.fail(new Error('TODO: 释放指定任务所属候选资源'))
  }
}

/** @anchor asset-candidates-contract */
export class AssetCandidatesService extends Context.Service<
  AssetCandidatesService,
  AssetCandidates
>()('toolkit/AssetCandidates') {
  private constructor(service: never) {
    super(service)
  }

  public static readonly layer: Layer.Layer<AssetCandidatesService> = Layer.effect(
    AssetCandidatesService,
    Effect.sync((): AssetCandidates => new PendingAssetCandidates()),
  )
}
