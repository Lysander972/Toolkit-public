import assert from 'node:assert/strict'
import { test } from 'vitest'
import { z } from 'zod'
import { FrontendLog } from '../../../src/frontend/plugin/FrontendLog.ts'
import type { LogDestination } from '../../../src/frontend/plugin/FrontendLog.ts'

/** @anchor frontend-log-destination */
interface ErrorLog {
  readonly level: number
  readonly component: string
  readonly msg: string
  readonly err: SerializedError
}

/** @anchor frontend-log-destination */
interface SerializedError {
  readonly type: string
  readonly message: string
  readonly stack: string
}

/** @anchor frontend-log-destination */
class MemoryLogDestination implements LogDestination {
  private readonly messages: string[] = []

  /** @anchor frontend-log-destination */
  public append(message: string): void {
    this.messages.push(message)
  }

  /** @anchor frontend-log-destination */
  public contents(): string {
    return this.messages.join('')
  }
}

/**
 * @anchor frontend-log-destination
 * @anchor host-shell-logging
 * @anchor logging-statistics-contract
 */
export class FrontendLogTest {
  private constructor() {}

  static {
    test('Pino 原生 child 与 Error serializer 写入输出通道', (): void => {
      const destination: MemoryLogDestination = new MemoryLogDestination()
      const logger: ReturnType<FrontendLog['logger']> = new FrontendLog(destination)
        .logger()
        .child({ component: 'sidebar' })
      logger.error({ err: new Error('query failed') }, '交互失败')
      const schema: z.ZodType<ErrorLog> = z.object({
        level: z.number(),
        component: z.string(),
        msg: z.string(),
        err: z.object({ type: z.string(), message: z.string(), stack: z.string() }),
      })
      const record: ErrorLog = schema.parse(JSON.parse(destination.contents()))
      assert.equal(record.level, 50)
      assert.equal(record.component, 'sidebar')
      assert.equal(record.msg, '交互失败')
      assert.equal(record.err.type, 'Error')
      assert.equal(record.err.message, 'query failed')
      assert.ok(record.err.stack.includes('query failed'))
    })
  }
}
