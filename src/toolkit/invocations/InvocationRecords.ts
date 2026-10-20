import { Context, Effect, Layer, Stream, type Option } from 'effect'

/** @anchor invocation-event-contract */
type InvocationOutcome = 'succeeded' | 'failed' | 'canceled'

/** @anchor invocation-event-contract */
export abstract class InvocationEvent {
  declare private readonly nominal: never

  /** @anchor invocation-event-contract */
  protected constructor(public readonly invocationId: string) {}
}

/** @anchor invocation-event-contract */
export class StartedInvocationEvent extends InvocationEvent {
  declare private readonly startedNominal: never

  /** @anchor invocation-event-contract */
  public constructor(
    invocationId: string,
    public readonly operation: string,
    public readonly startedAt: number,
  ) {
    super(invocationId)
  }
}

/** @anchor invocation-event-contract */
interface InvocationCompletion {
  readonly finishedAt: number
  readonly outcome: InvocationOutcome
  readonly diagnostics: readonly string[]
}

/** @anchor invocation-event-contract */
export class FinishedInvocationEvent extends InvocationEvent implements InvocationCompletion {
  declare private readonly finishedNominal: never

  /** @anchor invocation-event-contract */
  public constructor(
    invocationId: string,
    public readonly finishedAt: number,
    public readonly outcome: InvocationOutcome,
    public readonly diagnostics: readonly string[],
  ) {
    super(invocationId)
  }
}

/** @anchor invocation-records-contract */
interface InvocationRecord {
  readonly invocationId: string
  readonly operation: string
  readonly startedAt: number
  readonly completion: Option.Option<InvocationCompletion>
}

/** @anchor invocation-records-contract */
export interface InvocationRecords {
  /** @anchor invocation-records-contract */
  run(event: InvocationEvent): Effect.Effect<InvocationRecord, unknown>
  /** @anchor invocation-records-contract */
  read(invocationId: string): Effect.Effect<Option.Option<InvocationRecord>, unknown>
  /** @anchor invocation-display-flow */
  changes(): Stream.Stream<readonly InvocationRecord[], unknown>
}

/** @anchor invocation-records-contract */
class PendingInvocationRecords implements InvocationRecords {
  declare private readonly nominal: never

  /** @anchor invocation-records-contract */
  public run(_event: InvocationEvent): Effect.Effect<InvocationRecord, unknown> {
    return Effect.fail(new Error('TODO: Host 调用开始、结果与取消记录'))
  }

  /** @anchor invocation-records-contract */
  public read(_invocationId: string): Effect.Effect<Option.Option<InvocationRecord>, unknown> {
    return Effect.fail(new Error('TODO: 读取同一 Host Scope 的调用记录'))
  }

  /** @anchor invocation-display-flow */
  public changes(): ReturnType<InvocationRecords['changes']> {
    return Stream.fail(new Error('TODO: 订阅同一 Host Scope 的调用记录'))
  }
}

/** @anchor invocation-records-contract */
export class InvocationRecordsService extends Context.Service<
  InvocationRecordsService,
  InvocationRecords
>()('toolkit/InvocationRecords') {
  private constructor(service: never) {
    super(service)
  }

  public static readonly layer: Layer.Layer<InvocationRecordsService> = Layer.effect(
    InvocationRecordsService,
    Effect.sync((): InvocationRecords => new PendingInvocationRecords()),
  )
}
