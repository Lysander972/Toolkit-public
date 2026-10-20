import {
  LayoutType,
  type CanvasColor,
  type Vector3,
  type LayoutRect,
  type BoxFields,
  type FillFields,
  type MaskFields,
  type ChildLayoutFields,
  type ShadowOutlineFields,
  type ButtonFields,
  type TextStyleFields,
  type ScrollFields,
  type InputFields,
  type LabelFields,
  type TextEffectFields,
  type RawImageFields,
  type SpineFields,
  type LayoutParams,
} from './CanvasTypesBase.ts'
import type { CanvasRuntimeNode } from './CanvasTypesBase.ts'
import {
  ValueResolver,
  CanvasColorValue,
  GuidUtils,
  RichTextParser,
  CanvasRenderEntry,
} from './CanvasRuntimeUtils.ts'
import { FeatureRenderBase, FeatureValues } from './CanvasRenderBase.ts'
import { ImageRenderUtils, FourCornerGradient } from './CanvasRenderImage.ts'
import { LayoutRenderer } from './CanvasRenderLayout.ts'
import type { RenderSession } from './CanvasRenderState.ts'
import { Option } from 'effect'
import { Array as ReadonlyArray } from 'effect'
import {
  Container,
  autoDetectRenderer,
  type BaseTexture,
  type IRenderer,
  type Texture,
} from 'pixi.js'
import {
  AtlasAttachmentLoader,
  SkeletonBinary,
  SkeletonJson,
  Spine,
  TextureAtlas,
  type SkeletonData,
} from '@pixi-spine/all-3.8'

/** @anchor canvas-render-box */
export class BoxRenderer extends FeatureRenderBase<BoxFields> {
  private static readonly INSTANCE: BoxRenderer = new BoxRenderer()

  private constructor() {
    super()
  }

  /** @anchor canvas-render-box */
  public static getInstance(): BoxRenderer {
    return BoxRenderer.INSTANCE
  }

  /** @anchor canvas-render-box */
  public renderColor(
    node: Pick<BoxFields, 'color'>,
    defaultvalue: Pick<BoxFields, 'color'>,
    runtime: CanvasRuntimeNode,
  ): CanvasColor {
    const effective: Option.Option<CanvasColor> = FeatureValues.resolve(
      node.color,
      defaultvalue.color,
      true,
      'Box.color',
    )
    const color: CanvasColor = ValueResolver.requirePresentValue(effective, 'Box.color')
    const elem: HTMLElement = ValueResolver.requirePresentValue(runtime.elem, 'Box.elem')
    elem.classList.toggle('canvas-graphic-alpha-zero', color.a === 0)
    elem.style.backgroundColor = CanvasColorValue.toCss(color)
    return color
  }

  /** @anchor canvas-render-box */
  public render(node: BoxFields, defaultvalue: BoxFields, runtime: CanvasRuntimeNode): void {
    const hint: string = 'Box'
    const color: CanvasColor = this.renderColor(node, defaultvalue, runtime)

    // 方框绘制特性：所有字段无条件参与（用DEFAULT填充空值）
    const effective: BoxFields = {
      color: Option.some(color),
      borderWidth: FeatureValues.resolve(
        node.borderWidth,
        defaultvalue.borderWidth,
        true,
        `${hint}.borderWidth`,
      ),
      radius: FeatureValues.resolve(node.radius, defaultvalue.radius, true, `${hint}.radius`),
      slicedPicGuid: FeatureValues.resolve(
        node.slicedPicGuid,
        defaultvalue.slicedPicGuid,
        true,
        `${hint}.slicedPicGuid`,
      ),
      slicedBorderLB: FeatureValues.resolve(
        node.slicedBorderLB,
        defaultvalue.slicedBorderLB,
        true,
        `${hint}.slicedBorderLB`,
      ),
      slicedBorderRT: FeatureValues.resolve(
        node.slicedBorderRT,
        defaultvalue.slicedBorderRT,
        true,
        `${hint}.slicedBorderRT`,
      ),
      slicedRectLB: FeatureValues.resolve(
        node.slicedRectLB,
        defaultvalue.slicedRectLB,
        true,
        `${hint}.slicedRectLB`,
      ),
      slicedRectRT: FeatureValues.resolve(
        node.slicedRectRT,
        defaultvalue.slicedRectRT,
        true,
        `${hint}.slicedRectRT`,
      ),
    }

    const elem: HTMLElement = ValueResolver.requirePresentValue(runtime.elem, `${hint}.elem`)

    // 应用圆角
    const radius: number = ValueResolver.requirePresentValue(effective.radius, `${hint}.radius`)
    elem.style.borderRadius = radius > 0 ? `${String(radius)}px` : ''

    const borderWidth: number = ValueResolver.requirePresentValue(
      effective.borderWidth,
      `${hint}.borderWidth`,
    )

    // 有九宫格时渲染九宫格图片（GUID 必须有效才走图片渲染，否则短路到颜色填充）
    const slicedPicGuid: string = Option.getOrThrow(effective.slicedPicGuid)
    const hasSlicedImage: boolean = GuidUtils.isValid(slicedPicGuid)

    if (hasSlicedImage) {
      elem.style.border = ''
      elem.style.outline = ''
      elem.style.outlineOffset = ''
      elem.style.backgroundColor = ''
      const layout: LayoutRect = ValueResolver.requirePresentValue(runtime.layout, `${hint}.layout`)
      this.renderSlicedImage(elem, effective, hint, layout, runtime.session)
      return
    }

    // 边框模式：borderWidth > 0 时中间透明
    // 使用 outline 模拟边框，避免 CSS border 改变盒模型几何
    if (borderWidth > 0) {
      elem.style.outline = `${String(borderWidth)}px solid ${CanvasColorValue.toCss(color)}`
      elem.style.outlineOffset = `${String(-borderWidth)}px`
      elem.style.backgroundColor = ''
    } else {
      elem.style.border = ''
      elem.style.outline = ''
      elem.style.outlineOffset = ''
      elem.style.backgroundColor = CanvasColorValue.toCss(color)
    }
  }

  private renderSlicedImage(
    el: HTMLElement,
    params: BoxFields,
    hint: string,
    layoutSize: { width: number; height: number },
    session: RenderSession,
  ): void {
    const slicedPicGuid: string = ValueResolver.requirePresentValue(
      params.slicedPicGuid,
      `${hint}.slicedPicGuid`,
    )
    const blendColor: CanvasColor = ValueResolver.requirePresentValue(params.color, `${hint}.color`)

    el.classList.add('has-image')
    const domPath: string = Option.getOrThrow(Option.fromNullishOr(el.getAttribute('data-path')))
    const imageUrl: string = GuidUtils.loadImage(slicedPicGuid, domPath)

    ImageRenderUtils.clearCanvas(el)

    // 注册异步图片加载
    const task: () => Promise<void> = (): Promise<void> =>
      new Promise<void>((resolve: () => void, reject: (reason: unknown) => void): void => {
        const img: HTMLImageElement = new Image()
        img.onload = (): void => {
          try {
            const texW: number = Math.max(1, img.width)
            const texH: number = Math.max(1, img.height)

            const destination: { width: number; height: number } =
              ImageRenderUtils.resolveDestinationSize(layoutSize)
            const destW: number = destination.width
            const destH: number = destination.height

            let srcX: number = 0
            let srcY: number = 0
            let srcW: number = texW
            let srcH: number = texH

            if (Option.isSome(params.slicedRectLB) && Option.isSome(params.slicedRectRT)) {
              const rectLeft: number = params.slicedRectLB.value.x
              const rectBottom: number = params.slicedRectLB.value.y
              const rectRight: number = params.slicedRectRT.value.x
              const rectTop: number = params.slicedRectRT.value.y

              const xMin: number = rectLeft
              const xMax: number = rectRight
              const yMin: number = rectBottom
              const yMax: number = rectTop

              const xInvalid: boolean = xMin < 0 || xMin > texW || xMax < xMin || xMax > texW
              const yInvalid: boolean = yMin < 0 || yMin > texH || yMax < yMin || yMax > texH

              if (!xInvalid) {
                srcX = xMin
                srcW = xMax - xMin
              }
              if (!yInvalid) {
                srcY = texH - yMax
                srcH = yMax - yMin
              }
            }

            const bLB: Vector3 = ValueResolver.requirePresentValue(
              params.slicedBorderLB,
              `${hint}.slicedBorderLB`,
            )
            const bRT: Vector3 = ValueResolver.requirePresentValue(
              params.slicedBorderRT,
              `${hint}.slicedBorderRT`,
            )

            let sbL: number = bLB.x
            let sbB: number = bLB.y
            let sbR: number = bRT.x
            let sbT: number = bRT.y
            if (sbL < 0 || sbL > texW) {
              sbL = 0
              sbR = 0
            }
            if (sbB < 0 || sbB > texH) {
              sbB = 0
              sbT = 0
            }

            const srcTotalBorderW: number = sbL + sbR
            const srcTotalBorderH: number = sbT + sbB
            if (srcTotalBorderW > srcW && srcTotalBorderW > 0) {
              const scale: number = srcW / srcTotalBorderW
              sbL *= scale
              sbR *= scale
            }
            if (srcTotalBorderH > srcH && srcTotalBorderH > 0) {
              const scale: number = srcH / srcTotalBorderH
              sbT *= scale
              sbB *= scale
            }

            let dbL: number = sbL
            let dbR: number = sbR
            let dbT: number = sbT
            let dbB: number = sbB
            const totalBW: number = dbL + dbR
            const totalBH: number = dbT + dbB
            if (destW < totalBW && totalBW > 0) {
              const s: number = destW / totalBW
              dbL *= s
              dbR *= s
            }
            if (destH < totalBH && totalBH > 0) {
              const s: number = destH / totalBH
              dbT *= s
              dbB *= s
            }

            const scW: number = Math.max(0, srcW - sbL - sbR)
            const scH: number = Math.max(0, srcH - sbT - sbB)
            const srcColX: number[] = [srcX, srcX + sbL, srcX + srcW - sbR]
            const srcColW: number[] = [sbL, scW, sbR]
            const srcRowY: number[] = [srcY, srcY + sbT, srcY + srcH - sbB]
            const srcRowH: number[] = [sbT, scH, sbB]

            const intDW: number = Math.max(1, Math.round(destW))
            const intDH: number = Math.max(1, Math.round(destH))
            let idbL: number = Math.round(dbL)
            let idbR: number = Math.round(dbR)
            let idbT: number = Math.round(dbT)
            let idbB: number = Math.round(dbB)

            if (idbL + idbR > intDW) {
              const s: number = intDW / (idbL + idbR)
              idbL = Math.floor(idbL * s)
              idbR = intDW - idbL
            }
            if (idbT + idbB > intDH) {
              const s: number = intDH / (idbT + idbB)
              idbT = Math.floor(idbT * s)
              idbB = intDH - idbT
            }

            const dcW: number = Math.max(0, intDW - idbL - idbR)
            const dcH: number = Math.max(0, intDH - idbT - idbB)
            const destColX: number[] = [0, idbL, intDW - idbR]
            const destColW: number[] = [idbL, dcW, idbR]
            const destRowY: number[] = [0, idbT, intDH - idbB]
            const destRowH: number[] = [idbT, dcH, idbB]

            const canvas: HTMLCanvasElement = document.createElement('canvas')
            canvas.classList.add('canvas-render-generated', 'canvas-render-image')
            canvas.width = intDW
            canvas.height = intDH
            canvas.style.cssText = ImageRenderUtils.CANVAS_STYLE

            const ctx: CanvasRenderingContext2D | null = canvas.getContext('2d')
            if (ctx === null) throw new Error('renderSlicedImage 获取 2d 上下文失败')

            const maxTX: number = Math.max(0, texW - 1)
            const maxTY: number = Math.max(0, texH - 1)

            for (let r: number = 0; r < 3; r++) {
              for (let c: number = 0; c < 3; c++) {
                let sw: number = ReadonlyArray.getUnsafe(srcColW, c)
                let sh: number = ReadonlyArray.getUnsafe(srcRowH, r)
                let dw: number = ReadonlyArray.getUnsafe(destColW, c)
                let dh: number = ReadonlyArray.getUnsafe(destRowH, r)

                if (dw <= 0 && dh <= 0) continue

                let sx: number = ReadonlyArray.getUnsafe(srcColX, c)
                let sy: number = ReadonlyArray.getUnsafe(srcRowY, r)

                if (sw <= 0) {
                  const sampleX: number = Math.max(
                    srcX,
                    Math.min(srcX + srcW, ReadonlyArray.getUnsafe(srcColX, c)),
                  )
                  sx = Math.max(0, Math.min(maxTX, sampleX))
                  sw = 1
                }
                if (sh <= 0) {
                  const sampleY: number = Math.max(
                    srcY,
                    Math.min(srcY + srcH, ReadonlyArray.getUnsafe(srcRowY, r)),
                  )
                  sy = Math.max(0, Math.min(maxTY, sampleY))
                  sh = 1
                }
                if (dw <= 0) dw = 1
                if (dh <= 0) dh = 1

                ctx.drawImage(
                  img,
                  sx,
                  sy,
                  sw,
                  sh,
                  ReadonlyArray.getUnsafe(destColX, c),
                  ReadonlyArray.getUnsafe(destRowY, r),
                  dw,
                  dh,
                )
              }
            }

            ImageRenderUtils.applyBlend(ctx, intDW, intDH, blendColor)
            el.insertBefore(canvas, el.firstChild)
            resolve()
          } catch (error) {
            reject(new Error('九宫格图片合成失败', { cause: error }))
          }
        }
        img.onerror = (e: string | Event): void => {
          // 错误时也要标记完成，否则状态永远pending
          CanvasRenderEntry.reportCanvasPreviewError('sliced-image-load', e, 'warning')
          resolve()
        }
        img.src = imageUrl
      })
    session.track(task)
  }
}

/** @anchor canvas-render-mask */
export class MaskRenderer extends FeatureRenderBase<MaskFields> {
  private static readonly INSTANCE: MaskRenderer = new MaskRenderer()

  private constructor() {
    super()
  }

  /** @anchor canvas-render-mask */
  public static getInstance(): MaskRenderer {
    return MaskRenderer.INSTANCE
  }

  /** @anchor canvas-render-mask */
  public render(node: MaskFields, defaultvalue: MaskFields, runtime: CanvasRuntimeNode): void {
    const hint: string = 'Mask'

    const effective: MaskFields = {
      mask: FeatureValues.resolve(node.mask, defaultvalue.mask, true, `${hint}.mask`),
    }

    const mask: boolean = ValueResolver.requirePresentValue(effective.mask, `${hint}.mask`)

    const elem: HTMLElement = ValueResolver.requirePresentValue(runtime.elem, `${hint}.elem`)
    elem.style.overflow = mask ? 'hidden' : ''
    elem.classList.toggle('canvas-mask-enabled', mask)
  }
}

/** @anchor canvas-render-fill */
export class FillRenderer extends FeatureRenderBase<FillFields> {
  private static readonly INSTANCE: FillRenderer = new FillRenderer()

  private constructor() {
    super()
  }

  /** @anchor canvas-render-fill */
  public static getInstance(): FillRenderer {
    return FillRenderer.INSTANCE
  }

  /** @anchor canvas-render-fill */
  public render(node: FillFields, defaultvalue: FillFields, runtime: CanvasRuntimeNode): void {
    const hint: string = 'Fill'

    const effective: FillFields = {
      fillMethod: FeatureValues.resolve(
        node.fillMethod,
        defaultvalue.fillMethod,
        true,
        `${hint}.fillMethod`,
      ),
      fillAmount: FeatureValues.resolve(
        node.fillAmount,
        defaultvalue.fillAmount,
        true,
        `${hint}.fillAmount`,
      ),
    }

    const fillMethod: number = ValueResolver.requirePresentValue(
      effective.fillMethod,
      `${hint}.fillMethod`,
    )
    const fillAmount: number = ValueResolver.requirePresentValue(
      effective.fillAmount,
      `${hint}.fillAmount`,
    )

    const elem: HTMLElement = ValueResolver.requirePresentValue(runtime.elem, `${hint}.elem`)

    if (fillMethod < 0) {
      elem.style.clipPath = ''
      return
    }

    elem.style.clipPath = LayoutRenderer.getFillClipPath(fillMethod, fillAmount)
  }
}

/** @anchor canvas-render-scroll */
export class ScrollRenderer extends FeatureRenderBase<ScrollFields> {
  private static readonly INSTANCE: ScrollRenderer = new ScrollRenderer()

  private constructor() {
    super()
  }

  /** @anchor canvas-render-scroll */
  public static getInstance(): ScrollRenderer {
    return ScrollRenderer.INSTANCE
  }

  /** @anchor canvas-render-scroll */
  public render(node: ScrollFields, defaultvalue: ScrollFields, runtime: CanvasRuntimeNode): void {
    const hint: string = 'Scroll'

    const effective: ScrollFields = {
      scrollHorizontalDisabled: FeatureValues.resolve(
        node.scrollHorizontalDisabled,
        defaultvalue.scrollHorizontalDisabled,
        true,
        `${hint}.scrollHorizontalDisabled`,
      ),
      scrollVerticalDisabled: FeatureValues.resolve(
        node.scrollVerticalDisabled,
        defaultvalue.scrollVerticalDisabled,
        true,
        `${hint}.scrollVerticalDisabled`,
      ),
    }

    const scrollHorizontalDisabled: boolean = ValueResolver.requirePresentValue(
      effective.scrollHorizontalDisabled,
      `${hint}.scrollHorizontalDisabled`,
    )
    const scrollVerticalDisabled: boolean = ValueResolver.requirePresentValue(
      effective.scrollVerticalDisabled,
      `${hint}.scrollVerticalDisabled`,
    )

    if (!scrollHorizontalDisabled && !scrollVerticalDisabled) {
      return
    }

    const elem: HTMLElement = ValueResolver.requirePresentValue(runtime.elem, `${hint}.elem`)

    elem.classList.add('ScrollRect')
    const indicator: HTMLDivElement = document.createElement('div')
    indicator.className = 'canvas-render-generated scroll-indicator'

    const hSpan: HTMLSpanElement = document.createElement('span')
    hSpan.textContent = '↔'
    hSpan.title = '水平滚动'
    if (scrollHorizontalDisabled) {
      hSpan.classList.add('disabled')
      hSpan.title = '水平滚动已禁用'
    }
    indicator.appendChild(hSpan)

    const vSpan: HTMLSpanElement = document.createElement('span')
    vSpan.textContent = '↕'
    vSpan.title = '垂直滚动'
    if (scrollVerticalDisabled) {
      vSpan.classList.add('disabled')
      vSpan.title = '垂直滚动已禁用'
    }
    indicator.appendChild(vSpan)

    elem.appendChild(indicator)
  }
}

/** @anchor canvas-render-child-layout */
export class ChildLayoutRenderer extends FeatureRenderBase<ChildLayoutFields> {
  private static readonly INSTANCE: ChildLayoutRenderer = new ChildLayoutRenderer()

  private constructor() {
    super()
  }

  /** @anchor canvas-render-child-layout */
  public static getInstance(): ChildLayoutRenderer {
    return ChildLayoutRenderer.INSTANCE
  }

  /** @anchor canvas-render-child-layout */
  private static inferLayoutType(node: ChildLayoutFields): LayoutType {
    // GRID: 有 cellSize 和 layoutSpacing
    if (Option.isSome(node.cellSize) && Option.isSome(node.layoutSpacing)) {
      return LayoutType.GRID
    }
    // VERTICAL: 有 verticalSpacing
    if (Option.isSome(node.verticalSpacing)) {
      return LayoutType.VERTICAL
    }
    // HORIZONTAL: 有 horizontalSpacing
    if (Option.isSome(node.horizontalSpacing)) {
      return LayoutType.HORIZONTAL
    }
    return LayoutType.NONE
  }

  /** @anchor canvas-render-child-layout */
  public render(
    node: ChildLayoutFields,
    defaultvalue: ChildLayoutFields,
  ): Option.Option<LayoutParams> {
    const hint: string = 'ChildLayout'

    // 推导排版类型
    const layoutType: LayoutType = ChildLayoutRenderer.inferLayoutType(node)
    if (layoutType === LayoutType.NONE) {
      return Option.none()
    }

    const grid: boolean = layoutType === LayoutType.GRID
    const effective: ChildLayoutFields = {
      layoutPadding: FeatureValues.resolve(
        node.layoutPadding,
        defaultvalue.layoutPadding,
        true,
        `${hint}.layoutPadding`,
      ),
      childAlignment: FeatureValues.resolve(
        node.childAlignment,
        defaultvalue.childAlignment,
        true,
        `${hint}.childAlignment`,
      ),
      childExpandWidth: FeatureValues.resolve(
        node.childExpandWidth,
        defaultvalue.childExpandWidth,
        true,
        `${hint}.childExpandWidth`,
      ),
      childExpandHeight: FeatureValues.resolve(
        node.childExpandHeight,
        defaultvalue.childExpandHeight,
        true,
        `${hint}.childExpandHeight`,
      ),
      cellSize: FeatureValues.resolve(
        node.cellSize,
        defaultvalue.cellSize,
        grid,
        `${hint}.cellSize`,
      ),
      layoutSpacing: FeatureValues.resolve(
        node.layoutSpacing,
        defaultvalue.layoutSpacing,
        grid,
        `${hint}.layoutSpacing`,
      ),
      verticalSpacing: FeatureValues.resolve(
        node.verticalSpacing,
        defaultvalue.verticalSpacing,
        layoutType === LayoutType.VERTICAL,
        `${hint}.verticalSpacing`,
      ),
      horizontalSpacing: FeatureValues.resolve(
        node.horizontalSpacing,
        defaultvalue.horizontalSpacing,
        layoutType === LayoutType.HORIZONTAL,
        `${hint}.horizontalSpacing`,
      ),
    }

    // ========== 通用字段（所有排版类型都需要） ==========
    const layoutPadding: Vector3 = ValueResolver.requirePresentValue(
      effective.layoutPadding,
      `${hint}.layoutPadding`,
    )
    const childAlignment: number = ValueResolver.requirePresentValue(
      effective.childAlignment,
      `${hint}.childAlignment`,
    )
    ValueResolver.requirePresentValue(effective.childExpandWidth, `${hint}.childExpandWidth`)
    ValueResolver.requirePresentValue(effective.childExpandHeight, `${hint}.childExpandHeight`)

    // ========== 类型特定字段 ==========
    // GRID: 需要 cellSize + layoutSpacing
    // VERTICAL: 需要 verticalSpacing，无 cellSize
    // HORIZONTAL: 需要 horizontalSpacing，无 cellSize
    // 将排版参数写入节点运行时上下文
    if (layoutType === LayoutType.GRID) {
      return Option.some({
        layoutType,
        layoutPadding,
        childAlignment,
        cellSize: ValueResolver.requirePresentValue(effective.cellSize, `${hint}.cellSize`),
        layoutSpacing: ValueResolver.requirePresentValue(
          effective.layoutSpacing,
          `${hint}.layoutSpacing`,
        ),
      })
    } else if (layoutType === LayoutType.VERTICAL) {
      return Option.some({
        layoutType,
        layoutPadding,
        childAlignment,
        verticalSpacing: ValueResolver.requirePresentValue(
          effective.verticalSpacing,
          `${hint}.verticalSpacing`,
        ),
      })
    } else {
      return Option.some({
        layoutType,
        layoutPadding,
        childAlignment,
        horizontalSpacing: ValueResolver.requirePresentValue(
          effective.horizontalSpacing,
          `${hint}.horizontalSpacing`,
        ),
      })
    }
  }
}

/** @anchor canvas-render-shadow */
export class ShadowOutlineRenderer extends FeatureRenderBase<ShadowOutlineFields> {
  private static readonly INSTANCE: ShadowOutlineRenderer = new ShadowOutlineRenderer()

  private constructor() {
    super()
  }

  /** @anchor canvas-render-shadow */
  public static getInstance(): ShadowOutlineRenderer {
    return ShadowOutlineRenderer.INSTANCE
  }

  /** @anchor canvas-render-shadow */
  public render(
    node: ShadowOutlineFields,
    defaultvalue: ShadowOutlineFields,
    runtime: CanvasRuntimeNode,
  ): void {
    const hint: string = 'ShadowOutline'

    const effective: ShadowOutlineFields = {
      shadowColor: FeatureValues.resolve(
        node.shadowColor,
        defaultvalue.shadowColor,
        true,
        `${hint}.shadowColor`,
      ),
      shadewDistance: FeatureValues.resolve(
        node.shadewDistance,
        defaultvalue.shadewDistance,
        true,
        `${hint}.shadewDistance`,
      ),
      outlineColor: FeatureValues.resolve(
        node.outlineColor,
        defaultvalue.outlineColor,
        true,
        `${hint}.outlineColor`,
      ),
      outlineDistance: FeatureValues.resolve(
        node.outlineDistance,
        defaultvalue.outlineDistance,
        true,
        `${hint}.outlineDistance`,
      ),
    }

    const shadowColor: CanvasColor = ValueResolver.requirePresentValue(
      effective.shadowColor,
      `${hint}.shadowColor`,
    )
    const shadewDistance: Vector3 = ValueResolver.requirePresentValue(
      effective.shadewDistance,
      `${hint}.shadewDistance`,
    )
    const outlineColor: CanvasColor = ValueResolver.requirePresentValue(
      effective.outlineColor,
      `${hint}.outlineColor`,
    )
    const outlineDistance: Vector3 = ValueResolver.requirePresentValue(
      effective.outlineDistance,
      `${hint}.outlineDistance`,
    )

    const elem: HTMLElement = ValueResolver.requirePresentValue(runtime.elem, `${hint}.elem`)

    elem.style.boxShadow = ''
    elem.style.removeProperty('--canvas-graphic-shadow')
    // 统一参数序列（偏移/模糊/颜色），落载体由图形形态决定
    const shadows: Array<{ x: number; y: number; blur: number; color: string }> = []

    // 应用阴影
    if (shadowColor.a > 0 && (shadewDistance.x !== 0 || shadewDistance.y !== 0)) {
      shadows.push({
        x: shadewDistance.x,
        y: -shadewDistance.y,
        blur: shadewDistance.z,
        color: CanvasColorValue.toCss(shadowColor),
      })
    }

    // 原版 Unity Outline 是四个对角副本，不是上下左右十字扩边。
    if (outlineColor.a > 0 && outlineDistance.x !== 0) {
      const d: number = outlineDistance.x
      const color: string = CanvasColorValue.toCss(outlineColor)
      shadows.push({ x: d, y: d, blur: 0, color })
      shadows.push({ x: d, y: -d, blur: 0, color })
      shadows.push({ x: -d, y: d, blur: 0, color })
      shadows.push({ x: -d, y: -d, blur: 0, color })
    }

    if (shadows.length === 0) {
      return
    }

    if (elem.classList.contains('has-image')) {
      // 纹理图形：贴非透明像素轮廓；--canvas-graphic-shadow 由静态表转发到图形 canvas
      elem.style.setProperty(
        '--canvas-graphic-shadow',
        shadows
          .map(
            (s: { x: number; y: number; blur: number; color: string }): string =>
              `drop-shadow(${String(s.x)}px ${String(s.y)}px ${String(s.blur)}px ${s.color})`,
          )
          .join(' '),
      )
    } else {
      elem.style.boxShadow = shadows
        .map(
          (s: { x: number; y: number; blur: number; color: string }): string =>
            `${String(s.x)}px ${String(s.y)}px ${String(s.blur)}px ${s.color}`,
        )
        .join(', ')
    }
  }
}

/** @anchor canvas-render-button */
export class ButtonRenderer extends FeatureRenderBase<ButtonFields> {
  private static readonly INSTANCE: ButtonRenderer = new ButtonRenderer()

  private constructor() {
    super()
  }

  /** @anchor canvas-render-button */
  public static getInstance(): ButtonRenderer {
    return ButtonRenderer.INSTANCE
  }

  /** @anchor canvas-render-button */
  public render(node: ButtonFields, defaultvalue: ButtonFields, runtime: CanvasRuntimeNode): void {
    const hint: string = 'Button'

    const effective: ButtonFields = {
      label: FeatureValues.resolve(node.label, defaultvalue.label, true, `${hint}.label`),
      color: FeatureValues.resolve(node.color, defaultvalue.color, true, `${hint}.color`),
      clickCBName: FeatureValues.resolve(
        node.clickCBName,
        defaultvalue.clickCBName,
        true,
        `${hint}.clickCBName`,
      ),
      pressCBName: FeatureValues.resolve(
        node.pressCBName,
        defaultvalue.pressCBName,
        true,
        `${hint}.pressCBName`,
      ),
      releaseCBName: FeatureValues.resolve(
        node.releaseCBName,
        defaultvalue.releaseCBName,
        true,
        `${hint}.releaseCBName`,
      ),
      size: FeatureValues.resolve(node.size, defaultvalue.size, true, `${hint}.size`),
    }

    const buttonText: string = ValueResolver.requirePresentValue(effective.label, `${hint}.label`)
    const textColor: CanvasColor = ValueResolver.requirePresentValue(
      effective.color,
      `${hint}.color`,
    )
    const resolvedLayoutSize: Vector3 = ValueResolver.requirePresentValue(
      runtime.resolvedLayoutSize,
      `${hint}.resolvedLayoutSize`,
    )

    const elem: HTMLElement = ValueResolver.requirePresentValue(runtime.elem, `${hint}.elem`)

    // 计算自动字号
    const autoFontSize: number = RichTextParser.calculateBestFitFontSizeForLegacyText({
      text: buttonText,
      size: resolvedLayoutSize,
      minFontSize: RichTextParser.BUTTON_LEGACY_MIN_SIZE,
      maxFontSize: RichTextParser.BUTTON_LEGACY_MAX_SIZE,
      lineSpacing: 1,
    })

    // 创建按钮文字元素
    const textSpan: HTMLSpanElement = document.createElement('span')
    textSpan.classList.add('canvas-render-generated', 'button-text')

    // 检测是否有富文本标签
    if (RichTextParser.hasRichTextTags(buttonText)) {
      const richTextContainer: HTMLElement = RichTextParser.parse(
        buttonText,
        autoFontSize,
        textColor,
        1, // 按钮默认行间距
        false, // 按钮不是 TextPro
        0,
      )
      richTextContainer.style.display = 'flex'
      richTextContainer.style.justifyContent = 'center'
      richTextContainer.style.alignItems = 'center'
      richTextContainer.style.width = '100%'
      richTextContainer.style.height = '100%'
      textSpan.appendChild(richTextContainer)
    } else {
      textSpan.textContent = buttonText
      textSpan.style.color = CanvasColorValue.toCss(textColor)
      textSpan.style.fontSize = `${String(autoFontSize)}px`
    }

    // 固定居中对齐
    textSpan.style.display = 'flex'
    textSpan.style.justifyContent = 'center'
    textSpan.style.alignItems = 'center'
    textSpan.style.width = '100%'
    textSpan.style.height = '100%'
    textSpan.style.position = 'absolute'
    textSpan.style.top = '0'
    textSpan.style.left = '0'

    elem.appendChild(textSpan)
  }
}

/** @anchor canvas-render-text-style */
export class TextStyleRenderer extends FeatureRenderBase<TextStyleFields> {
  private static readonly INSTANCE: TextStyleRenderer = new TextStyleRenderer()

  private constructor() {
    super()
  }

  /** @anchor canvas-render-text-style */
  public static getInstance(): TextStyleRenderer {
    return TextStyleRenderer.INSTANCE
  }

  /** @anchor canvas-render-text-style */
  public render(
    node: Pick<TextStyleFields, 'fontSize' | 'textAlignment'>,
    defaultvalue: Pick<TextStyleFields, 'fontSize' | 'textAlignment'>,
    runtime: CanvasRuntimeNode,
  ): void {
    const hint: string = 'TextStyle'

    const effective: Pick<TextStyleFields, 'fontSize' | 'textAlignment'> = {
      fontSize: FeatureValues.resolve(
        node.fontSize,
        defaultvalue.fontSize,
        true,
        `${hint}.fontSize`,
      ),
      textAlignment: FeatureValues.resolve(
        node.textAlignment,
        defaultvalue.textAlignment,
        true,
        `${hint}.textAlignment`,
      ),
    }

    const fontSize: number = ValueResolver.requirePresentValue(
      effective.fontSize,
      `${hint}.fontSize`,
    )
    const textAlignment: number = ValueResolver.requirePresentValue(
      effective.textAlignment,
      `${hint}.textAlignment`,
    )

    const elem: HTMLElement = ValueResolver.requirePresentValue(runtime.elem, `${hint}.elem`)

    // 设置字号
    elem.style.setProperty('--canvas-font-size', `${String(fontSize)}px`)
    elem.style.fontSize = `${String(fontSize)}px`

    // 设置对齐方式
    const alignStyles: {
      justifyContent: string
      alignItems: string
      textAlign: string
    } = LayoutRenderer.getAlignmentStyles(textAlignment)
    elem.style.justifyContent = alignStyles.justifyContent
    elem.style.alignItems = alignStyles.alignItems
    elem.style.textAlign = alignStyles.textAlign
  }
}

/** @anchor canvas-render-input */
export class InputRenderer extends FeatureRenderBase<InputFields> {
  private static readonly INSTANCE: InputRenderer = new InputRenderer()

  private constructor() {
    super()
  }

  /** @anchor canvas-render-input */
  public static getInstance(): InputRenderer {
    return InputRenderer.INSTANCE
  }

  /** @anchor canvas-render-input */
  public render(node: InputFields, defaultvalue: InputFields, runtime: CanvasRuntimeNode): void {
    const hint: string = 'Input'

    const effective: InputFields = {
      multiLine: FeatureValues.resolve(
        node.multiLine,
        defaultvalue.multiLine,
        true,
        `${hint}.multiLine`,
      ),
      contentType: FeatureValues.resolve(
        node.contentType,
        defaultvalue.contentType,
        true,
        `${hint}.contentType`,
      ),
      labelColor: FeatureValues.resolve(
        node.labelColor,
        defaultvalue.labelColor,
        true,
        `${hint}.labelColor`,
      ),
      text: FeatureValues.resolve(node.text, defaultvalue.text, true, `${hint}.text`),
    }

    const multiLine: boolean = ValueResolver.requirePresentValue(
      effective.multiLine,
      `${hint}.multiLine`,
    )
    const contentType: number = ValueResolver.requirePresentValue(
      effective.contentType,
      `${hint}.contentType`,
    )
    const labelColor: CanvasColor = ValueResolver.requirePresentValue(
      effective.labelColor,
      `${hint}.labelColor`,
    )

    const elem: HTMLElement = ValueResolver.requirePresentValue(runtime.elem, `${hint}.elem`)

    if (multiLine) {
      elem.classList.add('multiline')
    }

    // ========== 文本内容渲染 ==========
    const displayText: string = Option.getOrThrow(effective.text)
    const text: HTMLSpanElement = document.createElement('span')
    text.classList.add('canvas-render-generated', 'canvas-render-text')
    text.dataset.contentType = contentType.toString()
    text.style.color = CanvasColorValue.toCss(labelColor)
    text.textContent = displayText
    elem.appendChild(text)
  }
}

/** @anchor canvas-render-label */
export class LabelRenderer extends FeatureRenderBase<LabelFields> {
  private static readonly INSTANCE: LabelRenderer = new LabelRenderer()

  private constructor() {
    super()
  }

  /** @anchor canvas-render-label */
  public static getInstance(): LabelRenderer {
    return LabelRenderer.INSTANCE
  }

  /** @anchor canvas-render-label */
  public render(node: LabelFields, defaultvalue: LabelFields, runtime: CanvasRuntimeNode): void {
    const hint: string = 'Label'

    const effective: LabelFields = {
      color: FeatureValues.resolve(node.color, defaultvalue.color, true, `${hint}.color`),
      boolBestFit: FeatureValues.resolve(
        node.boolBestFit,
        defaultvalue.boolBestFit,
        true,
        `${hint}.boolBestFit`,
      ),
      outlineColor: FeatureValues.resolve(
        node.outlineColor,
        defaultvalue.outlineColor,
        true,
        `${hint}.outlineColor`,
      ),
      outlineDistance: FeatureValues.resolve(
        node.outlineDistance,
        defaultvalue.outlineDistance,
        true,
        `${hint}.outlineDistance`,
      ),
      outlineWidth: FeatureValues.resolve(
        node.outlineWidth,
        defaultvalue.outlineWidth,
        true,
        `${hint}.outlineWidth`,
      ),
      text: FeatureValues.resolve(node.text, defaultvalue.text, true, `${hint}.text`),
      fontSize: FeatureValues.resolve(
        node.fontSize,
        defaultvalue.fontSize,
        true,
        `${hint}.fontSize`,
      ),
      lineSpacing: FeatureValues.resolve(
        node.lineSpacing,
        defaultvalue.lineSpacing,
        true,
        `${hint}.lineSpacing`,
      ),
      size: FeatureValues.resolve(node.size, defaultvalue.size, true, `${hint}.size`),
      lightFont: Option.none(),
    }

    const color: CanvasColor = ValueResolver.requirePresentValue(effective.color, `${hint}.color`)
    const boolBestFit: boolean = ValueResolver.requirePresentValue(
      effective.boolBestFit,
      `${hint}.boolBestFit`,
    )
    const outlineColor: CanvasColor = ValueResolver.requirePresentValue(
      effective.outlineColor,
      `${hint}.outlineColor`,
    )
    const outlineDistance: Vector3 = ValueResolver.requirePresentValue(
      effective.outlineDistance,
      `${hint}.outlineDistance`,
    )
    const outlineWidth: number = ValueResolver.requirePresentValue(
      effective.outlineWidth,
      `${hint}.outlineWidth`,
    )

    const elem: HTMLElement = ValueResolver.requirePresentValue(runtime.elem, `${hint}.elem`)

    const displayText: string = ValueResolver.requirePresentValue(effective.text, `${hint}.text`)
    const resolvedLayoutSize: Vector3 = ValueResolver.requirePresentValue(
      runtime.resolvedLayoutSize,
      `${hint}.resolvedLayoutSize`,
    )
    const lineSpacing: number = ValueResolver.requirePresentValue(
      effective.lineSpacing,
      `${hint}.lineSpacing`,
    )
    const fontSize: number = boolBestFit
      ? RichTextParser.calculateBestFitFontSizeForLegacyText({
          text: displayText,
          size: resolvedLayoutSize,
          minFontSize: RichTextParser.BEST_FIT_FALLBACK_MIN_SIZE,
          maxFontSize: RichTextParser.LEGACY_TEXT_DEFAULT_MAX_SIZE,
          lineSpacing,
        })
      : ValueResolver.requirePresentValue(effective.fontSize, `${hint}.fontSize`)

    elem.style.setProperty('--canvas-font-size', `${String(fontSize)}px`)
    elem.style.fontSize = `${String(fontSize)}px`

    // 应用整体颜色
    elem.style.color = CanvasColorValue.toCss(color)

    // 标准字体（新版原版已移除纤细字体变体，lightFont 不参与渲染）
    elem.style.fontWeight = '400'

    elem.style.textShadow = ''

    // 描边（按 Unity Outline 的四个对角副本模拟）
    if (outlineColor.a > 0 && outlineDistance.x !== 0) {
      const d: number = outlineDistance.x
      const c: string = CanvasColorValue.toCss(outlineColor)
      elem.style.textShadow = `${String(d)}px ${String(d)}px 0 ${c}, ${String(d)}px ${String(-d)}px 0 ${c}, ${String(-d)}px ${String(d)}px 0 ${c}, ${String(-d)}px ${String(-d)}px 0 ${c}`
    }
    // 备选：outlineWidth > 0 时用文字颜色做描边
    else if (outlineWidth > 0) {
      const TMP_SDF_RATIO: number = 0.1
      const actualOutlinePx: number = outlineWidth * fontSize * TMP_SDF_RATIO
      const cssColor: string = CanvasColorValue.toCss(color)
      const shadow: string = `${String(actualOutlinePx)}px ${String(actualOutlinePx)}px 0 ${cssColor},
                       ${String(actualOutlinePx)}px ${String(-actualOutlinePx)}px 0 ${cssColor},
                       ${String(-actualOutlinePx)}px ${String(actualOutlinePx)}px 0 ${cssColor},
                       ${String(-actualOutlinePx)}px ${String(-actualOutlinePx)}px 0 ${cssColor}`
      elem.style.textShadow = shadow
    }

    // ========== 富文本解析 + 文本内容渲染 ==========
    // 统一使用 RichTextParser 处理（处理 \n 转义和富文本标签）
    const richTextContainer: HTMLElement = RichTextParser.parse(
      displayText,
      fontSize,
      color,
      lineSpacing,
      false,
      0,
    )
    richTextContainer.style.display = 'block'
    richTextContainer.style.width = '100%'
    elem.appendChild(richTextContainer)
  }
}

/** @anchor canvas-render-text-effect */
export class TextEffectRenderer extends FeatureRenderBase<TextEffectFields> {
  private static readonly INSTANCE: TextEffectRenderer = new TextEffectRenderer()

  private constructor() {
    super()
  }

  /** @anchor canvas-render-text-effect */
  public static getInstance(): TextEffectRenderer {
    return TextEffectRenderer.INSTANCE
  }

  /** @anchor canvas-render-text-effect-values */
  private static resolve(node: TextEffectFields, defaultvalue: TextEffectFields): TextEffectFields {
    const hint: string = 'TextEffect'

    return {
      color: FeatureValues.resolve(node.color, defaultvalue.color, true, `${hint}.color`),
      boolBestFit: FeatureValues.resolve(
        node.boolBestFit,
        defaultvalue.boolBestFit,
        true,
        `${hint}.boolBestFit`,
      ),
      outlineColor: FeatureValues.resolve(
        node.outlineColor,
        defaultvalue.outlineColor,
        true,
        `${hint}.outlineColor`,
      ),
      outlineDistance: FeatureValues.resolve(
        node.outlineDistance,
        defaultvalue.outlineDistance,
        true,
        `${hint}.outlineDistance`,
      ),
      outlineWidth: FeatureValues.resolve(
        node.outlineWidth,
        defaultvalue.outlineWidth,
        true,
        `${hint}.outlineWidth`,
      ),
      faceDilate: FeatureValues.resolve(
        node.faceDilate,
        defaultvalue.faceDilate,
        true,
        `${hint}.faceDilate`,
      ),
      gradientTopLeft: FeatureValues.resolve(
        node.gradientTopLeft,
        defaultvalue.gradientTopLeft,
        true,
        `${hint}.gradientTopLeft`,
      ),
      gradientTopRight: FeatureValues.resolve(
        node.gradientTopRight,
        defaultvalue.gradientTopRight,
        true,
        `${hint}.gradientTopRight`,
      ),
      gradientBottomLeft: FeatureValues.resolve(
        node.gradientBottomLeft,
        defaultvalue.gradientBottomLeft,
        true,
        `${hint}.gradientBottomLeft`,
      ),
      gradientBottomRight: FeatureValues.resolve(
        node.gradientBottomRight,
        defaultvalue.gradientBottomRight,
        true,
        `${hint}.gradientBottomRight`,
      ),
      vertexGradientEnabled: FeatureValues.resolve(
        node.vertexGradientEnabled,
        defaultvalue.vertexGradientEnabled,
        true,
        `${hint}.vertexGradientEnabled`,
      ),
      underlayEnabled: FeatureValues.resolve(
        node.underlayEnabled,
        defaultvalue.underlayEnabled,
        true,
        `${hint}.underlayEnabled`,
      ),
      underlayColor: FeatureValues.resolve(
        node.underlayColor,
        defaultvalue.underlayColor,
        true,
        `${hint}.underlayColor`,
      ),
      underlayOffset: FeatureValues.resolve(
        node.underlayOffset,
        defaultvalue.underlayOffset,
        true,
        `${hint}.underlayOffset`,
      ),
      underlaySoftness: FeatureValues.resolve(
        node.underlaySoftness,
        defaultvalue.underlaySoftness,
        true,
        `${hint}.underlaySoftness`,
      ),
      glowEnabled: FeatureValues.resolve(
        node.glowEnabled,
        defaultvalue.glowEnabled,
        true,
        `${hint}.glowEnabled`,
      ),
      glowColor: FeatureValues.resolve(
        node.glowColor,
        defaultvalue.glowColor,
        true,
        `${hint}.glowColor`,
      ),
      glowOffset: FeatureValues.resolve(
        node.glowOffset,
        defaultvalue.glowOffset,
        true,
        `${hint}.glowOffset`,
      ),
      glowInner: FeatureValues.resolve(
        node.glowInner,
        defaultvalue.glowInner,
        true,
        `${hint}.glowInner`,
      ),
      glowOuter: FeatureValues.resolve(
        node.glowOuter,
        defaultvalue.glowOuter,
        true,
        `${hint}.glowOuter`,
      ),
      glowPower: FeatureValues.resolve(
        node.glowPower,
        defaultvalue.glowPower,
        true,
        `${hint}.glowPower`,
      ),
      bevelEnabled: FeatureValues.resolve(
        node.bevelEnabled,
        defaultvalue.bevelEnabled,
        true,
        `${hint}.bevelEnabled`,
      ),
      bevel: FeatureValues.resolve(node.bevel, defaultvalue.bevel, true, `${hint}.bevel`),
      bevelWidth: FeatureValues.resolve(
        node.bevelWidth,
        defaultvalue.bevelWidth,
        true,
        `${hint}.bevelWidth`,
      ),
      bevelOffset: FeatureValues.resolve(
        node.bevelOffset,
        defaultvalue.bevelOffset,
        true,
        `${hint}.bevelOffset`,
      ),
      bevelRoundness: FeatureValues.resolve(
        node.bevelRoundness,
        defaultvalue.bevelRoundness,
        true,
        `${hint}.bevelRoundness`,
      ),
      bevelClamp: FeatureValues.resolve(
        node.bevelClamp,
        defaultvalue.bevelClamp,
        true,
        `${hint}.bevelClamp`,
      ),
      lightAngle: FeatureValues.resolve(
        node.lightAngle,
        defaultvalue.lightAngle,
        true,
        `${hint}.lightAngle`,
      ),
      specularColor: FeatureValues.resolve(
        node.specularColor,
        defaultvalue.specularColor,
        true,
        `${hint}.specularColor`,
      ),
      specularPower: FeatureValues.resolve(
        node.specularPower,
        defaultvalue.specularPower,
        true,
        `${hint}.specularPower`,
      ),
      reflectivity: FeatureValues.resolve(
        node.reflectivity,
        defaultvalue.reflectivity,
        true,
        `${hint}.reflectivity`,
      ),
      diffuse: FeatureValues.resolve(node.diffuse, defaultvalue.diffuse, true, `${hint}.diffuse`),
      ambient: FeatureValues.resolve(node.ambient, defaultvalue.ambient, true, `${hint}.ambient`),
      text: FeatureValues.resolve(node.text, defaultvalue.text, true, `${hint}.text`),
      fontSize: FeatureValues.resolve(
        node.fontSize,
        defaultvalue.fontSize,
        true,
        `${hint}.fontSize`,
      ),
      lineSpacing: FeatureValues.resolve(
        node.lineSpacing,
        defaultvalue.lineSpacing,
        true,
        `${hint}.lineSpacing`,
      ),
      fontStyle: FeatureValues.resolve(
        node.fontStyle,
        defaultvalue.fontStyle,
        true,
        `${hint}.fontStyle`,
      ),
      size: FeatureValues.resolve(node.size, defaultvalue.size, true, `${hint}.size`),
    }
  }

  /** @anchor canvas-render-text-effect */
  public render(
    node: TextEffectFields,
    defaultvalue: TextEffectFields,
    runtime: CanvasRuntimeNode,
  ): void {
    const hint: string = 'TextEffect'
    const effective: TextEffectFields = TextEffectRenderer.resolve(node, defaultvalue)

    const elem: HTMLElement = ValueResolver.requirePresentValue(runtime.elem, `${hint}.elem`)

    const color: CanvasColor = ValueResolver.requirePresentValue(effective.color, `${hint}.color`)
    const boolBestFit: boolean = ValueResolver.requirePresentValue(
      effective.boolBestFit,
      `${hint}.boolBestFit`,
    )
    const outlineColor: CanvasColor = ValueResolver.requirePresentValue(
      effective.outlineColor,
      `${hint}.outlineColor`,
    )
    const outlineDistance: Vector3 = ValueResolver.requirePresentValue(
      effective.outlineDistance,
      `${hint}.outlineDistance`,
    )
    const outlineWidth: number = ValueResolver.requirePresentValue(
      effective.outlineWidth,
      `${hint}.outlineWidth`,
    )
    const faceDilate: number = ValueResolver.requirePresentValue(
      effective.faceDilate,
      `${hint}.faceDilate`,
    )
    const vertexGradientEnabled: boolean = ValueResolver.requirePresentValue(
      effective.vertexGradientEnabled,
      `${hint}.vertexGradientEnabled`,
    )
    const glowEnabled: boolean = ValueResolver.requirePresentValue(
      effective.glowEnabled,
      `${hint}.glowEnabled`,
    )
    const underlayEnabled: boolean = ValueResolver.requirePresentValue(
      effective.underlayEnabled,
      `${hint}.underlayEnabled`,
    )
    const bevelEnabled: boolean = ValueResolver.requirePresentValue(
      effective.bevelEnabled,
      `${hint}.bevelEnabled`,
    )

    const displayText: string = ValueResolver.requirePresentValue(effective.text, `${hint}.text`)
    const resolvedLayoutSize: Vector3 = ValueResolver.requirePresentValue(
      runtime.resolvedLayoutSize,
      `${hint}.resolvedLayoutSize`,
    )
    const lineSpacing: number = ValueResolver.requirePresentValue(
      effective.lineSpacing,
      `${hint}.lineSpacing`,
    )
    const fontStyle: number = ValueResolver.requirePresentValue(
      effective.fontStyle,
      `${hint}.fontStyle`,
    )
    const fontSize: number = boolBestFit
      ? RichTextParser.calculateBestFitFontSizeForTextPro({
          text: displayText,
          size: resolvedLayoutSize,
          minFontSize: RichTextParser.BEST_FIT_FALLBACK_MIN_SIZE,
          maxFontSize: RichTextParser.TEXTPRO_DEFAULT_MAX_SIZE,
          lineSpacing,
          fontStyle,
        })
      : ValueResolver.requirePresentValue(effective.fontSize, `${hint}.fontSize`)

    elem.style.setProperty('--canvas-font-size', `${String(fontSize)}px`)
    elem.style.fontSize = `${String(fontSize)}px`
    elem.style.textShadow = ''
    elem.style.webkitTextStroke = ''
    elem.style.paintOrder = ''
    elem.style.backgroundImage = ''
    elem.style.setProperty('-webkit-background-clip', '')
    elem.style.webkitTextFillColor = ''
    elem.classList.remove('text-pro', 'text-underlay', 'text-bevel')

    // 基础颜色
    elem.style.color = CanvasColorValue.toCss(color)

    // 字体样式（fontStyle）
    if (fontStyle !== 0) {
      if ((fontStyle & 1) !== 0) {
        elem.style.fontWeight = 'bold'
      }
      if ((fontStyle & 2) !== 0) {
        elem.style.fontStyle = 'italic'
      }
      const decorations: string[] = []
      if ((fontStyle & 4) !== 0) {
        decorations.push('underline')
      }
      if ((fontStyle & 64) !== 0) {
        decorations.push('line-through')
      }
      if (decorations.length > 0) {
        elem.style.textDecoration = decorations.join(' ')
      }
    }

    // SDF 单位转换常量
    // TMP 标准：pixel = outlineWidth * fontSize * (padding / samplingPointSize)
    // 推荐比例 padding/samplingPointSize = 0.1（即 10%）
    const TMP_SDF_RATIO: number = 0.1

    // SDF 像素单位
    const sdfUnit: number = fontSize * TMP_SDF_RATIO

    // 加粗（faceDilate）：使用 -webkit-text-stroke 配合填充色
    const dilatePx: number = Math.max(0, faceDilate) * sdfUnit

    // 描边（outlineWidth/outlineDistance）：独立于加粗
    const outlineDistanceX: number = outlineDistance.x
    const outlinePx: number =
      outlineDistanceX !== 0 ? Math.abs(outlineDistanceX) * sdfUnit : outlineWidth * sdfUnit

    // 文字效果标记
    elem.classList.add('text-pro')

    // 渲染策略：
    // 1. 加粗：用 -webkit-text-stroke 配合填充色实现文字变粗
    // 2. 描边：在加粗后的边界外侧画描边
    //    webkitTextStroke = dilatePx + outlinePx（描边画在最外层）
    if (outlinePx > 0) {
      const totalStrokePx: number = dilatePx + outlinePx
      elem.style.webkitTextStroke = `${String(totalStrokePx)}px ${CanvasColorValue.toCss(outlineColor)}`
      elem.style.paintOrder = 'stroke fill'
    } else if (dilatePx > 0) {
      // 只有加粗：描边色用填充色
      elem.style.webkitTextStroke = `${String(dilatePx)}px ${CanvasColorValue.toCss(color)}`
      elem.style.paintOrder = 'stroke fill'
    }

    // 收集 textShadow 效果（用于 glow 和 underlay）
    const textShadows: string[] = []

    // 顶点渐变（双线性插值）
    if (vertexGradientEnabled) {
      const tl: CanvasColor = ValueResolver.requirePresentValue(
        effective.gradientTopLeft,
        `${hint}.gradientTopLeft`,
      )
      const tr: CanvasColor = ValueResolver.requirePresentValue(
        effective.gradientTopRight,
        `${hint}.gradientTopRight`,
      )
      const bl: CanvasColor = ValueResolver.requirePresentValue(
        effective.gradientBottomLeft,
        `${hint}.gradientBottomLeft`,
      )
      const br: CanvasColor = ValueResolver.requirePresentValue(
        effective.gradientBottomRight,
        `${hint}.gradientBottomRight`,
      )

      const elemWidth: number = resolvedLayoutSize.x
      const elemHeight: number = resolvedLayoutSize.y
      const gradientUrl: string = FourCornerGradient.generateDataUrl(
        elemWidth,
        elemHeight,
        tl,
        tr,
        bl,
        br,
      )

      elem.style.backgroundImage = `url(${gradientUrl})`
      elem.style.setProperty('-webkit-background-clip', 'text')
      elem.style.webkitTextFillColor = 'transparent'
    }

    // Glow效果
    if (glowEnabled) {
      const glowColor: string = CanvasColorValue.toCss(
        ValueResolver.requirePresentValue(effective.glowColor, `${hint}.glowColor`),
      )
      const glowOffset: number = ValueResolver.requirePresentValue(
        effective.glowOffset,
        `${hint}.glowOffset`,
      )
      const glowOuter: number = ValueResolver.requirePresentValue(
        effective.glowOuter,
        `${hint}.glowOuter`,
      )
      const blurRadius: number = glowOuter * fontSize * 0.1
      const offsetX: number = glowOffset * fontSize * 0.1
      textShadows.push(
        `${String(offsetX)}px ${String(offsetX)}px ${String(blurRadius)}px ${glowColor}`,
      )
    }

    // Underlay效果
    if (underlayEnabled) {
      elem.classList.add('text-underlay')
      const underlayColor: string = CanvasColorValue.toCss(
        ValueResolver.requirePresentValue(effective.underlayColor, `${hint}.underlayColor`),
      )
      const underlayOffset: Vector3 = ValueResolver.requirePresentValue(
        effective.underlayOffset,
        `${hint}.underlayOffset`,
      )
      const underlaySoftness: number = ValueResolver.requirePresentValue(
        effective.underlaySoftness,
        `${hint}.underlaySoftness`,
      )
      elem.style.setProperty('--canvas-underlay-color', underlayColor)
      const offsetX: number = underlayOffset.x * fontSize * 0.1
      const offsetY: number = underlayOffset.y * fontSize * 0.1
      const blur: number = underlaySoftness * fontSize * 0.1
      textShadows.push(
        `${String(offsetX)}px ${String(offsetY)}px ${String(blur)}px ${underlayColor}`,
      )
    }

    // 设置 textShadow
    if (textShadows.length > 0) {
      elem.style.textShadow = textShadows.join(', ')
    }

    // Bevel效果（斜切/浮雕）
    // CSS 无法完全模拟 3D 光照效果，只添加类名，具体效果需要 CSS 配合
    if (bevelEnabled) {
      elem.classList.add('text-bevel')
      const bevel: number = ValueResolver.requirePresentValue(effective.bevel, `${hint}.bevel`)
      const bevelWidth: number = ValueResolver.requirePresentValue(
        effective.bevelWidth,
        `${hint}.bevelWidth`,
      )
      const lightAngle: number = ValueResolver.requirePresentValue(
        effective.lightAngle,
        `${hint}.lightAngle`,
      )
      const specularColor: string = CanvasColorValue.toCss(
        ValueResolver.requirePresentValue(effective.specularColor, `${hint}.specularColor`),
      )
      // 用 CSS 变量传递 Bevel 参数
      elem.style.setProperty('--canvas-bevel', String(bevel))
      elem.style.setProperty('--canvas-bevel-width', String(bevelWidth))
      elem.style.setProperty('--canvas-light-angle', `${String(lightAngle)}rad`)
      elem.style.setProperty('--canvas-specular-color', specularColor)
    }

    // ========== 富文本解析 + 文本内容渲染 ==========
    // 统一使用 RichTextParser 处理（处理 \n 转义和富文本标签）
    const richTextContainer: HTMLElement = RichTextParser.parse(
      displayText,
      fontSize,
      color,
      lineSpacing,
      true, // TextPro
      fontStyle,
    )
    richTextContainer.style.display = 'block'
    richTextContainer.style.width = '100%'
    elem.appendChild(richTextContainer)
  }
}

/** @anchor canvas-render-raw-image */
export class RawImageRenderer extends FeatureRenderBase<RawImageFields> {
  private static readonly INSTANCE: RawImageRenderer = new RawImageRenderer()

  private constructor() {
    super()
  }

  /** @anchor canvas-render-raw-image */
  public static getInstance(): RawImageRenderer {
    return RawImageRenderer.INSTANCE
  }

  /** @anchor canvas-render-raw-image */
  public render(
    node: RawImageFields,
    defaultvalue: RawImageFields,
    runtime: CanvasRuntimeNode,
  ): void {
    const hint: string = 'RawImage'

    const effective: RawImageFields = {
      color: FeatureValues.resolve(node.color, defaultvalue.color, true, `${hint}.color`),
      fileGuid: FeatureValues.resolve(
        node.fileGuid,
        defaultvalue.fileGuid,
        true,
        `${hint}.fileGuid`,
      ),
      softMaskGuid: FeatureValues.resolve(
        node.softMaskGuid,
        defaultvalue.softMaskGuid,
        true,
        `${hint}.softMaskGuid`,
      ),
    }

    const color: CanvasColor = ValueResolver.requirePresentValue(effective.color, `${hint}.color`)
    const fileGuid: string = ValueResolver.requirePresentValue(
      effective.fileGuid,
      `${hint}.fileGuid`,
    )
    const softMaskGuid: string = Option.getOrThrow(effective.softMaskGuid)

    const elem: HTMLElement = ValueResolver.requirePresentValue(runtime.elem, `${hint}.elem`)
    const el: HTMLElement = elem
    const layout: LayoutRect = ValueResolver.requirePresentValue(runtime.layout, `${hint}.layout`)
    el.classList.add('has-image')
    ImageRenderUtils.clearCanvas(el)
    const domPath: string = Option.getOrThrow(Option.fromNullishOr(el.getAttribute('data-path')))

    // 本渲染器是固定的三步流水线，各步职责正交，禁止互相吞并：
    //   第一步「取像素」：拿到图片的原始像素内容。有有效 GUID → 异步加载纹理；无有效 GUID → 纯白占位像素。
    //   第二步「上色」  ：对第一步的像素做正片叠底，套用 color（r/g/b 相乘，a 控制透明度）。
    //   第三步「软遮罩」：按 softMaskGuid 应用软蒙版，与像素来源无关，两条分支统一应用。
    // 硬约束：color 只属于第二步。第一步绝不能提前定色（例如无图时直接用 CSS 背景色），
    // 否则 color 被绕过 → 表现为「颜色参数无效 / 无图强制全白 / 写透明也无效」。
    // 纯白占位 × color = color，故无图 fallback 也会正确反映 color；color.a=0 时 applyBlend 清空为透明。

    // 第一步「取像素」——无有效 GUID：纯白占位像素，随后照常走第二步「上色」。
    if (!GuidUtils.isValid(fileGuid)) {
      const size: { width: number; height: number } =
        ImageRenderUtils.resolveDestinationSize(layout)
      const canvas: HTMLCanvasElement = document.createElement('canvas')
      canvas.classList.add('canvas-render-generated', 'canvas-render-image')
      canvas.width = size.width
      canvas.height = size.height
      canvas.style.cssText = ImageRenderUtils.CANVAS_STYLE
      const ctx: CanvasRenderingContext2D | null = canvas.getContext('2d')
      if (ctx === null) throw new Error('RawImage 获取 2d 上下文失败')
      ctx.fillStyle = '#ffffff'
      ctx.fillRect(0, 0, size.width, size.height)
      ImageRenderUtils.applyBlend(ctx, size.width, size.height, color)
      el.insertBefore(canvas, el.firstChild)
      RawImageRenderer.applySoftMask(runtime, softMaskGuid, domPath)
      return
    }

    // 第一步「取像素」——有有效 GUID：异步加载纹理，完成后走第二步「上色」。
    const imageUrl: string = GuidUtils.loadImage(fileGuid, domPath)
    const task: () => Promise<void> = (): Promise<void> =>
      new Promise<void>((resolve: () => void, reject: (reason: unknown) => void): void => {
        const img: HTMLImageElement = new Image()
        img.onload = (): void => {
          try {
            const texW: number = Math.max(1, img.width)
            const texH: number = Math.max(1, img.height)

            const destination: { width: number; height: number } =
              ImageRenderUtils.resolveDestinationSize(layout)
            const intDW: number = destination.width
            const intDH: number = destination.height

            const canvas: HTMLCanvasElement = document.createElement('canvas')
            canvas.classList.add('canvas-render-generated', 'canvas-render-image')
            canvas.width = intDW
            canvas.height = intDH
            canvas.style.cssText = ImageRenderUtils.CANVAS_STYLE

            const ctx: CanvasRenderingContext2D | null = canvas.getContext('2d')
            if (ctx === null) throw new Error('RawImage 获取 2d 上下文失败')

            // 第一步：纹理像素
            ctx.drawImage(img, 0, 0, texW, texH, 0, 0, intDW, intDH)
            // 第二步：正片叠底 color
            ImageRenderUtils.applyBlend(ctx, intDW, intDH, color)

            el.insertBefore(canvas, el.firstChild)

            // 第三步「软遮罩」——与像素来源无关，两条分支统一应用。
            RawImageRenderer.applySoftMask(runtime, softMaskGuid, domPath)
            resolve()
          } catch (error) {
            reject(new Error('RawImage 合成失败', { cause: error }))
          }
        }
        img.onerror = (e: string | Event): void => {
          // 错误时也要标记完成
          CanvasRenderEntry.reportCanvasPreviewError('raw-image-load', e, 'warning')
          resolve()
        }
        img.src = imageUrl
      })
    runtime.session.track(task)
  }

  /** @anchor canvas-render-raw-image */
  private static applySoftMask(
    runtime: CanvasRuntimeNode,
    softMaskGuid: string,
    domPath: string,
  ): void {
    if (!GuidUtils.isValid(softMaskGuid)) return
    const el: HTMLElement = ValueResolver.requirePresentValue(runtime.elem, 'RawImage.elem')
    const maskUrl: string = GuidUtils.loadImage(softMaskGuid, domPath)
    el.style.maskImage = `url("${maskUrl}")`
    el.style.setProperty('-webkit-mask-image', `url("${maskUrl}")`)
    el.style.maskSize = '100% 100%'
    el.style.setProperty('-webkit-mask-size', '100% 100%')
    el.style.maskPosition = 'center'
    el.style.setProperty('-webkit-mask-position', 'center')
  }
}

/** @anchor canvas-render-spine */
export class SpineRenderer extends FeatureRenderBase<SpineFields> {
  private static readonly INSTANCE: SpineRenderer = new SpineRenderer()

  private constructor() {
    super()
  }

  /** @anchor canvas-render-spine */
  public static getInstance(): SpineRenderer {
    return SpineRenderer.INSTANCE
  }

  /** @anchor canvas-render-spine */
  public render(node: SpineFields, defaultvalue: SpineFields, runtime: CanvasRuntimeNode): void {
    const hint: string = 'Spine'
    const effective: SpineFields = {
      color: FeatureValues.resolve(node.color, defaultvalue.color, true, `${hint}.color`),
      atlasGuid: FeatureValues.resolve(
        node.atlasGuid,
        defaultvalue.atlasGuid,
        true,
        `${hint}.atlasGuid`,
      ),
      skelGuid: FeatureValues.resolve(
        node.skelGuid,
        defaultvalue.skelGuid,
        true,
        `${hint}.skelGuid`,
      ),
      jsonGuid: FeatureValues.resolve(
        node.jsonGuid,
        defaultvalue.jsonGuid,
        true,
        `${hint}.jsonGuid`,
      ),
      pngGuids: FeatureValues.resolve(
        node.pngGuids,
        defaultvalue.pngGuids,
        true,
        `${hint}.pngGuids`,
      ),
      animIndex: FeatureValues.resolve(
        node.animIndex,
        defaultvalue.animIndex,
        true,
        `${hint}.animIndex`,
      ),
    }
    const color: CanvasColor = ValueResolver.requirePresentValue(effective.color, `${hint}.color`)
    const atlasGuid: string = ValueResolver.requirePresentValue(
      effective.atlasGuid,
      `${hint}.atlasGuid`,
    )
    const skelGuid: string = ValueResolver.requirePresentValue(
      effective.skelGuid,
      `${hint}.skelGuid`,
    )
    const jsonGuid: string = ValueResolver.requirePresentValue(
      effective.jsonGuid,
      `${hint}.jsonGuid`,
    )
    const animIndex: number = ValueResolver.requirePresentValue(
      effective.animIndex,
      `${hint}.animIndex`,
    )
    const pngGuids: readonly string[] = ValueResolver.requirePresentValue(
      effective.pngGuids,
      `${hint}.pngGuids`,
    )

    const elem: HTMLElement = ValueResolver.requirePresentValue(runtime.elem, `${hint}.elem`)

    const el: HTMLElement = elem
    const layout: LayoutRect = ValueResolver.requirePresentValue(runtime.layout, `${hint}.layout`)
    const domPath: string = Option.getOrThrow(Option.fromNullishOr(el.getAttribute('data-path')))
    for (const child of Array.from(el.children)) {
      if (child instanceof HTMLElement && child.classList.contains('canvas-render-spine')) {
        child.remove()
      }
    }
    ImageRenderUtils.clearCanvas(el)

    const task: () => Promise<void> = async (): Promise<void> => {
      try {
        const atlasText: string = await (
          await fetch(GuidUtils.loadSpineResource(atlasGuid, domPath))
        ).text()
        const atlas: TextureAtlas = await new Promise<TextureAtlas>(
          (resolve: (value: TextureAtlas) => void, reject: (reason: unknown) => void): void => {
            let pageIndex: number = 0
            new TextureAtlas(
              atlasText,
              (_pageName: string, done: (baseTexture: BaseTexture) => void): void => {
                const pageGuid: string = Option.getOrThrow(
                  Option.fromNullishOr(pngGuids[pageIndex]),
                )
                pageIndex += 1
                void runtime.session
                  .loadTexture(GuidUtils.loadSpineResource(pageGuid, domPath))
                  .then(
                    (texture: Texture): void => {
                      done(texture.baseTexture)
                    },
                    (reason: unknown): void => {
                      reject(new Error('Spine 纹理加载失败', { cause: reason }))
                    },
                  )
              },
              (loadedAtlas: TextureAtlas): void => {
                resolve(loadedAtlas)
              },
            )
          },
        )

        const attachmentLoader: AtlasAttachmentLoader = new AtlasAttachmentLoader(atlas)
        let skeletonData: SkeletonData
        if (GuidUtils.isValid(jsonGuid)) {
          const jsonText: string = await (
            await fetch(GuidUtils.loadSpineResource(jsonGuid, domPath))
          ).text()
          const skeletonJson: SkeletonJson = new SkeletonJson(attachmentLoader)
          skeletonJson.scale = 1
          skeletonData = skeletonJson.readSkeletonData(jsonText)
        } else {
          const skelBytes: Uint8Array = new Uint8Array(
            await (await fetch(GuidUtils.loadSpineResource(skelGuid, domPath))).arrayBuffer(),
          )
          const skeletonBinary: SkeletonBinary = new SkeletonBinary(attachmentLoader)
          skeletonBinary.scale = 1
          skeletonData = skeletonBinary.readSkeletonData(skelBytes)
        }

        const destination: { width: number; height: number } =
          ImageRenderUtils.resolveDestinationSize(layout)
        const intDW: number = destination.width
        const intDH: number = destination.height
        const wrapper: HTMLDivElement = document.createElement('div')
        wrapper.classList.add('canvas-render-generated', 'canvas-render-spine')
        wrapper.style.position = 'absolute'
        wrapper.style.inset = '0'
        wrapper.style.pointerEvents = 'none'
        wrapper.style.overflow = 'hidden'

        const renderer: IRenderer = autoDetectRenderer({
          width: intDW,
          height: intDH,
          antialias: true,
          backgroundAlpha: 0,
          resolution: 1,
          autoDensity: true,
        })
        const stage: Container = new Container()
        try {
          if (!(renderer.view instanceof HTMLCanvasElement)) {
            throw new Error('Spine 渲染视图必须为 HTMLCanvasElement')
          }
          const view: HTMLCanvasElement = renderer.view
          view.style.width = '100%'
          view.style.height = '100%'
          view.style.display = 'block'
          const spine: Spine = new Spine(skeletonData)
          stage.addChild(spine)
          spine.autoUpdate = false
          const animations: readonly SkeletonData['animations'][number][] = skeletonData.animations
          if (animations.length > 0) {
            const clampedAnimIndex: number = Math.max(0, Math.min(animations.length - 1, animIndex))
            spine.state.setAnimation(
              0,
              ReadonlyArray.getUnsafe(animations, clampedAnimIndex).name,
              true,
            )
          }
          spine.update(0)

          const bounds: ReturnType<Spine['getLocalBounds']> = spine.getLocalBounds()
          if (bounds.height <= 0) throw new Error('Spine 姿态高度必须为正数')
          const skeletonHeight: number = bounds.height
          const scale: number = intDH / skeletonHeight
          spine.scale.set(scale, scale)
          spine.x = intDW * 0.5 - (bounds.x + bounds.width * 0.5) * scale
          spine.y = intDH * 0.5 - (bounds.y + bounds.height * 0.5) * scale
          spine.tint =
            ((Math.round(color.r) & 0xff) << 16) |
            ((Math.round(color.g) & 0xff) << 8) |
            (Math.round(color.b) & 0xff)
          spine.alpha = Math.max(0, Math.min(1, color.a / 255))

          renderer.render(stage)
          const snapshot: HTMLCanvasElement = document.createElement('canvas')
          snapshot.width = intDW
          snapshot.height = intDH
          snapshot.style.cssText = ImageRenderUtils.CANVAS_STYLE
          const context: CanvasRenderingContext2D | null = snapshot.getContext('2d')
          if (context === null) throw new Error('Spine 快照获取 2d 上下文失败')
          context.drawImage(view, 0, 0)
          wrapper.appendChild(snapshot)
          el.insertBefore(wrapper, el.firstChild)
        } finally {
          stage.destroy({ children: true, texture: false, baseTexture: false })
          renderer.destroy(true)
        }
      } catch (e) {
        CanvasRenderEntry.reportCanvasPreviewError('spine-load', e, 'error')
        throw e
      }
    }
    runtime.session.track(task)
  }
}
