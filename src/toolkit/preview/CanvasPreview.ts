import { Context, Effect, Layer } from 'effect'
import type { CanvasKit } from 'canvaskit-wasm'
import { ProjectEntitiesService, type ProjectEntities } from '../entities/ProjectEntities.ts'
import type { HtmlViewEntity } from '../entities/ToolkitEntity.ts'
import type { EntityReadBaseline } from '../entities/EntityReadBaseline.ts'
import type { CanvasRenderer } from '../../canvas_render/CanvasRenderer.ts'
import type { CanvasViewport, CanvasFont } from '../../canvas_render/RenderContracts.ts'

/**
 * @anchor preview-service-contract
 * @anchor preview-cpu-render-boundary
 */
export interface CanvasPreviewRequest {
  readonly view: HtmlViewEntity
  readonly viewport: CanvasViewport
  readonly fonts: readonly CanvasFont[]
  readonly timeSeconds: number
}

/**
 * @anchor preview-service-contract
 * @anchor preview-cpu-render-boundary
 */
interface PreviewImage {
  readonly sourceEntityId: string
  readonly image: Awaited<ReturnType<typeof CanvasRenderer.render>>
  readonly baseline: EntityReadBaseline
}

/** @anchor preview-service-contract */
export interface CanvasPreview {
  /**
   * @anchor preview-service-contract
   * @anchor preview-render-dependencies
   */
  render(request: CanvasPreviewRequest, backend: CanvasKit): Effect.Effect<PreviewImage, unknown>
}

/** @anchor preview-service-contract */
class PendingCanvasPreview implements CanvasPreview {
  declare private readonly nominal: never

  /** @anchor preview-service-contract */
  public constructor(public readonly entities: ProjectEntities) {}

  /**
   * @anchor preview-service-contract
   * @anchor preview-cpu-render-boundary
   * @anchor preview-render-dependencies
   */
  public render(
    _request: CanvasPreviewRequest,
    _backend: CanvasKit,
  ): Effect.Effect<PreviewImage, unknown> {
    return Effect.fail(new Error('TODO: 按具名视图及实际实体资源基准交付 CPU PNG 预览'))
  }
}

/** @anchor preview-service-contract */
export class CanvasPreviewService extends Context.Service<CanvasPreviewService, CanvasPreview>()(
  'toolkit/CanvasPreview',
) {
  private constructor(service: never) {
    super(service)
  }

  /** @anchor preview-service-contract */
  public static layer(
    entities: Layer.Layer<ProjectEntitiesService, unknown>,
  ): Layer.Layer<CanvasPreviewService, unknown> {
    return Layer.effect(
      CanvasPreviewService,
      Effect.map(
        ProjectEntitiesService,
        (projectEntities: ProjectEntities): CanvasPreview =>
          new PendingCanvasPreview(projectEntities),
      ),
    ).pipe(Layer.provide(entities))
  }
}
