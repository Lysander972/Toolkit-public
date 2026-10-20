import { Option } from 'effect'

/** @anchor canvas-enums */
class EnumPair {
  private constructor(
    public readonly name: string,
    public readonly value: bigint,
  ) {}

  /** @anchor canvas-enums */
  public static of(name: string, value: bigint): EnumPair {
    return new EnumPair(name, value)
  }
}

/** @anchor canvas-enums */
export class CanvasEnums {
  private constructor() {}

  /** @anchor canvas-enums */
  public static isEnum(fieldName: string): boolean {
    return CanvasEnums.table(fieldName).length > 0
  }

  /** @anchor canvas-enums */
  public static toName(fieldName: string, value: bigint): Option.Option<string> {
    return Option.map(
      Option.fromNullishOr(
        CanvasEnums.table(fieldName).find((pair: EnumPair): boolean => pair.value === value),
      ),
      (pair: EnumPair): string => pair.name,
    )
  }

  /** @anchor canvas-enums */
  public static toValue(fieldName: string, name: string): Option.Option<bigint> {
    return Option.map(
      Option.fromNullishOr(
        CanvasEnums.table(fieldName).find((pair: EnumPair): boolean => pair.name === name),
      ),
      (pair: EnumPair): bigint => pair.value,
    )
  }

  private static table(fieldName: string): readonly EnumPair[] {
    if (
      fieldName === 'parentAnchor' ||
      fieldName === 'anchor' ||
      fieldName === 'childAlignment' ||
      fieldName === 'Alignment'
    ) {
      return CanvasEnums.textAnchor()
    }
    if (fieldName === 'widthExtend' || fieldName === 'heightExtend') return CanvasEnums.extent()
    if (fieldName === 'fillMethod') return CanvasEnums.fill()
    if (fieldName === 'contentType') return CanvasEnums.content()
    return []
  }

  private static textAnchor(): readonly EnumPair[] {
    // @non-environment-variable TextAnchor 枚举名是 Canvas 协议常量，不是环境变量
    return [
      EnumPair.of('UPPER_LEFT', 0n),
      EnumPair.of('UPPER_CENTER', 1n),
      EnumPair.of('UPPER_RIGHT', 2n),
      EnumPair.of('MIDDLE_LEFT', 3n),
      EnumPair.of('MIDDLE_CENTER', 4n),
      EnumPair.of('MIDDLE_RIGHT', 5n),
      EnumPair.of('LOWER_LEFT', 6n),
      EnumPair.of('LOWER_CENTER', 7n),
      EnumPair.of('LOWER_RIGHT', 8n),
    ]
  }

  private static extent(): readonly EnumPair[] {
    // @non-environment-variable Extent 枚举名是 Canvas 协议常量，不是环境变量
    return [
      EnumPair.of('ABSOLUTE', 0n),
      EnumPair.of('PERCENT', 1n),
      EnumPair.of('TO_EDGE', 2n),
      EnumPair.of('ASPECT', 3n),
    ]
  }

  private static fill(): readonly EnumPair[] {
    // @non-environment-variable FillMethod 枚举名是 Canvas 协议常量，不是环境变量
    return [
      EnumPair.of('NONE', -1n),
      EnumPair.of('HORIZONTAL', 0n),
      EnumPair.of('VERTICAL', 1n),
      EnumPair.of('RADIAL_90', 2n),
      EnumPair.of('RADIAL_180', 3n),
      EnumPair.of('RADIAL_360', 4n),
    ]
  }

  private static content(): readonly EnumPair[] {
    // @non-environment-variable ContentType 枚举名是 Canvas 协议常量，不是环境变量
    return [
      EnumPair.of('STANDARD', 0n),
      EnumPair.of('INTEGER_NUMBER', 1n),
      EnumPair.of('DECIMAL_NUMBER', 2n),
      EnumPair.of('PASSWORD', 3n),
    ]
  }
}
