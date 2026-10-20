import assert from 'node:assert/strict'
import { performance } from 'node:perf_hooks'
import { test, vi } from 'vitest'
import type { MockInstance } from 'vitest'
import { Effect, Option, Scope } from 'effect'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import type { CanvasDocument } from '../../../src/toolkit/canvas/CanvasValues.ts'
import type { CanvasEntity } from '../../../src/toolkit/entities/CanvasEntity.ts'
import { CanvasCorpusFixtures } from '../toolkit-file-support/CanvasCorpus.ts'
import type {
  BinaryCanvasFixture,
  InlineCanvasFixture,
} from '../toolkit-file-support/CanvasCorpus.ts'
import { CanvasTestWorkspace } from '../toolkit-file-support/CanvasTestWorkspace.ts'
import type { CanvasConversion } from '../../../src/toolkit/conversion/canvas/CanvasConversion.ts'
import type { FileTestWorkspace } from '../toolkit-file-support/FileTestWorkspace.ts'

/** @anchor canvas-conversion-dispatch */
export class CanvasConversionCorpusTests {
  private constructor() {}

  static {
    test('完整63独立与109内联Canvas的六向冷转换和命中等于纯函数基线', async (): Promise<void> => {
      const binary: readonly BinaryCanvasFixture[] = CanvasCorpusFixtures.binaryCanvasFixtures()
      const inline: readonly InlineCanvasFixture[] = CanvasCorpusFixtures.inlineCanvasFixtures()
      assert.equal(binary.length, 63)
      assert.equal(
        inline.filter((fixture: InlineCanvasFixture): boolean => fixture.format === 'base64')
          .length,
        108,
      )
      assert.equal(
        inline.filter((fixture: InlineCanvasFixture): boolean => fixture.format === 'hjson').length,
        1,
      )
      const documents: readonly CanvasDocument[] = [
        ...binary.map((fixture: BinaryCanvasFixture): CanvasDocument =>
          CanvasConverter.fromBinary(fixture.bytes),
        ),
        ...inline.map((fixture: InlineCanvasFixture): CanvasDocument =>
          CanvasConverter.fromInline(fixture.text),
        ),
      ]
      let coldMilliseconds: number = 0
      let hitMilliseconds: number = 0
      let baselineMilliseconds: number = 0
      let actualForwardConversions: number = 0
      let actualReverseConversions: number = 0
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
            const service: CanvasConversion = fixture.service
            const workspace: FileTestWorkspace = fixture.workspace
            const written: number = workspace.writtenBytes()
            for (const document of documents) {
              const started: number = performance.now()
              const entity: CanvasEntity = CanvasTestWorkspace.entity(document)
              const html: string = CanvasConverter.toHtmlInline(document)
              const base64: string = CanvasConverter.toInlineBase64(document)
              const hjson: string = CanvasConverter.toInlineHjson(document)
              const bytes: Uint8Array = CanvasConverter.toBinary(document)
              baselineMilliseconds += performance.now() - started
              const forwardSpy: MockInstance<typeof CanvasConverter.toHtmlInline> = vi.spyOn(
                CanvasConverter,
                'toHtmlInline',
              )
              const reverseSpy: MockInstance<typeof CanvasConverter.fromHtml> = vi.spyOn(
                CanvasConverter,
                'fromHtml',
              )
              const calls: readonly Effect.Effect<string, unknown>[] = [
                service.toHtml(entity),
                service.base64ToHtml(base64),
                service.hjsonToHtml(hjson),
                service.htmlToBase64(html),
                service.htmlToHjson(html),
              ]
              const expected: readonly string[] = [html, html, html, base64, hjson]
              for (const [index, call] of calls.entries()) {
                const coldStart: number = performance.now()
                const cold: string = yield* call
                coldMilliseconds += performance.now() - coldStart
                const hitStart: number = performance.now()
                const hit: string = yield* call
                hitMilliseconds += performance.now() - hitStart
                assert.equal(cold, Option.getOrThrow(Option.fromNullishOr(expected[index])))
                assert.equal(hit, cold)
              }
              for (let repeat: number = 0; repeat < 2; repeat += 1) {
                const reverseStart: number = performance.now()
                const result: CanvasEntity = yield* service.fromHtml(html, entity)
                const elapsed: number = performance.now() - reverseStart
                if (repeat === 0) coldMilliseconds += elapsed
                else hitMilliseconds += elapsed
                assert.deepEqual(CanvasConverter.toBinary(result.document), bytes)
              }
              assert.ok(forwardSpy.mock.calls.length <= 3)
              assert.ok(reverseSpy.mock.calls.length <= 3)
              actualForwardConversions += forwardSpy.mock.calls.length
              actualReverseConversions += reverseSpy.mock.calls.length
              forwardSpy.mockRestore()
              reverseSpy.mockRestore()
            }
            assert.equal(workspace.writtenBytes(), written)
            const memory: NodeJS.MemoryUsage = process.memoryUsage()
            console.info({
              canvasCorpus: documents.length,
              conversionCalls: documents.length * 12,
              actualForwardConversions,
              actualReverseConversions,
              baselineMilliseconds,
              coldMilliseconds,
              hitMilliseconds,
              heapBytes: memory.heapUsed,
              rssBytes: memory.rss,
              conversionWrittenFiles: 0,
              conversionWrittenBytes: 0,
              conversionMaximumFileBytes: 0,
              conversionCumulativeWrittenBytes: 0,
            })
          }),
        ),
      )
    })
  }
}
