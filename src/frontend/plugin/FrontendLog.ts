import pino from 'pino'
import type { Logger, DestinationStream } from 'pino'

/** @anchor frontend-log-destination */
export interface LogDestination {
  /** @anchor frontend-log-destination */
  append(message: string): void
}

/** @anchor frontend-log-destination */
export class FrontendLog implements DestinationStream {
  private readonly output: LogDestination

  /** @anchor frontend-log-destination */
  public constructor(output: LogDestination) {
    this.output = output
  }

  /**
   * @anchor frontend-log-destination
   * @anchor host-shell-logging
   * @anchor logging-statistics-contract
   * @passthrough-exempt 实现 pino DestinationStream.write，日志正文交给 LogDestination
   */
  public write(message: string): void {
    this.output.append(message)
  }

  /**
   * @anchor frontend-log-destination
   * @anchor host-shell-logging
   * @anchor logging-statistics-contract
   */
  public logger(): Logger {
    return pino({ serializers: { err: pino.stdSerializers.err } }, this)
  }
}
