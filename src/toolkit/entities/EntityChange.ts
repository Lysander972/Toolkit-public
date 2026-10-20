import { posix } from 'node:path'
import { normalize, virtualFs } from '@angular-devkit/core'
import { HostTree, MergeStrategy } from '@angular-devkit/schematics'
import type { Tree, Action } from '@angular-devkit/schematics'
import { Effect, Option } from 'effect'
import { isDeepStrictEqual } from 'node:util'
import type { FileEntity } from '@toolkit/project-model/entities'
import type {
  EntityFileDeclaration,
  EntityDirectoryDeclaration,
} from '@toolkit/project-model/logical-entities'
import type { FileMetadata } from '../files/FileContracts.ts'
import { ProjectFiles } from '../files/ProjectFiles.ts'
import { WorkspacePaths } from '@toolkit/project-model/paths'

/** @anchor entity-candidates */
export class EntityChange {
  private constructor(
    public readonly relativePath: string,
    private readonly baseline: readonly EntityFileDeclaration[],
    private readonly backend: virtualFs.SimpleMemoryHost,
    private readonly tree: Tree,
    private readonly directories: readonly EntityDirectoryDeclaration[],
    private readonly createdDirectories: Set<string>,
    private readonly deletedDirectories: Set<string>,
  ) {}

  /** @anchor entity-candidates */
  public static prepare(
    files: ProjectFiles,
    entity: FileEntity,
    bytes: Uint8Array,
  ): Effect.Effect<EntityChange, unknown> {
    return Effect.map(
      EntityChange.open(
        files,
        entity.relativePath,
        [
          { relativePath: entity.relativePath, revision: entity.revision },
          { relativePath: entity.guidRelativePath, revision: entity.guidRevision },
        ],
        [],
      ),
      (candidate: EntityChange): EntityChange => {
        candidate.write(entity.relativePath, bytes)
        return candidate
      },
    )
  }

  /** @anchor entity-candidates */
  public branch(): EntityChange {
    const tree: Tree = new HostTree(this.backend)
    for (const action of this.tree.actions) tree.apply(action, MergeStrategy.Error)
    return new EntityChange(
      this.relativePath,
      this.baseline,
      this.backend,
      tree,
      this.directories,
      new Set(this.createdDirectories),
      new Set(this.deletedDirectories),
    )
  }

  /** @anchor entity-candidates */
  public merge(candidate: EntityChange): void {
    if (this.relativePath !== candidate.relativePath || this.baseline !== candidate.baseline)
      throw new Error('候选不属于同一实体文件基准')
    for (const relativePath of candidate.createdDirectories)
      if (this.deletedDirectories.has(relativePath))
        throw new Error(`目录动作冲突: ${relativePath}`)
    for (const relativePath of candidate.deletedDirectories)
      if (this.createdDirectories.has(relativePath))
        throw new Error(`目录动作冲突: ${relativePath}`)
    this.tree.merge(candidate.tree, MergeStrategy.Error)
    for (const relativePath of candidate.createdDirectories)
      this.createdDirectories.add(relativePath)
    for (const relativePath of candidate.deletedDirectories)
      this.deletedDirectories.add(relativePath)
  }

  /** @anchor entity-candidates */
  public static open(
    files: ProjectFiles,
    relativePath: string,
    baseline: readonly EntityFileDeclaration[],
    directories: readonly EntityDirectoryDeclaration[],
  ): Effect.Effect<EntityChange, unknown> {
    return Effect.gen(function* (): Generator<
      Effect.Effect<unknown, unknown>,
      EntityChange,
      never
    > {
      const backend: virtualFs.SimpleMemoryHost = new virtualFs.SimpleMemoryHost()
      const host: virtualFs.SyncDelegateHost = new virtualFs.SyncDelegateHost(backend)
      for (const file of baseline) {
        const before: FileMetadata = yield* files.metadata(file.relativePath)
        if (before.revision !== file.revision)
          return yield* Effect.fail(new Error(`候选文件基准已变化: ${file.relativePath}`))
        const bytes: Uint8Array = yield* files.read(file.relativePath)
        const current: FileMetadata = yield* files.metadata(file.relativePath)
        if (current.revision !== file.revision)
          return yield* Effect.fail(new Error(`候选文件基准已变化: ${file.relativePath}`))
        host.write(normalize('/' + file.relativePath), Uint8Array.from(bytes).buffer)
      }
      const candidate: EntityChange = new EntityChange(
        relativePath,
        baseline,
        backend,
        new HostTree(backend),
        directories,
        new Set(),
        new Set(),
      )
      yield* candidate.verify(files)
      return candidate
    })
  }

  /**
   * @anchor entity-candidates
   * @passthrough-exempt 候选操作必须通过 Schematics Tree 读取内存内容，不能绕过候选访问 ProjectFiles。
   */
  public readText(relativePath: string): string {
    this.requireScope(relativePath)
    return this.tree.readText(relativePath)
  }

  /**
   * @anchor entity-candidates
   * @passthrough-exempt 写操作统一进入候选 Tree，由 Tree 负责同路径 create/overwrite action。
   */
  public write(relativePath: string, content: Uint8Array | string): void {
    this.requireScope(relativePath)
    const bytes: Buffer =
      typeof content === 'string' ? Buffer.from(content, 'utf8') : Buffer.from(content)
    if (!bytes.equals(Option.getOrThrow(Option.fromNullishOr(this.tree.read(relativePath)))))
      this.tree.overwrite(relativePath, bytes)
  }

  /**
   * @anchor entity-candidates
   * @passthrough-exempt 创建操作统一进入候选 Tree，提交阶段才连接 ProjectFiles。
   */
  public create(relativePath: string, content: Uint8Array | string): void {
    this.requireScope(relativePath)
    this.tree.create(relativePath, typeof content === 'string' ? content : Buffer.from(content))
  }

  /**
   * @anchor entity-candidates
   * @passthrough-exempt 删除操作统一进入候选 Tree，提交阶段才连接 ProjectFiles。
   */
  public delete(relativePath: string): void {
    this.requireScope(relativePath)
    this.tree.delete(relativePath)
  }

  /**
   * @anchor entity-candidates
   * @passthrough-exempt 重命名必须保留 Schematics 的原生 action 语义，提交阶段编译为文件原语。
   */
  public rename(sourceRelativePath: string, targetRelativePath: string): void {
    this.requireScope(sourceRelativePath)
    this.requireScope(targetRelativePath)
    this.tree.rename(sourceRelativePath, targetRelativePath)
  }

  /** @anchor entity-candidates */
  public createDirectory(relativePath: string): void {
    this.requireScope(relativePath)
    if (
      this.directories.some(
        (directory: EntityDirectoryDeclaration): boolean => directory.relativePath === relativePath,
      )
    )
      throw new Error(`目录已存在: ${relativePath}`)
    this.createdDirectories.add(relativePath)
  }

  /**
   * @anchor entity-candidates
   * @passthrough-exempt 目录动作集中记录候选范围，提交阶段按目录声明连接 ProjectFiles。
   */
  public deleteDirectory(relativePath: string): void {
    this.requireScope(relativePath)
    if (
      !this.directories.some(
        (directory: EntityDirectoryDeclaration): boolean => directory.relativePath === relativePath,
      )
    )
      throw new Error(`目录不在基准声明中: ${relativePath}`)
    this.deletedDirectories.add(relativePath)
  }

  /** @anchor entity-candidates */
  public affectedDirectories(): readonly string[] {
    return [
      ...new Set([
        posix.dirname(this.relativePath),
        ...this.directories.map(
          (directory: EntityDirectoryDeclaration): string => directory.relativePath,
        ),
        ...this.createdDirectories,
      ]),
    ]
  }

  /** @anchor entity-candidates */
  public commit(files: ProjectFiles): Effect.Effect<boolean, unknown> {
    return Effect.gen({ self: this }, function* (this: EntityChange): Generator<
      Effect.Effect<unknown, unknown>,
      boolean,
      never
    > {
      yield* this.verify(files)
      let changed: boolean = false
      for (const relativePath of [...this.createdDirectories].sort(
        (left: string, right: string): number => left.split('/').length - right.split('/').length,
      )) {
        yield* files.makeDirectory(relativePath)
        changed = true
      }
      const actions: readonly Action[] = this.tree.actions
      for (const action of actions) {
        if (action.kind !== 'r') continue
        const targetRelativePath: string = posix.relative('/', action.to)
        const content: Buffer = Option.getOrThrow(
          Option.fromNullishOr(this.tree.read(targetRelativePath)),
        )
        yield* files.write(targetRelativePath, content)
        changed = true
      }
      for (const action of actions) {
        if (action.kind !== 'c' && action.kind !== 'o') continue
        const written: boolean = yield* files.write(
          EntityChange.relativePath(action),
          action.content,
        )
        changed = changed || written
      }
      for (const action of actions) {
        if (action.kind !== 'd' && action.kind !== 'r') continue
        yield* files.remove(EntityChange.relativePath(action))
        changed = true
      }
      for (const relativePath of [...this.deletedDirectories].sort(
        (left: string, right: string): number => right.split('/').length - left.split('/').length,
      )) {
        yield* files.removeDirectory(relativePath)
        changed = true
      }
      return changed
    })
  }

  /** @anchor entity-candidates */
  private verify(files: ProjectFiles): Effect.Effect<void, unknown> {
    return Effect.gen({ self: this }, function* (this: EntityChange): Generator<
      Effect.Effect<unknown, unknown>,
      void,
      never
    > {
      for (const baseline of this.baseline) {
        const current: FileMetadata = yield* files.metadata(baseline.relativePath)
        if (current.revision !== baseline.revision)
          return yield* Effect.fail(new Error(`候选文件基准已变化: ${baseline.relativePath}`))
      }
      for (const baseline of this.directories) {
        const current: readonly FileMetadata[] = yield* files.directory(baseline.relativePath)
        if (
          !isDeepStrictEqual(
            current.map((entry: FileMetadata): string => entry.relativePath),
            baseline.entries,
          )
        )
          return yield* Effect.fail(new Error(`候选目录基准已变化: ${baseline.relativePath}`))
      }
    })
  }

  /** @anchor entity-candidates */
  private requireScope(relativePath: string): void {
    WorkspacePaths.require(relativePath)
    if (
      !this.baseline.some(
        (file: EntityFileDeclaration): boolean => file.relativePath === relativePath,
      ) &&
      !this.directories.some(
        (directory: EntityDirectoryDeclaration): boolean =>
          directory.relativePath === relativePath ||
          directory.relativePath === posix.dirname(relativePath),
      ) &&
      !this.createdDirectories.has(posix.dirname(relativePath))
    )
      throw new Error(`候选路径超出实体范围: ${relativePath}`)
  }

  /** @anchor entity-candidates */
  private static relativePath(action: Action): string {
    return posix.relative('/', action.path)
  }
}
