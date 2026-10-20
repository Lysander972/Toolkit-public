import assert from 'node:assert/strict'
import { Readable } from 'node:stream'
import { readFile } from 'node:fs/promises'
import { Context, Effect, Layer, Result, Scope } from 'effect'
import type { CanvasKit } from 'canvaskit-wasm'
import { test } from 'vitest'
import {
  AssetCandidatesService,
  type AssetCandidates,
} from '../../../src/toolkit/asset_candidates/AssetCandidates.ts'
import {
  AssetProcessingService,
  ImageRequirement,
  AudioRequirement,
  VideoRequirement,
  ModelRequirement,
  type AssetProcessing,
  type AssetRequirement,
  type AssetInspection,
} from '../../../src/toolkit/asset_processing/AssetProcessing.ts'
import type { GenerationArtifact } from '../../../src/toolkit/asset_generation/GenerationContracts.ts'
import type { CandidateStoreRequest } from '../../../src/toolkit/asset_candidates/CandidateContracts.ts'
import { CpuTestBackend } from '../canvas-render-support/CpuTestBackend.ts'

/** @anchor asset-candidates-contract */
export class AssetCandidatesTest {
  private constructor() {}

  static {
    test('真实候选 Layer 的存储、读取和按任务释放明确失败', async (): Promise<void> => {
      const pngBytes: Uint8Array = new Uint8Array(
        await readFile('res/frontend/assets/setup-icons/typescript.png'),
      )
      const originalBytes: Uint8Array = new Uint8Array(pngBytes)
      const content: Readable = Readable.from([pngBytes])
      const artifact: GenerationArtifact = Object.freeze({
        artifactId: 'image',
        name: 'image.png',
        mimeType: 'image/png',
        format: 'png',
        content,
      })
      const requirement: AssetRequirement = new ImageRequirement('png', 2, 2, true)
      const inspection: AssetInspection = Object.freeze({
        artifactId: artifact.artifactId,
        requirement,
        diagnostics: Object.freeze([]),
      })
      const request: CandidateStoreRequest = { taskId: 'task', artifact, inspection }
      const originalInspection: unknown = structuredClone(inspection)
      const signal: AbortSignal = new AbortController().signal
      try {
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const context: Context.Context<AssetCandidatesService> = yield* Layer.build(
                AssetCandidatesService.layer,
              )
              const candidates: AssetCandidates = Context.get(context, AssetCandidatesService)
              const operations: readonly Effect.Effect<unknown, unknown>[] = [
                candidates.store(request, signal),
                candidates.read('task', 'image', signal),
                candidates.release('task'),
              ]
              for (const operation of operations) {
                const result: Result.Result<unknown, unknown> = yield* Effect.result(operation)
                assert.ok(Result.isFailure(result))
                assert.ok(result.failure instanceof Error)
                assert.match(result.failure.message, /^TODO:/u)
              }
            }),
          ),
        )
        assert.equal(artifact.content, content)
        assert.equal(inspection.requirement, requirement)
        assert.deepEqual(structuredClone(inspection), originalInspection)
        assert.deepEqual(request, { taskId: 'task', artifact, inspection })
        assert.equal(signal.aborted, false)
        const chunks: Uint8Array[] = []
        const bytes: AsyncIterable<Uint8Array> = artifact.content
        for await (const chunk of bytes) chunks.push(chunk)
        assert.deepEqual(chunks, [originalBytes])
      } finally {
        content.destroy()
      }
    })
  }
}

/**
 * @anchor asset-processing-contract
 * @anchor asset-inspection-requirements
 */
export class AssetProcessingTest {
  private constructor() {}

  static {
    test('真实处理 Layer 的四类校验与切片明确失败，输入和图片字节保持原状', async (): Promise<void> => {
      const backend: CanvasKit = await CpuTestBackend.initialize()
      const pngBytes: Uint8Array = new Uint8Array(
        await readFile('res/frontend/assets/setup-icons/typescript.png'),
      )
      const originalBytes: Uint8Array = new Uint8Array(pngBytes)
      const content: Readable = Readable.from([pngBytes])
      const artifact: GenerationArtifact = Object.freeze({
        artifactId: 'image',
        name: 'image.png',
        mimeType: 'image/png',
        format: 'png',
        content,
      })
      const requirements: readonly AssetRequirement[] = [
        new ImageRequirement('png', 32, 32, true),
        new AudioRequirement('wav', 1.25, 44100, 2),
        new VideoRequirement('mp4', 640, 480, 1.25, 24),
        new ModelRequirement(['model.glb', 'texture.png']),
      ]
      const sliceRequest: Parameters<AssetProcessing['renderSlice']>[0] = {
        artifact,
        slice: {
          slicedBorderLB: { x: 1, y: 2, z: 0 },
          slicedBorderRT: { x: 3, y: 4, z: 0 },
          slicedRectLB: { x: 5, y: 6, z: 0 },
          slicedRectRT: { x: 7, y: 8, z: 0 },
        },
        sizes: [
          { width: 100, height: 80 },
          { width: 160, height: 120 },
        ],
      }
      const signal: AbortSignal = new AbortController().signal
      try {
        await Effect.runPromise(
          Effect.scoped(
            Effect.gen(function* (): Generator<
              Effect.Effect<unknown, unknown, Scope.Scope>,
              void,
              never
            > {
              const context: Context.Context<AssetProcessingService> = yield* Layer.build(
                AssetProcessingService.layer,
              )
              const processing: AssetProcessing = Context.get(context, AssetProcessingService)
              for (const requirement of requirements) {
                const result: Result.Result<unknown, unknown> = yield* Effect.result(
                  processing.inspect(artifact, requirement, signal),
                )
                assert.ok(Result.isFailure(result))
                assert.ok(result.failure instanceof Error)
                assert.equal(result.failure.message, 'TODO: 解码具名产物并按明确要求校验格式与质量')
              }
              const previewResult: Result.Result<unknown, unknown> = yield* Effect.result(
                processing.renderSlice(sliceRequest, backend, signal),
              )
              assert.ok(Result.isFailure(previewResult))
              assert.ok(previewResult.failure instanceof Error)
              assert.equal(
                previewResult.failure.message,
                'TODO: 按显式切片和尺寸案例装配 Canvas 并复用 CPU renderer',
              )
            }),
          ),
        )
        const chunks: Uint8Array[] = []
        for await (const chunk of artifact.content) chunks.push(chunk)
        assert.deepEqual(chunks, [originalBytes])
        assert.deepEqual(requirements, [
          new ImageRequirement('png', 32, 32, true),
          new AudioRequirement('wav', 1.25, 44100, 2),
          new VideoRequirement('mp4', 640, 480, 1.25, 24),
          new ModelRequirement(['model.glb', 'texture.png']),
        ])
        assert.deepEqual(sliceRequest, {
          artifact,
          slice: {
            slicedBorderLB: { x: 1, y: 2, z: 0 },
            slicedBorderRT: { x: 3, y: 4, z: 0 },
            slicedRectLB: { x: 5, y: 6, z: 0 },
            slicedRectRT: { x: 7, y: 8, z: 0 },
          },
          sizes: [
            { width: 100, height: 80 },
            { width: 160, height: 120 },
          ],
        })
        assert.equal(signal.aborted, false)
      } finally {
        content.destroy()
      }
    })
  }
}
