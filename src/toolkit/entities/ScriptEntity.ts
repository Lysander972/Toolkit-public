import type { LocalAst } from '@toolkit/script/local_ast'
import { FormatEntity } from './FormatEntity.ts'
import type { EncodedEntityFile } from './FormatEntity.ts'
import type { EntityReadBaseline } from './EntityReadBaseline.ts'

/** @anchor entity-format-contract */
export class ScriptEntity extends FormatEntity {
  declare private readonly scriptNominal: never

  /** @anchor entity-format-contract */
  public constructor(
    id: string,
    scriptRelativePath: string,
    baseline: EntityReadBaseline,
    public readonly ast: LocalAst,
    public readonly originals: readonly EncodedEntityFile[],
  ) {
    super(id, scriptRelativePath, baseline)
  }

  /** @anchor entity-format-contract */
  public override encode(): readonly EncodedEntityFile[] {
    throw new Error('TODO: 脚本 AST、编码、BOM 与伴生元数据的无损编码')
  }
}
