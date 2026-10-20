import { Option } from 'effect'
import type { CanvasRuntimeNode } from './CanvasTypesBase.ts'

/** @anchor canvas-render-dom-pipeline */
export class RenderDomCommon {
  /** @anchor canvas-render-dom-pipeline */
  public static appendLogicalPath(parentLogicalPath: string, nodeName: string): string {
    if (nodeName.length === 0) {
      return parentLogicalPath
    }
    return parentLogicalPath.length > 0 ? `${parentLogicalPath}/${nodeName}` : nodeName
  }
}

/** @anchor canvas-render-renderer-boundaries */
export abstract class FeatureRenderBase<Schema> {
  /** @anchor canvas-render-renderer-boundaries */
  public abstract render(input: Schema, defaultvalue: Schema, runtime: CanvasRuntimeNode): void
}

/**
 * 按显式启用规则选取输入或节点默认值，不修改输入与默认值。
 * @anchor canvas-render-whitelist-merge
 */
export class FeatureValues {
  /** @anchor canvas-render-effective-values */
  public static resolve<T>(
    input: Option.Option<T>,
    defaultvalue: Option.Option<T>,
    enabled: boolean,
    hint: string,
  ): Option.Option<T> {
    if (!enabled) return Option.none()
    const effective: Option.Option<T> = Option.isSome(input) ? input : defaultvalue
    if (Option.isNone(effective)) throw new Error(`${hint} 缺失必要字段`)
    return effective
  }
}
