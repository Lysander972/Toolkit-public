import type { FileEntity } from '@toolkit/project-model/entities'
import type { CanvasDocument } from '@toolkit/canvas/model'
import { FormatEntity } from './FormatEntity.ts'
import type { EncodedEntityFile } from './FormatEntity.ts'
import type { EntityReadBaseline } from './EntityReadBaseline.ts'

/** @anchor entity-format-contract */
export class CanvasEntity extends FormatEntity {
  declare private readonly canvasNominal: never

  /** @anchor entity-format-contract */
  public constructor(
    public readonly declaration: FileEntity,
    public readonly document: CanvasDocument,
    public readonly originals: readonly EncodedEntityFile[],
    baseline: EntityReadBaseline,
  ) {
    super(declaration.guid, declaration.relativePath, baseline)
  }

  /** @anchor entity-format-contract */
  public override encode(): readonly EncodedEntityFile[] {
    throw new Error('TODO: Canvas 语义、主体与伴生元数据的无损编码')
  }
}
