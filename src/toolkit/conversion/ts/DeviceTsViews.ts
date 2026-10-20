import { Effect, Option, Scope } from 'effect'
import { DeviceTsConverter } from '@toolkit/script/ts_conversion'
import type { DeviceConversionBasis } from '@toolkit/script/ts_conversion'
import { NativeInlineCanvas, type InlineCanvasCodec } from '@toolkit/script/inline_canvas'
import {
  DeviceContextSchema,
  type BescriptDeviceContext,
  type DeviceFunction,
} from '@toolkit/script/device_context'
import type { ScriptGroup } from '@toolkit/script/script_list'
import { TsViewText } from '@toolkit/script/ts_text'
import type { TsFragmentRevision, TsPrintState } from '@toolkit/script/ts_text'

/** @anchor bescript-ts-cache */
export interface DeviceTsView {
  readonly sourceFile: ReturnType<typeof DeviceTsConverter.toTs>
  readonly state: TsPrintState
}

/**
 * @anchor bescript-ts-cache
 * @anchor workspace-ts-cache-inventory
 */
export class DeviceTsViews {
  private readonly views: Map<string, DeviceTsView> = new Map()
  private closed: boolean = false

  private constructor() {}

  /**
   * @anchor bescript-ts-cache
   * @anchor workspace-ts-cache-inventory
   */
  public static make(): Effect.Effect<DeviceTsViews, never, Scope.Scope> {
    return Effect.acquireRelease(
      Effect.sync((): DeviceTsViews => new DeviceTsViews()),
      (views: DeviceTsViews): Effect.Effect<void> =>
        Effect.sync((): void => {
          views.close()
        }),
    )
  }

  /**
   * @anchor bescript-ts-cache
   * @anchor workspace-ts-cache-inventory
   * @anchor workspace-ts-computation-shortcuts
   */
  public update(
    context: BescriptDeviceContext,
    basis: DeviceConversionBasis,
    canvas: InlineCanvasCodec = new NativeInlineCanvas(),
  ): DeviceTsView {
    this.assertOpen()
    const relativePath: string = basis.identity.deviceRelativePath
    DeviceContextSchema.parse(context)
    const resolved: ReturnType<typeof DeviceTsConverter.resolve> = DeviceTsConverter.resolve(
      context,
      basis,
    )
    const dependencies: string = JSON.stringify([resolved.data, context.globalVariables])
    const revisions: readonly TsFragmentRevision[] = context.scriptGroups.flatMap(
      (group: ScriptGroup<DeviceFunction>): readonly TsFragmentRevision[] =>
        group.entries.map((item: DeviceFunction): TsFragmentRevision => ({
          id: JSON.stringify([group.kind, group.eventName, item.binding.name]),
          revision: JSON.stringify(item),
          dependencies,
        })),
    )
    const previous: Option.Option<DeviceTsView> = Option.fromNullishOr(this.views.get(relativePath))
    const reusableIds: Set<string> = new Set()
    if (Option.isSome(previous))
      for (const [index, revision] of revisions.entries()) {
        const old: Option.Option<TsFragmentRevision> = Option.fromNullishOr(
          previous.value.state.fragments[index],
        )
        if (
          Option.isSome(old) &&
          old.value.id === revision.id &&
          old.value.revision === revision.revision &&
          old.value.dependencies === revision.dependencies
        )
          reusableIds.add(revision.id)
      }
    const reusedMethods: ReturnType<typeof TsViewText.reusableMethods> = Option.isSome(previous)
      ? TsViewText.reusableMethods(
          previous.value.sourceFile,
          previous.value.state.fragments.map((fragment: TsFragmentRevision): string => fragment.id),
          reusableIds,
        )
      : new Map()
    const sourceFile: ReturnType<typeof DeviceTsConverter.toTs> =
      DeviceTsConverter.toTsWithReusedMethods(context, basis, resolved, reusedMethods, canvas)
    const state: TsPrintState = TsViewText.print(
      sourceFile,
      revisions,
      Option.map(previous, (view: DeviceTsView): TsPrintState => view.state),
    )
    const view: DeviceTsView = { sourceFile, state }
    this.views.set(relativePath, view)
    return view
  }

  /**
   * @anchor bescript-ts-cache
   * @anchor workspace-ts-cache-inventory
   */
  public remove(deviceRelativePath: string): void {
    this.assertOpen()
    this.views.delete(deviceRelativePath)
  }

  /** @anchor bescript-ts-cache */
  public assertOpen(): void {
    if (this.closed) throw new Error('TS 视图服务已关闭')
  }

  /**
   * @anchor bescript-ts-cache
   * @anchor workspace-ts-cache-inventory
   */
  private close(): void {
    this.views.clear()
    this.closed = true
  }
}
