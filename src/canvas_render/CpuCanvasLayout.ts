import type { CanvasDocument, CanvasNode } from '@toolkit/canvas/model'
import type { CanvasViewport } from './RenderContracts.ts'
import type { LayoutRect, Vector3 } from './CanvasTypesBase.ts'
import type { CpuRenderSession } from './CpuRenderSession.ts'
import type { RichTextParser } from './CanvasRuntimeUtils.ts'
import type { CanvasFieldValues } from './CanvasFieldValues.ts'
import type { Option } from 'effect'

/**
 * @anchor canvas-render-layout
 * @anchor canvas-render-layer-responsibilities
 * @anchor canvas-render-runtime-fields
 */
export interface CanvasLayoutNode {
  readonly source: CanvasNode
  readonly effective: ReturnType<typeof CanvasFieldValues.resolve>
  readonly textRuns: ReturnType<typeof RichTextParser.parse>
  readonly nodeIndices: readonly number[]
  readonly layout: LayoutRect
  readonly resolvedLayoutSize: Vector3
  readonly children: readonly CanvasLayoutNode[]
}

/** @anchor canvas-render-layout-diagnostics */
interface CanvasLayoutFailure {
  readonly nodeIndices: readonly number[]
  readonly computed: LayoutRect
  readonly message: string
}

/** @anchor canvas-render-layout */
export class CpuCanvasLayout {
  private constructor() {}

  /**
   * @anchor canvas-render-layout
   * @anchor canvas-render-child-layout
   * @anchor canvas-render-text-measurement
   */
  public static layout(
    _document: CanvasDocument,
    _viewport: CanvasViewport,
    _session: CpuRenderSession,
  ): CanvasLayoutNode {
    throw new Error('TODO: 按父最终尺寸和固定字体度量计算当前会话布局')
  }

  /** @anchor canvas-render-layout-diagnostics */
  public static diagnostic(_node: CanvasLayoutNode): Option.Option<CanvasLayoutFailure> {
    throw new Error('TODO: 读取同次布局失败的输入、计算结果与节点身份')
  }
}
