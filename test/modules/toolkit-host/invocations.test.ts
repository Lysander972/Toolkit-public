import assert from 'node:assert/strict'
import { Context, Effect, Layer, Result, Scope, Stream } from 'effect'
import { test } from 'vitest'
import {
  InvocationRecordsService,
  StartedInvocationEvent,
  FinishedInvocationEvent,
  type InvocationRecords,
  type InvocationEvent,
} from '../../../src/toolkit/invocations/InvocationRecords.ts'

/**
 * @anchor invocation-records-contract
 * @anchor invocation-event-contract
 * @anchor invocation-display-flow
 */
export class InvocationRecordsTest {
  private constructor() {}

  static {
    test('真实记录 Layer 明确拒绝开始、各终态及读取，保持输入', async (): Promise<void> => {
      const events: readonly InvocationEvent[] = Object.freeze([
        new StartedInvocationEvent('query', 'workspace_status', 1),
        new FinishedInvocationEvent('query', 2, 'succeeded', []),
        new FinishedInvocationEvent('query', 2, 'failed', ['读取失败']),
        new FinishedInvocationEvent('query', 2, 'canceled', []),
      ])
      const original: readonly InvocationEvent[] = structuredClone(events)
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const context: Context.Context<InvocationRecordsService> = yield* Layer.build(
              InvocationRecordsService.layer,
            )
            const records: InvocationRecords = Context.get(context, InvocationRecordsService)
            for (const event of events) {
              const result: Result.Result<unknown, unknown> = yield* Effect.result(
                records.run(event),
              )
              assert.ok(Result.isFailure(result))
              assert.ok(result.failure instanceof Error)
              assert.equal(result.failure.message, 'TODO: Host 调用开始、结果与取消记录')
            }
            const read: Result.Result<unknown, unknown> = yield* Effect.result(
              records.read('query'),
            )
            assert.ok(Result.isFailure(read))
            assert.ok(read.failure instanceof Error)
            assert.equal(read.failure.message, 'TODO: 读取同一 Host Scope 的调用记录')
            const changes: Result.Result<unknown, unknown> = yield* Effect.result(
              Stream.runCollect(records.changes()),
            )
            assert.ok(Result.isFailure(changes))
            assert.ok(changes.failure instanceof Error)
            assert.equal(changes.failure.message, 'TODO: 订阅同一 Host Scope 的调用记录')
          }),
        ),
      )
      assert.deepEqual(structuredClone(events), original)
    })
  }
}
