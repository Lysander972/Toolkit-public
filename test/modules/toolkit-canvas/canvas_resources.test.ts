import assert from 'node:assert/strict'
import { test } from 'vitest'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import type { CanvasDocument } from '../../../src/toolkit/canvas/CanvasValues.ts'

/** @anchor canvas-resource-dependencies */
export class CanvasResourceTests {
  private constructor() {}

  static {
    test('四种格式提取相同图片与 Spine 依赖，排除节点 guid 和普通文本', (): void => {
      const guids: readonly [string, string, string, string, string, string, string, string] = [
        '1'.repeat(32),
        '2'.repeat(32),
        '3'.repeat(32),
        '4'.repeat(32),
        '5'.repeat(32),
        '6'.repeat(32),
        '7'.repeat(32),
        '8'.repeat(32),
      ]
      const [picture, image, atlas, skeleton, json, texture, secondTexture, text]: typeof guids =
        guids
      const html: string = `<BeCanvas name="依赖" width="1080" height="2160">
        <BeUIRect name="容器" slicedpicguid="${picture}">
          <BeUIRawImage name="图片" guid="42" fileguid="${image}" softmaskguid="${picture}"></BeUIRawImage>
          <BeUISpine name="动画" atlasguid="${atlas}" skelguid="${skeleton}" jsonguid="${json}" pngguids="${texture},${secondTexture},${texture}"></BeUISpine>
          <BeUILabel name="${text}" text="${text}"></BeUILabel>
          <BeUIButton name="空引用" slicedpicguid=""></BeUIButton>
        </BeUIRect>
      </BeCanvas>`
      const document: CanvasDocument = CanvasConverter.fromHtml(html)
      const bytes: Uint8Array = CanvasConverter.toBinary(document)
      const documents: readonly CanvasDocument[] = [
        document,
        CanvasConverter.fromBinary(bytes),
        CanvasConverter.fromInline(CanvasConverter.toInlineBase64(document)),
        CanvasConverter.fromInline(CanvasConverter.toInlineHjson(document)),
      ]
      for (const parsed of documents) {
        const references: readonly string[] = CanvasConverter.resourceGuids(parsed)
        assert.deepEqual(references, guids.slice(0, 7))
        assert.equal(Object.isFrozen(references), true)
        assert.deepEqual(CanvasConverter.toBinary(parsed), bytes)
      }
      assert.deepEqual(
        CanvasConverter.resourceGuids(
          CanvasConverter.fromHtml('<BeCanvas name="空" width="1" height="1"></BeCanvas>'),
        ),
        [],
      )
    })
  }
}
