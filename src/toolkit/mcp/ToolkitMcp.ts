import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js'
import { z } from 'zod'
import type { Host } from '@toolkit/host'
import type { WorkspaceStatus } from '@toolkit/host/workspace'
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js'
import type { RequestHandlerExtra } from '@modelcontextprotocol/sdk/shared/protocol.js'
import type { ServerRequest, ServerNotification } from '@modelcontextprotocol/sdk/types.js'

/** @anchor mcp-schema-result-convergence */
interface WorkspaceStatusSettings {
  readonly guid: string
  readonly name: string
  readonly mainMap: string
}

/** @anchor mcp-workspace-query */
export class ToolkitMcp {
  private constructor() {}

  /**
   * @anchor mcp-workspace-query
   * @anchor mcp-read-tool-migration
   * @anchor mcp-schema-result-convergence
   * @anchor pi-read-closure-reservation
   */
  public static create(host: Host): McpServer {
    const server: McpServer = new McpServer({ name: 'toolkit', version: '0.1.0' })
    const statusInput: z.ZodType<Record<string, never>> = z.object({})
    const statusSettings: z.ZodType<WorkspaceStatusSettings> = z.object({
      guid: z.string(),
      name: z.string(),
      mainMap: z.string(),
    })
    server.registerTool(
      'workspace_status',
      {
        title: '工程状态',
        description: '读取当前工程的身份、名称、主地图和 Host 视图模式。',
        inputSchema: statusInput,
        outputSchema: {
          viewMode: z.enum(['files', 'memory']),
          settings: statusSettings,
        },
        annotations: { readOnlyHint: true },
      },
      async (
        _arguments: Readonly<Record<string, never>>,
        extra: RequestHandlerExtra<ServerRequest, ServerNotification>,
      ): Promise<CallToolResult> => {
        const status: WorkspaceStatus = await host.runInRequest(
          { kind: 'workspace-status' },
          extra.signal,
        )
        return {
          content: [{ type: 'text', text: JSON.stringify(status) }],
          structuredContent: { ...status },
        }
      },
    )
    return server
  }
}
