import { Effect } from 'effect'
import type { AssetGenerationConfig } from '@toolkit/config/assets'
import type { AssetCredentials } from '@toolkit/config/credentials'
import type { GenerationArtifact, GenerationOutput } from './GenerationContracts.ts'
import { WorkflowInputValue } from './GenerationContracts.ts'

/** @anchor asset-native-execution */
export class ComfyNodeReference extends WorkflowInputValue {
  declare private readonly referenceNominal: never

  /** @anchor asset-native-execution */
  public constructor(
    public readonly nodeId: string,
    public readonly outputIndex: number,
  ) {
    super()
  }
}

/** @anchor asset-native-execution */
interface ComfyNode {
  readonly class_type: string
  readonly inputs: Readonly<Record<string, WorkflowInputValue>>
}

/** @anchor asset-native-execution */
export interface ComfyExecutionRequest {
  readonly workflowId: string
  readonly graph: Readonly<Record<string, ComfyNode>>
  readonly uploads: readonly GenerationArtifact[]
}

/** @anchor asset-native-execution */
export class ComfyExecution {
  private constructor() {}

  /** @anchor asset-native-execution */
  public static execute(
    _request: ComfyExecutionRequest,
    _config: AssetGenerationConfig,
    _credentials: AssetCredentials,
    _signal: Parameters<AssetCredentials['read']>[0],
  ): Effect.Effect<GenerationOutput, unknown> {
    return Effect.fail(new Error('TODO: 原生 ComfyUI 上传、prompt、history 与 view 执行'))
  }
}
