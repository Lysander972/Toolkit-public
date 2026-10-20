import type { ExtensionFactory } from '@earendil-works/pi-coding-agent'
import type { PiWorkspace } from './PiWorkspace.ts'
import type { PiContext } from './PiContext.ts'

/** @anchor pi-host-bootstrap */
export interface PiExtensionSession {
  readonly workspace: Awaited<ReturnType<typeof PiWorkspace.open>>
  readonly context: Parameters<typeof PiContext.register>[0]
}

/** @anchor pi-host-bootstrap */
export class PiExtension {
  private constructor() {}

  /**
   * @anchor pi-host-bootstrap
   * @anchor pi-extension-placeholder
   */
  public static register(
    this: void,
    _pi: PiExtensionSession['context'],
  ): ReturnType<ExtensionFactory> {
    throw new Error('TODO: Pi 官方扩展 flags、事件与会话资源登记')
  }
}

const piExtension: ExtensionFactory = (
  pi: Parameters<ExtensionFactory>[0],
): ReturnType<ExtensionFactory> => PiExtension.register(pi)

export default piExtension
