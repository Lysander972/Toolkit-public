import { posix } from 'node:path'
import { Effect, Option, Result } from 'effect'
import { CarrierIndex } from '@toolkit/project-model/carrier-index'
import { ProjectSettingsParser } from '@toolkit/project-model'
import type { ProjectSettings } from '@toolkit/project-model'
import { GlobalVariableSchema } from '@toolkit/script/global_variables'
import type { LogicalVariable } from '@toolkit/project-model/logical-entities'
import type { CarrierDeviceEntry } from '@toolkit/project-model/carrier-index'
import { DeviceMetadataSchema } from '@toolkit/script/device_metadata'
import type { DeviceMetadata } from '@toolkit/script/device_metadata'
import { ScriptListSchema } from '@toolkit/script/script_list'
import type { ScriptEntry, ScriptGroup } from '@toolkit/script/script_list'
import type {
  EntitySnapshot,
  FileEntity,
  EntityDiagnostic,
  EntityFileMetadata,
} from '@toolkit/project-model/entities'
import type {
  LogicalDiagnostic,
  EntityDeclaration,
  ChildEntityDeclaration,
  LogicalScriptEntry,
  CarrierKind,
  GuidEntityKind,
} from '@toolkit/project-model/logical-entities'
import {
  LogicalCarrier,
  LogicalDevice,
  LogicalEntity,
  LogicalScriptGroup,
  LogicalRoot,
  LogicalDirectory,
  LogicalSpine,
  LogicalGuidAsset,
  LogicalRawFile,
  LogicalInlineScript,
  LogicalGuidEntity,
  LogicalEntitySnapshot,
} from '@toolkit/project-model/logical-entities'
import type { DeviceDeclaration } from '@toolkit/project-model/logical-entities'
import type { FileMetadata } from '../files/FileContracts.ts'
import { ProjectFiles } from '../files/ProjectFiles.ts'
import type { SemanticReadInput } from './EntityReadBaseline.ts'
import { EntityAssembly } from './EntityAssembly.ts'

/** @anchor entity-source-assembly */
export class LogicalEntitySources {
  declare private readonly nominal: never
  /** @anchor entity-source-assembly */
  public constructor(private readonly files: ProjectFiles) {}

  /** @anchor semantic-assembly-contract */
  public semanticInput(snapshot: LogicalEntitySnapshot): Effect.Effect<SemanticReadInput, unknown> {
    return Effect.fail(
      new Error(
        `TODO: 为逻辑快照中的 ${String(snapshot.entities.length)} 项实体读取仓库与内联脚本、平台输入并建立纯分析及完整依赖基准`,
      ),
    )
  }

  /** @anchor entity-source-assembly */
  public load(raw: EntitySnapshot): Effect.Effect<LogicalEntitySnapshot, unknown> {
    return Effect.gen({ self: this }, function* (this: LogicalEntitySources): Generator<
      Effect.Effect<unknown, unknown>,
      LogicalEntitySnapshot,
      never
    > {
      const listings: Map<string, readonly EntityFileMetadata[]> = new Map()
      const files: Map<string, EntityFileMetadata> = new Map()
      for (const directory of raw.directories) {
        listings.set(directory.relativePath, directory.entries)
        for (const entry of directory.entries)
          if (entry.kind === 'file') files.set(entry.relativePath, entry)
      }
      const assembly: EntityAssembly = new EntityAssembly(listings, files)
      const settings: Option.Option<ProjectSettings> = files.has('setting.json')
        ? yield* this.settings(assembly, files)
        : Option.none()
      assembly.add(
        new LogicalRoot(
          assembly.declare('.', [
            '.',
            ...['setting.json', 'setting.json.guid'].filter((relativePath: string): boolean =>
              files.has(relativePath),
            ),
          ]),
          settings,
        ),
      )
      for (const directoryRelativePath of [...listings.keys()].sort(
        (left: string, right: string): number => left.split('/').length - right.split('/').length,
      )) {
        if (directoryRelativePath !== '.' && !assembly.claimed.has(directoryRelativePath)) {
          const spine: boolean = directoryRelativePath.endsWith('.spine_')
          const declaration: ChildEntityDeclaration = {
            ...assembly.declare(directoryRelativePath, [directoryRelativePath]),
            parentId: assembly.parent(directoryRelativePath),
          }
          const directory: LogicalEntity = spine
            ? new LogicalSpine(declaration)
            : new LogicalDirectory(declaration)
          assembly.add(directory)
        }
        for (const asset of raw.entities.filter(
          (file: FileEntity): boolean => posix.dirname(file.relativePath) === directoryRelativePath,
        )) {
          if (assembly.claimed.has(asset.relativePath)) continue
          const kind: GuidEntityKind = LogicalGuidEntity.kind(asset.relativePath)
          if (kind === 'map' || kind === 'mech') yield* this.carrier(asset, kind, assembly, files)
          else {
            assembly.add(
              new LogicalGuidAsset({
                ...assembly.declare(asset.relativePath, [
                  asset.relativePath,
                  asset.guidRelativePath,
                ]),
                kind,
                parentId: assembly.parent(asset.relativePath),
                guid: asset.guid,
              }),
            )
          }
        }
      }
      for (const file of files.values()) {
        if (assembly.claimed.has(file.relativePath)) continue
        assembly.add(
          new LogicalRawFile({
            ...assembly.declare(file.relativePath, [file.relativePath]),
            parentId: assembly.parent(file.relativePath),
          }),
        )
      }
      return assembly.finish(
        raw.diagnostics.map((diagnostic: EntityDiagnostic): LogicalDiagnostic => ({
          kind: diagnostic.kind,
          relativePath: diagnostic.relativePath,
          relatedRelativePaths: diagnostic.relatedRelativePaths,
          detail: diagnostic.detail,
        })),
      )
    })
  }

  /**
   * @anchor logical-entity-sources
   * @fallback 载体缺少索引时用空条目继续装配载体并发布缺失索引诊断。
   */
  private carrier(
    asset: FileEntity,
    kind: CarrierKind,
    assembly: EntityAssembly,
    files: ReadonlyMap<string, EntityFileMetadata>,
  ): Effect.Effect<void, unknown> {
    return Effect.gen({ self: this }, function* (this: LogicalEntitySources): Generator<
      Effect.Effect<unknown, unknown>,
      void,
      never
    > {
      const derivedDirectoryRelativePath: string = asset.relativePath + '_'
      const indexRelativePath: string = posix.join(derivedDirectoryRelativePath, 'mech.json')
      const mapRelativePath: string = posix.join(derivedDirectoryRelativePath, 'map.json')
      const declared: readonly string[] = [
        asset.relativePath,
        asset.guidRelativePath,
        ...[derivedDirectoryRelativePath, indexRelativePath, mapRelativePath].filter(
          (relativePath: string): boolean =>
            assembly.listings.has(relativePath) || files.has(relativePath),
        ),
      ]
      const declaration: EntityDeclaration = assembly.declare(asset.relativePath, declared)
      const source: Option.Option<string> = files.has(indexRelativePath)
        ? Option.some(yield* this.text(indexRelativePath, files))
        : Option.none()
      const result: Result.Result<readonly CarrierDeviceEntry[], unknown> = Option.isSome(source)
        ? yield* Effect.result(
            Effect.try({
              try: (): readonly CarrierDeviceEntry[] => CarrierIndex.read(source.value).entries(),
              catch: (cause: unknown): unknown => cause,
            }),
          )
        : Result.fail(new Error(`载体缺少索引: ${indexRelativePath}`))
      const entries: readonly CarrierDeviceEntry[] = Result.isSuccess(result) ? result.success : []
      if (Result.isFailure(result)) assembly.error(indexRelativePath, result.failure)
      const carrier: LogicalCarrier = new LogicalCarrier({
        ...declaration,
        kind,
        parentId: assembly.parent(asset.relativePath),
        guid: asset.guid,
      })
      assembly.add(carrier)
      for (const entry of entries) yield* this.device(carrier, entry, assembly, files)
      const listing: Option.Option<readonly EntityFileMetadata[]> = Option.fromNullishOr(
        assembly.listings.get(derivedDirectoryRelativePath),
      )
      if (Option.isSome(listing))
        for (const child of listing.value) {
          if (
            child.kind === 'directory' &&
            LogicalEntitySources.isDeviceDirectory(child.relativePath) &&
            !entries.some(
              (entry: CarrierDeviceEntry): boolean =>
                String(entry.deviceId) === posix.basename(child.relativePath),
            )
          )
            assembly.diagnose(
              'unindexed-device',
              child.relativePath,
              [indexRelativePath],
              '零件目录未在索引中声明',
            )
        }
    })
  }

  /** @anchor logical-entity-sources */
  private device(
    carrier: LogicalCarrier,
    entry: CarrierDeviceEntry,
    assembly: EntityAssembly,
    files: ReadonlyMap<string, EntityFileMetadata>,
  ): Effect.Effect<void, unknown> {
    return Effect.gen({ self: this }, function* (this: LogicalEntitySources): Generator<
      Effect.Effect<unknown, unknown>,
      void,
      never
    > {
      const relativePath: string = posix.join(
        carrier.derivedDirectoryRelativePath,
        String(entry.deviceId),
      )
      const deviceJsonRelativePath: string = posix.join(relativePath, 'device.json')
      if (!assembly.listings.has(relativePath) || !files.has(deviceJsonRelativePath)) {
        assembly.diagnose(
          'missing-device',
          relativePath,
          [deviceJsonRelativePath],
          '索引零件缺少目录或 device.json',
        )
        return
      }
      const source: string = yield* this.text(deviceJsonRelativePath, files)
      const parsed: Result.Result<DeviceMetadata, unknown> = yield* Effect.result(
        Effect.try({
          try: (): DeviceMetadata => DeviceMetadataSchema.read(source),
          catch: (cause: unknown): unknown => cause,
        }),
      )
      if (Result.isFailure(parsed)) {
        assembly.error(deviceJsonRelativePath, parsed.failure)
        return
      }
      const metadata: DeviceMetadata = parsed.success
      if (metadata.deviceType !== entry.deviceType)
        assembly.diagnose(
          'invalid-device',
          deviceJsonRelativePath,
          [posix.join(carrier.derivedDirectoryRelativePath, 'mech.json')],
          '零件类型与索引不一致',
        )
      const varsRelativePath: string = posix.join(relativePath, 'vars.json')
      const variables: Option.Option<readonly LogicalVariable[]> = files.has(varsRelativePath)
        ? yield* this.variables(varsRelativePath, assembly, files)
        : Option.none()
      const declaration: DeviceDeclaration = {
        ...assembly.declare(relativePath, [
          relativePath,
          deviceJsonRelativePath,
          ...[varsRelativePath].filter((varsFileRelativePath: string): boolean =>
            files.has(varsFileRelativePath),
          ),
        ]),
        parentId: carrier.id,
        deviceId: entry.deviceId,
        deviceType: metadata.deviceType,
        variables,
        fields: {
          logicName: metadata.logicName,
          mechFileGuid: metadata.mechFileGuid,
          stickToBone: metadata.stickToBone,
        },
      }
      const device: LogicalDevice = new LogicalDevice(declaration)
      for (const child of Option.getOrThrow(
        Option.fromNullishOr(assembly.listings.get(relativePath)),
      )) {
        const name: string = posix.basename(child.relativePath)
        if (child.kind === 'directory' && (name === 'scripts' || name.startsWith('event_'))) {
          yield* this.group(child.relativePath, device, assembly, files)
        }
      }
      assembly.add(device)
    })
  }

  /** @anchor logical-script-sources */
  private group(
    relativePath: string,
    device: LogicalDevice,
    assembly: EntityAssembly,
    files: ReadonlyMap<string, EntityFileMetadata>,
  ): Effect.Effect<void, unknown> {
    return Effect.gen({ self: this }, function* (this: LogicalEntitySources): Generator<
      Effect.Effect<unknown, unknown>,
      void,
      never
    > {
      const listRelativePath: string = posix.join(relativePath, 'list.json')
      if (!files.has(listRelativePath)) {
        assembly.diagnose('missing-file', listRelativePath, [], '脚本清单缺失，保留原始目录')
        return
      }
      const source: string = yield* this.text(listRelativePath, files)
      const parsed: Result.Result<ScriptGroup<ScriptEntry>, unknown> = yield* Effect.result(
        Effect.try({
          try: (): ScriptGroup<ScriptEntry> =>
            ScriptListSchema.read({
              directoryName: posix.basename(relativePath),
              listText: source,
            }),
          catch: (cause: unknown): unknown => cause,
        }),
      )
      if (Result.isFailure(parsed)) {
        assembly.error(listRelativePath, parsed.failure)
        return
      }
      const entries: LogicalScriptEntry[] = parsed.success.entries.map(
        (entry: ScriptEntry): LogicalScriptEntry => ({
          name: entry.name,
          origin: entry.origin,
          guid: entry.guid,
          private: entry.isPrivate,
          relativePath:
            entry.origin === 'inline'
              ? Option.some(posix.join(relativePath, entry.name + '.code'))
              : Option.none(),
        }),
      )
      const group: LogicalScriptGroup = new LogicalScriptGroup({
        ...assembly.declare(relativePath, [relativePath, listRelativePath]),
        parentId: device.id,
        eventName: parsed.success.eventName,
        entries,
      })
      assembly.add(group)
      for (const entry of entries) {
        if (Option.isNone(entry.relativePath)) continue
        const sourceRelativePath: string = entry.relativePath.value
        if (!files.has(sourceRelativePath)) {
          assembly.diagnose(
            'missing-file',
            sourceRelativePath,
            [listRelativePath],
            '清单声明的内联脚本不存在',
          )
          continue
        }
        if (assembly.claimed.has(sourceRelativePath)) {
          assembly.error(sourceRelativePath, new Error('脚本清单重复名称'))
          continue
        }
        assembly.add(
          new LogicalInlineScript({
            ...assembly.declare(sourceRelativePath, [sourceRelativePath]),
            parentId: group.id,
          }),
        )
      }
    })
  }

  private text(
    relativePath: string,
    files: ReadonlyMap<string, EntityFileMetadata>,
  ): Effect.Effect<string, unknown> {
    return Effect.gen({ self: this }, function* (this: LogicalEntitySources): Generator<
      Effect.Effect<unknown, unknown>,
      string,
      never
    > {
      const baseline: EntityFileMetadata = Option.getOrThrow(
        Option.fromNullishOr(files.get(relativePath)),
      )
      const before: FileMetadata = yield* this.files.metadata(relativePath)
      if (baseline.revision !== before.revision)
        return yield* Effect.fail(new Error(`装配期间结构文件变化: ${relativePath}`))
      const bytes: Uint8Array = yield* this.files.read(relativePath)
      const after: FileMetadata = yield* this.files.metadata(relativePath)
      if (baseline.revision !== after.revision)
        return yield* Effect.fail(new Error(`装配期间结构文件变化: ${relativePath}`))
      return new TextDecoder('utf-8', { fatal: true }).decode(bytes)
    })
  }

  /** @anchor logical-structure-read */
  private settings(
    assembly: EntityAssembly,
    files: ReadonlyMap<string, EntityFileMetadata>,
  ): Effect.Effect<Option.Option<ProjectSettings>, unknown> {
    return Effect.gen({ self: this }, function* (this: LogicalEntitySources): Generator<
      Effect.Effect<unknown, unknown>,
      Option.Option<ProjectSettings>,
      never
    > {
      const source: string = yield* this.text('setting.json', files)
      const result: Result.Result<ProjectSettings, unknown> = yield* Effect.result(
        Effect.try({
          try: (): ProjectSettings => ProjectSettingsParser.parse(source),
          catch: (cause: unknown): unknown => cause,
        }),
      )
      if (Result.isFailure(result)) {
        assembly.error('setting.json', result.failure)
        return Option.none()
      }
      return Option.some(Object.freeze(result.success))
    })
  }

  /** @anchor logical-structure-read */
  private variables(
    relativePath: string,
    assembly: EntityAssembly,
    files: ReadonlyMap<string, EntityFileMetadata>,
  ): Effect.Effect<Option.Option<readonly LogicalVariable[]>, unknown> {
    return Effect.gen({ self: this }, function* (this: LogicalEntitySources): Generator<
      Effect.Effect<unknown, unknown>,
      Option.Option<readonly LogicalVariable[]>,
      never
    > {
      const source: string = yield* this.text(relativePath, files)
      const result: Result.Result<readonly LogicalVariable[], unknown> = yield* Effect.result(
        Effect.try({
          try: (): readonly LogicalVariable[] => GlobalVariableSchema.read(source),
          catch: (cause: unknown): unknown => cause,
        }),
      )
      if (Result.isFailure(result)) {
        assembly.error(relativePath, result.failure)
        return Option.none()
      }
      return Option.some(result.success)
    })
  }

  /** @anchor logical-entity-sources */
  private static isDeviceDirectory(relativePath: string): boolean {
    const name: string = posix.basename(relativePath)
    const value: number = Number(name)
    return Number.isInteger(value) && value >= 0 && String(value) === name
  }
}
