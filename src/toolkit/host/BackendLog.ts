import { Layer, Logger as EffectLogger, References } from 'effect'
import type { Logger } from 'pino'

/**
 * Effect 日志合同的唯一适配：把 Effect 运行时日志映射到组合根注入的 pino Logger,
 * 并以 mergeWithExisting: false 替换默认控制台日志,stdio 下 stdout 只承载协议。
 * Effect 侧最低级别放开到 Trace,级别过滤由注入的 pino Logger 原生 level 统一决定。
 * @anchor backend-logging
 */
export class BackendLog {
  private constructor() {}

  /**
   * @anchor backend-logging
   * @anchor logging-statistics-contract
   */
  public static layer(logger: Logger): Layer.Layer<never> {
    return Layer.merge(
      EffectLogger.layer([BackendLog.make(logger)], { mergeWithExisting: false }),
      Layer.succeed(References.MinimumLogLevel, 'Trace'),
    )
  }

  /**
   * @anchor backend-logging
   * @anchor logging-statistics-contract
   */
  private static make(logger: Logger): EffectLogger.Logger<unknown, void> {
    return EffectLogger.make((options: EffectLogger.Options<unknown>): void => {
      const message: string = BackendLog.message(options.message)
      switch (options.logLevel) {
        case 'Fatal':
          logger.fatal(message)
          break
        case 'Error':
          logger.error(message)
          break
        case 'Warn':
          logger.warn(message)
          break
        case 'Info':
          logger.info(message)
          break
        case 'Debug':
          logger.debug(message)
          break
        case 'Trace':
          logger.trace(message)
          break
        case 'All':
        case 'None':
          break
      }
    })
  }

  /**
   * @anchor backend-logging
   * @anchor logging-statistics-contract
   */
  private static message(message: unknown): string {
    return Array.isArray(message)
      ? message.map((part: unknown): string => String(part)).join(' ')
      : String(message)
  }
}
