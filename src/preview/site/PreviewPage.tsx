import { Component } from 'react'
import type { ReactNode } from 'react'
import { MotionTrack } from './MotionTrack.tsx'
import { ProductCarousel } from './ProductCarousel.tsx'

type CanvasTabId = 'menu' | 'gameOver'

interface PreviewPageState {
  readonly canvasTab: CanvasTabId
}

interface ToolSuccessRate {
  readonly name: string
  readonly value: string
}

/** @anchor preview-site */
export class PreviewPage extends Component<object, PreviewPageState> {
  private static readonly version: string = 'development'
  private static readonly builtAt: string = '本地开发'
  private readonly pageRole: string = 'preview-site'
  public override readonly state: PreviewPageState = { canvasTab: 'menu' }

  private switchCanvasTab(canvasTab: CanvasTabId): void {
    this.setState({ canvasTab })
  }

  /** @anchor preview-site */
  public override render(): ReactNode {
    const menuActive: boolean = this.state.canvasTab === 'menu'
    return (
      <main className="site" data-page={this.pageRole}>
        <header className="topbar">
          <a href="#product" className="brand">
            MechToolkit
          </a>
          <nav>
            <a href="#method">方法</a>
            <a href="#proof">眼见为实</a>
            <a href="#stats">数据</a>
            <a href="#verify">验证</a>
          </nav>
        </header>

        <MotionTrack sectionId="product" trackId="hero" className="page-hero">
          <section className="stage stage-hero">
            <div className="hero-copy motion-content motion-from-left">
              <h1>语法即约束</h1>
              <p className="hero-sub">让格式适应 LLM，而不是让 LLM 适应格式。</p>
              <p>
                MechToolkit 服务于 CreatAI 平台，让通用大模型在平台 DSL
                上获得稳定可用的编辑能力：可编辑逻辑脚本，理解平台独有特性，并实现完整玩法。
              </p>
              <div className="hero-actions">
                <a className="primary" href="#method">
                  了解方法
                </a>
                <a className="ghost" href="#proof">
                  眼见为实
                </a>
              </div>
              <dl className="hero-meta">
                <div>
                  <dt>版本</dt>
                  <dd>{PreviewPage.version}</dd>
                </div>
                <div>
                  <dt>构建时间</dt>
                  <dd>{PreviewPage.builtAt}</dd>
                </div>
              </dl>
            </div>
            <div className="hero-previews motion-content motion-from-right">
              <figure className="exhibit hero-card hero-card-main">
                <div className="chrome">
                  <span className="chrome-title">主页状态</span>
                  <span className="badge">截图待补</span>
                </div>
                <div className="placeholder-body hero-placeholder">
                  <p>主页截图待补（正式资源）。</p>
                </div>
              </figure>
              <figure className="exhibit hero-card hero-card-log">
                <div className="chrome">
                  <span className="chrome-title">日志状态</span>
                  <span className="badge">截图待补</span>
                </div>
                <div className="placeholder-body hero-placeholder">
                  <p>日志截图待补（正式资源）。</p>
                </div>
              </figure>
            </div>
          </section>
        </MotionTrack>

        <MotionTrack sectionId="method" trackId="method" className="page-track">
          <section className="stage stage-split">
            <div className="pane-left motion-content motion-from-left">
              <h2>换一种思路</h2>
              <p>
                代码模型的语法流利度，本质是训练语料出现频次的函数——
                <a href="https://arxiv.org/abs/2208.08227">MultiPL-E</a>{' '}
                按语言流行度分层后，低资源语言的表现随之塌陷。平台的 BeScript 与二进制 Canvas
                对模型而言语料为零：它不会报错，而是“插值”——挪用相近的语法、编造看似合理的关键字，输出流畅自信，平台却不认识。
              </p>
              <dl className="spec">
                <div className="spec-row">
                  <dt>语义，嵌入模型熟悉的语法</dt>
                  <dd>
                    地图与机械映射为 namespace，零件映射为 class，事件映射为固定区段，平台规则由 TS
                    语法特性承载。
                  </dd>
                </div>
                <div className="spec-row">
                  <dt>判定，交给工具而非提示词</dt>
                  <dd>非法写法在工具层变成带位置的明确诊断，回流给模型对齐每一处同步修改。</dd>
                </div>
                <div className="spec-row">
                  <dt>结果，用眼睛确认</dt>
                  <dd>CPU 预览接入后，可直接核对生成的图片与布局文本。</dd>
                </div>
              </dl>
            </div>
            <div className="pane-right motion-content motion-from-right">
              <figure className="exhibit method-exhibit">
                <div className="chrome">
                  <span className="chrome-title">VS Code 中的 TS 视图</span>
                  <span className="badge">待补资源</span>
                </div>
                <div className="placeholder-body">
                  <p>TS 视图截图待补（静态资源）。</p>
                </div>
              </figure>
            </div>
          </section>
        </MotionTrack>

        <MotionTrack sectionId="proof" trackId="visual-proof" className="block motion-section">
          <section className="stage stage-split">
            <div className="pane-left motion-content motion-from-left">
              <h2>眼见为实</h2>
              <p>Canvas 预览展示 CPU 生成的 PNG 和完整 XML 布局文本。</p>
            </div>
            <figure className="exhibit exhibit-tall motion-content motion-from-right">
              <div className="chrome">
                <span className="tabs" role="tablist" aria-label="Canvas 页面">
                  <button
                    type="button"
                    role="tab"
                    id="canvas-tab-menu"
                    aria-controls="canvas-panel-menu"
                    aria-selected={menuActive}
                    className={menuActive ? 'tab tab-active' : 'tab'}
                    onClick={(): void => {
                      this.switchCanvasTab('menu')
                    }}
                  >
                    菜单
                  </button>
                  <button
                    type="button"
                    role="tab"
                    id="canvas-tab-game-over"
                    aria-controls="canvas-panel-game-over"
                    aria-selected={!menuActive}
                    className={menuActive ? 'tab' : 'tab tab-active'}
                    onClick={(): void => {
                      this.switchCanvasTab('gameOver')
                    }}
                  >
                    结算
                  </button>
                </span>
                <span className="badge">CPU 预览待接入</span>
              </div>
              <div className="fit-frame fit-canvas canvas-stage">
                <section
                  id="canvas-panel-menu"
                  role="tabpanel"
                  aria-labelledby="canvas-tab-menu"
                  hidden={!menuActive}
                >
                  <p role="status">菜单 Canvas：PNG 和 XML 产物待接入。</p>
                </section>
                <section
                  id="canvas-panel-game-over"
                  role="tabpanel"
                  aria-labelledby="canvas-tab-game-over"
                  hidden={menuActive}
                >
                  <p role="status">结算 Canvas：PNG 和 XML 产物待接入。</p>
                </section>
              </div>
              <div className="exhibit-foot">
                <span>菜单截图待补（正式资源）。</span>
                <span>结算截图待补（正式资源）。</span>
              </div>
            </figure>
          </section>
        </MotionTrack>

        <MotionTrack sectionId="stats" trackId="success-rate" className="block motion-section">
          <section className="stage-stat">
            <div className="stat-grid">
              <div className="stat-side motion-content motion-from-left">
                <h2>真实任务中的成功率</h2>
                <p>
                  数字来自一次真实游戏开发过程的 MCP
                  调用日志，包含模型失败后反复试错的负面案例，未经筛选。
                </p>
                <p className="stat-total">
                  <strong>86%</strong> 成功 · 4402 次调用
                </p>
              </div>
              <figure className="exhibit stat-exhibit motion-content motion-from-right">
                <div className="chrome">
                  <span className="chrome-title">按工具分解</span>
                  <span className="badge">真实开发日志</span>
                </div>
                <dl className="spec stat-table">
                  {[
                    { name: 'patch_mech_view_multiedit', value: '81%' },
                    { name: 'patch_mech_view', value: '88%' },
                    { name: 'read_lint', value: '98%' },
                    { name: 'read_player_log', value: '100%' },
                    { name: 'refactor_script_delete', value: '95%' },
                    { name: 'read_sdk_docs', value: '94%' },
                  ].map((row: ToolSuccessRate): ReactNode => (
                    <div className="spec-row stat-row" key={row.name}>
                      <dt>{row.name}</dt>
                      <dd>
                        <span className="stat-num">{row.value}</span>
                        <span className="stat-bar" style={{ width: row.value }} />
                      </dd>
                    </div>
                  ))}
                </dl>
              </figure>
            </div>
          </section>
        </MotionTrack>

        <MotionTrack
          sectionId="carousel"
          trackId="product-carousel"
          className="block motion-section"
        >
          <section className="motion-content motion-from-left">
            <h2>游戏成品画面</h2>
            <p>用 MechToolkit 开发的完整玩法，运行中的真实画面。</p>
            <ProductCarousel />
          </section>
        </MotionTrack>

        <MotionTrack sectionId="verify" trackId="verified-output" className="block motion-section">
          <section className="motion-content motion-from-center">
            <h2>让产出可验证</h2>
            <p>
              模块归属、依赖方向与审计流程都服务于同一个结果：让每次修改都能被看见、被检查、被复现。
            </p>
            <a className="primary" href="/MechToolkit/architecture/">
              查看工程结构 ↗
            </a>
          </section>
        </MotionTrack>

        <footer>
          MechToolkit · <a href="https://github.com/Lysander972/MechToolkit">开发指南</a>
        </footer>
      </main>
    )
  }
}
