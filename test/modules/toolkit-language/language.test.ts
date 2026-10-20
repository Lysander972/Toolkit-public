import assert from 'node:assert/strict'
import { readFile, readdir } from 'node:fs/promises'
import { join } from 'node:path'
import { test } from 'vitest'
import { Context, Effect, Layer, Result, Scope } from 'effect'
import type { EntityFileMetadata } from '../../../src/toolkit/project_model/FileEntities.ts'
import {
  LanguageQueriesService,
  NavigationQueryRequest,
  DocumentDiagnosticsRequest,
  type LanguageQueries,
  type LanguageDocument,
  type LanguageQueryRequest,
} from '../../../src/toolkit/language/LanguageQueries.ts'
import { DiagnosticsService } from '../../../src/toolkit/diagnostics/Diagnostics.ts'
import {
  ProjectEntities,
  ProjectEntitiesService,
} from '../../../src/toolkit/entities/ProjectEntities.ts'
import { ProjectFiles } from '../../../src/toolkit/files/ProjectFiles.ts'
import { FileTestWorkspace } from '../toolkit-file-support/FileTestWorkspace.ts'

/**
 * @anchor language-query-contract
 * @anchor host-language-dispatch
 */
export class LanguageQueriesTest {
  private constructor() {}

  static {
    test('真实实体和诊断装配的四类语言查询明确失败并保留文本、基准及工程', async (): Promise<void> => {
      const workspace: FileTestWorkspace = await FileTestWorkspace.make()
      try {
        const original: string = 'guid: language\nname: 查询\nmainMap: ""'
        await workspace.write('setting.json', original)
        const files: ProjectFiles = workspace.files
        const controller: AbortController = new AbortController()
        const sourceMetadata: EntityFileMetadata = await Effect.runPromise(
          files.metadata('setting.json'),
        )
        const directoryMetadata: EntityFileMetadata = await Effect.runPromise(files.metadata('.'))
        const revision: string = sourceMetadata.revision
        const directoryRevision: string = directoryMetadata.revision
        const written: number = workspace.writtenBytes()
        const document: LanguageDocument = Object.freeze({
          entityId: 'device',
          documentRelativePath: 'views/查询.ts',
          text: 'namespace 查询 {}\n',
          textVersion: 7,
          baseline: Object.freeze({
            files: Object.freeze([Object.freeze({ relativePath: 'setting.json', revision })]),
            directories: [{ relativePath: '.', entries: await readdir(workspace.fullPath) }],
            fixedInputs: [{ name: 'platform', revision: 'platform-v1' }],
          }),
        })
        const originalDocument: LanguageDocument = structuredClone(document)
        const requests: readonly LanguageQueryRequest[] = [
          new NavigationQueryRequest(
            'definition',
            Object.freeze({ line: 0, character: 10 }),
            document,
            controller.signal,
          ),
          new NavigationQueryRequest(
            'references',
            Object.freeze({ line: 0, character: 10 }),
            document,
            controller.signal,
          ),
          new NavigationQueryRequest(
            'hover',
            Object.freeze({ line: 0, character: 10 }),
            document,
            controller.signal,
          ),
          new DocumentDiagnosticsRequest(document, controller.signal),
        ]
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
              const diagnostics: Layer.Layer<DiagnosticsService, unknown> =
                DiagnosticsService.layer(entities)
              const context: Context.Context<LanguageQueriesService> = yield* Layer.build(
                LanguageQueriesService.layer(entities, diagnostics),
              )
              const service: LanguageQueries = Context.get(context, LanguageQueriesService)
              for (const request of requests) {
                Object.freeze(request)
                const result: Result.Result<unknown, unknown> = yield* Effect.result(
                  service.query(request),
                )
                assert.ok(Result.isFailure(result))
                assert.ok(result.failure instanceof Error)
                assert.ok(result.failure.message.startsWith('TODO:'))
                assert.deepEqual(request.document, originalDocument)
                assert.equal(request.signal, controller.signal)
                assert.equal(request.signal.aborted, false)
                if (request instanceof NavigationQueryRequest) {
                  assert.deepEqual(request.position, { line: 0, character: 10 })
                }
              }
            }),
          ),
        )
        assert.equal(await readFile(join(workspace.fullPath, 'setting.json'), 'utf8'), original)
        assert.deepEqual(await readdir(workspace.fullPath), ['setting.json'])
        assert.equal(workspace.writtenBytes(), written)
        assert.equal((await Effect.runPromise(files.metadata('setting.json'))).revision, revision)
        assert.equal((await Effect.runPromise(files.metadata('.'))).revision, directoryRevision)
      } finally {
        await workspace.close()
      }
    })
  }
}
