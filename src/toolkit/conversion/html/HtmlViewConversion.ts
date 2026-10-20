import { Context, Effect, Layer } from 'effect'
import type { CanvasEntity } from '../../entities/CanvasEntity.ts'
import type { HtmlViewEntity } from '../../entities/ToolkitEntity.ts'
import { CanvasConversionService } from '../canvas/CanvasConversion.ts'

/** @anchor html-service-contract */
export interface HtmlViewConversion {
  /** @anchor html-service-contract */
  toHtml(canvas: CanvasEntity, viewRelativePath: string): Effect.Effect<HtmlViewEntity, unknown>
  /** @anchor html-service-contract */
  fromHtml(view: HtmlViewEntity, baseline: CanvasEntity): Effect.Effect<CanvasEntity, unknown>
}

/** @anchor html-service-contract */
class PendingHtmlViewConversion implements HtmlViewConversion {
  declare private readonly nominal: never

  /** @anchor html-service-contract */
  public toHtml(
    _canvas: CanvasEntity,
    _viewRelativePath: string,
  ): Effect.Effect<HtmlViewEntity, unknown> {
    return Effect.fail(new Error('TODO: 独立 Canvas 实体到完整 HTML 视图的装配'))
  }

  /** @anchor html-service-contract */
  public fromHtml(
    _view: HtmlViewEntity,
    _baseline: CanvasEntity,
  ): Effect.Effect<CanvasEntity, unknown> {
    return Effect.fail(new Error('TODO: 完整 HTML 视图到独立 Canvas 实体的反向转换'))
  }
}

/** @anchor html-service-contract */
export class HtmlViewConversionService extends Context.Service<
  HtmlViewConversionService,
  HtmlViewConversion
>()('toolkit/HtmlViewConversion') {
  private constructor(service: never) {
    super(service)
  }

  /** @anchor html-service-contract */
  public static layer(
    canvas: Layer.Layer<CanvasConversionService, unknown>,
  ): Layer.Layer<HtmlViewConversionService, unknown> {
    return Layer.effect(
      HtmlViewConversionService,
      Effect.as(CanvasConversionService, new PendingHtmlViewConversion()),
    ).pipe(Layer.provide(canvas))
  }
}
