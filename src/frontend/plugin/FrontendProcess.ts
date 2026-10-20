import { Context, Effect, Exit, Fiber, Layer, ManagedRuntime, Scope } from 'effect'
import type { Host, WorkspaceStatusRequest } from '@toolkit/host'
import type { WorkspaceStatus } from '@toolkit/host/workspace'

interface FrontendComponentState {
  readonly host: Host
  readonly processStartedAt: number
}

/**
 * 前端请求级空壳：按调用隔离实例，作用域结束时关闭。
 * @anchor frontend-component-layer
 */
export class FrontendRequestContext {
  private active: boolean = true

  private constructor() {}

  /** @anchor frontend-component-layer */
  public static make(): Effect.Effect<FrontendRequestContext, never, Scope.Scope> {
    return Effect.acquireRelease(
      Effect.sync((): FrontendRequestContext => new FrontendRequestContext()),
      (request: FrontendRequestContext): Effect.Effect<void> =>
        Effect.sync((): void => {
          request.release()
        }),
    )
  }

  /** @anchor frontend-component-layer */
  public isOpen(): boolean {
    return this.active
  }

  private release(): void {
    this.active = false
  }
}

/**
 * 前端请求级服务身份。
 * @anchor frontend-component-layer
 */
class FrontendRequestService extends Context.Service<
  FrontendRequestService,
  FrontendRequestContext
>()('toolkit/FrontendRequest') {
  private constructor(service: never) {
    super(service)
  }

  /** @anchor frontend-component-layer */
  public static layer(): Layer.Layer<FrontendRequestService> {
    return Layer.effect(FrontendRequestService, FrontendRequestContext.make())
  }
}

/**
 * 组件级服务身份：持有 Host 整体，不继承 Host 容器。
 * @anchor frontend-component-layer
 */
class FrontendComponentService extends Context.Service<
  FrontendComponentService,
  FrontendComponentState
>()('toolkit/FrontendComponent') {
  private constructor(service: never) {
    super(service)
  }

  /** @anchor frontend-component-layer */
  public static layer(host: Host, processStartedAt: number): Layer.Layer<FrontendComponentService> {
    return Layer.succeed(FrontendComponentService, { host, processStartedAt })
  }
}

/**
 * 进程级(前端)服务身份。
 * @anchor frontend-process-layer
 */
class FrontendProcessService extends Context.Service<
  FrontendProcessService,
  FrontendProcessModule
>()('toolkit/FrontendProcess') {
  private constructor(service: never) {
    super(service)
  }

  /** @anchor frontend-process-layer */
  public static layer(): Layer.Layer<FrontendProcessService> {
    return Layer.effect(FrontendProcessService, FrontendProcessModule.make())
  }
}

/**
 * 进程级(前端)层载体：进程级运行时在插件进程生命周期内单例。
 * @anchor frontend-process-layer
 */
export class FrontendProcessModule {
  private static live: number = 0

  /** @anchor frontend-process-layer */
  private constructor(private readonly startedAt: number) {
    FrontendProcessModule.live += 1
  }

  /** @anchor frontend-process-layer */
  public static make(): Effect.Effect<FrontendProcessModule, never, Scope.Scope> {
    return Effect.acquireRelease(
      Effect.sync((): FrontendProcessModule => new FrontendProcessModule(Date.now())),
      (module: FrontendProcessModule): Effect.Effect<void> =>
        Effect.sync((): void => {
          module.release()
        }),
    )
  }

  /** @anchor frontend-process-layer */
  public static openCount(): number {
    return FrontendProcessModule.live
  }

  /** @anchor frontend-process-layer */
  public processStartedAt(): number {
    return this.startedAt
  }

  /** @anchor frontend-process-layer */
  private release(): void {
    FrontendProcessModule.live -= 1
  }
}

/**
 * 组件级容器：持有 Host 整体，生命周期挂在进程级 Scope 上。
 * @anchor frontend-component-layer
 */
export class FrontendComponent {
  private closed: boolean = false

  private constructor(
    private readonly runtime: ManagedRuntime.ManagedRuntime<FrontendComponentService, never>,
    private readonly owner: Scope.Closeable,
  ) {}

  /** @anchor frontend-component-layer */
  public static make(
    host: Host,
    startedAt: number,
    parent: Scope.Scope,
  ): Effect.Effect<FrontendComponent> {
    return Effect.flatMap(
      Scope.fork(parent),
      (owner: Scope.Closeable): Effect.Effect<FrontendComponent> => {
        const runtime: ManagedRuntime.ManagedRuntime<FrontendComponentService, never> =
          ManagedRuntime.make(FrontendComponentService.layer(host, startedAt))
        const component: FrontendComponent = new FrontendComponent(runtime, owner)
        return Effect.as(
          Scope.addFinalizer(
            owner,
            Effect.promise((): Promise<void> => component.dispose()),
          ),
          component,
        )
      },
    )
  }

  /** @anchor frontend-component-layer */
  public host(): Host {
    this.assertOpen()
    return this.state().host
  }

  /** @anchor frontend-component-layer */
  public processStartedAt(): number {
    this.assertOpen()
    return this.state().processStartedAt
  }

  /**
   * 前端请求级入口：按调用隔离请求作用域。AbortSignal 随组件或进程 Scope 关闭而中止。
   * @anchor frontend-component-layer
   */
  public async runInRequest(
    request: WorkspaceStatusRequest,
    signal: AbortSignal,
  ): Promise<WorkspaceStatus> {
    this.assertOpen()
    const effect: Effect.Effect<WorkspaceStatus, unknown, FrontendComponentService> =
      Effect.andThen(FrontendRequestService, FrontendComponentService).pipe(
        Effect.flatMap(
          (component: FrontendComponentState): Effect.Effect<WorkspaceStatus, unknown> =>
            Effect.tryPromise({
              try: (requestSignal: AbortSignal): Promise<WorkspaceStatus> =>
                component.host.runInRequest(request, requestSignal),
              catch: (cause: unknown): unknown => cause,
            }),
        ),
        Effect.provide(FrontendRequestService.layer()),
      )
    const scoped: Effect.Effect<WorkspaceStatus, unknown, FrontendComponentService> =
      Effect.flatMap(
        Effect.forkIn(effect, this.runtime.scope),
        (fiber: Fiber.Fiber<WorkspaceStatus, unknown>): Effect.Effect<WorkspaceStatus, unknown> =>
          Fiber.join(fiber).pipe(
            Effect.onInterrupt((): Effect.Effect<void> => Fiber.interrupt(fiber)),
          ),
      )
    return this.runtime.runPromise(scoped, { signal })
  }

  /**
   * @anchor frontend-component-layer
   * @passthrough-exempt 组件关闭入口，关闭组件 Scope 并触发运行时释放
   */
  public async close(): Promise<void> {
    await Effect.runPromise(Scope.close(this.owner, Exit.void))
  }

  private state(): FrontendComponentState {
    return this.runtime.runSync(FrontendComponentService)
  }

  /**
   * 释放组件运行时，使绑定在其 Scope 上的请求中断。
   * @anchor frontend-component-layer
   */
  private async dispose(): Promise<void> {
    if (this.closed) {
      return
    }
    this.closed = true
    await this.runtime.dispose()
  }

  private assertOpen(): void {
    if (this.closed) {
      throw new Error('前端组件已关闭')
    }
  }
}

/**
 * 前端进程级入口：持有进程级运行时，派生组件级容器。
 * @anchor frontend-process-layer
 */
export class FrontendProcess {
  private closed: boolean = false

  private constructor(
    private readonly runtime: ManagedRuntime.ManagedRuntime<FrontendProcessService, never>,
  ) {}

  /** @anchor frontend-process-layer */
  public static async open(): Promise<FrontendProcess> {
    const runtime: ManagedRuntime.ManagedRuntime<FrontendProcessService, never> =
      ManagedRuntime.make(FrontendProcessService.layer())
    try {
      await runtime.runPromise(FrontendProcessService)
      return new FrontendProcess(runtime)
    } catch (error: unknown) {
      await runtime.dispose()
      throw error
    }
  }

  /** @anchor frontend-process-layer */
  public async openComponent(host: Host): Promise<FrontendComponent> {
    this.assertOpen()
    return this.runtime.runPromise(
      Effect.flatMap(
        FrontendProcessService,
        (process: FrontendProcessModule): Effect.Effect<FrontendComponent> =>
          FrontendComponent.make(host, process.processStartedAt(), this.runtime.scope),
      ),
    )
  }

  /** @anchor frontend-process-layer */
  public async close(): Promise<void> {
    this.closed = true
    await this.runtime.dispose()
  }

  private assertOpen(): void {
    if (this.closed) {
      throw new Error('前端容器已关闭')
    }
  }
}
