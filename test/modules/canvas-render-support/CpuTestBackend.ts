import { createRequire } from 'node:module'
import { z } from 'zod'
import CanvasKitInit from 'canvaskit-wasm'
import type { CanvasKit, CanvasKitInitOptions } from 'canvaskit-wasm'

/**
 * @anchor canvas-render-test-baseline
 * @anchor canvas-render-stage-baseline
 */
interface CanvasKitInitializer {
  (options: CanvasKitInitOptions): Promise<CanvasKit>
}

/**
 * @anchor canvas-render-test-baseline
 * @anchor canvas-render-stage-baseline
 */
export class CpuTestBackend {
  private constructor() {}

  /**
   * @anchor canvas-render-test-baseline
   * @anchor canvas-render-stage-baseline
   */
  public static initialize(): Promise<CanvasKit> {
    const require: NodeJS.Require = createRequire(import.meta.url)
    const initializerSchema: z.ZodType<CanvasKitInitializer> = z.custom<CanvasKitInitializer>(
      (value: unknown): boolean => typeof value === 'function',
    )
    const initialize: CanvasKitInitializer = initializerSchema.parse(CanvasKitInit)
    return initialize({
      locateFile: (): string => require.resolve('canvaskit-wasm/bin/canvaskit.wasm'),
    })
  }
}
