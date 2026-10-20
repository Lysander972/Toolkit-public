import { Effect, Option } from 'effect'
import type ts from 'typescript'
import { DeviceTsConverter } from '@toolkit/script/ts_conversion'
import type { DeviceConversionBasis, InlineCanvasTemplate } from '@toolkit/script/ts_conversion'
import type { InlineCanvasCodec } from '@toolkit/script/inline_canvas'
import type { BescriptDeviceContext } from '@toolkit/script/device_context'
import type { CanvasConversion } from '../canvas/CanvasConversion.ts'

/** @anchor ts-service-contract */
export class TsCanvasConversion implements InlineCanvasCodec {
  private constructor(
    private readonly rendered: ReadonlyMap<string, string>,
    private readonly encoded: ReadonlyMap<string, string>,
  ) {}

  /** @anchor ts-service-contract */
  public static prepare(
    context: BescriptDeviceContext,
    basis: DeviceConversionBasis,
    syntax: Option.Option<ts.SourceFile>,
    canvas: CanvasConversion,
  ): Effect.Effect<TsCanvasConversion, unknown> {
    return Effect.gen(function* (): Generator<
      Effect.Effect<unknown, unknown>,
      TsCanvasConversion,
      never
    > {
      const sources: readonly string[] = yield* Effect.try({
        try: (): readonly string[] => DeviceTsConverter.canvasSources(context, basis),
        catch: (cause: unknown): unknown => cause,
      })
      const rendered: Map<string, string> = new Map()
      const encoded: Map<string, string> = new Map()
      for (const source of sources) {
        rendered.set(source, yield* TsCanvasConversion.render(source, canvas))
      }
      const templates: readonly InlineCanvasTemplate[] = yield* Effect.try({
        try: (): readonly InlineCanvasTemplate[] =>
          Option.match(syntax, {
            onNone: (): readonly InlineCanvasTemplate[] => [],
            onSome: (sourceFile: ts.SourceFile): readonly InlineCanvasTemplate[] =>
              DeviceTsConverter.canvasTemplates(sourceFile, basis),
          }),
        catch: (cause: unknown): unknown => cause,
      })
      for (const template of templates) {
        const source: string = yield* template.nodeMode
          ? canvas.htmlToHjson(template.html)
          : canvas.htmlToBase64(template.html)
        encoded.set(JSON.stringify([template.nodeMode, template.html]), source)
        rendered.set(source, yield* TsCanvasConversion.render(source, canvas))
      }
      return new TsCanvasConversion(rendered, encoded)
    })
  }

  /** @anchor ts-service-contract */
  private static render(source: string, canvas: CanvasConversion): Effect.Effect<string, unknown> {
    return source.startsWith('canvas:') ? canvas.base64ToHtml(source) : canvas.hjsonToHtml(source)
  }

  /** @anchor ts-service-contract */
  public toHtml(source: string): string {
    return Option.getOrThrow(Option.fromNullishOr(this.rendered.get(source)))
  }

  /** @anchor ts-service-contract */
  public fromHtml(html: string, nodeMode: boolean): string {
    return Option.getOrThrow(
      Option.fromNullishOr(this.encoded.get(JSON.stringify([nodeMode, html]))),
    )
  }

  /** @anchor ts-service-contract */
  public equivalent(current: string, original: string): boolean {
    return this.toHtml(current) === this.toHtml(original)
  }
}
