import type { EntityReadBaseline } from '../entities/EntityReadBaseline.ts'

/** @anchor change-plan-refactor-input */
export abstract class RefactorRequest {
  declare private readonly nominal: never

  /** @anchor change-plan-refactor-input */
  protected constructor(
    public readonly subjectId: string,
    public readonly baseline: EntityReadBaseline,
  ) {}
}

/** @anchor change-plan-refactor-input */
type RenameScope = 'local-variable' | 'global-variable' | 'script'

/** @anchor change-plan-refactor-input */
interface SymbolRename {
  readonly oldName: string
  readonly newName: string
}

/** @anchor change-plan-refactor-input */
export class RenameRefactorRequest extends RefactorRequest {
  declare private readonly renameNominal: never

  /** @anchor change-plan-refactor-input */
  public constructor(
    subjectId: string,
    baseline: EntityReadBaseline,
    public readonly scope: RenameScope,
    public readonly renames: readonly SymbolRename[],
  ) {
    super(subjectId, baseline)
  }
}
