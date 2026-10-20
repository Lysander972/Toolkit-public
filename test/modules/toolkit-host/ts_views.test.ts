import assert from 'node:assert/strict'
import { test } from 'vitest'
import { Context, Effect, Layer, Option, Result, Scope } from 'effect'
import ts from 'typescript'
import { DeviceTsViews } from '../../../src/toolkit/conversion/ts/DeviceTsViews.ts'
import {
  TsViewConversionService,
  type TsViewConversion,
} from '../../../src/toolkit/conversion/ts/TsViewConversion.ts'
import { DeviceTsConverter } from '../../../src/toolkit/script/ts_conversion/DeviceTsConverter.ts'
import { TsViewText } from '../../../src/toolkit/script/ts_text/TsViewText.ts'
import { CanvasTestWorkspace } from '../toolkit-file-support/CanvasTestWorkspace.ts'
import {
  CanvasConversionService,
  type CanvasConversion,
} from '../../../src/toolkit/conversion/canvas/CanvasConversion.ts'
import { TsViewFixture } from '../toolkit-script-fixtures/TsViewFixture.ts'
import type { TsPrintState } from '../../../src/toolkit/script/ts_text/TsViewText.ts'
import type { DeviceConversionBasis } from '../../../src/toolkit/script/ts_conversion/DeviceTsConverter.ts'
import type { BescriptDeviceContext } from '../../../src/toolkit/script/device_context/DeviceContext.ts'

/**
 * @anchor bescript-ts-cache
 * @anchor workspace-ts-cache-inventory
 * @anchor workspace-ts-computation-shortcuts
 */
export class TsViewsHostTest {
  private constructor() {}

  static {
    test('Scope 关闭后的视图服务失败；转换失败保留上次有效缓存', async (): Promise<void> => {
      let released: Option.Option<DeviceTsViews> = Option.none()
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, never, Scope.Scope>,
            void,
            never
          > {
            const views: DeviceTsViews = yield* DeviceTsViews.make()
            released = Option.some(views)
            const basis: DeviceConversionBasis = TsViewFixture.basis()
            views.update(TsViewFixture.context('原值'), basis)
            assert.throws(
              (): TsPrintState =>
                views.update(TsViewFixture.context('新值'), { ...basis, methods: [] }).state,
            )
            assert.equal(
              views.update(TsViewFixture.context('原值'), basis).state.printedFragments,
              0,
            )
          }),
        ),
      )
      assert.throws(
        (): TsPrintState =>
          Option.getOrThrow(released).update(TsViewFixture.context('原值'), TsViewFixture.basis())
            .state,
        /服务已关闭/u,
      )
    })
    test('内部服务按完整输入更新，双零件隔离且失败保留缓存', async (): Promise<void> => {
      let released: Option.Option<TsViewConversion> = Option.none()
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
            const canvas: CanvasConversion = fixture.service
            const services: Context.Context<TsViewConversionService> = yield* Layer.build(
              TsViewConversionService.layer(Layer.succeed(CanvasConversionService, canvas)),
            )
            const service: TsViewConversion = Context.get(services, TsViewConversionService)
            released = Option.some(service)
            const basis: DeviceConversionBasis = TsViewFixture.basis()
            const other: DeviceConversionBasis = {
              ...basis,
              identity: {
                ...basis.identity,
                deviceRelativePath: '机械/测试.mech_/1',
                id: '1',
                className: 'Device_Second',
              },
              scripts: basis.scripts.map(
                (
                  script: DeviceConversionBasis['scripts'][number],
                ): DeviceConversionBasis['scripts'][number] => ({
                  ...script,
                  scriptRelativePath: '机械/测试.mech_/1/scripts/' + script.bindingName + '.code',
                }),
              ),
            }
            const original: BescriptDeviceContext = TsViewFixture.context('原值')
            const edited: BescriptDeviceContext = TsViewFixture.context('实际修改')
            for (const current of [basis, other]) {
              const cold: TsPrintState = yield* service.print(original, current)
              assert.equal(cold.printedFragments, 2)
              assert.equal(
                cold.text,
                ts.createPrinter().printFile(DeviceTsConverter.toTs(original, current)),
              )
              assert.equal((yield* service.print(original, current)).printedFragments, 0)
            }
            const updated: TsPrintState = yield* service.print(edited, basis)
            assert.equal(updated.printedFragments, 1)
            assert.equal(
              updated.text,
              ts.createPrinter().printFile(DeviceTsConverter.toTs(edited, basis)),
            )
            assert.deepEqual(
              DeviceTsConverter.fromTs(TsViewText.parse(updated.text), basis),
              edited,
            )
            const failure: Result.Result<TsPrintState, unknown> = yield* Effect.result(
              service.print(original, { ...basis, methods: [] }),
            )
            assert.equal(Result.isFailure(failure), true)
            assert.equal((yield* service.print(edited, basis)).printedFragments, 0)
            const changed: DeviceConversionBasis = {
              ...basis,
              methods: basis.methods.map(
                (
                  method: DeviceConversionBasis['methods'][number],
                ): DeviceConversionBasis['methods'][number] =>
                  method.besName === 'echo' ? { ...method, tsName: 'echoChanged' } : method,
              ),
            }
            const semantic: TsPrintState = yield* service.print(edited, changed)
            assert.equal(semantic.printedFragments, 2)
            assert.equal(
              semantic.text,
              ts.createPrinter().printFile(DeviceTsConverter.toTs(edited, changed)),
            )
            assert.equal((yield* service.print(original, other)).printedFragments, 0)
            yield* service.remove(basis.identity.deviceRelativePath)
            assert.equal((yield* service.print(edited, changed)).printedFragments, 2)
            assert.equal((yield* service.print(original, other)).printedFragments, 0)
          }),
        ),
      )
      await assert.rejects(
        Effect.runPromise(
          Option.getOrThrow(released).print(TsViewFixture.context('原值'), TsViewFixture.basis()),
        ),
        /服务已关闭/u,
      )
      await assert.rejects(
        Effect.runPromise(
          Option.getOrThrow(released).remove(TsViewFixture.basis().identity.deviceRelativePath),
        ),
        /服务已关闭/u,
      )
    })
  }
}
