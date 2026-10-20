# VS Code Frontend Specification

## Purpose

规定 VS Code 前端的目录职责与只读发布形态。前端容器与界面生命周期由本规范约束；后端 Host 与 MCP 传输见运行时规范。文件归属与依赖方向以 `tools/model.c4` 为准。

## Requirements

### Requirement: Canvas 展示边界

Canvas Webview MUST 展示 Host 生成的 HTML/XML 文本、PNG 图片及同次节点集合，以保留树形视图和选中高亮，并通过消息合同请求刷新。Canvas 宿主 MUST 持有扩展装配的同一 Host；输入由 Host 的 Views 取得，资源由实体层读取，PNG 经同一 Preview 与唯一 CPU renderer 生成。前端不得独立读取 Canvas 或引用资源、监听工程文件、定义二进制转 HTML 服务，或执行 Canvas 解析、布局、DOM 重建游戏画面及截图；树与高亮使用原生界面元素展示。刷新沿用 Host 主动驱动的工程刷新链，引用资源变化与 Canvas 变化使用同一实体基准。LSP/标准进程内连接及 Canvas 展示接入仅作为待办登记，未实现前不得伪装为可用能力。

展示请求只确定源实体身份、viewport、固定字体和显式时间；Host 取得视图及其完整来源基准，读取实际资源并交付文本、PNG 和最终依赖基准。CanvasPreviewProvider.openPanel MUST 接受直接复用 Host.openPreview 的具名请求、原生 WebviewPanel 和 AbortSignal，返回同一 Host 的展示资源类型。该资源的 take(signal) 交付初次内容及 Host 主动刷新，Option.None 表示源实体删除；内容直接复用 Host 的视图、PNG 和最终依赖基准。面板持有该资源，关闭时取消进行中的接入与 take，并调用 close 释放；Host 关闭取消进行中的请求。前端不接管工程监听或派生纠偏。第二步只声明接入合同，openPanel 执行明确 TODO 失败；原生编辑器继续显示未接入状态页。原生文档到源实体身份的接入、组合根的唯一 CPU 后端装配、实际文本和 PNG 展示及面板关闭绑定随正式接入实现。

必要性与取舍：Host 已提供具名展示请求和 take/close 资源合同，VS Code 已提供只读编辑器、WebviewPanel 与关闭事件，原生 AbortSignal 表达取消。前端只声明面板与同一 Host 展示资源之间的接入，不新建结果协议、读取、监听或渲染链。直接复用 Host 参数及返回类型使刷新和释放合同保持一致；阶段二以明确失败保留尚未实现的装配边界。真实 ESM 扩展回归验证接入失败、请求和面板原状、Host 可继续查询及工程零写入；实际 PNG 展示、主动刷新与关闭释放的行为随正式接入验收。

前端打开与 MCP 预览 MUST 共享 Host 的 Views → 实体资源 → Preview → CPU renderer 主干。VS Code 文档 URI 只在宿主边界定位源实体身份，不能把 URI、Webview 文件路径或文档缓存作为第二份 Canvas 内容输入；当前定位尚未接入。唯一 CanvasKit 后端由组合根装配并供 Host 使用，不由每个面板初始化另一份渲染服务。面板展示 PNG 字节的协议编码由宿主消息边界承担，HTML/XML 作为可读文本安全展示，不执行其中脚本或通过 DOM 重建布局；Webview 的图片缩放属于产物展示，不替代 viewport 对应的 Host 渲染。用户改变 viewport 或显式时间时向同一 Host 请求新产物，不在 Webview 自行重排或运行另一套动画引擎。

正式接入验收 MUST 经实际扩展打开面板：同一源、viewport、字体、时间与基准的前端图片应与 Host 产物一致；Canvas 不变而引用图片变化时收到主干的新产物，无关资源变化保持原结果；源删除显示真实删除，关闭面板撤销展示并结束等待，Host 关闭释放请求。测试必须观察实际图片、文本与资源生命周期，不用注册调用次数、页面关键词或配置形状代替这些行为。当前扩展回归仅证明同一 Host 注入、明确 TODO、未接入状态和工程零写入，不宣称上述正式展示已实现。

复用既有扩展回归使同一 Host 的装配与接入拒绝在同一生命周期验收，拆成独立单元调用无法证明正式扩展接线。

Canvas 插件预览 MUST 保留树形视图和选中高亮。树按同次 CanvasRenderResult.nodes 的索引链、名称和类型显示原始父子顺序；选择按索引链识别节点，同名节点不合并。选中树节点时，覆盖层使用该节点的 viewport 四角标示对应画面位置；PNG 与覆盖层共享相同显示变换，缩放和留白后仍对齐，父子旋转不简化为未经变换的矩形。树的展开和选择属于面板内只读状态，选择以 Option 表达，不重新渲染 PNG、不修改 Canvas 或工程。

Host 的每次结果 MUST 将 PNG 与节点集合一起替换；刷新后的选中节点与高亮必须对应当前结果，源删除时清空图片、树和高亮，面板关闭释放消息与展示资源。前端不从 PNG 猜测节点，不解析 HTML 重建布局，不在过期图片上覆盖新节点几何。树形视图属于 Canvas 面板，与侧栏单页状态区分。

必要性与取舍：设计复用 Host 的唯一渲染结果，节点索引链与最终四角随该结果提供；React 的树形语义元素、选择状态与 SVG 覆盖层负责展示。前端维护展开和选择，不复制布局引擎或工程模型。静态 PNG 可以保留树选择与高亮；第二阶段声明结果合同和验收目标，面板接入保持 TODO。

正式测试 MUST 验证嵌套树顺序、重名节点分别选择、切换选择后只有对应节点高亮、缩放留白与父子旋转后的独立预期坐标、刷新后选择与当前结果一致及删除后移除对应高亮。组件测试观察实际树选择和覆盖层几何，CPU 测试独立验证四角与像素；实际扩展回归验证同一 Host 的初次结果、刷新和关闭释放。选中前后 PNG 字节和工程内容保持相同；组件测试不冒充浏览器实际几何展示，预览网页仍只由用户手动打开。

#### Scenario: 前端展示 Host 产物

- **WHEN** Host 返回 Canvas 预览结果
- **THEN** Webview 展示返回的 PNG、文本和同次节点树；选择树节点高亮其对应位置，前端只进行展示坐标缩放

<!-- @anchor id=vscode-canvas-display-boundary -->
<!-- @architecture id=frontend.ui -->
<!-- @architecture id=frontend.language -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=frontend.plugin.canvas_preview -->
<!-- @budget id=vscode-canvas-display-boundary level=S -->

### Requirement: 单一扩展形态

VS Code 前端 MUST 只作为一份扩展放在 `src/frontend`，MUST NOT 再拆成互相独立发布的包。容器划分、职责、文件归属与依赖方向唯一以聚合架构模型为准。

源码文件通过相对 `$schema` 引用本地依赖 JSON Schema 镜像。CI MUST 对现役 JSON 执行 JSON 解析、Prettier 格式和本地 `$schema` 可读性检查；远端 SchemaStore 源地址与镜像版本由镜像文件的来源记录维护，网络不可用时不得阻断本地静态检查。

VS Code 原生入口要求模块导出 `activate` 和 `deactivate`，该边界 MUST 使用显式类型的具名常量函数，行为仍由生命周期类承担。Webview 原生注入 `acquireVsCodeApi` 仅以 ambient 声明表达。上述原生契约 MUST NOT 导致其余前端文件关闭风格规则。

取得工作区 Host 后，后续编辑器、侧栏或命令注册失败 MUST 清空当前会话并关闭该 Host，再传播激活错误；成功会话由 deactivate 关闭。复用同一生命周期释放入口，真实扩展产物回归以 Host 关闭后拒绝查询、MCP 端口可重新绑定及下一次激活成功验证释放。

扩展入口与 Webview 产物 MUST 统一为 ESM，MUST NOT 维护 CJS 发布出口。最低 VS Code 版本 MUST 为 1.100，该版本的官方扩展加载器支持 `.mjs` 与 `type: module`。第三方 CommonJS 依赖由 esbuild 打包，构建边界用 Node 原生 `createRequire`、`fileURLToPath` 和 `dirname` 提供依赖必需的 Node 模块与当前产物位置，不另建前端模块或兼容输出。

插件与 Host MUST 使用 VS Code 扩展宿主自带的 JavaScript 运行时，Canvas 渲染 MUST 由同一 Host 的进程内 CPU 渲染链完成，界面与 Canvas 产物展示使用 VS Code Webview。发布依赖 MUST 随扩展交付；需要 tsx 时使用随扩展交付的依赖。用户只需安装 VS Code 与扩展即可运行，无需安装 .NET、独立 Node.js、tsx 或 Chrome。前端不提供这些运行时的环境检测、安装入口或配置卡片。一键配置中的 Agent CLI 是用户选择的平台配置入口，独立于插件运行环境。

#### Scenario: 前端容器划分由架构模型裁定

- **WHEN** 检查前端容器划分、职责、文件归属与依赖方向
- **THEN** 以聚合架构模型为唯一依据，规范不另立第二套划分

<!-- @anchor id=frontend-single-extension -->
<!-- @nonCode -->

### Requirement: 只读发布形态

除一键配置外，扩展业务 MUST 只读。

MUST 保留诊断、语法高亮、悬停、定义与引用跳转、文档链接、符号、Canvas 预览，以及侧栏上的运行状态、工具清单、配置指引、版本和调用日志。侧栏 MUST 是单页，调用日志 MUST 在该页内展开，MUST NOT 另设工作区结构预览。

浏览器宿主桥接 MUST 统一持有消息发送与状态订阅；组件卸载释放订阅，正式插件与 Storybook 共用产品 UI。侧栏独立架构入口 MUST 打开公开站点的 architecture 页面，与展示主页入口一致。

侧栏 MUST 使用冷蓝灰与青蓝的紧凑工作台排版，支持 VS Code 原生深浅主题。顶部品牌栏显示版本与独立架构入口；工程主区域显示工程状态、连接端点、刷新操作和语言服务、Canvas、外部调用状态。配置入口 MUST 默认展开为双列，显示 Agent 名称与 basic/full 等级；MCP 工具采用紧凑清单，开发指南、版本检查与运行日志集中于底部。空工程、无工具、无记录与实际错误 MUST 清晰呈现；错误使用语义错误色，调用状态同时使用文字与状态色。窄侧栏 MUST 保持内容可读、操作可达和键盘焦点可见。

分组、状态展示与展开反馈由 React 语义元素及 CSS Grid、原生 details 和 VS Code 主题 class 承担；项目界面只组合现有状态与动作，不另建主题控制、区域折叠机制或消息协议。调用记录详情沿用原生 details 的页内展开，正式产品与 Storybook 使用同一结构和样式，行为测试覆盖动作、筛选与延迟格式化，headed 浏览器验证深浅主题、窄布局和焦点。

MUST NOT 提供手动修改、快速重构、修改性 CodeLens、可编辑派生视图、重命名、补全或格式化。

#### Scenario: 扩展只保留只读能力与配置入口

- **WHEN** 查看扩展对用户暴露的业务能力
- **THEN** 写操作只剩一键配置，其余为只读语言服务、Canvas 预览和单页状态与日志

<!-- @anchor id=frontend-readonly-surface -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=frontend.ui -->
<!-- @budget id=frontend-readonly-surface level=L -->

### Requirement: 侧栏消息桥接

浏览器桥接拥有消息发送与状态订阅，插件 SidebarProvider 拥有 Webview、状态投影与操作分派，组件只消费同一状态。订阅随所属生命周期释放，使用现有消息合同，不另建查询协议。

#### Scenario: 前端消息桥接与状态订阅

- **WHEN** 侧栏建立或销毁浏览器桥接和状态订阅
- **THEN** 消息发送、订阅释放和只读状态交付保持单一入口

<!-- @anchor id=frontend-sidebar-bridge -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=frontend.ui -->
<!-- @budget id=frontend-sidebar-bridge level=L -->

### Requirement: 标准语言客户端接入

语言查询 MUST 由标准 `vscode-languageclient` 消费标准 LSP Server，前端 MUST NOT 定义 `ReadonlyLanguageService` 等自定义查询协议、Provider 转发适配器或第二套解析结果。LSP Server 的 BEScript 适配由独立纯函数库与 Host 提供；LanguageClient.connect 接收同一 Host 和标准 LanguageClientOptions，接入层建立进程内标准消息连接并拥有客户端生命周期，不把 ServerOptions 的外部进程或工厂回调交给调用方选择。官方客户端的 ServerOptions 包含进程启动和工厂函数；该外部适配在接入层内部完成，不能扩张为内部业务回调端口。在其完成前，前端只登记 TODO 并显示“未接入”。激活生命周期 MUST 使用运行时规范中的前端容器，MUST NOT 自建第二套容器。

#### Scenario: 标准语言客户端骨架明确失败

- **WHEN** 将扩展装配的同一 Host 和标准 LanguageClientOptions 交给 LanguageClient.connect
- **THEN** 返回明确 TODO，选项及工程保持原状，客户端尚未建立，侧栏维持未接入

必要性与取舍：标准 vscode-languageclient 承担 LSP 消息与客户端生命周期，Host 已承担语言查询及工作区资源。接入层只声明这两者之间的连接合同，保留完整输入并明确失败；当前不增加自定义协议、业务回调或第二套解析和监听。骨架测试使用真实 Host 与独立选项快照验证明确失败及工程零写入；标准消息连接、实际诊断与导航及客户端释放随正式接入验收。

正式连接选用已锁定的 vscode-languageclient@9.0.1 与同代 vscode-languageserver@9.0.1 的标准消息端口。客户端内部 ServerOptions 工厂返回 MessageTransports，服务端 createConnection 接收配对的 MessageReader/MessageWriter；传输使用 Node 原生双向 PassThrough 与 SDK 的 StreamMessageReader/StreamMessageWriter。connect 创建这一对进程内端口、绑定同一 Host 后等待标准客户端 start 完成，成功交付该客户端；失败释放已创建的客户端、服务端连接及流。扩展关闭时先结束客户端请求和连接，再关闭 Host。当前只有客户端直接依赖；正式服务端接入前须把已经存在于根锁文件的服务端版本登记为直接依赖，不能借传递依赖或另起外部进程完成接入。

标准文档同步只持有打开文档的 URI、文本与版本，不是第二份工程实体状态。服务端将 URI 在边界定位成工程相对文档及源实体，准备 LanguageDocument 和完整读取基准，将标准定义、引用、悬停与诊断请求交给 Host.query → LanguageQueries → Entities/Diagnostics。取消令牌在边界关联原生 AbortSignal，Host 关闭终止请求；响应保留请求版本，过期结果不覆盖新文档。生成视图坐标按运行时的 UTF-16 映射合同交付，缺少映射不得捏造范围。文档链接、符号及语义 token 的查询和映射继续使用同一实体与纯语义输入，具体结果字段及适配执行保持 TODO；适配层不建立解析器、文件监听或业务状态树。只读发布不注册 rename、修改性 CodeAction、补全或格式化能力。

高亮采用 VS Code 官方推荐的两层机制：TextMate grammar 提供词法 scopes，LSP semanticTokens 在实体语义与来源映射可用后补充语义分类，颜色由 VS Code 主题决定。原版 .code 使用 bescript/source.bescript，工程 Hjson 使用扩展登记的 mech-hjson/source.mech-hjson；TS 和 HTML 派生视图使用 VS Code 已有的 TypeScript 与 HTML grammar，不重写通用语法。TextMate 的模式仅用于官方词法着色，不承担工程语法解析、重构或诊断。语义 token 必须来自共享 AST/Context/分析及经过验证的坐标映射，不能把 semanticTokenTypes、semanticTokenScopes 或 enabled 配置声明当作已交付 token。

当前事实：扩展激活未调用 connect，侧栏显示“未接入”；connect 明确失败，未建立 Server 或语义 token provider。BEScript grammar 的 patterns/repository 为空，尚无自定义 .code 词法高亮；Hjson grammar 已登记资源，正式着色结果仍待宿主验收。TS/HTML 的基础着色依赖宿主已有语法，不能归功于本模块的 LSP。阶段二保留这一执行边界，词法规则、语义 token 和标准消息链不以配置形状测试冒充实现。正式验收须通过实际 TextMate 引擎或 VS Code scopes 核对注释、字符串、关键字、数字、嵌入内容与多行状态，标准 LSP 核对独立预期导航/诊断、UTF-16、取消、版本失效和关闭释放；语义 token 核对分类、范围及主题叠加。

选型依据：[VS Code Syntax Highlight Guide](https://code.visualstudio.com/api/language-extensions/syntax-highlight-guide)、[Semantic Highlight Guide](https://code.visualstudio.com/api/language-extensions/semantic-highlight-guide) 以及当前锁定 SDK 的 ServerOptions、MessageTransports 与 createConnection 声明。标准 SDK 已承担消息、文档同步及协议生命周期，宿主已承担 TextMate 与主题；项目仅连接 Host 语义及来源坐标。进程内连接省去额外进程和第二份工作区状态，代价是 CPU 计算与扩展共享事件循环，需复用现有增量转换和分析并按实际热点优化，不能声称标准消息连接自动隔离 CPU 工作。

<!-- @anchor id=frontend-language-contract -->
<!-- @architecture id=frontend.language -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=frontend-language-contract level=S -->

### Requirement: LSP 与语法资源登记边界

前端语言资源 MUST 以扩展清单为唯一登记入口。BEScript 的 `.code` 文件 MUST 绑定 `bescript` 语言与 `source.bescript` grammar；工程 Hjson MUST 绑定 `mech-hjson` 语言与 `source.mech-hjson` grammar，并覆盖清单声明的固定文件名及工程目录关联。TS 与 HTML 派生视图 MUST 继续使用 VS Code 原生 grammar，不得复制通用语法资源。

`semanticTokenTypes` 与 `semanticTokenScopes` 的登记只表达将来的主题分类合同，不得作为已交付 semantic tokens 的证据。标准 LSP 连接未完成时 MUST 保持明确 TODO、不得激活客户端或注册第二套查询协议；BEScript grammar 没有实际 patterns/repository 时 MUST 保持未接入事实。Hjson grammar 已登记的词法规则可由宿主加载，但不替代 LSP 语义 token 或工程解析。

#### Scenario: 语言资源与未接入边界

- **WHEN** 读取扩展清单、语法资源和语言客户端骨架
- **THEN** `.code`、工程 Hjson、TS/HTML 的资源归属与文件范围分别符合登记；LSP 连接明确失败，BEScript 空 grammar 与 semantic token 登记不被宣称为已交付能力

<!-- @anchor id=frontend-lsp-syntax-hjson-convergence -->
<!-- @architecture id=frontend.language -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=frontend-lsp-syntax-hjson-convergence level=S -->

### Requirement: MCP 工具名单

宿主客户端 MUST 仅列举当前 Host 实际注册的 `workspace_status` 工具。每条完整名 MUST 是前缀 `mcp__mechtoolkit__` 接上对应入口名。入口名 MUST 互异，完整名 MUST 与入口名一一对应。名单只供读取，MUST NOT 在此处改写外部配置。

#### Scenario: 按前缀展开工具完整名

- **WHEN** 读取工程 MCP 工具名单
- **THEN** 入口名非空且互异，每条完整名是前缀加对应入口名，查找集合与完整名一致

<!-- @anchor id=frontend-mcp-tool-catalog -->
<!-- @architecture id=frontend.host_client -->
<!-- @budget id=frontend-mcp-tool-catalog level=S -->

### Requirement: 工程相对路径

宿主客户端接受的工程路径 MUST 是相对工程根的路径，分隔符 MUST 是 `/`。空字符串、反斜杠、绝对路径、`./` 前缀、空段、`.` 段和 `..` 段 MUST 拒绝。单独的 `.` MUST 表示工程根。把绝对路径收成相对路径时 MUST 先确认它在工程根之内，再按上述规则校验。拼文件系统绝对路径 MUST 只替换分隔符并拼接，MUST NOT 在这一步改判相对路径是否合法。

#### Scenario: 校验工程相对路径

- **WHEN** 校验或换算工程路径
- **THEN** 合法相对路径原样保留，`.` 表示工程根；绝对路径、反斜杠、空段和点段被拒绝；工程内绝对路径变成 `/` 分隔的相对路径；拼绝对路径只替换分隔符

<!-- @anchor id=frontend-workspace-path -->
<!-- @architecture id=frontend.host_client -->
<!-- @budget id=frontend-workspace-path level=S -->

### Requirement: 前端容器栈分层

前端栈 MUST 独立于后端栈，用 Effect Context、Layer 与 Scope 表达为进程级(前端)、组件级与请求级：进程级运行时在插件进程生命周期内单例；组件级容器持有 Host 整体；请求级按单次交互独立实例化。前端容器 MUST NOT 与 Host 容器相互继承。组件级容器随组件关闭释放，前端容器关闭后组件级入口 MUST 失败。进程级容器存活数量随进程级运行时建立而增加，随运行时关闭而减少。调用方通过 FrontendProcess.openComponent 取得资源，并在原生 try/finally 中调用组件 close；请求级入口接受具名输入和原生 AbortSignal，分派同一 Host 业务并返回具名结果，不接收跨模块业务回调。调用方取消、组件级或进程级 Scope 关闭 MUST 中断该请求、关闭请求上下文并触发传给 Host 的信号。前端容器释放不关闭其持有的 Host；整体 Host 生命周期由扩展组合根负责。

现役 FrontendComponent.runInRequest 的 Host 消费仅为工程状态，输入 MUST 直接复用 WorkspaceStatusRequest，结果直接复用 WorkspaceStatus。它按次建立前端请求 Scope，并调用同一 Host.runInRequest；Effect 原生信号贯穿两侧，调用方取消时同时中断绑定组件 Scope 的请求 Fiber。其他尚未实现的业务只在其所属模块建立粗略合同并明确 TODO，当前不增加通用结果包装或回调入口。一键配置由侧栏调用已存在的 OneClickSetup.plan/apply，保持迁移能力和最后一步核对范围，不包装成 Host 业务请求。

必要性与取舍：Effect 已提供 Context、Layer、父子 Scope、ManagedRuntime 和 Fiber 中断，Host 已提供具名状态请求及原生取消。项目只管理前端进程、组件资源和按次请求的生命周期，并直接复用正式 Host 输入输出；具名资源交由原生 try/finally 持有，删除不增加能力的通用组件回调。业务回调会让调用方把任意操作放入容器并分裂受控入口，具名请求使业务分派和取消归属明确，代价是调用方显式取得与释放资源。有效迁移测试验证同一 Host、共享进程身份、真实状态变化、独立组件释放，以及三种取消来源使实际 Host 读取任务中断并释放前端请求；测试以真实读取后挂起控制时序，观察实际上下文状态和底层信号，不以 mock 调用次数代替行为证据。

#### Scenario: frontend-process-layer

- **WHEN** 在插件进程内打开前端进程级容器，派生组件级容器后关闭前端容器，再次派生
- **THEN** 进程级容器在插件进程生命周期内单例，前端容器关闭后组件级入口失败，存活数量回到打开前

<!-- @anchor id=frontend-process-layer -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=frontend-process-layer level=M -->

#### Scenario: frontend-component-layer

- **WHEN** 在前端进程内为同一 Host 建立组件级容器，进入请求级入口，再关闭前端容器
- **THEN** 组件级容器持有 Host 整体且不与 Host 容器相互继承，请求级按单次交互独立实例化并在入口结束后关闭，组件级容器随组件关闭释放；进程级容器关闭时进行中的请求被中断并收到 AbortSignal

<!-- @anchor id=frontend-component-layer -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=frontend-component-layer level=M -->

### Requirement: 原生配置来源

VSCode MUST 只读取 WorkspaceConfiguration，默认值 MUST 由扩展 contributes.configuration 提供，RunningHub 凭据是可选的明文配置输入，MUST NOT 维护第二默认值表、TOML 同步、加密存储或密钥文件。端口 MUST 是 1..65535 的整数。Pi 使用运行时规范约束的独立显式参数来源；当前未实现来源拒绝读取，缺口由架构模型通报。

#### Scenario: 原生配置校验

- **WHEN** 读取 toolkit.mcp.port
- **THEN** 直接验证原生 get 返回值，缺失或类型错误失败，原生默认值 5050 可读

<!-- @anchor id=config-vscode-source -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=toolkit.config.tests -->
<!-- @budget id=config-vscode-source level=S -->

### Requirement: Agent 官方参考与版本证据

所有支持的 Agent 的官方配置、MCP、Skills、Hooks、扩展文档及已发布 Schema MUST 原样保存为架构登记的参考文档。source.json 记录官方 URL、上游提交或公开版本、核对日期及 blob/SHA-256；滚动网站未公开版本时，以核对日期和内容校验标识固定快照，不伪造版本。细节修订与验收前 MUST 核对上游新鲜度，并核对所消费能力对应版本；原始文档中的 Schema 发布地址与实际 Schema 必须对应，数据库迁移 Schema 不作为配置 Schema。参考不参与运行时配置，也不成为第二份产品协议。

GitHub 官方参考固定提交；Claude、Cursor、Factory、Trae 使用官方站点快照。Claude settings Schema 和 Cursor 1.0.0 plugin Schema 各自仅证明其声明对象，不能冒充 MCP 配置 Schema。OpenCode 与 Kilo 的配置 Schema 采用各自官方文档链接的发布端点。Codex 文档站点返回 HTTP 403，仓库说明仍指向官方页面；当前配置和 Skill 位置依据同提交的配置 Schema、MCP CLI 与 Skill root 官方源码。完整 Hooks/插件网页原文获取保持 TODO，未取得的正文不视为已核验。Trae 保存含修订元数据的官方 HTML 原文；未发现独立配置 Schema 时沿用官方字段说明，不自造官方 Schema。

Pi 复用既有固定 1.0.0 官方参考及对应 ExtensionAPI 声明，不复制基线；生命周期采用 session_start/session_shutdown。官方 registerTool 表达原生工具扩展，Toolkit 按运行时规范保持 MCP 唯一工具合同并采用 registerMcpServer；命令和文件拦截采用 tool_call，输入与上下文变换采用 input/before_agent_start/context。PiExtension 与 Host 的具体接入保持明确 TODO；Pi 不作为 HTTP 一键配置目标添加另一安装流程。

#### Scenario: 官方参考的来源与版本核对

- **WHEN** 修订平台配置合同或验收对应能力
- **THEN** 依据固定来源、版本与原文字节核对所消费能力，未取得的正文保持 TODO

<!-- @anchor id=frontend-agent-references -->
<!-- @nonCode -->

### Requirement: 一键配置目标与配置等级

唯一目标范围如下；项目位置相对工程根，用户位置相对用户目录，Skill 身份统一为 mechtoolkit：

| Agent | MCP 范围与位置 | Skill 范围与位置 | 正式入口与迁移状态 |
|---|---|---|---|
| Claude Code | 项目 .mcp.json | 用户 .claude/skills | claude mcp add --transport http --scope project；已迁移 |
| Codex | 用户 .codex/config.toml | 用户 .agents/skills | codex mcp add --url；已迁移 |
| Gemini | 项目 .gemini/settings.json | 用户 .gemini/skills | gemini mcp add --transport http --scope project；skills install --scope user；接入 TODO |
| OpenCode | 项目 opencode.json | 用户 .config/opencode/skills | 当前 V2 mcp.servers；opencode mcp add --url；接入 TODO |
| Trae | 项目 .trae/mcp.json | 用户 .trae/skills | 官方 JSON 配置；已迁移 |
| Cursor | 项目 .cursor/mcp.json | 用户 .cursor/skills | 官方 JSON 配置；已迁移 |
| Factory Droid | 用户 .factory/mcp.json | 用户 .factory/skills | droid mcp add --type http 只写用户配置；接入 TODO |
| Kimi Code | 用户 .kimi/mcp.json | 用户 .kimi/skills | kimi mcp add --transport http；接入 TODO |
| Cline | 用户 .cline/data/settings/cline_mcp_settings.json | 用户 .cline/skills | cline mcp add --transport http --yes；CLI 接入 TODO |
| Kilo Code | 项目 .kilo/kilo.json | 用户 .kilo/skills | kilo mcp add --url；接入 TODO |
| Crush | 项目 crush.json | 项目 .crush/skills | 官方仍支持 JSON，crushrc 是当前配置 DSL；保留已迁移 JSON，DSL 执行不新增 |

Cline 目标明确使用 CLI 官方位置；IDE 扩展通过其 UI 提供实际配置文件，不从 VS Code globalStorage 猜测。Crush 选官方项目 Skill 位置，随项目安装；这避免把 Unix 用户配置目录当成 Windows Skill 目录，也无需新增环境变量分支。OpenCode V2 的 mcp.servers 与 Kilo 的 mcp 由各自官方合同决定，不加入旧版本多路径兼容。

配置等级 MUST 只有 basic 和 full；basic 是 Skills 与 HTTP MCP，full 额外包含命令防护、文件防护和上下文注入。每个既有目标 MUST 有具名完整级谓词；后端防护未实现时明确 TODO 并显示 basic，MUST NOT 写入失效脚本或声称 full。检测与执行 MUST 消费同一目标描述。Skill 身份 MUST 是 mechtoolkit，内容 MUST 来自固定版本 docs/MechToolkit。MUST 保留 Claude Code、Codex、Gemini、OpenCode、Trae、Cursor、Factory Droid、Kimi、Cline、Kilo Code 和 Crush。

#### Scenario: 平台目标与完整级判定

- **WHEN** 获取已支持平台的一键配置目标
- **THEN** 每个平台提供唯一配置位置、管理字段、正式命令入口与 Skill 位置，只有防护与上下文注入全部就绪时才声明 full

<!-- @anchor id=frontend-setup-targets -->
<!-- @architecture id=frontend.host_client -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=frontend-setup-targets level=L -->

### Requirement: 一键配置的结构化编辑

JSON/JSONC MUST 使用 jsonc-parser modify/applyEdits；YAML 使用 yaml Document/setIn；TOML 使用 smol-toml parse/stringify；XML 使用 xmldom DOM/XMLSerializer。格式错误 MUST 自然失败。原子写使用 write-file-atomic。只替换声明的管理字段，其余数据保留；TOML 改变内容时不保证注释与排版，同语义值经成熟解析和 Node isDeepStrictEqual 判定后保留原文，使官方 CLI 已写入的注释与排版不会引发重复执行。重复执行无内容变化 MUST 不写盘。

解析与原子写均由成熟依赖承担，项目仅选择管理字段并应用结构化编辑。验收覆盖未管理字段保留、重复执行零写入、TOML 同语义保留原文及变更值生效。

#### Scenario: 重复配置与损坏配置

- **WHEN** 对同一目标两次应用 basic，或输入损坏配置
- **THEN** 第一次补齐管理字段，第二次不写；损坏配置失败且原文不被覆盖

<!-- @anchor id=frontend-configuration-editor -->
<!-- @architecture id=frontend.host_client -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=frontend-configuration-editor level=M -->

### Requirement: 一键配置计划与正式入口执行

阶段二保留结构化编辑、固定版本 Skill 文件安装及 Claude/Codex 正式 CLI 的迁移能力。新增平台 CLI、官方 Skill 安装器、发布资源装配与 full 防护接入只声明合同；尚未接入的目标 MUST 在任何写入之前明确 TODO 失败。basic/full 是配置内容等级，不是安装接入完成的证明。正式 CLI 有对应操作时 MUST 使用正式入口，不绕过其配置管理。

项目职责仅限选择管理字段、绑定实际 MCP 地址和 Skill 源点、产生并应用写入计划；解析与原子写均由成熟依赖承担。正式 CLI 经 Execa 执行独立参数数组，Windows 命令入口解析由其成熟实现承担，MUST NOT 拼接 shell 命令。

计划 MUST 固定同一目标的端点、配置范围与 Skill 范围，apply 只消费计划，不再接受第二个端点。原生 CLI 的进程由 Execa 等待完成并传播失败，文件操作使用独占计划及原子写。阶段二不新增 CLI 调度或 Schema 执行引擎；正式接入时补齐实际 CLI 安装和重复执行行为证据。原始参考获取与现有解析能力满足字段、范围和入口决策，直接文件编辑只用于官方允许的配置目标；新增 CLI 改为明确失败的收益是保持原生控制权，代价是这些目标当前只可规划。

迁移验收覆盖真实文件安装，新增 CLI 入口 TODO 失败且配置/Skill 零写入。真实 ESM 扩展验证 Gemini 失败状态交付、工程原状及 Trae 管理字段和 Skill 安装；测试不宣称证明全部平台 CLI 执行或完整 Schema 正确性。

#### Scenario: 目标规划与 Skill 安装

- **WHEN** 为既有平台生成并执行一键配置计划
- **THEN** 从同一目标描述选择文件与字段，安装固定版本 Skill，正式 CLI 管理自己的配置；完整防护未接入的目标只声明 basic

<!-- @anchor id=frontend-setup -->
<!-- @architecture id=frontend.host_client -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=frontend-setup level=L -->

### Requirement: 运行日志与调用记录

前端运行日志 MUST 使用 pino@10.4.0，Logger 直接注入，原生 child、level 与 Error serializer。唯一 destination 适配 MUST 将 Pino 字符串交给 VSCode LogOutputChannel.append，由 VSCode 管理文件寿命，MUST NOT 自建轮转、tail、轮询或文本前缀路由。经组合根注入的后端运行日志 MUST 复用同一输出通道，MUST NOT 另建通道。

调用日志 MUST 消费 Host 唯一 InvocationRecords 的具名事件与 changes 流。存储、同步与订阅使用锁定 Effect 的 Ref、Semaphore、PubSub.sliding 和 Stream，具体合同见运行时规范「调用记录流与展示投影」；参数、结果和改写内容只接收所属业务允许的展示摘要。TODO：当前具名开始/终态及订阅空壳已经声明，实际采集、存储、展示摘要与前端绑定尚未实现。侧栏继续显示“未接入”，前端状态查询或配置操作不包装成 MCP 调用记录，前端不另建权威事件集合或从日志文件反推调用状态。

界面 MUST 保留单页调用日志展示组件，展示输入仅表达界面所需数据，MUST NOT 充当 Host 事件 Schema。完成接入后 MUST 展示真实调用状态（包含取消）、耗时、工具以及具名参数/结果/改写摘要，仅展开时格式化详情；最近 500 条展示投影来自 Host，同一记录流同时供结构化日志输出和显示。UI 不维护另一份历史或读取输出通道文件；原生 Webview.postMessage/消息事件及 JSON 编码传递具名投影，边界用 Zod 校验，Option 在边界显式编码为 null，内部继续使用 Option。字段和实际接线随正式接入实现，当前展示输入不是已接入的业务证据。

后端运行日志 MUST 与前端共用 pino@10.4.0 合同，经组合根注入与 Effect Logger 适配，具体合同见运行时规范；调用事件独立于运行日志传输。Pi stdio 原型不定义正式日志接入。

#### Scenario: 调用日志接入状态与展示

- **WHEN** Host 调用事件尚未接入，或独立使用日志展示组件
- **THEN** 实际侧栏显示未接入；展示组件按输入呈现状态，展开后显示参数、结果和改写文本

<!-- @anchor id=frontend-logging -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=frontend.ui -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=frontend-logging level=M -->

#### Scenario: 原生运行日志输出

- **WHEN** 前端 Logger 记录运行错误
- **THEN** Pino 原生 child 与 Error serializer 生成结构化日志，唯一 destination 写入 VS Code 管理的输出通道

<!-- @anchor id=frontend-log-destination -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=frontend-log-destination level=S -->

### Requirement: 只读适配的能力边界

前端 MUST 迁移语法资源与只读 LSP、诊断、文档导航和 Canvas 宿主，MUST NOT 启动旧 C# Host 或复用手工行协议。当前新版 MCP 只注册 workspace_status；LSP、Canvas 预览及外部调用事件对端未实现时 MUST 显示“未接入”，不能返回伪成功。只读查询与 Canvas 宿主 MUST 显式注入同一 Host，Canvas 展示接入执行保持 TODO，禁止另建读取、转换、资源供给或刷新链。

#### Scenario: 展示实际能力

- **WHEN** 用户打开只读语言查询或 Canvas 预览
- **THEN** 已绑定服务执行查询，未绑定服务显示未接入；工程状态与 MCP 使用实际新版 Host

<!-- @anchor id=frontend-readonly-adapter -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=frontend-readonly-adapter level=XL -->
