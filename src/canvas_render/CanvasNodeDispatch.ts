import type { CanvasLayoutNode } from './CpuCanvasLayout.ts'
import type { CpuRenderSession } from './CpuRenderSession.ts'

/** @anchor canvas-render-node-render-order */
export class CanvasNodeDispatch {
  private constructor() {}

  /** @anchor canvas-render-node-render-order */
  public static draw(_node: CanvasLayoutNode, _session: CpuRenderSession): Promise<void> {
    return Promise.reject(new Error('TODO: 按节点类型与固定能力顺序调度同次布局绘制'))
  }
}
