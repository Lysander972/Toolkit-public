import { z } from 'zod'
import { HjsonDocument } from './HjsonDocument.ts'

/**
 * @anchor logical-carrier-index
 * @format-exempt hjson-object-boundary
 */
interface HjsonFields {
  [key: string]: unknown
}

/** @anchor logical-carrier-index */
export interface CarrierDeviceEntry {
  readonly deviceId: number
  readonly deviceType: number
}

/** @anchor logical-carrier-index */
export class CarrierIndex {
  private static readonly object: z.ZodType<HjsonFields> = z.custom<HjsonFields>(
    (input: unknown): boolean => z.record(z.string(), z.unknown()).safeParse(input).success,
  )
  private constructor(private readonly document: HjsonDocument) {}

  /** @anchor logical-carrier-index */
  public static read(source: string): CarrierIndex {
    const index: CarrierIndex = new CarrierIndex(HjsonDocument.read(source))
    index.entries()
    return index
  }

  /** @anchor logical-carrier-index */
  public entries(): readonly CarrierDeviceEntry[] {
    const entries: CarrierDeviceEntry[] = this.collect(
      CarrierIndex.object.parse(this.document.fields),
    )
    const ids: Set<number> = new Set()
    for (const entry of entries) {
      if (ids.has(entry.deviceId)) throw new Error(`零件索引重复: ${String(entry.deviceId)}`)
      ids.add(entry.deviceId)
    }
    return entries
  }

  /** @anchor logical-carrier-index */
  public add(entry: CarrierDeviceEntry): void {
    z.number().int().nonnegative().parse(entry.deviceId)
    z.number().int().nonnegative().parse(entry.deviceType)
    if (
      this.entries().some(
        (current: CarrierDeviceEntry): boolean => current.deviceId === entry.deviceId,
      )
    )
      throw new Error(`零件索引已存在: ${String(entry.deviceId)}`)
    const list: unknown[] = z
      .custom<unknown[]>((input: unknown): boolean => Array.isArray(input))
      .parse(this.document.fields['list'])
    list.push(`${String(entry.deviceId)}-${String(entry.deviceType)}`)
  }

  /** @anchor logical-carrier-index */
  public remove(deviceId: number): void {
    if (!this.entries().some((entry: CarrierDeviceEntry): boolean => entry.deviceId === deviceId))
      throw new Error(`零件索引不存在: ${String(deviceId)}`)
    this.removeFrom(CarrierIndex.object.parse(this.document.fields), deviceId)
  }

  /**
   * @anchor logical-carrier-index
   * @passthrough-exempt 交由 HjsonDocument 统一保留注释、顺序和未知字段，索引只负责结构语义。
   */
  public text(): string {
    return this.document.text()
  }

  private collect(record: HjsonFields): CarrierDeviceEntry[] {
    return z
      .array(z.unknown())
      .parse(record['list'])
      .flatMap((item: unknown): CarrierDeviceEntry[] => {
        if (typeof item === 'string') return [CarrierIndex.entry(item)]
        return this.collect(CarrierIndex.object.parse(item))
      })
  }

  private removeFrom(record: HjsonFields, deviceId: number): void {
    const list: unknown[] = z
      .custom<unknown[]>((input: unknown): boolean => Array.isArray(input))
      .parse(record['list'])
    for (let index: number = list.length - 1; index >= 0; index -= 1) {
      const item: unknown = list[index]
      if (typeof item === 'string') {
        if (CarrierIndex.entry(item).deviceId === deviceId) list.splice(index, 1)
      } else this.removeFrom(CarrierIndex.object.parse(item), deviceId)
    }
  }

  /** @anchor logical-carrier-index @format-exempt carrier-index-scalar */
  private static entry(source: string): CarrierDeviceEntry {
    const parts: readonly [string, string] = z
      .tuple([z.string().regex(/^\d+$/u), z.string().regex(/^\d+$/u)])
      .parse(source.split('-'))
    return {
      deviceId: z.number().int().nonnegative().parse(Number(parts[0])),
      deviceType: z.number().int().nonnegative().parse(Number(parts[1])),
    }
  }
}
