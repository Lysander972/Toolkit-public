import { posix } from 'node:path'
import { Option } from 'effect'
import type { EntityFileMetadata } from '@toolkit/project-model/entities'
import type {
  LogicalDiagnostic,
  EntityDeclaration,
  EntityFileDeclaration,
  EntityDirectoryDeclaration,
  LogicalEntity,
} from '@toolkit/project-model/logical-entities'
import { LogicalEntitySnapshot } from '@toolkit/project-model/logical-entities'

/** @anchor entity-source-assembly */
export class EntityAssembly {
  declare private readonly nominal: never
  private readonly entities: LogicalEntity[] = []
  private readonly diagnostics: LogicalDiagnostic[] = []
  private readonly claimedRelativePaths: Set<string> = new Set()
  private readonly parents: Map<string, string> = new Map([['.', 'root']])

  /** @anchor entity-source-assembly */
  public constructor(
    public readonly listings: ReadonlyMap<string, readonly EntityFileMetadata[]>,
    private readonly files: ReadonlyMap<string, EntityFileMetadata>,
  ) {}

  /** @anchor entity-source-assembly */
  public declare(
    primaryRelativePath: string,
    ownedRelativePaths: readonly string[],
  ): EntityDeclaration {
    const files: EntityFileDeclaration[] = []
    const directories: EntityDirectoryDeclaration[] = []
    for (const relativePath of ownedRelativePaths) {
      this.claimedRelativePaths.add(relativePath)
      const listing: Option.Option<readonly EntityFileMetadata[]> = Option.fromNullishOr(
        this.listings.get(relativePath),
      )
      const file: Option.Option<EntityFileMetadata> = Option.fromNullishOr(
        this.files.get(relativePath),
      )
      if (Option.isSome(listing))
        directories.push({
          relativePath,
          entries: listing.value.map((entry: EntityFileMetadata): string => entry.relativePath),
        })
      if (Option.isSome(file)) files.push({ relativePath, revision: file.value.revision })
    }
    return { primaryRelativePath, files, directories }
  }

  /** @anchor entity-source-assembly */
  public parent(relativePath: string): string {
    return Option.getOrThrow(Option.fromNullishOr(this.parents.get(posix.dirname(relativePath))))
  }

  /** @anchor entity-source-assembly */
  public get claimed(): ReadonlySet<string> {
    return this.claimedRelativePaths
  }

  /** @anchor entity-source-assembly */
  public add(entity: LogicalEntity): void {
    this.entities.push(entity)
    for (const directory of entity.directories) {
      this.parents.set(directory.relativePath, entity.id)
    }
  }

  /** @anchor entity-source-assembly */
  public diagnose(
    kind: LogicalDiagnostic['kind'],
    relativePath: string,
    relatedRelativePaths: readonly string[],
    detail: string,
  ): void {
    this.diagnostics.push({ kind, relativePath, relatedRelativePaths, detail })
  }

  /** @anchor entity-source-assembly */
  public finish(diagnostics: readonly LogicalDiagnostic[]): LogicalEntitySnapshot {
    return new LogicalEntitySnapshot(this.entities, [...diagnostics, ...this.diagnostics])
  }

  /** @anchor entity-source-assembly */
  public error(relativePath: string, cause: unknown): void {
    this.diagnostics.push({
      kind: 'invalid-structure',
      relativePath,
      relatedRelativePaths: [],
      detail: String(cause),
    })
  }
}
