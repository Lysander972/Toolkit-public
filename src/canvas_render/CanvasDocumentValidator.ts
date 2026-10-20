import type { CanvasDocument } from '@toolkit/canvas/model'

/** @anchor canvas-render-dom-schema */
export class CanvasDocumentValidator {
  private constructor() {}

  /**
   * @anchor canvas-render-dom-schema
   * @anchor canvas-render-binary-dependencies
   */
  public static validate(_document: CanvasDocument): CanvasDocument {
    throw new Error('TODO: 从结构化 CanvasDocument 校验节点字段与依赖组')
  }
}
