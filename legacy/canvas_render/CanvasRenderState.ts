import { Option } from 'effect'
import { Assets, type Texture } from 'pixi.js'

/** @anchor canvas-render-async-state */
export interface RenderStatus {
  readonly status: 'pending' | 'complete' | 'error'
  readonly pending: number
  readonly hasError: boolean
}

/** @anchor canvas-render-single-entry */
export class RenderSession {
  private static readonly INSTANCE: RenderSession = new RenderSession()
  private readonly tasks: Set<Promise<void>> = new Set<Promise<void>>()
  private readonly textureUrls: Set<string> = new Set<string>()
  private readonly textureLoads: Set<Promise<Texture>> = new Set<Promise<Texture>>()
  private started: boolean = false
  private dispatchComplete: boolean = false
  private completed: boolean = false
  private error: Option.Option<unknown> = Option.none()

  /** @anchor canvas-render-single-entry */
  public static getInstance(): RenderSession {
    return RenderSession.INSTANCE
  }

  /** @anchor canvas-render-single-entry */
  public start(work: () => void | Promise<void>): Promise<void> {
    if (this.started) throw new Error('Canvas 渲染会话只能启动一次')
    this.started = true
    document.documentElement.dataset.renderStatus = 'pending'
    return this.run(work)
  }

  /** @anchor canvas-render-async-state @anchor canvas-render-resource-settlement */
  public track(work: () => Promise<void>): void {
    if (!this.started || this.dispatchComplete) throw new Error('资源任务登记时机无效')
    const tracked: Promise<void> = Promise.resolve()
      .then(work)
      .then(
        (): void => {},
        (error: unknown): void => {
          if (Option.isNone(this.error)) this.error = Option.some(error)
        },
      )
      .finally((): void => {
        this.tasks.delete(tracked)
      })
    this.tasks.add(tracked)
  }

  /** @anchor canvas-render-async-state */
  public loadTexture(url: string): Promise<Texture> {
    this.textureUrls.add(url)
    const loading: Promise<Texture> = Assets.load<Texture>(url)
    this.textureLoads.add(loading)
    return loading
  }

  /** @anchor canvas-render-async-state */
  private async run(work: () => void | Promise<void>): Promise<void> {
    try {
      await work()
    } catch (error) {
      if (Option.isNone(this.error)) this.error = Option.some(error)
    }
    this.dispatchComplete = true
    await Promise.all(this.tasks)
    await Promise.allSettled(this.textureLoads)
    try {
      if (this.textureUrls.size > 0) await Assets.unload([...this.textureUrls])
    } catch (error) {
      this.error = Option.some(
        Option.isSome(this.error)
          ? new AggregateError([this.error.value, error], 'Canvas 渲染与资源释放失败')
          : error,
      )
    } finally {
      this.textureUrls.clear()
      this.textureLoads.clear()
    }
    if (Option.isSome(this.error)) {
      document.documentElement.dataset.renderStatus = 'error'
      throw this.error.value
    }
    this.completed = true
    document.documentElement.dataset.renderStatus = 'complete'
  }

  /** @anchor canvas-render-async-state */
  public getStatus(): RenderStatus {
    return {
      status: Option.isSome(this.error) ? 'error' : this.completed ? 'complete' : 'pending',
      pending: this.tasks.size,
      hasError: Option.isSome(this.error),
    }
  }
}
