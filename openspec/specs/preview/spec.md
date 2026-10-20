# preview Specification

## Purpose

规定正式产品 UI 的公开交互场景、Canvas 的 CPU 图片与 XML 文本交付、最小资源集合和同版本静态站点发布。展示层消费已经生成的产物，Canvas 布局与绘制由统一进程内渲染链承担。

## Requirements

### Requirement: CPU 图片预览链

预览 MUST 调用统一 CPU Canvas 渲染入口生成 PNG，由 Host 将图片结果与完整 HTML/XML 文本分别交给展示层。展示页面可以使用浏览器显示已经生成的图片和文本，但不得执行 Canvas 布局、DOM 渲染、Playwright 截图或 Chromium 作为渲染依赖。Preview 接收具名请求中的 `HtmlViewEntity`，只消费其唯一 `CanvasDocument` 和实体资源，不反向依赖 Views 调度服务。viewport、固定字体、显式时间与组合根注入后端遵循 toolkit-runtime 的预览业务合同。

用户要求：“html应该可以直接用户手动开 也可以内部渲染出图”“静态动态均可 但是插件前端的预览当中应该仍然保留树形视图和选中高亮”。完整 HTML MUST 支持本地直接打开并显示 Canvas；内部出图使用同一文档与资源，经 CPU renderer 生成图片。插件的树形视图与选中高亮遵循 vscode-frontend 的「Canvas 展示边界」。

选型采用 CPU 图片展示，浏览器原生图片元素负责显示，完整 HTML 保留唯一 BeCanvas 子树用于格式往返。展示装配负责交付本地可访问的图片及完整页面，纯格式转换不读取资源或执行绘制。复用唯一 CPU 布局与绘制，避免浏览器和内部维护两套引擎；取舍是图片表示指定时间的静态画面，插件树选择和高亮由独立界面状态承担。图片封装不改变 Canvas 持久化语义。

阶段二只声明完整请求与结果端口，实体资源准备、渲染调用及缓存保持 TODO 明确失败。必要性与取舍：复用 HtmlViewEntity、CanvasRenderResult 和 Effect 的实体 Layer，预览只承担请求到实际资源的业务连接；另建页面渲染、解析或监听会重复现有职责。真实 Layer 与两种 Host 模式验证失败、输入保真、关闭后拒绝调用及工程零写入；PNG 业务效果待正式实现验收。

#### Scenario: 预览输入输出边界

- **WHEN** Host 请求 Canvas 预览
- **THEN** Preview 从实体快照取得输入，调用 CPU renderer 返回 PNG、尺寸和诊断；展示层只显示结果

<!-- @anchor id=preview-cpu-render-boundary -->
<!-- @architecture id=toolkit.preview -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=preview-cpu-render-boundary level=M -->

### Requirement: 正式 UI 与有限宿主场景

预览 MUST 复用现役 React 侧栏、消息合同与样式。正式插件经浏览器桥接发送动作并订阅状态，订阅 MUST 在组件卸载时释放。Storybook MUST 通过 `sb.mock` 替换同一桥接依赖，以 args 提供调用记录，以场景初始化提供工程状态和固定动作结果；UI 自身的筛选与展开 MUST 使用正式实现。场景状态只存在于实例内存，刷新 MUST 回到起点。展示 MUST 标明模拟数据，未接入能力 MUST 保留现役状态。

原生模块 mock 只负责装配，不能承担项目固定工程状态与动作结果；场景实现的职责限于既有消息合同的有限结果及订阅生命周期，不模拟文件系统、VS Code API 或 Host 业务。配置、筛选、展开、刷新与卸载由行为测试验证。

#### Scenario: 固定工程场景

- **WHEN** 访客加载插件面板并执行配置、筛选和展开
- **THEN** 展示水果切切切的固定状态与模拟结果，筛选和展开由正式 UI 执行，刷新恢复初始状态

<!-- @anchor id=preview-scenes -->
<!-- @architecture id=preview.scenes -->
<!-- @architecture id=preview.tests -->
<!-- @budget id=preview-scenes level=M -->

### Requirement: 真实 Canvas 与最小资源输出

构建 MUST 从只读测试工程的 UI_Menu.canvas 和 UI_GameOver.canvas 经 CanvasConverter 取得 CanvasDocument 与 XML 文本，再由 CPU renderer 生成 PNG。资源索引接受平台 Hjson 字符串 GUID 及带 guid 的元数据对象，格式遵循 Hjson 规范。只提供实际引用资源与颜色；未命中使构建失败。选定工程图片为测试占位资源，公开页面标注原图待补齐，不能以占位图通过声称真实图片验收。结算标题保留空文本，不模拟游戏运行时填充。

阶段二资源准备产出唯一 CanvasRenderInput、XML 文本与产物相对名称；复用正式转换、资源字节、固定字体和调色板合同。`build:preview:assets` 是显式 CPU 产物入口，渲染接入保持 TODO 明确失败。`build:preview` 独立构建静态站点，主页与侧栏在 CPU 产物尚未生成时显示“PNG 和 XML 产物待接入”；不嵌入原始布局页面或加载渲染脚本。后续生成的 PNG 与 XML 共用一组产物，不由展示页面补算。

颜色表 MUST 根据工程 `paletteExcel` 指向的资源，使用 read-excel-file 读取第一工作表；首行按 `name`、`color` 列定位，第 4 行起为数据，RGBA 通道为 0–255，输出保持该范围。只提供场景引用颜色。构建将文档、实际资源字节、固定字体与调色板交给 CPU renderer；输出 PNG 与 XML 文本，页面不加载渲染脚本。

Vite、Storybook、esbuild 与 XLSX 依赖承担标准构建与解析；项目准备逻辑负责结构化引用提取、资源解析及最小输入集合。实际 Canvas 验收使用 CPU 输出像素、尺寸与资源错误。发布内容仅限通过静态产物白名单验证的站点文件。

#### Scenario: 两个真实 Canvas 共用产物

- **WHEN** 显式生成 Canvas 预览产物或构建静态展示站点
- **THEN** 资源准备保留真实文档与最小输入集合；CPU 接入完成后菜单和结算共用 PNG 与 XML，阶段二产物入口明确失败，静态页面展示待接入状态

<!-- @anchor id=preview-assets -->
<!-- @architecture id=preview.assets -->
<!-- @architecture id=preview.tests -->
<!-- @budget id=preview-assets level=L -->

### Requirement: 站点与统一入口

展示主页 MUST 使用 React + Vite，提供两个 Canvas 的 CPU 预览、成品画面轮播与工程验证入口。人工产品截图以 res/preview/ 为源点，缺失时展示 TODO；CPU Canvas 图片来自本次正式构建，与测试临时截图分开。Storybook 展示固定 UI 场景，不嵌入主页。主页与侧栏架构入口指向 /MechToolkit/architecture/。版本与构建时间来自本次构建。
架构入口 MUST 以产品总览为默认视图，并按产品子图、源码与规范证据逐级展开；开发治理、工具链规范和历史参考 MUST 保持在独立视图中，不得混入默认产品总览。

全部构建入口 MUST 在根 package.json 中声明，产物 MUST 位于顶层 `_build/preview/site/`。发布产物 MUST 通过静态产物白名单验证，源码映射与 Storybook 项目元数据 MUST 不生成。新增现役源码、原生配置与测试 MUST 登记到 LikeC4 模型并纳入统一标准检查。Storybook 原生配置与 CSF 场景文件采用工具要求的顶层配置、默认导出和场景命名导出；仅这些文件豁免 Java 风格顶层声明规则，其他类型、风格与检查约束继续执行。

预览网页只由用户手动打开。构建、测试和 CI MUST NOT 启动浏览器、自动打开页面或截图；`serve:preview` 只提供标准静态服务。HTML 本地展示、预览站点展示和内部 CPU 出图分别验收，共用绘制结果不等于三种使用方式已经全部通过。

#### Scenario: 同一部署基址

- **WHEN** 访客打开主页或模拟侧栏的展示入口
- **THEN** Canvas、截图、轮播和工程验证地址均从 `/MechToolkit/` 解析

<!-- @anchor id=preview-site -->
<!-- @architecture id=preview.site -->
<!-- @budget id=preview-site level=M -->

### Requirement: 统一滚动叙事动效

主页所有叙事模块 MUST 使用同一条原生 CSS `view-timeline` 动效轨道。桌面展示舞台 MUST 固定于同一视口位置，文档滚动只推进时间轴，不能带动舞台上下移动。向下滚动时，下一组内容 MUST 从声明的左侧或右侧淡入，并附带最多 14px 的向下偏移；当前内容 MUST 原位渐隐并附带最多 14px 的向上偏移，以形成轻微运动感。左右切面共享 72px 水平距离与相同透明度关键帧。向上滚动 MUST 由同一时间轴自然反向播放，禁止按滚动方向维护独立状态或为单个模块复制动画实现。小屏保持内容自然排版；`prefers-reduced-motion` 或不支持原生时间轴时，内容 MUST 保持静态可见。

#### Scenario: 叙事轨道自然倒放

双栏模块 MUST 使用等宽两栏和统一间距，左侧说明与右侧展示分别从各自一侧入场；Canvas 与成功率页遵循同一布局。成品画面为单体模块，从左侧入场。首屏主页与日志展示框 MUST 同尺寸，主页覆盖日志。导航锚点 MUST 指向文档中的轨道，不得指向固定舞台内容。

- **WHEN** 访客向下滚动经过两个连续模块，再向上滚动返回
- **THEN** 舞台位置保持固定，前一模块轻抬渐隐、后一模块从左右下方小幅淡入，并沿同一时间轴自然倒放

<!-- @anchor id=preview-motion -->
<!-- @architecture id=preview.site -->
<!-- @architecture id=preview.tests -->
<!-- @budget id=preview-motion level=M -->

### Requirement: 预览产物验收与发布

根 `pnpm test` MUST 验证 CPU renderer 的 PNG 尺寸、像素和诊断，并验证站点只展示已生成图片与文本。不得以浏览器截图、Playwright、Chromium 或 DOM/CSS 渲染通过验收。发布站点可由标准静态构建生成，发布前检查产物白名单和资源引用。

阶段二正式测试验证 CPU 空壳明确失败及输入保真、资源准备、Vitest/happy-dom 下的正式 UI 交互与待接入状态；PNG 业务验收随 CPU 实现接入。happy-dom 的行为断言覆盖状态、动作、订阅释放与语义 DOM，不作为 CSS 几何、动效像素或 PNG 效果证据。站点白名单由 `vitest.config.mjs` 这一套件执行。`pnpm test` 与 `test:coverage` 都先构建预览，再按该配置校验同次构建的静态产物；`test:coverage` 只比 `pnpm test` 多官方 `--coverage`。

正式接入时 MUST 按以下使用方式建立行为测试，独立检查展示与绘制；阶段二未实现项保持目标合同，不登记为已通过或跳过的测试。

| 使用方式 | 自动化行为证据 | 必须捕获的代表性错误 |
| --- | --- | --- |
| 本地手动打开 HTML | 结构化解析实际生成的完整 HTML，按 file URL 解析并读取图片内容；解码 PNG，核对尺寸、独立预期像素及本次 CPU 输出；反向转换字节与源文档一致 | 本地图片无法读取、显示旧图、展示壳污染持久化数据 |
| 内部渲染出图 | 完整 HTML 经正式转换取得文档，使用真实资源、字体和显式时间调用唯一 CPU 入口；比较输出 PNG 的尺寸、像素、诊断与同次 HTML 展示图片 | 内部出图启动浏览器、读取展示壳作为布局、忽略编辑后的 XML、资源或时间不一致 |
| 手动打开预览站点 | 检查同次构建的页面及图片/XML引用完整性，组件测试验证切换、读取和刷新；页面消费与独立 HTML 相同的 PNG 字节 | 页面引用不存在的产物、部署基址错误、展示端重新计算布局、切换后展示错误文档 |

插件 Canvas 面板另须保留树形视图与选中高亮，消费同次 PNG 与节点几何；具体交互及测试遵循 vscode-frontend 的「Canvas 展示边界」。本地 HTML 的静态快照和内部 PNG 输出不取代插件的这两项能力。

自动化覆盖 HTML 本地资源可读性、CPU 像素、站点行为及插件树选择与高亮；happy-dom 只验证组件行为，不承担 CSS 几何和动效像素验证。CSS 几何与动效验收须使用能够计算标准 CSS 布局和时间轴的环境；本阶段不以状态文字或配置形状替代这些行为证据，也不启动浏览器。

测试资源在成功和失败路径均释放。资源行为验证覆盖两种 GUID 元数据、未命中资源与颜色、格式白名单、最小集合及 XLSX 通道解析。CI 在 main push 上传站点前 MUST 成功执行 CPU 产物入口；该入口 TODO 失败时不上传或发布站点。发布 job 依赖代码检查和正式审计成功，仅在 main push 向公开 MechToolkit gh-pages 单向发布并串行执行。PR、失败与取消不发布，发布历史保持。

#### Scenario: 成功受检版本发布

- **WHEN** main 的代码检查、CPU 产物验证与审计全部通过
- **THEN** 发布同一次运行的整套产物；失败和取消不改变线上站点

<!-- @anchor id=preview-browser -->
<!-- @architecture id=preview.tests -->
<!-- @architecture id=repository.build -->
<!-- @budget id=preview-browser level=L -->
