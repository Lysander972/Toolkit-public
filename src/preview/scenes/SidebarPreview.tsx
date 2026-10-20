import { Component } from 'react'
import type { ReactNode } from 'react'
import { Option } from 'effect'
import { BrowserHostBridge } from '../../frontend/host_client/BrowserHostBridge.ts'
import type { SidebarConnection } from '../../frontend/host_client/BrowserHostBridge.ts'
import { SidebarMessages } from '../../frontend/host_client/SidebarMessages.ts'
import type { SidebarRequest } from '../../frontend/host_client/SidebarMessages.ts'
import { Sidebar } from '../../frontend/ui/Sidebar.tsx'
import type { CallDisplayRecord } from '../../frontend/ui/Sidebar.tsx'

/** @anchor preview-scenes */
export interface SidebarPreviewProps {
  readonly calls: readonly CallDisplayRecord[]
}

/** @anchor preview-scenes */
export class SidebarPreview extends Component<SidebarPreviewProps> {
  private readonly bridge: SidebarConnection = BrowserHostBridge.connect()
  /** @anchor preview-scenes */
  public override render(): ReactNode {
    return (
      <div className="preview-context" data-testid="preview-context">
        <aside className="preview-activity" aria-label="插件侧栏上下文">
          ◈
        </aside>
        <div className="preview-panel">
          <p className="preview-label">模拟宿主数据 · 正式产品 UI · 刷新恢复初始场景</p>
          <Sidebar
            bridge={this.bridge}
            calls={this.props.calls}
            send={(action: string, target: SidebarRequest['target']): void => {
              this.bridge.send(
                SidebarMessages.requestSchema.parse({ action, target: Option.getOrNull(target) }),
              )
            }}
          />
          <section className="preview-canvases">
            <h2>真实 Canvas</h2>
            <p>CPU 预览待接入，生成后展示 PNG 和完整 XML 布局文本。</p>
            <details>
              <summary>菜单 Canvas</summary>
              <p role="status">菜单 Canvas：PNG 和 XML 产物待接入。</p>
            </details>
            <details>
              <summary>结算 Canvas</summary>
              <p role="status">结算 Canvas：PNG 和 XML 产物待接入。</p>
            </details>
          </section>
        </div>
      </div>
    )
  }
}
