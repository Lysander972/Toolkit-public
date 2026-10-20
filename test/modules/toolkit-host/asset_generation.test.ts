import assert from 'node:assert/strict'
import { Readable } from 'node:stream'
import { Effect, Result, type Option } from 'effect'
import { test } from 'vitest'
import { AssetGeneration } from '../../../src/toolkit/asset_generation/AssetGeneration.ts'
import { WorkflowBindings } from '../../../src/toolkit/asset_generation/WorkflowBindings.ts'
import {
  ComfyExecution,
  ComfyNodeReference,
} from '../../../src/toolkit/asset_generation/ComfyExecution.ts'
import type { ComfyExecutionRequest } from '../../../src/toolkit/asset_generation/ComfyExecution.ts'
import {
  TextGenerationParameter,
  NumberGenerationParameter,
  BooleanGenerationParameter,
} from '../../../src/toolkit/asset_generation/GenerationContracts.ts'
import type {
  GenerationArtifact,
  GenerationRequest,
  GenerationReviewRequest,
} from '../../../src/toolkit/asset_generation/GenerationContracts.ts'
import type { AssetGenerationConfig } from '../../../src/toolkit/config/AssetGenerationConfig.ts'
import type { AssetCredentials } from '../../../src/toolkit/config/AssetCredentials.ts'

/**
 * @anchor asset-generation-contract
 * @anchor asset-generation-input-output
 * @anchor asset-workflow-binding
 * @anchor asset-native-execution
 */
export class AssetGenerationTest {
  private constructor() {}

  static {
    test('生成、绑定、传输与评阅空壳拒绝交付结果，保留上传字节', async (): Promise<void> => {
      const content: Readable = Readable.from([new Uint8Array([1, 2, 3])])
      const artifact: GenerationArtifact = Object.freeze({
        artifactId: 'source',
        name: 'source.png',
        mimeType: 'image/png',
        format: 'png',
        content,
      })
      const request: GenerationRequest = Object.freeze({
        capability: 'image-editing',
        prompt: '保留构图并修改颜色',
        seed: 42,
        parameters: Object.freeze({
          strength: new NumberGenerationParameter(0.5),
          caption: new TextGenerationParameter('保留构图'),
          preserveAlpha: new BooleanGenerationParameter(true),
        }),
        inputs: Object.freeze([artifact]),
      })
      const config: AssetGenerationConfig = Object.freeze<AssetGenerationConfig>({
        endpointTemplate: 'https://www.runninghub.cn/proxy/{api_key}',
        workflows: [
          {
            capability: 'image-editing',
            workflowId: 'unverified',
            models: [
              {
                nodeId: '1',
                inputName: 'ckpt_name',
                nodeModelName: 'fixture.safetensors',
                resourceType: 'CHECKPOINT',
                createTime: '2026-10-12T00:00:00Z',
                favoriteEvidence: '测试输入，未验证生产可用性',
              },
            ],
          },
        ],
      })
      const transportRequest: ComfyExecutionRequest = {
        workflowId: 'unverified',
        graph: {
          '1': {
            class_type: 'CheckpointLoaderSimple',
            inputs: { ckpt_name: new TextGenerationParameter('fixture.safetensors') },
          },
          '2': {
            class_type: 'CLIPTextEncode',
            inputs: {
              text: new TextGenerationParameter('保留构图并修改颜色'),
              clip: new ComfyNodeReference('1', 1),
            },
          },
        },
        uploads: request.inputs,
      }
      const reviewRequest: GenerationReviewRequest = { artifact, instruction: '检查颜色与透明边缘' }
      const originalConfig: AssetGenerationConfig = structuredClone(config)
      const graphSnapshot: unknown = structuredClone(transportRequest.graph)
      const credentials: AssetCredentials = {
        read: (): Effect.Effect<Option.Option<string>, unknown> =>
          Effect.fail(new Error('空壳测试没有远端凭据')),
      }
      const signal: AbortSignal = new AbortController().signal
      try {
        assert.throws(
          (): unknown => WorkflowBindings.select(config, request),
          (error: unknown): boolean => error instanceof Error && error.message.startsWith('TODO:'),
        )
        const operations: readonly Effect.Effect<unknown, unknown>[] = [
          AssetGeneration.execute(request, config, credentials, signal),
          ComfyExecution.execute(transportRequest, config, credentials, signal),
          AssetGeneration.review(reviewRequest, config, credentials, signal),
        ]
        for (const operation of operations) {
          const result: Result.Result<unknown, unknown> = await Effect.runPromise(
            Effect.result(operation),
          )
          assert.ok(Result.isFailure(result))
          assert.ok(result.failure instanceof Error)
          assert.ok(result.failure.message.startsWith('TODO:'))
        }
        const chunks: Uint8Array[] = []
        const bytes: AsyncIterable<Uint8Array> = artifact.content
        for await (const chunk of bytes) chunks.push(chunk)
        assert.deepEqual(chunks, [new Uint8Array([1, 2, 3])])
        assert.equal(request.prompt, '保留构图并修改颜色')
        assert.equal(request.seed, 42)
        assert.deepEqual(request.parameters, {
          strength: new NumberGenerationParameter(0.5),
          caption: new TextGenerationParameter('保留构图'),
          preserveAlpha: new BooleanGenerationParameter(true),
        })
        assert.deepEqual(config, originalConfig)
        assert.deepEqual(structuredClone(transportRequest.graph), graphSnapshot)
        assert.equal(transportRequest.workflowId, 'unverified')
        assert.equal(transportRequest.uploads, request.inputs)
        assert.deepEqual(reviewRequest, { artifact, instruction: '检查颜色与透明边缘' })
        assert.equal(signal.aborted, false)
      } finally {
        content.destroy()
      }
    })
  }
}
