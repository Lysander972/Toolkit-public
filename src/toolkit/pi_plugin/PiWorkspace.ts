import type { ExtensionContext } from '@earendil-works/pi-coding-agent'
import type { Host } from '@toolkit/host'
import type { HttpMcp } from '@toolkit/mcp/http'
import type { ConfigSource } from '@toolkit/config'

/** @anchor pi-workspace-boundary */
export class PiWorkspace {
  private constructor(
    public readonly host: Awaited<ReturnType<typeof Host.open>>,
    public readonly http: Awaited<ReturnType<typeof HttpMcp.listen>>,
  ) {}

  /** @anchor pi-workspace-boundary */
  public static open(
    _context: ExtensionContext,
    _config: Awaited<ReturnType<ConfigSource['read']>>,
    _logger: Parameters<typeof Host.open>[2],
  ): Promise<PiWorkspace> {
    return Promise.reject(new Error('TODO: Pi 会话专属 memory Host 与 HTTP MCP 接入'))
  }

  /** @anchor pi-workspace-boundary */
  public close(): Promise<void> {
    return Promise.reject(new Error('TODO: 注销官方 MCP 连接、关闭 HTTP 与 Host'))
  }

  /** @anchor pi-workspace-boundary */
  public endpoint(): ReturnType<HttpMcp['endpoint']> {
    throw new Error('TODO: 官方 MCP 连接使用会话 HTTP endpoint')
  }
}
