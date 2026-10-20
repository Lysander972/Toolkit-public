import type { Effect, Option } from 'effect'

/** @anchor asset-config-contract */
export interface AssetCredentials {
  /** @anchor asset-config-contract */
  read(signal: AbortSignal): Effect.Effect<Option.Option<string>, unknown>
}
