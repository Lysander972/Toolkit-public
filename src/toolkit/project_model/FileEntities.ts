import Hjson from 'hjson'
import { z } from 'zod'
import type { Option } from 'effect'
import { GuidFormat } from './GuidFormat.ts'

/** @anchor entity-file-identity */
export interface FileEntity {
  readonly guid: string
  readonly relativePath: string
  readonly revision: string
  readonly guidRelativePath: string
  readonly guidRevision: string
}

/** @anchor entity-file-identity */
export type EntityDiagnosticKind =
  'missing-guid' | 'orphan-guid' | 'invalid-guid' | 'duplicate-guid'

/** @anchor entity-file-identity */
export interface EntityDiagnostic {
  readonly kind: EntityDiagnosticKind
  readonly relativePath: string
  readonly guid: Option.Option<string>
  readonly relatedRelativePaths: readonly string[]
  readonly detail: string
}

/** @anchor entity-file-identity */
export interface EntitySnapshot {
  readonly entities: readonly FileEntity[]
  readonly diagnostics: readonly EntityDiagnostic[]
  readonly directories: readonly EntityDirectoryListing[]
}

/** @anchor entity-file-identity */
export interface EntityFileMetadata {
  readonly relativePath: string
  readonly kind: EntityFileKind
  readonly revision: string
}

/** @anchor entity-file-identity */
export type EntityFileKind = 'file' | 'directory'

/** @anchor entity-file-identity */
export interface EntityDirectoryListing {
  readonly relativePath: string
  readonly entries: readonly EntityFileMetadata[]
}

/** @anchor entity-guid-format */
interface GuidMetadata {
  readonly guid: string
}

/** @anchor entity-guid-format */
export class GuidMetadataParser {
  private static readonly schema: z.ZodType<GuidMetadata> = z.object({
    guid: z.string().refine(GuidFormat.validate),
  })

  private constructor() {}

  /** @anchor entity-guid-format */
  public static parse(source: string): string {
    if (GuidFormat.validate(source)) return source
    return GuidMetadataParser.schema.parse(Hjson.parse(source)).guid
  }
}
