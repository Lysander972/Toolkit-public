import type { ExtensionAPI } from '@earendil-works/pi-coding-agent'

/** @anchor pi-context-boundary */
export class PiContext {
  private constructor() {}

  /** @anchor pi-context-boundary */
  public static register(_pi: ExtensionAPI): never {
    throw new Error('TODO: Pi 官方 Skills 发现与工具结果上下文登记')
  }
}
