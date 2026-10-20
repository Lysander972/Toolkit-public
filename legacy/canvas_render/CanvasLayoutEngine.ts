import { RichTextParser, CanvasRenderEntry, GlobalCanvasRuntime } from './CanvasRuntimeUtils.ts'
import { CanvasLayoutDiagnostics } from './CanvasRenderLayout.ts'
import { CanvasRenderPipeline } from './CanvasRenderPipeline.ts'
import { RenderSession, type RenderStatus } from './CanvasRenderState.ts'

/** @anchor canvas-render-single-entry */
export class CanvasLayoutEngine {
  public static readonly initialRender: Promise<void> = CanvasLayoutEngine.render()

  private constructor() {}

  /** @anchor canvas-render-single-entry */
  public static render(): Promise<void> {
    const session: RenderSession = RenderSession.getInstance()
    return session.start(async (): Promise<void> => {
      try {
        GlobalCanvasRuntime.initialize()
        await RichTextParser.loadRequiredFonts()
        CanvasRenderPipeline.renderCanvasFromDom(session)
      } catch (err) {
        CanvasRenderEntry.renderCanvasError(err)
        CanvasRenderEntry.reportCanvasPreviewError('render', err, 'error')
        throw err
      }
    })
  }

  /** @anchor canvas-render-single-entry */
  public static getRenderStatus(): RenderStatus {
    return RenderSession.getInstance().getStatus()
  }

  /** @anchor canvas-render-layout-diagnostics */
  public static getRenderDiagnostics(): ReturnType<typeof CanvasLayoutDiagnostics.get> {
    return CanvasLayoutDiagnostics.get()
  }

  static {
    // 页面契约入口：模块求值时自挂载到 window，宿主仅观察完成状态。
    Object.defineProperty(window, 'CanvasLayoutEngine', {
      value: CanvasLayoutEngine,
      configurable: true,
    })
  }
}
