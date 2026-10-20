import assert from 'node:assert/strict'
import { Buffer } from 'node:buffer'
import { test, vi } from 'vitest'
import type { MockInstance } from 'vitest'
import { Effect, Option, Scope } from 'effect'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import type { CanvasDocument } from '../../../src/toolkit/canvas/CanvasValues.ts'
import type { CanvasConversion } from '../../../src/toolkit/conversion/canvas/CanvasConversion.ts'
import { CanvasTestWorkspace } from '../toolkit-file-support/CanvasTestWorkspace.ts'

/**
 * @anchor canvas-conversion-cache
 * @anchor workspace-canvas-cache-inventory
 * @anchor workspace-canvas-computation-shortcuts
 */
export class CanvasCacheTests {
  private constructor() {}

  static {
    test('并发同内容只执行一次转换，方向和内容变化独立取键', async (): Promise<void> => {
      const html: string = '<BeCanvas name="并发" width="1080" height="2160"></BeCanvas>'
      const expected: string = CanvasConverter.toInlineBase64(CanvasConverter.fromHtml(html))
      const spy: MockInstance<typeof CanvasConverter.fromHtml> = vi.spyOn(
        CanvasConverter,
        'fromHtml',
      )
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
            const service: CanvasConversion = fixture.service
            const results: readonly string[] = yield* Effect.all(
              Array.from({ length: 8 }, (): Effect.Effect<string, unknown> =>
                service.htmlToBase64(html),
              ),
              { concurrency: 4 },
            )
            assert.deepEqual(
              results,
              Array.from({ length: 8 }, (): string => expected),
            )
            assert.equal(spy.mock.calls.length, 1)
            yield* service.htmlToHjson(html)
            yield* service.htmlToBase64(html.replace('并发', '修改'))
            assert.equal(yield* service.htmlToBase64(html), expected)
            assert.equal(spy.mock.calls.length, 3)
          }),
        ),
      )
    })

    test('原生访问顺序保持128项淘汰，关闭后既有Effect失败且工作区独立', async (): Promise<void> => {
      let pending: Option.Option<Effect.Effect<string, unknown>> = Option.none()
      const spy: MockInstance<typeof CanvasConverter.fromHtml> = vi.spyOn(
        CanvasConverter,
        'fromHtml',
      )
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
            const service: CanvasConversion = fixture.service
            for (let index: number = 0; index < 128; index += 1)
              yield* service.htmlToBase64(
                `<BeCanvas name="${String(index)}" width="1080" height="2160"></BeCanvas>`,
              )
            assert.equal(spy.mock.calls.length, 128)
            yield* service.htmlToBase64('<BeCanvas name="0" width="1080" height="2160"></BeCanvas>')
            yield* service.htmlToBase64(
              '<BeCanvas name="128" width="1080" height="2160"></BeCanvas>',
            )
            yield* service.htmlToBase64('<BeCanvas name="0" width="1080" height="2160"></BeCanvas>')
            assert.equal(spy.mock.calls.length, 129)
            yield* service.htmlToBase64('<BeCanvas name="1" width="1080" height="2160"></BeCanvas>')
            assert.equal(spy.mock.calls.length, 130)
            pending = Option.some(
              service.htmlToBase64('<BeCanvas name="0" width="1080" height="2160"></BeCanvas>'),
            )
          }),
        ),
      )
      await assert.rejects(Effect.runPromise(Option.getOrThrow(pending)), /Canvas 转换服务已关闭/u)
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
            const service: CanvasConversion = fixture.service
            yield* service.htmlToBase64('<BeCanvas name="0" width="1080" height="2160"></BeCanvas>')
            yield* service.htmlToBase64('<BeCanvas name="0" width="1080" height="2160"></BeCanvas>')
            assert.equal(spy.mock.calls.length, 131)
          }),
        ),
      )
    })
  }
}

/** @anchor canvas-conversion-cache */
export class CanvasBudgetTests {
  private constructor() {}

  static {
    test.each([0, 1])(
      '键加结果为1 MiB + %i时完整转换，仅预算内复用',
      async (excess: number): Promise<void> => {
        const document: CanvasDocument = CanvasConverter.fromHtml(
          '<BeCanvas name="" width="1080" height="2160"></BeCanvas>',
        )
        const initial: string = CanvasConverter.toInlineBase64(document)
        const base: string = '<BeCanvas name="" width="1080" height="2160"></BeCanvas>'
        const overhead: number =
          Buffer.byteLength(JSON.stringify(['html-base64', base])) + Buffer.byteLength(initial) + 2
        const remaining: number = 1048576 + excess - overhead
        const name: string = 'x'.repeat(Math.floor(remaining / 2))
        const html: string =
          ' '.repeat(remaining % 2) +
          `<BeCanvas name="${name}" width="1080" height="2160"></BeCanvas>`
        const expected: string = CanvasConverter.toInlineBase64(CanvasConverter.fromHtml(html))
        assert.equal(
          Buffer.byteLength(JSON.stringify(['html-base64', html])) +
            Buffer.byteLength(expected) +
            2,
          1048576 + excess,
        )
        const spy: MockInstance<typeof CanvasConverter.fromHtml> = vi.spyOn(
          CanvasConverter,
          'fromHtml',
        )
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
              const service: CanvasConversion = fixture.service
              assert.equal(yield* service.htmlToBase64(html), expected)
              assert.equal(yield* service.htmlToBase64(html), expected)
              assert.equal(spy.mock.calls.length, excess === 0 ? 1 : 2)
            }),
          ),
        )
      },
    )

    test('大输入和中文XML展开的大输出完整返回且不驻留', async (): Promise<void> => {
      const largeInput: string =
        ' '.repeat(1048576) + '<BeCanvas name="大输入" width="1080" height="2160"></BeCanvas>'
      const expectedInput: string = CanvasConverter.toInlineBase64(
        CanvasConverter.fromHtml(largeInput),
      )
      const document: CanvasDocument = CanvasConverter.fromInline(
        `==hjson:大输出:\n[{"type":"UILabel","name":"文本","text":"${'甲&'.repeat(150000)}"}]==`,
      )
      const source: string = CanvasConverter.toInlineBase64(document)
      const expectedOutput: string = CanvasConverter.toHtmlInline(document)
      assert.ok(Buffer.byteLength(source) < 1048576)
      assert.ok(Buffer.byteLength(expectedOutput) > 1048576)
      const htmlSpy: MockInstance<typeof CanvasConverter.fromHtml> = vi.spyOn(
        CanvasConverter,
        'fromHtml',
      )
      const inlineSpy: MockInstance<typeof CanvasConverter.fromInline> = vi.spyOn(
        CanvasConverter,
        'fromInline',
      )
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
            const service: CanvasConversion = fixture.service
            for (let repeat: number = 0; repeat < 2; repeat += 1) {
              assert.equal(yield* service.htmlToBase64(largeInput), expectedInput)
              assert.equal(yield* service.base64ToHtml(source), expectedOutput)
            }
            assert.equal(htmlSpy.mock.calls.length, 2)
            assert.equal(inlineSpy.mock.calls.length, 2)
          }),
        ),
      )
    })
  }
}
