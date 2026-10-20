import { createRoot } from 'react-dom/client'
import type { Root } from 'react-dom/client'
import { Option } from 'effect'
import { Sidebar } from './Sidebar.tsx'
import type { SetupTargetId } from '../host_client/SetupTargets.ts'
import { BrowserHostBridge } from '../host_client/BrowserHostBridge.ts'
import type { SidebarConnection } from '../host_client/BrowserHostBridge.ts'
import { SidebarMessages } from '../host_client/SidebarMessages.ts'
import '../../../res/frontend/styles/tk-tokens.css'
import '../../../res/frontend/styles/sidebar.css'

/** @anchor frontend-readonly-surface */
export class SidebarBootstrap {
  private static readonly root: Root = createRoot(
    Option.getOrThrow(Option.fromNullishOr(document.querySelector('#root'))),
  )
  private constructor() {}

  // TODO: 待 Host 调用事件 Schema 固化与成熟调用日志组件选型完成后，向展示组件绑定真实调用数据。
  static {
    const bridge: SidebarConnection = BrowserHostBridge.connect()
    SidebarBootstrap.root.render(
      <Sidebar
        calls={[]}
        bridge={bridge}
        send={(action: string, target: Option.Option<SetupTargetId>): void => {
          bridge.send(
            SidebarMessages.requestSchema.parse({ action, target: Option.getOrNull(target) }),
          )
        }}
      />,
    )
  }
}
