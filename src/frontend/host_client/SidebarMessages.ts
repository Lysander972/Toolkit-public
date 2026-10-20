import { z } from 'zod'
import { Option } from 'effect'
import type { SetupTargetId } from './SetupTargets.ts'

type SidebarAction =
  | 'ready'
  | 'status'
  | 'configure'
  | 'logs'
  | 'settings'
  | 'documentation'
  | 'version'
  | 'architecture'
/** @anchor frontend-readonly-surface */
type SetupCardLevel = 'basic' | 'full'

/** @anchor frontend-readonly-surface */
export interface SidebarRequest {
  readonly action: SidebarAction
  readonly target: Option.Option<SetupTargetId>
}

/** @anchor frontend-readonly-surface */
export interface SetupCard {
  readonly id: SetupTargetId
  readonly title: string
  readonly level: SetupCardLevel
}

/** @anchor frontend-readonly-surface */
export interface SidebarState {
  readonly status: string
  readonly endpoint: string
  readonly version: string
  readonly error: string
  readonly tools: readonly string[]
  readonly targets: readonly SetupCard[]
  readonly language: string
  readonly canvas: string
  readonly externalCalls: string
}

/** @anchor frontend-readonly-surface */
export interface SidebarEnvelope {
  readonly kind: 'state'
  readonly payload: unknown
}

/** @anchor frontend-readonly-surface */
export class SidebarMessages {
  public static readonly targetSchema: z.ZodType<SetupTargetId> = z.enum([
    'claudeCode',
    'codex',
    'gemini',
    'openCode',
    'trae',
    'cursor',
    'factoryDroid',
    'kimiCode',
    'cline',
    'kiloCode',
    'crush',
  ])
  public static readonly requestSchema: z.ZodType<SidebarRequest> = z.object({
    action: z.enum([
      'ready',
      'status',
      'configure',
      'logs',
      'settings',
      'documentation',
      'version',
      'architecture',
    ]),
    target: SidebarMessages.targetSchema.nullable().transform(Option.fromNullishOr),
  })
  public static readonly envelopeSchema: z.ZodType<SidebarEnvelope> = z.object({
    kind: z.literal('state'),
    payload: z.unknown(),
  })
  public static readonly stateSchema: z.ZodType<SidebarState> = z.object({
    status: z.string(),
    endpoint: z.string(),
    version: z.string(),
    error: z.string(),
    tools: z.array(z.string()),
    targets: z.array(
      z.object({
        id: SidebarMessages.targetSchema,
        title: z.string(),
        level: z.enum(['basic', 'full']),
      }),
    ),
    language: z.string(),
    canvas: z.string(),
    externalCalls: z.string(),
  })
  private constructor() {}
}
