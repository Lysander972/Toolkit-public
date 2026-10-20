import type { CanvasLayoutNode } from './CpuCanvasLayout.ts'
import type { CpuRenderSession } from './CpuRenderSession.ts'

/** @anchor canvas-render-image-composition */
export class CanvasImageComposition {
  private constructor() {}

  /** @anchor canvas-render-image-composition */
  public static draw(_node: CanvasLayoutNode, _session: CpuRenderSession): Promise<void> {
    return Promise.reject(new Error('TODO: 通过 Skia 原生能力合成图片、九宫格与遮罩'))
  }
}
