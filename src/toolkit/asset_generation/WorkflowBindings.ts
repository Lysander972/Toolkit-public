import type { AssetGenerationConfig } from '@toolkit/config/assets'
import type { ComfyExecution } from './ComfyExecution.ts'
import type { GenerationRequest } from './GenerationContracts.ts'

/** @anchor asset-workflow-binding */
export class WorkflowBindings {
  private constructor() {}

  /** @anchor asset-workflow-binding */
  public static select(
    _config: AssetGenerationConfig,
    _request: GenerationRequest,
  ): Parameters<typeof ComfyExecution.execute>[0] {
    throw new Error('TODO: 有来源 API graph、参数与实际模型绑定及验证')
  }
}
