import type ts from 'typescript'
import type { CanvasDocument } from '@toolkit/canvas/model'
import { FormatEntity } from './FormatEntity.ts'
import type { EncodedEntityFile } from './FormatEntity.ts'
import type { EntityReadBaseline } from './EntityReadBaseline.ts'

/** @anchor view-entity-contract */
export abstract class ToolkitEntity extends FormatEntity {
  declare private readonly viewNominal: never

  /** @anchor view-entity-contract */
  protected constructor(
    public readonly sourceEntityId: string,
    public readonly viewRelativePath: string,
    baseline: EntityReadBaseline,
    public readonly text: string,
  ) {
    super(viewRelativePath, viewRelativePath, baseline)
  }

  /** @anchor view-entity-contract */
  public override encode(): readonly EncodedEntityFile[] {
    return [{ fileRelativePath: this.viewRelativePath, bytes: new TextEncoder().encode(this.text) }]
  }
}

/** @anchor view-entity-contract */
export class TsViewEntity extends ToolkitEntity {
  declare private readonly tsNominal: never

  /** @anchor view-entity-contract */
  public constructor(
    sourceEntityId: string,
    viewRelativePath: string,
    baseline: EntityReadBaseline,
    text: string,
    public readonly syntax: ts.SourceFile,
  ) {
    super(sourceEntityId, viewRelativePath, baseline, text)
  }
}

/** @anchor view-entity-contract */
export class HtmlViewEntity extends ToolkitEntity {
  declare private readonly htmlNominal: never

  /** @anchor view-entity-contract */
  public constructor(
    sourceEntityId: string,
    viewRelativePath: string,
    baseline: EntityReadBaseline,
    text: string,
    public readonly document: CanvasDocument,
  ) {
    super(sourceEntityId, viewRelativePath, baseline, text)
  }
}
