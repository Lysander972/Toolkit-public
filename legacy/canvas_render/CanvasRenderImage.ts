import type { CanvasColor } from './CanvasTypesBase.ts'

/** @anchor canvas-render-gradient */
export class FourCornerGradient {
  /** @anchor canvas-render-gradient */
  public static generateDataUrl(
    width: number,
    height: number,
    topLeft: CanvasColor,
    topRight: CanvasColor,
    bottomLeft: CanvasColor,
    bottomRight: CanvasColor,
  ): string {
    const canvas: HTMLCanvasElement = document.createElement('canvas')
    canvas.width = Math.max(1, Math.round(width))
    canvas.height = Math.max(1, Math.round(height))
    const ctx: CanvasRenderingContext2D | null = canvas.getContext('2d')
    if (ctx === null) {
      throw new Error('FourCornerGradient 获取 2d 上下文失败')
    }

    const w: number = canvas.width
    const h: number = canvas.height
    const imageData: ImageData = ctx.createImageData(w, h)
    const data: Uint8ClampedArray = imageData.data

    const verticalFactors: Float64Array = Float64Array.from(
      { length: h },
      (_unused: unknown, row: number): number => (h > 1 ? row / (h - 1) : 0),
    )
    for (let x: number = 0; x < w; x++) {
      const tx: number = w > 1 ? x / (w - 1) : 0
      const topR: number = topLeft.r + (topRight.r - topLeft.r) * tx
      const topG: number = topLeft.g + (topRight.g - topLeft.g) * tx
      const topB: number = topLeft.b + (topRight.b - topLeft.b) * tx
      const topA: number = topLeft.a + (topRight.a - topLeft.a) * tx
      const bottomR: number = bottomLeft.r + (bottomRight.r - bottomLeft.r) * tx
      const bottomG: number = bottomLeft.g + (bottomRight.g - bottomLeft.g) * tx
      const bottomB: number = bottomLeft.b + (bottomRight.b - bottomLeft.b) * tx
      const bottomA: number = bottomLeft.a + (bottomRight.a - bottomLeft.a) * tx
      for (const [y, ty] of verticalFactors.entries()) {
        const idx: number = (y * w + x) * 4
        data[idx] = Math.round(topR + (bottomR - topR) * ty)
        data[idx + 1] = Math.round(topG + (bottomG - topG) * ty)
        data[idx + 2] = Math.round(topB + (bottomB - topB) * ty)
        data[idx + 3] = Math.round(topA + (bottomA - topA) * ty)
      }
    }

    ctx.putImageData(imageData, 0, 0)
    return canvas.toDataURL()
  }
}

/** @anchor canvas-render-image-composition */
export class ImageRenderUtils {
  public static readonly CANVAS_STYLE: string =
    'position:absolute;top:0;left:0;width:100%;height:100%;z-index:0;pointer-events:none;'

  /** @anchor canvas-render-image-composition */
  public static resolveDestinationSize(layoutSize: { width: number; height: number }): {
    width: number
    height: number
  } {
    return {
      width: Math.max(1, Math.round(layoutSize.width)),
      height: Math.max(1, Math.round(layoutSize.height)),
    }
  }

  /** @anchor canvas-render-image-composition */
  public static applyBlend(
    ctx: CanvasRenderingContext2D,
    w: number,
    h: number,
    color: CanvasColor,
  ): void {
    const r: number = Math.max(0, Math.min(255, Math.round(color.r)))
    const g: number = Math.max(0, Math.min(255, Math.round(color.g)))
    const b: number = Math.max(0, Math.min(255, Math.round(color.b)))
    const a: number = Math.max(0, Math.min(255, color.a)) / 255
    if (a <= 0) {
      ctx.clearRect(0, 0, w, h)
      return
    }

    const mask: HTMLCanvasElement = document.createElement('canvas')
    mask.width = w
    mask.height = h
    const mCtx: CanvasRenderingContext2D | null = mask.getContext('2d')
    if (mCtx === null) throw new Error('applyBlend 获取 mask 上下文失败')
    mCtx.drawImage(ctx.canvas, 0, 0, w, h)

    // 画布上下文只能整体消费;局部别名避免对入参属性直接赋值。
    const painter: CanvasRenderingContext2D = ctx
    painter.save()
    painter.globalCompositeOperation = 'multiply'
    painter.fillStyle = `rgb(${String(r)}, ${String(g)}, ${String(b)})`
    painter.fillRect(0, 0, w, h)
    painter.restore()

    painter.save()
    painter.globalCompositeOperation = 'destination-in'
    painter.globalAlpha = a
    painter.drawImage(mask, 0, 0, w, h)
    painter.restore()
  }

  /** @anchor canvas-render-image-composition */
  public static clearCanvas(el: HTMLElement): void {
    el.classList.remove('sliced')
    el.querySelectorAll('canvas.canvas-render-image').forEach((c: Element): void => {
      c.remove()
    })
    const s: CSSStyleDeclaration = el.style
    s.backgroundImage = ''
    s.backgroundBlendMode = ''
    s.backgroundSize = ''
    s.backgroundPosition = ''
    s.backgroundRepeat = ''
    s.removeProperty('-webkit-mask-image')
    s.removeProperty('mask-image')
    s.removeProperty('-webkit-mask-size')
    s.removeProperty('mask-size')
    s.removeProperty('-webkit-mask-position')
    s.removeProperty('mask-position')
  }
}
