import { resolve } from 'node:path'
import { CanvasAssets } from './CanvasAssets.ts'

/** @anchor preview-assets */
export class PreviewAssetBuild {
  private constructor() {}

  static {
    // @non-manual-path 用户确认的公开 Canvas 场景工程，不是治理扫描范围。
    void new CanvasAssets(resolve('test/data/projects/水果切切切')).build()
  }
}
