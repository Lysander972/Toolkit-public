import { once } from 'node:events'
import { Option } from 'effect'
import { WebStandardStreamableHTTPServerTransport } from '@modelcontextprotocol/sdk/server/webStandardStreamableHttp.js'
import { createAdaptorServer } from '@hono/node-server'
import { ToolkitMcp } from './ToolkitMcp.ts'
import type { ServerType } from '@hono/node-server'
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js'
import type { Host } from '@toolkit/host'

/** @anchor mcp-http-lifecycle */
export class HttpMcp {
  private constructor(private readonly server: ServerType) {}

  /** @anchor mcp-http-lifecycle */
  public static async listen(host: Host, port: number): Promise<HttpMcp> {
    const server: ServerType = createAdaptorServer({
      fetch: (request: Request): Promise<Response> =>
        HttpMcp.handle(host, request, binding.endpoint()),
    })
    const binding: HttpMcp = new HttpMcp(server)
    server.listen(port, '127.0.0.1')
    await once(server, 'listening')
    return binding
  }

  /** @anchor mcp-http-lifecycle */
  public endpoint(): URL {
    const address: Option.Option<NonNullable<ReturnType<ServerType['address']>>> =
      Option.fromNullishOr(this.server.address())
    return Option.match(address, {
      onNone: (): URL => {
        throw new Error('MCP HTTP 服务未监听 TCP 端口')
      },
      onSome: (value: NonNullable<ReturnType<ServerType['address']>>): URL => {
        if (typeof value === 'string') {
          throw new Error('MCP HTTP 服务未监听 TCP 端口')
        }
        return new URL(`http://127.0.0.1:${String(value.port)}/mcp`)
      },
    })
  }

  /** @anchor mcp-http-lifecycle */
  public async close(): Promise<void> {
    await new Promise<void>((resolve: () => void, reject: (error: Error) => void): void => {
      this.server.close((error: unknown): void => {
        if (error instanceof Error) {
          reject(error)
        } else {
          resolve()
        }
      })
    })
  }

  /** @anchor mcp-http-lifecycle */
  private static async handle(host: Host, request: Request, endpoint: URL): Promise<Response> {
    if (new URL(request.url).pathname !== endpoint.pathname) {
      return new Response(null, { status: 404 })
    }
    const server: McpServer = ToolkitMcp.create(host)
    const transport: WebStandardStreamableHTTPServerTransport =
      new WebStandardStreamableHTTPServerTransport({
        enableJsonResponse: true,
        allowedHosts: [endpoint.host],
        enableDnsRebindingProtection: true,
      })
    try {
      await server.connect(transport)
      return await transport.handleRequest(request)
    } finally {
      await server.close()
    }
  }
}
