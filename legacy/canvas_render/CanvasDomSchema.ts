import { z } from 'zod'
import type { Simplify } from 'type-fest'
import { Option } from 'effect'
import type { CanvasUISchema } from './CanvasViewSchema.ts'
import {
  TextAnchor,
  ExtentType,
  FillMethodType,
  UIInputContentType,
  type Vector3,
  type CanvasColor,
  type LayoutFields,
  type BoxFields,
  type FillFields,
  type MaskFields,
  type ChildLayoutFields,
  type ShadowOutlineFields,
  type TextContentFields,
  type ButtonFields,
  type TextStyleFields,
  type LabelFields,
  type InputFields,
  type RawImageFields,
  type SpineFields,
  type ScrollFields,
  type TextEffectFields,
} from './CanvasTypesBase.ts'
import { CanvasColorValue, CanvasEnumNameParser, ValueResolver } from './CanvasRuntimeUtils.ts'

type DomFieldsShape<Fields> = {
  readonly [Field in keyof Fields as Lowercase<Field & string>]: z.ZodType<Fields[Field]>
}

type DomLayoutShape = DomFieldsShape<Omit<LayoutFields, 'name' | 'guid'>>
type DomBoxShape = DomFieldsShape<BoxFields>
type DomFillShape = DomFieldsShape<FillFields>
type DomMaskShape = DomFieldsShape<MaskFields>
type DomChildShape = DomFieldsShape<ChildLayoutFields>
interface DomShadowShape extends DomFieldsShape<Omit<ShadowOutlineFields, 'shadewDistance'>> {
  readonly shadowdistance: z.ZodType<ShadowOutlineFields['shadewDistance']>
}
type DomTextShape = DomFieldsShape<TextContentFields>
type DomButtonShape = DomFieldsShape<Omit<ButtonFields, 'size' | 'color'>>
interface DomStyleShape extends DomFieldsShape<Omit<TextStyleFields, 'textAlignment'>> {
  readonly alignment: z.ZodType<TextStyleFields['textAlignment']>
}
type DomLabelShape = DomFieldsShape<
  Omit<LabelFields, 'size' | 'text' | 'fontSize' | 'lineSpacing' | 'lightFont'>
>
interface DomInputFieldShape extends DomFieldsShape<Omit<InputFields, 'text' | 'labelColor'>> {
  readonly textcolor: z.ZodType<InputFields['labelColor']>
}
type DomRawShape = DomFieldsShape<RawImageFields>
type DomSpineShape = DomFieldsShape<SpineFields>
type DomScrollShape = DomFieldsShape<ScrollFields>
type DomEffectShape = DomFieldsShape<
  Omit<TextEffectFields, 'size' | 'text' | 'fontSize' | 'lineSpacing' | 'fontStyle'>
>
type DomIdentityShape = DomFieldsShape<Pick<LayoutFields, 'name' | 'guid'>>

type UIRectInput = z.output<
  z.ZodObject<
    Simplify<
      DomIdentityShape &
        DomLayoutShape &
        DomBoxShape &
        DomFillShape &
        DomMaskShape &
        DomChildShape &
        DomShadowShape
    >
  >
>

type UIButtonInput = z.output<
  z.ZodObject<
    Simplify<
      DomIdentityShape &
        DomLayoutShape &
        DomBoxShape &
        DomShadowShape &
        DomStyleShape &
        DomButtonShape
    >
  >
>

type UILabelInput = z.output<
  z.ZodObject<
    Simplify<
      DomIdentityShape &
        DomLayoutShape &
        DomShadowShape &
        DomTextShape &
        DomStyleShape &
        DomLabelShape
    >
  >
>

type TextProInput = z.output<
  z.ZodObject<
    Simplify<
      DomIdentityShape &
        DomLayoutShape &
        DomShadowShape &
        DomTextShape &
        DomStyleShape &
        DomEffectShape
    >
  >
>

type UIInputNodeInput = z.output<
  z.ZodObject<
    Simplify<
      DomIdentityShape &
        DomLayoutShape &
        Pick<DomBoxShape, 'color'> &
        DomShadowShape &
        DomTextShape &
        Pick<DomStyleShape, 'fontsize' | 'alignment'> &
        DomInputFieldShape
    >
  >
>

type UIRawImageInput = z.output<
  z.ZodObject<Simplify<DomIdentityShape & DomLayoutShape & DomShadowShape & DomRawShape>>
>

type UISpineInput = z.output<
  z.ZodObject<Simplify<DomIdentityShape & DomLayoutShape & DomShadowShape & DomSpineShape>>
>

type ScrollRectInput = z.output<
  z.ZodObject<
    Simplify<
      DomIdentityShape &
        DomLayoutShape &
        DomBoxShape &
        DomMaskShape &
        DomChildShape &
        DomShadowShape &
        DomScrollShape
    >
  >
>

interface CanvasRootOutput {
  readonly name: string
  readonly width: number
  readonly height: number
}

/** @anchor canvas-render-dom-schema */
export class CanvasDomSchema {
  private static readonly numeric: z.ZodType<number> = ValueResolver.numeric
  private static readonly string: z.ZodType<Option.Option<string>> = z
    .string()
    .optional()
    .transform(Option.fromNullishOr)
  private static readonly number: z.ZodType<Option.Option<number>> = CanvasDomSchema.numeric
    .optional()
    .transform(Option.fromNullishOr)
  private static readonly long: z.ZodType<Option.Option<number>> = CanvasDomSchema.numeric
    .pipe(z.number().int({ error: '必须为 Long(整型)' }))
    .optional()
    .transform(Option.fromNullishOr)
  private static readonly bool: z.ZodType<Option.Option<boolean>> = z
    .enum(['0', '1'])
    .transform((value: '0' | '1'): boolean => value === '1')
    .optional()
    .transform(Option.fromNullishOr)
  private static readonly vector: z.ZodType<Option.Option<Vector3>> = z
    .string()
    .transform((value: string): Vector3 => {
      const components: [number, number, number] = z
        .tuple([CanvasDomSchema.numeric, CanvasDomSchema.numeric, CanvasDomSchema.numeric])
        .parse(value.split(','))
      return { x: components[0], y: components[1], z: components[2] }
    })
    .optional()
    .transform(Option.fromNullishOr)
  private static readonly color: z.ZodType<Option.Option<CanvasColor>> = z
    .string()
    .transform((value: string): CanvasColor => {
      if (value.startsWith('rgba(') && value.endsWith(')'))
        return CanvasColorValue.parseHtmlRgbaToColor255(value, 'color')
      if (value.startsWith('palette(') && value.endsWith(')'))
        return CanvasColorValue.parseHtmlPaletteToColor255(value, 'color')
      throw new Error('颜色必须为 rgba 或 palette')
    })
    .optional()
    .transform(Option.fromNullishOr)
  private static readonly list: z.ZodType<Option.Option<string[]>> = z
    .string()
    .transform((value: string): string[] =>
      value === '' ? [] : z.array(z.string().min(1)).parse(value.split(',')),
    )
    .optional()
    .transform(Option.fromNullishOr)
  private static readonly anchor: z.ZodType<Option.Option<TextAnchor>> = z
    .string()
    .transform((value: string): TextAnchor =>
      z.enum(TextAnchor).parse(CanvasEnumNameParser.parse('TextAnchor', value)),
    )
    .optional()
    .transform(Option.fromNullishOr)
  private static readonly extent: z.ZodType<Option.Option<ExtentType>> = z
    .string()
    .transform((value: string): ExtentType =>
      z.enum(ExtentType).parse(CanvasEnumNameParser.parse('ExtentType', value)),
    )
    .optional()
    .transform(Option.fromNullishOr)
  private static readonly fill: z.ZodType<Option.Option<FillMethodType>> = z
    .string()
    .transform((value: string): FillMethodType =>
      z.enum(FillMethodType).parse(CanvasEnumNameParser.parse('FillMethodType', value)),
    )
    .optional()
    .transform(Option.fromNullishOr)
  private static readonly input: z.ZodType<Option.Option<UIInputContentType>> = z
    .string()
    .transform((value: string): UIInputContentType =>
      z.enum(UIInputContentType).parse(CanvasEnumNameParser.parse('UIInput.ContentType', value)),
    )
    .optional()
    .transform(Option.fromNullishOr)
  private static readonly layoutFields: DomLayoutShape = {
    pos: CanvasDomSchema.vector,
    legacypos: CanvasDomSchema.vector,
    size: CanvasDomSchema.vector,
    parentanchor: CanvasDomSchema.anchor,
    anchor: CanvasDomSchema.anchor,
    widthextend: CanvasDomSchema.extent,
    heightextend: CanvasDomSchema.extent,
    rot: CanvasDomSchema.number,
  }
  private static readonly boxFields: DomBoxShape = {
    color: CanvasDomSchema.color,
    borderwidth: CanvasDomSchema.number,
    radius: CanvasDomSchema.number,
    slicedpicguid: CanvasDomSchema.string,
    slicedborderlb: CanvasDomSchema.vector,
    slicedborderrt: CanvasDomSchema.vector,
    slicedrectlb: CanvasDomSchema.vector,
    slicedrectrt: CanvasDomSchema.vector,
  }
  private static readonly fillFields: DomFillShape = {
    fillmethod: CanvasDomSchema.fill,
    fillamount: CanvasDomSchema.number,
  }
  private static readonly maskFields: DomMaskShape = {
    mask: CanvasDomSchema.bool,
  }
  private static readonly childFields: DomChildShape = {
    cellsize: CanvasDomSchema.vector,
    layoutspacing: CanvasDomSchema.vector,
    layoutpadding: CanvasDomSchema.vector,
    childalignment: CanvasDomSchema.anchor,
    verticalspacing: CanvasDomSchema.number,
    horizontalspacing: CanvasDomSchema.number,
    childexpandwidth: CanvasDomSchema.bool,
    childexpandheight: CanvasDomSchema.bool,
  }
  private static readonly shadowFields: DomShadowShape = {
    shadowcolor: CanvasDomSchema.color,
    shadowdistance: CanvasDomSchema.vector,
    outlinecolor: CanvasDomSchema.color,
    outlinedistance: CanvasDomSchema.vector,
  }
  private static readonly textFields: DomTextShape = {
    text: CanvasDomSchema.string,
  }
  private static readonly buttonFields: DomButtonShape = {
    label: CanvasDomSchema.string,
    clickcbname: CanvasDomSchema.string,
    presscbname: CanvasDomSchema.string,
    releasecbname: CanvasDomSchema.string,
  }
  private static readonly styleFields: DomStyleShape = {
    fontsize: CanvasDomSchema.number,
    alignment: CanvasDomSchema.anchor,
    linespacing: CanvasDomSchema.number,
    characterspacing: CanvasDomSchema.number,
    fontstyle: CanvasDomSchema.long,
    lightfont: CanvasDomSchema.bool,
  }
  private static readonly labelFields: DomLabelShape = {
    color: CanvasDomSchema.color,
    boolbestfit: CanvasDomSchema.bool,
    outlinecolor: CanvasDomSchema.color,
    outlinedistance: CanvasDomSchema.vector,
    outlinewidth: CanvasDomSchema.number,
  }
  private static readonly inputFields: DomInputFieldShape = {
    multiline: CanvasDomSchema.bool,
    contenttype: CanvasDomSchema.input,
    textcolor: CanvasDomSchema.color,
  }
  private static readonly rawFields: DomRawShape = {
    color: CanvasDomSchema.color,
    fileguid: CanvasDomSchema.string,
    softmaskguid: CanvasDomSchema.string,
  }
  private static readonly spineFields: DomSpineShape = {
    color: CanvasDomSchema.color,
    atlasguid: CanvasDomSchema.string,
    skelguid: CanvasDomSchema.string,
    jsonguid: CanvasDomSchema.string,
    pngguids: CanvasDomSchema.list,
    animindex: CanvasDomSchema.long,
  }
  private static readonly scrollFields: DomScrollShape = {
    scrollhorizontaldisabled: CanvasDomSchema.bool,
    scrollverticaldisabled: CanvasDomSchema.bool,
  }
  private static readonly effectFields: DomEffectShape = {
    color: CanvasDomSchema.color,
    boolbestfit: CanvasDomSchema.bool,
    outlinecolor: CanvasDomSchema.color,
    outlinedistance: CanvasDomSchema.vector,
    outlinewidth: CanvasDomSchema.number,
    facedilate: CanvasDomSchema.number,
    underlaysoftness: CanvasDomSchema.number,
    glowoffset: CanvasDomSchema.number,
    glowinner: CanvasDomSchema.number,
    glowouter: CanvasDomSchema.number,
    glowpower: CanvasDomSchema.number,
    bevel: CanvasDomSchema.number,
    bevelwidth: CanvasDomSchema.number,
    beveloffset: CanvasDomSchema.number,
    bevelroundness: CanvasDomSchema.number,
    bevelclamp: CanvasDomSchema.number,
    lightangle: CanvasDomSchema.number,
    specularpower: CanvasDomSchema.number,
    reflectivity: CanvasDomSchema.number,
    diffuse: CanvasDomSchema.number,
    ambient: CanvasDomSchema.number,
    gradienttopleft: CanvasDomSchema.color,
    gradienttopright: CanvasDomSchema.color,
    gradientbottomleft: CanvasDomSchema.color,
    gradientbottomright: CanvasDomSchema.color,
    underlaycolor: CanvasDomSchema.color,
    glowcolor: CanvasDomSchema.color,
    specularcolor: CanvasDomSchema.color,
    vertexgradientenabled: CanvasDomSchema.bool,
    underlayenabled: CanvasDomSchema.bool,
    glowenabled: CanvasDomSchema.bool,
    bevelenabled: CanvasDomSchema.bool,
    underlayoffset: CanvasDomSchema.vector,
  }
  public static readonly nodes: {
    readonly UIRect: z.ZodType<CanvasUISchema['UIRect']>
    readonly UIButton: z.ZodType<CanvasUISchema['UIButton']>
    readonly UILabel: z.ZodType<CanvasUISchema['UILabel']>
    readonly TextPro: z.ZodType<CanvasUISchema['TextPro']>
    readonly UIInput: z.ZodType<CanvasUISchema['UIInput']>
    readonly UIRawImage: z.ZodType<CanvasUISchema['UIRawImage']>
    readonly UISpine: z.ZodType<CanvasUISchema['UISpine']>
    readonly ScrollRect: z.ZodType<CanvasUISchema['ScrollRect']>
  } = {
    UIRect: z
      .strictObject({
        name: z.string(),
        guid: CanvasDomSchema.long,
        ...CanvasDomSchema.layoutFields,
        ...CanvasDomSchema.boxFields,
        ...CanvasDomSchema.fillFields,
        ...CanvasDomSchema.maskFields,
        ...CanvasDomSchema.childFields,
        ...CanvasDomSchema.shadowFields,
      })
      .transform((attributes: UIRectInput): CanvasUISchema['UIRect'] => ({
        name: attributes.name,
        guid: attributes.guid,
        pos: attributes.pos,
        legacyPos: attributes.legacypos,
        size: attributes.size,
        parentAnchor: attributes.parentanchor,
        anchor: attributes.anchor,
        widthExtend: attributes.widthextend,
        heightExtend: attributes.heightextend,
        rot: attributes.rot,
        color: attributes.color,
        borderWidth: attributes.borderwidth,
        radius: attributes.radius,
        slicedPicGuid: attributes.slicedpicguid,
        slicedBorderLB: attributes.slicedborderlb,
        slicedBorderRT: attributes.slicedborderrt,
        slicedRectLB: attributes.slicedrectlb,
        slicedRectRT: attributes.slicedrectrt,
        fillMethod: attributes.fillmethod,
        fillAmount: attributes.fillamount,
        mask: attributes.mask,
        cellSize: attributes.cellsize,
        layoutSpacing: attributes.layoutspacing,
        layoutPadding: attributes.layoutpadding,
        childAlignment: attributes.childalignment,
        verticalSpacing: attributes.verticalspacing,
        horizontalSpacing: attributes.horizontalspacing,
        childExpandWidth: attributes.childexpandwidth,
        childExpandHeight: attributes.childexpandheight,
        shadowColor: attributes.shadowcolor,
        shadewDistance: attributes.shadowdistance,
        outlineColor: attributes.outlinecolor,
        outlineDistance: attributes.outlinedistance,
      })),
    UIButton: z
      .strictObject({
        name: z.string(),
        guid: CanvasDomSchema.long,
        ...CanvasDomSchema.layoutFields,
        ...CanvasDomSchema.boxFields,
        ...CanvasDomSchema.shadowFields,
        ...CanvasDomSchema.styleFields,
        ...CanvasDomSchema.buttonFields,
      })
      .transform((attributes: UIButtonInput): CanvasUISchema['UIButton'] => ({
        name: attributes.name,
        guid: attributes.guid,
        pos: attributes.pos,
        legacyPos: attributes.legacypos,
        size: attributes.size,
        parentAnchor: attributes.parentanchor,
        anchor: attributes.anchor,
        widthExtend: attributes.widthextend,
        heightExtend: attributes.heightextend,
        rot: attributes.rot,
        color: attributes.color,
        borderWidth: attributes.borderwidth,
        radius: attributes.radius,
        slicedPicGuid: attributes.slicedpicguid,
        slicedBorderLB: attributes.slicedborderlb,
        slicedBorderRT: attributes.slicedborderrt,
        slicedRectLB: attributes.slicedrectlb,
        slicedRectRT: attributes.slicedrectrt,
        shadowColor: attributes.shadowcolor,
        shadewDistance: attributes.shadowdistance,
        outlineColor: attributes.outlinecolor,
        outlineDistance: attributes.outlinedistance,
        label: attributes.label,
        fontSize: attributes.fontsize,
        textAlignment: attributes.alignment,
        lineSpacing: attributes.linespacing,
        characterSpacing: attributes.characterspacing,
        fontStyle: attributes.fontstyle,
        lightFont: attributes.lightfont,
        clickCBName: attributes.clickcbname,
        pressCBName: attributes.presscbname,
        releaseCBName: attributes.releasecbname,
      })),
    UILabel: z
      .strictObject({
        name: z.string(),
        guid: CanvasDomSchema.long,
        ...CanvasDomSchema.layoutFields,
        ...CanvasDomSchema.shadowFields,
        ...CanvasDomSchema.textFields,
        ...CanvasDomSchema.styleFields,
        ...CanvasDomSchema.labelFields,
      })
      .transform((attributes: UILabelInput): CanvasUISchema['UILabel'] => ({
        name: attributes.name,
        guid: attributes.guid,
        pos: attributes.pos,
        legacyPos: attributes.legacypos,
        size: attributes.size,
        parentAnchor: attributes.parentanchor,
        anchor: attributes.anchor,
        widthExtend: attributes.widthextend,
        heightExtend: attributes.heightextend,
        rot: attributes.rot,
        shadowColor: attributes.shadowcolor,
        shadewDistance: attributes.shadowdistance,
        text: attributes.text,
        fontSize: attributes.fontsize,
        textAlignment: attributes.alignment,
        lineSpacing: attributes.linespacing,
        characterSpacing: attributes.characterspacing,
        fontStyle: attributes.fontstyle,
        color: attributes.color,
        lightFont: attributes.lightfont,
        boolBestFit: attributes.boolbestfit,
        outlineColor: attributes.outlinecolor,
        outlineDistance: attributes.outlinedistance,
        outlineWidth: attributes.outlinewidth,
      })),
    TextPro: z
      .strictObject({
        name: z.string(),
        guid: CanvasDomSchema.long,
        ...CanvasDomSchema.layoutFields,
        ...CanvasDomSchema.shadowFields,
        ...CanvasDomSchema.textFields,
        ...CanvasDomSchema.styleFields,
        ...CanvasDomSchema.effectFields,
      })
      .transform((attributes: TextProInput): CanvasUISchema['TextPro'] => ({
        name: attributes.name,
        guid: attributes.guid,
        pos: attributes.pos,
        legacyPos: attributes.legacypos,
        size: attributes.size,
        parentAnchor: attributes.parentanchor,
        anchor: attributes.anchor,
        widthExtend: attributes.widthextend,
        heightExtend: attributes.heightextend,
        rot: attributes.rot,
        shadowColor: attributes.shadowcolor,
        shadewDistance: attributes.shadowdistance,
        text: attributes.text,
        fontSize: attributes.fontsize,
        textAlignment: attributes.alignment,
        lineSpacing: attributes.linespacing,
        characterSpacing: attributes.characterspacing,
        fontStyle: attributes.fontstyle,
        lightFont: attributes.lightfont,
        color: attributes.color,
        boolBestFit: attributes.boolbestfit,
        outlineColor: attributes.outlinecolor,
        outlineDistance: attributes.outlinedistance,
        outlineWidth: attributes.outlinewidth,
        faceDilate: attributes.facedilate,
        underlaySoftness: attributes.underlaysoftness,
        glowOffset: attributes.glowoffset,
        glowInner: attributes.glowinner,
        glowOuter: attributes.glowouter,
        glowPower: attributes.glowpower,
        bevel: attributes.bevel,
        bevelWidth: attributes.bevelwidth,
        bevelOffset: attributes.beveloffset,
        bevelRoundness: attributes.bevelroundness,
        bevelClamp: attributes.bevelclamp,
        lightAngle: attributes.lightangle,
        specularPower: attributes.specularpower,
        reflectivity: attributes.reflectivity,
        diffuse: attributes.diffuse,
        ambient: attributes.ambient,
        gradientTopLeft: attributes.gradienttopleft,
        gradientTopRight: attributes.gradienttopright,
        gradientBottomLeft: attributes.gradientbottomleft,
        gradientBottomRight: attributes.gradientbottomright,
        underlayColor: attributes.underlaycolor,
        glowColor: attributes.glowcolor,
        specularColor: attributes.specularcolor,
        vertexGradientEnabled: attributes.vertexgradientenabled,
        underlayEnabled: attributes.underlayenabled,
        glowEnabled: attributes.glowenabled,
        bevelEnabled: attributes.bevelenabled,
        underlayOffset: attributes.underlayoffset,
      })),
    UIInput: z
      .strictObject({
        name: z.string(),
        guid: CanvasDomSchema.long,
        ...CanvasDomSchema.layoutFields,
        color: CanvasDomSchema.color,
        ...CanvasDomSchema.shadowFields,
        ...CanvasDomSchema.textFields,
        fontsize: CanvasDomSchema.number,
        alignment: CanvasDomSchema.anchor,
        ...CanvasDomSchema.inputFields,
      })
      .transform((attributes: UIInputNodeInput): CanvasUISchema['UIInput'] => ({
        name: attributes.name,
        guid: attributes.guid,
        pos: attributes.pos,
        legacyPos: attributes.legacypos,
        size: attributes.size,
        parentAnchor: attributes.parentanchor,
        anchor: attributes.anchor,
        widthExtend: attributes.widthextend,
        heightExtend: attributes.heightextend,
        rot: attributes.rot,
        color: attributes.color,
        shadowColor: attributes.shadowcolor,
        shadewDistance: attributes.shadowdistance,
        outlineColor: attributes.outlinecolor,
        outlineDistance: attributes.outlinedistance,
        text: attributes.text,
        fontSize: attributes.fontsize,
        textAlignment: attributes.alignment,
        multiLine: attributes.multiline,
        contentType: attributes.contenttype,
        labelColor: attributes.textcolor,
      })),
    UIRawImage: z
      .strictObject({
        name: z.string(),
        guid: CanvasDomSchema.long,
        ...CanvasDomSchema.layoutFields,
        ...CanvasDomSchema.shadowFields,
        ...CanvasDomSchema.rawFields,
      })
      .transform((attributes: UIRawImageInput): CanvasUISchema['UIRawImage'] => ({
        name: attributes.name,
        guid: attributes.guid,
        pos: attributes.pos,
        legacyPos: attributes.legacypos,
        size: attributes.size,
        parentAnchor: attributes.parentanchor,
        anchor: attributes.anchor,
        widthExtend: attributes.widthextend,
        heightExtend: attributes.heightextend,
        rot: attributes.rot,
        shadowColor: attributes.shadowcolor,
        shadewDistance: attributes.shadowdistance,
        outlineColor: attributes.outlinecolor,
        outlineDistance: attributes.outlinedistance,
        color: attributes.color,
        fileGuid: attributes.fileguid,
        softMaskGuid: attributes.softmaskguid,
      })),
    UISpine: z
      .strictObject({
        name: z.string(),
        guid: CanvasDomSchema.long,
        ...CanvasDomSchema.layoutFields,
        ...CanvasDomSchema.shadowFields,
        ...CanvasDomSchema.spineFields,
      })
      .transform((attributes: UISpineInput): CanvasUISchema['UISpine'] => ({
        name: attributes.name,
        guid: attributes.guid,
        pos: attributes.pos,
        legacyPos: attributes.legacypos,
        size: attributes.size,
        parentAnchor: attributes.parentanchor,
        anchor: attributes.anchor,
        widthExtend: attributes.widthextend,
        heightExtend: attributes.heightextend,
        rot: attributes.rot,
        shadowColor: attributes.shadowcolor,
        shadewDistance: attributes.shadowdistance,
        outlineColor: attributes.outlinecolor,
        outlineDistance: attributes.outlinedistance,
        color: attributes.color,
        atlasGuid: attributes.atlasguid,
        skelGuid: attributes.skelguid,
        jsonGuid: attributes.jsonguid,
        pngGuids: attributes.pngguids,
        animIndex: attributes.animindex,
      })),
    ScrollRect: z
      .strictObject({
        name: z.string(),
        guid: CanvasDomSchema.long,
        ...CanvasDomSchema.layoutFields,
        ...CanvasDomSchema.boxFields,
        ...CanvasDomSchema.maskFields,
        ...CanvasDomSchema.childFields,
        ...CanvasDomSchema.shadowFields,
        ...CanvasDomSchema.scrollFields,
      })
      .transform((attributes: ScrollRectInput): CanvasUISchema['ScrollRect'] => ({
        name: attributes.name,
        guid: attributes.guid,
        pos: attributes.pos,
        legacyPos: attributes.legacypos,
        size: attributes.size,
        parentAnchor: attributes.parentanchor,
        anchor: attributes.anchor,
        widthExtend: attributes.widthextend,
        heightExtend: attributes.heightextend,
        rot: attributes.rot,
        color: attributes.color,
        borderWidth: attributes.borderwidth,
        radius: attributes.radius,
        slicedPicGuid: attributes.slicedpicguid,
        slicedBorderLB: attributes.slicedborderlb,
        slicedBorderRT: attributes.slicedborderrt,
        slicedRectLB: attributes.slicedrectlb,
        slicedRectRT: attributes.slicedrectrt,
        mask: attributes.mask,
        cellSize: attributes.cellsize,
        layoutSpacing: attributes.layoutspacing,
        layoutPadding: attributes.layoutpadding,
        childAlignment: attributes.childalignment,
        verticalSpacing: attributes.verticalspacing,
        horizontalSpacing: attributes.horizontalspacing,
        childExpandWidth: attributes.childexpandwidth,
        childExpandHeight: attributes.childexpandheight,
        shadowColor: attributes.shadowcolor,
        shadewDistance: attributes.shadowdistance,
        outlineColor: attributes.outlinecolor,
        outlineDistance: attributes.outlinedistance,
        scrollHorizontalDisabled: attributes.scrollhorizontaldisabled,
        scrollVerticalDisabled: attributes.scrollverticaldisabled,
      })),
  }
  public static readonly root: z.ZodType<CanvasRootOutput> = z.strictObject({
    name: z.string(),
    width: CanvasDomSchema.numeric.pipe(z.number().positive()),
    height: CanvasDomSchema.numeric.pipe(z.number().positive()),
  })

  /** @anchor canvas-render-dom-schema @anchor canvas-render-binary-field-mapping @anchor canvas-render-binary-dependencies @anchor canvas-render-xml-grammar */
  public static extract<Schema extends z.ZodType>(
    element: Element,
    schema: Schema,
  ): z.output<Schema> {
    return schema.parse(
      Object.fromEntries(
        Array.from(element.attributes, (attribute: Attr): [string, string] => [
          attribute.name,
          attribute.value,
        ]),
      ),
    )
  }
}
