import {
  TextAnchor,
  ExtentType,
  FillMethodType,
  type Vector3,
  type LayoutRect,
  type LayoutFields,
  CanvasRenderConstants,
} from './CanvasTypesBase.ts'
import type {
  CanvasRuntimeNode,
  CanvasLayoutFailure,
  CanvasRenderDiagnostics,
} from './CanvasTypesBase.ts'
import { CanvasColorValue } from './CanvasRuntimeUtils.ts'
import { FeatureValues } from './CanvasRenderBase.ts'
import { Option } from 'effect'

/** @anchor canvas-render-layout */
interface AxisLayout {
  readonly size: number
  readonly start: number
}

/** @anchor canvas-render-layout-diagnostics */
export class CanvasLayoutDiagnostics {
  private static failure: Option.Option<CanvasLayoutFailure> = Option.none()

  /** @anchor canvas-render-layout-diagnostics */
  public static recordInvalidSize(
    node: CanvasRuntimeNode,
    parentWidth: number,
    parentHeight: number,
    input: CanvasLayoutFailure['input'],
    result: CanvasLayoutFailure['result'],
  ): void {
    CanvasLayoutDiagnostics.failure = Option.some({
      node,
      parentWidth,
      parentHeight,
      input,
      result,
    })
  }

  /** @anchor canvas-render-layout-diagnostics */
  public static get(): Option.Option<CanvasRenderDiagnostics> {
    if (Option.isNone(CanvasLayoutDiagnostics.failure)) return Option.none()
    const failure: CanvasLayoutFailure = CanvasLayoutDiagnostics.failure.value
    const elem: HTMLElement = Option.getOrThrow(failure.node.elem)
    const attributes: Record<string, string> = {}
    for (const attribute of Array.from(elem.attributes))
      attributes[attribute.name] = attribute.value

    const ancestors: CanvasRenderDiagnostics['ancestors'] = []
    for (
      let current: HTMLElement | null = elem.parentElement;
      current !== null;
      current = current.parentElement
    ) {
      ancestors.push({
        tagName: current.tagName,
        name: current.getAttribute('name'),
        path: current.getAttribute('data-path'),
        style: CanvasLayoutDiagnostics.readStyle(current),
        outerHtml: current.outerHTML,
      })
    }

    return Option.some({
      kind: 'layout-invalid-size',
      node: {
        type: failure.node.type,
        name: failure.node.name,
        path: elem.getAttribute('data-path'),
        attributes,
        style: CanvasLayoutDiagnostics.readStyle(elem),
      },
      ancestors,
      input: {
        ...failure.input,
        parentWidth: failure.parentWidth,
        parentHeight: failure.parentHeight,
      },
      result: failure.result,
      html: document.documentElement.outerHTML,
    })
  }

  /** @anchor canvas-render-layout-diagnostics */
  private static readStyle(elem: HTMLElement): {
    width: string
    height: string
    left: string
    top: string
  } {
    return {
      width: elem.style.width,
      height: elem.style.height,
      left: elem.style.left,
      top: elem.style.top,
    }
  }
}

/** @anchor canvas-render-layout */
export class LayoutRenderer {
  private static readonly INSTANCE: LayoutRenderer = new LayoutRenderer()

  /** @anchor canvas-render-layout */
  public static getInstance(): LayoutRenderer {
    return LayoutRenderer.INSTANCE
  }

  private constructor() {}

  /** @anchor canvas-render-layout */
  public render(
    node: LayoutFields,
    defaultvalue: LayoutFields,
    runtime: CanvasRuntimeNode,
    parentSize: Vector3,
  ): LayoutRect {
    const hint: string = 'Layout'

    // pos 和 legacyPos 互斥：有 pos 用 pos，否则用 legacyPos
    const useLegacyPos: boolean = Option.isNone(node.pos) && Option.isSome(node.legacyPos)
    const effective: LayoutFields = {
      name: node.name,
      guid: node.guid,
      pos: FeatureValues.resolve(node.pos, defaultvalue.pos, !useLegacyPos, `${hint}.pos`),
      legacyPos: FeatureValues.resolve(
        node.legacyPos,
        defaultvalue.legacyPos,
        useLegacyPos,
        `${hint}.legacyPos`,
      ),
      size: FeatureValues.resolve(node.size, defaultvalue.size, true, `${hint}.size`),
      parentAnchor: FeatureValues.resolve(
        node.parentAnchor,
        defaultvalue.parentAnchor,
        true,
        `${hint}.parentAnchor`,
      ),
      anchor: FeatureValues.resolve(node.anchor, defaultvalue.anchor, true, `${hint}.anchor`),
      widthExtend: FeatureValues.resolve(
        node.widthExtend,
        defaultvalue.widthExtend,
        true,
        `${hint}.widthExtend`,
      ),
      heightExtend: FeatureValues.resolve(
        node.heightExtend,
        defaultvalue.heightExtend,
        true,
        `${hint}.heightExtend`,
      ),
      rot: FeatureValues.resolve(node.rot, defaultvalue.rot, true, `${hint}.rot`),
    }

    const parentWidth: number = parentSize.x
    const parentHeight: number = parentSize.y
    if (
      !Number.isFinite(parentWidth) ||
      !Number.isFinite(parentHeight) ||
      parentWidth <= 0 ||
      parentHeight <= 0
    ) {
      throw new Error(`${hint} style.width/height 无效`)
    }

    // 使用锚点/轴心点逻辑计算布局
    return LayoutRenderer.calculateLayoutCore(effective, runtime, parentWidth, parentHeight)
  }

  /** @anchor canvas-render-layout */
  private static resolvePosition(effective: LayoutFields): Vector3 {
    const hint: string = 'Layout.position'

    if (Option.isSome(effective.pos)) {
      return effective.pos.value
    }

    if (Option.isSome(effective.legacyPos)) {
      // parentAnchor 已由 Node DEFAULT 补齐。
      const parentAnchor: TextAnchor = Option.getOrThrow(effective.parentAnchor)
      return LayoutRenderer.resolveLegacyPosByParentAnchor(effective.legacyPos.value, parentAnchor)
    }

    throw new Error(`${hint} pos 和 legacyPos 不能同时为空`)
  }

  /** @anchor canvas-render-layout */
  private static resolveLegacyPosByParentAnchor(
    legacyPos: Vector3,
    parentAnchor: TextAnchor,
  ): Vector3 {
    let resolvedX: number = legacyPos.x
    let resolvedY: number = legacyPos.y
    const pivot: { x: number; y: number } = CanvasRenderConstants.ANCHOR_PIVOT[parentAnchor]

    if (pivot.x === 1) {
      resolvedX = -resolvedX
    }

    if (pivot.y === 1) {
      resolvedY = -resolvedY
    }

    return { x: resolvedX, y: resolvedY, z: legacyPos.z }
  }

  /** @anchor canvas-render-layout */
  private static calculateLayoutCore(
    effective: LayoutFields,
    node: CanvasRuntimeNode,
    parentWidth: number,
    parentHeight: number,
  ): LayoutRect {
    const hint: string = `Layout(${node.type}/${node.name})`
    const pos: Vector3 = LayoutRenderer.resolvePosition(effective)
    // 当前布局使用的字段已由 Node DEFAULT 补齐。
    const size: Vector3 = Option.getOrThrow(effective.size)
    const parentAnchor: TextAnchor = Option.getOrThrow(effective.parentAnchor)
    const anchor: TextAnchor = Option.getOrThrow(effective.anchor)
    const widthExtend: ExtentType = Option.getOrThrow(effective.widthExtend)
    const heightExtend: ExtentType = Option.getOrThrow(effective.heightExtend)
    const rot: number = Option.getOrThrow(effective.rot)
    const parentPivot: { x: number; y: number } = CanvasRenderConstants.ANCHOR_PIVOT[parentAnchor]
    const selfPivot: { x: number; y: number } = CanvasRenderConstants.ANCHOR_PIVOT[anchor]
    let horizontal: AxisLayout
    let vertical: AxisLayout
    if (widthExtend === ExtentType.ASPECT) {
      if (heightExtend === ExtentType.ASPECT) {
        throw new Error(`${hint} widthExtend/heightExtend 不允许同时为 Aspect`)
      }
      if (size.x <= 0) throw new Error(`${hint}.size.x 作为 AspectRatio 必须为正数`)
      vertical = LayoutRenderer.calculateAxis(
        heightExtend,
        size.y,
        pos.y,
        parentHeight,
        parentPivot.y,
        selfPivot.y,
      )
      const width: number = vertical.size * size.x
      horizontal = { size: width, start: parentPivot.x * parentWidth + pos.x - selfPivot.x * width }
    } else {
      horizontal = LayoutRenderer.calculateAxis(
        widthExtend,
        size.x,
        pos.x,
        parentWidth,
        parentPivot.x,
        selfPivot.x,
      )
      if (heightExtend === ExtentType.ASPECT) {
        if (size.y <= 0) throw new Error(`${hint}.size.y 作为 AspectRatio 必须为正数`)
        const height: number = horizontal.size / size.y
        vertical = {
          size: height,
          start: parentPivot.y * parentHeight + pos.y - selfPivot.y * height,
        }
      } else {
        vertical = LayoutRenderer.calculateAxis(
          heightExtend,
          size.y,
          pos.y,
          parentHeight,
          parentPivot.y,
          selfPivot.y,
        )
      }
    }
    if (
      !Number.isFinite(horizontal.size) ||
      !Number.isFinite(vertical.size) ||
      horizontal.size <= 0 ||
      vertical.size <= 0
    ) {
      CanvasLayoutDiagnostics.recordInvalidSize(
        node,
        parentWidth,
        parentHeight,
        { pos, size, parentAnchor, anchor, widthExtend, heightExtend },
        {
          width: horizontal.size,
          height: vertical.size,
          left: horizontal.start,
          bottom: vertical.start,
        },
      )
      throw new Error(
        `${hint} 计算出非法尺寸: width=${String(horizontal.size)}, height=${String(vertical.size)}; 诊断=layout-invalid-size`,
      )
    }
    return {
      left: horizontal.start,
      top: parentHeight - vertical.start - vertical.size,
      width: horizontal.size,
      height: vertical.size,
      rot,
      transformOriginX: selfPivot.x * 100,
      transformOriginY: (1 - selfPivot.y) * 100,
    }
  }

  /** @anchor canvas-render-layout */
  private static calculateAxis(
    extent: Exclude<ExtentType, ExtentType.ASPECT>,
    size: number,
    pos: number,
    parentSize: number,
    parentPivot: number,
    selfPivot: number,
  ): AxisLayout {
    if (extent === ExtentType.TO_EDGE) {
      const delta: number =
        selfPivot === 0.5 ? -size * 2 : selfPivot === 1 ? -size + pos : -size - pos
      return {
        size: parentSize + delta,
        start: (parentPivot - selfPivot) * parentSize + pos - selfPivot * delta,
      }
    }
    const dimension: number = extent === ExtentType.ABSOLUTE ? size : size * parentSize
    return { size: dimension, start: parentPivot * parentSize + pos - selfPivot * dimension }
  }

  /** @anchor canvas-render-layout */
  public static getAlignmentStyles(alignment: TextAnchor): {
    justifyContent: string
    alignItems: string
    textAlign: string
  } {
    let justifyContent: string
    let textAlign: string
    const pivot: { x: number; y: number } = CanvasRenderConstants.ANCHOR_PIVOT[alignment]
    if (pivot.x === 0.5) {
      justifyContent = 'center'
      textAlign = 'center'
    } else if (pivot.x === 1) {
      justifyContent = 'flex-end'
      textAlign = 'right'
    } else {
      justifyContent = 'flex-start'
      textAlign = 'left'
    }

    let alignItems: string
    if (pivot.y === 1) {
      alignItems = 'flex-start'
    } else if (pivot.y === 0.5) {
      alignItems = 'center'
    } else {
      alignItems = 'flex-end'
    }

    return { justifyContent, alignItems, textAlign }
  }

  /** @anchor canvas-render-layout */
  public static getFillClipPath(method: FillMethodType, amount: number): string {
    if (amount >= 1) {
      return ''
    }
    if (amount <= 0) {
      return 'polygon(0 0, 0 0, 0 0)'
    }

    const pct: number = amount * 100
    switch (method) {
      case FillMethodType.HORIZONTAL:
        return `inset(0 ${String(100 - pct)}% 0 0)`
      case FillMethodType.VERTICAL:
        return `inset(${String(100 - pct)}% 0 0 0)`
      case FillMethodType.RADIAL_90:
      case FillMethodType.RADIAL_180:
      case FillMethodType.RADIAL_360:
        return CanvasColorValue.generateRadialClipPath(method, amount)
      case FillMethodType.NONE:
        throw new Error(`不支持的 FillMethodType: ${String(method)}`)
    }
  }
}
