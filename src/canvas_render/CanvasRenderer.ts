import type { CanvasKit } from 'canvaskit-wasm'
import type { CanvasRenderInput, CanvasRenderResult } from './RenderContracts.ts'

/**
 * @anchor canvas-render-cpu-boundary
 * @anchor canvas-render-module-flow
 * @anchor canvas-render-source-layout
 */
export class CanvasRenderer {
  private constructor() {}

  /**
   * @anchor canvas-render-cpu-boundary
   * @anchor canvas-render-dom-pipeline
   */
  public static render(
    _input: CanvasRenderInput,
    _backend: CanvasKit,
  ): Promise<CanvasRenderResult> {
    return Promise.reject(new Error('TODO: Canvas CPU 渲染'))
  }
}
