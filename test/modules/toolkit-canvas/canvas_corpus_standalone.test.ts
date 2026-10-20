import assert from 'node:assert/strict'
import { test } from 'vitest'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import { CanvasCorpusFixtures } from '../toolkit-file-support/CanvasCorpus.ts'
import type { BinaryCanvasFixture } from '../toolkit-file-support/CanvasCorpus.ts'

/** @anchor canvas-convert */
export class CanvasCorpusStandaloneTest {
  private constructor() {}

  static {
    const fixtures: readonly BinaryCanvasFixture[] = CanvasCorpusFixtures.binaryCanvasFixtures()
    assert.equal(fixtures.length, 63)
    test.each(fixtures)(
      '独立 Canvas 四向无损: $relativePath',
      (fixture: BinaryCanvasFixture): void => {
        const document: ReturnType<typeof CanvasConverter.fromBinary> = CanvasConverter.fromBinary(
          fixture.bytes,
        )
        CanvasCorpusStandaloneTest.assertFourWay(document, fixture.bytes, fixture.relativePath)
      },
    )
  }

  private static assertFourWay(
    document: ReturnType<typeof CanvasConverter.fromBinary>,
    expectedBytes: Uint8Array,
    source: string,
  ): void {
    assert.deepEqual(CanvasConverter.toBinary(document), expectedBytes, source)

    const base64: string = CanvasConverter.toInlineBase64(document)
    assert.deepEqual(
      CanvasConverter.toBinary(CanvasConverter.fromInline(base64)),
      expectedBytes,
      source,
    )

    const hjson: string = CanvasConverter.toInlineHjson(document)
    assert.deepEqual(
      CanvasConverter.toBinary(CanvasConverter.fromInline(hjson)),
      expectedBytes,
      source,
    )

    const htmlInline: string = CanvasConverter.toHtmlInline(document)
    assert.deepEqual(
      CanvasConverter.toBinary(CanvasConverter.fromHtml(htmlInline)),
      expectedBytes,
      source,
    )

    const html: string = CanvasConverter.toHtml(document)
    assert.deepEqual(
      CanvasConverter.toBinary(CanvasConverter.fromHtml(html)),
      expectedBytes,
      source,
    )
  }
}
