import { Context, Effect, Option, Ref, Result, Scope, Semaphore } from 'effect'
import { isDeepStrictEqual } from 'node:util'
import type { EntitySnapshot, FileEntity } from '@toolkit/project-model/entities'
import { ProjectSettingsParser } from '@toolkit/project-model'
import type { ProjectSettings } from '@toolkit/project-model'
import type {
  LogicalEntity,
  LogicalEntityOperation,
  LogicalEntityQuery,
  LogicalEntitySnapshot,
} from '@toolkit/project-model/logical-entities'
import { ProjectFiles } from '../files/ProjectFiles.ts'
import { EntitySources } from './EntitySources.ts'
import { EntityState } from './EntityState.ts'
import { EntityChange } from './EntityChange.ts'
import { LogicalEntitySources } from './LogicalEntitySources.ts'
import { EntityOperations } from './EntityOperations.ts'
import type { FileMetadata } from '../files/FileContracts.ts'
import type { DeviceEntity } from './DeviceEntity.ts'
import type { CanvasEntity } from './CanvasEntity.ts'
import type { ScriptEntity } from './ScriptEntity.ts'
import type { ToolkitEntity } from './ToolkitEntity.ts'
import { SemanticAssembly } from '../semantic/SemanticAssembly.ts'
import type {
  EntityRefreshResult,
  EntityFileChanges,
  EntitySemanticSnapshot,
  SemanticReadInput,
} from './EntityReadBaseline.ts'

/** @anchor entity-runtime */
export class ProjectEntitiesService extends Context.Service<
  ProjectEntitiesService,
  ProjectEntities
>()('toolkit/ProjectEntities') {
  private constructor(service: never) {
    super(service)
  }
}

/** @anchor entity-runtime */
export class ProjectEntities {
  private constructor(
    private readonly files: ProjectFiles,
    private readonly sources: EntitySources,
    private readonly state: EntityState,
    private readonly current: Ref.Ref<Result.Result<EntitySnapshot, unknown>>,
    private readonly serial: Semaphore.Semaphore,
    private readonly logicalSources: LogicalEntitySources,
    private readonly logicalCurrent: Ref.Ref<Result.Result<LogicalEntitySnapshot, unknown>>,
  ) {}

  /**
   * @anchor entity-runtime
   * @anchor workspace-entity-refresh-phases
   */
  public static make(files: ProjectFiles): Effect.Effect<ProjectEntities, unknown, Scope.Scope> {
    return Effect.gen(function* (): Generator<
      Effect.Effect<unknown, unknown, Scope.Scope>,
      ProjectEntities,
      never
    > {
      const sources: EntitySources = new EntitySources(files)
      const state: EntityState = yield* Effect.acquireRelease(
        Effect.sync((): EntityState => new EntityState()),
        (resource: EntityState): Effect.Effect<void> =>
          Effect.sync((): void => {
            resource.close()
          }),
      )
      const initial: EntitySnapshot = state.replace(yield* sources.load())
      const logicalSources: LogicalEntitySources = new LogicalEntitySources(files)
      const logicalInitial: LogicalEntitySnapshot = state.replaceLogical(
        yield* logicalSources.load(initial),
      )
      const logicalCurrent: Ref.Ref<Result.Result<LogicalEntitySnapshot, unknown>> =
        yield* Ref.make<Result.Result<LogicalEntitySnapshot, unknown>>(
          Result.succeed(logicalInitial),
        )
      const current: Ref.Ref<Result.Result<EntitySnapshot, unknown>> = yield* Ref.make<
        Result.Result<EntitySnapshot, unknown>
      >(Result.succeed(initial))
      const service: ProjectEntities = new ProjectEntities(
        files,
        sources,
        state,
        current,
        yield* Semaphore.make(1),
        logicalSources,
        logicalCurrent,
      )
      yield* Effect.addFinalizer((): Effect.Effect<void> =>
        Ref.set(logicalCurrent, Result.fail(new Error('工程实体服务已关闭'))),
      )
      yield* Effect.addFinalizer((): Effect.Effect<void> =>
        Ref.set(current, Result.fail(new Error('工程实体服务已关闭'))),
      )
      return service
    })
  }

  /** @anchor entity-runtime */
  public snapshot(): Effect.Effect<EntitySnapshot, unknown> {
    return Effect.flatMap(Ref.get(this.current), Effect.fromResult)
  }

  /** @anchor entity-runtime */
  public settings(): Effect.Effect<ProjectSettings, unknown> {
    return Effect.flatMap(this.snapshot(), (): Effect.Effect<ProjectSettings, unknown> =>
      Effect.map(this.files.read('setting.json'), (bytes: Uint8Array): ProjectSettings =>
        ProjectSettingsParser.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes)),
      ),
    )
  }

  /** @anchor entity-format-contract */
  public device(id: string): Effect.Effect<DeviceEntity, unknown> {
    return Effect.fail(new Error('TODO: 从权威实体声明装配零件格式实体: ' + id))
  }

  /** @anchor entity-format-contract */
  public canvas(guid: string): Effect.Effect<CanvasEntity, unknown> {
    return Effect.fail(new Error('TODO: 从权威 GUID 关联装配 Canvas 格式实体: ' + guid))
  }

  /** @anchor entity-format-contract */
  public script(id: string): Effect.Effect<ScriptEntity, unknown> {
    return Effect.fail(new Error('TODO: 从权威脚本绑定装配脚本格式实体: ' + id))
  }

  /**
   * @anchor entity-runtime
   * @anchor derived-view-entity-reconciliation
   */
  public publishView(view: ToolkitEntity): Effect.Effect<boolean, unknown> {
    return Effect.fail(
      new Error('TODO: 派生归属与源事件隔离后的视图发布: ' + view.viewRelativePath),
    )
  }

  /**
   * @anchor view-source-classification
   * @anchor derived-view-entity-reconciliation
   */
  public classifyChanges(
    _changedRelativePaths: readonly string[],
  ): Effect.Effect<EntityFileChanges, unknown> {
    return Effect.fail(new Error('TODO: 按唯一派生归属划分源重读目录与派生视图纠偏输入'))
  }

  /** @anchor entity-runtime */
  public logicalSnapshot(): Effect.Effect<LogicalEntitySnapshot, unknown> {
    return Effect.flatMap(Ref.get(this.logicalCurrent), Effect.fromResult)
  }

  /** @anchor semantic-assembly-contract */
  public semanticSnapshot(): Effect.Effect<EntitySemanticSnapshot, unknown> {
    return this.serial.withPermit(
      this.logicalSnapshot().pipe(
        Effect.flatMap(
          (snapshot: LogicalEntitySnapshot): Effect.Effect<SemanticReadInput, unknown> =>
            this.logicalSources.semanticInput(snapshot),
        ),
        Effect.flatMap((input: SemanticReadInput): Effect.Effect<EntitySemanticSnapshot, unknown> =>
          Effect.try({
            try: (): EntitySemanticSnapshot => ({
              entities: input.assembly.entities,
              analysis: input.assembly.analysis,
              devices: SemanticAssembly.assemble(input.assembly),
              baseline: input.baseline,
            }),
            catch: (cause: unknown): unknown => cause,
          }),
        ),
      ),
    )
  }

  /** @anchor entity-runtime */
  public fail(cause: unknown): Effect.Effect<void> {
    return this.serial.withPermit(
      Effect.all(
        [
          Ref.set(this.current, Result.fail(cause)),
          Ref.set(this.logicalCurrent, Result.fail(cause)),
        ],
        { discard: true },
      ),
    )
  }

  /** @anchor entity-runtime */
  public query(query: LogicalEntityQuery): Effect.Effect<readonly LogicalEntity[], unknown> {
    return Effect.flatMap(
      this.logicalSnapshot(),
      (snapshot: LogicalEntitySnapshot): Effect.Effect<readonly LogicalEntity[], unknown> =>
        Effect.try({
          try: (): readonly LogicalEntity[] => snapshot.query(query),
          catch: (cause: unknown): unknown => cause,
        }),
    )
  }

  /** @anchor entity-runtime */
  public prepareOperation(
    baseline: LogicalEntitySnapshot,
    operation: LogicalEntityOperation,
  ): Effect.Effect<EntityChange, unknown> {
    return this.serial.withPermit(
      Effect.flatMap(this.logicalSnapshot(), (): Effect.Effect<EntityChange, unknown> =>
        EntityOperations.prepare(this.files, baseline, operation),
      ),
    )
  }

  /** @anchor entity-runtime */
  public resolve(guid: string): Effect.Effect<Option.Option<FileEntity>, unknown> {
    return Effect.flatMap(this.snapshot(), (): Effect.Effect<Option.Option<FileEntity>, unknown> =>
      Effect.try({
        try: (): Option.Option<FileEntity> => this.state.resolve(guid),
        catch: (cause: unknown): unknown => cause,
      }),
    )
  }

  /** @anchor entity-runtime */
  public read(entity: FileEntity): Effect.Effect<Uint8Array, unknown> {
    return this.serial.withPermit(
      Effect.gen({ self: this }, function* (this: ProjectEntities): Generator<
        Effect.Effect<unknown, unknown>,
        Uint8Array,
        never
      > {
        yield* this.snapshot()
        const bytes: Uint8Array = yield* this.files.read(entity.relativePath)
        const current: FileMetadata = yield* this.files.metadata(entity.relativePath)
        if (current.revision !== entity.revision)
          return yield* Effect.fail(new Error(`实体内容版本已变化: ${entity.relativePath}`))
        const companion: FileMetadata = yield* this.files.metadata(entity.guidRelativePath)
        if (companion.revision !== entity.guidRevision)
          return yield* Effect.fail(new Error(`实体身份版本已变化: ${entity.guidRelativePath}`))
        return bytes
      }),
    )
  }

  /** @anchor entity-runtime */
  public prepare(entity: FileEntity, bytes: Uint8Array): Effect.Effect<EntityChange, unknown> {
    return this.serial.withPermit(
      Effect.flatMap(this.snapshot(), (): Effect.Effect<EntityChange, unknown> =>
        EntityChange.prepare(this.files, entity, bytes),
      ),
    )
  }

  /** @anchor entity-runtime */
  public commit(candidate: EntityChange): Effect.Effect<boolean, unknown> {
    return this.serial.withPermit(
      Effect.flatMap(this.snapshot(), (): Effect.Effect<boolean, unknown> =>
        candidate
          .commit(this.files)
          .pipe(
            Effect.onExit((): Effect.Effect<void, unknown> =>
              this.publish(candidate.affectedDirectories()),
            ),
          ),
      ),
    )
  }

  /**
   * @anchor entity-runtime
   * @anchor workspace-entity-refresh-phases
   */
  public refresh(
    directoryRelativePaths: readonly string[],
  ): Effect.Effect<EntityRefreshResult, unknown> {
    return this.serial.withPermit(
      Effect.gen({ self: this }, function* (this: ProjectEntities): Generator<
        Effect.Effect<unknown, unknown>,
        EntityRefreshResult,
        never
      > {
        const previous: LogicalEntitySnapshot = this.state.logicalSnapshot()
        yield* this.publish(directoryRelativePaths)
        const current: LogicalEntitySnapshot = yield* this.logicalSnapshot()
        const before: ReadonlyMap<string, LogicalEntity> = new Map(
          previous.entities.map((entity: LogicalEntity): readonly [string, LogicalEntity] => [
            entity.id,
            entity,
          ]),
        )
        const after: ReadonlySet<string> = new Set(
          current.entities.map((entity: LogicalEntity): string => entity.id),
        )
        return {
          updatedEntityIds: current.entities
            .filter(
              (entity: LogicalEntity): boolean => !isDeepStrictEqual(before.get(entity.id), entity),
            )
            .map((entity: LogicalEntity): string => entity.id),
          removedEntityIds: previous.entities
            .filter((entity: LogicalEntity): boolean => !after.has(entity.id))
            .map((entity: LogicalEntity): string => entity.id),
        }
      }),
    )
  }

  /** @anchor entity-runtime */
  private publish(directoryRelativePaths: readonly string[]): Effect.Effect<void, unknown> {
    return Effect.gen({ self: this }, function* (this: ProjectEntities): Generator<
      Effect.Effect<unknown, unknown>,
      void,
      never
    > {
      const previous: Result.Result<EntitySnapshot, unknown> = yield* Ref.get(this.current)
      const baseline: EntitySnapshot = this.state.snapshot()
      const load: Effect.Effect<EntitySnapshot, unknown> = Result.isFailure(previous)
        ? this.sources.load()
        : Effect.reduce(
            (): EntitySnapshot => baseline,
            (
              snapshot: EntitySnapshot,
              relativePath: string,
            ): Effect.Effect<EntitySnapshot, unknown> =>
              this.sources.refresh(snapshot, relativePath),
          )(directoryRelativePaths)
      const loaded: Result.Result<EntitySnapshot, unknown> = yield* Effect.result(
        Effect.map(load, (snapshot: EntitySnapshot): EntitySnapshot =>
          this.state.replace(snapshot),
        ),
      )
      if (!(
        Result.isSuccess(previous) &&
        Result.isSuccess(loaded) &&
        previous.success === loaded.success
      ))
        yield* Ref.set(this.current, loaded)
      const logicalLoaded: Result.Result<LogicalEntitySnapshot, unknown> = Result.isSuccess(loaded)
        ? yield* Effect.result(
            Effect.map(
              this.logicalSources.load(loaded.success),
              (snapshot: LogicalEntitySnapshot): LogicalEntitySnapshot =>
                this.state.replaceLogical(snapshot),
            ),
          )
        : Result.fail(loaded.failure)
      const logicalPrevious: Result.Result<LogicalEntitySnapshot, unknown> = yield* Ref.get(
        this.logicalCurrent,
      )
      if (!(
        Result.isSuccess(logicalPrevious) &&
        Result.isSuccess(logicalLoaded) &&
        logicalPrevious.success === logicalLoaded.success
      ))
        yield* Ref.set(this.logicalCurrent, logicalLoaded)
      yield* Effect.fromResult(logicalLoaded)
    })
  }
}
