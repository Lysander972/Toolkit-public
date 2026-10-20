import type { CanvasKit } from 'canvaskit-wasm'
import type { CanvasRenderInput, CanvasRenderResult } from './RenderContracts.ts'
import type { CpuRasterSurface } from './CpuRasterSurface.ts'

/** @anchor canvas-render-single-entry */
export class CpuRenderSession {
  private constructor(
    public readonly input: CanvasRenderInput,
    public readonly surface: Awaited<ReturnType<typeof CpuRasterSurface.create>>,
  ) {}

  /** @anchor canvas-render-single-entry */
  public static create(_input: CanvasRenderInput, _backend: CanvasKit): Promise<CpuRenderSession> {
    return Promise.reject(new Error('TODO: 创建独立 CPU 渲染会话及资源作用域'))
  }

  /** @anchor canvas-render-async-state */
  public finish(): Promise<CanvasRenderResult> {
    return Promise.reject(new Error('TODO: 收束资源任务并交付 PNG 与实际诊断'))
  }

  /** @anchor canvas-render-resource-settlement */
  public close(): Promise<void> {
    return Promise.reject(new Error('TODO: 释放 surface、字体、图片与 Spine 数据'))
  }
}
