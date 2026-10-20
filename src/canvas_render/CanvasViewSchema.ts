import type {
  UIComponentType,
  LayoutFields,
  BoxFields,
  FillFields,
  MaskFields,
  ChildLayoutFields,
  ShadowOutlineFields,
  ButtonFields,
  TextContentFields,
  TextStyleFields,
  LabelFields,
  TextEffectFields,
  InputFields,
  RawImageFields,
  SpineFields,
  ScrollFields,
} from './CanvasTypesBase.ts'

/** @anchor canvas-render-schema-dts */
export interface UIRectSchema
  extends LayoutFields, BoxFields, FillFields, MaskFields, ChildLayoutFields, ShadowOutlineFields {}
/** @anchor canvas-render-schema-dts */
export interface UIButtonSchema
  extends LayoutFields, BoxFields, ShadowOutlineFields, TextStyleFields, ButtonFields {}
/** @anchor canvas-render-schema-dts */
export interface UILabelSchema
  extends LayoutFields, ShadowOutlineFields, TextContentFields, TextStyleFields, LabelFields {}
/** @anchor canvas-render-schema-dts */
export interface TextProSchema
  extends LayoutFields, ShadowOutlineFields, TextContentFields, TextStyleFields, TextEffectFields {}
/** @anchor canvas-render-schema-dts */
export interface UIInputSchema
  extends
    LayoutFields,
    Pick<BoxFields, 'color'>,
    ShadowOutlineFields,
    TextContentFields,
    Pick<TextStyleFields, 'fontSize' | 'textAlignment'>,
    InputFields {}
/** @anchor canvas-render-schema-dts */
export interface UIRawImageSchema extends LayoutFields, ShadowOutlineFields, RawImageFields {}
/** @anchor canvas-render-schema-dts */
export interface UISpineSchema extends LayoutFields, ShadowOutlineFields, SpineFields {}
/** @anchor canvas-render-schema-dts */
export interface ScrollRectSchema
  extends
    LayoutFields,
    BoxFields,
    MaskFields,
    ChildLayoutFields,
    ShadowOutlineFields,
    ScrollFields {}

/** @anchor canvas-render-schema-dts */
export interface CanvasUISchema {
  readonly UIRect: UIRectSchema
  readonly UIButton: UIButtonSchema
  readonly UILabel: UILabelSchema
  readonly TextPro: TextProSchema
  readonly UIInput: UIInputSchema
  readonly UIRawImage: UIRawImageSchema
  readonly UISpine: UISpineSchema
  // ScrollRect：方框、遮罩、子元素排版、通用阴影描边、滑动面板。
  readonly ScrollRect: ScrollRectSchema
}

/** @anchor canvas-render-schema-dts */
export type CanvasUISchemaCoversAll = CanvasUISchema[UIComponentType]

/** @anchor canvas-render-schema-dts */
export type CanvasUISchemaOf<Type extends UIComponentType> = CanvasUISchema[Type]
