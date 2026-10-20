import { Context, Effect, Layer, Option } from 'effect'
import { WorkspacePaths } from '@toolkit/project-model/paths'
import { DeviceTsConverter } from '@toolkit/script/ts_conversion'
import { TsViewText } from '@toolkit/script/ts_text'
import type ts from 'typescript'
import type { BescriptDeviceContext } from '@toolkit/script/device_context'
import type { DeviceConversionBasis } from '@toolkit/script/ts_conversion'
import type { TsPrintState } from '@toolkit/script/ts_text'
import { DeviceTsViews, type DeviceTsView } from './DeviceTsViews.ts'
import type { DeviceEntity } from '../../entities/DeviceEntity.ts'
import { TsViewEntity } from '../../entities/ToolkitEntity.ts'
import { CanvasConversionService, type CanvasConversion } from '../canvas/CanvasConversion.ts'
import { TsCanvasConversion } from './TsCanvasConversion.ts'

/** @anchor ts-service-contract */
export interface TsViewConversion {
  /** @anchor bescript-ts-cache */
  print(
    context: BescriptDeviceContext,
    basis: DeviceConversionBasis,
  ): Effect.Effect<TsPrintState, unknown>
  /** @anchor bescript-ts-cache */
  remove(deviceRelativePath: string): Effect.Effect<void, unknown>
  /** @anchor ts-service-contract */
  toTs(device: DeviceEntity, viewRelativePath: string): Effect.Effect<TsViewEntity, unknown>
  /** @anchor ts-service-contract */
  fromTs(view: TsViewEntity, baseline: DeviceEntity): Effect.Effect<DeviceEntity, unknown>
}

/** @anchor ts-service-contract */
class WorkspaceTsViewConversion implements TsViewConversion {
  declare private readonly nominal: never

  /** @anchor bescript-ts-cache */
  public constructor(
    private readonly views: DeviceTsViews,
    private readonly canvas: CanvasConversion,
  ) {}

  /** @anchor bescript-ts-cache */
  public print(
    context: BescriptDeviceContext,
    basis: DeviceConversionBasis,
  ): Effect.Effect<TsPrintState, unknown> {
    return Effect.map(this.update(context, basis), (view: DeviceTsView): TsPrintState => view.state)
  }

  /** @anchor bescript-ts-cache */
  private update(
    context: BescriptDeviceContext,
    basis: DeviceConversionBasis,
  ): Effect.Effect<DeviceTsView, unknown> {
    return TsCanvasConversion.prepare(context, basis, Option.none(), this.canvas).pipe(
      Effect.flatMap((canvas: TsCanvasConversion): Effect.Effect<DeviceTsView, unknown> =>
        Effect.try({
          try: (): DeviceTsView => this.views.update(context, basis, canvas),
          catch: (cause: unknown): unknown => cause,
        }),
      ),
    )
  }

  /** @anchor bescript-ts-cache */
  public remove(deviceRelativePath: string): Effect.Effect<void, unknown> {
    return Effect.try({
      try: (): void => {
        this.views.remove(deviceRelativePath)
      },
      catch: (cause: unknown): unknown => cause,
    })
  }

  /** @anchor ts-service-contract */
  public toTs(
    device: DeviceEntity,
    viewRelativePath: string,
  ): Effect.Effect<TsViewEntity, unknown> {
    return Effect.gen({ self: this }, function* (this: WorkspaceTsViewConversion): Generator<
      Effect.Effect<unknown, unknown>,
      TsViewEntity,
      never
    > {
      yield* Effect.try({
        try: (): string => WorkspacePaths.require(viewRelativePath),
        catch: (cause: unknown): unknown => cause,
      })
      const view: DeviceTsView = yield* this.update(device.context, device.conversionBasis)
      return yield* Effect.try({
        try: (): TsViewEntity =>
          new TsViewEntity(
            device.id,
            viewRelativePath,
            device.baseline,
            view.state.text,
            view.sourceFile,
          ),
        catch: (cause: unknown): unknown => cause,
      })
    })
  }

  /** @anchor ts-service-contract */
  public fromTs(view: TsViewEntity, baseline: DeviceEntity): Effect.Effect<DeviceEntity, unknown> {
    return Effect.gen({ self: this }, function* (this: WorkspaceTsViewConversion): Generator<
      Effect.Effect<unknown, unknown>,
      DeviceEntity,
      never
    > {
      const syntax: ts.SourceFile = yield* Effect.try({
        try: (): ts.SourceFile => {
          this.views.assertOpen()
          if (
            view.sourceEntityId !== baseline.id ||
            JSON.stringify(view.baseline) !== JSON.stringify(baseline.baseline)
          )
            throw new Error('TS 视图与零件读取基准不一致')
          return TsViewText.parse(view.text)
        },
        catch: (cause: unknown): unknown => cause,
      })
      const canvas: TsCanvasConversion = yield* TsCanvasConversion.prepare(
        baseline.context,
        baseline.conversionBasis,
        Option.some(syntax),
        this.canvas,
      )
      return yield* Effect.try({
        try: (): DeviceEntity => {
          this.views.assertOpen()
          return baseline.withContext(
            DeviceTsConverter.fromTs(syntax, baseline.conversionBasis, canvas),
          )
        },
        catch: (cause: unknown): unknown => cause,
      })
    })
  }
}

/**
 * @anchor ts-service-contract
 * @anchor bescript-cross-language-glue
 */
export class TsViewConversionService extends Context.Service<
  TsViewConversionService,
  TsViewConversion
>()('toolkit/TsViewConversion') {
  private constructor(service: never) {
    super(service)
  }

  /** @anchor ts-service-contract */
  public static layer(
    canvas: Layer.Layer<CanvasConversionService, unknown>,
  ): Layer.Layer<TsViewConversionService, unknown> {
    return Layer.effect(
      TsViewConversionService,
      Effect.map(
        Effect.all([DeviceTsViews.make(), CanvasConversionService]),
        ([views, conversion]: readonly [DeviceTsViews, CanvasConversion]): TsViewConversion =>
          new WorkspaceTsViewConversion(views, conversion),
      ),
    ).pipe(Layer.provide(canvas))
  }
}
