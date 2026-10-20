import { Context, Effect, Layer, Scope } from 'effect'
import {
  ProjectEntities,
  ProjectEntitiesService,
} from '../../../src/toolkit/entities/ProjectEntities.ts'
import { CanvasConversionService } from '../../../src/toolkit/conversion/canvas/CanvasConversion.ts'
import type { CanvasConversion } from '../../../src/toolkit/conversion/canvas/CanvasConversion.ts'
import { CanvasEntity } from '../../../src/toolkit/entities/CanvasEntity.ts'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import type { CanvasDocument } from '../../../src/toolkit/canvas/CanvasValues.ts'
import { FileTestWorkspace } from './FileTestWorkspace.ts'

/** @anchor canvas-conversion-test-resources */
export class CanvasTestWorkspace {
  private constructor(
    public readonly service: CanvasConversion,
    public readonly entities: ProjectEntities,
    public readonly workspace: FileTestWorkspace,
  ) {}

  /** @anchor canvas-conversion-test-resources */
  public static make(): Effect.Effect<CanvasTestWorkspace, unknown, Scope.Scope> {
    return Effect.gen(function* (): Generator<
      Effect.Effect<unknown, unknown, Scope.Scope>,
      CanvasTestWorkspace,
      never
    > {
      const workspace: FileTestWorkspace = yield* Effect.acquireRelease(
        Effect.tryPromise({
          try: (): Promise<FileTestWorkspace> => FileTestWorkspace.make(),
          catch: (cause: unknown): unknown => cause,
        }),
        (workspace: FileTestWorkspace): Effect.Effect<void> =>
          Effect.promise((): Promise<void> => workspace.close()),
      )
      yield* Effect.tryPromise({
        try: (): Promise<void> =>
          workspace.write('setting.json', 'guid: project\nname: Canvas\nmainMap: ""\n'),
        catch: (cause: unknown): unknown => cause,
      })
      const entities: ProjectEntities = yield* ProjectEntities.make(workspace.files)
      const context: Context.Context<CanvasConversionService> = yield* Layer.build(
        CanvasConversionService.layer(Layer.succeed(ProjectEntitiesService, entities)),
      )
      return new CanvasTestWorkspace(
        Context.get(context, CanvasConversionService),
        entities,
        workspace,
      )
    })
  }

  /** @anchor canvas-conversion-test-resources */
  public static entity(document: CanvasDocument): CanvasEntity {
    const bytes: Uint8Array = CanvasConverter.toBinary(document)
    const guid: string = 'a'.repeat(32)
    return new CanvasEntity(
      {
        guid,
        relativePath: 'ui.canvas',
        revision: 'body',
        guidRelativePath: 'ui.canvas.guid',
        guidRevision: 'identity',
      },
      document,
      [
        { fileRelativePath: 'ui.canvas', bytes },
        { fileRelativePath: 'ui.canvas.guid', bytes: new TextEncoder().encode(guid) },
      ],
      {
        files: [
          { relativePath: 'ui.canvas', revision: 'body' },
          { relativePath: 'ui.canvas.guid', revision: 'identity' },
          { relativePath: '图片.png', revision: 'image' },
        ],
        directories: [{ relativePath: '', entries: ['ui.canvas', 'ui.canvas.guid', '图片.png'] }],
        fixedInputs: [{ name: 'platform', revision: 'platform-v1' }],
      },
    )
  }
}
