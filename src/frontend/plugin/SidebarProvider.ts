import * as vscode from 'vscode'
import { Option } from 'effect'
import { z } from 'zod'
import { gt, valid } from 'semver'
import type { Logger } from 'pino'
import { SidebarMessages } from '../host_client/SidebarMessages.ts'
import type { SidebarRequest, SidebarState, SetupCard } from '../host_client/SidebarMessages.ts'
import { SetupTargets } from '../host_client/SetupTargets.ts'
import type { SetupTarget, SetupTargetId } from '../host_client/SetupTargets.ts'
import { McpToolCatalog } from '../host_client/mcpToolCatalog.ts'
import type { OneClickSetup, SetupPlan } from '../host_client/OneClickSetup.ts'
import type { VsCodeHost } from './VsCodeHost.ts'
import type { WorkspaceStatus } from '@toolkit/host/workspace'

/** @anchor frontend-sidebar-bridge */
interface ReleaseInfo {
  readonly tag_name: string
  readonly html_url: string
}

/** @anchor frontend-sidebar-bridge */
interface ManifestInfo {
  readonly version: string
}

/** @anchor frontend-sidebar-bridge */
export class SidebarProvider implements vscode.WebviewViewProvider {
  private view: Option.Option<vscode.WebviewView> = Option.none()
  private readonly disposables: vscode.Disposable[] = []
  private readonly version: string

  /** @anchor frontend-sidebar-bridge */
  public constructor(
    private readonly context: vscode.ExtensionContext,
    private readonly session: Option.Option<VsCodeHost>,
    private readonly setup: Option.Option<OneClickSetup>,
    private readonly logger: Logger,
    private readonly output: vscode.LogOutputChannel,
  ) {
    const manifestSchema: z.ZodType<ManifestInfo> = z.object({ version: z.string() })
    this.version = manifestSchema.parse(context.extension.packageJSON).version
  }

  /** @anchor frontend-sidebar-bridge */
  public resolveWebviewView(view: vscode.WebviewView): void {
    const webview: vscode.Webview = view.webview
    this.view = Option.some(view)
    const uiRoot: vscode.Uri = vscode.Uri.joinPath(
      this.context.extensionUri,
      '../../_build/frontend/ui',
    )
    webview.options = { enableScripts: true, localResourceRoots: [uiRoot] }
    const script: vscode.Uri = view.webview.asWebviewUri(vscode.Uri.joinPath(uiRoot, 'sidebar.js'))
    const style: vscode.Uri = view.webview.asWebviewUri(vscode.Uri.joinPath(uiRoot, 'sidebar.css'))
    const html: string =
      '<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src \'none\'; style-src ' +
      webview.cspSource +
      '; script-src ' +
      webview.cspSource +
      ';"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="' +
      style.toString() +
      '"></head><body><div id="root"></div><script type="module" src="' +
      script.toString() +
      '"></script></body></html>'
    webview.html = html
    this.disposables.push(
      webview.onDidReceiveMessage((message: unknown): void => {
        void this.receive(message).catch((error: unknown): void => {
          this.logger.error({ err: error }, '前端交互失败')
          void this.publish(String(error))
        })
      }),
    )
  }

  /** @anchor frontend-sidebar-bridge */
  public dispose(): void {
    for (const disposable of this.disposables) disposable.dispose()
  }

  /** @anchor frontend-sidebar-bridge */
  private async receive(message: unknown): Promise<void> {
    const request: SidebarRequest = SidebarMessages.requestSchema.parse(message)
    switch (request.action) {
      case 'ready':
        await this.publish('')
        break
      case 'status':
        await this.publish('')
        break
      case 'configure':
        await this.configure(Option.getOrThrow(request.target))
        break
      case 'logs':
        this.output.show(true)
        break
      case 'settings':
        await vscode.commands.executeCommand(
          'workbench.action.openSettings',
          '@ext:mechtoolkit.toolkit-bescript-suite',
        )
        break
      case 'documentation':
        await vscode.env.openExternal(
          vscode.Uri.parse('https://github.com/Lysander972/MechToolkit'),
        )
        break
      case 'version':
        await this.versionCheck()
        break
      case 'architecture':
        await vscode.env.openExternal(
          vscode.Uri.parse('https://lysander972.github.io/MechToolkit/architecture/'),
        )
        break
    }
  }

  /**
   * @anchor frontend-sidebar-bridge
   * @fallback 未打开工程时展示等待状态。
   * @fallback 未打开工程时不声明可用工具。
   * @fallback 未打开工程时不生成配置目标。
   */
  private async publish(error: string): Promise<void> {
    let status: string = '请打开包含 setting.json 的 CreatAI 工程'
    let endpoint: string = ''
    if (Option.isSome(this.session)) {
      const session: VsCodeHost = this.session.value
      const result: WorkspaceStatus = await session.component.runInRequest(
        { kind: 'workspace-status' },
        new AbortController().signal,
      )
      status = result.settings.name + ' · ' + result.viewMode
      endpoint = session.endpoint.toString()
    }
    const state: SidebarState = {
      status,
      endpoint,
      version: this.version,
      error,
      tools: Option.isSome(this.session) ? McpToolCatalog.entries : [],
      targets: Option.isSome(this.session)
        ? SetupTargets.forEndpoint(endpoint).map((target: SetupTarget): SetupCard => ({
            id: target.id,
            title: target.title,
            level: target.fullReady() ? 'full' : 'basic',
          }))
        : [],
      language: '未接入',
      canvas: '未接入',
      // TODO: Host 调用事件尚未接通；调用事件须先完成成熟组件选型与正式 Schema 定义，再接入前端。
      externalCalls: '未接入',
    }
    if (Option.isSome(this.view)) {
      await this.view.value.webview.postMessage({ kind: 'state', payload: state })
    }
  }

  /** @anchor frontend-sidebar-bridge */
  private async configure(id: SetupTargetId): Promise<void> {
    const session: VsCodeHost = Option.getOrThrow(this.session)
    const setup: OneClickSetup = Option.getOrThrow(this.setup)
    const target: SetupTarget = Option.getOrThrow(
      Option.fromNullishOr(
        SetupTargets.forEndpoint(session.endpoint.toString()).find(
          (entry: SetupTarget): boolean => entry.id === id,
        ),
      ),
    )
    const plan: SetupPlan = await setup.plan(target)
    await setup.apply(plan)
    await this.publish('')
  }

  /** @anchor frontend-sidebar-bridge */
  private async versionCheck(): Promise<void> {
    const schema: z.ZodType<ReleaseInfo> = z.object({ tag_name: z.string(), html_url: z.url() })
    const response: Response = await fetch(
      'https://api.github.com/repos/Lysander972/MechToolkit/releases/latest',
      { signal: AbortSignal.timeout(5000) },
    )
    if (!response.ok) throw new Error('版本查询 HTTP ' + String(response.status))
    const release: ReleaseInfo = schema.parse(await response.json())
    const version: string = Option.getOrThrow(Option.fromNullishOr(valid(release.tag_name)))
    await vscode.window.showInformationMessage(
      gt(version, this.version)
        ? '新版本：' + version + ' · ' + release.html_url
        : '当前已是最新版本',
    )
  }
}
