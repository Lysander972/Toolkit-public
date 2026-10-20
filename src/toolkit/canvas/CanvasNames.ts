import { Option } from 'effect'

/** @anchor canvas-names */
class NamePair {
  private constructor(
    public readonly fieldName: string,
    public readonly htmlName: string,
  ) {}

  /** @anchor canvas-names */
  public static of(fieldName: string, htmlName: string): NamePair {
    return new NamePair(fieldName, htmlName)
  }
}

/** @anchor canvas-names */
export class CanvasNames {
  private constructor() {}

  /** @anchor canvas-names */
  public static html(fieldName: string): Option.Option<string> {
    return Option.map(
      Option.fromNullishOr(
        CanvasNames.pairs().find((pair: NamePair): boolean => pair.fieldName === fieldName),
      ),
      (pair: NamePair): string => pair.htmlName,
    )
  }

  /**
   * @anchor canvas-names
   * @anchor canvas-render-binary-field-mapping
   * @anchor canvas-text-field-mapping
   * @anchor canvas-resource-field-mapping
   */
  private static pairs(): readonly NamePair[] {
    return [
      NamePair.of('name', 'name'),
      NamePair.of('guid', 'guid'),
      NamePair.of('Pos', 'pos'),
      NamePair.of('pos', 'legacypos'),
      NamePair.of('size', 'size'),
      NamePair.of('rot', 'rot'),
      NamePair.of('parentAnchor', 'parentanchor'),
      NamePair.of('anchor', 'anchor'),
      NamePair.of('widthExtend', 'widthextend'),
      NamePair.of('heightExtend', 'heightextend'),
      NamePair.of('color', 'color'),
      NamePair.of('BorderWidth', 'borderwidth'),
      NamePair.of('Radius', 'radius'),
      NamePair.of('mask', 'mask'),
      NamePair.of('shadowColor', 'shadowcolor'),
      NamePair.of('shadewDistance', 'shadowdistance'),
      NamePair.of('outlineColor', 'outlinecolor'),
      NamePair.of('outlineDistance', 'outlinedistance'),
      NamePair.of('outlineWidth', 'outlinewidth'),
      NamePair.of('text', 'text'),
      NamePair.of('label', 'label'),
      NamePair.of('labelColor', 'textcolor'),
      NamePair.of('FontSize', 'fontsize'),
      NamePair.of('Alignment', 'alignment'),
      NamePair.of('lineSpacing', 'linespacing'),
      NamePair.of('lightFont', 'lightfont'),
      NamePair.of('boolBestFit', 'boolbestfit'),
      NamePair.of('characterSpacing', 'characterspacing'),
      NamePair.of('fontStyle', 'fontstyle'),
      NamePair.of('fillMethod', 'fillmethod'),
      NamePair.of('fillAmount', 'fillamount'),
      NamePair.of('cellSize', 'cellsize'),
      NamePair.of('spacing', 'layoutspacing'),
      NamePair.of('verticle_spacing', 'verticalspacing'),
      NamePair.of('horizontal_spacing', 'horizontalspacing'),
      NamePair.of('padding', 'layoutpadding'),
      NamePair.of('childAlignment', 'childalignment'),
      NamePair.of('childExpendWidth', 'childexpandwidth'),
      NamePair.of('childExpendHeight', 'childexpandheight'),
      NamePair.of('slicedPicGuid', 'slicedpicguid'),
      NamePair.of('slicedBorderLB', 'slicedborderlb'),
      NamePair.of('slicedBorderRT', 'slicedborderrt'),
      NamePair.of('slicedRectLB', 'slicedrectlb'),
      NamePair.of('slicedRectRT', 'slicedrectrt'),
      NamePair.of('fileGuid', 'fileguid'),
      NamePair.of('softMaskGuid', 'softmaskguid'),
      NamePair.of('atlasGuid', 'atlasguid'),
      NamePair.of('skelGuid', 'skelguid'),
      NamePair.of('jsonGuid', 'jsonguid'),
      NamePair.of('pngGuids', 'pngguids'),
      NamePair.of('animIndex', 'animindex'),
      NamePair.of('clickCBName', 'clickcbname'),
      NamePair.of('pressCBName', 'presscbname'),
      NamePair.of('releaseCBName', 'releasecbname'),
      NamePair.of('multiLine', 'multiline'),
      NamePair.of('contentType', 'contenttype'),
      NamePair.of('scrollHorizontal', 'scrollhorizontaldisabled'),
      NamePair.of('scrollVertical', 'scrollverticaldisabled'),
      NamePair.of('FaceDilate', 'facedilate'),
      NamePair.of('bUNDERLAY', 'underlayenabled'),
      NamePair.of('UnderlayColor', 'underlaycolor'),
      NamePair.of('UnderlayOffset', 'underlayoffset'),
      NamePair.of('UnderlaySoftness', 'underlaysoftness'),
      NamePair.of('bGLOW', 'glowenabled'),
      NamePair.of('GlowColor', 'glowcolor'),
      NamePair.of('GlowOffset', 'glowoffset'),
      NamePair.of('GlowInner', 'glowinner'),
      NamePair.of('GlowOuter', 'glowouter'),
      NamePair.of('GlowPower', 'glowpower'),
      NamePair.of('enableVertexGradient', 'vertexgradientenabled'),
      NamePair.of('topLeft', 'gradienttopleft'),
      NamePair.of('topRight', 'gradienttopright'),
      NamePair.of('bottomLeft', 'gradientbottomleft'),
      NamePair.of('bottomRight', 'gradientbottomright'),
      NamePair.of('bBEVEL', 'bevelenabled'),
      NamePair.of('SpecularColor', 'specularcolor'),
      NamePair.of('LightAngle', 'lightangle'),
      NamePair.of('SpecularPower', 'specularpower'),
      NamePair.of('Reflectivity', 'reflectivity'),
      NamePair.of('Diffuse', 'diffuse'),
      NamePair.of('Ambient', 'ambient'),
      NamePair.of('Bevel', 'bevel'),
      NamePair.of('BevelOffset', 'beveloffset'),
      NamePair.of('BevelWidth', 'bevelwidth'),
      NamePair.of('BevelRoundness', 'bevelroundness'),
      NamePair.of('BevelClamp', 'bevelclamp'),
    ]
  }
}
