/** @anchor config-source-readonly */
export interface ToolkitConfig {
  readonly mcpPort: number
}

/**
 * @anchor config-source-readonly
 * @anchor config-schema-convergence
 */
export interface ConfigSource {
  /** @anchor config-source-readonly */
  read(): Promise<ToolkitConfig>
}
