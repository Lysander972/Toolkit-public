import * as vscode from 'vscode'
import type { LanguageClient } from '../language/LanguageClient.ts'
import { homedir } from 'node:os'
import { join } from 'node:path'
import { Option } from 'effect'
import type { Logger } from 'pino'
import { FrontendLog } from './FrontendLog.ts'
import { VsCodeHost } from './VsCodeHost.ts'
import { VsCodeConfigSource } from './VsCodeConfigSource.ts'
import { SidebarProvider } from './SidebarProvider.ts'
import { CanvasPreviewProvider } from './CanvasPreview.ts'
import { OneClickSetup } from '../host_client/OneClickSetup.ts'
import type { ToolkitConfig } from '@toolkit/config'

/** @anchor vscode-host-bootstrap */
export class ExtensionLifecycle {
  private static session: Option.Option<VsCodeHost> = Option.none()

  private constructor() {}

  /**
   * @anchor vscode-host-bootstrap
   * @anchor host-shell-logging
   * @fallback 无工作区时以空列表保持扩展侧栏可用。
   */
  public static async activate(context: vscode.ExtensionContext): Promise<void> {
    const output: vscode.LogOutputChannel = vscode.window.createOutputChannel('MechToolkit', {
      log: true,
    })
    context.subscriptions.push(output)
    const rootLogger: Logger = new FrontendLog(output).logger()
    const logger: Logger = rootLogger.child({ component: 'extension' })
    const folders: readonly vscode.WorkspaceFolder[] = Option.getOrElse(
      Option.fromNullishOr(vscode.workspace.workspaceFolders),
      (): readonly vscode.WorkspaceFolder[] => [],
    )
    try {
      let setup: Option.Option<OneClickSetup> = Option.none()
      if (folders.length > 1) throw new Error('MechToolkit 只接受单个 CreatAI 工程')
      const workspace: Option.Option<vscode.WorkspaceFolder> = Option.fromNullishOr(folders[0])
      if (Option.isSome(workspace)) {
        const config: ToolkitConfig = await new VsCodeConfigSource(
          vscode.workspace.getConfiguration(),
        ).read()
        const workspaceFullPath: string = workspace.value.uri.fsPath
        const session: VsCodeHost = await VsCodeHost.open(
          workspaceFullPath,
          config.mcpPort,
          rootLogger,
        )
        ExtensionLifecycle.session = Option.some(session)
        setup = Option.some(
          new OneClickSetup({
            workspaceFullPath,
            homeFullPath: homedir(),
            skillSourceFullPath: join(context.extensionUri.fsPath, '../../docs/MechToolkit'),
          }),
        )
        context.subscriptions.push(
          vscode.window.registerCustomEditorProvider(
            'toolkit.canvasPreview',
            new CanvasPreviewProvider(session.host),
          ),
        )
        const languageClient: Option.Option<Awaited<ReturnType<typeof LanguageClient.connect>>> =
          Option.none()
        if (Option.isNone(languageClient)) {
          logger.info('标准只读 LSP 待接入：等待纯函数适配库与 Host 提供 LanguageClient')
        }
      }
      const sidebar: SidebarProvider = new SidebarProvider(
        context,
        ExtensionLifecycle.session,
        setup,
        logger.child({ component: 'sidebar' }),
        output,
      )
      context.subscriptions.push(
        sidebar,
        vscode.window.registerWebviewViewProvider('toolkit.mcpLog', sidebar),
      )
      context.subscriptions.push(
        vscode.commands.registerCommand('toolkit.showLogs', (): void => {
          output.show(true)
        }),
        vscode.commands.registerCommand('toolkit.openSettings', (): Thenable<unknown> =>
          vscode.commands.executeCommand(
            'workbench.action.openSettings',
            '@ext:mechtoolkit.toolkit-bescript-suite',
          ),
        ),
        vscode.commands.registerCommand('toolkit.openDocumentation', (): Thenable<boolean> =>
          vscode.env.openExternal(vscode.Uri.parse('https://github.com/Lysander972/MechToolkit')),
        ),
      )
      logger.info('前端已激活')
    } catch (error: unknown) {
      await ExtensionLifecycle.deactivate()
      throw error
    }
  }

  /**
   * @anchor vscode-host-bootstrap
   * @anchor host-shell-logging
   */
  public static async deactivate(): Promise<void> {
    const session: Option.Option<VsCodeHost> = ExtensionLifecycle.session
    ExtensionLifecycle.session = Option.none()
    if (Option.isSome(session)) await session.value.close()
  }
}

export const activate: (context: vscode.ExtensionContext) => Promise<void> = async (
  context: vscode.ExtensionContext,
): Promise<void> => ExtensionLifecycle.activate(context)

export const deactivate: () => Promise<void> = async (): Promise<void> =>
  ExtensionLifecycle.deactivate()
