import { Host } from '@toolkit/host'
import { FrontendProcess, FrontendComponent } from './FrontendProcess.ts'
import { HttpMcp } from '@toolkit/mcp/http'
import type { Logger } from 'pino'

/** @anchor vscode-host-bootstrap */
export class VsCodeHost {
  private constructor(
    public readonly host: Host,
    private readonly frontend: FrontendProcess,
    public readonly component: FrontendComponent,
    private readonly mcp: HttpMcp,
    public readonly endpoint: URL,
  ) {}

  /**
   * @anchor vscode-host-bootstrap
   * @anchor backend-log-injection
   * @anchor deployment-lifecycle-contract
   */
  public static async open(
    workspaceFullPath: string,
    port: number,
    logger: Logger,
  ): Promise<VsCodeHost> {
    const frontend: FrontendProcess = await FrontendProcess.open()
    try {
      const host: Host = await Host.open(workspaceFullPath, 'files', logger)
      try {
        const component: FrontendComponent = await frontend.openComponent(host)
        try {
          const mcp: HttpMcp = await HttpMcp.listen(component.host(), port)
          return new VsCodeHost(host, frontend, component, mcp, mcp.endpoint())
        } catch (error: unknown) {
          await component.close()
          throw error
        }
      } catch (error: unknown) {
        await host.close()
        throw error
      }
    } catch (error: unknown) {
      await frontend.close()
      throw error
    }
  }

  /**
   * @anchor vscode-host-bootstrap
   * @anchor host-shell-logging
   * @anchor deployment-lifecycle-contract
   */
  public async close(): Promise<void> {
    try {
      await this.mcp.close()
    } finally {
      try {
        try {
          await this.component.close()
        } finally {
          await this.host.close()
        }
      } finally {
        await this.frontend.close()
      }
    }
  }
}
