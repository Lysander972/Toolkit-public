import {
  LayoutType,
  ExtentType,
  type UIComponentType,
  type RenderNodeContext,
  type LayoutParams,
  type LayoutRect,
  type Vector3,
} from './CanvasTypesBase.ts'
import {
  UIRectNodeClass,
  UIButtonNodeClass,
  UILabelNodeClass,
  TextProNodeClass,
  UIInputNodeClass,
  UIRawImageNodeClass,
  UISpineNodeClass,
  ScrollRectNodeClass,
  type CanvasNodeInstance,
} from './CanvasTypesNodes.ts'
import { RuntimeNodeParser, ValueResolver } from './CanvasRuntimeUtils.ts'
import { CanvasDomSchema } from './CanvasDomSchema.ts'
import { RenderDomCommon } from './CanvasRenderBase.ts'
import { Option } from 'effect'
import { Array as ReadonlyArray } from 'effect'
import type { RenderSession } from './CanvasRenderState.ts'

/** @anchor canvas-render-dom-schema */
interface CanvasRenderTree {
  readonly elem: HTMLElement
  readonly node: CanvasNodeInstance
  readonly children: readonly CanvasRenderTree[]
}

/** @anchor canvas-render-dom-pipeline */
class NodeRenderFlow {
  /** @anchor canvas-render-layer-responsibilities */
  public static applyNodeIdentity(input: RenderNodeContext): void {
    const { elem, node, domPath }: RenderNodeContext = input
    elem.classList.add('canvas-element', node.type)
    elem.setAttribute('data-name', node.name)
    elem.setAttribute('data-type', node.type)
    elem.setAttribute('data-path', domPath)
  }

  /** @anchor canvas-render-dom-pipeline */
  public static applyNodeLayout(input: RenderNodeContext): void {
    const { elem }: RenderNodeContext = input
    const layout: LayoutRect = ValueResolver.requirePresentValue(
      input.layout,
      'NodeRenderFlow.applyNodeLayout.layout',
    )
    elem.style.position = 'absolute'
    elem.style.width = `${String(layout.width)}px`
    elem.style.height = `${String(layout.height)}px`
    elem.style.left = `${String(layout.left)}px`
    elem.style.top = `${String(layout.top)}px`
    elem.style.transformOrigin = `${String(layout.transformOriginX)}% ${String(layout.transformOriginY)}%`
    elem.style.transform = `rotate(${String(-layout.rot)}deg)`
  }
}

/**
 * Canvas 渲染管线主入口
 *
 * 职责：协调整个渲染流程
 *
 * 核心方法：
 * - {@link renderCanvasFromDom} - 从 DOM 渲染整个 Canvas（主入口）
 * - {@link prepareTree}、{@link arrangeTree} - 计算最终布局
 * - {@link renderTree} - 投影单个 DOM 元素及其子元素
 * @anchor canvas-render-dom-pipeline
 * @anchor canvas-render-source-layout
 * @anchor canvas-render-module-flow
 */
export class CanvasRenderPipeline {
  /** @anchor canvas-render-node-interfaces */
  private static createNodeByType(
    type: UIComponentType,
    elem: Element,
    session: RenderSession,
  ): CanvasNodeInstance {
    switch (type) {
      case 'UIRect':
        return new UIRectNodeClass(CanvasDomSchema.extract(elem, UIRectNodeClass.SCHEMA), session)
      case 'UIButton':
        return new UIButtonNodeClass(
          CanvasDomSchema.extract(elem, UIButtonNodeClass.SCHEMA),
          session,
        )
      case 'UILabel':
        return new UILabelNodeClass(CanvasDomSchema.extract(elem, UILabelNodeClass.SCHEMA), session)
      case 'TextPro':
        return new TextProNodeClass(CanvasDomSchema.extract(elem, TextProNodeClass.SCHEMA), session)
      case 'UIInput':
        return new UIInputNodeClass(CanvasDomSchema.extract(elem, UIInputNodeClass.SCHEMA), session)
      case 'UIRawImage':
        return new UIRawImageNodeClass(
          CanvasDomSchema.extract(elem, UIRawImageNodeClass.SCHEMA),
          session,
        )
      case 'UISpine':
        return new UISpineNodeClass(CanvasDomSchema.extract(elem, UISpineNodeClass.SCHEMA), session)
      case 'ScrollRect':
        return new ScrollRectNodeClass(
          CanvasDomSchema.extract(elem, ScrollRectNodeClass.SCHEMA),
          session,
        )
    }
  }

  private static extractTree(elem: Element, session: RenderSession): CanvasRenderTree {
    if (!(elem instanceof HTMLElement)) {
      throw new Error('渲染节点必须为 HTMLElement')
    }
    const node: CanvasNodeInstance = CanvasRenderPipeline.createNodeByType(
      RuntimeNodeParser.resolveComponentType(elem),
      elem,
      session,
    )
    return {
      elem,
      node,
      children: Array.from(elem.children, (child: Element): CanvasRenderTree =>
        CanvasRenderPipeline.extractTree(child, session),
      ),
    }
  }
  /** @anchor canvas-render-dom-pipeline */
  private static applyLegacyLabelVisibility(ctx: RenderNodeContext): void {
    const { elem, node }: RenderNodeContext = ctx
    if (node.type !== 'UILabel') {
      return
    }
    const layout: LayoutRect = ValueResolver.requirePresentValue(
      ctx.layout,
      'CanvasRenderPipeline.applyLegacyLabelVisibility.layout',
    )

    elem.removeAttribute('data-text-hidden-by-font-size')
    elem.removeAttribute('data-text-visible-lines')
    elem.style.overflow = 'hidden'

    const richTextContainer: Element | null = elem.querySelector(':scope > .canvas-render-text')
    if (!(richTextContainer instanceof HTMLElement)) {
      throw new Error('UILabel 缺少已生成的文本容器')
    }

    // 直接读 RichTextParser 设置的内联 lineHeight（精确的像素值），不依赖 getComputedStyle
    const rawLineHeight: string = richTextContainer.style.lineHeight
    const lineHeightPx: number = Number.parseFloat(rawLineHeight)
    if (!Number.isFinite(lineHeightPx) || lineHeightPx <= 0) {
      throw new Error(`UILabel 行高无效: ${rawLineHeight}`)
    }

    const visibleLineCount: number = Math.floor(layout.height / lineHeightPx)
    elem.setAttribute('data-text-visible-lines', String(Math.max(0, visibleLineCount)))

    if (visibleLineCount <= 0) {
      richTextContainer.style.maxHeight = '0px'
      richTextContainer.style.overflow = 'hidden'
      elem.setAttribute('data-text-hidden-by-font-size', '1')
      return
    }

    richTextContainer.style.maxHeight = `${String(lineHeightPx * visibleLineCount)}px`
    richTextContainer.style.overflow = 'hidden'
  }

  /** @anchor canvas-render-dom-pipeline */
  private static prepareTree(tree: CanvasRenderTree, parentSize: Vector3): void {
    tree.node.attachElement(tree.elem)
    tree.node.prepareLayout({ x: parentSize.x, y: parentSize.y, z: 0 })
  }

  /** @anchor canvas-render-dom-pipeline */
  private static arrangeTree(tree: CanvasRenderTree): void {
    const parentLayout: LayoutRect = ValueResolver.requirePresentValue(
      tree.node.getRuntime().layout,
      'CanvasRenderPipeline.arrangeTree.layout',
    )
    for (const child of tree.children)
      CanvasRenderPipeline.prepareTree(child, {
        x: parentLayout.width,
        y: parentLayout.height,
        z: 0,
      })
    const layoutParams: Option.Option<LayoutParams> = tree.node.getRuntime().layoutParams
    if (Option.isSome(layoutParams) && tree.children.length > 0) {
      const params: LayoutParams = layoutParams.value
      for (const child of tree.children) {
        if (
          Option.exists(
            child.node.input.widthExtend,
            (value: ExtentType): boolean => value === ExtentType.TO_EDGE,
          ) ||
          Option.exists(
            child.node.input.heightExtend,
            (value: ExtentType): boolean => value === ExtentType.TO_EDGE,
          )
        ) {
          throw new Error('自动排版子节点不允许使用 ToEdge 尺寸')
        }
      }
      const padding: Vector3 = params.layoutPadding
      const availableWidth: number = parentLayout.width - padding.x * 2
      const availableHeight: number = parentLayout.height - padding.y * 2
      const alignment: number = params.childAlignment
      const alignX: number = (alignment % 3) * 0.5
      const alignY: number = Math.floor(alignment / 3) * 0.5
      if (params.layoutType === LayoutType.GRID) {
        const cellWidth: number = params.cellSize.x
        const cellHeight: number = params.cellSize.y
        const columns: number = Math.max(
          1,
          Math.floor(
            (availableWidth + params.layoutSpacing.x) / (cellWidth + params.layoutSpacing.x),
          ),
        )
        const rows: number = Math.ceil(tree.children.length / columns)
        const contentWidth: number =
          columns * cellWidth + Math.max(0, columns - 1) * params.layoutSpacing.x
        const contentHeight: number =
          rows * cellHeight + Math.max(0, rows - 1) * params.layoutSpacing.y
        const startX: number = padding.x + (availableWidth - contentWidth) * alignX
        const startY: number = padding.y + (availableHeight - contentHeight) * alignY
        const lower: boolean = alignment >= 6
        tree.children.forEach((child: CanvasRenderTree, index: number): void => {
          const childLayout: LayoutRect = ValueResolver.requirePresentValue(
            child.node.getRuntime().layout,
            'CanvasRenderPipeline.arrangeTree.childLayout',
          )
          const row: number = Math.floor(index / columns)
          const column: number = index % columns
          const effectiveRow: number = lower ? rows - row - 1 : row
          child.node.applyLayout({
            ...childLayout,
            width: cellWidth,
            height: cellHeight,
            left: startX + column * (cellWidth + params.layoutSpacing.x),
            top: startY + effectiveRow * (cellHeight + params.layoutSpacing.y),
          })
        })
      } else if (params.layoutType === LayoutType.VERTICAL) {
        const heights: number[] = tree.children.map(
          (child: CanvasRenderTree): number =>
            ValueResolver.requirePresentValue(
              child.node.getRuntime().layout,
              'CanvasRenderPipeline.arrangeTree.childLayout',
            ).height,
        )
        const contentHeight: number =
          heights.reduce((sum: number, height: number): number => sum + height, 0) +
          Math.max(0, heights.length - 1) * params.verticalSpacing
        let currentTop: number = padding.y + (availableHeight - contentHeight) * alignY
        tree.children.forEach((child: CanvasRenderTree, index: number): void => {
          const childLayout: LayoutRect = ValueResolver.requirePresentValue(
            child.node.getRuntime().layout,
            'CanvasRenderPipeline.arrangeTree.childLayout',
          )
          child.node.applyLayout({
            ...childLayout,
            left: padding.x + (availableWidth - childLayout.width) * alignX,
            top: currentTop,
          })
          currentTop += ReadonlyArray.getUnsafe(heights, index) + params.verticalSpacing
        })
      } else {
        const widths: number[] = tree.children.map(
          (child: CanvasRenderTree): number =>
            ValueResolver.requirePresentValue(
              child.node.getRuntime().layout,
              'CanvasRenderPipeline.arrangeTree.childLayout',
            ).width,
        )
        const contentWidth: number =
          widths.reduce((sum: number, width: number): number => sum + width, 0) +
          Math.max(0, widths.length - 1) * params.horizontalSpacing
        let currentLeft: number = padding.x + (availableWidth - contentWidth) * alignX
        tree.children.forEach((child: CanvasRenderTree, index: number): void => {
          const childLayout: LayoutRect = ValueResolver.requirePresentValue(
            child.node.getRuntime().layout,
            'CanvasRenderPipeline.arrangeTree.childLayout',
          )
          child.node.applyLayout({
            ...childLayout,
            left: currentLeft,
            top: padding.y + (availableHeight - childLayout.height) * alignY,
          })
          currentLeft += ReadonlyArray.getUnsafe(widths, index) + params.horizontalSpacing
        })
      }
    }
    for (const child of tree.children) {
      CanvasRenderPipeline.arrangeTree(child)
    }
  }

  /** @anchor canvas-render-dom-pipeline */
  private static renderTree(tree: CanvasRenderTree, path: string, logicalPath: string): void {
    const { elem, node }: CanvasRenderTree = tree
    const ctx: RenderNodeContext = {
      elem,
      node: node.getRuntime(),
      layout: node.getRuntime().layout,
      domPath: path,
      domLogicalPath: logicalPath,
    }
    NodeRenderFlow.applyNodeIdentity(ctx)
    NodeRenderFlow.applyNodeLayout(ctx)
    node.renderFeatures()
    CanvasRenderPipeline.applyLegacyLabelVisibility(ctx)
    tree.children.forEach((child: CanvasRenderTree, index: number): void => {
      CanvasRenderPipeline.renderTree(
        child,
        `${path}.${String(index)}`,
        RenderDomCommon.appendLogicalPath(logicalPath, child.node.input.name),
      )
    })
  }

  /** @anchor canvas-render-single-entry @anchor canvas-render-view-isolation */
  public static renderCanvasFromDom(session: RenderSession): void {
    const rootElem: HTMLElement | null = document.querySelector<HTMLElement>('BeCanvas')
    if (rootElem === null) {
      throw new Error('HTML 缺少 BeCanvas')
    }

    const { width, height }: { name: string; width: number; height: number } =
      CanvasDomSchema.extract(rootElem, CanvasDomSchema.root)
    const rootChildren: CanvasRenderTree[] = Array.from(
      rootElem.children,
      (elem: Element): CanvasRenderTree => CanvasRenderPipeline.extractTree(elem, session),
    )
    rootElem.classList.add('viewport')
    rootElem.style.width = `${String(width)}px`
    rootElem.style.height = `${String(height)}px`

    for (const child of rootChildren) {
      CanvasRenderPipeline.prepareTree(child, { x: width, y: height, z: 0 })
    }
    for (const child of rootChildren) CanvasRenderPipeline.arrangeTree(child)
    for (const [index, child] of rootChildren.entries()) {
      CanvasRenderPipeline.renderTree(
        child,
        String(index),
        RenderDomCommon.appendLogicalPath('', child.node.input.name),
      )
    }
  }
}
