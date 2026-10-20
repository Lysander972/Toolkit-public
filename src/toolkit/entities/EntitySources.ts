import { posix } from 'node:path'
import { Effect, Option } from 'effect'
import { GuidMetadataParser } from '@toolkit/project-model/entities'
import type {
  EntityDiagnostic,
  EntitySnapshot,
  FileEntity,
  EntityDirectoryListing,
  EntityFileMetadata,
} from '@toolkit/project-model/entities'
import type { FileMetadata } from '../files/FileContracts.ts'
import { ProjectFiles } from '../files/ProjectFiles.ts'

/** @anchor entity-sources */
export class EntitySources {
  private readonly files: ProjectFiles
  private static readonly standaloneExtensions: ReadonlySet<string> = new Set<string>([
    '.code',
    '.canvas',
    '.png',
    '.jpg',
    '.jpeg',
    '.atlas',
    '.skel',
  ])

  /** @anchor entity-sources */
  public constructor(files: ProjectFiles) {
    this.files = files
  }

  /** @anchor entity-sources */
  private static isAssetDirectory(relativePath: string): boolean {
    return relativePath
      .split('/')
      .every((segment: string): boolean => !['.map_', '.mech_'].includes(posix.extname(segment)))
  }

  /** @anchor entity-sources */
  public load(): Effect.Effect<EntitySnapshot, unknown> {
    return this.refresh({ entities: [], diagnostics: [], directories: [] }, '.')
  }

  /** @anchor entity-sources */
  public refresh(
    previous: EntitySnapshot,
    directoryRelativePath: string,
  ): Effect.Effect<EntitySnapshot, unknown> {
    return Effect.flatMap(
      this.locate(directoryRelativePath),
      (listing: EntityDirectoryListing): Effect.Effect<EntitySnapshot, unknown> =>
        this.reload(previous, listing),
    )
  }

  /** @anchor entity-sources */
  private reload(
    previous: EntitySnapshot,
    listing: EntityDirectoryListing,
  ): Effect.Effect<EntitySnapshot, unknown> {
    return Effect.gen({ self: this }, function* (this: EntitySources): Generator<
      Effect.Effect<unknown, unknown>,
      EntitySnapshot,
      never
    > {
      const children: readonly string[] = listing.entries
        .filter((entry: EntityFileMetadata): boolean => entry.kind === 'directory')
        .map((entry: EntityFileMetadata): string => entry.relativePath)
      const removed: readonly string[] = previous.directories
        .map((directory: EntityDirectoryListing): string => directory.relativePath)
        .filter(
          (relativePath: string): boolean =>
            relativePath !== '.' &&
            posix.dirname(relativePath) === listing.relativePath &&
            !children.includes(relativePath),
        )
      const retained: (relativePath: string) => boolean = (relativePath: string): boolean =>
        posix.dirname(relativePath) !== listing.relativePath &&
        !removed.some((removedRelativePath: string): boolean =>
          relativePath.startsWith(removedRelativePath + '/'),
        )
      const direct: EntitySnapshot = yield* this.readDirectory(listing)
      const entities: FileEntity[] = [
        ...previous.entities.filter((entity: FileEntity): boolean => retained(entity.relativePath)),
        ...direct.entities,
      ]
      const diagnostics: EntityDiagnostic[] = [
        ...previous.diagnostics.filter(
          (diagnostic: EntityDiagnostic): boolean =>
            diagnostic.kind !== 'duplicate-guid' && retained(diagnostic.relativePath),
        ),
        ...direct.diagnostics,
      ]
      const directories: Map<string, EntityDirectoryListing> = new Map(
        previous.directories
          .filter(
            (directory: EntityDirectoryListing): boolean =>
              !removed.some(
                (removedRelativePath: string): boolean =>
                  directory.relativePath === removedRelativePath ||
                  directory.relativePath.startsWith(removedRelativePath + '/'),
              ),
          )
          .map((directory: EntityDirectoryListing): [string, EntityDirectoryListing] => [
            directory.relativePath,
            directory,
          ]),
      )
      directories.set(listing.relativePath, listing)
      for (const childRelativePath of children) {
        if (directories.has(childRelativePath)) continue
        const subtree: EntitySnapshot = yield* this.reload(
          { entities: [], diagnostics: [], directories: [] },
          yield* this.enumerate(childRelativePath),
        )
        entities.push(...subtree.entities)
        diagnostics.push(
          ...subtree.diagnostics.filter(
            (diagnostic: EntityDiagnostic): boolean => diagnostic.kind !== 'duplicate-guid',
          ),
        )
        for (const directory of subtree.directories)
          directories.set(directory.relativePath, directory)
      }
      return EntitySources.assemble(entities, diagnostics, [...directories.values()])
    })
  }

  /** @anchor entity-sources */
  private locate(directoryRelativePath: string): Effect.Effect<EntityDirectoryListing, unknown> {
    return Effect.gen({ self: this }, function* (this: EntitySources): Generator<
      Effect.Effect<unknown, unknown>,
      EntityDirectoryListing,
      never
    > {
      let listing: EntityDirectoryListing = yield* this.enumerate('.')
      if (directoryRelativePath === '.') return listing
      for (const segment of directoryRelativePath.split('/')) {
        const nextRelativePath: string = posix.join(listing.relativePath, segment)
        if (
          !listing.entries.some(
            (entry: EntityFileMetadata): boolean =>
              entry.kind === 'directory' && entry.relativePath === nextRelativePath,
          )
        )
          break
        listing = yield* this.enumerate(nextRelativePath)
      }
      return listing
    })
  }

  /** @anchor entity-sources */
  private enumerate(relativePath: string): Effect.Effect<EntityDirectoryListing, unknown> {
    return Effect.map(
      this.files.directory(relativePath),
      (entries: readonly FileMetadata[]): EntityDirectoryListing => ({
        relativePath,
        entries: entries.map((entry: FileMetadata): EntityFileMetadata => ({
          relativePath: entry.relativePath,
          kind: entry.kind,
          revision: entry.revision,
        })),
      }),
    )
  }

  /** @anchor entity-sources */
  private readDirectory(listing: EntityDirectoryListing): Effect.Effect<EntitySnapshot, unknown> {
    return Effect.gen({ self: this }, function* (this: EntitySources): Generator<
      Effect.Effect<unknown, unknown>,
      EntitySnapshot,
      never
    > {
      const files: readonly EntityFileMetadata[] = listing.entries.filter(
        (entry: EntityFileMetadata): boolean => entry.kind === 'file',
      )
      const entities: FileEntity[] = []
      const diagnostics: EntityDiagnostic[] = []
      if (!EntitySources.isAssetDirectory(listing.relativePath))
        return { entities, diagnostics, directories: [listing] }
      for (const file of files) {
        if (file.relativePath.endsWith('.guid')) {
          const sourceRelativePath: string = posix.join(
            listing.relativePath,
            posix.basename(file.relativePath, '.guid'),
          )
          if (
            !files.some(
              (entry: EntityFileMetadata): boolean => entry.relativePath === sourceRelativePath,
            )
          ) {
            diagnostics.push({
              kind: 'orphan-guid',
              relativePath: file.relativePath,
              guid: Option.none(),
              relatedRelativePaths: [sourceRelativePath],
              detail: '伴生 GUID 没有主体文件',
            })
          }
          continue
        }
        const guidRelativePath: string = file.relativePath + '.guid'
        const companion: Option.Option<EntityFileMetadata> = Option.fromNullishOr(
          files.find(
            (entry: EntityFileMetadata): boolean => entry.relativePath === guidRelativePath,
          ),
        )
        if (Option.isNone(companion)) {
          if (EntitySources.standaloneExtensions.has(posix.extname(file.relativePath)))
            diagnostics.push({
              kind: 'missing-guid',
              relativePath: file.relativePath,
              guid: Option.none(),
              relatedRelativePaths: [guidRelativePath],
              detail: '独立资产缺少 GUID 伴生文件',
            })
          continue
        }
        const bytes: Uint8Array = yield* this.files.read(guidRelativePath)
        const after: FileMetadata = yield* this.files.metadata(guidRelativePath)
        if (after.revision !== companion.value.revision)
          return yield* Effect.fail(new Error(`装配期间 GUID 文件变化: ${guidRelativePath}`))
        try {
          const guid: string = GuidMetadataParser.parse(
            new TextDecoder('utf-8', { fatal: true }).decode(bytes),
          )
          entities.push({
            guid,
            relativePath: file.relativePath,
            revision: file.revision,
            guidRelativePath,
            guidRevision: companion.value.revision,
          })
        } catch (cause: unknown) {
          diagnostics.push({
            kind: 'invalid-guid',
            relativePath: guidRelativePath,
            guid: Option.none(),
            relatedRelativePaths: [file.relativePath],
            detail: String(cause),
          })
        }
      }
      return { entities, diagnostics, directories: [listing] }
    })
  }

  /** @anchor entity-sources */
  private static assemble(
    entities: readonly FileEntity[],
    diagnostics: readonly EntityDiagnostic[],
    directories: readonly EntityDirectoryListing[],
  ): EntitySnapshot {
    const groups: Map<string, FileEntity[]> = new Map<string, FileEntity[]>()
    for (const entity of entities) {
      if (!groups.has(entity.guid)) groups.set(entity.guid, [])
      Option.getOrThrow(Option.fromNullishOr(groups.get(entity.guid))).push(entity)
    }
    const conflicts: EntityDiagnostic[] = []
    for (const [guid, members] of groups) {
      if (members.length < 2) continue
      const relatedRelativePaths: readonly string[] = members
        .map((entity: FileEntity): string => entity.relativePath)
        .sort((left: string, right: string): number => left.localeCompare(right))
      for (const entity of members)
        conflicts.push({
          kind: 'duplicate-guid',
          relativePath: entity.relativePath,
          guid: Option.some(guid),
          relatedRelativePaths,
          detail: 'GUID 对应多个文件实体',
        })
    }
    return {
      entities: [...entities].sort((left: FileEntity, right: FileEntity): number =>
        left.relativePath.localeCompare(right.relativePath),
      ),
      diagnostics: [...diagnostics, ...conflicts].sort(
        (left: EntityDiagnostic, right: EntityDiagnostic): number =>
          left.relativePath.localeCompare(right.relativePath),
      ),
      directories: [...directories].sort(
        (left: EntityDirectoryListing, right: EntityDirectoryListing): number =>
          left.relativePath.localeCompare(right.relativePath),
      ),
    }
  }
}
