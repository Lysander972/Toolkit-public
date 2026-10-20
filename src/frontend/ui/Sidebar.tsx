import { Component } from 'react'
import type { ReactNode, ChangeEvent } from 'react'
import { Option } from 'effect'
import { SidebarMessages } from '../host_client/SidebarMessages.ts'
import type { SidebarState, SetupCard } from '../host_client/SidebarMessages.ts'
import type { SetupTargetId } from '../host_client/SetupTargets.ts'
import type { SidebarConnection } from '../host_client/BrowserHostBridge.ts'

/**
 * @anchor frontend-readonly-surface
 */
export interface SidebarProps {
  readonly send: (action: string, target: Option.Option<SetupTargetId>) => void
  readonly calls: readonly CallDisplayRecord[]
  readonly bridge: SidebarConnection
}

/** @anchor frontend-logging */
type CallDisplayStatus = 'running' | 'success' | 'error'

/** @anchor frontend-logging */
export interface CallDisplayRecord {
  readonly id: string
  readonly durationMs: number
  readonly tool: string
  readonly arguments: unknown
  readonly result: Option.Option<unknown>
  readonly rewrittenText: Option.Option<string>
  readonly status: CallDisplayStatus
}

/** @anchor frontend-readonly-surface */
export interface SidebarViewState {
  readonly status: SidebarState
  readonly filter: string
}

/** @anchor frontend-readonly-surface */
export class Sidebar extends Component<SidebarProps, SidebarViewState> {
  public override readonly state: SidebarViewState = {
    status: {
      status: '等待工程',
      endpoint: '',
      version: '',
      error: '',
      tools: [],
      targets: [],
      language: '未接入',
      canvas: '未接入',
      externalCalls: '未接入',
    },
    filter: '',
  }
  private unsubscribe: Option.Option<() => void> = Option.none()

  /** @anchor frontend-readonly-surface */
  public override componentDidMount(): void {
    this.unsubscribe = Option.some(
      this.props.bridge.subscribe((state: SidebarState): void => {
        this.setState({ status: SidebarMessages.stateSchema.parse(state) })
      }),
    )
    this.props.send('ready', Option.none())
  }

  /** @anchor frontend-readonly-surface */
  public override componentWillUnmount(): void {
    Option.getOrThrow(this.unsubscribe)()
    this.unsubscribe = Option.none()
  }

  /** @anchor frontend-readonly-surface */
  public override render(): ReactNode {
    const status: SidebarState = this.state.status
    return (
      <main className="tk-sidebar">
        <header className="tk-masthead">
          <div className="tk-brand">
            <h1>MechToolkit</h1>
            <span className="tk-version">{status.version}</span>
          </div>
          <button
            className="tk-architecture"
            aria-label="架构图"
            onClick={(): void => {
              this.props.send('architecture', Option.none())
            }}
          >
            ◈
          </button>
        </header>
        <section className="tk-workspace" aria-labelledby="tk-workspace-heading">
          <div className="tk-section-heading">
            <h2 id="tk-workspace-heading">工程状态</h2>
            <button
              className="tk-refresh"
              onClick={(): void => {
                this.props.send('status', Option.none())
              }}
            >
              刷新
            </button>
          </div>
          <p className="tk-project-name">{status.status}</p>
          {status.endpoint !== '' && <code className="tk-endpoint">{status.endpoint}</code>}
          <dl className="tk-capabilities">
            <div>
              <dt>语言服务</dt>
              <dd>{status.language}</dd>
            </div>
            <div>
              <dt>Canvas</dt>
              <dd>{status.canvas}</dd>
            </div>
            <div>
              <dt>外部调用</dt>
              <dd>{status.externalCalls}</dd>
            </div>
          </dl>
        </section>
        {status.error !== '' && (
          <p className="tk-error" role="alert">
            {status.error}
          </p>
        )}
        <section className="tk-setup" aria-labelledby="tk-setup-heading">
          <div className="tk-section-heading">
            <h2 id="tk-setup-heading">一键配置</h2>
            <button
              onClick={(): void => {
                this.props.send('settings', Option.none())
              }}
            >
              扩展设置
            </button>
          </div>
          <p className="tk-section-description">选择 Agent，配置 Skills 与 MCP。</p>
          <div className="tk-targets">
            {status.targets.map((target: SetupCard): ReactNode => (
              <button
                key={target.id}
                onClick={(): void => {
                  this.props.send('configure', Option.some(target.id))
                }}
              >
                <strong>{target.title}</strong>
                <small className="tk-level">{target.level}</small>
              </button>
            ))}
          </div>
          {status.targets.length === 0 && (
            <p className="tk-empty">打开 CreatAI 工程后可配置 Agent。</p>
          )}
          <p className="tk-setup-note">
            当前提供基础级配置；部分 Agent 的安装接入待完成。完整级防护与上下文注入未接入。
          </p>
        </section>
        <section className="tk-tools" aria-labelledby="tk-tools-heading">
          <div className="tk-section-heading">
            <h2 id="tk-tools-heading">MCP 工具</h2>
            <span className="tk-count" aria-label={`${String(status.tools.length)} 个工具`}>
              {status.tools.length}
            </span>
          </div>
          <ul className="tk-tool-list">
            {status.tools.map((tool: string): ReactNode => (
              <li key={tool}>
                <code>{tool}</code>
              </li>
            ))}
          </ul>
          {status.tools.length === 0 && <p className="tk-empty">当前工程暂无可用工具。</p>}
        </section>
        <details className="tk-call-log">
          <summary className="tk-log-heading">
            <span>调用记录（{this.props.calls.length}）</span>
            <span className="tk-log-hint" aria-hidden="true">
              展开
            </span>
          </summary>
          <p className="tk-section-description">外部 MCP 调用事件：{status.externalCalls}。</p>
          <label className="tk-filter">
            筛选工具
            <input
              type="search"
              placeholder="输入工具名称"
              value={this.state.filter}
              onChange={(event: ChangeEvent<HTMLInputElement>): void => {
                this.setState({ filter: event.target.value })
              }}
            />
          </label>
          {this.props.calls.length === 0 && <p className="tk-empty">暂无调用记录。</p>}
          {this.props.calls
            .filter((call: CallDisplayRecord): boolean => call.tool.includes(this.state.filter))
            .toReversed()
            .map((call: CallDisplayRecord): ReactNode => (
              <CallDetails key={call.id} record={call} />
            ))}
        </details>
        <footer className="tk-utilities" aria-label="版本、文档与日志">
          <button
            onClick={(): void => {
              this.props.send('documentation', Option.none())
            }}
          >
            开发指南
          </button>
          <button
            onClick={(): void => {
              this.props.send('version', Option.none())
            }}
          >
            检查版本
          </button>
          <button
            onClick={(): void => {
              this.props.send('logs', Option.none())
            }}
          >
            查看运行日志
          </button>
        </footer>
      </main>
    )
  }
}

/** @anchor frontend-logging */
interface CallDetailsProps {
  readonly record: CallDisplayRecord
}

/** @anchor frontend-logging */
interface CallDetailsState {
  readonly expanded: boolean
}

/** @anchor frontend-logging */
class CallDetails extends Component<CallDetailsProps, CallDetailsState> {
  private readonly componentRole: string = 'call-details'
  public override readonly state: CallDetailsState = { expanded: false }

  private toggle(expanded: boolean): void {
    this.setState({ expanded })
  }

  /** @anchor frontend-logging */
  public override render(): ReactNode {
    const record: CallDisplayRecord = this.props.record
    const resultValue: unknown = Option.getOrNull(record.result)
    const statusLabel: string =
      record.status === 'success' ? '成功' : record.status === 'error' ? '失败' : '进行中'
    return (
      <details
        className="tk-call"
        data-component={this.componentRole}
        data-status={record.status}
        onToggle={(event: React.ToggleEvent<HTMLDetailsElement>): void => {
          const target: HTMLDetailsElement = event.currentTarget
          const expanded: boolean = target.open
          this.toggle(expanded)
        }}
      >
        <summary className="tk-call-heading">
          <code className="tk-call-tool">{record.tool}</code>
          <span className="tk-call-status">{statusLabel}</span>
          <span className="tk-call-duration">{record.durationMs} ms</span>
        </summary>
        {this.state.expanded && (
          <div className="tk-call-body">
            <h3>参数</h3>
            <pre>{JSON.stringify(record.arguments, null, 2)}</pre>
            <h3>{record.status === 'error' ? '错误' : '结果'}</h3>
            <pre>{JSON.stringify(resultValue, null, 2)}</pre>
            {Option.isSome(record.rewrittenText) && (
              <>
                <h3>改写文本</h3>
                <pre>{record.rewrittenText.value}</pre>
              </>
            )}
          </div>
        )}
      </details>
    )
  }
}
