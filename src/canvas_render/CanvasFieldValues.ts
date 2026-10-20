import type { UIComponentType } from './CanvasTypesBase.ts'
import type { CanvasUISchemaOf } from './CanvasViewSchema.ts'

/** @anchor canvas-render-effective-values */
export class CanvasFieldValues {
  private constructor() {}

  /**
   * @anchor canvas-render-effective-values
   * @anchor canvas-render-whitelist-merge
   */
  public static resolve<Type extends UIComponentType>(
    _input: CanvasUISchemaOf<Type>,
    _defaults: CanvasUISchemaOf<Type>,
    _enabled: ReadonlySet<keyof CanvasUISchemaOf<Type>>,
  ): CanvasUISchemaOf<Type> {
    throw new Error('TODO: 按特性启用白名单生成独立 effective 字段值')
  }
}
