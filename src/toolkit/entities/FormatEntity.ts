import type { EntityReadBaseline } from './EntityReadBaseline.ts'

/** @anchor entity-format-contract */
export interface EncodedEntityFile {
  readonly fileRelativePath: string
  readonly bytes: Uint8Array
}

/** @anchor entity-format-contract */
export abstract class FormatEntity {
  declare private readonly nominal: never

  /** @anchor entity-format-contract */
  protected constructor(
    public readonly id: string,
    public readonly sourceRelativePath: string,
    public readonly baseline: EntityReadBaseline,
  ) {}

  /** @anchor entity-format-contract */
  public abstract encode(): readonly EncodedEntityFile[]
}
