import type { Canvas, CanvasKit } from 'canvaskit-wasm'
import type { CanvasViewport } from './RenderContracts.ts'

/** @anchor canvas-render-cpu-boundary */
export class CpuRasterSurface {
  private constructor(public readonly canvas: Canvas) {}

  /** @anchor canvas-render-cpu-boundary */
  public static create(_backend: CanvasKit, _viewport: CanvasViewport): Promise<CpuRasterSurface> {
    return Promise.reject(new Error('TODO: 创建 CanvasKit 软件 CPU surface'))
  }

  /** @anchor canvas-render-cpu-boundary */
  public encodePng(): Promise<Uint8Array> {
    return Promise.reject(new Error('TODO: 从 CPU surface 编码 PNG 字节'))
  }

  /** @anchor canvas-render-resource-settlement */
  public close(): void {
    throw new Error('TODO: 释放会话独占的 CPU surface')
  }
}
