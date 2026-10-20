import type { AssetCapability } from '@toolkit/config/assets'

/** @anchor asset-generation-input-output */
export abstract class WorkflowInputValue {
  declare private readonly nominal: never
}

/** @anchor asset-generation-input-output */
abstract class GenerationParameterValue extends WorkflowInputValue {
  declare private readonly parameterNominal: never
}

/** @anchor asset-generation-input-output */
export class TextGenerationParameter extends GenerationParameterValue {
  declare private readonly textNominal: never

  /** @anchor asset-generation-input-output */
  public constructor(public readonly value: string) {
    super()
  }
}

/** @anchor asset-generation-input-output */
export class NumberGenerationParameter extends GenerationParameterValue {
  declare private readonly numberNominal: never

  /** @anchor asset-generation-input-output */
  public constructor(public readonly value: number) {
    super()
  }
}

/** @anchor asset-generation-input-output */
export class BooleanGenerationParameter extends GenerationParameterValue {
  declare private readonly booleanNominal: never

  /** @anchor asset-generation-input-output */
  public constructor(public readonly value: boolean) {
    super()
  }
}

/** @anchor asset-generation-input-output */
export interface GenerationArtifact {
  readonly artifactId: string
  readonly name: string
  readonly mimeType: string
  readonly format: string
  readonly content: AsyncIterable<Uint8Array>
}

/** @anchor asset-generation-input-output */
export interface GenerationRequest {
  readonly capability: AssetCapability
  readonly prompt: string
  readonly seed: number
  readonly parameters: Readonly<Record<string, GenerationParameterValue>>
  readonly inputs: readonly GenerationArtifact[]
}

/** @anchor asset-generation-input-output */
export interface GenerationOutput {
  readonly promptId: string
  readonly workflowId: string
  readonly artifacts: readonly GenerationArtifact[]
}

/** @anchor asset-generation-input-output */
export interface GenerationReviewRequest {
  readonly artifact: GenerationArtifact
  readonly instruction: string
}

/** @anchor asset-generation-input-output */
export interface GenerationReview {
  readonly promptId: string
  readonly workflowId: string
  readonly observations: readonly string[]
}
