import * as vscode from 'vscode'
import type { Host } from '@toolkit/host'

/** @anchor frontend-readonly-adapter */
export class CanvasPreviewProvider implements vscode.CustomReadonlyEditorProvider {
  declare private readonly nominal: never

  /** @anchor frontend-readonly-adapter */
  public constructor(public readonly host: Host) {}

  /** @anchor vscode-canvas-display-boundary */
  public openPanel(
    _request: Parameters<Host['openPreview']>[0],
    _panel: vscode.WebviewPanel,
    _signal: AbortSignal,
  ): Promise<Awaited<ReturnType<Host['openPreview']>>> {
    return Promise.reject(new Error('TODO: 同一 Host 的 Canvas 面板展示、刷新交付与关闭释放接入'))
  }

  /** @anchor frontend-readonly-adapter */
  public openCustomDocument(uri: vscode.Uri): vscode.CustomDocument {
    return { uri, dispose: (): void => {} }
  }

  /** @anchor vscode-canvas-display-boundary */
  public resolveCustomEditor(
    _document: vscode.CustomDocument,
    panel: vscode.WebviewPanel,
  ): Promise<void> {
    const webview: vscode.Webview = panel.webview
    webview.options = {
      enableScripts: false,
      localResourceRoots: [],
    }
    webview.html =
      '<!doctype html><html lang="zh-CN"><meta charset="utf-8"><body><p>Canvas 预览未接入：等待同一 Host 交付 PNG 和 HTML/XML 文本。</p></body></html>'
    return Promise.resolve()
  }
}
