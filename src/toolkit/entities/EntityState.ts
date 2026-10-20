import { isDeepStrictEqual } from 'node:util'
import { Option } from 'effect'
import { applySnapshot, destroy, getSnapshot, resolveIdentifier, types } from 'mobx-state-tree'
import type {
  IArrayType,
  IModelType,
  ISimpleType,
  IType,
  Instance,
  ModelProperties,
} from 'mobx-state-tree'
import type { EntityDiagnostic, EntitySnapshot, FileEntity } from '@toolkit/project-model/entities'
import { LogicalEntitySnapshot } from '@toolkit/project-model/logical-entities'

/** @anchor entity-state */
interface FileProperties extends ModelProperties {
  readonly relativePath: ISimpleType<string>
  readonly guid: ISimpleType<string>
  readonly revision: ISimpleType<string>
  readonly guidRelativePath: ISimpleType<string>
  readonly guidRevision: ISimpleType<string>
}

/** @anchor entity-state */
interface StateProperties extends ModelProperties {
  readonly files: IModelType<FileStateProperties, object>
  readonly logical: IType<LogicalEntitySnapshot, LogicalEntitySnapshot, LogicalEntitySnapshot>
}

/** @anchor entity-state */
interface FileStateProperties extends ModelProperties {
  readonly entities: IArrayType<IModelType<FileProperties, object>>
  readonly diagnostics: IType<
    readonly EntityDiagnostic[],
    readonly EntityDiagnostic[],
    readonly EntityDiagnostic[]
  >
  readonly directories: IType<
    EntitySnapshot['directories'],
    EntitySnapshot['directories'],
    EntitySnapshot['directories']
  >
}

/** @anchor entity-state */
export class EntityState {
  private static readonly file: IModelType<FileProperties, object> = types.model('FileEntity', {
    relativePath: types.identifier,
    guid: types.string,
    revision: types.string,
    guidRelativePath: types.string,
    guidRevision: types.string,
  })

  private static readonly model: IModelType<StateProperties, object> = types.model(
    'ProjectEntities',
    {
      files: types.model('FileEntities', {
        entities: types.array(EntityState.file),
        diagnostics: types.frozen<readonly EntityDiagnostic[]>(),
        directories: types.frozen<EntitySnapshot['directories']>(),
      }),
      logical: types.frozen<LogicalEntitySnapshot>(),
    },
  )

  private readonly tree: Instance<IModelType<StateProperties, object>> = EntityState.model.create({
    files: { entities: [], diagnostics: [], directories: [] },
    logical: new LogicalEntitySnapshot([], []),
  })

  /** @anchor entity-state */
  public replace(snapshot: EntitySnapshot): EntitySnapshot {
    const previous: EntitySnapshot = this.snapshot()
    if (isDeepStrictEqual(previous, snapshot)) return previous
    applySnapshot(this.tree.files, snapshot)
    return this.snapshot()
  }

  /** @anchor entity-state */
  public snapshot(): EntitySnapshot {
    return getSnapshot(this.tree.files)
  }

  /** @anchor entity-state */
  public replaceLogical(snapshot: LogicalEntitySnapshot): LogicalEntitySnapshot {
    const previous: LogicalEntitySnapshot = this.logicalSnapshot()
    if (isDeepStrictEqual(previous, snapshot)) return previous
    applySnapshot(this.tree, {
      ...getSnapshot(this.tree),
      logical: snapshot,
    })
    return this.logicalSnapshot()
  }

  /** @anchor entity-state */
  public logicalSnapshot(): LogicalEntitySnapshot {
    return getSnapshot(this.tree).logical
  }

  /** @anchor entity-state */
  public resolve(guid: string): Option.Option<FileEntity> {
    const matches: readonly FileEntity[] = this.tree.files.entities.filter(
      (entity: FileEntity): boolean => entity.guid === guid,
    )
    if (matches.length > 1) throw new Error(`GUID 重复: ${guid}`)
    return Option.map(Option.fromNullishOr(matches[0]), (entity: FileEntity): FileEntity =>
      getSnapshot(
        Option.getOrThrow(
          Option.fromNullishOr(resolveIdentifier(EntityState.file, this.tree, entity.relativePath)),
        ),
      ),
    )
  }

  /** @anchor entity-state */
  public close(): void {
    destroy(this.tree)
  }
}
