import { Effect } from 'effect'
import type { AssetCredentials } from '@toolkit/config/credentials'
import type { WorkflowBindings } from './WorkflowBindings.ts'
import type { ComfyExecution } from './ComfyExecution.ts'
import type {
  GenerationRequest,
  GenerationReviewRequest,
  GenerationReview,
} from './GenerationContracts.ts'

/** @anchor asset-generation-contract */
export class AssetGeneration {
  private constructor() {}

  /** @anchor asset-generation-contract */
  public static execute(
    _request: GenerationRequest,
    _config: Parameters<typeof WorkflowBindings.select>[0],
    _credentials: AssetCredentials,
    _signal: AbortSignal,
  ): ReturnType<typeof ComfyExecution.execute> {
    return Effect.fail(new Error('TODO: 选择已验证工作流并执行具名生成请求'))
  }

  /** @anchor asset-generation-contract */
  public static review(
    _request: GenerationReviewRequest,
    _config: Parameters<typeof WorkflowBindings.select>[0],
    _credentials: AssetCredentials,
    _signal: AbortSignal,
  ): Effect.Effect<GenerationReview, unknown> {
    return Effect.fail(new Error('TODO: 选定候选经已验证原生工作流进行视觉评阅'))
  }
}
