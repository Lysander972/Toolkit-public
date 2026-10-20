/** @anchor canvas-schema */
export type FieldKind = 'bool' | 'float' | 'long' | 'string' | 'vector' | 'color' | 'list'

/** @anchor canvas-schema */
export class CanvasSchema {
  private constructor() {}

  /** @anchor canvas-schema */
  public static supported(typeName: string): boolean {
    return CanvasSchema.types().includes(typeName)
  }

  /** @anchor canvas-schema */
  public static accepts(typeName: string, fieldName: string, typeCode: number): boolean {
    if (fieldName === 'type' || fieldName === 'sons') return false
    if (fieldName === 'name') return typeCode === 6
    const specific: boolean = CanvasSchema.specific(typeName).includes(fieldName)
    const common: boolean = CanvasSchema.common().includes(fieldName)
    if (!specific && !common) return false
    return CanvasSchema.codes(fieldName).includes(typeCode)
  }

  /** @anchor canvas-schema */
  public static kind(fieldName: string): FieldKind {
    const codes: readonly number[] = CanvasSchema.codes(fieldName)
    if (codes.includes(12)) return 'color'
    if (codes.includes(16)) return 'list'
    if (codes.includes(4)) return 'vector'
    if (codes.includes(5)) return 'bool'
    if (codes.includes(1)) return 'float'
    if (codes.includes(24)) return 'long'
    return 'string'
  }

  private static types(): readonly string[] {
    return [
      'Canvas',
      'UIRect',
      'UIButton',
      'UILabel',
      'UIInput',
      'UIRawImage',
      'UISpine',
      'TextPro',
      'ScrollRect',
    ]
  }

  private static common(): readonly string[] {
    return [
      'guid',
      'Pos',
      'pos',
      'parentAnchor',
      'anchor',
      'widthExtend',
      'heightExtend',
      'size',
      'rot',
      'color',
      'shadowColor',
      'shadewDistance',
      'outlineColor',
      'outlineDistance',
    ]
  }

  private static specific(typeName: string): readonly string[] {
    switch (typeName) {
      case 'UIRect':
        return CanvasSchema.rect()
      case 'UIButton':
        return [
          ...CanvasSchema.visual(),
          ...CanvasSchema.slice(),
          ...CanvasSchema.buttonText(),
          'clickCBName',
          'pressCBName',
          'releaseCBName',
        ]
      case 'UILabel':
        return [...CanvasSchema.text(), 'outlineWidth', 'boolBestFit']
      case 'TextPro':
        return [...CanvasSchema.text(), 'outlineWidth', 'boolBestFit', ...CanvasSchema.effects()]
      case 'UIInput':
        return ['text', 'FontSize', 'Alignment', 'multiLine', 'contentType', 'labelColor']
      case 'UIRawImage':
        return ['fileGuid', 'softMaskGuid']
      case 'UISpine':
        return ['atlasGuid', 'skelGuid', 'jsonGuid', 'pngGuids', 'animIndex']
      case 'ScrollRect':
        return [
          ...CanvasSchema.visual(),
          'mask',
          ...CanvasSchema.slice(),
          ...CanvasSchema.layout(),
          'scrollHorizontal',
          'scrollVertical',
        ]
      default:
        return []
    }
  }

  private static rect(): readonly string[] {
    return [
      ...CanvasSchema.visual(),
      'mask',
      'fillMethod',
      'fillAmount',
      ...CanvasSchema.slice(),
      ...CanvasSchema.layout(),
    ]
  }

  private static visual(): readonly string[] {
    return ['BorderWidth', 'Radius']
  }

  private static slice(): readonly string[] {
    return ['slicedPicGuid', 'slicedBorderLB', 'slicedBorderRT', 'slicedRectLB', 'slicedRectRT']
  }

  private static layout(): readonly string[] {
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

  private static buttonText(): readonly string[] {
    return [
      'label',
      'FontSize',
      'Alignment',
      'lineSpacing',
      'lightFont',
      'characterSpacing',
      'fontStyle',
    ]
  }

  private static effects(): readonly string[] {
    return [
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
      'topLeft',
      'topRight',
      'bottomLeft',
      'bottomRight',
      'FaceDilate',
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
    ]
  }

  private static codes(fieldName: string): readonly number[] {
    if (
      fieldName === 'color' ||
      fieldName.endsWith('Color') ||
      CanvasSchema.colorFields().includes(fieldName)
    ) {
      return [12, 6]
    }
    if (fieldName === 'pngGuids') return [16]
    if (CanvasSchema.floatFields().includes(fieldName)) return [1]
    if (CanvasSchema.vectorFields().includes(fieldName)) return [4]
    if (CanvasSchema.boolFields().includes(fieldName)) return [5]
    if (CanvasSchema.longFields().includes(fieldName)) return [24]
    return [6]
  }

  private static colorFields(): readonly string[] {
    return ['topLeft', 'topRight', 'bottomLeft', 'bottomRight', 'labelColor']
  }

  private static floatFields(): readonly string[] {
    return [
      'rot',
      'BorderWidth',
      'Radius',
      'fillAmount',
      'FontSize',
      'lineSpacing',
      'characterSpacing',
      'outlineWidth',
      'verticle_spacing',
      'horizontal_spacing',
      'FaceDilate',
      'UnderlaySoftness',
      'GlowOffset',
      'GlowInner',
      'GlowOuter',
      'GlowPower',
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
    ]
  }

  private static vectorFields(): readonly string[] {
    return [
      'Pos',
      'pos',
      'size',
      'shadewDistance',
      'outlineDistance',
      'cellSize',
      'spacing',
      'padding',
      'slicedBorderLB',
      'slicedBorderRT',
      'slicedRectLB',
      'slicedRectRT',
      'UnderlayOffset',
    ]
  }

  private static boolFields(): readonly string[] {
    return [
      'mask',
      'lightFont',
      'childExpendWidth',
      'childExpendHeight',
      'boolBestFit',
      'multiLine',
      'scrollHorizontal',
      'scrollVertical',
      'bUNDERLAY',
      'bGLOW',
      'enableVertexGradient',
      'bBEVEL',
    ]
  }

  private static longFields(): readonly string[] {
    return [
      'guid',
      'parentAnchor',
      'anchor',
      'widthExtend',
      'heightExtend',
      'fillMethod',
      'Alignment',
      'fontStyle',
      'childAlignment',
      'contentType',
      'animIndex',
    ]
  }
}
