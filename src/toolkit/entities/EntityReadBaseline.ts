import type {
  EntityDirectoryDeclaration,
  EntityFileDeclaration,
} from '@toolkit/project-model/logical-entities'
import type { SemanticAssemblyInput, SemanticDevice } from '../semantic/SemanticAssembly.ts'

/** @anchor semantic-assembly-contract */
interface FixedInputRevision {
  readonly name: string
  readonly revision: string
}

/** @anchor semantic-assembly-contract */
export interface EntityReadBaseline {
  readonly files: readonly EntityFileDeclaration[]
  readonly directories: readonly EntityDirectoryDeclaration[]
  readonly fixedInputs: readonly FixedInputRevision[]
}

/** @anchor runtime-convergence-contract */
export interface EntityRefreshResult {
  readonly updatedEntityIds: readonly string[]
  readonly removedEntityIds: readonly string[]
}

/** @anchor view-source-classification */
export interface EntityFileChanges {
  readonly sourceDirectoryRelativePaths: readonly string[]
  readonly derivedViewRelativePaths: readonly string[]
}

/** @anchor semantic-assembly-contract */
export interface SemanticReadInput {
  readonly assembly: SemanticAssemblyInput
  readonly baseline: EntityReadBaseline
}

/** @anchor semantic-assembly-contract */
export interface EntitySemanticSnapshot {
  readonly entities: SemanticAssemblyInput['entities']
  readonly analysis: SemanticAssemblyInput['analysis']
  readonly devices: readonly SemanticDevice[]
  readonly baseline: EntityReadBaseline
}
