import { Option } from 'effect'

/** @anchor canvas-var */
export abstract class Var {
  private readonly barrier: number

  /** @anchor canvas-var */
  protected constructor(barrier: number) {
    this.barrier = barrier
  }

  /** @anchor canvas-var */
  public typeCode(): number {
    return this.barrier
  }
}

/** @anchor canvas-var */
export class BoolVar extends Var {
  private constructor(public readonly value: boolean) {
    super(5)
  }

  /** @anchor canvas-var */
  public static of(value: boolean): BoolVar {
    return new BoolVar(value)
  }
}

/** @anchor canvas-var */
export class FloatVar extends Var {
  private constructor(public readonly value: number) {
    super(1)
  }

  /** @anchor canvas-var */
  public static of(value: number): FloatVar {
    const rounded: number = Math.fround(value)
    if (!Number.isFinite(rounded)) throw new Error('浮点必须是有限值')
    return new FloatVar(rounded)
  }
}

/** @anchor canvas-var */
export class LongVar extends Var {
  private constructor(public readonly value: bigint) {
    super(24)
  }

  /** @anchor canvas-var */
  public static of(value: bigint): LongVar {
    return new LongVar(value)
  }
}

/** @anchor canvas-var */
export class StringVar extends Var {
  private constructor(public readonly value: string) {
    super(6)
  }

  /** @anchor canvas-var */
  public static of(value: string): StringVar {
    return new StringVar(value)
  }
}

/** @anchor canvas-var */
export class Vector3Var extends Var {
  private constructor(
    public readonly x: number,
    public readonly y: number,
    public readonly z: number,
  ) {
    super(4)
  }

  /** @anchor canvas-var */
  public static of(x: number, y: number, z: number): Vector3Var {
    const rounded: [number, number, number] = [Math.fround(x), Math.fround(y), Math.fround(z)]
    if (rounded.some((value: number): boolean => !Number.isFinite(value))) {
      throw new Error('向量分量必须是有限值')
    }
    return new Vector3Var(rounded[0], rounded[1], rounded[2])
  }
}

/** @anchor canvas-var */
export class ColorVar extends Var {
  private constructor(
    public readonly red: number,
    public readonly green: number,
    public readonly blue: number,
    public readonly alpha: number,
    public readonly palette: Option.Option<string>,
  ) {
    super(12)
  }

  /** @anchor canvas-var */
  public static rgba(red: number, green: number, blue: number, alpha: number): ColorVar {
    const rounded: [number, number, number, number] = [
      Math.fround(red),
      Math.fround(green),
      Math.fround(blue),
      Math.fround(alpha),
    ]
    if (
      rounded.some((value: number): boolean => !Number.isFinite(value)) ||
      rounded.some((value: number): boolean => value < 0 || value > 1)
    ) {
      throw new Error('颜色通道必须是 0..1 的有限值')
    }
    return new ColorVar(rounded[0], rounded[1], rounded[2], rounded[3], Option.none())
  }

  /** @anchor canvas-var */
  public static named(palette: string): ColorVar {
    return new ColorVar(
      Math.fround(1),
      Math.fround(1),
      Math.fround(1),
      Math.fround(1),
      Option.some(palette),
    )
  }
}

/** @anchor canvas-var */
export class ListVar extends Var {
  private constructor(public readonly items: readonly Var[]) {
    super(16)
  }

  /** @anchor canvas-var */
  public static of(items: readonly Var[]): ListVar {
    return new ListVar(items)
  }
}

/** @anchor canvas-var */
export class DictEntry {
  private constructor(
    public readonly key: string,
    public readonly value: Var,
  ) {}

  /** @anchor canvas-var */
  public static of(key: string, value: Var): DictEntry {
    return new DictEntry(key, value)
  }
}

/** @anchor canvas-var */
export class DictVar extends Var {
  private constructor(public readonly entries: readonly DictEntry[]) {
    super(17)
  }

  /** @anchor canvas-var */
  public static of(entries: readonly DictEntry[]): DictVar {
    return new DictVar(entries)
  }

  /** @anchor canvas-var */
  public find(key: string): Option.Option<Var> {
    return Option.map(
      Option.fromNullishOr(this.entries.find((entry: DictEntry): boolean => entry.key === key)),
      (entry: DictEntry): Var => entry.value,
    )
  }
}

/** @anchor canvas-node */
export class CanvasNode {
  private readonly fields: Map<string, Var>
  private readonly fieldOrder: string[]
  private readonly childNodes: CanvasNode[]

  private constructor(public readonly typeName: string) {
    this.fields = new Map()
    this.fieldOrder = []
    this.childNodes = []
  }

  /** @anchor canvas-node */
  public static create(typeName: string): CanvasNode {
    return new CanvasNode(typeName)
  }

  /** @anchor canvas-node */
  public setField(name: string, value: Var): void {
    if (!this.fields.has(name)) this.fieldOrder.push(name)
    this.fields.set(name, value)
  }

  /** @anchor canvas-node */
  public field(name: string): Option.Option<Var> {
    return Option.fromNullishOr(this.fields.get(name))
  }

  /** @anchor canvas-node */
  public orderedFields(): readonly string[] {
    return this.fieldOrder
  }

  /** @anchor canvas-node */
  public children(): readonly CanvasNode[] {
    return this.childNodes
  }

  /**
   * @anchor canvas-node
   * @passthrough-exempt 子节点保存在本对象的 childNodes
   */
  public addChild(child: CanvasNode): void {
    this.childNodes.push(child)
  }
}

/** @anchor canvas-node */
export class CanvasDocument {
  private constructor(
    public readonly name: string,
    public readonly root: CanvasNode,
  ) {}

  /** @anchor canvas-node */
  public static of(name: string, root: CanvasNode): CanvasDocument {
    return new CanvasDocument(name, root)
  }
}
