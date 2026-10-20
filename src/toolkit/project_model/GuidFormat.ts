import { z } from 'zod'

/**
 * @anchor entity-guid-format
 * @format-exempt guid-scalar-format GUID 是外部固定长度十六进制标量，使用 Zod 校验格式。
 */
export class GuidFormat {
  private static readonly schema: z.ZodType<string> = z.string().regex(/^[0-9a-f]{32}$/u)

  private constructor() {}

  /**
   * @anchor entity-guid-format
   */
  public static validate(this: void, value: string): boolean {
    return GuidFormat.schema.safeParse(value).success
  }
}
