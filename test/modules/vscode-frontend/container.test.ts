import { test, vi } from 'vitest'
import type { MockInstance } from 'vitest'
import assert from 'node:assert/strict'
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import { Effect, Scope } from 'effect'
import pino from 'pino'
import { Host } from '../../../src/toolkit/host/Host.ts'
import type { WorkspaceStatus } from '../../../src/toolkit/host/WorkspaceServices.ts'
import { ProjectFiles } from '../../../src/toolkit/files/ProjectFiles.ts'
import {
  FrontendProcess,
  FrontendProcessModule,
  FrontendRequestContext,
  type FrontendComponent,
} from '../../../src/frontend/plugin/FrontendProcess.ts'

/** @anchor frontend-component-layer */
type CloseSource = 'request' | 'component' | 'process'

/**
 * @anchor frontend-process-layer
 * @anchor frontend-component-layer
 */
export class FrontendProcessTest {
  private constructor() {}

  static {
    test('具名组件资源持有同一 Host，状态请求读取实际变化，关闭只释放所属前端资源', async (): Promise<void> => {
      await mkdir('_build/tests', { recursive: true })
      const workspaceFullPath: string = await mkdtemp(resolve('_build/tests/frontend-stack-'))
      try {
        const settingFullPath: string = join(workspaceFullPath, 'setting.json')
        await writeFile(settingFullPath, 'guid: first\nname: 工程\nmainMap: ""', 'utf8')
        const openBefore: number = FrontendProcessModule.openCount()
        const frontendProcess: FrontendProcess = await FrontendProcess.open()
        const host: Host = await Host.open(workspaceFullPath, 'files', pino({ enabled: false }))
        try {
          assert.equal(FrontendProcessModule.openCount(), openBefore + 1)
          const first: FrontendComponent = await frontendProcess.openComponent(host)
          const second: FrontendComponent = await frontendProcess.openComponent(host)
          try {
            assert.equal(first.host(), host)
            assert.equal(second.host(), host)
            assert.notEqual(first, second)
            assert.equal(first.processStartedAt(), second.processStartedAt())
            const request: Parameters<FrontendComponent['runInRequest']>[0] = {
              kind: 'workspace-status',
            }
            const signal: AbortSignal = new AbortController().signal
            const before: WorkspaceStatus = await first.runInRequest(request, signal)
            assert.deepEqual(before, {
              viewMode: 'files',
              settings: { guid: 'first', name: '工程', mainMap: '' },
            })
            await writeFile(settingFullPath, 'guid: changed\nname: 新工程\nmainMap: map', 'utf8')
            const after: WorkspaceStatus = await second.runInRequest(request, signal)
            assert.deepEqual(after, {
              viewMode: 'files',
              settings: { guid: 'changed', name: '新工程', mainMap: 'map' },
            })
            assert.deepEqual(request, { kind: 'workspace-status' })
            await assert.rejects(first.runInRequest(request, AbortSignal.abort('请求取消')))
            await first.close()
            assert.throws((): Host => first.host(), /前端组件已关闭/u)
            await assert.rejects(first.runInRequest(request, signal), /前端组件已关闭/u)
            assert.deepEqual(await second.runInRequest(request, signal), after)
            await frontendProcess.close()
            assert.equal(FrontendProcessModule.openCount(), openBefore)
            await assert.rejects(frontendProcess.openComponent(host), /前端容器已关闭/u)
            assert.throws((): Host => second.host(), /前端组件已关闭/u)
            assert.deepEqual(await host.status(), after)
          } finally {
            await first.close()
            await second.close()
          }
        } finally {
          await frontendProcess.close()
          await host.close()
        }
        assert.equal(FrontendProcessModule.openCount(), openBefore)
      } finally {
        await rm(workspaceFullPath, { recursive: true })
      }
    })
  }
}

/** @anchor frontend-component-layer */
export class FrontendRequestScopeTest {
  private constructor() {}

  static {
    test('请求上下文独立创建，作用域结束后释放', async (): Promise<void> => {
      const contexts: FrontendRequestContext[] = []
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const first: FrontendRequestContext = yield* FrontendRequestContext.make()
            const second: FrontendRequestContext = yield* FrontendRequestContext.make()
            contexts.push(first, second)
            assert.notEqual(first, second)
            assert.equal(first.isOpen(), true)
            assert.equal(second.isOpen(), true)
          }),
        ),
      )
      assert.equal(contexts.length, 2)
      for (const context of contexts) assert.equal(context.isOpen(), false)
    })

    test.each<CloseSource>(['request', 'component', 'process'])(
      '%s 取消传到实际 Host 读取操作，并释放正在运行的前端请求',
      async (closeSource: CloseSource): Promise<void> => {
        await mkdir('_build/tests', { recursive: true })
        const workspaceFullPath: string = await mkdtemp(resolve('_build/tests/frontend-scope-'))
        try {
          await writeFile(
            join(workspaceFullPath, 'setting.json'),
            'guid: scope\nname: 取消验证\nmainMap: ""',
            'utf8',
          )
          const frontendProcess: FrontendProcess = await FrontendProcess.open()
          const host: Host = await Host.open(workspaceFullPath, 'memory', pino({ enabled: false }))
          try {
            const component: FrontendComponent = await frontendProcess.openComponent(host)
            const contexts: FrontendRequestContext[] = []
            const make: typeof FrontendRequestContext.make =
              FrontendRequestContext.make.bind(FrontendRequestContext)
            const contextObserver: MockInstance<typeof FrontendRequestContext.make> = vi
              .spyOn(FrontendRequestContext, 'make')
              .mockImplementation((): ReturnType<typeof FrontendRequestContext.make> =>
                make().pipe(
                  Effect.tap((context: FrontendRequestContext): Effect.Effect<void> =>
                    Effect.sync((): void => {
                      contexts.push(context)
                    }),
                  ),
                ),
              )
            const started: PromiseWithResolvers<void> = Promise.withResolvers()
            let aborted: boolean = false
            const reading: MockInstance<ProjectFiles['read']> = vi
              .spyOn(ProjectFiles.prototype, 'read')
              .mockImplementationOnce(function (
                this: ProjectFiles,
                relativePath: string,
              ): Effect.Effect<Uint8Array, unknown> {
                reading.mockRestore()
                return Effect.flatMap(
                  this.read(relativePath),
                  (bytes: Uint8Array): Effect.Effect<Uint8Array> =>
                    Effect.promise((signal: AbortSignal): Promise<Uint8Array> => {
                      started.resolve()
                      return new Promise((resolve: (value: Uint8Array) => void): void => {
                        signal.addEventListener(
                          'abort',
                          (): void => {
                            aborted = true
                            resolve(bytes)
                          },
                          { once: true },
                        )
                      })
                    }),
                )
              })
            try {
              const controller: AbortController = new AbortController()
              const pending: Promise<WorkspaceStatus> = component.runInRequest(
                { kind: 'workspace-status' },
                controller.signal,
              )
              const rejected: Promise<void> = assert.rejects(pending)
              await started.promise
              assert.equal(contexts.length, 1)
              for (const context of contexts) assert.equal(context.isOpen(), true)
              switch (closeSource) {
                case 'request':
                  controller.abort('请求取消')
                  break
                case 'component':
                  await component.close()
                  break
                case 'process':
                  await frontendProcess.close()
                  break
              }
              await rejected
              assert.equal(aborted, true)
              for (const context of contexts) assert.equal(context.isOpen(), false)
              reading.mockRestore()
              const current: WorkspaceStatus = await host.status()
              assert.equal(current.settings.name, '取消验证')
              if (closeSource === 'request') {
                assert.deepEqual(
                  await component.runInRequest(
                    { kind: 'workspace-status' },
                    new AbortController().signal,
                  ),
                  current,
                )
                assert.equal(contexts.length, 2)
                await host.close()
                assert.equal(component.host(), host)
                await assert.rejects(
                  component.runInRequest(
                    { kind: 'workspace-status' },
                    new AbortController().signal,
                  ),
                  /Host 已关闭/u,
                )
                assert.equal(contexts.length, 3)
                assert.equal(new Set(contexts).size, 3)
                for (const context of contexts) assert.equal(context.isOpen(), false)
              } else {
                await assert.rejects(
                  component.runInRequest(
                    { kind: 'workspace-status' },
                    new AbortController().signal,
                  ),
                  /前端组件已关闭/u,
                )
              }
            } finally {
              reading.mockRestore()
              contextObserver.mockRestore()
              await component.close()
            }
          } finally {
            await frontendProcess.close()
            await host.close()
          }
        } finally {
          await rm(workspaceFullPath, { recursive: true })
        }
      },
    )
  }
}
