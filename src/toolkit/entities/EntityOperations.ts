import { randomUUID } from 'node:crypto'
import { posix } from 'node:path'
import { Effect, Option } from 'effect'
import { z } from 'zod'
import { CarrierIndex } from '@toolkit/project-model/carrier-index'
import { CanvasConverter } from '@toolkit/canvas'
import { DeviceMetadataSchema } from '@toolkit/script/device_metadata'
import { ScriptListSchema } from '@toolkit/script/script_list'
import type { DeviceMetadata } from '@toolkit/script/device_metadata'
import {
  LogicalEntity,
  LogicalRoot,
  LogicalGuidAsset,
  LogicalDevice,
  LogicalInlineScript,
  LogicalScriptGroup,
  LogicalCarrier,
  CreateContainer,
  CreateFileEntity,
  CreateCarrier,
  CreateDevice,
  UpdateBytes,
  UpdateScript,
  UpdateScriptList,
  UpdateDeviceFields,
  UpdateSettings,
  UpdateVariables,
  DeleteEntity,
  MoveEntity,
} from '@toolkit/project-model/logical-entities'
import type {
  LogicalEntitySnapshot,
  LogicalEntityOperation,
  LogicalScriptEntry,
  EntityFileDeclaration,
  EntityOperationScope,
} from '@toolkit/project-model/logical-entities'
import { ProjectFiles } from '../files/ProjectFiles.ts'
import { EntityChange } from './EntityChange.ts'
import { EntityFormats } from './EntityFormats.ts'

/** @anchor logical-entity-operations */
export class EntityOperations {
  private constructor(
    private readonly snapshot: LogicalEntitySnapshot,
    private readonly change: EntityChange,
  ) {}

  /** @anchor logical-entity-operations */
  public static prepare(
    files: ProjectFiles,
    snapshot: LogicalEntitySnapshot,
    operation: LogicalEntityOperation,
  ): Effect.Effect<EntityChange, unknown> {
    return Effect.gen(function* (): Generator<
      Effect.Effect<unknown, unknown>,
      EntityChange,
      never
    > {
      const scope: EntityOperationScope = yield* Effect.try({
        try: (): EntityOperationScope => snapshot.scope(operation),
        catch: (cause: unknown): unknown => cause,
      })
      const candidate: EntityChange = yield* EntityChange.open(
        files,
        scope.subject.primaryRelativePath,
        scope.files,
        scope.directories,
      )
      const operations: EntityOperations = new EntityOperations(snapshot, candidate)
      yield* Effect.try({
        try: (): void => {
          operations.apply(operation, scope.subject)
        },
        catch: (cause: unknown): unknown => cause,
      })
      return candidate
    })
  }

  private apply(operation: LogicalEntityOperation, subject: LogicalEntity): void {
    if (operation instanceof CreateDevice) this.createDevice(operation, subject)
    else if (operation instanceof CreateContainer) this.createContainer(operation, subject)
    else if (operation instanceof CreateFileEntity) this.createFile(operation, subject)
    else if (operation instanceof CreateCarrier) this.createCarrier(operation, subject)
    else if (operation instanceof DeleteEntity) this.remove(subject)
    else if (operation instanceof MoveEntity) this.move(operation, subject)
    else this.update(operation, subject)
  }

  private createDevice(operation: CreateDevice, parent: LogicalEntity): void {
    const carrier: LogicalCarrier = parent.require(LogicalCarrier)
    const deviceId: number = z.number().int().nonnegative().parse(operation.deviceId)
    const metadata: DeviceMetadata = DeviceMetadataSchema.read(operation.source)
    const directoryRelativePath: string = posix.join(
      carrier.derivedDirectoryRelativePath,
      String(deviceId),
    )
    this.snapshot.requireVacant(directoryRelativePath)
    const indexRelativePath: string = carrier.indexRelativePath
    const index: CarrierIndex = CarrierIndex.read(this.change.readText(indexRelativePath))
    index.add({ deviceId, deviceType: metadata.deviceType })
    this.change.createDirectory(directoryRelativePath)
    this.change.create(posix.join(directoryRelativePath, 'device.json'), operation.source)
    this.change.write(indexRelativePath, index.text())
  }

  private createContainer(operation: CreateContainer, parent: LogicalEntity): void {
    const relativePath: string = parent.createRelativePath(operation.kind, operation.name)
    this.snapshot.requireVacant(relativePath)
    if (operation.kind === 'script-group') {
      ScriptListSchema.read({ directoryName: operation.name, listText: '[]' })
      this.change.createDirectory(relativePath)
      this.change.create(posix.join(relativePath, 'list.json'), '[]')
      return
    }
    this.change.createDirectory(relativePath)
  }

  private createFile(operation: CreateFileEntity, parent: LogicalEntity): void {
    const relativePath: string = parent.createRelativePath(operation.kind, operation.name)
    this.snapshot.requireVacant(relativePath)
    if (operation.kind === 'inline-script') {
      const group: LogicalScriptGroup = parent.require(LogicalScriptGroup)
      this.change.create(
        relativePath,
        EntityFormats.script(new TextDecoder('utf-8', { fatal: true }).decode(operation.bytes)),
      )
      const entry: LogicalScriptEntry = {
        origin: 'inline',
        name: operation.name,
        guid: Option.none(),
        private: false,
        relativePath: Option.some(relativePath),
      }
      this.change.write(
        group.listRelativePath,
        EntityFormats.list(
          [...group.entries, entry],
          group.primaryRelativePath,
          this.change.readText(group.listRelativePath),
        ),
      )
      return
    }
    if (operation.kind === 'canvas') CanvasConverter.fromBinary(operation.bytes)
    const guid: string = randomUUID().replaceAll('-', '')
    this.snapshot.requireVacant(relativePath + '.guid')
    this.change.create(relativePath, operation.bytes)
    this.change.create(relativePath + '.guid', guid)
    if (operation.kind === 'code-repo-script')
      EntityFormats.script(new TextDecoder('utf-8', { fatal: true }).decode(operation.bytes))
  }

  private createCarrier(operation: CreateCarrier, parent: LogicalEntity): void {
    const relativePath: string = parent.createRelativePath(operation.kind, operation.name)
    this.snapshot.requireVacant(relativePath)
    this.snapshot.requireVacant(relativePath + '.guid')
    if (CarrierIndex.read(operation.indexSource).entries().length !== 0)
      throw new Error('新建载体索引必须为空，零件由创建操作添加')
    const directoryRelativePath: string = relativePath + '_'
    this.snapshot.requireVacant(directoryRelativePath)
    this.change.create(relativePath, operation.bytes)
    this.change.create(relativePath + '.guid', randomUUID().replaceAll('-', ''))
    this.change.createDirectory(directoryRelativePath)
    this.change.create(posix.join(directoryRelativePath, 'mech.json'), operation.indexSource)
    if (operation.kind === 'map')
      this.change.create(
        posix.join(directoryRelativePath, 'map.json'),
        Option.getOrThrow(operation.mapSource),
      )
    else if (Option.isSome(operation.mapSource)) throw new Error('机械不声明 map.json')
  }

  private update(operation: LogicalEntityOperation, subject: LogicalEntity): void {
    if (operation instanceof UpdateBytes) {
      if (!(subject instanceof LogicalGuidAsset) || subject.kind === 'code-repo-script')
        throw new Error('字节替换仅允许独立资产')
      if (subject.kind === 'canvas') CanvasConverter.fromBinary(operation.bytes)
      this.change.write(subject.primaryRelativePath, operation.bytes)
    } else if (operation instanceof UpdateScript) {
      if (subject.kind !== 'inline-script' && subject.kind !== 'code-repo-script')
        throw new Error('源码修改仅允许脚本实体')
      this.change.write(subject.primaryRelativePath, EntityFormats.script(operation.source))
    } else if (operation instanceof UpdateDeviceFields) {
      const device: LogicalDevice = subject.require(LogicalDevice)
      this.change.write(
        device.deviceJsonRelativePath,
        EntityFormats.device(this.change.readText(device.deviceJsonRelativePath), operation.fields),
      )
    } else if (operation instanceof UpdateScriptList) {
      const group: LogicalScriptGroup = subject.require(LogicalScriptGroup)
      const source: string = EntityFormats.list(
        operation.entries,
        group.primaryRelativePath,
        this.change.readText(group.listRelativePath),
      )
      for (const entry of operation.entries)
        if (Option.isSome(entry.relativePath)) {
          const relativePath: string = entry.relativePath.value
          if (
            !this.snapshot.entities.some(
              (entity: LogicalEntity): boolean =>
                entity.kind === 'inline-script' && entity.primaryRelativePath === relativePath,
            )
          )
            throw new Error('内联条目必须引用已有组内脚本')
        }
      for (const entry of group.entries)
        if (Option.isSome(entry.relativePath)) {
          const relativePath: string = entry.relativePath.value
          if (
            !operation.entries.some(
              (next: LogicalScriptEntry): boolean =>
                Option.isSome(next.relativePath) && next.relativePath.value === relativePath,
            )
          ) {
            const inline: LogicalEntity = this.snapshot.entity('inline-script:' + relativePath)
            for (const file of inline.files) this.change.delete(file.relativePath)
          }
        }
      this.change.write(group.listRelativePath, source)
    } else if (operation instanceof UpdateVariables) {
      const device: LogicalDevice = subject.require(LogicalDevice)
      const relativePath: string = device.variablesRelativePath
      const exists: boolean = device.files.some(
        (file: EntityFileDeclaration): boolean => file.relativePath === relativePath,
      )
      if (Option.isNone(operation.variables)) {
        if (exists) this.change.delete(relativePath)
      } else {
        const source: string = EntityFormats.variables(
          operation.variables.value,
          exists ? Option.some(this.change.readText(relativePath)) : Option.none(),
        )
        if (exists) this.change.write(relativePath, source)
        else this.change.create(relativePath, source)
      }
    } else if (operation instanceof UpdateSettings) {
      subject.require(LogicalRoot)
      this.change.write(
        'setting.json',
        EntityFormats.settings(
          this.change.readText('setting.json'),
          operation.name,
          operation.mainMap,
        ),
      )
    } else throw new Error('未知实体操作')
  }

  private remove(subject: LogicalEntity): void {
    if (subject instanceof LogicalDevice) {
      const carrier: LogicalCarrier = this.snapshot
        .entity(subject.parentEntityId)
        .require(LogicalCarrier)
      const relativePath: string = carrier.indexRelativePath
      const index: CarrierIndex = CarrierIndex.read(this.change.readText(relativePath))
      index.remove(subject.deviceId)
      this.change.write(relativePath, index.text())
    }
    if (subject instanceof LogicalInlineScript) {
      const group: LogicalScriptGroup = this.snapshot
        .entity(subject.parentEntityId)
        .require(LogicalScriptGroup)
      this.change.write(
        group.listRelativePath,
        EntityFormats.list(
          group.entries.filter(
            (entry: LogicalScriptEntry): boolean =>
              Option.isNone(entry.relativePath) ||
              entry.relativePath.value !== subject.primaryRelativePath,
          ),
          group.primaryRelativePath,
          this.change.readText(group.listRelativePath),
        ),
      )
    }
    for (const entity of this.snapshot.subtree(subject)) {
      for (const file of entity.files) this.change.delete(file.relativePath)
      for (const directory of entity.directories)
        this.change.deleteDirectory(directory.relativePath)
    }
  }

  private move(operation: MoveEntity, subject: LogicalEntity): void {
    const parent: LogicalEntity = this.snapshot.entity(operation.parentId)
    const targetRelativePath: string = subject.moveRelativePath(
      parent,
      operation.name,
      this.snapshot,
    )
    this.snapshot.requireVacant(targetRelativePath)
    if (subject instanceof LogicalInlineScript) {
      const group: LogicalScriptGroup = parent.require(LogicalScriptGroup)
      this.change.rename(subject.primaryRelativePath, targetRelativePath)
      const entries: LogicalScriptEntry[] = group.entries.map(
        (entry: LogicalScriptEntry): LogicalScriptEntry =>
          Option.isSome(entry.relativePath) &&
          entry.relativePath.value === subject.primaryRelativePath
            ? { ...entry, name: operation.name, relativePath: Option.some(targetRelativePath) }
            : entry,
      )
      this.change.write(
        group.listRelativePath,
        EntityFormats.list(
          entries,
          group.primaryRelativePath,
          this.change.readText(group.listRelativePath),
        ),
      )
      return
    }
    if (subject instanceof LogicalGuidAsset)
      this.snapshot.requireVacant(targetRelativePath + '.guid')
    for (const entity of this.snapshot.subtree(subject)) {
      for (const directory of entity.directories) {
        const nextRelativePath: string = posix.join(
          targetRelativePath,
          posix.relative(subject.primaryRelativePath, directory.relativePath),
        )
        this.change.createDirectory(nextRelativePath)
        this.change.deleteDirectory(directory.relativePath)
      }
      for (const file of entity.files)
        this.change.rename(
          file.relativePath,
          subject instanceof LogicalGuidAsset
            ? file.relativePath === subject.guidRelativePath
              ? targetRelativePath + '.guid'
              : targetRelativePath
            : posix.join(
                targetRelativePath,
                posix.relative(subject.primaryRelativePath, file.relativePath),
              ),
        )
    }
  }
}
