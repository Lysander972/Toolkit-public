import { Buffer } from 'node:buffer'
import { createHash } from 'node:crypto'
import { Cache, Context, Data, Duration, Effect, Exit, Fiber, Layer, Option, Scope } from 'effect'
import { CanvasConverter } from '@toolkit/canvas'
import type { CanvasDocument } from '@toolkit/canvas/model'
import type { FileEntity } from '@toolkit/project-model/entities'
import { ProjectEntitiesService } from '../../entities/ProjectEntities.ts'
import type { ProjectEntities } from '../../entities/ProjectEntities.ts'
import { CanvasEntity } from '../../entities/CanvasEntity.ts'
import type { EncodedEntityFile } from '../../entities/FormatEntity.ts'

/** @anchor canvas-service-contract */
export interface CanvasConversion {
  /** @anchor canvas-service-contract */
  toHtml(entity: CanvasEntity): Effect.Effect<string, unknown>
  /** @anchor canvas-service-contract */
  base64ToHtml(source: string): Effect.Effect<string, unknown>
  /** @anchor canvas-service-contract */
  hjsonToHtml(source: string): Effect.Effect<string, unknown>
  /** @anchor canvas-service-contract */
  fromHtml(html: string, baseline: CanvasEntity): Effect.Effect<CanvasEntity, unknown>
  /** @anchor canvas-service-contract */
  htmlToBase64(html: string): Effect.Effect<string, unknown>
  /** @anchor canvas-service-contract */
  htmlToHjson(html: string): Effect.Effect<string, unknown>
}

/** @anchor canvas-conversion-dispatch */
type CanvasDirection =
  'entity-html' | 'base64-html' | 'hjson-html' | 'html-entity' | 'html-base64' | 'html-hjson'

/** @anchor canvas-conversion-dispatch */
interface CanvasConversionInput {
  readonly direction: CanvasDirection
  readonly content: string
}

/** @anchor canvas-conversion-dispatch */
class CanvasConversionKey extends Data.Class<CanvasConversionInput> {
  declare private readonly nominal: never

  /** @anchor canvas-conversion-dispatch */
  public byteLength(): number {
    return Buffer.byteLength(JSON.stringify([this.direction, this.content]), 'utf8')
  }

  /** @anchor canvas-conversion-dispatch */
  public decode(): CanvasDocument {
    switch (this.direction) {
      case 'entity-html':
        return CanvasConverter.fromBinary(Buffer.from(this.content, 'base64'))
      case 'base64-html':
        if (!this.content.startsWith('canvas:')) throw new Error('需要原版 Base64 Canvas')
        return CanvasConverter.fromInline(this.content)
      case 'hjson-html':
        if (!this.content.startsWith('==hjson:')) throw new Error('需要原版 Hjson Canvas')
        return CanvasConverter.fromInline(this.content)
      case 'html-entity':
      case 'html-base64':
      case 'html-hjson':
        return CanvasConverter.fromHtml(this.content)
    }
  }

  /** @anchor canvas-conversion-dispatch */
  public encode(document: CanvasDocument): string {
    switch (this.direction) {
      case 'entity-html':
      case 'base64-html':
      case 'hjson-html':
        return CanvasConverter.toHtmlInline(document)
      case 'html-entity':
        return Buffer.from(CanvasConverter.toBinary(document)).toString('base64')
      case 'html-base64':
        return CanvasConverter.toInlineBase64(document)
      case 'html-hjson':
        return CanvasConverter.toInlineHjson(document)
    }
  }
}

/** @anchor canvas-conversion-dependencies */
class CanvasDependency {
  declare private readonly nominal: never

  /** @anchor canvas-conversion-dependencies */
  public constructor(
    public readonly guid: string,
    public readonly fingerprint: Option.Option<string>,
  ) {}
}

/** @anchor canvas-conversion-dependencies */
class CanvasCacheEntry {
  declare private readonly nominal: never

  /** @anchor canvas-conversion-dependencies */
  public constructor(
    public readonly text: string,
    public readonly dependencies: readonly CanvasDependency[],
  ) {}

  /** @anchor canvas-conversion-dependencies */
  public fingerprint(): string {
    return JSON.stringify(
      this.dependencies.map(
        (dependency: CanvasDependency): readonly [string, readonly string[]] => [
          dependency.guid,
          Option.toArray(dependency.fingerprint),
        ],
      ),
    )
  }

  /** @anchor canvas-conversion-dependencies */
  public byteLength(): number {
    return Buffer.byteLength(this.text, 'utf8') + Buffer.byteLength(this.fingerprint(), 'utf8')
  }

  /** @anchor canvas-conversion-dependencies */
  public static dependencies(
    guids: readonly string[],
    entities: ProjectEntities,
  ): Effect.Effect<readonly CanvasDependency[], unknown> {
    return Effect.forEach(
      guids,
      (guid: string): Effect.Effect<CanvasDependency, unknown> =>
        Effect.gen(function* (): Generator<
          Effect.Effect<unknown, unknown>,
          CanvasDependency,
          never
        > {
          const resolved: Option.Option<FileEntity> = yield* entities.resolve(guid)
          if (Option.isNone(resolved)) return new CanvasDependency(guid, Option.none())
          const content: Uint8Array = yield* entities.read(resolved.value)
          return new CanvasDependency(
            guid,
            Option.some(
              JSON.stringify([
                resolved.value.relativePath,
                createHash('sha256').update(content).digest('hex'),
              ]),
            ),
          )
        }),
      { concurrency: 4 },
    )
  }
}

/**
 * @anchor canvas-conversion-cache
 * @anchor workspace-canvas-cache-inventory
 */
class WorkspaceCanvasConversion implements CanvasConversion {
  private static readonly maximumBytes: number = 1048576
  private closed: boolean = false

  private constructor(
    private readonly results: Cache.Cache<CanvasConversionKey, CanvasCacheEntry, unknown>,
    private readonly entities: ProjectEntities,
    private readonly scope: Scope.Scope,
  ) {}

  /**
   * @anchor canvas-conversion-cache
   * @anchor workspace-canvas-cache-inventory
   * @anchor workspace-canvas-computation-shortcuts
   */
  public static make(
    entities: ProjectEntities,
  ): Effect.Effect<WorkspaceCanvasConversion, never, Scope.Scope> {
    return Effect.gen(function* (): Generator<
      Effect.Effect<unknown, never, Scope.Scope>,
      WorkspaceCanvasConversion,
      never
    > {
      const scope: Scope.Scope = yield* Scope.Scope
      const results: Cache.Cache<CanvasConversionKey, CanvasCacheEntry, unknown> =
        yield* Cache.makeWith(
          (key: CanvasConversionKey): Effect.Effect<CanvasCacheEntry, unknown> =>
            WorkspaceCanvasConversion.compute(key, entities),
          {
            capacity: 128,
            timeToLive: (
              exit: Exit.Exit<CanvasCacheEntry, unknown>,
              key: CanvasConversionKey,
            ): Duration.Duration =>
              Exit.isSuccess(exit) &&
              key.byteLength() + exit.value.byteLength() <= WorkspaceCanvasConversion.maximumBytes
                ? Duration.infinity
                : Duration.zero,
          },
        )
      return yield* Effect.acquireRelease(
        Effect.sync(
          (): WorkspaceCanvasConversion => new WorkspaceCanvasConversion(results, entities, scope),
        ),
        (service: WorkspaceCanvasConversion): Effect.Effect<void> => service.release(),
      )
    })
  }

  /** @anchor canvas-service-contract */
  public toHtml(entity: CanvasEntity): Effect.Effect<string, unknown> {
    return Effect.try({
      try: (): string => Buffer.from(CanvasConverter.toBinary(entity.document)).toString('base64'),
      catch: (cause: unknown): unknown => cause,
    }).pipe(
      Effect.flatMap((content: string): Effect.Effect<string, unknown> =>
        this.convert(new CanvasConversionKey({ direction: 'entity-html', content })),
      ),
    )
  }

  /** @anchor canvas-service-contract */
  public base64ToHtml(source: string): Effect.Effect<string, unknown> {
    return this.convert(new CanvasConversionKey({ direction: 'base64-html', content: source }))
  }

  /** @anchor canvas-service-contract */
  public hjsonToHtml(source: string): Effect.Effect<string, unknown> {
    return this.convert(new CanvasConversionKey({ direction: 'hjson-html', content: source }))
  }

  /** @anchor canvas-conversion-result */
  public fromHtml(html: string, baseline: CanvasEntity): Effect.Effect<CanvasEntity, unknown> {
    return this.convert(new CanvasConversionKey({ direction: 'html-entity', content: html })).pipe(
      Effect.flatMap((content: string): Effect.Effect<CanvasEntity, unknown> =>
        Effect.try({
          try: (): CanvasEntity =>
            new CanvasEntity(
              baseline.declaration,
              CanvasConverter.fromBinary(Buffer.from(content, 'base64')),
              baseline.originals.map((original: EncodedEntityFile): EncodedEntityFile => ({
                fileRelativePath: original.fileRelativePath,
                bytes: Uint8Array.from(original.bytes),
              })),
              baseline.baseline,
            ),
          catch: (cause: unknown): unknown => cause,
        }),
      ),
    )
  }

  /** @anchor canvas-service-contract */
  public htmlToBase64(html: string): Effect.Effect<string, unknown> {
    return this.convert(new CanvasConversionKey({ direction: 'html-base64', content: html }))
  }

  /** @anchor canvas-service-contract */
  public htmlToHjson(html: string): Effect.Effect<string, unknown> {
    return this.convert(new CanvasConversionKey({ direction: 'html-hjson', content: html }))
  }

  /**
   * @anchor workspace-canvas-cache-inventory
   * @anchor workspace-canvas-computation-shortcuts
   */
  private convert(key: CanvasConversionKey): Effect.Effect<string, unknown> {
    return Effect.suspend((): Effect.Effect<string, unknown> => {
      if (this.closed) return Effect.fail(new Error('Canvas 转换服务已关闭'))
      return Effect.gen({ self: this }, function* (this: WorkspaceCanvasConversion): Generator<
        Effect.Effect<unknown, unknown>,
        string,
        never
      > {
        if (key.byteLength() > WorkspaceCanvasConversion.maximumBytes)
          return (yield* WorkspaceCanvasConversion.compute(key, this.entities)).text
        const existing: Option.Option<CanvasCacheEntry> = yield* Cache.getOption(this.results, key)
        if (Option.isSome(existing)) {
          const current: readonly CanvasDependency[] = yield* CanvasCacheEntry.dependencies(
            existing.value.dependencies.map(
              (dependency: CanvasDependency): string => dependency.guid,
            ),
            this.entities,
          )
          if (
            new CanvasCacheEntry(existing.value.text, current).fingerprint() !==
            existing.value.fingerprint()
          )
            yield* Cache.invalidateWhen(
              this.results,
              key,
              (entry: CanvasCacheEntry): boolean => entry === existing.value,
            )
        }
        return (yield* Cache.get(this.results, key)).text
      }).pipe(
        Effect.forkIn(this.scope),
        Effect.flatMap((fiber: Fiber.Fiber<string, unknown>): Effect.Effect<string, unknown> =>
          Fiber.join(fiber).pipe(
            Effect.onInterrupt((): Effect.Effect<void> => Fiber.interrupt(fiber)),
          ),
        ),
      )
    })
  }

  /** @anchor workspace-canvas-cache-inventory */
  private release(): Effect.Effect<void> {
    this.closed = true
    return Cache.invalidateAll(this.results)
  }

  /** @anchor canvas-conversion-dispatch */
  private static compute(
    key: CanvasConversionKey,
    entities: ProjectEntities,
  ): Effect.Effect<CanvasCacheEntry, unknown> {
    return Effect.gen(function* (): Generator<
      Effect.Effect<unknown, unknown>,
      CanvasCacheEntry,
      never
    > {
      const document: CanvasDocument = yield* Effect.try({
        try: (): CanvasDocument => key.decode(),
        catch: (cause: unknown): unknown => cause,
      })
      const dependencies: readonly CanvasDependency[] = yield* CanvasCacheEntry.dependencies(
        CanvasConverter.resourceGuids(document),
        entities,
      )
      const text: string = yield* Effect.try({
        try: (): string => key.encode(document),
        catch: (cause: unknown): unknown => cause,
      })
      return new CanvasCacheEntry(text, dependencies)
    })
  }
}

/** @anchor canvas-service-contract */
export class CanvasConversionService extends Context.Service<
  CanvasConversionService,
  CanvasConversion
>()('toolkit/CanvasConversion') {
  private constructor(service: never) {
    super(service)
  }

  /** @anchor canvas-service-contract */
  public static layer(
    entities: Layer.Layer<ProjectEntitiesService, unknown>,
  ): Layer.Layer<CanvasConversionService, unknown> {
    return Layer.effect(
      CanvasConversionService,
      Effect.flatMap(
        ProjectEntitiesService,
        (service: ProjectEntities): Effect.Effect<WorkspaceCanvasConversion, never, Scope.Scope> =>
          WorkspaceCanvasConversion.make(service),
      ),
    ).pipe(Layer.provide(entities))
  }
}
