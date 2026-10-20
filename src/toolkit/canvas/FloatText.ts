import { Array as EffectArray } from 'effect'
import { z } from 'zod'

/** @anchor canvas-float */
export class FloatText {
  private constructor() {}

  /** @anchor canvas-float */
  public static bits(value: number): number {
    const view: DataView = FloatText.view32()
    view.setFloat32(0, value, true)
    return view.getUint32(0, true)
  }

  /** @anchor canvas-float */
  public static fromBits(bits: number): number {
    const view: DataView = FloatText.view32()
    view.setUint32(0, bits, true)
    return view.getFloat32(0, true)
  }

  /**
   * float32 的最短十进制。解析回 float32 后按位相等。
   * @anchor canvas-float
   */
  public static format(value: number): string {
    const rounded: number = Math.fround(value)
    if (!Number.isFinite(rounded)) throw new Error('浮点必须是有限值')
    const target: number = FloatText.bits(rounded)
    if (Object.is(rounded, -0)) return '-0'
    if (rounded === 0) return '0'
    const integer: string = FloatText.integer(rounded, target)
    if (integer.length > 0) return integer
    let digits: number = 1
    while (digits <= 9) {
      const text: string = FloatText.general(rounded, digits)
      if (FloatText.bits(Math.fround(Number(text))) === target) return text
      digits += 1
    }
    return FloatText.general(rounded, 9)
  }

  /**
   * 颜色通道 0..1 写成 0..255。整数能按位还原时用整数，否则用截断后的全精度小数。
   * @anchor canvas-float
   */
  public static formatChannel(channel: number): string {
    const rounded: number = Math.fround(channel)
    if (!Number.isFinite(rounded)) throw new Error('颜色通道必须是有限值')
    const target: number = FloatText.bits(rounded)
    if (Object.is(rounded, -0)) return '-0'
    const nearest: number = Math.round(rounded * 255)
    if (nearest < 0 || nearest > 255) throw new Error('颜色通道超出 0..255')
    if (FloatText.bits(Math.fround(nearest / 255)) === target) return String(nearest)
    const truncated: number = FloatText.truncateMantissa(rounded * 255, 24)
    let digits: number = 1
    while (digits <= 17) {
      const text: string = FloatText.general(truncated, digits)
      const parsed: number = Number(text)
      if (FloatText.bits(Math.fround(parsed / 255)) === target) return text
      digits += 1
    }
    return FloatText.general(truncated, 17)
  }

  /** @anchor canvas-float */
  public static parse(text: string, fieldName: string): number {
    const rounded: number = Math.fround(FloatText.parseDecimal(text, fieldName))
    if (!Number.isFinite(rounded))
      throw new Error(`字段 ${fieldName} 超出有限 float32 范围: ${text}`)
    return rounded
  }

  /** @anchor canvas-view-precision-readability */
  public static parseDecimal(text: string, fieldName: string): number {
    if (
      text.length === 0 ||
      // eslint-disable-next-line no-restricted-properties -- 校验用户输入不含首尾空白并整体拒绝，非格式转换。
      text !== text.trim()
    )
      throw new Error(`字段 ${fieldName} 需要数值`)
    let parsed: number
    try {
      parsed = z.number().parse(JSON.parse(text))
    } catch (cause: unknown) {
      throw new Error(`字段 ${fieldName} 必须为十进制数值: ${text}`, { cause })
    }
    return parsed
  }

  /** @anchor canvas-float */
  public static truncateMantissa(value: number, keepFractionBits: number): number {
    if (!Number.isFinite(value) || value === 0) return value
    if (keepFractionBits < 0 || keepFractionBits > 52) {
      throw new Error('尾数保留位数必须在 0..52')
    }
    const view: DataView = new DataView(new ArrayBuffer(8))
    view.setFloat64(0, value, true)
    const bits: bigint = view.getBigUint64(0, true)
    const signAndExponent: bigint = bits & 0xfff0_0000_0000_0000n
    const mantissa: bigint = bits & 0x000f_ffff_ffff_ffffn
    const drop: bigint = BigInt(52 - keepFractionBits)
    const truncated: bigint = signAndExponent | (mantissa & ~((1n << drop) - 1n))
    view.setBigUint64(0, truncated, true)
    return view.getFloat64(0, true)
  }

  private static integer(value: number, target: number): string {
    if (!Number.isInteger(value) || Math.abs(value) >= 1e7) return ''
    const text: string = String(value)
    return FloatText.bits(Math.fround(Number(text))) === target ? text : ''
  }

  private static general(value: number, digits: number): string {
    const precision: string = value.toPrecision(digits)
    if (!precision.includes('e')) return FloatText.trimZeros(precision)
    const parts: readonly string[] = precision.split('e')
    const coefficient: string = EffectArray.getUnsafe(parts, 0)
    const exponent: number = Number(EffectArray.getUnsafe(parts, 1))
    const sign: string = coefficient.startsWith('-') ? '-' : ''
    const digitsOnly: string = coefficient.replace('-', '').replace('.', '')
    if (exponent >= 0 && exponent < digitsOnly.length) {
      const whole: string = digitsOnly.slice(0, exponent + 1)
      const fraction: string = digitsOnly.slice(exponent + 1)
      const body: string = fraction.length === 0 ? whole : `${whole}.${fraction}`
      return FloatText.trimZeros(sign + body)
    }
    if (exponent < 0 && exponent > -7) {
      const zeros: string = '0'.repeat(-exponent - 1)
      return FloatText.trimZeros(`${sign}0.${zeros}${digitsOnly}`)
    }
    return precision
  }

  private static trimZeros(text: string): string {
    if (!text.includes('.')) return text
    let end: number = text.length
    while (end > 0 && text[end - 1] === '0') end -= 1
    if (end > 0 && text[end - 1] === '.') end -= 1
    return end === text.length ? text : text.slice(0, end)
  }

  private static view32(): DataView {
    return new DataView(new ArrayBuffer(4))
  }
}
