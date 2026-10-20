import { z } from 'zod'
import type { ToolkitConfig, ConfigSource } from '@toolkit/config'

/** @anchor config-vscode-source */
export interface VsCodeWorkspaceConfiguration {
  /** @anchor config-vscode-source */
  get(section: string): unknown
}

/**
 * @anchor config-vscode-source
 * @anchor config-schema-convergence
 */
export class VsCodeConfigSource implements ConfigSource {
  private static readonly portSchema: z.ZodType<number> = z.number().int().min(1).max(65535)
  private readonly configuration: VsCodeWorkspaceConfiguration

  /** @anchor config-vscode-source */
  public constructor(configuration: VsCodeWorkspaceConfiguration) {
    this.configuration = configuration
  }

  /**
   * @anchor config-vscode-source
   * @anchor config-schema-convergence
   */
  public read(): Promise<ToolkitConfig> {
    return Promise.resolve({
      mcpPort: VsCodeConfigSource.portSchema.parse(this.configuration.get('toolkit.mcp.port')),
    })
  }
}
