import { Option } from 'effect'
import { CanvasNames } from './CanvasNames.ts'

/** @anchor canvas-tags */
export enum HtmlKind {
  name = 'name',
  field = 'field',
  meta = 'meta',
}

/** @anchor canvas-tags */
export class HtmlAttribute {
  private constructor(
    public readonly htmlName: string,
    public readonly fieldName: string,
    public readonly kind: HtmlKind,
    public readonly required: boolean,
  ) {}

  /** @anchor canvas-tags */
  public static of(
    htmlName: string,
    fieldName: string,
    kind: HtmlKind,
    required: boolean,
  ): HtmlAttribute {
    return new HtmlAttribute(htmlName, fieldName, kind, required)
  }
}

/** @anchor canvas-tags */
export class HtmlTag {
  private constructor(
    public readonly tagName: string,
    public readonly canvasType: string,
    public readonly attributes: readonly HtmlAttribute[],
  ) {}

  /** @anchor canvas-tags */
  public static of(
    tagName: string,
    canvasType: string,
    attributes: readonly HtmlAttribute[],
  ): HtmlTag {
    return new HtmlTag(tagName, canvasType, attributes)
  }
}

/** @anchor canvas-tags */
export class CanvasTags {
  private static readonly tags: readonly HtmlTag[] = CanvasTags.build()

  private constructor() {}

  /** @anchor canvas-tags */
  public static tag(tagName: string): Option.Option<HtmlTag> {
    return Option.fromNullishOr(
      CanvasTags.tags.find((item: HtmlTag): boolean => item.tagName === tagName),
    )
  }

  /** @anchor canvas-tags */
  public static byType(canvasType: string): Option.Option<HtmlTag> {
    return Option.fromNullishOr(
      CanvasTags.tags.find((item: HtmlTag): boolean => item.canvasType === canvasType),
    )
  }

  /** @anchor canvas-tags */
  public static htmlAttribute(tag: HtmlTag, htmlName: string): Option.Option<HtmlAttribute> {
    return Option.fromNullishOr(
      tag.attributes.find((item: HtmlAttribute): boolean => item.htmlName === htmlName),
    )
  }

  /** @anchor canvas-tags */
  public static nodeField(tag: HtmlTag, fieldName: string): Option.Option<HtmlAttribute> {
    return Option.fromNullishOr(
      tag.attributes.find(
        (item: HtmlAttribute): boolean =>
          item.fieldName === fieldName && item.kind === HtmlKind.field,
      ),
    )
  }

  /** @anchor canvas-tags */
  private static build(): readonly HtmlTag[] {
    return [
      CanvasTags.canvas(),
      CanvasTags.rect(),
      CanvasTags.button(),
      CanvasTags.label(),
      CanvasTags.textPro(),
      CanvasTags.input(),
      CanvasTags.rawImage(),
      CanvasTags.spine(),
      CanvasTags.scroll(),
    ]
  }

  /** @anchor canvas-render-binary-field-mapping */
  private static canvas(): HtmlTag {
    return HtmlTag.of('BeCanvas', 'Canvas', [
      HtmlAttribute.of('name', 'name', HtmlKind.name, false),
      HtmlAttribute.of('width', '', HtmlKind.meta, true),
      HtmlAttribute.of('height', '', HtmlKind.meta, true),
    ])
  }

  /** @anchor canvas-render-binary-field-mapping */
  private static rect(): HtmlTag {
    return HtmlTag.of(
      'BeUIRect',
      'UIRect',
      CanvasTags.fields(
        CanvasTags.join([
          CanvasTags.layout(),
          CanvasTags.visual(),
          ['fillMethod', 'fillAmount', 'mask'],
          CanvasTags.slice(),
          CanvasTags.arrange(),
        ]),
      ),
    )
  }

  /** @anchor canvas-text-field-mapping */
  private static button(): HtmlTag {
    return HtmlTag.of('BeUIButton', 'UIButton', [
      ...CanvasTags.fields(
        CanvasTags.join([CanvasTags.layout(), CanvasTags.visual(), CanvasTags.slice()]),
      ),
      HtmlAttribute.of('label', 'label', HtmlKind.field, false),
      ...CanvasTags.fields([
        'FontSize',
        'Alignment',
        'lineSpacing',
        'lightFont',
        'characterSpacing',
        'fontStyle',
        'clickCBName',
        'pressCBName',
        'releaseCBName',
      ]),
    ])
  }

  /** @anchor canvas-text-field-mapping */
  private static label(): HtmlTag {
    return HtmlTag.of(
      'BeUILabel',
      'UILabel',
      CanvasTags.fields(
        CanvasTags.join([CanvasTags.layout(), CanvasTags.text(), ['outlineWidth', 'boolBestFit']]),
      ),
    )
  }

  /**
   * @anchor canvas-text-field-mapping
   * @anchor canvas-resource-field-mapping
   */
  private static textPro(): HtmlTag {
    return HtmlTag.of(
      'BeUILabelTextPro',
      'TextPro',
      CanvasTags.fields(
        CanvasTags.join([
          CanvasTags.layout(),
          CanvasTags.text(),
          ['boolBestFit', 'outlineWidth'],
          CanvasTags.effects(),
        ]),
      ),
    )
  }

  /** @anchor canvas-text-field-mapping */
  private static input(): HtmlTag {
    return HtmlTag.of(
      'BeUIInput',
      'UIInput',
      CanvasTags.fields(
        CanvasTags.join([
          CanvasTags.layout(),
          ['text', 'FontSize', 'Alignment', 'contentType', 'multiLine', 'labelColor'],
        ]),
      ),
    )
  }

  /** @anchor canvas-resource-field-mapping */
  private static rawImage(): HtmlTag {
    return HtmlTag.of(
      'BeUIRawImage',
      'UIRawImage',
      CanvasTags.fields(CanvasTags.join([CanvasTags.layout(), ['fileGuid', 'softMaskGuid']])),
    )
  }

  /** @anchor canvas-resource-field-mapping */
  private static spine(): HtmlTag {
    return HtmlTag.of(
      'BeUISpine',
      'UISpine',
      CanvasTags.fields(
        CanvasTags.join([
          CanvasTags.layout(),
          ['atlasGuid', 'skelGuid', 'jsonGuid', 'pngGuids', 'animIndex'],
        ]),
      ),
    )
  }

  /**
   * @anchor canvas-render-binary-field-mapping
   * @anchor canvas-resource-field-mapping
   */
  private static scroll(): HtmlTag {
    return HtmlTag.of(
      'BeUIRectScrollRect',
      'ScrollRect',
      CanvasTags.fields(
        CanvasTags.join([
          CanvasTags.layout(),
          ['scrollHorizontal', 'scrollVertical'],
          CanvasTags.visual(),
          ['mask'],
          CanvasTags.slice(),
          CanvasTags.arrange(),
        ]),
      ),
    )
  }

  /**
   * @anchor canvas-render-binary-field-mapping
   * @anchor canvas-text-field-mapping
   * @anchor canvas-resource-field-mapping
   */
  private static fields(names: readonly string[]): readonly HtmlAttribute[] {
    return names.map((fieldName: string): HtmlAttribute => {
      const htmlName: Option.Option<string> = CanvasNames.html(fieldName)
      if (Option.isNone(htmlName)) throw new Error(`字段没有 HTML 名称: ${fieldName}`)
      return HtmlAttribute.of(htmlName.value, fieldName, HtmlKind.field, false)
    })
  }

  private static join(groups: readonly (readonly string[])[]): readonly string[] {
    const names: string[] = []
    for (const group of groups) names.push(...group)
    return names
  }

  /** @anchor canvas-render-binary-field-mapping */
  private static layout(): readonly string[] {
    return [
      'name',
      'guid',
      'Pos',
      'pos',
      'size',
      'parentAnchor',
      'anchor',
      'widthExtend',
      'heightExtend',
      'rot',
      'color',
      'shadowColor',
      'shadewDistance',
      'outlineColor',
      'outlineDistance',
    ]
  }

  /** @anchor canvas-render-binary-field-mapping */
  private static visual(): readonly string[] {
    return ['BorderWidth', 'Radius']
  }

  /** @anchor canvas-render-binary-field-mapping */
  private static slice(): readonly string[] {
    return ['slicedPicGuid', 'slicedBorderLB', 'slicedBorderRT', 'slicedRectLB', 'slicedRectRT']
  }

  /** @anchor canvas-text-field-mapping */
  private static text(): readonly string[] {
    return [
      'text',
      'FontSize',
      'Alignment',
      'lineSpacing',
      'lightFont',
      'characterSpacing',
      'fontStyle',
    ]
  }

  /** @anchor canvas-render-binary-field-mapping */
  private static arrange(): readonly string[] {
    return [
      'cellSize',
      'spacing',
      'verticle_spacing',
      'horizontal_spacing',
      'childExpendWidth',
      'childExpendHeight',
      'childAlignment',
      'padding',
    ]
  }

  /** @anchor canvas-resource-field-mapping */
  private static effects(): readonly string[] {
    return [
      'FaceDilate',
      'bUNDERLAY',
      'UnderlayColor',
      'UnderlayOffset',
      'UnderlaySoftness',
      'bGLOW',
      'GlowColor',
      'GlowOffset',
      'GlowInner',
      'GlowOuter',
      'GlowPower',
      'enableVertexGradient',
      'bBEVEL',
      'SpecularColor',
      'LightAngle',
      'SpecularPower',
      'Reflectivity',
      'Diffuse',
      'Ambient',
      'Bevel',
      'BevelOffset',
      'BevelWidth',
      'BevelRoundness',
      'BevelClamp',
      'topLeft',
      'topRight',
      'bottomLeft',
      'bottomRight',
    ]
  }
}
