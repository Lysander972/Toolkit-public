import assert from 'node:assert/strict'
import { test } from 'vitest'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import { CanvasCorpusFixtures } from '../toolkit-file-support/CanvasCorpus.ts'
import type { InlineCanvasFixture } from '../toolkit-file-support/CanvasCorpus.ts'

/** @anchor canvas-convert */
export class CanvasCorpusInlineTest {
  private constructor() {}

  static {
    const fixtures: readonly InlineCanvasFixture[] = CanvasCorpusFixtures.inlineCanvasFixtures()
    const base64Fixtures: readonly InlineCanvasFixture[] = fixtures.filter(
      (fixture: InlineCanvasFixture): boolean => fixture.format === 'base64',
    )
    const hjsonFixtures: readonly InlineCanvasFixture[] = fixtures.filter(
      (fixture: InlineCanvasFixture): boolean => fixture.format === 'hjson',
    )

    assert.equal(base64Fixtures.length, 108)
    assert.equal(hjsonFixtures.length, 1)

    test.each(fixtures)(
      '内联 Canvas 四向无损: $relativePath#$occurrence',
      (fixture: InlineCanvasFixture): void => {
        const document: ReturnType<typeof CanvasConverter.fromInline> = CanvasConverter.fromInline(
          fixture.text,
        )
        const expectedBytes: Uint8Array = CanvasConverter.toBinary(document)
        assert.deepEqual(CanvasConverter.toBinary(document), expectedBytes, fixture.relativePath)

        const base64: string = CanvasConverter.toInlineBase64(document)
        assert.equal(base64, fixture.format === 'base64' ? fixture.text : base64)
        assert.deepEqual(
          CanvasConverter.toBinary(CanvasConverter.fromInline(base64)),
          expectedBytes,
        )

        const hjson: string = CanvasConverter.toInlineHjson(document)
        assert.equal(hjson, fixture.format === 'hjson' ? fixture.text : hjson)
        assert.deepEqual(CanvasConverter.toBinary(CanvasConverter.fromInline(hjson)), expectedBytes)

        const htmlInline: string = CanvasConverter.toHtmlInline(document)
        assert.deepEqual(
          CanvasConverter.toBinary(CanvasConverter.fromHtml(htmlInline)),
          expectedBytes,
        )

        const html: string = CanvasConverter.toHtml(document)
        assert.deepEqual(CanvasConverter.toBinary(CanvasConverter.fromHtml(html)), expectedBytes)
      },
    )
  }
}
