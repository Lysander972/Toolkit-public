import type { EntityFileMetadata } from '@toolkit/project-model/entities'

/** @anchor workspace-file-paths */
export interface FileMetadata extends EntityFileMetadata {
  readonly size: bigint
}
