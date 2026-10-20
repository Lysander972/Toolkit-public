import { Option } from 'effect'

/* ---------- 类型系统基础 ---------- */

/** @anchor canvas-render-field-contract */
enum TextAnchor {
  UPPER_LEFT = 0,
  UPPER_CENTER = 1,
  UPPER_RIGHT = 2,
  MIDDLE_LEFT = 3,
  MIDDLE_CENTER = 4,
  MIDDLE_RIGHT = 5,
  LOWER_LEFT = 6,
  LOWER_CENTER = 7,
  LOWER_RIGHT = 8,
}

/** @anchor canvas-render-field-contract */
enum ExtentType {
  ABSOLUTE = 0,
  PERCENT = 1,
  TO_EDGE = 2,
  ASPECT = 3,
}

/** @anchor canvas-render-field-contract */
enum FillMethodType {
  NONE = -1,
  HORIZONTAL = 0,
  VERTICAL = 1,
  RADIAL_90 = 2,
  RADIAL_180 = 3,
  RADIAL_360 = 4,
}

/** @anchor canvas-render-field-contract */
enum UIInputContentType {
  STANDARD = 0,
  INTEGER_NUMBER = 1,
  DECIMAL_NUMBER = 2,
  PASSWORD = 3,
}

/** @anchor canvas-render-field-contract */
export type UIComponentType =
  | 'UIRect'
  | 'UIButton'
  | 'UILabel'
  | 'TextPro'
  | 'UIInput'
  | 'UIRawImage'
  | 'UISpine'
  | 'ScrollRect'
/** @anchor canvas-render-field-contract */
export interface Vector3 {
  readonly x: number
  readonly y: number
  readonly z: number
}

/** @anchor canvas-render-field-contract */
export interface CanvasColor {
  readonly r: number
  readonly g: number
  readonly b: number
  readonly a: number
  readonly palette: Option.Option<string>
}

/** @anchor canvas-render-field-contract */
export interface LayoutRect {
  readonly left: number
  readonly top: number
  readonly width: number
  readonly height: number
  readonly rot: number
  readonly transformOriginX: number
  readonly transformOriginY: number
}

/* ---------- 特性字段接口 ---------- */

/** @anchor canvas-render-node-interfaces */
export interface TextContentFields {
  readonly text: Option.Option<string>
}

/** @anchor canvas-render-node-interfaces */
export interface LayoutFields {
  readonly guid: Option.Option<number>
  // 节点名称（辅助组装路径名）
  readonly name: string
  // 布局字段
  readonly pos: Option.Option<Vector3>
  readonly legacyPos: Option.Option<Vector3>
  readonly size: Option.Option<Vector3>
  readonly parentAnchor: Option.Option<TextAnchor>
  readonly anchor: Option.Option<TextAnchor>
  readonly widthExtend: Option.Option<ExtentType>
  readonly heightExtend: Option.Option<ExtentType>
  readonly rot: Option.Option<number>
}

/**
 * @anchor canvas-render-node-interfaces
 * @anchor canvas-render-box
 * @anchor canvas-render-renderer-boundaries
 */
export interface BoxFields {
  // 方框基础样式
  readonly color: Option.Option<CanvasColor>
  readonly borderWidth: Option.Option<number>
  readonly radius: Option.Option<number>
  // 九宫格相关（有九宫格时主动禁用方框厚度）
  readonly slicedPicGuid: Option.Option<string>
  readonly slicedBorderLB: Option.Option<Vector3>
  readonly slicedBorderRT: Option.Option<Vector3>
  readonly slicedRectLB: Option.Option<Vector3>
  readonly slicedRectRT: Option.Option<Vector3>
}

/**
 * @anchor canvas-render-node-interfaces
 * @anchor canvas-render-fill
 */
export interface FillFields {
  readonly fillMethod: Option.Option<FillMethodType>
  readonly fillAmount: Option.Option<number>
}

/**
 * @anchor canvas-render-node-interfaces
 * @anchor canvas-render-mask
 */
export interface MaskFields {
  readonly mask: Option.Option<boolean>
}

/** @anchor canvas-render-node-interfaces */
export interface ChildLayoutFields {
  readonly cellSize: Option.Option<Vector3>
  readonly layoutSpacing: Option.Option<Vector3>
  readonly layoutPadding: Option.Option<Vector3>
  readonly childAlignment: Option.Option<TextAnchor>
  readonly verticalSpacing: Option.Option<number>
  readonly horizontalSpacing: Option.Option<number>
  readonly childExpandWidth: Option.Option<boolean>
  readonly childExpandHeight: Option.Option<boolean>
}

/**
 * @anchor canvas-render-node-interfaces
 * @anchor canvas-render-shadow
 */
export interface ShadowOutlineFields {
  readonly shadowColor: Option.Option<CanvasColor>
  readonly shadewDistance: Option.Option<Vector3>
  readonly outlineColor: Option.Option<CanvasColor>
  readonly outlineDistance: Option.Option<Vector3>
}

/**
 * @anchor canvas-render-node-interfaces
 * @anchor canvas-render-button
 */
export interface ButtonFields {
  readonly size: Option.Option<Vector3>
  readonly label: Option.Option<string>
  // 文字颜色（同时也是按钮背景色，这就是问题所在）
  readonly color: Option.Option<CanvasColor>
  // 三个回调
  readonly clickCBName: Option.Option<string>
  readonly pressCBName: Option.Option<string>
  readonly releaseCBName: Option.Option<string>
}

/**
 * @anchor canvas-render-node-interfaces
 * @anchor canvas-render-text-style
 */
export interface TextStyleFields {
  // 字号
  readonly fontSize: Option.Option<number>
  // 对齐方式
  readonly textAlignment: Option.Option<TextAnchor>
  // 行间距
  readonly lineSpacing: Option.Option<number>
  // 字符间距
  readonly characterSpacing: Option.Option<number>
  // 字体样式
  readonly fontStyle: Option.Option<number>
  readonly lightFont: Option.Option<boolean>
}

/**
 * @anchor canvas-render-node-interfaces
 * @anchor canvas-render-scroll
 */
export interface ScrollFields {
  readonly scrollHorizontalDisabled: Option.Option<boolean>
  readonly scrollVerticalDisabled: Option.Option<boolean>
}

/**
 * @anchor canvas-render-node-interfaces
 * @anchor canvas-render-input
 */
export interface InputFields {
  readonly text: Option.Option<string>
  readonly multiLine: Option.Option<boolean>
  readonly contentType: Option.Option<UIInputContentType>
  readonly labelColor: Option.Option<CanvasColor>
}

/**
 * @anchor canvas-render-node-interfaces
 * @anchor canvas-render-label
 */
export interface LabelFields {
  readonly size: Option.Option<Vector3>
  readonly text: Option.Option<string>
  readonly fontSize: Option.Option<number>
  readonly lineSpacing: Option.Option<number>
  //整体颜色
  readonly color: Option.Option<CanvasColor>
  //纤细字体
  readonly lightFont: Option.Option<boolean>
  // 自动适配尺寸
  readonly boolBestFit: Option.Option<boolean>
  //描边
  readonly outlineColor: Option.Option<CanvasColor>
  readonly outlineDistance: Option.Option<Vector3>
  readonly outlineWidth: Option.Option<number>
}

/**
 * @anchor canvas-render-text-effect-values
 * @anchor canvas-render-binary-field-evidence
 * @anchor canvas-render-text-effect
 */
export interface TextEffectFields {
  readonly size: Option.Option<Vector3>
  readonly text: Option.Option<string>
  readonly fontSize: Option.Option<number>
  readonly lineSpacing: Option.Option<number>
  readonly fontStyle: Option.Option<number>
  // 基础颜色(正片叠底需要)
  readonly color: Option.Option<CanvasColor>
  // 自动适配尺寸
  readonly boolBestFit: Option.Option<boolean>
  // 专有描边（SDF）
  readonly outlineColor: Option.Option<CanvasColor>
  readonly outlineDistance: Option.Option<Vector3>
  readonly outlineWidth: Option.Option<number>
  readonly faceDilate: Option.Option<number>

  // 顶点渐变
  readonly gradientTopLeft: Option.Option<CanvasColor>
  readonly gradientTopRight: Option.Option<CanvasColor>
  readonly gradientBottomLeft: Option.Option<CanvasColor>
  readonly gradientBottomRight: Option.Option<CanvasColor>
  readonly vertexGradientEnabled: Option.Option<boolean>

  // Underlay
  readonly underlayEnabled: Option.Option<boolean>
  readonly underlayColor: Option.Option<CanvasColor>
  readonly underlayOffset: Option.Option<Vector3>
  readonly underlaySoftness: Option.Option<number>

  // Glow
  readonly glowEnabled: Option.Option<boolean>
  readonly glowColor: Option.Option<CanvasColor>
  readonly glowOffset: Option.Option<number>
  readonly glowInner: Option.Option<number>
  readonly glowOuter: Option.Option<number>
  readonly glowPower: Option.Option<number>

  // Bevel
  readonly bevelEnabled: Option.Option<boolean>
  readonly bevel: Option.Option<number>
  readonly bevelWidth: Option.Option<number>
  readonly bevelOffset: Option.Option<number>
  readonly bevelRoundness: Option.Option<number>
  readonly bevelClamp: Option.Option<number>
  readonly lightAngle: Option.Option<number>
  readonly specularColor: Option.Option<CanvasColor>
  readonly specularPower: Option.Option<number>
  readonly reflectivity: Option.Option<number>
  readonly diffuse: Option.Option<number>
  readonly ambient: Option.Option<number>
}

/**
 * @anchor canvas-render-node-interfaces
 * @anchor canvas-render-raw-image
 */
export interface RawImageFields {
  readonly color: Option.Option<CanvasColor>
  readonly fileGuid: Option.Option<string>
  readonly softMaskGuid: Option.Option<string>
}

/**
 * @anchor canvas-render-node-interfaces
 * @anchor canvas-render-spine
 */
export interface SpineFields {
  readonly color: Option.Option<CanvasColor>
  readonly atlasGuid: Option.Option<string>
  readonly skelGuid: Option.Option<string>
  readonly jsonGuid: Option.Option<string>
  readonly pngGuids: Option.Option<readonly string[]>
  readonly animIndex: Option.Option<number>
}
