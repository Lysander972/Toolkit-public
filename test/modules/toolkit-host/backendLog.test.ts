import assert from 'node:assert/strict'
import { test } from 'vitest'
import { z } from 'zod'
import pino from 'pino'
import { Effect, ManagedRuntime } from 'effect'
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import { BackendLog } from '../../../src/toolkit/host/BackendLog.ts'
import { Host } from '../../../src/toolkit/host/Host.ts'
import type { Logger } from 'pino'

/** @anchor backend-logging */
interface LogRecord {
  readonly level: number
  readonly component: string
  readonly msg: string
}

/** @anchor backend-logging */
class MemoryLogDestination {
  private readonly messages: string[] = []

  /** @anchor backend-logging */
  public write(message: string): void {
    this.messages.push(message)
  }

  /** @anchor backend-logging */
  public records(): LogRecord[] {
    const schema: z.ZodType<LogRecord> = z.object({
      level: z.number(),
      component: z.string(),
      msg: z.string(),
    })
    return this.messages.map((message: string): LogRecord => schema.parse(JSON.parse(message)))
  }
}

/**
 * @anchor backend-logging
 * @anchor logging-statistics-contract
 */
export class BackendLogTest {
  private constructor() {}

  static {
    test('Effect 六个级别各自映射为对应 pino level 并保留组件标识', async (): Promise<void> => {
      const destination: MemoryLogDestination = new MemoryLogDestination()
      const runtime: ManagedRuntime.ManagedRuntime<never, never> = ManagedRuntime.make(
        BackendLog.layer(pino({ level: 'trace' }, destination).child({ component: 'effect' })),
      )
      try {
        await runtime.runPromise(
          Effect.gen(function* (): Generator<Effect.Effect<void>, void, never> {
            yield* Effect.logInfo('信息')
            yield* Effect.logError('错误')
            yield* Effect.logWarning('警告')
            yield* Effect.logDebug('调试')
            yield* Effect.logTrace('琐碎')
            yield* Effect.logFatal('致命')
          }),
        )
      } finally {
        await runtime.dispose()
      }
      const records: LogRecord[] = destination.records()
      assert.deepEqual(
        records.map((record: LogRecord): number => record.level),
        [30, 50, 40, 20, 10, 60],
      )
      assert.deepEqual(
        records.map((record: LogRecord): string => record.msg),
        ['信息', '错误', '警告', '调试', '琐碎', '致命'],
      )
      assert.ok(records.every((record: LogRecord): boolean => record.component === 'effect'))
    })
  }
}

/** @anchor backend-log-injection */
export class HostLogInjectionTest {
  private constructor() {}

  static {
    test('Host 打开、关闭与打开失败的边界日志写入注入的输出', async (): Promise<void> => {
      await mkdir('_build/tests', { recursive: true })
      const workspaceFullPath: string = await mkdtemp(resolve('_build/tests/host-log-'))
      try {
        await writeFile(
          join(workspaceFullPath, 'setting.json'),
          'guid: a\nname: 工程\nmainMap: ""',
          'utf8',
        )
        const destination: MemoryLogDestination = new MemoryLogDestination()
        const logger: Logger = pino({ serializers: { err: pino.stdSerializers.err } }, destination)
        const host: Host = await Host.open(workspaceFullPath, 'memory', logger)
        await host.close()
        const hostMessages: string[] = destination
          .records()
          .filter((record: LogRecord): boolean => record.component === 'host')
          .map((record: LogRecord): string => record.msg)
        assert.deepEqual(hostMessages, ['Host 已打开', 'Host 已关闭'])

        const missingFullPath: string = await mkdtemp(resolve('_build/tests/host-log-missing-'))
        try {
          const failDestination: MemoryLogDestination = new MemoryLogDestination()
          const failLogger: Logger = pino(
            { serializers: { err: pino.stdSerializers.err } },
            failDestination,
          )
          await assert.rejects((): Promise<Host> =>
            Host.open(missingFullPath, 'memory', failLogger),
          )
          const failMessages: string[] = failDestination
            .records()
            .filter((record: LogRecord): boolean => record.component === 'host')
            .map((record: LogRecord): string => record.msg)
          assert.deepEqual(failMessages, ['Host 打开失败', 'Host 已关闭'])
        } finally {
          await rm(missingFullPath, { recursive: true })
        }
      } finally {
        await rm(workspaceFullPath, { recursive: true })
      }
    })
  }
}
