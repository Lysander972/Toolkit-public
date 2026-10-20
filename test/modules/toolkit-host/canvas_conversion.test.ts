import assert from 'node:assert/strict'
import { stat } from 'node:fs/promises'
import { test, vi } from 'vitest'
import type { MockInstance } from 'vitest'
import { Cause, Effect, Exit, Option, Scope } from 'effect'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import { StringVar } from '../../../src/toolkit/canvas/CanvasValues.ts'
import type { CanvasDocument, CanvasNode } from '../../../src/toolkit/canvas/CanvasValues.ts'
import { CanvasEntity } from '../../../src/toolkit/entities/CanvasEntity.ts'
import type { CanvasConversion } from '../../../src/toolkit/conversion/canvas/CanvasConversion.ts'
import { CanvasTestWorkspace } from '../toolkit-file-support/CanvasTestWorkspace.ts'

/** @anchor canvas-service-contract */
export class CanvasContractTests {
  private constructor() {}

  static {
    test('六向冷转换和命中保持纯函数结果，片段不带文档壳', async (): Promise<void> => {
      const html: string =
        '<BeCanvas name="四向" width="1080" height="2160"><BeUIButton name="按钮" label="甲&amp;乙" color="palette(危险)"></BeUIButton><BeUILabel name="文本" text="第一行&#10;第二行"></BeUILabel></BeCanvas>'
      const document: CanvasDocument = CanvasConverter.fromHtml(html)
      const entity: CanvasEntity = CanvasTestWorkspace.entity(document)
      const fragment: string = CanvasConverter.toHtmlInline(document)
      const base64: string = CanvasConverter.toInlineBase64(document)
      const hjson: string = CanvasConverter.toInlineHjson(document)
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
              assert.equal(yield* service.toHtml(entity), fragment)
              assert.equal(yield* service.base64ToHtml(base64), fragment)
              assert.equal(yield* service.hjsonToHtml(hjson), fragment)
              assert.equal(yield* service.htmlToBase64(html), base64)
              assert.equal(yield* service.htmlToHjson(html), hjson)
              assert.deepEqual(
                CanvasConverter.toBinary((yield* service.fromHtml(html, entity)).document),
                CanvasConverter.toBinary(document),
              )
            }
          }),
        ),
      )
    })

    test('明确格式入口拒绝错形态和损坏输入，原始错误不驻留', async (): Promise<void> => {
      const html: string = '<BeCanvas name="有效" width="1080" height="2160"></BeCanvas>'
      const document: CanvasDocument = CanvasConverter.fromHtml(html)
      const entity: CanvasEntity = CanvasTestWorkspace.entity(document)
      const expected: Error = new Error('原始解析失败')
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
            const service: CanvasConversion = fixture.service
            const invalid: readonly Effect.Effect<unknown, unknown>[] = [
              service.base64ToHtml(CanvasConverter.toInlineHjson(document)),
              service.hjsonToHtml(CanvasConverter.toInlineBase64(document)),
              service.base64ToHtml('canvas:bad=****'),
              service.hjsonToHtml('==hjson:bad:\n{}=='),
              service.htmlToBase64('<html></html>'),
              service.htmlToHjson('<BeCanvas name="bad"><Unknown /></BeCanvas>'),
              service.fromHtml(
                '<BeCanvas name="bad"><BeUIRect name="r" color="palette()"></BeUIRect></BeCanvas>',
                entity,
              ),
            ]
            for (const call of invalid) assert.equal(Exit.isFailure(yield* Effect.exit(call)), true)
            const spy: MockInstance<typeof CanvasConverter.fromHtml> = vi.spyOn(
              CanvasConverter,
              'fromHtml',
            )
            spy.mockImplementationOnce((): CanvasDocument => {
              throw expected
            })
            const failed: Exit.Exit<string, unknown> = yield* Effect.exit(
              service.htmlToBase64(html),
            )
            assert.ok(Exit.isFailure(failed))
            assert.equal(Option.getOrThrow(Cause.findErrorOption(failed.cause)), expected)
            assert.equal(
              yield* service.htmlToBase64(html),
              CanvasConverter.toInlineBase64(document),
            )
            assert.equal(
              yield* service.htmlToBase64(html),
              CanvasConverter.toInlineBase64(document),
            )
            assert.equal(spy.mock.calls.length, 2)
          }),
        ),
      )
    })
  }
}

/** @anchor canvas-conversion-result */
export class CanvasEntityResultTests {
  private constructor() {}

  static {
    test('反向保留每次调用基准与来源，模型和字节副本隔离，语义变化重新转换', async (): Promise<void> => {
      const html: string =
        '<BeCanvas name="编辑" width="1080" height="2160"><BeUILabel name="文本" text="新值"></BeUILabel></BeCanvas>'
      const baseline: CanvasEntity = CanvasTestWorkspace.entity(
        CanvasConverter.fromHtml(html.replace('新值', '旧值')),
      )
      const secondGuid: string = 'b'.repeat(32)
      const secondBaseline: CanvasEntity = new CanvasEntity(
        {
          guid: secondGuid,
          relativePath: 'second.canvas',
          revision: 'second-body',
          guidRelativePath: 'second.canvas.guid',
          guidRevision: 'second-identity',
        },
        CanvasConverter.fromHtml(html),
        [
          {
            fileRelativePath: 'second.canvas',
            bytes: CanvasConverter.toBinary(CanvasConverter.fromHtml(html)),
          },
          { fileRelativePath: 'second.canvas.guid', bytes: new TextEncoder().encode(secondGuid) },
        ],
        {
          files: [
            { relativePath: 'second.canvas', revision: 'second-body' },
            { relativePath: 'second.canvas.guid', revision: 'second-identity' },
          ],
          directories: [{ relativePath: '', entries: ['second.canvas', 'second.canvas.guid'] }],
          fixedInputs: [{ name: 'platform', revision: 'platform-v2' }],
        },
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
            const first: CanvasEntity = yield* service.fromHtml(html, baseline)
            assert.deepEqual(first.declaration, baseline.declaration)
            assert.deepEqual(first.baseline, baseline.baseline)
            assert.deepEqual(first.originals, baseline.originals)
            assert.notEqual(first.originals[0], baseline.originals[0])
            const node: CanvasNode = Option.getOrThrow(
              Option.fromNullishOr(first.document.root.children()[0]),
            )
            node.setField('text', StringVar.of('独立修改'))
            Option.getOrThrow(Option.fromNullishOr(first.originals[0])).bytes.fill(0)
            const second: CanvasEntity = yield* service.fromHtml(html, secondBaseline)
            assert.deepEqual(second.declaration, secondBaseline.declaration)
            assert.deepEqual(second.baseline, secondBaseline.baseline)
            assert.equal(second.id, secondBaseline.id)
            assert.equal(second.sourceRelativePath, secondBaseline.sourceRelativePath)
            assert.deepEqual(second.originals, secondBaseline.originals)
            assert.equal(
              CanvasConverter.toHtmlInline(second.document),
              CanvasConverter.toHtmlInline(CanvasConverter.fromHtml(html)),
            )
            assert.equal(CanvasConverter.toHtmlInline(baseline.document).includes('旧值'), true)
            assert.notEqual(yield* service.toHtml(first), yield* service.toHtml(second))
            assert.throws((): void => {
              second.encode()
            }, /TODO:/u)
          }),
        ),
      )
    })
  }
}

/** @anchor canvas-conversion-test-resources */
export class CanvasResourceTests {
  private constructor() {}

  static {
    test('转换成功和解析失败均关闭服务并释放独占目录', async (): Promise<void> => {
      for (const fails of [false, true]) {
        let released: Option.Option<CanvasTestWorkspace> = Option.none()
        const result: Exit.Exit<string, unknown> = await Effect.runPromise(
          Effect.exit(
            Effect.scoped(
              Effect.gen(function* (): Generator<
                Effect.Effect<unknown, unknown, Scope.Scope>,
                string,
                never
              > {
                const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
                released = Option.some(fixture)
                return yield* fixture.service.htmlToBase64(
                  fails
                    ? '<BeCanvas>'
                    : '<BeCanvas name="资源" width="1080" height="2160"></BeCanvas>',
                )
              }),
            ),
          ),
        )
        assert.equal(Exit.isFailure(result), fails)
        const fixture: CanvasTestWorkspace = Option.getOrThrow(released)
        await assert.rejects(stat(fixture.workspace.fullPath), { code: 'ENOENT' })
        await assert.rejects(
          Effect.runPromise(
            fixture.service.htmlToBase64(
              '<BeCanvas name="关闭" width="1080" height="2160"></BeCanvas>',
            ),
          ),
          /Canvas 转换服务已关闭/u,
        )
      }
    })
  }
}
