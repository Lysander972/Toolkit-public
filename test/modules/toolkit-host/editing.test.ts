import assert from 'node:assert/strict'
import { mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { Readable } from 'node:stream'
import { test } from 'vitest'
import ts from 'typescript'
import pino from 'pino'
import { Context, Effect, Layer, Option, Result, Scope } from 'effect'
import { NodeFileSystem } from '@effect/platform-node'
import { CanvasConverter } from '@toolkit/canvas'
import type {
  Editing,
  EditingRequest,
  EditingCandidate,
} from '../../../src/toolkit/editing/Editing.ts'
import {
  ReplaceTsViewRequest,
  ReplaceHtmlViewRequest,
} from '../../../src/toolkit/editing/Editing.ts'
import {
  WorkspaceService,
  type WorkspaceModule,
} from '../../../src/toolkit/host/WorkspaceServices.ts'
import {
  ProjectEntities,
  ProjectEntitiesService,
} from '../../../src/toolkit/entities/ProjectEntities.ts'
import { TsViewEntity, HtmlViewEntity } from '../../../src/toolkit/entities/ToolkitEntity.ts'
import { EntityChange } from '../../../src/toolkit/entities/EntityChange.ts'
import { Host } from '../../../src/toolkit/host/Host.ts'
import { RenameRefactorRequest } from '../../../src/toolkit/change_plan/RefactorRequest.ts'
import { ProjectFiles } from '../../../src/toolkit/files/ProjectFiles.ts'
import { PlatformFiles, PlatformFilesService } from '../../../src/toolkit/files/PlatformFiles.ts'
import { ProjectWatcher, ProjectWatcherService } from '../../../src/toolkit/files/ProjectWatcher.ts'

/**
 * @anchor editing-service-contract
 * @anchor editing-request-contract
 * @anchor host-editing-dispatch
 */
export class EditingTest {
  private constructor() {}

  static {
    test('真实工作区编辑、Host重构及候选提交保持具体TODO失败、输入保真与零写入', async (): Promise<void> => {
      await mkdir('_build/tests', { recursive: true })
      const workspaceFullPath: string = await mkdtemp(resolve('_build/tests/editing-'))
      try {
        const platform: PlatformFiles = await Effect.runPromise(
          PlatformFiles.make(workspaceFullPath).pipe(Effect.provide(NodeFileSystem.layer)),
        )
        const files: ProjectFiles = await Effect.runPromise(
          ProjectFiles.make().pipe(Effect.provideService(PlatformFilesService, platform)),
        )
        const originalSettings: string = 'guid: editing\nname: 原始\nmainMap: ""'
        const updatedSettings: string = 'guid: editing\nname: 候选\nmainMap: ""'
        await writeFile(join(workspaceFullPath, 'setting.json'), originalSettings, 'utf8')
        const revision: string = (await Effect.runPromise(files.metadata('setting.json'))).revision
        const sourceBaseline: EditingRequest['baseline']['files'] = Object.freeze([
          Object.freeze({ relativePath: 'setting.json', revision }),
        ])
        const baseline: EditingRequest['baseline'] = Object.freeze({
          files: sourceBaseline,
          directories: [{ relativePath: '.', entries: await readdir(workspaceFullPath) }],
          fixedInputs: [{ name: 'platform', revision: 'platform-v1' }],
        })
        const originalBaseline: EditingRequest['baseline'] = structuredClone(baseline)
        const tsText: string = 'namespace 示例 {}\n'
        const htmlText: string = '<BeCanvas name="示例" width="2" height="2"></BeCanvas>'
        const tsView: TsViewEntity = new TsViewEntity(
          'device',
          'views/示例.ts',
          baseline,
          tsText,
          ts.createSourceFile('views/示例.ts', tsText, ts.ScriptTarget.Latest),
        )
        const htmlView: HtmlViewEntity = new HtmlViewEntity(
          'canvas',
          'views/示例.html',
          baseline,
          htmlText,
          CanvasConverter.fromHtml(htmlText),
        )
        const originalCanvas: Uint8Array = CanvasConverter.toBinary(htmlView.document)
        const change: EntityChange = await Effect.runPromise(
          EntityChange.open(files, 'setting.json', sourceBaseline, baseline.directories),
        )
        change.write('setting.json', updatedSettings)
        const candidate: EditingCandidate = Object.freeze({
          change,
          baseline,
          diagnostics: [{ subjectId: 'root', message: '候选诊断保真' }],
        })
        const originalDiagnostics: EditingCandidate['diagnostics'] = structuredClone(
          candidate.diagnostics,
        )
        const directoryRevision: string = (await Effect.runPromise(files.metadata('.'))).revision
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const entities: Layer.Layer<ProjectEntitiesService, unknown> = Layer.effect(
                ProjectEntitiesService,
                ProjectEntities.make(files),
              )
              const context: Context.Context<WorkspaceService> = yield* Layer.build(
                WorkspaceService.layer(
                  'memory',
                  entities,
                  Layer.effect(ProjectWatcherService, ProjectWatcher.make(workspaceFullPath)),
                  Option.none(),
                ),
              )
              const service: WorkspaceModule = Context.get(context, WorkspaceService)
              const requests: readonly EditingRequest[] = [
                new ReplaceTsViewRequest(tsView, 'namespace 更新 {}\n', baseline),
                new ReplaceHtmlViewRequest(
                  htmlView,
                  '<BeCanvas name="更新" width="2" height="2"></BeCanvas>',
                  baseline,
                ),
              ]
              for (const request of requests) {
                Object.freeze(request)
                const updatedText: string = request.updatedText
                const result: Result.Result<unknown, unknown> = yield* Effect.result(
                  service.editing.replace(request),
                )
                assert.ok(Result.isFailure(result))
                assert.ok(result.failure instanceof Error)
                assert.equal(
                  result.failure.message,
                  'TODO: 按完整读取基准反向转换、计划与校验统一编辑候选',
                )
                assert.equal(request.updatedText, updatedText)
                assert.deepEqual(request.baseline, originalBaseline)
              }
              const result: Result.Result<boolean, unknown> = yield* Effect.result(
                service.editing.commit(candidate),
              )
              assert.ok(Result.isFailure(result))
              assert.ok(result.failure instanceof Error)
              assert.equal(result.failure.message, 'TODO: 复核完整候选基准并经实体层提交及实际重读')
            }),
          ),
        )
        const renames: readonly RenameRefactorRequest[] = [
          new RenameRefactorRequest('script:局部', baseline, 'local-variable', [
            { oldName: '旧局部', newName: '新局部' },
          ]),
          new RenameRefactorRequest('device:零件', baseline, 'global-variable', [
            { oldName: '旧全局', newName: '新全局' },
          ]),
          new RenameRefactorRequest('method:脚本', baseline, 'script', [
            { oldName: '旧方法', newName: '新方法' },
          ]),
        ]
        const beforeRenames: readonly RenameRefactorRequest[] = structuredClone(renames)
        const host: Host = await Host.open(workspaceFullPath, 'memory', pino({ enabled: false }))
        try {
          for (const request of renames) {
            await assert.rejects(host.refactor(request, new AbortController().signal), {
              message: 'TODO: 按重构意图读取完整依赖、计算计划并校验统一编辑候选',
            })
          }
          const request: RenameRefactorRequest = new RenameRefactorRequest(
            'script:取消',
            baseline,
            'local-variable',
            [{ oldName: '取消前', newName: '取消后' }],
          )
          await assert.rejects(host.refactor(request, AbortSignal.abort('重构取消')))
          assert.equal((await host.status()).settings.name, '原始')
        } finally {
          await host.close()
        }
        await assert.rejects(
          host.refactor(
            new RenameRefactorRequest('script:关闭', baseline, 'script', [
              { oldName: '关闭前', newName: '关闭后' },
            ]),
            new AbortController().signal,
          ),
          /Host 已关闭/u,
        )
        assert.deepEqual(structuredClone(renames), beforeRenames)
        assert.equal(tsView.text, tsText)
        assert.equal(tsView.syntax.getFullText(), tsText)
        assert.equal(htmlView.text, htmlText)
        assert.deepEqual(CanvasConverter.toBinary(htmlView.document), originalCanvas)
        assert.equal(change.readText('setting.json'), updatedSettings)
        assert.deepEqual(candidate.diagnostics, originalDiagnostics)
        assert.deepEqual(candidate.baseline, originalBaseline)
        assert.deepEqual(await readdir(workspaceFullPath), ['setting.json'])
        assert.equal((await Effect.runPromise(files.metadata('setting.json'))).revision, revision)
        assert.equal((await Effect.runPromise(files.metadata('.'))).revision, directoryRevision)
        assert.equal(
          new TextDecoder('utf-8', { fatal: true }).decode(
            await Effect.runPromise(files.read('setting.json')),
          ),
          originalSettings,
        )
      } finally {
        await rm(workspaceFullPath, { recursive: true })
      }
    })
  }
}

/** @anchor asset-import-contract */
export class AssetImportTest {
  private constructor() {}

  static {
    test('资产入库空壳保持字节流、确认输入和工程内容', async (): Promise<void> => {
      const pngBytes: Uint8Array = new Uint8Array(
        await readFile('res/frontend/assets/setup-icons/typescript.png'),
      )
      const originalBytes: Uint8Array = new Uint8Array(pngBytes)
      await mkdir('_build/tests', { recursive: true })
      const workspaceFullPath: string = await mkdtemp(resolve('_build/tests/asset-import-'))
      const content: Readable = Readable.from([pngBytes])
      try {
        const originalSettings: string = 'guid: asset-import\nname: 原始\nmainMap: ""'
        await writeFile(join(workspaceFullPath, 'setting.json'), originalSettings, 'utf8')
        await mkdir(join(workspaceFullPath, 'image'))
        await writeFile(join(workspaceFullPath, 'image/existing.png'), originalBytes)
        const platform: PlatformFiles = await Effect.runPromise(
          PlatformFiles.make(workspaceFullPath).pipe(Effect.provide(NodeFileSystem.layer)),
        )
        const files: ProjectFiles = await Effect.runPromise(
          ProjectFiles.make().pipe(Effect.provideService(PlatformFilesService, platform)),
        )
        const request: Parameters<Editing['importAsset']>[0] = Object.freeze({
          content,
          targetRelativePath: 'image/selected.png',
          confirmation: '确认入库选定产物',
        })
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const entities: Layer.Layer<ProjectEntitiesService, unknown> = Layer.effect(
                ProjectEntitiesService,
                ProjectEntities.make(files),
              )
              const context: Context.Context<WorkspaceService> = yield* Layer.build(
                WorkspaceService.layer(
                  'memory',
                  entities,
                  Layer.effect(ProjectWatcherService, ProjectWatcher.make(workspaceFullPath)),
                  Option.none(),
                ),
              )
              const service: WorkspaceModule = Context.get(context, WorkspaceService)
              const result: Result.Result<unknown, unknown> = yield* Effect.result(
                service.editing.importAsset(request),
              )
              assert.ok(Result.isFailure(result))
              assert.ok(result.failure instanceof Error)
              assert.match(result.failure.message, /^TODO:/u)
            }),
          ),
        )
        const chunks: Uint8Array[] = []
        const bytes: AsyncIterable<Uint8Array> = request.content
        for await (const chunk of bytes) chunks.push(chunk)
        assert.deepEqual(chunks, [originalBytes])
        assert.equal(request.targetRelativePath, 'image/selected.png')
        assert.equal(request.confirmation, '确认入库选定产物')
        assert.deepEqual(await readdir(workspaceFullPath), ['image', 'setting.json'])
        assert.deepEqual(await readdir(join(workspaceFullPath, 'image')), ['existing.png'])
        assert.deepEqual(
          new Uint8Array(await readFile(join(workspaceFullPath, 'image/existing.png'))),
          originalBytes,
        )
        assert.equal(
          await readFile(join(workspaceFullPath, 'setting.json'), 'utf8'),
          originalSettings,
        )
      } finally {
        content.destroy()
        await rm(workspaceFullPath, { recursive: true })
      }
    })
  }
}
