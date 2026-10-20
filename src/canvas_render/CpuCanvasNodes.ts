import type { CanvasImageComposition } from './CanvasImageComposition.ts'

/** @anchor canvas-render-feature-projection */
export class CpuCanvasNodes {
  private constructor() {}

  /** @anchor canvas-render-feature-projection */
  public static draw(
    _node: Parameters<typeof CanvasImageComposition.draw>[0],
    _session: Parameters<typeof CanvasImageComposition.draw>[1],
  ): Promise<void> {
    return Promise.reject(new Error('TODO: 将确定布局与特性字段绘制到 CPU surface'))
  }
}
