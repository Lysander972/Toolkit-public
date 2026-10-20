import { Option } from 'effect'
import type {
  SidebarRequest,
  SidebarState,
  SetupCard,
} from '../../frontend/host_client/SidebarMessages.ts'
import type { SidebarConnection } from '../../frontend/host_client/BrowserHostBridge.ts'
import { McpToolCatalog } from '../../frontend/host_client/mcpToolCatalog.ts'
import { SetupTargets } from '../../frontend/host_client/SetupTargets.ts'
import type { SetupTarget } from '../../frontend/host_client/SetupTargets.ts'
import type { CallDisplayRecord } from '../../frontend/ui/Sidebar.tsx'

/** @anchor preview-scenes */
export class PreviewScenario {
  public static readonly initial: SidebarState = {
    status: '水果切切切 · files',
    endpoint: '模拟宿主 · 无在线连接',
    version: '0.1.0',
    error: '',
    tools: McpToolCatalog.entries,
    targets: SetupTargets.forEndpoint('模拟宿主 · 无在线连接').map(
      (target: SetupTarget): SetupCard => ({
        id: target.id,
        title: target.title,
        level: target.fullReady() ? 'full' : 'basic',
      }),
    ),
    language: '未接入',
    canvas: '未接入 · PNG 和 XML 产物待接入',
    externalCalls: '模拟记录',
  }
  public static readonly calls: readonly CallDisplayRecord[] = [
    {
      id: 'status-success',
      durationMs: 12,
      tool: 'workspace_status',
      arguments: {},
      result: Option.some({ name: '水果切切切', viewMode: 'files' }),
      rewrittenText: Option.none(),
      status: 'success',
    },
    {
      id: 'status-error',
      durationMs: 3,
      tool: 'workspace_status',
      arguments: {},
      result: Option.some({ message: '演示错误：工程未打开' }),
      rewrittenText: Option.none(),
      status: 'error',
    },
  ]
  private state: SidebarState
  private listener: Option.Option<(state: SidebarState) => void> = Option.none()
  private constructor(initial: SidebarState) {
    this.state = initial
  }

  /** @anchor preview-scenes */
  public static connect(initial: SidebarState): SidebarConnection {
    const scenario: PreviewScenario = new PreviewScenario(initial)
    return {
      send: (request: SidebarRequest): void => {
        scenario.send(request)
      },
      subscribe: (listener: (state: SidebarState) => void): (() => void) => {
        scenario.listener = Option.some(listener)
        listener(scenario.state)
        return (): void => {
          scenario.listener = Option.none()
        }
      },
    }
  }

  private send(request: SidebarRequest): void {
    switch (request.action) {
      case 'ready':
        break
      case 'status':
        this.state = PreviewScenario.initial
        break
      case 'configure': {
        const targetId: SetupCard['id'] = Option.getOrThrow(request.target)
        const target: SetupCard = Option.getOrThrow(
          Option.fromNullishOr(
            this.state.targets.find((entry: SetupCard): boolean => entry.id === targetId),
          ),
        )
        this.state = { ...this.state, status: '水果切切切 · ' + target.title + ' 模拟配置完成' }
        break
      }
      case 'architecture':
        window.open('/MechToolkit/architecture/', '_blank', 'noopener')
        return
      case 'documentation':
        window.open('https://github.com/Lysander972/MechToolkit', '_blank', 'noopener')
        return
      case 'settings':
        this.state = { ...this.state, error: '模拟设置：MCP 端口 5050；页面不写入配置。' }
        break
      case 'version':
        this.state = { ...this.state, error: '模拟版本查询：当前展示版本 0.1.0。' }
        break
      case 'logs':
        this.state = { ...this.state, error: '模拟日志：请展开本页调用记录。' }
        break
    }
    Option.getOrThrow(this.listener)(this.state)
  }
}
