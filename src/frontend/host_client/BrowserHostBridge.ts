import { Option } from 'effect'
import { SidebarMessages } from './SidebarMessages.ts'
import type { SidebarEnvelope, SidebarRequest, SidebarState } from './SidebarMessages.ts'

/** @anchor frontend-sidebar-bridge */
interface WebviewApi {
  /** @anchor frontend-sidebar-bridge */
  postMessage(message: unknown): void
}

/** @anchor frontend-sidebar-bridge */
declare function acquireVsCodeApi(): WebviewApi

/** @anchor frontend-sidebar-bridge */
export interface SidebarConnection {
  readonly send: (request: SidebarRequest) => void
  readonly subscribe: (listener: (state: SidebarState) => void) => () => void
}

/** @anchor frontend-sidebar-bridge */
export class BrowserHostBridge {
  private constructor() {}

  /** @anchor frontend-sidebar-bridge */
  public static connect(this: void): SidebarConnection {
    const api: WebviewApi = acquireVsCodeApi()
    return {
      send: (request: SidebarRequest): void => {
        api.postMessage({ action: request.action, target: Option.getOrNull(request.target) })
      },
      subscribe: (listener: (state: SidebarState) => void): (() => void) => {
        const receive: (event: MessageEvent<unknown>) => void = (
          event: MessageEvent<unknown>,
        ): void => {
          const envelope: SidebarEnvelope = SidebarMessages.envelopeSchema.parse(event.data)
          listener(SidebarMessages.stateSchema.parse(envelope.payload))
        }
        window.addEventListener('message', receive)
        return (): void => {
          window.removeEventListener('message', receive)
        }
      },
    }
  }
}
