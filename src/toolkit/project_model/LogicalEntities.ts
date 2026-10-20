import { posix } from 'node:path'
import { Option } from 'effect'
import type { ProjectSettings } from './ProjectSettings.ts'
import { WorkspacePaths } from './WorkspacePaths.ts'
import { GuidFormat } from './GuidFormat.ts'

/** @anchor logical-entity-shape */
export type LogicalEntityKind =
  | 'root'
  | 'directory'
  | 'map'
  | 'mech'
  | 'device'
  | 'script-group'
  | 'inline-script'
  | 'code-repo-script'
  | 'canvas'
  | 'resource'
  | 'spine'
  | 'raw-file'
/** @anchor logical-entity-model */
export type CarrierKind = 'map' | 'mech'
/** @anchor logical-entity-model */
export type GuidAssetKind = 'code-repo-script' | 'canvas' | 'resource'
/** @anchor logical-entity-model */
export type GuidEntityKind = 'map' | 'mech' | 'code-repo-script' | 'canvas' | 'resource'
/** @anchor logical-script-model */
export type ScriptOrigin = 'inline' | 'repository'

/** @anchor logical-entity-shape */
export interface EntityFileDeclaration {
  readonly relativePath: string
  readonly revision: string
}
/** @anchor logical-entity-shape */
export interface EntityDirectoryDeclaration {
  readonly relativePath: string
  readonly entries: readonly string[]
}
/** @anchor logical-entity-shape */
export interface EntityDeclaration {
  readonly primaryRelativePath: string
  readonly files: readonly EntityFileDeclaration[]
  readonly directories: readonly EntityDirectoryDeclaration[]
}
/** @anchor logical-entity-shape */
export interface ChildEntityDeclaration extends EntityDeclaration {
  readonly parentId: string
}

/** @anchor logical-entity-shape */
export abstract class LogicalEntity<EntityKind extends LogicalEntityKind = LogicalEntityKind> {
  declare private readonly nominal: never
  public abstract readonly parentId: Option.Option<string>
  public readonly id: string
  public readonly primaryRelativePath: string
  public readonly ownedRelativePaths: readonly string[]
  public readonly files: readonly EntityFileDeclaration[]
  public readonly directories: readonly EntityDirectoryDeclaration[]

  /** @anchor logical-entity-shape */
  protected constructor(
    public readonly kind: EntityKind,
    declaration: EntityDeclaration,
    additionalRelativePaths: readonly string[] = [],
  ) {
    this.primaryRelativePath = WorkspacePaths.require(declaration.primaryRelativePath)
    this.id = kind === 'root' ? 'root' : kind + ':' + this.primaryRelativePath
    this.files = Object.freeze(
      declaration.files.map((file: EntityFileDeclaration): EntityFileDeclaration =>
        Object.freeze({ ...file }),
      ),
    )
    this.directories = Object.freeze(
      declaration.directories.map(
        (directory: EntityDirectoryDeclaration): EntityDirectoryDeclaration =>
          Object.freeze({
            relativePath: directory.relativePath,
            entries: Object.freeze([...directory.entries]),
          }),
      ),
    )
    this.ownedRelativePaths = Object.freeze([
      ...this.files.map((file: EntityFileDeclaration): string =>
        WorkspacePaths.require(file.relativePath),
      ),
      ...this.directories.map((directory: EntityDirectoryDeclaration): string =>
        WorkspacePaths.require(directory.relativePath),
      ),
    ])
    if (
      new Set(this.ownedRelativePaths).size !== this.ownedRelativePaths.length ||
      !this.ownedRelativePaths.includes(this.primaryRelativePath)
    )
      throw new Error('实体声明必须唯一覆盖主体')
    const directory: boolean = ['root', 'directory', 'spine', 'device', 'script-group'].includes(
      kind,
    )
    if (
      kind !== 'root' &&
      this.ownedRelativePaths.some(
        (relativePath: string): boolean =>
          relativePath !== this.primaryRelativePath &&
          !(directory && relativePath.startsWith(this.primaryRelativePath + '/')) &&
          !additionalRelativePaths.includes(relativePath),
      )
    )
      throw new Error('实体声明覆盖了主体范围外的路径')
    if (
      directory !==
      this.directories.some(
        (declaration: EntityDirectoryDeclaration): boolean =>
          declaration.relativePath === this.primaryRelativePath,
      )
    )
      throw new Error('实体主体形状与类型不一致')
  }

  /** @anchor logical-entity-shape */
  public require<EntityType extends LogicalEntity>(
    type: abstract new (...args: never[]) => EntityType,
  ): EntityType {
    if (!(this instanceof type)) throw new Error(`实体类型不符: ${this.kind}`)
    return this
  }

  /** @anchor logical-entity-operations */
  public createRelativePath(
    kind: ContainerCreateKind | FileCreateKind | CarrierKind,
    name: string,
  ): string {
    WorkspacePaths.require(name)
    if (name === '.' || name.includes('/')) throw new Error('实体名称必须是单一路径片段')
    if (kind === 'inline-script') this.require(LogicalScriptGroup)
    else if (kind === 'script-group') this.require(LogicalDevice)
    else if (!(this instanceof LogicalRoot || this instanceof LogicalDirectoryContainer))
      throw new Error('独立资产父级必须是工程根或资源目录')
    const relativePath: string = posix.join(
      this.primaryRelativePath,
      kind === 'inline-script' ? name + '.code' : name,
    )
    const extension: string = posix.extname(name)
    if (kind === 'directory' || kind === 'spine') {
      if (
        ['.map_', '.mech_'].includes(extension) ||
        (kind === 'spine') !== (extension === '.spine_')
      )
        throw new Error('目录名称与实体类型不一致')
    } else if (
      kind !== 'inline-script' &&
      kind !== 'script-group' &&
      (extension === '.guid' || LogicalGuidEntity.kind(relativePath) !== kind)
    )
      throw new Error('文件扩展名与实体类型不一致')
    return relativePath
  }

  /** @anchor logical-entity-operations */
  public moveRelativePath(
    parent: LogicalEntity,
    name: string,
    snapshot: LogicalEntitySnapshot,
  ): string {
    if (this instanceof LogicalInlineScript) {
      if (parent.id !== this.parentEntityId) throw new Error('内联脚本只能在同组重命名')
      return parent.createRelativePath('inline-script', name)
    }
    if (!(this instanceof LogicalGuidAsset || this instanceof LogicalDirectory))
      throw new Error(`实体禁止移动或重命名: ${this.kind}`)
    if (
      this instanceof LogicalDirectory &&
      snapshot
        .subtree(this)
        .some(
          (entity: LogicalEntity): boolean =>
            entity instanceof LogicalCarrier || entity instanceof LogicalSpine,
        )
    )
      throw new Error('目录移动不能改变固定实体的位置')
    const targetRelativePath: string = parent.createRelativePath(this.kind, name)
    if (
      this instanceof LogicalGuidAsset &&
      posix.extname(targetRelativePath) !== posix.extname(this.primaryRelativePath)
    )
      throw new Error('重命名必须保持资产扩展名')
    if (targetRelativePath.startsWith(this.primaryRelativePath + '/'))
      throw new Error('目录不能移动到自身子目录')
    return targetRelativePath
  }
}

/** @anchor logical-entity-shape */
export abstract class LogicalChildEntity<
  EntityKind extends Exclude<LogicalEntityKind, 'root'>,
> extends LogicalEntity<EntityKind> {
  declare private readonly childNominal: never
  public readonly parentEntityId: string
  /** @anchor logical-entity-shape */
  protected constructor(
    kind: EntityKind,
    declaration: ChildEntityDeclaration,
    additionalRelativePaths: readonly string[] = [],
  ) {
    super(kind, declaration, additionalRelativePaths)
    this.parentEntityId = declaration.parentId
  }

  /** @anchor logical-entity-shape */
  public get parentId(): Option.Option<string> {
    return Option.some(this.parentEntityId)
  }
}
/** @anchor logical-entity-shape */
export abstract class LogicalDirectoryContainer<
  EntityKind extends 'directory' | 'spine',
> extends LogicalChildEntity<EntityKind> {
  declare private readonly containerNominal: never
}

/** @anchor logical-entity-model */
export class LogicalRoot extends LogicalEntity<'root'> {
  declare private readonly rootNominal: never
  public readonly parentId: Option.Option<never> = Option.none()
  /** @anchor logical-entity-model */
  public constructor(
    declaration: EntityDeclaration,
    public readonly settings: Option.Option<ProjectSettings>,
  ) {
    super('root', declaration)
    if (this.primaryRelativePath !== '.') throw new Error('工程根必须使用根目录声明')
    Object.freeze(this)
  }
}
/** @anchor logical-entity-model */
export class LogicalDirectory extends LogicalDirectoryContainer<'directory'> {
  declare private readonly directoryNominal: never
  /** @anchor logical-entity-model */
  public constructor(declaration: ChildEntityDeclaration) {
    super('directory', declaration)
    Object.freeze(this)
  }
}
/** @anchor logical-entity-shape */
export interface GuidEntityDeclaration extends ChildEntityDeclaration {
  readonly guid: string
}
/** @anchor logical-entity-shape */
export abstract class LogicalGuidEntity<
  EntityKind extends GuidEntityKind,
> extends LogicalChildEntity<EntityKind> {
  declare private readonly guidNominal: never
  public readonly guid: string
  public readonly guidRelativePath: string
  /** @anchor logical-entity-shape */
  protected constructor(
    kind: EntityKind,
    declaration: GuidEntityDeclaration,
    additionalRelativePaths: readonly string[] = [],
  ) {
    super(kind, declaration, [
      declaration.primaryRelativePath + '.guid',
      ...additionalRelativePaths,
    ])
    this.guid = declaration.guid
    this.guidRelativePath = this.primaryRelativePath + '.guid'
    if (
      !GuidFormat.validate(this.guid) ||
      !this.files.some(
        (file: EntityFileDeclaration): boolean => file.relativePath === this.guidRelativePath,
      ) ||
      LogicalGuidEntity.kind(this.primaryRelativePath) !== kind
    )
      throw new Error('GUID 实体身份或伴生声明不符')
  }

  /** @anchor logical-entity-shape */
  public static kind(relativePath: string): GuidEntityKind {
    switch (posix.extname(relativePath)) {
      case '.code':
        return 'code-repo-script'
      case '.canvas':
        return 'canvas'
      case '.map':
        return 'map'
      case '.mech':
        return 'mech'
      default:
        return 'resource'
    }
  }
}
/** @anchor logical-entity-model */
export interface CarrierDeclaration extends GuidEntityDeclaration {
  readonly kind: CarrierKind
}
/** @anchor logical-entity-model */
export class LogicalCarrier extends LogicalGuidEntity<CarrierKind> {
  declare private readonly carrierNominal: never
  public readonly derivedDirectoryRelativePath: string
  public readonly indexRelativePath: string
  /** @anchor logical-entity-model */
  public constructor(declaration: CarrierDeclaration) {
    super(declaration.kind, declaration, [
      declaration.primaryRelativePath + '_',
      posix.join(declaration.primaryRelativePath + '_', 'mech.json'),
      posix.join(declaration.primaryRelativePath + '_', 'map.json'),
    ])
    this.derivedDirectoryRelativePath = this.primaryRelativePath + '_'
    this.indexRelativePath = posix.join(this.derivedDirectoryRelativePath, 'mech.json')
    Object.freeze(this)
  }
}
/** @anchor logical-entity-model */
export interface LogicalDeviceFields {
  readonly logicName: Option.Option<string>
  readonly mechFileGuid: Option.Option<string>
  readonly stickToBone: Option.Option<string>
}
/** @anchor logical-entity-model */
export interface DeviceDeclaration extends ChildEntityDeclaration {
  readonly deviceId: number
  readonly deviceType: number
  readonly fields: LogicalDeviceFields
  readonly variables: Option.Option<readonly LogicalVariable[]>
}
/** @anchor logical-script-model */
export interface LogicalVariable {
  readonly name: string
  readonly typeName: string
  readonly comment: Option.Option<string>
}
/** @anchor logical-entity-model */
export class LogicalDevice extends LogicalChildEntity<'device'> {
  declare private readonly deviceNominal: never
  public readonly deviceId: number
  public readonly deviceType: number
  public readonly deviceJsonRelativePath: string
  public readonly variablesRelativePath: string
  public readonly fields: LogicalDeviceFields
  public readonly variables: Option.Option<readonly LogicalVariable[]>
  /** @anchor logical-entity-model */
  public constructor(declaration: DeviceDeclaration) {
    super('device', declaration)
    if (posix.basename(this.primaryRelativePath) !== String(declaration.deviceId))
      throw new Error('零件目录名称与索引 ID 不一致')
    this.deviceId = declaration.deviceId
    this.deviceType = declaration.deviceType
    this.deviceJsonRelativePath = posix.join(this.primaryRelativePath, 'device.json')
    this.variablesRelativePath = posix.join(this.primaryRelativePath, 'vars.json')
    this.fields = Object.freeze({ ...declaration.fields })
    this.variables = Option.map(
      declaration.variables,
      (variables: readonly LogicalVariable[]): readonly LogicalVariable[] =>
        Object.freeze(
          variables.map((variable: LogicalVariable): LogicalVariable =>
            Object.freeze({ ...variable }),
          ),
        ),
    )
    Object.freeze(this)
  }
}
/** @anchor logical-script-model */
export interface LogicalScriptEntry {
  readonly name: string
  readonly origin: ScriptOrigin
  readonly guid: Option.Option<string>
  readonly private: boolean
  readonly relativePath: Option.Option<string>
}
/** @anchor logical-script-model */
export interface ScriptGroupDeclaration extends ChildEntityDeclaration {
  readonly eventName: Option.Option<string>
  readonly entries: readonly LogicalScriptEntry[]
}
/** @anchor logical-script-model */
export class LogicalScriptGroup extends LogicalChildEntity<'script-group'> {
  declare private readonly groupNominal: never
  public readonly listRelativePath: string
  public readonly eventName: Option.Option<string>
  public readonly entries: readonly LogicalScriptEntry[]
  /** @anchor logical-script-model */
  public constructor(declaration: ScriptGroupDeclaration) {
    super('script-group', declaration)
    this.listRelativePath = posix.join(this.primaryRelativePath, 'list.json')
    this.eventName = declaration.eventName
    this.entries = Object.freeze(
      declaration.entries.map((entry: LogicalScriptEntry): LogicalScriptEntry =>
        Object.freeze({ ...entry }),
      ),
    )
    Object.freeze(this)
  }
}
/** @anchor logical-script-model */
export class LogicalInlineScript extends LogicalChildEntity<'inline-script'> {
  declare private readonly inlineNominal: never
  /** @anchor logical-script-model */
  public constructor(declaration: ChildEntityDeclaration) {
    super('inline-script', declaration)
    Object.freeze(this)
  }
}
/** @anchor logical-entity-model */
export interface GuidAssetDeclaration extends GuidEntityDeclaration {
  readonly kind: GuidAssetKind
}
/** @anchor logical-entity-model */
export class LogicalGuidAsset extends LogicalGuidEntity<GuidAssetKind> {
  declare private readonly assetNominal: never
  /** @anchor logical-entity-model */
  public constructor(declaration: GuidAssetDeclaration) {
    super(declaration.kind, declaration)
    Object.freeze(this)
  }
}
/** @anchor logical-entity-model */
export class LogicalSpine extends LogicalDirectoryContainer<'spine'> {
  declare private readonly spineNominal: never
  /** @anchor logical-entity-model */
  public constructor(declaration: ChildEntityDeclaration) {
    super('spine', declaration)
    Object.freeze(this)
  }
}
/** @anchor logical-entity-model */
export class LogicalRawFile extends LogicalChildEntity<'raw-file'> {
  declare private readonly rawNominal: never
  /** @anchor logical-entity-model */
  public constructor(declaration: ChildEntityDeclaration) {
    super('raw-file', declaration)
    Object.freeze(this)
  }
}
/** @anchor logical-entity-model */
export type LogicalDiagnosticKind =
  | 'missing-file'
  | 'invalid-structure'
  | 'orphan-file'
  | 'unindexed-device'
  | 'missing-device'
  | 'invalid-device'
  | 'missing-guid'
  | 'orphan-guid'
  | 'invalid-guid'
  | 'duplicate-guid'
/** @anchor logical-entity-model */
export interface LogicalDiagnostic {
  readonly kind: LogicalDiagnosticKind
  readonly relativePath: string
  readonly relatedRelativePaths: readonly string[]
  readonly detail: string
}
/** @anchor logical-entity-shape */
export class LogicalEntitySnapshot {
  declare private readonly snapshotNominal: never
  public readonly entities: readonly LogicalEntity[]
  public readonly diagnostics: readonly LogicalDiagnostic[]
  private readonly entitiesByGuid: ReadonlyMap<string, readonly LogicalGuidEntity<GuidEntityKind>[]>
  /** @anchor logical-entity-shape */
  public constructor(
    entities: readonly LogicalEntity[],
    diagnostics: readonly LogicalDiagnostic[],
  ) {
    this.entities = Object.freeze([...entities])
    this.entitiesByGuid = Map.groupBy(
      this.entities.flatMap((entity: LogicalEntity): LogicalGuidEntity<GuidEntityKind>[] =>
        entity instanceof LogicalGuidEntity ? [entity] : [],
      ),
      (entity: LogicalGuidEntity<GuidEntityKind>): string => entity.guid,
    )
    this.diagnostics = Object.freeze(
      diagnostics.map((diagnostic: LogicalDiagnostic): LogicalDiagnostic =>
        Object.freeze({
          ...diagnostic,
          relatedRelativePaths: Object.freeze([...diagnostic.relatedRelativePaths]),
        }),
      ),
    )
    const ids: Set<string> = new Set()
    const owned: Set<string> = new Set()
    for (const entity of entities) {
      if (ids.has(entity.id)) throw new Error(`重复实体身份: ${entity.id}`)
      ids.add(entity.id)
      for (const relativePath of entity.ownedRelativePaths) {
        if (owned.has(relativePath)) throw new Error(`重复文件归属: ${relativePath}`)
        owned.add(relativePath)
      }
      if (entity instanceof LogicalChildEntity) {
        const parent: LogicalEntity = this.entity(entity.parentEntityId)
        if (!(
          parent instanceof LogicalRoot ||
          parent instanceof LogicalDirectoryContainer ||
          parent instanceof LogicalCarrier ||
          parent instanceof LogicalDevice ||
          parent instanceof LogicalScriptGroup
        ))
          throw new Error('父级实体必须是容器')
        const directoryRelativePath: string =
          parent instanceof LogicalCarrier
            ? parent.derivedDirectoryRelativePath
            : parent.primaryRelativePath
        if (posix.dirname(entity.primaryRelativePath) !== directoryRelativePath)
          throw new Error('子实体路径不属于声明父级')
        if (entity instanceof LogicalDevice) parent.require(LogicalCarrier)
        else if (entity instanceof LogicalScriptGroup) parent.require(LogicalDevice)
        else if (entity instanceof LogicalInlineScript) parent.require(LogicalScriptGroup)
        else if (
          entity instanceof LogicalGuidEntity &&
          !(parent instanceof LogicalRoot || parent instanceof LogicalDirectoryContainer)
        )
          throw new Error('GUID 实体必须属于资源容器')
      }
    }
    if (
      entities.length > 0 &&
      this.subtree(this.entity('root').require(LogicalRoot)).length !== entities.length
    )
      throw new Error('实体层级必须连通工程根')
    Object.freeze(this)
  }

  /** @anchor logical-entity-shape */
  public entity(id: string): LogicalEntity {
    return Option.getOrThrow(
      Option.fromNullishOr(
        this.entities.find((entity: LogicalEntity): boolean => entity.id === id),
      ),
    )
  }

  /** @anchor logical-entity-shape */
  public subtree(subject: LogicalEntity): readonly LogicalEntity[] {
    const selected: LogicalEntity[] = [subject]
    const ids: Set<string> = new Set([subject.id])
    for (const entity of selected)
      for (const child of this.entities)
        if (
          Option.isSome(child.parentId) &&
          child.parentId.value === entity.id &&
          !ids.has(child.id)
        ) {
          selected.push(child)
          ids.add(child.id)
        }
    return selected
  }

  /** @anchor logical-entity-operations */
  public requireVacant(relativePath: string): void {
    WorkspacePaths.require(relativePath)
    if (
      this.entities.some((entity: LogicalEntity): boolean =>
        entity.ownedRelativePaths.includes(relativePath),
      )
    )
      throw new Error(`目标已存在: ${relativePath}`)
  }

  /** @anchor logical-entity-shape */
  public query(query: LogicalEntityQuery): readonly LogicalEntity[] {
    const candidates: readonly LogicalEntity[] = Option.match(query.guid, {
      onNone: (): readonly LogicalEntity[] => this.entities,
      onSome: (guid: string): readonly LogicalGuidEntity<GuidEntityKind>[] => {
        const identities: readonly LogicalGuidEntity<GuidEntityKind>[] = Option.toArray(
          Option.fromNullishOr(this.entitiesByGuid.get(guid)),
        ).flat()
        LogicalEntitySnapshot.requireUniqueGuid(guid, identities)
        return identities
      },
    })
    const matches: readonly LogicalEntity[] = candidates.filter(
      (entity: LogicalEntity): boolean =>
        (Option.isNone(query.kind) || query.kind.value === entity.kind) &&
        (Option.isNone(query.primaryRelativePath) ||
          query.primaryRelativePath.value === entity.primaryRelativePath) &&
        (Option.isNone(query.parentId) ||
          (Option.isSome(entity.parentId) && query.parentId.value === entity.parentId.value)),
    )
    return matches
  }

  /** @anchor logical-entity-shape */
  public requireUniqueGuids(): void {
    for (const [guid, entities] of this.entitiesByGuid)
      LogicalEntitySnapshot.requireUniqueGuid(guid, entities)
  }

  /** @anchor logical-entity-shape */
  private static requireUniqueGuid(
    guid: string,
    entities: readonly LogicalGuidEntity<GuidEntityKind>[],
  ): void {
    if (entities.length > 1) throw new Error(`GUID 重复: ${guid}`)
  }

  /** @anchor logical-entity-operations */
  public scope(operation: LogicalEntityOperation): EntityOperationScope {
    const subject: LogicalEntity = this.entity(operation.subjectId)
    if (
      operation instanceof DeleteEntity &&
      (subject instanceof LogicalRoot || subject instanceof LogicalRawFile)
    )
      throw new Error(`实体禁止独立删除: ${subject.kind}`)
    const selected: LogicalEntity[] =
      operation instanceof DeleteEntity ||
      operation instanceof MoveEntity ||
      operation instanceof UpdateScriptList
        ? [...this.subtree(subject)]
        : [subject]
    if (
      (operation instanceof DeleteEntity || operation instanceof MoveEntity) &&
      subject instanceof LogicalChildEntity
    )
      selected.push(this.entity(subject.parentEntityId))
    if (operation instanceof MoveEntity) selected.push(this.entity(operation.parentId))
    const files: Map<string, EntityFileDeclaration> = new Map()
    const directories: Map<string, EntityDirectoryDeclaration> = new Map()
    for (const entity of selected) {
      for (const file of entity.files) files.set(file.relativePath, file)
      for (const directory of entity.directories) directories.set(directory.relativePath, directory)
    }
    return { subject, files: [...files.values()], directories: [...directories.values()] }
  }
}
/** @anchor logical-entity-model */
export interface LogicalEntityQuery {
  readonly kind: Option.Option<LogicalEntityKind>
  readonly primaryRelativePath: Option.Option<string>
  readonly guid: Option.Option<string>
  readonly parentId: Option.Option<string>
}

/** @anchor logical-entity-operations */
export interface EntityOperationScope {
  readonly subject: LogicalEntity
  readonly files: readonly EntityFileDeclaration[]
  readonly directories: readonly EntityDirectoryDeclaration[]
}

/** @anchor logical-entity-operations */
export abstract class LogicalEntityOperation {
  declare private readonly nominal: never
  /** @anchor logical-entity-operations */
  protected constructor(public readonly subjectId: string) {}
}
/** @anchor logical-entity-operations */
export type ContainerCreateKind = 'directory' | 'spine' | 'script-group'
/** @anchor logical-entity-operations */
export type FileCreateKind = 'code-repo-script' | 'canvas' | 'resource' | 'inline-script'
/** @anchor logical-entity-operations */
export class CreateContainer extends LogicalEntityOperation {
  declare private readonly createContainerNominal: never
  /** @anchor logical-entity-operations */
  public constructor(
    parentId: string,
    public readonly kind: ContainerCreateKind,
    public readonly name: string,
  ) {
    super(parentId)
  }
}
/** @anchor logical-entity-operations */
export class CreateFileEntity extends LogicalEntityOperation {
  declare private readonly createFileNominal: never
  public readonly bytes: Uint8Array
  /** @anchor logical-entity-operations */
  public constructor(
    parentId: string,
    public readonly kind: FileCreateKind,
    public readonly name: string,
    bytes: Uint8Array,
  ) {
    super(parentId)
    this.bytes = Uint8Array.from(bytes)
  }
}
/** @anchor logical-entity-operations */
export class CreateCarrier extends LogicalEntityOperation {
  declare private readonly createCarrierNominal: never
  public readonly bytes: Uint8Array
  /** @anchor logical-entity-operations */
  public constructor(
    parentId: string,
    public readonly kind: CarrierKind,
    public readonly name: string,
    bytes: Uint8Array,
    public readonly indexSource: string,
    public readonly mapSource: Option.Option<string>,
  ) {
    super(parentId)
    this.bytes = Uint8Array.from(bytes)
  }
}
/** @anchor logical-entity-operations */
export class CreateDevice extends LogicalEntityOperation {
  declare private readonly createDeviceNominal: never
  /** @anchor logical-entity-operations */
  public constructor(
    parentId: string,
    public readonly deviceId: number,
    public readonly source: string,
  ) {
    super(parentId)
  }
}
/** @anchor logical-update-operations */
export class UpdateBytes extends LogicalEntityOperation {
  declare private readonly updateBytesNominal: never
  public readonly bytes: Uint8Array
  /** @anchor logical-update-operations */
  public constructor(id: string, bytes: Uint8Array) {
    super(id)
    this.bytes = Uint8Array.from(bytes)
  }
}
/** @anchor logical-update-operations */
export class UpdateScript extends LogicalEntityOperation {
  declare private readonly updateScriptNominal: never
  /** @anchor logical-entity-operations */
  public constructor(
    id: string,
    public readonly source: string,
  ) {
    super(id)
  }
}
/** @anchor logical-update-operations */
export class UpdateScriptList extends LogicalEntityOperation {
  declare private readonly updateListNominal: never
  /** @anchor logical-entity-operations */
  public constructor(
    id: string,
    public readonly entries: readonly LogicalScriptEntry[],
  ) {
    super(id)
  }
}
/** @anchor logical-update-operations */
export class UpdateDeviceFields extends LogicalEntityOperation {
  declare private readonly updateDeviceNominal: never
  /** @anchor logical-entity-operations */
  public constructor(
    id: string,
    public readonly fields: LogicalDeviceFields,
  ) {
    super(id)
  }
}
/** @anchor logical-update-operations */
export class UpdateSettings extends LogicalEntityOperation {
  declare private readonly updateSettingsNominal: never
  /** @anchor logical-entity-operations */
  public constructor(
    id: string,
    public readonly name: string,
    public readonly mainMap: string,
  ) {
    super(id)
  }
}
/** @anchor logical-update-operations */
export class UpdateVariables extends LogicalEntityOperation {
  declare private readonly updateVariablesNominal: never
  /** @anchor logical-update-operations */
  public constructor(
    id: string,
    public readonly variables: Option.Option<readonly LogicalVariable[]>,
  ) {
    super(id)
  }
}
/** @anchor logical-entity-operations */
export class DeleteEntity extends LogicalEntityOperation {
  declare private readonly deleteNominal: never
  /** @anchor logical-entity-operations */
  public constructor(id: string) {
    super(id)
  }
}
/** @anchor logical-entity-operations */
export class MoveEntity extends LogicalEntityOperation {
  declare private readonly moveNominal: never
  /** @anchor logical-entity-operations */
  public constructor(
    id: string,
    public readonly parentId: string,
    public readonly name: string,
  ) {
    super(id)
  }
}
