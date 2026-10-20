import type { GenerationArtifact } from '../asset_generation/GenerationContracts.ts'
import type { AssetInspection } from '../asset_processing/AssetProcessing.ts'

/** @anchor asset-candidates-contract */
export interface AssetCandidate {
  readonly taskId: string
  readonly artifactId: string
  readonly name: string
  readonly mimeType: string
  readonly format: string
  readonly inspection: AssetInspection
}

/** @anchor asset-candidates-contract */
export interface CandidateStoreRequest {
  readonly taskId: string
  readonly artifact: GenerationArtifact
  readonly inspection: AssetInspection
}
