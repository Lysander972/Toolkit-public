/**
 * 工程 MCP 工具名单。入口名固定，完整名由前缀接上入口名。
 * @anchor frontend-mcp-tool-catalog
 */
export class McpToolCatalog {
  public static readonly prefix: string = 'mcp__mechtoolkit__'

  public static readonly entries: readonly string[] = ['workspace_status']

  public static readonly names: readonly string[] = McpToolCatalog.entries.map(
    (entry: string): string => `${McpToolCatalog.prefix}${entry}`,
  )

  public static readonly nameSet: ReadonlySet<string> = new Set<string>(McpToolCatalog.names)

  private constructor() {}
}
