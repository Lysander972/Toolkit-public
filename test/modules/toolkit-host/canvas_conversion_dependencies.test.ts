import assert from 'node:assert/strict'
import { test, vi } from 'vitest'
import type { MockInstance } from 'vitest'
import { Cause, Context, Deferred, Effect, Exit, Fiber, Layer, Option, Scope } from 'effect'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import { CanvasConversionService } from '../../../src/toolkit/conversion/canvas/CanvasConversion.ts'
import type { CanvasConversion } from '../../../src/toolkit/conversion/canvas/CanvasConversion.ts'
import { ProjectEntitiesService } from '../../../src/toolkit/entities/ProjectEntities.ts'
import type { ProjectEntities } from '../../../src/toolkit/entities/ProjectEntities.ts'
import type { EntitySnapshot, FileEntity } from '../../../src/toolkit/project_model/FileEntities.ts'
import { MoveEntity, DeleteEntity } from '../../../src/toolkit/project_model/LogicalEntities.ts'
import type {
  LogicalEntity,
  LogicalEntitySnapshot,
} from '../../../src/toolkit/project_model/LogicalEntities.ts'
import type { FileTestWorkspace } from '../toolkit-file-support/FileTestWorkspace.ts'
import { CanvasTestWorkspace } from '../toolkit-file-support/CanvasTestWorkspace.ts'

/**
 * @anchor canvas-conversion-dependencies
 * @anchor workspace-canvas-computation-shortcuts
 */
export class CanvasFileDependencyTests {
  private constructor() {}

  static {
    test('真实图片与Spine资源内容变化失效，同内容与无关改写命中，引用去重', async (): Promise<void> => {
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
            const service: CanvasConversion = fixture.service
            const entities: ProjectEntities = fixture.entities
            const workspace: FileTestWorkspace = fixture.workspace
            const relativePaths: readonly string[] = [
              'picture.png',
              'mask.png',
              'texture.png',
              'animation.atlas',
              'animation.skel',
              'animation.json',
            ]
            const guids: readonly [string, string, string, string, string, string] = [
              '1'.repeat(32),
              '2'.repeat(32),
              '3'.repeat(32),
              '4'.repeat(32),
              '5'.repeat(32),
              '6'.repeat(32),
            ]
            const [picture, mask, texture, atlas, skeleton, json]: typeof guids = guids
            yield* Effect.tryPromise({
              try: async (): Promise<void> => {
                for (const [index, relativePath] of relativePaths.entries()) {
                  await workspace.write(relativePath, Uint8Array.of(index, 1))
                  await workspace.write(
                    relativePath + '.guid',
                    Option.getOrThrow(Option.fromNullishOr(guids[index])),
                  )
                }
              },
              catch: (cause: unknown): unknown => cause,
            })
            yield* entities.refresh(['.'])
            assert.equal((yield* entities.snapshot()).entities.length, 6)
            const html: string = `<BeCanvas name="资源" width="1080" height="2160"><BeUIRawImage name="图片" fileguid="${picture}" softmaskguid="${mask}"></BeUIRawImage><BeUISpine name="动画" pngguids="${texture},${texture}" atlasguid="${atlas}" skelguid="${skeleton}" jsonguid="${json}"></BeUISpine></BeCanvas>`
            const expected: string = CanvasConverter.toInlineBase64(CanvasConverter.fromHtml(html))
            const parser: MockInstance<typeof CanvasConverter.fromHtml> = vi.spyOn(
              CanvasConverter,
              'fromHtml',
            )
            const read: MockInstance<typeof entities.read> = vi.spyOn(entities, 'read')
            assert.equal(yield* service.htmlToBase64(html), expected)
            assert.equal(yield* service.htmlToBase64(html), expected)
            assert.equal(parser.mock.calls.length, 1)
            assert.equal(read.mock.calls.length, 12)
            for (const [index, relativePath] of relativePaths.entries()) {
              const before: FileEntity = Option.getOrThrow(
                yield* entities.resolve(Option.getOrThrow(Option.fromNullishOr(guids[index]))),
              )
              yield* Effect.tryPromise({
                try: (): Promise<void> => workspace.write(relativePath, Uint8Array.of(index, 2)),
                catch: (cause: unknown): unknown => cause,
              })
              yield* entities.refresh(['.'])
              assert.equal(
                (yield* entities.snapshot()).entities.some(
                  (entity: FileEntity): boolean =>
                    entity.relativePath === relativePath && entity.revision !== before.revision,
                ),
                true,
              )
              assert.equal(yield* service.htmlToBase64(html), expected)
              assert.equal(parser.mock.calls.length, index + 2)
            }
            const before: FileEntity = Option.getOrThrow(yield* entities.resolve('1'.repeat(32)))
            yield* Effect.tryPromise({
              try: (): Promise<void> => workspace.write('picture.png', Uint8Array.of(0, 2)),
              catch: (cause: unknown): unknown => cause,
            })
            yield* entities.refresh(['.'])
            assert.equal(
              (yield* entities.snapshot()).entities.some(
                (entity: FileEntity): boolean =>
                  entity.relativePath === 'picture.png' && entity.revision !== before.revision,
              ),
              true,
            )
            assert.equal(yield* service.htmlToBase64(html), expected)
            const snapshot: EntitySnapshot = yield* entities.snapshot()
            yield* Effect.tryPromise({
              try: (): Promise<void> => workspace.write('unrelated.txt', '无关'),
              catch: (cause: unknown): unknown => cause,
            })
            yield* entities.refresh(['.'])
            const current: EntitySnapshot = yield* entities.snapshot()
            assert.notEqual(current, snapshot)
            assert.equal(
              current.directories.some(
                (directory: EntitySnapshot['directories'][number]): boolean =>
                  directory.entries.some(
                    (entry: EntitySnapshot['directories'][number]['entries'][number]): boolean =>
                      entry.relativePath === 'unrelated.txt',
                  ),
              ),
              true,
            )
            assert.equal(yield* service.htmlToBase64(html), expected)
            assert.equal(parser.mock.calls.length, 7)
          }),
        ),
      )
    })

    test('身份出现、移动、消失改变指纹，重复GUID和过期基准直接失败', async (): Promise<void> => {
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
            const service: CanvasConversion = fixture.service
            const entities: ProjectEntities = fixture.entities
            const workspace: FileTestWorkspace = fixture.workspace
            const guid: string = '1'.repeat(32)
            const html: string = `<BeCanvas name="身份" width="1080" height="2160"><BeUIRawImage name="图片" fileguid="${guid}"></BeUIRawImage></BeCanvas>`
            const parser: MockInstance<typeof CanvasConverter.fromHtml> = vi.spyOn(
              CanvasConverter,
              'fromHtml',
            )
            yield* service.htmlToBase64(html)
            yield* Effect.tryPromise({
              try: async (): Promise<void> => {
                await workspace.write('image.png', Uint8Array.of(1))
                await workspace.write('image.png.guid', guid)
              },
              catch: (cause: unknown): unknown => cause,
            })
            yield* entities.refresh(['.'])
            assert.equal(
              (yield* entities.snapshot()).entities.some(
                (entity: FileEntity): boolean => entity.guid === guid,
              ),
              true,
            )
            yield* service.htmlToBase64(html)
            assert.equal(parser.mock.calls.length, 2)
            const baseline: FileEntity = Option.getOrThrow(yield* entities.resolve(guid))
            yield* entities.commit(yield* entities.prepare(baseline, Uint8Array.of(2)))
            const stale: Exit.Exit<Uint8Array, unknown> = yield* Effect.exit(
              entities.read(baseline),
            )
            assert.ok(Exit.isFailure(stale))
            vi.spyOn(entities, 'resolve').mockReturnValueOnce(Effect.succeedSome(baseline))
            assert.ok(Exit.isFailure(yield* Effect.exit(service.htmlToBase64(html))))
            yield* service.htmlToBase64(html)
            assert.equal(parser.mock.calls.length, 3)
            const logical: LogicalEntitySnapshot = yield* entities.logicalSnapshot()
            const image: LogicalEntity = Option.getOrThrow(
              Option.fromNullishOr(
                logical.entities.find(
                  (entity: LogicalEntity): boolean => entity.primaryRelativePath === 'image.png',
                ),
              ),
            )
            yield* entities.commit(
              yield* entities.prepareOperation(
                logical,
                new MoveEntity(image.id, 'root', 'renamed.png'),
              ),
            )
            yield* service.htmlToBase64(html)
            assert.equal(parser.mock.calls.length, 4)
            yield* Effect.tryPromise({
              try: async (): Promise<void> => {
                await workspace.write('duplicate.png', Uint8Array.of(2))
                await workspace.write('duplicate.png.guid', guid)
              },
              catch: (cause: unknown): unknown => cause,
            })
            yield* entities.refresh(['.'])
            assert.equal(
              (yield* entities.snapshot()).diagnostics.some(
                (diagnostic: EntitySnapshot['diagnostics'][number]): boolean =>
                  diagnostic.kind === 'duplicate-guid',
              ),
              true,
            )
            assert.ok(Exit.isFailure(yield* Effect.exit(service.htmlToBase64(html))))
            yield* Effect.tryPromise({
              try: (): Promise<void> => workspace.write('duplicate.png.guid', '9'.repeat(32)),
              catch: (cause: unknown): unknown => cause,
            })
            yield* entities.refresh(['.'])
            const snapshot: EntitySnapshot = yield* entities.snapshot()
            assert.equal(
              snapshot.entities.some(
                (entity: FileEntity): boolean => entity.guid === '9'.repeat(32),
              ),
              true,
            )
            assert.equal(
              snapshot.diagnostics.every(
                (diagnostic: EntitySnapshot['diagnostics'][number]): boolean =>
                  diagnostic.kind !== 'duplicate-guid',
              ),
              true,
            )
            const resolvedSnapshot: LogicalEntitySnapshot = yield* entities.logicalSnapshot()
            const duplicate: LogicalEntity = Option.getOrThrow(
              Option.fromNullishOr(
                resolvedSnapshot.entities.find(
                  (entity: LogicalEntity): boolean =>
                    entity.primaryRelativePath === 'duplicate.png',
                ),
              ),
            )
            yield* entities.commit(
              yield* entities.prepareOperation(resolvedSnapshot, new DeleteEntity(duplicate.id)),
            )
            const finalSnapshot: LogicalEntitySnapshot = yield* entities.logicalSnapshot()
            const renamed: LogicalEntity = Option.getOrThrow(
              Option.fromNullishOr(
                finalSnapshot.entities.find(
                  (entity: LogicalEntity): boolean => entity.primaryRelativePath === 'renamed.png',
                ),
              ),
            )
            yield* entities.commit(
              yield* entities.prepareOperation(finalSnapshot, new DeleteEntity(renamed.id)),
            )
            yield* service.htmlToBase64(html)
            assert.equal(parser.mock.calls.length, 5)
          }),
        ),
      )
    })
  }
}

/** @anchor canvas-conversion-dependencies */
export class CanvasDependencyFailureTests {
  private constructor() {}

  static {
    test('依赖核对保留原始IO错误，失败不交付旧结果，原生Scope关闭中断核对', async (): Promise<void> => {
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
            const service: CanvasConversion = fixture.service
            const entities: ProjectEntities = fixture.entities
            const html: string = `<BeCanvas name="失败" width="1080" height="2160"><BeUIRawImage name="图片" fileguid="${'1'.repeat(32)}"></BeUIRawImage></BeCanvas>`
            const expected: string = yield* service.htmlToBase64(html)
            const error: Error = new Error('实体读取IO错误')
            const resolve: MockInstance<typeof entities.resolve> = vi.spyOn(entities, 'resolve')
            resolve.mockReturnValueOnce(Effect.fail(error))
            const failed: Exit.Exit<string, unknown> = yield* Effect.exit(
              service.htmlToBase64(html),
            )
            assert.ok(Exit.isFailure(failed))
            assert.equal(Option.getOrThrow(Cause.findErrorOption(failed.cause)), error)
            assert.equal(yield* service.htmlToBase64(html), expected)
            const scope: Scope.Closeable = yield* Scope.make()
            const context: Context.Context<CanvasConversionService> = yield* Layer.build(
              CanvasConversionService.layer(Layer.succeed(ProjectEntitiesService, entities)),
            ).pipe(Effect.provideService(Scope.Scope, scope))
            const other: CanvasConversion = Context.get(context, CanvasConversionService)
            const started: Deferred.Deferred<boolean> = yield* Deferred.make<boolean>()
            try {
              resolve.mockReturnValue(
                Deferred.succeed(started, true).pipe(
                  Effect.flatMap((): Effect.Effect<never> => Effect.never),
                ),
              )
              const pending: Fiber.Fiber<Exit.Exit<string, unknown>> = yield* Effect.exit(
                other.htmlToBase64(html),
              ).pipe(Effect.forkChild)
              yield* Deferred.await(started)
              yield* Scope.close(scope, Exit.void)
              assert.ok(Exit.isFailure(yield* Fiber.join(pending)))
              resolve.mockRestore()
              assert.equal(yield* service.htmlToBase64(html), expected)
            } finally {
              yield* Scope.close(scope, Exit.void)
            }
          }),
        ),
      )
    })
  }
}
