import assert from 'node:assert/strict'
import { readFile, readdir } from 'node:fs/promises'
import { join } from 'node:path'
import { Context, Effect, Layer, Result, Scope } from 'effect'
import { test } from 'vitest'
import pino from 'pino'
import {
  DiagnosticsService,
  type Diagnostics,
  type DiagnosticRequest,
} from '../../../src/toolkit/diagnostics/Diagnostics.ts'
import {
  ProjectEntities,
  ProjectEntitiesService,
} from '../../../src/toolkit/entities/ProjectEntities.ts'
import { Host } from '../../../src/toolkit/host/Host.ts'
import type { ViewMode } from '../../../src/toolkit/views/ProjectViews.ts'
import { FileTestWorkspace } from '../toolkit-file-support/FileTestWorkspace.ts'

/**
 * @anchor diagnostics-service-contract
 * @anchor host-diagnostics-dispatch
 */
export class DiagnosticsTests {
  private constructor() {}

  static {
    test('仅注入实体即可装配诊断，空范围与显式范围失败且保留请求和工程', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      try {
        const original: string = 'guid: diagnostics\nname: 诊断\nmainMap: ""'
        await workspace.write('setting.json', original)
        const revision: string = (await Effect.runPromise(workspace.files.metadata('setting.json')))
          .revision
        const written: number = workspace.writtenBytes()
        const entries: readonly string[] = await readdir(workspace.fullPath)
        const requests: readonly DiagnosticRequest[] = [[], ['root']].map(
          (entityIds: readonly string[]): DiagnosticRequest => ({
            entityIds,
            baseline: {
              files: [{ relativePath: 'setting.json', revision }],
              directories: [{ relativePath: '.', entries }],
              fixedInputs: [],
            },
          }),
        )
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const entities: Layer.Layer<ProjectEntitiesService, unknown> = Layer.effect(
                ProjectEntitiesService,
                ProjectEntities.make(workspace.files),
              )
              const context: Context.Context<DiagnosticsService> = yield* Layer.build(
                DiagnosticsService.layer(entities),
              )
              const service: Diagnostics = Context.get(context, DiagnosticsService)
              for (const request of requests) {
                const before: DiagnosticRequest = structuredClone(request)
                const result: Result.Result<
                  Effect.Success<ReturnType<Diagnostics['inspect']>>,
                  unknown
                > = yield* Effect.result(service.inspect(request))
                assert.ok(Result.isFailure(result))
                assert.ok(result.failure instanceof Error)
                assert.equal(
                  result.failure.message,
                  'TODO: 按同次实体读取基准准备 Lint 并交付具名诊断结果',
                )
                assert.deepEqual(request, before)
              }
            }),
          ),
        )
        assert.equal(await readFile(join(workspace.fullPath, 'setting.json'), 'utf8'), original)
        assert.deepEqual(await readdir(workspace.fullPath), entries)
        assert.equal(workspace.writtenBytes(), written)
        assert.equal(
          (await Effect.runPromise(workspace.files.metadata('setting.json'))).revision,
          revision,
        )
      } finally {
        await workspace.close()
      }
    })

    test('两种模式的 Host 诊断明确失败，关闭后拒绝调用且工程零写入', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      try {
        const original: string = 'guid: diagnostics\nname: 诊断\nmainMap: ""'
        await workspace.write('setting.json', original)
        const revision: string = (await Effect.runPromise(workspace.files.metadata('setting.json')))
          .revision
        const request: DiagnosticRequest = {
          entityIds: ['root'],
          baseline: {
            files: [{ relativePath: 'setting.json', revision }],
            directories: [{ relativePath: '.', entries: ['setting.json'] }],
            fixedInputs: [],
          },
        }
        const before: DiagnosticRequest = structuredClone(request)
        const written: number = workspace.writtenBytes()
        const modes: readonly ViewMode[] = ['files', 'memory']
        for (const mode of modes) {
          const host: Host = await Host.open(workspace.fullPath, mode, pino({ enabled: false }))
          const controller: AbortController = new AbortController()
          try {
            await assert.rejects(host.inspect(request, controller.signal), {
              message: 'TODO: 按同次实体读取基准准备 Lint 并交付具名诊断结果',
            })
            assert.deepEqual(request, before)
          } finally {
            await host.close()
          }
          await assert.rejects(host.inspect(request, controller.signal), /Host 已关闭/u)
        }
        assert.equal(await readFile(join(workspace.fullPath, 'setting.json'), 'utf8'), original)
        assert.deepEqual(await readdir(workspace.fullPath), ['setting.json'])
        assert.equal(workspace.writtenBytes(), written)
      } finally {
        await workspace.close()
      }
    })
  }
}
