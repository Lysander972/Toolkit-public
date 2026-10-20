import type { ToolkitConfig, ConfigSource } from './ConfigSource.ts'

/**
 * @anchor config-pi-source-placeholder
 * @anchor config-schema-convergence
 */
export class PiConfigSource implements ConfigSource {
  // TODO: Pi Agent 插件的配置来源待其入口接入时实现，当前保持显式占位。
  /** @anchor config-pi-source-placeholder */
  private constructor() {}

  /** @anchor config-pi-source-placeholder */
  public static create(): PiConfigSource {
    return new PiConfigSource()
  }

  /**
   * @anchor config-pi-source-placeholder
   * @anchor config-schema-convergence
   */
  public read(): Promise<ToolkitConfig> {
    return Promise.reject(
      new Error('TODO: PiConfigSource 尚未实现，待 Pi Agent 插件配置来源接入后提供读取'),
    )
  }
}
