import {
  TextAnchor,
  ExtentType,
  FillMethodType,
  UIInputContentType,
  type UIComponentType,
  type Vector3,
  type LayoutRect,
  type LayoutFields,
  type LayoutParams,
  type CanvasRuntimeNode,
} from './CanvasTypesBase.ts'
import type { CanvasUISchema } from './CanvasViewSchema.ts'
import type { RenderSession } from './CanvasRenderState.ts'
import { CanvasDomSchema } from './CanvasDomSchema.ts'
import type { z } from 'zod'
import { LayoutRenderer } from './CanvasRenderLayout.ts'
import {
  BoxRenderer,
  MaskRenderer,
  FillRenderer,
  ScrollRenderer,
  ChildLayoutRenderer,
  ShadowOutlineRenderer,
  ButtonRenderer,
  TextStyleRenderer,
  InputRenderer,
  LabelRenderer,
  TextEffectRenderer,
  RawImageRenderer,
  SpineRenderer,
} from './CanvasRenderNode.ts'
import { Option } from 'effect'

/** @anchor canvas-render-node-interfaces */
export abstract class CanvasNodeModel<Input extends LayoutFields = LayoutFields> {
  public readonly type: UIComponentType
  public readonly input: Input
  private runtime: CanvasRuntimeNode

  /** @anchor canvas-render-dom-schema */
  protected constructor(source: Input, type: UIComponentType, session: RenderSession) {
    this.input = source
    this.type = type
    this.runtime = {
      type,
      name: source.name,
      session,
      elem: Option.none(),
      layout: Option.none(),
      resolvedLayoutSize: Option.none(),
      layoutParams: Option.none(),
    }
  }

  /** @anchor canvas-render-runtime-fields */
  public getRuntime(): CanvasRuntimeNode {
    return this.runtime
  }

  /** @anchor canvas-render-runtime-fields */
  public attachElement(elem: HTMLElement): void {
    this.runtime = { ...this.runtime, elem: Option.some(elem) }
  }

  /** @anchor canvas-render-runtime-fields */
  public applyLayout(layout: LayoutRect): void {
    this.runtime = {
      ...this.runtime,
      layout: Option.some(layout),
      resolvedLayoutSize: Option.some({ x: layout.width, y: layout.height, z: 0 }),
    }
  }

  /** @anchor canvas-render-runtime-fields */
  protected applyChildLayout(params: Option.Option<LayoutParams>): void {
    this.runtime = { ...this.runtime, layoutParams: params }
  }

  /** @anchor canvas-render-node-render-order */
  public abstract prepareLayout(parentSize: Vector3): void
  /** @anchor canvas-render-node-render-order */
  public abstract renderFeatures(): void

  /** @anchor canvas-render-node-render-order @anchor canvas-render-feature-projection */
  public render(parentSize: Vector3): void {
    this.prepareLayout(parentSize)
    this.renderFeatures()
  }
}

/** @anchor canvas-render-node-interfaces */
export class UIRectNodeClass extends CanvasNodeModel<CanvasUISchema['UIRect']> {
  private static readonly DEFAULT: CanvasUISchema['UIRect'] = {
    // LayoutFields
    name: '',
    guid: Option.none(),
    pos: Option.some({ x: 0, y: 0, z: 0 }),
    legacyPos: Option.some({ x: 0, y: 0, z: 0 }),
    size: Option.some({ x: 100, y: 100, z: 0 }),
    parentAnchor: Option.some(TextAnchor.LOWER_LEFT),
    anchor: Option.some(TextAnchor.LOWER_LEFT),
    widthExtend: Option.some(ExtentType.ABSOLUTE),
    heightExtend: Option.some(ExtentType.ABSOLUTE),
    rot: Option.some(0),
    // BoxFields
    color: Option.some({ r: 255, g: 255, b: 255, a: 255, palette: Option.none() }),
    borderWidth: Option.some(0),
    radius: Option.some(0),
    slicedPicGuid: Option.some(''),
    slicedBorderLB: Option.some({ x: 0, y: 0, z: 0 }),
    slicedBorderRT: Option.some({ x: 0, y: 0, z: 0 }),
    slicedRectLB: Option.some({ x: 0, y: 0, z: 0 }),
    slicedRectRT: Option.some({ x: 0, y: 0, z: 0 }),
    // FillFields
    fillMethod: Option.some(FillMethodType.NONE),
    fillAmount: Option.some(1),
    // MaskFields
    mask: Option.some(false),
    // ChildLayoutFields
    cellSize: Option.some({ x: 100, y: 100, z: 0 }),
    layoutSpacing: Option.some({ x: 0, y: 0, z: 0 }),
    layoutPadding: Option.some({ x: 0, y: 0, z: 0 }),
    childAlignment: Option.some(TextAnchor.UPPER_CENTER),
    verticalSpacing: Option.some(0),
    horizontalSpacing: Option.some(0),
    childExpandWidth: Option.some(false),
    childExpandHeight: Option.some(false),
    // ShadowOutlineFields
    shadowColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    shadewDistance: Option.some({ x: 0, y: 0, z: 0 }),
    outlineColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    outlineDistance: Option.some({ x: 0, y: 0, z: 0 }),
  }

  public static readonly SCHEMA: (typeof CanvasDomSchema.nodes)['UIRect'] =
    CanvasDomSchema.nodes.UIRect

  /** @anchor canvas-render-dom-schema */
  public constructor(source: z.output<typeof UIRectNodeClass.SCHEMA>, session: RenderSession) {
    super(source, 'UIRect', session)
  }

  /** @anchor canvas-render-node-render-order */
  public prepareLayout(parentSize: Vector3): void {
    this.applyLayout(
      LayoutRenderer.getInstance().render(
        this.input,
        UIRectNodeClass.DEFAULT,
        this.getRuntime(),
        parentSize,
      ),
    )
    this.applyChildLayout(
      ChildLayoutRenderer.getInstance().render(this.input, UIRectNodeClass.DEFAULT),
    )
  }

  /** @anchor canvas-render-node-render-order */
  public renderFeatures(): void {
    BoxRenderer.getInstance().render(this.input, UIRectNodeClass.DEFAULT, this.getRuntime())
    FillRenderer.getInstance().render(this.input, UIRectNodeClass.DEFAULT, this.getRuntime())
    MaskRenderer.getInstance().render(this.input, UIRectNodeClass.DEFAULT, this.getRuntime())
    ShadowOutlineRenderer.getInstance().render(
      this.input,
      UIRectNodeClass.DEFAULT,
      this.getRuntime(),
    )
  }
}

/** @anchor canvas-render-node-interfaces */
export class UIButtonNodeClass extends CanvasNodeModel<CanvasUISchema['UIButton']> {
  private static readonly DEFAULT: CanvasUISchema['UIButton'] = {
    // LayoutFields
    name: '',
    guid: Option.none(),
    pos: Option.some({ x: 0, y: 0, z: 0 }),
    legacyPos: Option.some({ x: 0, y: 0, z: 0 }),
    size: Option.some({ x: 100, y: 100, z: 0 }),
    parentAnchor: Option.some(TextAnchor.LOWER_LEFT),
    anchor: Option.some(TextAnchor.LOWER_LEFT),
    widthExtend: Option.some(ExtentType.ABSOLUTE),
    heightExtend: Option.some(ExtentType.ABSOLUTE),
    rot: Option.some(0),
    // BoxFields
    color: Option.some({ r: 0, g: 255, b: 0, a: 255, palette: Option.none() }),
    borderWidth: Option.some(0),
    radius: Option.some(0),
    slicedPicGuid: Option.some(''),
    slicedBorderLB: Option.some({ x: 0, y: 0, z: 0 }),
    slicedBorderRT: Option.some({ x: 0, y: 0, z: 0 }),
    slicedRectLB: Option.some({ x: 0, y: 0, z: 0 }),
    slicedRectRT: Option.some({ x: 0, y: 0, z: 0 }),
    // ShadowOutlineFields
    shadowColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    shadewDistance: Option.some({ x: 0, y: 0, z: 0 }),
    outlineColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    outlineDistance: Option.some({ x: 0, y: 0, z: 0 }),
    // ButtonFields
    label: Option.some(''),
    fontSize: Option.none(),
    textAlignment: Option.none(),
    lineSpacing: Option.none(),
    characterSpacing: Option.none(),
    fontStyle: Option.none(),
    lightFont: Option.none(),
    clickCBName: Option.some(''),
    pressCBName: Option.some(''),
    releaseCBName: Option.some(''),
  }

  public static readonly SCHEMA: (typeof CanvasDomSchema.nodes)['UIButton'] =
    CanvasDomSchema.nodes.UIButton

  /** @anchor canvas-render-dom-schema */
  public constructor(source: z.output<typeof UIButtonNodeClass.SCHEMA>, session: RenderSession) {
    super(source, 'UIButton', session)
  }

  /** @anchor canvas-render-node-render-order */
  public prepareLayout(parentSize: Vector3): void {
    this.applyLayout(
      LayoutRenderer.getInstance().render(
        this.input,
        UIButtonNodeClass.DEFAULT,
        this.getRuntime(),
        parentSize,
      ),
    )
  }

  /** @anchor canvas-render-node-render-order */
  public renderFeatures(): void {
    // 方框绘制
    BoxRenderer.getInstance().render(this.input, UIButtonNodeClass.DEFAULT, this.getRuntime())

    // 通用阴影/描边（应用到按钮本体）
    ShadowOutlineRenderer.getInstance().render(
      this.input,
      UIButtonNodeClass.DEFAULT,
      this.getRuntime(),
    )

    // 按钮特有（文字 + 回调）
    ButtonRenderer.getInstance().render(this.input, UIButtonNodeClass.DEFAULT, this.getRuntime())
  }
}

/** @anchor canvas-render-node-interfaces */
export class UILabelNodeClass extends CanvasNodeModel<CanvasUISchema['UILabel']> {
  private static readonly DEFAULT: CanvasUISchema['UILabel'] = {
    // LayoutFields
    name: '',
    guid: Option.none(),
    pos: Option.some({ x: 0, y: 0, z: 0 }),
    legacyPos: Option.some({ x: 0, y: 0, z: 0 }),
    size: Option.some({ x: 100, y: 100, z: 0 }),
    parentAnchor: Option.some(TextAnchor.LOWER_LEFT),
    anchor: Option.some(TextAnchor.LOWER_LEFT),
    widthExtend: Option.some(ExtentType.ABSOLUTE),
    heightExtend: Option.some(ExtentType.ABSOLUTE),
    rot: Option.some(0),
    // TextContentFields
    text: Option.some(''),
    // TextStyleFields
    fontSize: Option.some(45),
    textAlignment: Option.some(TextAnchor.MIDDLE_CENTER),
    lineSpacing: Option.some(1),
    characterSpacing: Option.some(0),
    fontStyle: Option.some(0),
    // LabelFields
    shadowColor: Option.none(),
    shadewDistance: Option.none(),
    color: Option.some({ r: 255, g: 255, b: 255, a: 255, palette: Option.none() }),
    lightFont: Option.some(false),
    boolBestFit: Option.some(true),
    outlineColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    outlineDistance: Option.some({ x: 0, y: 0, z: 0 }),
    outlineWidth: Option.some(0),
  }

  public static readonly SCHEMA: (typeof CanvasDomSchema.nodes)['UILabel'] =
    CanvasDomSchema.nodes.UILabel

  /** @anchor canvas-render-dom-schema */
  public constructor(source: z.output<typeof UILabelNodeClass.SCHEMA>, session: RenderSession) {
    super(source, 'UILabel', session)
  }

  /** @anchor canvas-render-node-render-order */
  public prepareLayout(parentSize: Vector3): void {
    this.applyLayout(
      LayoutRenderer.getInstance().render(
        this.input,
        UILabelNodeClass.DEFAULT,
        this.getRuntime(),
        parentSize,
      ),
    )
  }

  /** @anchor canvas-render-node-render-order */
  public renderFeatures(): void {
    // 基础文字样式
    TextStyleRenderer.getInstance().render(this.input, UILabelNodeClass.DEFAULT, this.getRuntime())

    // 常规文本框专用（包含颜色+描边+富文本解析+文本渲染）
    LabelRenderer.getInstance().render(this.input, UILabelNodeClass.DEFAULT, this.getRuntime())
  }
}

/** @anchor canvas-render-node-interfaces */
export class TextProNodeClass extends CanvasNodeModel<CanvasUISchema['TextPro']> {
  private static readonly DEFAULT: CanvasUISchema['TextPro'] = {
    // LayoutFields
    name: '',
    guid: Option.none(),
    pos: Option.some({ x: 0, y: 0, z: 0 }),
    legacyPos: Option.some({ x: 0, y: 0, z: 0 }),
    size: Option.some({ x: 100, y: 100, z: 0 }),
    parentAnchor: Option.some(TextAnchor.LOWER_LEFT),
    anchor: Option.some(TextAnchor.LOWER_LEFT),
    widthExtend: Option.some(ExtentType.ABSOLUTE),
    heightExtend: Option.some(ExtentType.ABSOLUTE),
    rot: Option.some(0),
    // TextContentFields
    text: Option.some(''),
    // TextStyleFields
    fontSize: Option.some(36),
    textAlignment: Option.some(TextAnchor.MIDDLE_CENTER),
    lineSpacing: Option.some(0),
    characterSpacing: Option.some(0),
    fontStyle: Option.some(0),
    // TextEffectFields
    shadowColor: Option.none(),
    shadewDistance: Option.none(),
    lightFont: Option.none(),
    color: Option.some({ r: 255, g: 255, b: 255, a: 255, palette: Option.none() }),
    boolBestFit: Option.some(true),
    outlineColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    outlineDistance: Option.some({ x: 0, y: 0, z: 0 }),
    outlineWidth: Option.some(0),
    faceDilate: Option.some(0),
    gradientTopLeft: Option.some({ r: 255, g: 255, b: 255, a: 255, palette: Option.none() }),
    gradientTopRight: Option.some({ r: 255, g: 255, b: 255, a: 255, palette: Option.none() }),
    gradientBottomLeft: Option.some({ r: 255, g: 255, b: 255, a: 255, palette: Option.none() }),
    gradientBottomRight: Option.some({ r: 255, g: 255, b: 255, a: 255, palette: Option.none() }),
    vertexGradientEnabled: Option.some(false),
    underlayEnabled: Option.some(false),
    underlayColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    underlayOffset: Option.some({ x: 0, y: 0, z: 0 }),
    underlaySoftness: Option.some(0),
    bevelEnabled: Option.some(false),
    bevel: Option.some(0),
    bevelWidth: Option.some(0),
    bevelOffset: Option.some(0),
    bevelRoundness: Option.some(0),
    bevelClamp: Option.some(0),
    lightAngle: Option.some(0),
    specularColor: Option.some({ r: 255, g: 255, b: 255, a: 255, palette: Option.none() }),
    specularPower: Option.some(0),
    reflectivity: Option.some(0),
    diffuse: Option.some(0),
    ambient: Option.some(0),
    glowEnabled: Option.some(false),
    glowColor: Option.some({ r: 255, g: 255, b: 255, a: 255, palette: Option.none() }),
    glowOffset: Option.some(0),
    glowInner: Option.some(0),
    glowOuter: Option.some(0),
    glowPower: Option.some(0),
  }

  public static readonly SCHEMA: (typeof CanvasDomSchema.nodes)['TextPro'] =
    CanvasDomSchema.nodes.TextPro

  /** @anchor canvas-render-dom-schema */
  public constructor(source: z.output<typeof TextProNodeClass.SCHEMA>, session: RenderSession) {
    super(source, 'TextPro', session)
  }

  /** @anchor canvas-render-node-render-order */
  public prepareLayout(parentSize: Vector3): void {
    this.applyLayout(
      LayoutRenderer.getInstance().render(
        this.input,
        TextProNodeClass.DEFAULT,
        this.getRuntime(),
        parentSize,
      ),
    )
  }

  /** @anchor canvas-render-node-render-order */
  public renderFeatures(): void {
    // 基础文字样式
    TextStyleRenderer.getInstance().render(this.input, TextProNodeClass.DEFAULT, this.getRuntime())

    // 特效文本框专用（包含SDF描边+顶点渐变+Glow+Bevel+Underlay+富文本+文本渲染）
    TextEffectRenderer.getInstance().render(this.input, TextProNodeClass.DEFAULT, this.getRuntime())
  }
}

/** @anchor canvas-render-node-interfaces */
export class UIInputNodeClass extends CanvasNodeModel<CanvasUISchema['UIInput']> {
  private static readonly DEFAULT: CanvasUISchema['UIInput'] = {
    // LayoutFields
    name: '',
    guid: Option.none(),
    pos: Option.some({ x: 0, y: 0, z: 0 }),
    legacyPos: Option.some({ x: 0, y: 0, z: 0 }),
    size: Option.some({ x: 100, y: 100, z: 0 }),
    parentAnchor: Option.some(TextAnchor.LOWER_LEFT),
    anchor: Option.some(TextAnchor.LOWER_LEFT),
    widthExtend: Option.some(ExtentType.ABSOLUTE),
    heightExtend: Option.some(ExtentType.ABSOLUTE),
    rot: Option.some(0),
    // BoxFields
    color: Option.some({ r: 255, g: 255, b: 255, a: 255, palette: Option.none() }),
    // ShadowOutlineFields
    shadowColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    shadewDistance: Option.some({ x: 0, y: 0, z: 0 }),
    outlineColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    outlineDistance: Option.some({ x: 0, y: 0, z: 0 }),
    // TextContentFields
    text: Option.some(''),
    // TextStyleFields
    fontSize: Option.some(32),
    textAlignment: Option.some(TextAnchor.MIDDLE_LEFT),
    // InputFields
    multiLine: Option.some(false),
    contentType: Option.some(UIInputContentType.STANDARD),
    labelColor: Option.some({ r: 0, g: 0, b: 0, a: 255, palette: Option.none() }),
  }

  public static readonly SCHEMA: (typeof CanvasDomSchema.nodes)['UIInput'] =
    CanvasDomSchema.nodes.UIInput

  /** @anchor canvas-render-dom-schema */
  public constructor(source: z.output<typeof UIInputNodeClass.SCHEMA>, session: RenderSession) {
    super(source, 'UIInput', session)
  }

  /** @anchor canvas-render-node-render-order */
  public prepareLayout(parentSize: Vector3): void {
    this.applyLayout(
      LayoutRenderer.getInstance().render(
        this.input,
        UIInputNodeClass.DEFAULT,
        this.getRuntime(),
        parentSize,
      ),
    )
  }

  /** @anchor canvas-render-node-render-order */
  public renderFeatures(): void {
    // 方框绘制
    BoxRenderer.getInstance().renderColor(this.input, UIInputNodeClass.DEFAULT, this.getRuntime())

    // 通用阴影/描边（应用到本体）
    ShadowOutlineRenderer.getInstance().render(
      this.input,
      UIInputNodeClass.DEFAULT,
      this.getRuntime(),
    )

    // 基础文字样式
    TextStyleRenderer.getInstance().render(this.input, UIInputNodeClass.DEFAULT, this.getRuntime())

    // 输入框专用（包含文本渲染）
    InputRenderer.getInstance().render(this.input, UIInputNodeClass.DEFAULT, this.getRuntime())
  }
}

/** @anchor canvas-render-node-interfaces */
export class UIRawImageNodeClass extends CanvasNodeModel<CanvasUISchema['UIRawImage']> {
  private static readonly DEFAULT: CanvasUISchema['UIRawImage'] = {
    // LayoutFields
    name: '',
    guid: Option.none(),
    pos: Option.some({ x: 0, y: 0, z: 0 }),
    legacyPos: Option.some({ x: 0, y: 0, z: 0 }),
    size: Option.some({ x: 100, y: 100, z: 0 }),
    parentAnchor: Option.some(TextAnchor.LOWER_LEFT),
    anchor: Option.some(TextAnchor.LOWER_LEFT),
    widthExtend: Option.some(ExtentType.ABSOLUTE),
    heightExtend: Option.some(ExtentType.ABSOLUTE),
    rot: Option.some(0),
    // ShadowOutlineFields
    shadowColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    shadewDistance: Option.some({ x: 0, y: 0, z: 0 }),
    outlineColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    outlineDistance: Option.some({ x: 0, y: 0, z: 0 }),
    // RawImageFields
    color: Option.some({ r: 255, g: 255, b: 255, a: 255, palette: Option.none() }),
    fileGuid: Option.some(''),
    softMaskGuid: Option.some(''),
  }

  public static readonly SCHEMA: (typeof CanvasDomSchema.nodes)['UIRawImage'] =
    CanvasDomSchema.nodes.UIRawImage

  /** @anchor canvas-render-dom-schema */
  public constructor(source: z.output<typeof UIRawImageNodeClass.SCHEMA>, session: RenderSession) {
    super(source, 'UIRawImage', session)
  }

  /** @anchor canvas-render-node-render-order */
  public prepareLayout(parentSize: Vector3): void {
    this.applyLayout(
      LayoutRenderer.getInstance().render(
        this.input,
        UIRawImageNodeClass.DEFAULT,
        this.getRuntime(),
        parentSize,
      ),
    )
  }

  /** @anchor canvas-render-node-render-order */
  public renderFeatures(): void {
    // 原始图片
    RawImageRenderer.getInstance().render(
      this.input,
      UIRawImageNodeClass.DEFAULT,
      this.getRuntime(),
    )

    // 通用阴影/描边（应用到本体）
    ShadowOutlineRenderer.getInstance().render(
      this.input,
      UIRawImageNodeClass.DEFAULT,
      this.getRuntime(),
    )
  }
}

/** @anchor canvas-render-node-interfaces */
export class UISpineNodeClass extends CanvasNodeModel<CanvasUISchema['UISpine']> {
  private static readonly DEFAULT: CanvasUISchema['UISpine'] = {
    name: '',
    guid: Option.none(),
    pos: Option.some({ x: 0, y: 0, z: 0 }),
    legacyPos: Option.some({ x: 0, y: 0, z: 0 }),
    size: Option.some({ x: 100, y: 100, z: 0 }),
    parentAnchor: Option.some(TextAnchor.LOWER_LEFT),
    anchor: Option.some(TextAnchor.LOWER_LEFT),
    widthExtend: Option.some(ExtentType.ABSOLUTE),
    heightExtend: Option.some(ExtentType.ABSOLUTE),
    rot: Option.some(0),
    shadowColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    shadewDistance: Option.some({ x: 0, y: 0, z: 0 }),
    outlineColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    outlineDistance: Option.some({ x: 0, y: 0, z: 0 }),
    color: Option.some({ r: 255, g: 255, b: 255, a: 255, palette: Option.none() }),
    atlasGuid: Option.some(''),
    skelGuid: Option.some(''),
    jsonGuid: Option.some(''),
    pngGuids: Option.some([]),
    animIndex: Option.some(0),
  }

  public static readonly SCHEMA: (typeof CanvasDomSchema.nodes)['UISpine'] =
    CanvasDomSchema.nodes.UISpine

  /** @anchor canvas-render-dom-schema */
  public constructor(source: z.output<typeof UISpineNodeClass.SCHEMA>, session: RenderSession) {
    super(source, 'UISpine', session)
  }

  /** @anchor canvas-render-node-render-order */
  public prepareLayout(parentSize: Vector3): void {
    this.applyLayout(
      LayoutRenderer.getInstance().render(
        this.input,
        UISpineNodeClass.DEFAULT,
        this.getRuntime(),
        parentSize,
      ),
    )
  }

  /** @anchor canvas-render-node-render-order */
  public renderFeatures(): void {
    SpineRenderer.getInstance().render(this.input, UISpineNodeClass.DEFAULT, this.getRuntime())
    ShadowOutlineRenderer.getInstance().render(
      this.input,
      UISpineNodeClass.DEFAULT,
      this.getRuntime(),
    )
  }
}

/** @anchor canvas-render-node-interfaces */
export class ScrollRectNodeClass extends CanvasNodeModel<CanvasUISchema['ScrollRect']> {
  private static readonly DEFAULT: CanvasUISchema['ScrollRect'] = {
    // LayoutFields
    name: '',
    guid: Option.none(),
    pos: Option.some({ x: 0, y: 0, z: 0 }),
    legacyPos: Option.some({ x: 0, y: 0, z: 0 }),
    size: Option.some({ x: 100, y: 100, z: 0 }),
    parentAnchor: Option.some(TextAnchor.LOWER_LEFT),
    anchor: Option.some(TextAnchor.LOWER_LEFT),
    widthExtend: Option.some(ExtentType.ABSOLUTE),
    heightExtend: Option.some(ExtentType.ABSOLUTE),
    rot: Option.some(0),
    // BoxFields
    color: Option.some({ r: 255, g: 255, b: 255, a: 255, palette: Option.none() }),
    borderWidth: Option.some(0),
    radius: Option.some(0),
    slicedPicGuid: Option.some(''),
    slicedBorderLB: Option.some({ x: 0, y: 0, z: 0 }),
    slicedBorderRT: Option.some({ x: 0, y: 0, z: 0 }),
    slicedRectLB: Option.some({ x: 0, y: 0, z: 0 }),
    slicedRectRT: Option.some({ x: 0, y: 0, z: 0 }),
    // MaskFields
    mask: Option.some(false),
    // ChildLayoutFields
    cellSize: Option.some({ x: 100, y: 100, z: 0 }),
    layoutSpacing: Option.some({ x: 0, y: 0, z: 0 }),
    layoutPadding: Option.some({ x: 0, y: 0, z: 0 }),
    childAlignment: Option.some(TextAnchor.UPPER_CENTER),
    verticalSpacing: Option.some(0),
    horizontalSpacing: Option.some(0),
    childExpandWidth: Option.some(false),
    childExpandHeight: Option.some(false),
    // ShadowOutlineFields
    shadowColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    shadewDistance: Option.some({ x: 0, y: 0, z: 0 }),
    outlineColor: Option.some({ r: 0, g: 0, b: 0, a: 0, palette: Option.none() }),
    outlineDistance: Option.some({ x: 0, y: 0, z: 0 }),
    // ScrollFields
    scrollHorizontalDisabled: Option.some(false),
    scrollVerticalDisabled: Option.some(false),
  }

  public static readonly SCHEMA: (typeof CanvasDomSchema.nodes)['ScrollRect'] =
    CanvasDomSchema.nodes.ScrollRect

  /** @anchor canvas-render-dom-schema */
  public constructor(source: z.output<typeof ScrollRectNodeClass.SCHEMA>, session: RenderSession) {
    super(source, 'ScrollRect', session)
  }

  /** @anchor canvas-render-node-render-order */
  public prepareLayout(parentSize: Vector3): void {
    this.applyLayout(
      LayoutRenderer.getInstance().render(
        this.input,
        ScrollRectNodeClass.DEFAULT,
        this.getRuntime(),
        parentSize,
      ),
    )
    this.applyChildLayout(
      ChildLayoutRenderer.getInstance().render(this.input, ScrollRectNodeClass.DEFAULT),
    )
  }

  /** @anchor canvas-render-node-render-order */
  public renderFeatures(): void {
    // 方框绘制
    BoxRenderer.getInstance().render(this.input, ScrollRectNodeClass.DEFAULT, this.getRuntime())

    // 遮罩
    MaskRenderer.getInstance().render(this.input, ScrollRectNodeClass.DEFAULT, this.getRuntime())

    // 通用阴影/描边（应用到本体）
    ShadowOutlineRenderer.getInstance().render(
      this.input,
      ScrollRectNodeClass.DEFAULT,
      this.getRuntime(),
    )

    // 滑动面板专用
    ScrollRenderer.getInstance().render(this.input, ScrollRectNodeClass.DEFAULT, this.getRuntime())
  }
}

export type CanvasNodeInstance =
  | UIRectNodeClass
  | UIButtonNodeClass
  | UILabelNodeClass
  | TextProNodeClass
  | UIInputNodeClass
  | UIRawImageNodeClass
  | UISpineNodeClass
  | ScrollRectNodeClass
