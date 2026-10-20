import type { CanvasTextRun } from './RenderContracts.ts'

/** @anchor canvas-render-rich-text */
export class RichTextParser {
  private constructor() {}

  /** @anchor canvas-render-rich-text */
  public static parse(_text: string): readonly CanvasTextRun[] {
    throw new Error('TODO: 按原版富文本方言准备 Skia 文本段')
  }
}
