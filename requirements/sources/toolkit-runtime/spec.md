# Toolkit Runtime Specification

## Purpose

约束新版工程模型、Host、MCP 与两个插件启动入口。架构层级、职责和依赖方向唯一以递归聚合后的 `tools/**/*.c4` LikeC4 模型为准。本规范只约束这些架构节点的实现行为，不定义架构分层或替代架构模型。

## Requirements

### Requirement: 实施依赖顺序与交付合同

实现 MUST 按下表依赖推进；模块接口和数据所有权以 C4 为准。TODO 表示尚未实现的工作，不能作为可调用能力。旧 C# 仅用于核对二进制、字段、目录与游戏语义，不迁入旧 Host、IPC、浏览器、fallback 或全量重建机制。

| 顺序 | 责任节点                         | 输入与输出                                                                          | 完成条件                                                                                  |
| ---- | -------------------------------- | ----------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| 1    | Analysis → Semantic → Entities   | 逻辑快照/LocalAst/平台数据 → 符号分析 → Context 与转换依据 → 格式实体和完整依赖基准 | 仓库引用、跨零件目标和平台数据均有明确来源；引用变化拒绝旧基准                            |
| 2    | Conversion → Views               | 格式实体 → TS/HTML 视图；Host 按实际文件变化主动读取实体并计算影响范围              | TS 与 HTML 使用共享工作区转换服务；删除释放相应缓存；无关变化不触发转换                   |
| 3    | Change Plan → Editing → Entities | 视图文本与原基准 → 反向转换 → 统一候选 → 校验 → 提交 → 实际文件重读                 | 准备零写入，过期或冲突拒绝；多文件失败暴露实际结果；后续读取使用重读结果                  |
| 4    | Renderer → Preview               | CanvasDocument、viewport、资源字节、调色板、字体、时间 → PNG 与诊断                 | 软件 CPU surface，固定输入产生相同像素；资源缺失失败；成功和失败均释放资源                |
| 5    | Host → VS Code/MCP               | 受控请求 → HTML/XML 文本与 PNG                                                      | files 写出派生文件，memory 读取真实源工程且不写派生文件；前端只展示，MCP 只调用已实现业务 |

源变化由 Host 主动编排：消费 Files/take → 调用 Entities.refresh 实际重读并取得更新删除集合 → 调用 Entities.semanticSnapshot 取得完整基准 → 调用 Views → Conversion → files 写出或 memory 交付。需要预览时，由 Host 取得具名视图后调用 Preview → Renderer；PNG 与文本分开交付，Preview 不进入源变化调度链。

files 与 memory 的启动和源变化主循环 MUST 共享同一前缀：建立 PlatformFiles、ProjectFiles、ProjectWatcher、ProjectEntities 和工作区 Layer；监听 ready 后首次装配实体并验证工程设置，随后由唯一 Host Scope 消费 `ProjectWatcher.take`，按 `Entities.refresh` 的实际重读结果进入 `WorkspaceUpdates.refresh`。模式差异只允许出现在视图结果之后：files 继续经实体层发布或纠偏已登记派生文件，memory 在同一影响结果处短路所有派生文件写入和派生文件恢复，具名读取或展示请求时才在内存生成视图。两种模式不得各自建立监听、实体快照、转换缓存或刷新循环。

Views MUST 基于转换返回的实际依赖建立源到视图关联；Host 按明确的更新或删除输入主动调用视图服务，派生文件事件不得回流成源实体。Preview 缓存由其独立输入决定，不拥有派生视图调度。提交后以实际重读为事实，不以候选内存内容冒充落盘结果。

files 模式下，旁路篡改或删除已登记派生文件 MUST 由同一文件监听和 Host 主调度处理。源实体内容未变化也 MUST 根据派生归属恢复相应产物；恢复写入产生的通知以实际内容相同零写入收敛。变化输入须保留定位派生产物所需的信息，不能只依赖源实体更新集合。memory 模式没有派生文件恢复操作。

Canvas 与引用资源变化 MUST 使用同一实体重读结果和完整依赖基准判定影响。已打开预览的资源变化属于 Host 同次刷新交付，前端只展示新产物；不能靠 Canvas 二进制变化、独立监听或定时轮询决定刷新。预览渲染仍由具名请求执行，不把 Preview 改成源变化调度者。

阶段二已迁移的源变化执行止于 Files/take 和 Entities.refresh；WorkspaceUpdates.refresh 的派生分类、视图与预览交付保持明确 TODO 失败。单批重读或交付错误经注入的 Effect 日志输出，主循环继续消费下一批；监听自身失败则使实体服务失败，不交付旧状态。此边界不代表视图、诊断、语言查询或预览已随源变化更新。必要性与取舍：复用 Chokidar 原生通知、Effect 的队列与作用域，以及现有实体重读，不另建轮询或调度器；保持连续批次消费可暴露后续实际错误，但未实现的派生能力仍显式报错。真实 files/memory Host 回归从外部连续新增源文件，观察注入日志的刷新错误、后续工程查询和原始文件集合与写入量；直接实体回归另行核对重读的更新删除集合。两者共同验证当前执行边界，不以日志替代派生产物正确性。

资产生成与 Pi 接入的目标合同由完整 C4 子图约束；实际缺失以 `#gap` 通报，未实现能力不注册为工具。VS Code 标准 LSP 接入留在前端待办，接口职责为文本诊断/导航及取消，业务继续经 Host，不能新增工程状态或绕过实体层写入。

TODO：两种供给模式的产物一致性尚待视图与预览交付接通。完成后，files 与 memory Host 读取相同真实源工程并请求同一 TS/HTML 视图和 PNG 时，语义内容与图片 MUST 一致；files 只经实体层写出派生文件，memory 只交付内存结果，任一模块失败报告实际错误。验收 MUST 比较实际视图内容与图片。

#### Scenario: 两种供给模式沿同一边界消费源变化

- **WHEN** files 与 memory Host 在真实源工程中连续收到源文件变化
- **THEN** 两者均消费实际监听批次并主动重读实体，更新删除集合进入同一工作区刷新入口；当前交付入口由 TODO 明确失败并记录错误，随后继续消费下一批，保持源文件实际内容且派生写入量为零

<!-- @anchor id=runtime-convergence-contract -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.view_service -->
<!-- @architecture id=pure.change_plan -->
<!-- @budget id=runtime-convergence-contract level=M -->

### Requirement: files 与 memory 主循环分支

Host MUST 保持两种模式的步骤顺序一致：监听 ready → 实体首次装配与启动校验 → `take` 变化批次 → 按目录调用实体重读 → 取得实际更新删除集合 → 进入工作区刷新。files 分支在刷新结果中处理源视图发布和已登记派生文件纠偏；memory 分支保留实体重读、影响计算和转换失效输入，但跳过发布、写文件以及派生文件事件恢复。预览的两种模式使用同一具名请求和渲染合同，memory 不因跳过落盘而绕过 Host 或另建调度器。

当前阶段 `WorkspaceUpdates.refresh` 的派生分类、视图交付和预览影响仍是 TODO；因此两种模式都在同一个明确错误处停止，监听仍继续消费下一批，且测试必须核对零派生写入。这一失败边界不能表述为已经完成 files/memory 分支。

#### Scenario: 两种模式共享启动装配与生命周期

- **WHEN** 分别以 files 与 memory 模式打开真实工程
- **THEN** 两者经同一启动入口装配工作区服务、校验工程设置，并在所属 Host Scope 启动唯一监听消费；启动失败释放已创建资源，关闭释放所属工作区

<!-- @anchor id=workspace-view-mode-startup -->
<!-- @architecture id=toolkit.host.logic.lifecycle -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-view-mode-startup level=L -->

#### Scenario: 两种模式共享连续批次重读与刷新循环

- **WHEN** files 与 memory Host 接收连续源文件变化
- **THEN** 两者均从同一监听入口取得批次，按变化目录重读实体，将实际变化和更新删除集合交给同一刷新入口；当前两者均在刷新 TODO 处记录错误且派生写入量为零，随后继续消费下一批

<!-- @anchor id=workspace-view-mode-loop -->
<!-- @architecture id=toolkit.host.logic.refresh -->
<!-- @architecture id=toolkit.files -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-view-mode-loop level=M -->

#### Scenario: 视图实例保存各自的供给模式

- **WHEN** 同一真实工程分别建立 files 与 memory Host 的工作区视图实例
- **THEN** 每个视图实例保存创建时传入的模式，Host 查询分别返回 files 与 memory；实际派生交付分支遵循本 Requirement 正文的待实现合同

<!-- @anchor id=workspace-view-mode-binding -->
<!-- @architecture id=toolkit.view_service -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-view-mode-binding level=M -->

### Requirement: 冷启动与连续刷新步骤及热点

Host 的冷启动 MUST 按以下顺序完成：创建并注入唯一 PlatformFiles、ProjectFiles、ProjectWatcher、ProjectEntities 与工作区 Layer；监听等待 `ready`；实体服务首次枚举工程并装配原始与逻辑快照；Host 经实体服务读取当前设置完成启动校验；最后在同一 Host Scope 启动唯一 `WorkspaceUpdates.watch`。启动失败在交付 Host 前释放已创建资源。

首次实际变化的刷新 MUST 经过 `take` 取得批次、保留精确路径、按目录调用 `ProjectEntities.refresh`、取得更新删除集合，然后进入 `WorkspaceUpdates.refresh`。该批次不重复创建服务、监听或全量 Host；当前阶段在刷新 TODO 处明确失败，错误记录后监听继续消费下一批。

启动后的正常刷新复用同一循环和工作区实例：监听归并批次，实体按变化目录重读，随后由 Views/Preview 按实际基准处理影响；提交后的刷新同样以实际磁盘重读为事实。正常刷新不得回到冷启动的 Layer、监听或完整 Host 创建路径，也不得以定时轮询替代事件批次。

性能核对必须区分事实与待测热点：冷启动成本包括监听建立与 ready、目录枚举/元数据读取、原始实体解析、逻辑实体装配和固定输入读取；首次及正常刷新成本包括变化目录重读、逻辑快照重建、实际依赖基准核对、转换依赖读取；预览请求另有图片/字体解码、文本 shaping、布局、Spine/遮罩合成、PNG 编码和协议字节交付。当前没有完整端到端基准，不能声称热点已消除；Effect/Promise 异步签名也不代表同步解析、WASM 绘制或编码脱离宿主事件循环。

#### Scenario: 冷启动装配并启动所属工作区消费循环

- **WHEN** Host 首次打开真实工程
- **THEN** 按正文顺序完成工作区服务装配、监听 ready、实体首次装配与设置校验，在所属 Host Scope 启动唯一消费循环；启动失败释放已创建资源，关闭释放所属工作区

<!-- @anchor id=workspace-cold-start -->
<!-- @architecture id=toolkit.host.logic.lifecycle -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-cold-start level=L -->

#### Scenario: 实体首次装配与后续目录重读共享状态

- **WHEN** 实体服务完成首次装配后接收实际变化目录
- **THEN** 首次从工程文件建立原始与逻辑快照，后续在同一实体服务中重读指定目录并更新快照，交付实际更新删除标识；服务状态随所属 Scope 释放

<!-- @anchor id=workspace-entity-refresh-phases -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-entity-refresh-phases level=L -->

#### Scenario: 首次与后续变化复用同一批次消费循环

- **WHEN** 已启动的 Host 连续接收两批实际文件变化
- **THEN** 同一循环复用已有监听和实体服务，依次取得批次、按变化目录重读、携带精确变化路径和更新删除集合调用工作区刷新；当前刷新 TODO 记录错误后继续消费下一批，派生写入量为零

<!-- @anchor id=workspace-refresh-phases -->
<!-- @architecture id=toolkit.host.logic.refresh -->
<!-- @architecture id=toolkit.files -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-refresh-phases level=M -->

### Requirement: 工程格式计算独立于 IO

工程设置解析 MUST 是纯计算：输入完整 Hjson 文本，输出只读工程身份、名称和主地图引用。Hjson 语法与规范化写出遵循 `openspec/specs/hjson-format/spec.md`。MUST 使用 Hjson 官方解析器及结构校验，不读取文件、配置或全局工作区。空名称和空主地图引用 MUST 保留；缺失必需字段或字段类型错误 MUST 抛出错误。查询投影不承担工程序列化。

#### Scenario: 读取 CreatAI 工程设置

- **WHEN** 调用方提供真实 `setting.json` 内容
- **THEN** 解析其 Hjson 结构并保留字段原值；无效输入直接失败

<!-- @anchor id=project-settings -->
<!-- @architecture id=toolkit.project_model.logic -->
<!-- @architecture id=toolkit.project_model.tests -->
<!-- @budget id=project-settings level=S -->

### Requirement: 专属 Host 管理工作区

每个 Host MUST 有独立 Effect ManagedRuntime 与工作区对象。启动必须显式选择 `files` 或 `memory` 模式并验证工程设置可读。绝对工程根只交给创建 Host 的文件系统边界，业务查询不返回绝对路径。Host 关闭 MUST 释放运行时与其 Scope，关闭后的查询 MUST 失败。工程状态 MUST 经实体服务读取当前设置，Host MUST NOT 直接解析文件或保留无失效机制的设置缓存。

必要性与取舍：复用 Effect ManagedRuntime、Layer 与 Scope，由同一 Host 持有运行时、关闭状态与调用作用域，统一完成服务装配、状态查询和关闭释放。生命周期保持单一 Scenario，使启动失败释放、调用随关闭取消和关闭后拒绝访问归属于同一所有者。将这些操作拆到独立生命周期对象会增加运行时、关闭状态和作用域的跨对象传递；仅拆 Scenario 仍会计入同一 Host 文件的直接依赖。Host 同时承担服务装配与受控业务入口，文件级依赖包含装配服务和入口类型，采用满足该职责的最低 L 级预算；转换、编辑和预览算法仍由各业务服务承担。

#### Scenario: 两种模式的 Host 独立启动和关闭

- **WHEN** 分别创建文件模式和无文件模式 Host，修改工程设置后再次查询
- **THEN** 各自返回所属模式与最新设置；关闭后不可继续查询

<!-- @anchor id=host-lifecycle -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=host-lifecycle level=L -->

### Requirement: 后端容器栈分层

Host 内的容器栈 MUST 用 Effect Context、Layer 与 Scope 表达为进程级(后端)、工作区级、快照级与请求级：根运行时在各 Host 生命周期内唯一，持有该 Host 的工作区服务；工作区服务绑定工程根与视图模式。快照级与请求级按受控入口分别独立实例化，入口结束或失败 MUST 释放对应 Scope。分层为空壳时不携带业务逻辑。Host 关闭后入口 MUST 失败。上下文的作用域不等于业务快照，MUST NOT 提供未实现的实体状态或业务查询。

运行中的请求与快照 Effect MUST 绑定 Host Scope。受控入口 MUST 接收具名请求输入与原生 AbortSignal，由 Host 分派实际业务调用；请求与快照 Scope 在该次调用结束时释放。Host Scope 关闭 MUST 中断 Effect、关闭该次上下文并触发所属信号；可取消外部操作 MUST 消费该信号。MUST NOT 宣称能够强制停止不接受取消的任意 JavaScript Promise。前端请求采用相同取消合同。

现役 runInRequest 与 withSnapshot 的消费者只请求工程状态，输入为 WorkspaceStatusRequest，结果直接复用 WorkspaceStatus；TS／HTML 读取沿用已有 readTs／readHtml 入口。删除无消费者的重复视图分派及联合结果，不新增结果包装或重载。必要性与取舍：Effect 的 Context、Layer、Scope 和 ManagedRuntime 已承担实例隔离、取消和释放；这两个入口仅区分请求级与快照级上下文，并连接已有状态查询，维持原调用者和结果合同。现役测试验证独立上下文释放、状态读取、请求取消后的后续可用性及关闭后拒绝调用。

预算必要性与取舍：请求与快照上下文、对应的 Effect 服务及 Host 入口共同表达同一作用域分层合同，保持单一 Scenario；每次调用以独立 Layer 提供上下文，经同一 Host 执行入口绑定所属运行时与取消信号。复用 Effect 原生作用域能力，业务状态仍归工作区服务。依赖按承载这些入口的整个 Host 文件计量，包含 Host 的装配服务和业务入口类型，最低满足等级为 L。仅拆 Scenario 会重复计入同一文件的依赖；另建上下文调度对象则增加 Layer 与 Host 作用域之间的传递，因此保留现有内聚边界及 L 级预算。

#### Scenario: backend-container-stack

- **WHEN** 在同一 Host 上两次进入快照级与请求级入口，并在请求进行中关闭 Host
- **THEN** Host 按具名输入分派业务，快照级与请求级各自得到独立实例，入口结束或失败后上下文已关闭；进行中的请求被中断并收到 AbortSignal；Host 关闭后入口失败

<!-- @anchor id=backend-container-stack -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=backend-container-stack level=L -->

### Requirement: 后端运行日志

后端运行日志 MUST 使用 pino@10.4.0，与前端共用同一日志库与结构化 JSON 合同。pino Logger MUST 由启动组合根创建并作为显式参数注入 Host，Host MUST NOT 自行创建日志输出、destination 或轮转。Effect 运行时日志 MUST 经 Effect Logger 适配完整映射（Fatal、Error、Warn、Info、Debug、Trace 各自对应 pino level）到注入的 pino Logger，MUST 替换默认控制台日志，MUST NOT 让框架日志绕过注入输出污染协议通道。记录内容 MUST 限组件、模块与操作标识，MUST NOT 记录密钥与完整请求响应。

#### Scenario: Effect 日志合同接 Pino

- **WHEN** Effect 运行时产生框架日志，或后端经 Effect 日志记录运行日志
- **THEN** 六个级别各自映射为对应 pino level，经注入的 pino Logger 输出结构化 JSON

<!-- @anchor id=backend-logging -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=backend-logging level=M -->

#### Scenario: 组合根注入统一日志输出

- **WHEN** 插件组合根打开 Host
- **THEN** pino Logger 由组合根创建并显式注入；Host 打开成功、打开失败与关闭的边界日志写入同一输出

<!-- @anchor id=backend-log-injection -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @architecture id=frontend.plugin -->
<!-- @budget id=backend-log-injection level=M -->

### Requirement: 两种宿主的运行日志所有权

运行日志 MUST 在宿主组合根创建唯一 pino Logger，前端、Pi 接入、Host 与 Effect 适配仅派生 child；组件通过 component/module/operation 字段区分，过滤由 Pino 原生 level 承担。运行日志不作为 MCP 业务结果、调用事件或会话记忆，不把完整请求响应、凭据、资产字节或带秘密的远端 URL 写入日志。Host 的成功、失败与关闭日志使用注入输出，输出必须存活到所属 Host 和进行中请求全部结束。

VS Code 使用现有 FrontendLog 将 Pino JSON 写入唯一 LogOutputChannel.append，扩展生命周期持有输出通道，原生 showLogs 打开它；通道文件位置和寿命由 VS Code 管理。deactivate 先关闭 MCP/组件/Host，再由扩展 subscriptions 释放输出通道。前端与后端不另写日志文件、轮转或 tail；侧栏调用详情来自调用事件，不从该通道解析。

Pi 组合根在 session_start 为当次会话建立 Pino Logger，destination 固定使用 Node process.stderr；同一 Logger 传给 PiWorkspace.open → Host.open，并派生 Pi 与 Host 的 child。stderr 是进程拥有的流，插件不关闭它；会话关闭先结束 MCP/HTTP/Host，使关闭诊断仍可输出，再释放本会话对 Logger 的引用，下一会话重新建立所属 Logger。Pi 的 JSON/RPC/print stdout 保留宿主原生输出，不插入运行日志；TUI、RPC 与无 UI 模式使用同一 stderr 合同，不逐条调用 ctx.ui.notify，也不将运行日志追加到会话 transcript。需要保存时由启动 Pi 的调用方重定向 stderr，Toolkit 不增加日志文件路径配置、落盘副本或轮转服务。

固定 Pi 的 mcp.md 中 ~/.pi/agent/mcp.log 只接收 MCP server logging notifications，并由 Pi 自身按 5 MB 轮转；它不是供进程内扩展写 Pino 的通用输出接口。现役 HttpMcp 是每请求创建和释放的 JSON 响应传输，后台刷新及启动/关闭日志不属于某次 MCP 请求；为接入该文件增加持久 MCP 连接或把日志塞进工具结果，会改变现有生命周期。因此本产品运行日志使用注入 stderr，Pi 自身 MCP 连接诊断仍归 Pi，二者不互相反推调用状态。

必要性与取舍：Pino 已提供 JSON、child、level、Error serializer 和标准 DestinationStream，Node 已提供 stderr，VS Code 已提供 LogOutputChannel 与 subscriptions，Pi 已提供会话事件。现有 FrontendLog 只连接宿主 append，BackendLog 只连接 Effect 日志；无需新的日志框架、文件监听或自研轮转。VS Code 原生通道方便查看，Pi stderr 可由调用方统一采集且覆盖无 UI 模式；取舍是 Pi 插件不提供独立日志面板与文件保留策略。现役测试验证实际六级 Pino 输出、Error serializer 和 Host 打开/失败/关闭日志，Pi logger 注入签名已存在但创建及会话释放仍 TODO。正式 Pi 接入须验证 stderr 有实际结构化记录、stdout 无日志混入、失败/关闭输出及会话隔离；现有 Pi 空壳失败不冒充该行为已经实现。

#### Scenario: VS Code 输出保留到后端释放完成

- **WHEN** VS Code 扩展创建日志输出、启动后端并随后关闭
- **THEN** 组合根经 FrontendLog 创建 Pino 并注入后端，结构化日志写入原生输出通道；deactivate 先关闭 MCP、组件与 Host，再由扩展 subscriptions 释放通道

<!-- @anchor id=host-shell-logging -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=host-shell-logging level=M -->

#### Scenario: Pi 会话拥有注入 stderr 的日志生命周期

- **WHEN** Pi 会话开始、启动失败或关闭
- **THEN** 会话组合根创建写入 stderr 的唯一 Pino 并传入 PiWorkspace 与 Host，关闭 MCP、HTTP 和 Host 后释放会话 Logger 引用，保留进程拥有的 stderr；stdout 保持宿主原生输出。当前会话日志装配与释放待实现

<!-- @anchor id=pi-session-logging -->
<!-- @architecture id=toolkit.pi_plugin.logic -->
<!-- @todo -->

### Requirement: 日志与成功率统计分离

运行日志与调用统计 MUST 使用两条明确边界：运行日志经组合根注入的 pino 记录组件、模块、操作和错误，调用统计经 Host 工作区 Scope 的 `InvocationRecords` 记录具名开始/结束事件。日志文本、前端展示状态和 MCP 返回内容不得反向推断统计，也不得建立第二份调用事件集合。

成功率统计 MUST 只从已完成的 `InvocationRecords` 终态按会话、Host 和具名 operation 聚合：`succeeded` 为成功数，`failed` 为失败数，`canceled` 单独计数且不进入成功率分母；成功率定义为 `succeeded / (succeeded + failed)`，没有完成的成功/失败样本时以 `Option.none` 表达。统计结果只交付计数、比率、时间范围和诊断摘要，不保存秘密、完整请求响应、资产字节或可恢复任务状态。Host 关闭释放记录与聚合状态，两个 Host/会话互不共享。

当前 `InvocationRecords` 的登记、读取、订阅和成功率聚合保持 TODO；Pino 运行日志可用不代表调用统计已接入，前端侧栏继续显示未接入。未接入时不得从历史 legacy 日志、字符串提示或 UI 计数生成成功率，也不得为达到展示全绿而丢弃失败、取消或重复事件。

#### Scenario: 运行日志经注入输出交付结构化记录

- **WHEN** Effect 产生运行日志或前端通过注入的 Pino 记录组件错误
- **THEN** Effect 六级日志映射到 Pino 对应级别，前端保留组件标识与原生 Error serializer 结果，结构化记录写入注入输出；调用统计的数据来源遵循本 Requirement 的真实终态合同

<!-- @anchor id=logging-statistics-contract -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=logging-statistics-contract level=M -->

#### Scenario: 成功率只聚合真实调用终态

- **WHEN** 同一 Host 收到实际调用的 succeeded、failed、canceled 终态，并按会话和 operation 请求统计
- **THEN** 从唯一 InvocationRecords 来源聚合成功与失败数，取消单独计数且不进入分母，无成功或失败样本时返回 `Option.none`；统计随所属 Host 关闭释放，各会话互相隔离。调用记录与成功率聚合当前待实现

<!-- @anchor id=invocation-success-statistics -->
<!-- @architecture id=toolkit.invocations -->
<!-- @todo -->

### Requirement: MCP 工具复用 Host 业务

MCP 接入 MUST 通过官方 SDK 注册工具并调用绑定的 Host。`workspace_status` 为只读工具，返回当前工程设置与视图模式。只有有真实实现的工具可以进入工具列表。协议层 MUST NOT 实现工程格式解析、视图转换或修改编排。

#### Scenario: 通过 HTTP 查询工程

- **WHEN** MCP 客户端通过 HTTP 调用 `workspace_status`
- **THEN** 工具返回所属 Host 的实际查询结果，业务语义与直接查询一致

<!-- @anchor id=mcp-workspace-query -->
<!-- @architecture id=toolkit.mcp.logic -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=mcp-workspace-query level=S -->

### Requirement: 旧版 MCP 读工具迁移清单

新版 MCP 读工具 MUST 以 Host 已实现的具名只读入口为唯一来源；旧版工具名不得因为配置、兼容清单或前端展示自动重新注册。当前已迁移且可注册的读工具只有 `workspace_status`。旧版 `read_view` 的 TS/HTML 读取分别归入共享 Views/转换服务，`read_lint` 归入 Diagnostics，`read_entities_by_name` 与 `read_entities_by_guid` 归入 ProjectEntities 查询，四个 `read_call_graph_*` 归入同次语义分析与语言查询，`read_device_script_outline` 归入 TS 视图实体读取；这些入口在对应业务完成前保持未注册并明确 TODO。`read_sdk_docs` 属于文档资源读取，`read_player_log` 属于外部运行日志读取，当前没有新版 Host 业务合同，保持未迁移，不得伪装成工程读工具。

迁移后的每个工具 MUST 复用同一 Host Scope、实体基准、取消信号和结果类型；协议层只做 schema、调用映射和错误交付，不复制查询、解析、语义分析或文件读取。工具列表 MUST 精确等于当前真实注册集合；对未迁移旧名的调用 MUST 返回协议层的未知工具错误，不能返回空结果、旧版数据或成功占位。

#### Scenario: 读工具列表与迁移状态一致

- **WHEN** MCP 客户端列举工具并尝试调用旧版读工具名
- **THEN** 列表只包含已实现的 `workspace_status`；旧版读工具均不在列表，调用未迁移名称失败；`workspace_status` 仍经同一 Host 返回实时工程状态

<!-- @anchor id=mcp-read-tool-migration -->
<!-- @architecture id=toolkit.mcp.logic -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=mcp-read-tool-migration level=S -->

### Requirement: MCP 工具 schema 与结果收敛

每个现役 MCP 工具 MUST 由同一注册入口同时声明唯一名称、描述、输入 schema、输出 schema 和只读/副作用注解；schema MUST 使用官方 SDK 支持的结构化对象，不接受匿名字符串、位置参数或未登记的额外字段。输出 MUST 复用 Host 业务结果类型，经 MCP SDK 的 `structuredContent` 交付；如需兼容纯文本客户端，可同时提供等价 JSON 文本，但文本不能成为第二套结果结构。

当前 `workspace_status` 是唯一现役工具：输入为无字段对象，输出固定为 `{ viewMode, settings: { guid, name, mainMap } }`，`viewMode` 仅允许 `files` 或 `memory`；工具标记 `readOnlyHint`，调用经 `Host.runInRequest` 使用同一 `WorkspaceStatus`。未来读工具、修改工具和资产工具必须分别复用所属 Host 请求/候选结果，不得在协议层重新定义工程实体、诊断、计划或任务状态。未实现工具不声明 schema、不进入列表，也不返回空结构冒充成功。

#### Scenario: schema 与结果结构一一对应

- **WHEN** MCP 客户端列举并调用 `workspace_status`
- **THEN** 工具元数据包含稳定的输入/输出 JSON Schema 与只读注解；调用结果同时满足声明的结构化 schema 和等价 JSON 文本，字段名、枚举和值与 Host 的 `WorkspaceStatus` 一致

<!-- @anchor id=mcp-schema-result-convergence -->
<!-- @architecture id=toolkit.mcp.logic -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=mcp-schema-result-convergence level=S -->

### Requirement: MCP 多模态结果与编辑反馈

MCP 工具结果 MUST 直接使用锁定 @modelcontextprotocol/sdk 1.31.0 的 CallToolResult。原生 content 支持 text、image、audio、resource_link 和 resource；structuredContent 独立承载满足 outputSchema 的 JSON 业务结果，isError 表达实际工具失败。图像内容使用 type=image、实际 PNG 字节的 base64 data 和 mimeType=image/png；它与文本及结构化结果属于同一次 tools/call 返回，不把文件路径、Markdown 图片链接、base64 字符串文本或“请查看预览”的提示当作已经交付图像。MCP 协议支持图像不等于所有客户端或模型都显示、理解图像；客户端展示和模型输入能力在实际宿主接入时验证，不另造私有图像协议。

图像 MUST 来自同一 Host 的 Preview 或 AssetProcessing.renderSlice 成功结果，协议层只用 Node Buffer 做字节到 base64 的标准编码，不重绘、不截图、不访问 Canvas 或图片文件，也不创建另一后端。内部业务继续传递 Uint8Array 和具名结果；base64 只在 MCP 返回边界生成，不存入任务、调用记录、缓存或工作区。文本和 structuredContent 保留对应 sourceEntityId/taskId/artifactId、实际诊断、尺寸及读取基准或资源身份，使调用方能把每幅图像与本次结果关联；多个切片尺寸按业务结果顺序交付并以文字标注案例。JSON 元数据不复制完整图像字节。

patch 反馈 MUST 保留编辑结果的真实阶段：候选准备成功交付 ChangePlan/诊断及候选信息，实际 commit 和重读成功才报告工程已修改。Canvas 编辑或影响已打开 Canvas 的资源修改，在同一 Host 的实际影响计算及刷新完成后，把该次预览 PNG 随结果返回；纯脚本等没有实际 Canvas 预览结果的调用只交付对应文本和结构化结果，不凭请求名称猜测影响。候选阶段不得把旧工程预览标成修改后的效果。提交成功后若渲染失败，反馈明确工程提交已成功及预览失败诊断，不把整次操作描述为未写入，也不重试提交；协议适配复用业务实际结果，不新增隐式回滚或第二提交状态。

资产 result/renderSlice 的反馈同样交付实际候选或切片图像及具名校验结果，视觉评阅意见作为文本/结构化结果保留；视频、3D 等不能凭扩展名塞进 image。现有音频内容块和资源内容块是协议事实，具体格式的业务交付需有对应已实现入口，不因 SDK 存在字段就注册能力。VS Code Webview 展示和 Pi MCP 客户端消费同一 Host 的 PNG；MCP 返回图像不要求在工程落盘预览或启动浏览器。

必要性与取舍：MCP 官方 Tools 规范的 Tool Result 部分及锁定 SDK 的 ImageContentSchema、ContentBlockSchema、CallToolResultSchema 已提供多模态返回，Node Buffer 提供标准编码，唯一 CPU renderer 提供 PNG。项目只连接具名业务结果与官方内容块，无需附件服务、提示词提醒协议或第二渲染链。内联 PNG 让支持图像的调用方在同次结果取得视觉证据，代价是 base64 增加传输体积；只交付本次具名预览和显式尺寸案例，不复制全工作区图片。当前仅 workspace_status 已注册并返回文本/structuredContent，patch、Preview 和资产工具仍 TODO，不把本合同视为图像反馈已接入。现役 HTTP 回归验证实际结构化结果、错误和工具注册边界；正式接入验收须以同一 tools/call 解码图像并核对独立预期像素、尺寸、业务身份及提交阶段，验证坏图像/渲染失败、多个案例顺序、取消与两个宿主的实际显示，不能以 schema 字段存在冒充功能证据。

#### Scenario: 具名预览随实际工具结果交付

- **WHEN** 已实现业务在本次工具调用中成功交付预览或切片 PNG
- **THEN** MCP 以原生 image 内容块和同次文本/structuredContent 返回实际图像及业务结果；未实现工具保持未注册，提交与渲染阶段按实际事实反馈

<!-- @anchor id=mcp-multimodal-feedback -->
<!-- @architecture id=toolkit.mcp.logic -->
<!-- @todo -->

### Requirement: Pi 读闭包工具预留

MCP MUST 预留名为 `read_closure` 的新增只读工具位置，供 Pi 代理在原生文件读取之外按需取得一个视图及其同次读取的关联源码闭包。粗略输入为工程相对 `viewRelativePath`；粗略输出为视图文本、具名闭包文本和生成它们的 `EntityReadBaseline`。闭包只包含 Host 根据当前实体与语义依赖实际解析出的内容，不接受任意绝对路径、目录扫描或调用方拼接的第二份上下文。

`read_closure` 不是工程唯一读入口，也不替代其他 Agent 的原生文件工具；VS Code 工具目录和其他宿主不得把它标记为权威读取能力。正式接入时，Pi 只经同一 Host Scope、实体服务和取消信号调用，MCP 层不复制视图解析、闭包计算或文件读取。当前 Host 尚无该入口，`read_closure` MUST 保持未注册并返回明确的未实现/未知工具错误；不得用旧版 Python 适配器、草稿合并或空闭包占位。

#### Scenario: 读闭包位置已预留但未伪造能力

- **WHEN** MCP 客户端列举工具或请求 `read_closure`
- **THEN** 当前工具列表仍只有已实现的 `workspace_status`，`read_closure` 不在列表且调用失败；正式接入位置、输入输出边界和 Pi 便利用途可由本合同追溯

<!-- @anchor id=pi-read-closure-reservation -->
<!-- @architecture id=toolkit.mcp.logic -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=pi-read-closure-reservation level=S -->

### Requirement: VS Code 提供对外 MCP

HTTP 接入 MUST 使用官方 Streamable HTTP 传输、监听本机回环地址并提供 `/mcp`。每个请求的协议资源由接入层释放；服务关闭后 MUST 停止接受请求。插件入口 MUST 创建文件模式 Host，并统一拥有前端容器、Host 和 MCP 服务的生命周期；启动失败时释放已创建的 Host 与前端容器，Host 释放失败时仍释放前端容器。

#### Scenario: HTTP 服务提供并释放协议资源

- **WHEN** 外部 MCP 客户端连接服务并查询工程，随后关闭服务
- **THEN** 查询成功，关闭后不能再次连接

<!-- @anchor id=mcp-http-lifecycle -->
<!-- @architecture id=toolkit.mcp.logic -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=mcp-http-lifecycle level=S -->

#### Scenario: VS Code 启动文件模式 Host

- **WHEN** VS Code 启动入口打开工程
- **THEN** 经前端栈进程级容器与组件级容器创建专属文件模式 Host 与对外 MCP 服务，MCP 工具回调经 Host 请求级入口执行；关闭入口时按 MCP、Host、前端容器顺序释放；启动失败时已创建的前端容器被释放，Host 释放失败时前端容器仍被释放

<!-- @anchor id=vscode-host-bootstrap -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=frontend.tests -->
<!-- @budget id=vscode-host-bootstrap level=M -->

### Requirement: Pi 接入边界

Pi 接入 MUST 使用已登记官方基线的扩展 API；扩展工厂只登记 flags 和事件，`session_start` 建立 memory Host 和回环 HTTP MCP，再调用官方 `registerMcpServer`。`session_shutdown` 按注销 MCP、关闭 HTTP、关闭 Host 的顺序释放；会话替换、fork 和 reload 遵循同一生命周期。工具合同唯一由 MCP 提供，Pi 官方客户端交付文本、HTML/XML、PNG、结构化结果与错误。固定宿主 @earendil-works/pi-coding-agent@1.0.0，由 peerDependencies 声明。官方默认工厂、工作区及上下文端口已声明，执行保持 TODO 失败。

#### Scenario: Pi 待办入口明确失败

- **WHEN** 调用尚未实现的 Pi 接入
- **THEN** 返回明确未实现错误，不启动子进程、不生成派生文件

<!-- @anchor id=pi-extension-placeholder -->
<!-- @architecture id=toolkit.pi_plugin.logic -->
<!-- @architecture id=toolkit.pi_plugin.tests -->
<!-- @budget id=pi-extension-placeholder level=S -->

#### Scenario: Pi 扩展工厂保留官方接入合同

- **WHEN** 官方宿主调用默认导出的 Pi 扩展工厂
- **THEN** 工厂进入官方类型约束的登记入口；当前入口以明确 TODO 错误失败，flags 与会话事件登记仍待实现，工具注册集合继续由唯一 MCP 入口决定

<!-- @anchor id=pi-host-bootstrap -->
<!-- @architecture id=toolkit.pi_plugin.logic.registration -->
<!-- @architecture id=toolkit.pi_plugin.tests -->
<!-- @budget id=pi-host-bootstrap level=S -->

#### Scenario: Pi 工作区端口保留会话资源边界

- **WHEN** 调用 Pi 工作区的打开、关闭或 endpoint 查询入口
- **THEN** 端口以官方上下文、配置、注入 Logger、Host 与 HTTP 类型表达会话资源边界；当前各入口均以明确 TODO 错误失败，资源建立与释放顺序遵循本 Requirement 正文

<!-- @anchor id=pi-workspace-boundary -->
<!-- @architecture id=toolkit.pi_plugin.logic.lifecycle -->
<!-- @budget id=pi-workspace-boundary level=S -->

#### Scenario: Pi 上下文登记入口明确失败

- **WHEN** 使用官方 ExtensionAPI 调用 Pi 上下文登记入口
- **THEN** 当前入口以明确 TODO 错误失败，Skills 发现与工具结果上下文登记待实现

<!-- @anchor id=pi-context-boundary -->
<!-- @architecture id=toolkit.pi_plugin.logic.context -->
<!-- @budget id=pi-context-boundary level=S -->

### Requirement: 会话记忆与连续失败反馈边界

Pi 会话 MUST 为可选的有状态记忆预留明确边界：记忆只归当前 `session` 的 `PiContext`，由官方会话事件创建和释放，不进入工程实体、Host 工作区快照、MCP 工具 schema 或进程全局状态。记忆未启用时，工具调用仍按无记忆请求执行；会话替换、fork、reload 和 `session_shutdown` MUST 丢弃旧会话记忆，不从历史文本或已关闭 Host 恢复工程状态。记忆内容只保存经产品合同允许的会话上下文，不保存密钥、完整工程文件、资产字节或未登记的隐式偏好。

连续失败反馈 MUST 由同一会话的正式调用终态派生：只统计 `InvocationRecords` 中的 `failed` 结果，按具名 operation 和会话隔离；成功或取消清零对应连续失败计数，会话关闭释放计数。反馈 MUST 原样保留本次真实错误与诊断，并在达到明确次数后提示重新核对输入、schema、路径或基准；不得重试调用、修改请求、遮蔽原错误或恢复旧草稿。反馈组件不能接收业务回调、拥有第二个执行器或复制调用记录。

当前 Pi 会话事件、可选记忆、调用终态消费和连续失败反馈均保持 TODO；占位入口 MUST 明确失败且不创建会话状态、持久化文件或伪造建议。旧版 `legacy` 的失败提示和会话字典只作参考，不属于现役实现或迁移依据。

#### Scenario: 会话状态与失败反馈按生命周期隔离

- **WHEN** 同一 Pi 会话连续产生失败、成功、取消和会话替换事件，并分别启用或关闭可选记忆
- **THEN** 失败反馈只来自该会话的真实终态，成功或取消后对应计数归零；会话替换释放记忆和计数，不影响另一会话；当前未接入入口明确失败且工程、文件和 Host 状态保持不变

<!-- @anchor id=pi-session-memory-feedback -->
<!-- @architecture id=toolkit.pi_plugin.logic -->
<!-- @architecture id=toolkit.invocations -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.pi_plugin.tests -->
<!-- @todo -->

### Requirement: 骨架的后续接入位置

脚本与 Canvas 纯转换 MUST 独立于运行时服务。TS 转换服务持有完整 Context 打印的官方 AST 与局部片段缓存，Canvas 转换服务在同一工作区 Layer 中共享六向转换缓存。TODO：完整语义装配和编辑业务按各自合同填充；Canvas 预览接入 CPU 渲染入口。Host 装配服务、管理生命周期并按 C4 的主动驱动链编排调用，实际工程 IO 只由实体层调用文件层；平台数据从唯一结构化文本资源输入。

视图调度由 Host 与 Views 负责：文件模式在 Host 明确读取实体结果后写出派生视图；memory 模式读取真实源工程并只在内存生成视图，不落盘。Preview 不反向依赖 Views，调用方先取得 `HtmlViewEntity`/`CanvasDocument` 再调用渲染入口。

Host 的转换依赖只在 WorkspaceService.layer 中装配并注入 Views 与 Editing；WorkspaceModule 不暴露转换服务，Host 不提供直接打印或删除转换缓存的入口。首次派生交付与提交后的主动刷新执行仍为 TODO，源变化链只由 Host 消费文件批次并按实体实际重读结果驱动 Views。

VS Code 编辑器生命周期、只读界面与一键配置由前端规范约束。TODO：Pi 官方扩展会话资源与 HTTP MCP 接入尚未实现，官方入口保持明确失败。

资产任务、远端生成、产物校验、候选存储和确认提交 MUST 按 C4 目标合同实施。未实现入口继续明确失败，MCP 工具仅登记具有实际行为证据的能力。

#### Scenario: 尚未实现的业务不进入可调用列表

- **WHEN** 业务尚只有架构安排，未实现转换、渲染、生成或插件界面
- **THEN** 保持明确 TODO；内部骨架调用明确失败，未实现能力不进入 MCP 工具列表

<!-- @anchor id=runtime-extension-boundaries -->
<!-- @nonCode -->

### Requirement: 工作区状态与缓存唯一清单

现役工作区中跨调用或跨请求保留的状态 MUST 只属于创建它的 Host Scope，并按下列清单实现；清单以源码中的实际状态为准，不把单次计算局部表误报为工作区缓存。

| 状态 | 数量与位置 | 键或内容 | 刷新、失效与关闭 |
| --- | --- | --- | --- |
| 文件内容缓存 | 1 处，`ProjectFiles.contents` | `FileMetadata`（含 revision）；成功的小文件字节 | Effect Cache，容量 128；不超过 1 MiB 的成功读取无限 TTL，失败和超预算零 TTL；读取前后 revision 不一致时失效本次键；随 Host Scope 释放 |
| Canvas 转换缓存 | 1 处，`WorkspaceCanvasConversion.results` | 转换方向与完整输入内容；结果文本和实际资源 GUID 指纹 | Effect Cache，容量 128，总键、结果和依赖记录不超过 1 MiB；命中重新读取实际依赖，指纹变化只失效观察到的旧项；关闭时清空并中断所属转换；由 `CanvasConversionService.layer` 在工作区共享 |
| TS 视图驻留状态 | 1 处，`DeviceTsViews.views` | 零件相对路径到 `DeviceTsView`（官方 AST 与 `TsPrintState`） | 更新按片段 revision 与依赖增量复用；`remove` 删除零件；Scope 关闭清空；由 `TsViewConversionService.layer` 为工作区创建唯一实例 |

`TsCanvasConversion` 的 `rendered` 与 `encoded` 仅属于一次转换准备；`ProjectEntities` 的两个 `Ref` 是工程实体权威快照；`ProjectWatcher` 的 `pending` 与 Queue 是待消费事件；`EntityChange` 是单次候选。这些对象不构成额外内容缓存，也不得复制成第二份工作区索引。Preview 当前没有实现缓存，保留其独立 TODO 合同；Views 不建立预览注册表。

Host MUST 通过同一 ManagedRuntime 和工作区 Layer 取得上述实例；请求入口不得另建文件缓存、转换缓存、监听器或运行时。当前实现不存在旧版式的多份工作区内容缓存：文件、Canvas 转换和 TS 视图各只有一处状态归属；内存表仅在明确的单次准备或候选生命周期内存在。

#### Scenario: 工作区共享唯一缓存实例

- **WHEN** 创建工作区 Layer 并取得转换与实体服务
- **THEN** 同一工作区复用同一 Layer 值，各状态归所属 Host Scope；装配入口直接连接服务，采用 L 预算容纳其十五条直接依赖

<!-- @anchor id=workspace-state-cache-inventory -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-state-cache-inventory level=L -->

#### Scenario: 文件缓存按 revision 读取

- **WHEN** 重复读取文件或改变其 revision
- **THEN** 仅成功的小文件内容驻留，读取前后 revision 一致才交付结果；状态归所属工作区

<!-- @anchor id=workspace-file-cache-inventory -->
<!-- @architecture id=toolkit.files -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-file-cache-inventory level=S -->

#### Scenario: Canvas 缓存隔离与释放

- **WHEN** 重复转换、改变实际资源或关闭所属 Scope
- **THEN** 相同输入共享结果，实际依赖变化失效对应旧项，关闭释放缓存并中断所属转换；另一工作区保持独立

<!-- @anchor id=workspace-canvas-cache-inventory -->
<!-- @architecture id=toolkit.conversion.canvas -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-canvas-cache-inventory level=M -->

#### Scenario: TS 驻留状态按零件释放

- **WHEN** 更新、删除零件或关闭所属 Scope
- **THEN** 片段按实际 revision 与依赖复用，删除移除对应状态，关闭清空所属状态；另一工作区保持独立

<!-- @anchor id=workspace-ts-cache-inventory -->
<!-- @architecture id=toolkit.conversion.ts -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-ts-cache-inventory level=M -->

### Requirement: 可复现计算短路

所有已经避免重复计算的工作区能力 MUST 使用可从完整输入和实际依赖重建的精确键，并在命中前复核该键所声明的版本事实。文件内容复用 MUST 以 `FileMetadata` 的 revision 为依据；Canvas 转换复用 MUST 以转换方向、完整输入以及实际引用 GUID 对应路径和内容 SHA-256 为依据；TS 视图复用 MUST 以零件相对路径、片段 id、完整 `DeviceFunction` revision 和既有语义依赖 JSON 为依据。同一 Host 与 Scope 内的 Layer 实例 MUST 共享这些唯一状态，不能由请求入口或进程全局 Map 建立第二份缓存。

失败结果 MUST 不进入缓存；TS 更新失败 MUST 保留上一次有效条目；超过容量或字节预算的输入 MUST 完整计算但不驻留；Scope 关闭后缓存和进行中的转换 MUST 失效。失效只能由对应 revision、完整内容或实际依赖变化触发，不得使用无依据 TTL、可能碰撞的短哈希或猜测性黑名单。语义分析、单次候选、`TsCanvasConversion` 准备状态和当前未实现的 Preview 结果不得声称拥有跨调用缓存。

#### Scenario: 文件内容以实际 revision 复用

- **WHEN** 同一工作区重复读取文件或改变 revision
- **THEN** 相同 revision 复用成功内容；读取期间变化直接失败，失败和超预算内容不驻留

<!-- @anchor id=workspace-computation-shortcuts -->
<!-- @architecture id=toolkit.files -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-computation-shortcuts level=S -->

#### Scenario: Canvas 内容与实际依赖决定命中

- **WHEN** 重复转换相同方向与完整输入，随后改变实际引用资源
- **THEN** 相同事实复用结果，引用指纹变化仅失效观察到的旧项；无关资源保持命中，失败和超预算结果不驻留，关闭后访问失败

<!-- @anchor id=workspace-canvas-computation-shortcuts -->
<!-- @architecture id=toolkit.conversion.canvas -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-canvas-computation-shortcuts level=M -->

#### Scenario: TS 片段以 revision 与语义依赖复用

- **WHEN** 更新同一零件片段，改变其 revision 或语义依赖，随后删除零件或关闭 Scope
- **THEN** 仅完整事实相同的片段复用；更新失败保留原有效状态，删除与关闭释放所属状态

<!-- @anchor id=workspace-ts-computation-shortcuts -->
<!-- @architecture id=toolkit.conversion.ts -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-ts-computation-shortcuts level=M -->

### Requirement: 只读配置来源

配置消费 MUST 经抽象只读 `ConfigSource` 获得 `ToolkitConfig`，MUST NOT 提供写入、同步或第二来源。VSCode 的具体来源和默认值见前端规范。Pi MUST 读取官方显式 flags：必填 `toolkit-mcp-port` 为 1..65535 整数，资产参数来自显式 `toolkit-assets-config` 文件；缺失的资产输入用 Option 表达未启用。参数在入口传给 PiConfigSource，MUST NOT 消费环境变量或另造默认值。当前未实现来源继续读取失败，MUST NOT 返回伪成功。

配置值归原生来源，ConfigSource 每次读取当前来源并交付独立只读结果；已经交付的结果不随后续读取改写。必要性与取舍：已有 WorkspaceConfiguration 提供值和默认值，Zod 校验端口，Promise 表达读取结果；本合同只隔离宿主来源，不增加缓存、同步或另一套默认值。行为验证使用同一来源的实际值变化和独立预期核对新旧结果，并验证 Pi 来源明确失败。注入配置值的单元测试只证明读取与校验，原生默认值由扩展清单和实际宿主供给，不能由重复写入默认数字的测试冒充。

#### Scenario: 两种实现读取同一只读类型

- **WHEN** 任意实现读取配置
- **THEN** 返回同一只读内部类型 `ToolkitConfig`，不存在任何写入口

<!-- @anchor id=config-source-readonly -->
<!-- @architecture id=toolkit.config.logic -->
<!-- @architecture id=toolkit.config.tests -->
<!-- @budget id=config-source-readonly level=S -->

#### Scenario: Pi 占位来源保持未实现

- **WHEN** Pi 入口读取占位配置来源
- **THEN** 读取抛出含 TODO 说明的未实现错误，不返回任何配置值

<!-- @anchor id=config-pi-source-placeholder -->
<!-- @architecture id=toolkit.config.logic -->
<!-- @architecture id=toolkit.config.tests -->
<!-- @budget id=config-pi-source-placeholder level=S -->

### Requirement: 配置 schema 单一来源

现役配置 schema MUST 以 `ToolkitConfig` 与 `ConfigSource` 为内部唯一类型边界，并按宿主来源收敛：VS Code 的 `toolkit.mcp.port` 只来自原生 `WorkspaceConfiguration`，默认值只在扩展 `contributes.configuration` 声明；Pi 的 `toolkit-mcp-port` 和 `toolkit-assets-config` 只来自官方显式 flags/文件输入。配置读取每次返回独立只读值，端口统一校验为 1..65535 的整数。

配置消费不得维护第二份默认值表、TOML 同步、环境变量、密钥文件或协议层 schema；资产公开参数与可选明文凭据保持分离，未启用资产能力用 `Option` 表达。旧版 Host 配置文件只属于 legacy 参考，不参与新版来源或默认值。未实现来源 MUST 直接失败，不返回伪配置；新增配置字段必须同时更新唯一内部类型、宿主官方 schema 和对应来源验证，不能通过散落对象或隐式缺省扩展语义。

#### Scenario: 不同宿主读取同一配置合同

- **WHEN** VS Code 读取原生配置、配置值发生变化，或 Pi 尝试读取尚未接入的显式来源
- **THEN** VS Code 每次交付当前且独立的 `ToolkitConfig`，非法端口直接失败；Pi 返回明确 TODO；两者不读第二来源、不写配置、不生成默认或凭据副本

<!-- @anchor id=config-schema-convergence -->
<!-- @architecture id=toolkit.config.logic -->
<!-- @architecture id=frontend.plugin -->
<!-- @architecture id=toolkit.config.tests -->
<!-- @budget id=config-schema-convergence level=S -->

### Requirement: 资产配置与凭据分开注入

AssetGenerationConfig MUST 仅包含公开只读参数：明确的 RunningHub proxy 或 proxy-plus URL 模板，以及能力、workflowId 和实际模型节点输入绑定。能力白名单为图像生成、图像编辑、UI 贴图、视频、音乐、音效和图生 3D；九宫格属于 UI 贴图的确定性切片处理，不新增生成通道。模型绑定保存 resource/list 的文件名、类型和 createTime，以及人工收藏的实际证据，不能从模板名或模型宣传推定。未启用的资产配置在组合根以 Option 表达，不创造默认模型、工作流或密钥。

AssetCredentials.read MUST 只在远端执行边界按 AbortSignal 读取凭据，返回 `Effect<Option<string>>`：None 表达未配置，Some 表达实际凭据，来源读取错误保持失败；公开配置不携带凭据。未使用 RunningHub 时，Host 启动、工程读取、转换、Lint、预览和候选本地处理不读取或要求凭据。实际生成或远端视觉评阅由 ComfyExecution 在开始 HTTP 前读取当次凭据，None 直接报告缺少 RunningHub 凭据并结束该执行，不能变成启动条件或自动创建凭据。

凭据 MUST 是可选明文输入，不额外加密。持久化归宿主已有配置：VS Code 来源为原生 WorkspaceConfiguration，Pi 来源为 toolkit-assets-config 明确指定的配置文件；与公开参数来自同一宿主来源，但通过独立 AssetCredentials 端口交付。Toolkit 不另建密钥文件、加密层或第二配置存储，明文输入不进入公开参数、任务状态、调用记录或运行日志。入口只交付读取端口，后端不保存跨调用凭据缓存；配置修改在下一次实际远端执行读取时生效，已开始的执行使用自己的当次值。具体来源读取由各入口后续接入，本阶段只声明类型和端口，不创建配置文件或凭据存储。

必要性与取舍：ConfigSource 已承担只读入口参数，宿主已有配置承担明文持久化；TypeScript 只读类型、Effect 错误与原生 Option 足以区分未配置、已配置和读取失败。端口只把凭据限制在实际远端边界；复用宿主配置符合用户的可选明文要求，代价是按宿主普通配置文件的访问权限保存，新增加密或 SecretStorage 会改变这项要求。公开参数及凭据分别交付，无需第二配置存储或缓存。现有真实 Host 和配置回归验证仅端口配置即可启动、读取与关闭，生成空壳回归验证明确失败及输入保真；这些证据不代表远端缺失处理已实现。实际来源接入时须验证未启用时无需凭据、缺失时零 HTTP 并明确失败、来源读取失败、动态读取和凭据不进入公开输出。

#### Scenario: 资产参数合同保持只读

- **WHEN** 组合根准备资产生成输入
- **THEN** 公开参数与可选明文凭据端口分开，未配置以 Option 表达，仅实际远端执行要求凭据；具体来源和远端缺失处理保持 TODO

<!-- @anchor id=asset-config-contract -->
<!-- @architecture id=toolkit.config.logic -->
<!-- @budget id=asset-config-contract level=S -->

### Requirement: 有来源的生产工作流目录

生产工作流目录 MUST 固定官方上游版本并引用唯一平台事实来源。只有 API graph、参数绑定、原生节点和实际模型验证证据齐备的条目可用于执行。当前目录为 unavailable，workflows 为空；参考 UI graph 不进入生产条目。目录空壳不证明任何模型、能力或节点在 RunningHub 可用，执行不得因此成功。

选型：Node 原生 fetch/AbortSignal 与 Effect 4.0.0 承担后续 HTTP 执行与取消，Zod 4.6.4 承担结构校验；不新增 ComfyUI 客户端、推理引擎或生成依赖。上游 graph 格式与节点来源由目录的精确官方提交约束，模型仍必须按调用时实际创建时间窗口和收藏事实验证；锁定上游不锁死过时模型。API graph 的转换与远端验证属于后续正式实现，不进行计费探测。

目录中的平台合同引用只提供外部协议、节点和模型库的来源证据；配置来源及凭据表达遵循本规范的现役合同。官方提交和参考工作流来源足以固定后续核对依据，不能替代 RunningHub 原生节点、实际模型绑定和生产验证；阶段二保留 unavailable 及空工作流集合，避免把未经验证的参考资源接入执行。

#### Scenario: 尚无生产验证证据

- **WHEN** 请求目录中尚未验证的工作流
- **THEN** 明确失败，参考来源和上游版本不能代替生产可用性证据

<!-- @anchor id=asset-workflows-contract -->
<!-- @architecture id=toolkit.asset_generation.workflows -->
<!-- @todo -->

### Requirement: 原子生成固定原生传输合同

AssetGeneration.execute MUST 接收 GenerationRequest、显式配置和凭据端口及 AbortSignal。请求固定能力、prompt、seed、工作流具名参数与上传产物；参数仅按工作流绑定白名单解释，不能将任意参数透传为任意节点。GenerationArtifact 交付 artifactId、名称、MIME、格式与 AsyncIterable 字节块，不携带工程路径、URL 中秘密或大 base64。GenerationOutput 保留 promptId、workflowId 与具名产物；本模块不保存跨请求任务状态，不读写工程。

WorkflowBindings.select MUST 将完整请求和已验证生产目录绑定为 ComfyExecutionRequest；该请求固定原生 API graph、workflowId 和上传内容。ComfyExecution.execute MUST 使用唯一平台事实允许的 upload/image、prompt、history 和 view，HTTP 及字节下载消费信号。graph 仅来源于有版本及验证证据的工作流，节点字段按对应原生节点 schema 验证。当前所列输入类型只是阶段二的基本 graph 合同；需要其他结构化节点值时在正式实现前依据已验证 graph 补齐，不靠字符串拼接、任意对象或兼容分支绕过。

review MUST 接收所选产物与明确指令，经同一已验证原生工作流返回 promptId、workflowId 和观察意见，意见不能自动确认或替换候选。停止本地等待不证明远端已停止；远端取消只使用已经核实的任务级合同，不调用全局 interrupt。当前三个入口均明确 TODO 失败，类型以 Parameters/ReturnType 绑定选择和传输成员，只证明合同方向，不产生调用成功。

必要性：原生 fetch/AbortSignal、Effect 与 Zod 已承担 HTTP、取消、错误及结构校验，官方 API graph 和模型库事实承担输入依据。项目只声明工作流绑定和原子执行的业务接口，不自研客户端框架、解析器、模型 fallback 或执行缓存。正式实现的验收覆盖绑定、错误上传、prompt 关联、history 结果校验、流式下载、取消事实及实际产物；空壳阶段验证合法请求失败、输入和工程保持原状。

粗略输入合同以具名名义类表达文本、数字、布尔工作流参数；三者共享 GenerationParameterValue，原生节点输入复用这些标量及具名 ComfyNodeReference，节点结构由 ComfyNode 声明。共同 WorkflowInputValue 根仅表达节点可接受的输入，生成参数根保留标量边界，节点引用不作为生成参数。TypeScript 与 instanceof 承担类型和收窄，不增加序列化或执行逻辑；该声明层次替代匿名语义联合和重复节点成员。入口直接保留完整签名及明确 TODO 失败，不以同名重载隐藏输入。现役测试以非空参数、graph、模型绑定和独立预期验证失败后的输入及字节保真，不把这些测试输入当作生产验证证据。

#### Scenario: 原子生成骨架明确失败

- **WHEN** 调用 AssetGeneration.execute 或 review
- **THEN** 返回明确 TODO，不提交远端任务、不制造产物或意见

<!-- @anchor id=asset-generation-contract -->
<!-- @architecture id=toolkit.asset_generation -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=asset-generation-contract level=M -->

#### Scenario: 生成参数与产物合同保持具名边界

- **WHEN** 构造生成请求、标量参数、流式产物及评阅输入输出
- **THEN** 标量与节点引用边界分明，产物只交付身份与字节流；类型不执行远端请求

<!-- @anchor id=asset-generation-input-output -->
<!-- @architecture id=toolkit.asset_generation -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=asset-generation-input-output level=M -->

#### Scenario: 工作流绑定入口明确失败

- **WHEN** 将生成请求与显式配置交给 WorkflowBindings.select
- **THEN** 抛出具名 TODO，原请求与配置保持原值

<!-- @anchor id=asset-workflow-binding -->
<!-- @architecture id=toolkit.asset_generation -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=asset-workflow-binding level=S -->

#### Scenario: 原生传输合同与执行入口明确失败

- **WHEN** 将具名 graph、上传内容、配置与凭据端口交给 ComfyExecution.execute
- **THEN** 节点引用保留原值，入口返回 TODO，输入与上传字节保持原样

<!-- @anchor id=asset-native-execution -->
<!-- @architecture id=toolkit.asset_generation -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=asset-native-execution level=S -->

### Requirement: 调用记录由 Host 单侧驱动

Host MUST 在工作区 Scope 中共享唯一 InvocationRecordsService。Host 分派具名请求前提供 started 事件，结束时提供 finished 事件；调用标识与 operation 在开始时确定，结束事件记录 succeeded、failed 或 canceled、完成时间和诊断。InvocationRecords.run 只原子登记生命周期事件，不接收业务函数、Promise 或回调，不调用 Host 或其他业务。记录不保存秘密、完整请求响应或资产字节；业务结果由 Host 原调用返回，记录保存结果状态及诊断。未知标识读取以 Option.none 表达，错误直接失败，不能用空记录遮蔽错误。

取消执行及 AbortSignal 由 Host 的原生 Effect Scope 管理；请求中断、业务失败和 Host 关闭均须由同一入口交付终态，记录服务不建立第二执行器、监听或取消控制器。工作区关闭释放所属记录；两个 Host 的标识空间与记录隔离，不跨会话恢复。

生命周期事件 MUST 使用共同抽象根及具名开始、结束类型，结束状态采用具名字面量枚举域。只读记录保留 invocationId、operation 和 startedAt，completion 为 Option 包装的完成时间、结果及诊断；None 表达正在执行，Some 表达实际终态，不重复维护 status 或用裸缺失字段区分阶段。run 与 read 保留实际输入，执行继续 TODO。具名类型消除匿名语义联合并保留各阶段必要字段，完成信息单独包装避免两套记录及状态失配；事件合同、登记读取和订阅流分别采用 S 预算，测试验证实际四类事件失败与独立输入快照。

必要性：Effect 已提供 Scope、中断与共享 Layer，Host 已拥有执行入口；它们不提供本项目按操作标识主动读取的调用结果记录。该模块只声明只读记录及具名事件登记合同，不重复执行、调度或存储业务结果。当前 run/read/changes 明确 TODO 失败，装配成功不证明记录和取消已实现；骨架验证真实 Layer、开始及三种终态输入、读取与流消费失败和输入保真。正式实现验证先开始后终态、重复或未知标识错误、取消及关闭终态、Host 隔离和释放。

#### Scenario: 调用记录骨架明确失败

- **WHEN** 通过工作区 Layer 登记生命周期事件或读取调用记录
- **THEN** 返回明确 TODO，输入不变，不制造记录或执行外部业务

<!-- @anchor id=invocation-records-contract -->
<!-- @architecture id=toolkit.invocations -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=invocation-records-contract level=S -->

#### Scenario: 生命周期事件保留具名终态

- **WHEN** 构造开始及成功、失败、取消的结束事件
- **THEN** 事件保留调用标识、时刻、具名结果与实际诊断，完成信息通过 Option 进入记录

<!-- @anchor id=invocation-event-contract -->
<!-- @architecture id=toolkit.invocations -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=invocation-event-contract level=S -->

### Requirement: 调用记录流与展示投影

MCP 请求 MUST 经同一 Host 登记唯一 invocationId 的开始与终态，InvocationRecords 是调用状态的唯一来源；记录流从这一登记正向供结构化日志输出、只读展示和统计，不能先写文件再 tail、轮询或解析文件恢复调用状态。运行日志与调用记录的生命周期合同各自保持：Pino 负责输出，InvocationRecords 负责实际状态和诊断，前端和 Pi 只投影同一结果。

选型固定为已有 Effect 4.0.0 的 Ref、Semaphore、PubSub.sliding 与 Stream。Ref 保存所属 Host 的活动记录和最近 500 条已完成记录；登记在原生许可内校验并更新，同一 invocationId 仅一次开始和一次终态。活动记录保留到结束，不因为历史容量被提前淘汰；结束后进入有界历史，旧已完成记录淘汰后 read 返回 Option.none。展示投影按同一来源选最近 500 条，不另建前端历史。统计在实际终态登记时按既有计数合同聚合，不能从可能合并的展示流计算成功率。

changes MUST 返回原生 Stream 的只读记录快照。发布使用 PubSub.sliding 容量 1、replay 1，每次只发布同一权威状态的最新展示快照；订阅初始快照与登记在同一许可边界建立，避免先读后订阅漏掉变化。慢展示可合并中间快照，最终保留最新状态，不形成无限等待队列，也不阻塞业务执行。活动订阅绑定消费者 Scope，前端关闭释放订阅，Host 关闭关闭所属发布资源并结束等待。初始快照为空是实际无记录；阶段二 changes 明确失败，不能返回空流伪装已接入。SubscriptionRef 的原生构造使用 unbounded PubSub，会让慢消费者累积快照，本合同选择已有 sliding PubSub 表达有界展示，不自研订阅器。

同一次登记的具名事件直接交给注入 Pino 输出调用标识、operation、时刻、终态和诊断；文件落点继续归宿主运行日志输出，UI 同时消费 changes 的内存投影。输出文件被外界修改不影响权威调用状态和显示；输出错误如实暴露，不能反向改变真实业务终态。开始/结束事件只产生一次，前端状态刷新不生成外部 MCP 调用。协议解码前的失败由协议层直接报告，尚未进入 Host 的请求不伪造 Host 记录；业务开始后的失败和取消保留真实终态。

参数、结果与改写文本展示 MUST 使用所属业务定义的具名摘要：只保留该操作允许的输入字段、诊断、资源/候选/任务身份及实际变更摘要。记录不保存完整请求响应、工程源码、图像 base64、资产字节、密钥或远端带凭据 URL；未提供的摘要用 Option 表达，不从日志字符串或结果文本补猜。当前 InvocationRecord 只声明生命周期字段，摘要类型和 Webview 投影字段在对应业务正式接入前补齐，不能把现有 UI 的 unknown 展示字段当作内部业务契约。取消在展示时保留独立状态，不映射为成功、失败或永久进行中。

必要性与取舍：Effect 原生组件承担原子状态、许可、有界广播及 Scope 订阅，Pino 和宿主输出承担结构化日志文件，Webview 原生消息与 Zod 承担展示边界。项目只负责 invocationId 生命周期、500 条历史及具名摘要的业务投影，不增加消息框架、文件读取链或日志数据库。快照广播允许慢 UI 合并中间状态，换取有界内存；实际终态仍由唯一登记逐项处理并输出，统计不依赖显示速度。当前流端口保持 TODO，真实 Layer 回归消费 Stream 验证具体失败及原事件保真，现有 UI 回归只证明独立展示行为。正式实现须验证开始→终态、初始订阅竞争、慢消费者最新状态、500 条淘汰与活动记录保留、取消、关闭释放、输出与显示同源、文件污染不改变状态以及秘密和完整内容不进入摘要。

#### Scenario: 调用记录订阅骨架明确失败

- **WHEN** 通过真实工作区 Layer 消费 changes 返回的 Stream
- **THEN** 消费返回具名 TODO 错误，原事件保持原值；有界快照、日志输出与展示投影按正文目标继续实现

<!-- @anchor id=invocation-display-flow -->
<!-- @architecture id=toolkit.invocations -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=invocation-display-flow level=S -->

### Requirement: 唯一工作区服务装配

Host MUST 在同一 ManagedRuntime 中装配文件、实体、Canvas 转换、TS 转换、HTML 转换、视图、诊断、语言查询、编辑、预览与资产任务。所有有状态服务属于该 Host 工作区 Scope；请求与快照按既有入口隔离，不各自建立工作区缓存、监听或 ManagedRuntime。服务装配 MUST 复用同一 Layer 值，利用 Effect 原生 memoization 共享实体与转换依赖。

各服务的职责和所有权由 C4 的工作区生命周期子图确定。实现 MUST 在同一 Host Scope 内共享文件派生快照及转换服务，Host 按明确输入主动编排调用；模块之间只使用单向主动调用和只读数据读取。TODO 服务可以装配，执行其业务入口必须明确失败。

Host MUST 在自己的工作区 Scope 启动唯一 WorkspaceUpdates.watch，单一消费 ProjectWatcher.take 的实际变化批次，保留精确 changedRelativePaths 及由它们归并的 directoryRelativePaths。现役迁移链继续调用 ProjectEntities.refresh 实际重读，沿用实体身份交付更新和删除标识，再把原批次与实际重读结果交给同一工作区 WorkspaceUpdates.refresh。该新增端口明确 TODO 失败，不用空派生路径和空预览集合冒充输入。初次装配前监听 ready；提交错误和重读错误保持明确结果，下一次实际事件继续主动处理；关闭 Scope 取消等待、阻止后续调用并释放所属资源。

正式刷新实现 MUST 在重读前由实体 classifyChanges 区分源目录与派生纠偏路径；只按源范围重读，再取得完整 semanticSnapshot 基准，连同实际展示依赖调用 Views.refresh。WorkspaceUpdates 的结果复用 ViewRefreshResult，已打开预览的产物经其展示资源主动交付。阶段二保留已有目录重读能力及粗略输入合同；派生归属、纠偏、展示登记和影响交付明确 TODO，不提前编排未实现业务。

必要性：Effect 原生提供 Layer 共享、资源释放和中断；项目只声明实际服务依赖及所属工作区，不自造容器或生命周期管理器。组合根直接装配各具名服务依赖，职责拆分已经落实到各服务，装配场景采用 L 依赖预算；继续拆开组合根会分裂生命周期所有权。实际启动、关闭与服务调用验证组合根，未实现业务不作为成功能力。

#### Scenario: 同一工作区服务共享与释放

- **WHEN** 创建工作区 Layer，取得各服务并关闭 Host
- **THEN** 依赖指向同一实体服务和同一 Canvas 转换服务，所有资源绑定工作区 Scope；两个 Host 状态隔离

<!-- @anchor id=workspace-service-stack -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-service-stack level=L -->

### Requirement: Host 持有 Canvas 展示资源

Host.openPreview MUST 接收 OpenCanvasPreviewRequest 的源实体身份、viewport、固定字体与显式时间，以及组合根注入的 CanvasKit 和原生 AbortSignal。该请求由同一 Host 的 WorkspaceUpdates 处理，读取具名 HTML 视图并调用共享 Preview，返回属于该 Host Scope 的 CanvasPreviewDisplay；前端只消费资源，不建立源工程监听或另一个 Host。

展示资源 take(signal) MUST 主动交付 `Option<CanvasPreviewContent>`：Some 同时携带本次 HtmlViewEntity 与 Preview 的实际图片和完整依赖基准，None 表示源已删除。源或其实际引用资源变化时，由 Host 同次刷新取得影响并向已有资源交付更新；无关变化不触发渲染。面板释放调用 close，取消当前等待并撤销该展示登记；Host 关闭同时释放全部展示资源与进行中请求。files 和 memory 使用同一请求及结果合同，memory 仅在实际展示请求时生成视图。

必要性与取舍：现有 Views 提供具名视图与完整刷新影响，Preview 提供图片和实际依赖基准，Effect Scope 与原生信号提供释放和取消；这些能力不拥有前端已打开展示的工作区登记。WorkspaceUpdates 只声明该所有权、变化输入和资源交付，复用已有结果类型。逐次独立 render 无法交付引用资源变化后的更新，前端另建监听会分裂控制权；资源合同增加明确释放责任，使展示登记与 Host 生命周期一致。阶段二只声明粗略合同，打开及刷新明确 TODO，登记、等待队列、影响计算和渲染编排待正式实现；不创建占位注册表或伪资源。

#### Scenario: 两种模式的展示合同明确失败

- **WHEN** files 与 memory Host 接收具名展示请求及真实 CanvasKit 后端
- **THEN** 阶段二打开入口返回具体 TODO，请求和实际工程保持原样，关闭 Host 后拒绝请求；正式实现时验证首次产物、引用资源变化、源删除、无关变化和面板释放

<!-- @anchor id=host-preview-display -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=host-preview-display level=M -->

#### Scenario: Host 分派展示资源请求

- **WHEN** 通过 Host.openPreview 提交源身份、viewport、字体、时间和真实后端
- **THEN** 调用所属工作区 WorkspaceUpdates，当前交付具体 TODO；请求与关闭共享 Host 生命周期，十四条文件依赖采用 L 预算

<!-- @anchor id=host-preview-display-dispatch -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=host-preview-display-dispatch level=L -->

### Requirement: 预览必须追踪实际图片依赖

预览刷新判断 MUST 使用生成该视图时的完整 `EntityReadBaseline` 和实际资源依赖。Canvas 主体字节不变时，若视图引用的图片、遮罩、纹理或 Spine 文件内容、GUID 身份或 revision 发生变化，Host 同一批次 MUST 将该展示列入 `affectedPreviews`，重新读取具名视图并调用 Preview；无关资源变化不得触发渲染。判断不得只比较 Canvas 二进制、缓存文本或 renderer 自身资源指纹，也不得建立第二个资源监听。

Canvas 转换服务已经对 `resourceGuids` 投影出的实际引用读取实体并计算内容指纹；这只能证明转换结果的依赖失效，不能替代 Host 对已打开展示的刷新交付。当前 `WorkspaceUpdates.refresh`、展示登记和 Preview 渲染仍明确 TODO，阶段二只能证明资源指纹变化会使转换缓存失效、预览请求失败保真；不能声称旧版预览资源不刷新问题已经解决。

#### Scenario: 工作区预览刷新入口明确失败

- **WHEN** 将真实文件变化与实体重读结果交给 WorkspaceUpdates.refresh
- **THEN** 当前返回具名 TODO，输入与工程保持原值；正式实现按正文交付引用资源变化后的展示

<!-- @anchor id=preview-resource-refresh -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=preview-resource-refresh level=M -->

#### Scenario: 视图预览影响入口明确失败

- **WHEN** 将非空预览依赖与源更新交给 ProjectViews.refresh
- **THEN** 当前返回具名 TODO，保留完整输入；正式实现以实际基准区分相关与无关资源

<!-- @anchor id=preview-view-impact -->
<!-- @architecture id=toolkit.view_service -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=preview-view-impact level=M -->

#### Scenario: 预览渲染入口保留来源依赖

- **WHEN** 将带来源基准的视图交给 Preview.render
- **THEN** 当前返回具名 TODO，请求保持原值；正式渲染结果扩展实际资源基准

<!-- @anchor id=preview-render-dependencies -->
<!-- @architecture id=toolkit.preview -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=preview-render-dependencies level=M -->

### Requirement: 视图层只编排关联与供给

ProjectViews MUST 读取实体层交付的确定快照并调用 TS、HTML 转换服务。files 模式在加载与 Host 明确要求的相关变化时经实体层写出派生文件；memory 模式只在读取时生成或复用转换结果，不预写文件。相同实体及依据产生相同视图内容，缓存只由转换服务持有。视图层是 Issue #61 外部刷新调度的唯一接入位置：`refreshTs` 接收明确的更新或删除输入，更新携带完整 DeviceEntity 与视图相对路径并调用 TS 转换服务 `toTs`，删除携带零件相对路径并调用 `remove`；返回值以 Option 区分交付视图与删除完成。该入口不另建监听或反向驱动实体。工作区 Layer MUST 向视图层共享注入同一个 TS 转换服务，视图对象持有并调用该实例。影响范围计算、实体装配、视图关联和文件写出不属于 Issue #61；这些接入仍为 TODO，读取与写出明确失败。

主动刷新入口为 `ProjectViews.refresh(ViewRefreshInput)`：输入携带 Host 实际重读取得的 EntityReadBaseline，以及明确的 updatedEntityIds 和 removedEntityIds。标识沿用实体身份，删除输入不要求已删除实体仍可读取；更新实体经共享 ProjectEntities 获取，并核对输入基准。输出 ViewRefreshResult 携带本次实际依赖 baseline、具名 ToolkitEntity 视图集合和 removedViewRelativePaths；所有路径相对工程根。实际转换依赖进入输出基准，不能用输入基准冒充完整依赖。files 模式在返回前经实体层完成派生交付，memory 模式的源刷新只失效关联及转换结果，具名读取时才生成视图。新增影响计算、关联维护和交付保持 TODO 失败，不返回虚构结果。

ViewRefreshInput.derivedViewRelativePaths MUST 沿用实体层 EntityFileChanges 的派生路径合同，源更新集合为空仍核对这些已登记产物。previewDependencies MUST 携带 Host 已打开预览的源实体身份及上次实际渲染取得的完整 EntityReadBaseline，包含图片、骨骼、调色板和固定字体等实际依赖；Views 不另建已打开预览注册表。ViewRefreshResult.affectedPreviews MUST 交付仍存在且实际受影响的源身份及本次重读核对后的完整依赖基准，removedPreviewSourceEntityIds 明确交付已删除源身份。Host 根据同次结果主动读取具名 HTML 视图并调用 Preview 刷新已打开展示；memory 模式同样只在这个实际展示请求时生成内容。源 Canvas 字节不变但其实际资源依赖变化时仍交付影响，无关变化保持未受影响。

这些字段是阶段二粗略合同，影响计算、派生纠偏及预览交付继续 TODO 明确失败。Host 保留监听的精确变化及实际目录重读结果，交给工作区刷新端口；正式实现时由实体归属与真实展示登记产生这些字段，空集合不能代替尚未取得的输入。

工作区 Layer MUST 共享注入同一实体、TS 与 HTML 转换服务。视图只拥有正向转换与派生交付，TS/HTML 反向转换由 Editing 编排；publish 仅表达派生视图写出，不提交源实体。视图关联记录转换结果的真实依赖与具名路径，内容缓存仍唯一归转换服务。

必要性与取舍：Effect 原生 Layer 负责共享与生命周期，已有实体合同提供身份、变化分类及读取基准，已有转换能力提供内容；项目只声明源到视图关联、更新/删除批次及两种供给模式，不重复解析或缓存。只比较源 Canvas 会漏掉图片等依赖变化，另建监听或预览注册表会分裂 Host 控制权；调用方显式提供实际预览依赖，Views 结合本次实体事实交付影响，代价是每批传递已有依赖记录。关联由工作区 Scope 持有并随关闭释放，转换内容及缓存仍唯一归转换服务。实际 Host 读取入口验证 TODO，已有 refreshTs 以真实转换和缓存删除验证；空壳刷新使用两种模式、纯派生纠偏及非空预览依赖验证明确失败、输入保真和零写入。正式刷新再以相关变化、删除、无关变化及两种模式的内容一致性验收。

#### Scenario: 未接入视图按实际能力失败

- **WHEN** 在任一模式调用尚未完成的视图读取、写出或更新入口
- **THEN** 明确返回 TODO 错误，MCP 列表仍只公布真实可用工具

<!-- @anchor id=view-service-contract -->
<!-- @architecture id=toolkit.view_service -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=view-service-contract level=M -->

#### Scenario: 视图刷新合同保留完整影响输入输出

- **WHEN** 构造源更新、派生纠偏及已打开预览依赖的刷新输入
- **THEN** 输入保留实际基准，结果分别表达更新视图、删除路径与预览影响；计算与交付继续 TODO

<!-- @anchor id=view-refresh-data-contract -->
<!-- @architecture id=toolkit.view_service -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=view-refresh-data-contract level=M -->

#### Scenario: TS 统一更新接入

- **WHEN** 将 UpdateTsView 或 RemoveTsView 消息交给 refreshTs
- **THEN** 更新交付当前零件的真实 TS 视图，删除释放同一转换服务的缓存并返回 None；该入口不创建监听、缓存或文件写出旁路

<!-- @anchor id=view-ts-refresh -->
<!-- @architecture id=toolkit.view_service -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=view-ts-refresh level=M -->

#### Scenario: Host 受控读取视图

- **WHEN** 通过 Host 读取 TS 或 HTML 视图
- **THEN** 在当前 Host 运行时调用视图服务；未实现能力返回 TODO，关闭后拒绝调用

<!-- @anchor id=host-view-access -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=host-view-access level=L -->

### Requirement: 语义装配与完整读取基准

语义装配 MUST 是纯计算：输入实体层取得的逻辑快照、LocalAst、平台数据、仓库脚本和纯分析结果，输出 DeviceContext 与显式转换依据。Analysis 只依赖逻辑快照与第一层 LocalAst，MUST NOT 反向依赖 Context 或语义装配。实体层负责读取并保留实际依赖基准，语义装配不访问 Files、不拥有状态树。

装配入口采用无实例状态的 `SemanticAssembly.assemble`。输入 `SemanticAssemblyInput` 的 entities 使用现有 LogicalEntitySnapshot，提供工程身份、所属关系与仓库声明；analysis 使用现有 CallGraph，其 table 提供符号、全局变量、方法来源与 LocalAst，edges 和 unresolved 保留调用分析结果；platform 复用 DeviceConversionBasis 的 types、methods、events、enums、identifiers 字段。实体层 MUST 提供来自同一次读取的快照、分析与平台输入。输出为只读 SemanticDevice 集合，每项携带原 LogicalDevice 声明、BescriptDeviceContext 和 DeviceConversionBasis；实体层负责把结果与原始格式内容、实际读取基准组合。

必要性：已有纯分析负责符号和调用解析，已有 Context 与转换依据负责下游转换合同；纯装配衔接这些已有数据，避免实体层重复承担语义计算。平台字段直接引用 SdkPlatformData，保留原类型和只读字段，不复制平台 schema。入口使用一份完整方法签名；空壳执行明确失败。纯装配的输入输出及入口采用 S 预算，实体读取和完整基准单独计量。采用现有类型和静态纯入口即可表达单向数据流，无需新增解析器、身份模型、缓存或运行时依赖。TODO：共享仓库源码、跨零件目标和平台变更的装配行为在正式实现时验证。

已有能力与取舍：SymbolTable 提供绑定身份、来源 AST 与变量表，CallGraph 提供已解析目标及未解析诊断；DeviceContextSchema 校验元数据、变量与有序脚本组，DeviceConversionBasis 表达身份、脚本关联、目标和平台映射。它们分别承担分析、结构校验和转换输入合同，均不负责把工程声明装配成完整零件语义。继续复用这些能力，装配仅连接同次输入：按声明保持空脚本组及顺序，按绑定关联共享 AST、仓库来源和私有性，并使用分析结果形成所需目标依据；缺失来源沿用具体诊断。把装配塞入转换器会增加工程关联职责，把它放入有状态服务会重复实体层的读取生命周期。静态纯入口保留一次计算所需关联工作，换取可重复计算及单向依赖；实际读取、版本复核和结果生命周期仍由实体层承担。

实体层的 `LogicalEntitySources.semanticInput` MUST 接收当前 LogicalEntitySnapshot，准备 SemanticReadInput，将语义装配输入与 EntityReadBaseline 一起交付。基准的 files 复用 EntityFileDeclaration，directories 复用 EntityDirectoryDeclaration，fixedInputs 记录具名固定输入的 revision；只记录本次实际依赖，不能以全工程快照替代依赖闭包。`ProjectEntities.semanticSnapshot` 在实体串行范围内连接该读取入口与 SemanticAssembly.assemble，返回 devices、原 entities、原 analysis 与原 baseline，错误按实际步骤暴露。原快照与分析以同一引用交付，使下游复用同次读取，不另造快照或重复计算分析。TODO：语义读取入口当前明确失败，编排不提供可用的完整语义快照。

必要性：复用已有实体读取服务与 Semaphore 保持 IO 所有权和读取生命周期一致；具名基准只补充既有文件及目录声明尚未覆盖的固定输入版本。该结构属于实体服务，不新增状态树、缓存或文件读取旁路。

读取基准 MUST 覆盖本次实际使用的零件主体、所属清单/索引、仓库引用、跨零件语义目标、平台数据和内联/独立 Canvas；预览额外包括实际图片、atlas、骨骼、纹理、调色板及字体。工程依赖由实体身份、工程相对路径与 revision 表达；固定字体和平台输入的版本也进入基准。平台数据来自宿主组合根装载的包内唯一结构化 SDK 文档，经 SdkDocument 校验后显式供实体层提供给纯装配，其内容版本进入 fixedInputs。PlatformFiles 只负责运行时工程文件 IO，不承担部署 SDK 资源读取。缺失引用是具体诊断，可选值只用 Option。提交复核实际依赖，无关变化不使有效候选失效。

#### Scenario: 实体语义读取入口明确失败

- **WHEN** 调用实体语义输入准备或 semanticSnapshot
- **THEN** 当前返回具体 TODO，保留输入；完整依赖读取与基准失配按正文目标实现。实体读取复用现有快照、串行范围与语义输入，关联文件的十七条直接依赖采用 L 预算

<!-- @anchor id=semantic-assembly-contract -->
<!-- @architecture id=toolkit.entities -->
<!-- @budget id=semantic-assembly-contract level=L -->

#### Scenario: 语义装配空壳明确失败

- **WHEN** 调用尚未实现的 SemanticAssembly.assemble，输入为空快照或已有工程声明
- **THEN** 抛出标明完整零件语义装配待实现的 TODO 错误，输入保持原样，不交付空集合为成功结果

阶段二验收以实际调用的错误和输入前后内容验证空壳边界；成功返回、普通错误或调用前修改输入均应使测试失败。完整引用装配和读取基准失效的行为随正式实现验收。

<!-- @anchor id=semantic-assembly-pending -->
<!-- @architecture id=toolkit.semantic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=semantic-assembly-pending level=S -->

### Requirement: 结构化 SDK 文档与双向注册合同

SDK 数据权威为 repo-layout 规定的固定版本结构化平台文档，内容白名单为公开接口、签名、格式、可观察行为及可公开访问的来源资料。运行时 MUST 只消费同一包内文档，文档查询、类型声明、转换、语义与 Lint 使用该数据，MUST NOT 分别维护方法、事件、枚举或继承注册表。legacy 的手写 definitions 与单例注册表只作同源参考，旧版硬编码 SDK 方法签名继续保留，不作为新版生产数据或缺失文档时的 fallback。

选型为 UTF-8 JSON、标准 JSON.parse/JSON.stringify 与现有 Zod。导出方 MUST 从权威结构化定义取得完整数据，用标准库 JSON 序列化；工具只接收符合内容白名单的结构化数据，不从 Markdown、TS 声明、TSV 或字符串切割反推。导入方 MUST 将 JSON.parse 结果直接交给具名 Zod schema.parse，导出方使用同一数据合同校验后 JSON.stringify。双方保持 formatVersion、sourceRevision、平台语义和文档条目，Option 在 JSON 边界显式编码为 null、内部复原 Option；不直接序列化 Effect Option 的内部对象。格式版本明确支持范围，未知版本与不完整数据直接失败。

粗略 SdkDocumentData 包含格式版本、权威来源 revision、SdkPlatformData 与具名文档条目；SdkPlatformData 通过 Pick 复用 DeviceConversionBasis 的五组平台字段，SemanticAssemblyInput 引用同一类型。继承、零件能力、事件适用范围与文档符号身份的完整字段仍 TODO，正式 schema 必须覆盖真实权威定义，不能把当前五组字段宣称为完整 SDK。注册是对已验证文档的只读查询投影，标识重复、引用不存在或双向方法/参数映射冲突必须报错，不采用旧注册表的后值覆盖。具名文档条目与实际符号关联，声明打印是下游输出，不成为导入格式。

宿主组合根在工作区创建时通过原生文件 IO 装载固定包资源并导入一次，将只读文档显式供同一 Host 的消费者使用；该固定输入随 Host 生命周期释放，不另建可变全局单例或监听源 SDK。SDK 资源唯一源点按 repo-layout 的 res 合同登记并进入两种部署包。fixedInputs 的平台 revision MUST 包含格式版本、来源 revision 和实际文档内容的确定性版本，文档语义或映射变化使旧语义与候选基准失效。具体资源和装配尚未落地时继续明确 TODO，不创建空资源冒充可用 SDK。

必要性与取舍：标准库承担 JSON 语法与字符串转义，Zod 承担外部结构校验，现有转换依据承担平台映射；项目只需权威定义到同一结构化文档的语义投影及只读查询。JSON 相比旧 TSV/手写注册能直接表达有序参数、继承与文档，代价是完整 schema 和固定版本数据仍需接入真实平台交付。复用原平台类型避免复制表，纯 read/write 入口表达双向边界；不新增依赖、解析器、运行时数据树或安装旁路。依据为 [ECMAScript JSON](https://tc39.es/ecma262/multipage/structured-data.html#sec-json-object)、[Zod codecs](https://zod.dev/codecs) 的双向转换能力与当前转换依据的唯一映射校验。当前正式数据尚未提供，完整数据接入和生产往返验证均保留具体待办，验收使用独立平台数据与可观察行为。

#### Scenario: SDK 文档导入导出空壳明确失败

- **WHEN** 调用 SdkDocument.read 或 write
- **THEN** 分别抛出具名导入或导出 TODO，输入保持原样，不交付空注册表或部分文档

阶段二验证实际入口错误与非空输入保真。正式实现后验收必须以独立权威样本核对来源、完整符号及中文/转义文档、有序参数、继承和双向映射；仅 write→read 自往返不足以证明源导出正确。截断、错误版本、重复标识、丢失字段和反向映射错误必须使功能测试失败。

<!-- @anchor id=sdk-document-contract -->
<!-- @architecture id=toolkit.semantic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=sdk-document-contract level=S -->

### Requirement: 诊断业务消费纯分析

诊断服务 MUST 从实体基准准备纯分析和 Lint 输入，持有诊断结果并通过受控入口返回；源码解析与符号构建仍归纯库，纯分析合同见 `openspec/specs/analysis/spec.md`。TODO：当前 inspect 明确失败，诊断规则与结果交付后续填充。

一次检查的输入准备顺序 MUST 为：实体层取得逻辑快照和实际脚本正文 → 第一层解析为 LocalAst → SymbolTableBuilder 关联零件方法绑定、来源和变量 → CallGraphBuilder 解析调用边与未解析调用 → SemanticAssembly 结合该分析与显式平台输入装配完整 Context → 实体层交付同次 semanticSnapshot 及读取基准 → Diagnostics 按请求范围和方法绑定准备 ScriptLintInput → ScriptLint.inspect 生成局部规则诊断 → CallGraphLint.inspect 对原完整分析执行一次跨方法检查 → Diagnostics 交付原分析及具名诊断结果。Host.inspect 是受控请求入口，不承担这些计算；Diagnostics 取得已由实体层准备的分析，不再重复构建。分析位于 Context 装配之前，Lint 消费其后的一致结构化输入；TS/HTML 打印、视图落盘及 PNG 渲染不参与该计算链。

阶段二的 Host.inspect 已分派到工作区唯一 Diagnostics；Diagnostics.inspect 在进入输入准备前明确 TODO 失败。SymbolTableBuilder 和 CallGraphBuilder 的纯计算可独立执行，不能据此声称 Host 已完成 Lint。语义读取、Context 装配和 ScriptLint 的各自入口也保持具体 TODO，独立测试分别证明其执行边界；错误不会被转换成空诊断。此顺序复用已迁移解析、实体与纯分析能力，避免以视图重新解析或独立 IO 形成第二份输入，代价是诊断须等待完整语义读取接通。

`Diagnostics.inspect` 接收 DiagnosticRequest：entityIds 指定实体身份范围，baseline 使用 EntityReadBaseline 固定调用方实际读取的依赖。请求不再接收可由调用方任意构造的 LogicalEntitySnapshot 作为基准。诊断服务持有工作区 Layer 注入的唯一 ProjectEntities，主动取得 semanticSnapshot 中同次读取的 entities、analysis、devices 与 baseline，并核对请求基准；期间相关依赖已变化时报告实际失配。空 entityIds 表示空检查范围，不能隐式扩张为全工程。全工程检查由调用方显式提供实体集合。

实体结构诊断使用既有 Diagnostic 的 entityId、sourceRelativePath、message，并复用 LintSeverity；方法级与全图级脚本诊断直接保留共同 LintDiagnostic，不复制其规则、引用身份或位置模型。DiagnosticResult 交付本次实际依赖 baseline、原 CallGraph、实体 diagnostics 与 scriptDiagnostics。分析的来源诊断和未解析调用仍保留在原 CallGraph 中，不另建相同诊断副本。源码解析、实体读取或纯计算失败经 Effect 原样暴露，不能返回部分成功或空诊断代替失败。

Lint 输入按分析的方法绑定关联同次语义快照中的完整 Context 与 LocalAst，复用 ScriptLintInput 合同；共享仓库源码保持每个绑定的独立作用域。Diagnostics 只拥有诊断编排和结果，不反向驱动实体、转换或 Views，不建立监听。诊断结果只代表其交付基准；后续受控读取必须核对实际依赖，不能把旧结果冒充当前检查。

选型与必要性：Effect 原生 Layer 共享工作区实体服务，Scope 管理生命周期，Effect 保留原始失败；实体语义读取已准备纯分析结果，Diagnostics 直接复用，避免再次读取并拼接不同修订。LocalAst、Context、CallGraph 与 ScriptLint 已承担格式、作用域、分析和纯检查；本服务只声明范围、基准核对及具名结果合同。实体服务、实体基准、纯分析及 Lint 的四个直接文件依赖分别提供装配、修订证明、调用分析和诊断格式，均有不可替代的合同职责，超过 S 的两项依赖上限；这些合同属于同一诊断入口，拆分会增加跨模块传递，因此正式区采用最小可容纳的 M 预算。当前 inspect 保持 TODO，不加入假调用或诊断缓存；正式实现以实际文件变更导致基准失配、绑定作用域、独立预期诊断和原始错误传播验收。

诊断结果的持有归 Host 工作区 Scope，调用方消费带基准的只读结果；服务关闭随同一 Scope 释放，受控调用与取消继续由 Host 管理。独立解析或为诊断再建立监听会复制实体权威；复用同次语义读取减少重复计算，代价是完整语义装配尚未实现时诊断也明确失败。当前不建立结果注册表或猜测缓存策略。阶段二使用唯一实体 Layer 直接装配诊断，并在 files、memory Host 中执行具名请求，验证空范围和显式实体范围均返回具体 TODO、请求与实际工程保持原样，以及关闭后的 Host 拒绝诊断调用；这些证据不代表检查范围计算、过期基准拒绝或规则执行已完成。测试使用实际实体与文件服务、独占工程资源及 Host 入口共同验证装配和 IO 边界，替身不能提供该行为证据。

#### Scenario: 诊断骨架不伪造结果

- **WHEN** 请求未实现的诊断
- **THEN** 返回 TODO 错误，不返回空列表冒充检查通过

<!-- @anchor id=diagnostics-service-contract -->
<!-- @architecture id=toolkit.diagnostics -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=diagnostics-service-contract level=M -->

#### Scenario: Host 分派诊断请求

- **WHEN** 通过 Host.inspect 提交具名请求
- **THEN** 使用所属工作区唯一诊断服务，当前交付具体 TODO，关闭后拒绝调用；Host 生命周期与请求分派共享十四条文件依赖，采用 L 预算

<!-- @anchor id=host-diagnostics-dispatch -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=host-diagnostics-dispatch level=L -->

### Requirement: 语言查询复用实体与诊断

`LanguageQueries.query` MUST 接收具名 LanguageQueryRequest。NavigationQueryRequest 的 kind 为 definition、references 或 hover，并携带位置；DocumentDiagnosticsRequest 查询明确文档的诊断，不接收无意义的位置。两者共享 LanguageDocument 和原生 AbortSignal。文档携带 entityId、工程相对 documentRelativePath、完整 text、textVersion 与 EntityReadBaseline；实体身份确定共享仓库源码所属引用作用域，文本版本用于关联编辑器请求，不能代替实际依赖基准。位置采用 0-based 行和 UTF-16 character，范围的 end 为排他端点。未知请求、无效位置或文本与实体视图不一致 MUST 明确失败；未保存文本的语义分析留待有明确合同的后续实现，当前读取基于实体已交付的文本。

工作区 Layer MUST 注入同一 ProjectEntities 和 Diagnostics，复用组合根的 Layer 值。查询主动取得实体语义快照，消费同次 CallGraph.table、edges、unresolved 和完整基准；定义、引用与悬停不重新解析源码或构造符号表。诊断由 Diagnostics.inspect 交付，使用请求实体的明确范围；相关依赖变化 MUST 失败，不拼接不同修订。查询只读，不拥有工程状态、监听、视图刷新或提交权限。

LanguageQueryResult MUST 携带请求文档相对路径、文本版本和实际依赖基准。结果对象尚未接通，具体导航位置、悬停内容和诊断报告的字段在正式消费者出现时按本合同增加；不得在当前无消费者时导出未实现的结果类。无匹配导航结果是空列表，不能用于掩盖读取或计算失败。正式结果中的来源定位保留 methodKey、原 CallGraph 来源定位和目标文档相对路径；生成视图范围用 Option 包装，只有确切映射才交付 Some。来源正文的 1-based 行与 sourceRef 保留原语义，不能直接当作 TS 坐标。缺少映射时保留源定位和 None；不捏造行列。诊断保留既有实体及脚本位置模型，前端展示映射在正式实现前补齐经验证的合同。

query 的结果类型 MUST 使用已有 LanguageQueryResult 抽象根，不使用语义联合或合并成带空字段的大对象。阶段二仅有 Host 通过抽象根消费结果，具体结果对象尚未实现，待实际查询消费者和结果计算接通后再增加具名实现；不得为了预留 API 导出无消费者的具体类。阶段二实现保留完整请求参数并明确 TODO 失败；真实查询验证四类请求的文档、文本版本、文件/目录/固定输入基准及原生信号保真，实际导航、诊断映射和取消执行仍待正式实现。

公开边界由实际消费者决定：LanguageQueries 的 query 签名公开，Host 直接复用其成功类型；抽象结果根在模块内声明并通过该签名传递，不额外导出具名符号。删除尚未消费的具体结果构造器及来源位置类型，保留结果基准和文档关联的粗略骨架。取舍：TypeScript 已校验签名传递和结果基准，Knip 校验额外导出的可达性；提前公开具体类增加未验证的 API，当前不会提供查询能力，故在真实消费接通时再补。定义与引用仍须交付具名位置结果，悬停内容缺失用 Option.none，诊断报告直接复用 Diagnostics.inspect 成功类型且与结果基准一致；这些目标不因骨架收敛而改变。

TsPrintState.positions 已交付方法边界的 UTF-16 偏移和 1-based 行号，语言查询应复用该依据及官方 TS AST；该边界尚不提供 BEScript 来源节点到生成 TS 字符范围的完整映射，不能直接扩张为已实现的导航或诊断定位能力。

取消生命周期归 Host；查询消费请求的 AbortSignal，可取消操作随之结束，交付后前端按文档与版本关联结果。TODO：query 当前明确失败，实体完整语义读取、查询、坐标映射和取消执行尚未接通；Host 受控入口及标准 LSP 接入按各自步骤收敛，不以装配冒充已实现能力。

选型与必要性：Effect 提供共享 Layer、Scope 和中断，实体提供确定快照及完整读取基准，已有 SymbolTable/CallGraph 提供引用作用域与来源，Diagnostics 提供唯一诊断结果。TsPrintState 当前提供生成文本与片段缓存，尚无完整导航坐标映射；TypeScript 官方 AST 可承担后续生成文档定位，不自造文本解析器。语言服务只声明请求关联、只读查询及交付合同，不新增依赖或第二状态树。正式区采用 M 预算，实体服务、实体基准和诊断三项文件依赖承担当前装配与基准合同，分析结果的消费随正式查询接通；真实工作区装配与文件零写入由测试覆盖。空壳测试仅证明四类请求明确失败和输入、磁盘保真；正式验收须覆盖独立预期定义/引用、共享源码不同作用域、UTF-16 范围、诊断定位、过期基准/文本和取消。

#### Scenario: 查询输入关联文档与引用作用域

- **WHEN** Host 准备导航或文档诊断请求
- **THEN** 具名输入保留文档身份、文本版本、完整基准和取消信号，导航位置使用 0-based UTF-16 坐标；正式校验和查询仍为 TODO

<!-- @anchor id=language-query-input -->
<!-- @architecture id=toolkit.language -->
<!-- @budget id=language-query-input level=M -->

#### Scenario: 查询结果保留来源与版本

- **WHEN** 语言查询交付正式结果
- **THEN** 导航、悬停与诊断使用具名结果，保留实际基准与文本版本；缺少范围或悬停内容以 Option 表达，诊断直接复用原合同；正式结果计算仍为 TODO

<!-- @anchor id=language-query-output -->
<!-- @architecture id=toolkit.language -->
<!-- @budget id=language-query-output level=M -->

#### Scenario: 语言查询空壳明确失败

- **WHEN** 在真实工作区请求定义、引用、悬停或文档诊断
- **THEN** 返回明确 TODO，保留请求及源工程内容，不返回虚构查询结果

<!-- @anchor id=language-query-contract -->
<!-- @architecture id=toolkit.language -->
<!-- @architecture id=toolkit.language_tests -->
<!-- @budget id=language-query-contract level=M -->

#### Scenario: Host 分派语言查询

- **WHEN** 通过 Host.query 提交具名查询
- **THEN** 使用所属工作区唯一语言服务，当前交付具体 TODO；请求取消与关闭沿用 Host 生命周期，十四条直接依赖采用 L 预算

<!-- @anchor id=host-language-dispatch -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=host-language-dispatch level=L -->

### Requirement: 脚本静态检查使用完整结构化输入

`ScriptLint.inspect` MUST 是无实例状态的纯计算，接收 ScriptLintInput 并返回只读 LintDiagnostic 集合。输入的 methodKey 复用 CallGraph.table.methods 的引用身份，ast 复用 LocalAst，context 复用该引用所属零件的完整 BescriptDeviceContext，analysis 复用同一次读取的 CallGraph。Diagnostics 负责准备这些一致输入；共享仓库源码在不同零件引用下分别检查，不以源码路径合并语义作用域。Lint 不读取工程、不解析文本、不重新生成符号表或调用图，不持有诊断状态。

诊断 MUST 携带 ruleId、severity、message、methodKey 与 location。severity 沿用既有 info、warning、error、fatal 四级；规则标识由具体规则规范确定，本空壳不复制旧规则表或臆造规则。location 使用源工程 sourceRelativePath，line 是 LocalAst.sourceText 内的 1-based 正文行，sourceRef 指向该 AST 的来源节点；无法定位到正文行或节点时分别显式 None，不伪造 0 或生成后 TS 坐标。源码路径及引用身份来自分析结果，语句行及来源引用来自 AST；诊断不修改这些权威数据。前端坐标映射由后续语言查询消费。

选型与必要性：已有 Chevrotain 解析与 LocalAst 合同承担 BEScript 语法，Context 承担零件组成，CallGraph 承担符号和调用解析；ESLint 检查的是 TypeScript，不能解释游戏引用作用域和 BEScript 语义。项目只保留基于这些结构化结果报告确定脚本错误的纯入口，不重复实现解析器、图算法或通用 lint 引擎。旧 C# 规则仅作行为参考，新增规则须有权威规范及独立行为证据；需要平台签名等额外数据的规则，在正式实现前补齐真实输入合同，不能从 Context 或分析中猜测缺失数据。

TODO：inspect 当前明确抛错，不返回空诊断冒充检查完成。正式实现逐规则以独立预期验证合法输入、代表性错误、共享源码不同引用作用域和实际来源定位；诊断状态、实体结构及资源读取错误由 Diagnostics 负责交付。

同步入口只消费已完成的结构化读取，不建立 Effect 服务或工作区缓存；异步读取和取消由调用方负责。已有 CallGraph 的来源诊断与未解析调用由 Diagnostics 保留，Lint 不复制相同诊断。相较于独立读取或重新分析，复用同次输入保持绑定作用域一致并减少重复计算，代价是调用方必须准备完整输入。阶段二行为验证以真实解析的共享 AST、完整零件 Context 及实际构建的 CallGraph 调用空壳，覆盖不同方法绑定及重复调用，确认具体 TODO 错误与输入保真；该证据只验证空壳边界，规则准确性在正式实现时逐项验收。

#### Scenario: 纯检查空壳明确失败

- **WHEN** 调用尚未实现的 ScriptLint.inspect
- **THEN** 明确返回 TODO 错误，不读取文件、不改变输入、不保存或虚构诊断结果

<!-- @anchor id=script-lint-contract -->
<!-- @architecture id=toolkit.lint -->
<!-- @architecture id=toolkit.lint_tests -->
<!-- @budget id=script-lint-contract level=S -->

### Requirement: 变更计划消费完整输入进行纯计算

`ChangePlan.plan` MUST 消费 ChangePlanInput：当前 LogicalEntitySnapshot、EntityReadBaseline、CallGraph 和具名 ChangePlanRequest。零件、Canvas 与脚本请求分别携带同种格式实体的 original 和 updated；updated 来自已完成的反向转换，计划不解析视图文本。显式操作请求复用现有 LogicalEntityOperation，重命名及导入导出使用其已有移动、创建、删除和更新合同，不新增第二套文件动作模型。资产生成接入保持后置。

输出 ChangePlanResult MUST 携带只读 operations、diagnostics 和本次实际依赖 baseline。operations 使用已有 LogicalEntityOperation 集合；diagnostics 使用 subjectId 与 message 表达具体问题。输入基准涵盖原始读取，计划发现的关联依赖也须来自调用者已提供的快照与分析；未提供所需来源时必须失败，不能访问文件补齐。计划不执行操作、不创建 EntityChange 或 Schematics Tree、不分配 GUID，也不调用实体服务或转换服务。

Editing MUST 拥有反向转换、调用计划、分析验证与候选物化的单向编排；Entities 拥有实际基准复核和文件提交。零件 Context、脚本 LocalAst 与 CanvasDocument 已由各格式实体携带，计划直接消费已有实体合同，不另建或重复传递这些模型。TODO：plan 当前明确抛错，不能返回空操作集合冒充完成。

请求 MUST 经共同抽象根 ChangePlanRequest 和零件、Canvas、脚本、显式实体操作的具名请求类型表达；每类格式编辑只接收同种 original/updated，操作请求直接携带已有 LogicalEntityOperation。plan 使用单一完整输入签名，空壳阶段未消费输入以 `_` 标识，明确抛出 TODO 错误。

快速重构意图 MUST 经独立 RefactorRequest 进入 RefactorChangePlanRequest，携带目标 subjectId 和原读取 baseline；不能用一个已准备的 LogicalEntityOperation 冒充变量重命名或引用级联计划。阶段二以 RenameRefactorRequest 表达局部变量、零件全局变量及脚本命名三个作用域和批量 oldName/newName；这是粗略声明，不是最终工具 schema。局部变量以脚本为目标，全局变量以零件为目标，脚本命名以方法绑定为目标；实体读取与分析确定实际引用，不能按文本替换或沿用旧版“不级联代码仓库”的快捷规则跳过关联验证。共享仓库脚本的影响必须按全部方法绑定检视，无法成立的重构报告具体诊断。

旧快速重构能力按意图归并：变量与脚本命名、文件移动、脚本仓库导出/绑定/删除、脚本删除与零件大纲排序、Canvas 抽取/引用/内联、变量作用域变更。它们均由同一纯计划输出已有创建、移动、删除、脚本/list/变量更新操作；路径与 GUID 输入由 Editing 经实体查询统一定位，再交付完整来源。移动保留实体 GUID，内联与导出按实际引用决定源对象是否保留，删除须验证引用，不能把多文件操作拆成逐文件提交。其余具名意图字段、Canvas 源位置及协议 schema 留待对应业务实现；本阶段只建立共同意图边界及重命名粗略请求，不复制旧工具的 force、默认值、路径特判或调度锁。

必要性与取舍：现有格式实体承载原始内容与反向语义，LogicalEntityOperation 已表达实体操作，CallGraph 已承担引用分析；这些模块不负责把编辑意图与关联影响收敛成统一操作集合。直接在 Editing 内计算会混合纯计划与有副作用编排；另造文件动作则重复 Schematics 与实体操作合同。静态纯入口只拥有意图与关联影响计算，复用原有数据类型和 EntityReadBaseline，无状态且生命周期限于一次同步调用。具名请求比匿名联合增加少量类型声明，但与现有强类型实体操作一致，由 TypeScript 约束同种实体配对，避免匿名对象传播。空壳行为验收使用非空快照、完整基准及真实 Canvas 编辑和显式操作，验证调用明确失败且输入保持原值；正式实现的验收须覆盖仓库脚本多处引用、跨零件关联、Canvas 身份保留、原基准与关联基准，以及准备阶段零 IO。

#### Scenario: 纯计划空壳明确失败

- **WHEN** 以格式实体编辑、重构意图或显式操作的完整输入调用尚未实现的 ChangePlan.plan
- **THEN** 明确抛出 TODO 错误，输入快照、分析、基准及请求保持原值；不生成操作、候选或身份，不读写工程文件

<!-- @anchor id=change-plan-contract -->
<!-- @architecture id=pure.change_plan -->
<!-- @budget id=change-plan-contract level=M -->

#### Scenario: 重构意图保留目标与原基准

- **WHEN** 构造变量或脚本重命名意图并交给纯计划
- **THEN** 具名意图保留 subjectId、baseline、作用域及批量新旧名称；计划入口仍明确 TODO

<!-- @anchor id=change-plan-refactor-input -->
<!-- @architecture id=pure.change_plan -->
<!-- @budget id=change-plan-refactor-input level=S -->

### Requirement: 编辑业务统一经实体层持久化

`Editing.replace` MUST 接收具名 EditingRequest：ReplaceTsViewRequest 携带原 TsViewEntity，ReplaceHtmlViewRequest 携带原 HtmlViewEntity，两者共享完整 updatedText 及 EntityReadBaseline；实际种类按具名请求类确定，未知请求明确失败。不接受任意 ToolkitEntity 子类或由调用方准备的反向语义冒充视图编辑。Views 只供读取当前具名视图，实体服务提供同次格式实体、语义快照与完整读取基准；原视图、原始格式实体及请求基准必须一致，相关输入已变化时明确失败。

Editing 的工作区 Layer MUST 共享注入同一 ProjectEntities、ProjectViews、TsViewConversion 和 HtmlViewConversion，复用组合根已有 Layer 值，不能另建转换缓存。TS 使用现有 fromTs，独立 HTML 使用现有 fromHtml；更新文本必须通过转换层的正式结构化解析，原视图、原实体与请求不得修改。Editing 将 original/updated 格式实体、同次逻辑快照、分析及完整基准交给 ChangePlan.plan，随后以纯分析校验候选语义。计划不重复转换、不调用服务、不建立 Tree。

`EditingCandidate` 是只读交付封套：change 为唯一 EntityChange/Schematics 候选，baseline 为计划与验证阶段的完整实际依赖基准，diagnostics 直接复用 ChangePlanResult 的诊断合同；封套不建立第二棵可写树或第二套动作。Editing 经实体原语将计划操作物化为同一候选，准备过程只读取实际依赖并修改候选内存，不写工程、不发布视图、不分配运行时候选注册表。

patch 接入完成文本匹配后，MUST 将完整更新文本和原视图基准交给 Host.replace → Editing.replace → 共享反向转换 → ChangePlan 与纯分析校验 → EntityChange 候选；提交沿 Host.commit → Editing.commit → Entities 基准复核与文件原语 → 实际重读 → Host 主动刷新。匹配不承担工程 IO 或语义分析，其具体规则按正式接入单独验收。当前 MCP 只登记已实现的工程状态工具，patch 尚未注册，Host 的 replace 与 commit 在对应 Editing 入口明确 TODO 失败。

快速重构的业务入口为 Host.refactor → 同一工作区 Editing.refactor。Editing 读取目标及实际关联实体、完整语义快照与分析，核对意图 baseline，将 RefactorChangePlanRequest 交给 ChangePlan.plan，再校验计划并物化既有 EditingCandidate；提交复用 Host.commit 的同一链。结构化重构直接消费实体语义，只有输入来自视图文本时才使用对应共享反向转换。对外 MCP 重构工具将把输入映射到这一入口，适配层只负责协议及坐标，不能自行计算计划或写文件。VS Code 前端遵循只读发布合同，不提供重命名或修改性 CodeAction。当前 Host.refactor 已分派到 Editing.refactor 的具体 TODO 失败；MCP 重构工具尚未接入。

必要性与取舍：旧实现已证明这些意图涉及脚本、变量、list、GUID 与引用的组合操作，现有实体 CRUD 不表达完整意图；复用 ChangePlan、CallGraph、EntityChange 和同一提交链可保持单一 IO 边界。增加一个重构准备入口而复用候选结果，避免每个工具建立独立写入和锁；RefactorRequest 与计划封套共用同一意图对象，不复制请求数据或持有跨请求状态。阶段二验证真实 Layer 和 Host 的具体失败、重命名输入与完整基准保持原值以及工程零写入；正式引用级联与变更结果仍按业务实现验收。

候选是调用方持有的单次操作内存分支，不是落盘草稿或跨请求恢复机制。准备与校验失败 MUST 保留源工程和视图原状，不能自动保存草稿文件、登记候选恢复标识或提供草稿提交与清理入口。EntityChange 的 branch/merge 只组合同一读取基准上的内存动作，不能充当会话草稿存储。必要性与取舍：Schematics 已提供隔离候选及冲突检测，调用方持有候选即可完成准备与提交；再存一份失败视图会引入第二份内容、失效及恢复生命周期。操作内候选减少状态与持久化，失败后重新准备须显式读取有效基准。阶段二回归分别核对准备和提交的具体 TODO 错误、原视图和非空候选内容、实际文件与目录版本，错误步骤错接或创建草稿文件均使回归失败。

`Editing.commit` MUST 接收该完整封套，交由实体层复核文件、目录与固定输入基准并提交；boolean 只表达成功完成且实际重读后的变更结果。过期、冲突、结构或语义错误明确失败，多文件提交失败必须暴露实际结果，不能自动回滚或将候选内容冒充落盘结果。实体层重读实际文件，Host 再主动调用 Views 刷新消费者；Editing 不调用 publish，不反向调度 Views，不直接调用 Files。

TODO：replace、refactor 与 commit 继续明确失败。既有 ProjectEntities.prepareOperation 和 EntityChange 支持单操作准备与同一文件基准的分支合并，尚不承担 ChangePlan 的完整多操作依赖闭包及 fixedInputs 复核；正式接通前必须在实体模块补齐该合同，不能在 Editing 绕过 IO 边界或逐操作提交制造半成功。资产 importAsset 的请求与候选准备按实施顺序单独后置，本项不实现或声明猜测的资产入库参数。

阶段二 replace 与 commit 的实现 MUST 保留完整具名输入。读取基准和候选诊断由上游交付，本服务不另建可变版本或重复校验器；空壳验证同时覆盖非空文件、目录及固定输入基准与实际内存候选的内容和诊断，失败后与调用前独立快照一致。该验证证明输入及磁盘保真，完整基准复核与候选提交仍由正式实现验收。

必要性：现有转换承担结构化反向语义，ChangePlan 表达强类型操作和诊断，分析库承担引用验证，EntityChange/Schematics 承担内存候选与冲突，Entities 承担读取和持久化；这些模块不拥有视图编辑到候选提交的编排。Editing 只连接这些已有能力并交付完整基准，不新增依赖、解析器、状态树或候选存储。正式区使用 M 预算；测试使用真实工作区装配、两种合法视图与实际非空 EntityChange，验证 TODO 失败、原文档与候选保持不变以及工程零写入。该空壳证据不证明正式编辑已实现。正式验收覆盖 TS/HTML 反向保真、关联影响、过期基准、操作冲突、准备零写入及提交失败后的实际重读。

#### Scenario: 编辑骨架保留原始工程

- **WHEN** 调用未实现的视图编辑、快速重构准备或提交
- **THEN** 明确返回 TODO，磁盘保持原状

<!-- @anchor id=editing-service-contract -->
<!-- @architecture id=toolkit.editing -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=editing-service-contract level=M -->

#### Scenario: 视图编辑请求保留种类与基准

- **WHEN** 构造 TS 或 HTML 完整替换请求
- **THEN** 具名请求只接收对应视图，保留完整文本和原读取基准

<!-- @anchor id=editing-request-contract -->
<!-- @architecture id=toolkit.editing -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=editing-request-contract level=M -->

#### Scenario: Host 分派准备与提交

- **WHEN** 通过 Host.replace、refactor 或 commit 请求编辑业务
- **THEN** 使用所属工作区唯一 Editing，当前各入口交付具体 TODO，输入和工程保持原值；请求与关闭共享 Host 生命周期，十四条直接依赖采用 L 预算

<!-- @anchor id=host-editing-dispatch -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=host-editing-dispatch level=L -->

### Requirement: 预览业务拥有渲染任务与缓存

预览入口 MUST 接收具名 CanvasPreviewRequest：调用方已取得的 HtmlViewEntity、显式 viewport、固定字体集合及有限 timeSeconds。文档唯一来自该视图的 document，完整 HTML/XML 文本仍由 Host 单独交付；预览不再次解析文本、不调用 Views、不驱动转换或派生文件写出。调用方将组合根初始化的 CanvasKit 作为 render 的显式 backend 参数注入，Preview 不加载 WASM、不另建图形后端。

视图的来源基准 MUST 是生成该文档时的完整 EntityReadBaseline，包含实际依赖的 files、directories 和 fixedInputs；预览复用这份基准，不由调用方重新构造另一份。资源读取在该来源基准上扩展实际依赖并复核。当前根实体已携带统一基准结构，但语义依赖闭包和固定输入读取尚未接通，不能宣称预览请求已完成。

Preview 的工作区 Layer MUST 只注入同一个 ProjectEntities 服务。正式实现按视图的源身份及读取基准，从实体层解析并读取实际引用资源与工程调色板，将实际字节、身份、revision 与工程相对路径组装成 CanvasRenderInput，再调用唯一 CanvasRenderer.render。视图过期、读取基准变化、资源缺失和渲染失败保持实际错误，不输出替代图片。结果 PreviewImage MUST 携带 sourceEntityId、复用 CanvasRenderResult 的 image，以及覆盖视图来源、资源、调色板与固定输入的完整 EntityReadBaseline；不重复定义 PNG、尺寸、指纹和诊断合同。

预览缓存属于 Host 工作区 Scope，关闭时释放。复用 Effect 原生缓存，键的语义 MUST 覆盖源身份、文档、viewport、固定字体、显式时间和每项实际资源的身份、revision、字节及调色板；每次请求先确认实体基准有效，不能仅凭 renderer 的资源指纹或 revision 跳过读取复核。仅成功结果可复用，失败不得遮蔽后续真实修正；并发请求不共享可变 surface 或请求资源。缓存的具体容量、生命周期参数和结构化键在正式实现前依据真实消费需求确定，不建立猜测配置或自造缓存判定。

TODO：render 当前明确失败；实体资源准备、renderer 调用与缓存均未实现。该阶段只确立请求、交付、依赖和资源所有权，不以空图片、假调用或缓存命中冒充成功能力。

VS Code 与 Pi MUST 使用同一 CPU 渲染合同：各宿主组合根从随产品交付的固定 canvaskit-wasm 包及 WASM 初始化后端并注入 Host 的预览请求；后端在宿主生命周期内共享，surface、布局和可变图形对象由单次请求独占。初始化不放在每次 render 或 Webview 中，关闭先结束仍使用后端的请求，再释放工作区缓存和会话资源。固定字体也由组合根从产品资源提供，不读取环境字体。VS Code Webview 只展示 Host 交付的 PNG；Pi 的 Node 进程不需要 Electron、DOM 或 GPU，MCP 交付相同 PNG，由 Pi 官方客户端负责图片展示。files/memory 的差异只涉及派生视图供给，不改变 renderer 的输入、算法或 PNG 合同。当前两个宿主的后端与字体装配、预览工具和 PNG 展示仍待接入；Node 软件 surface 的像素验证只证明选型可行。

性能判断 MUST 区分已验证能力与待测业务：WASM 初始化和固定字体加载属于冷启动成本；每次请求的实体依赖读取、图片/字体解码、文本 shaping 与自动字号、布局、Spine 姿态/网格、阴影及遮罩合成、PNG 编码和协议字节交付属于候选热点。软件 surface 的 8888 像素存储随 width × height 增长，另有快照、解码图片和中间层，不能把 PNG 压缩大小当作峰值内存。Promise 或 Effect 异步签名不把同步 WASM 绘制与编码移出宿主事件循环；VS Code 与 Pi 均承担此成本，当前没有完整业务测量，不能宣称不存在瓶颈。

低成本优化沿用现有合同：初始化按宿主共享，资源读取在完整基准内合并，同一视图复用转换结果与成功预览缓存，绘制直接使用 Skia 合成并避免逐像素读回和反复 PNG 编解码。独立资源 IO 可通过原生并发执行，同次请求的布局、绘制和编码按依赖顺序执行；并发请求独占 surface，不能靠共享可变图形对象节省内存。阶段二不新增线程池、队列、猜测限额或性能基准工具；正式业务接通后使用小型真实文档分别核对冷/热请求、节点与像素规模、资源变化、并发请求和峰值内存，以实际成本决定进一步优化。

必要性与取舍：CanvasKit 官方 Node/ESM 入口与 MakeSurface 类型已支持内存 CPU surface，PNG 的独立预期像素及重复编码有实际 Node 证据；复用同一 renderer 避免 Electron 与非 Electron 宿主各自维护绘制引擎。代价是 CPU 与内存成本仍由宿主承担；现有转换和 Effect 缓存承担复用，不提前制造第二调度或缓存体系。渲染与 Host 预览测试共用 CpuTestBackend 的唯一初始化边界，测试每份后端属于该测试实例，不复制包入口校验或 WASM 路径逻辑。

阶段二实现端口 MUST 保留具名请求与显式后端参数。失败保真验证涵盖视图的文档、原文、源身份、工程相对路径及完整来源基准，并包含实际字体字节与显式时间；files 与 memory Host 经同一入口失败，关闭后拒绝调用。基准来自调用方已取得的视图，Preview 不建立第二份基准或生命周期；错误不改变源文件、伴生身份及目录内容。

必要性：HtmlViewEntity 已持有唯一 CanvasDocument，ProjectEntities 提供 GUID 与实际基准读取，CanvasRenderResult 已表达 CPU 输出，Effect 提供 Layer 共享、Scope 与原生缓存。它们不承担具名预览请求到实体资源准备的业务编排；Preview 只连接这些现有能力，不新增解析器、文件读取、字体度量、光栅化或依赖。空壳验证必须使用真实 Layer 和合法视图，确认仅注入实体即可装配、执行明确失败、文档及请求不变且工程零写入；该证据不证明 PNG 业务完成。正式实现验收覆盖过期视图、同输入输出、资源及字体变化、资源修正后重试和并发请求隔离。

#### Scenario: 未接入预览明确失败

- **WHEN** 请求预览业务骨架
- **THEN** 返回 TODO 错误，不制造空图片

<!-- @anchor id=preview-service-contract -->
<!-- @architecture id=toolkit.preview -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=preview-service-contract level=M -->

#### Scenario: Host 分派预览请求

- **WHEN** 通过 Host.render 提交视图、真实后端与信号
- **THEN** 使用所属工作区唯一预览服务，当前交付具体 TODO；请求与后端保持原值，十四条 Host 直接依赖采用 L 预算

<!-- @anchor id=host-preview-dispatch -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=host-preview-dispatch level=L -->

### Requirement: 资产任务经编辑业务确认入库

资产服务 MUST 拥有 Host Scope 内跨请求任务及候选关联，执行并发上限为 4；submit/status/result/cancel/discard/renderSlice/review/confirm 各自使用具名输入和具名结果。不同命令通过具名名义类及共同抽象根表达，以 instanceof 收窄；对象字段由具名 interface 声明，状态枚举使用同类型字面量的具名 type，不声明语义联合或匿名对象联合。renderSlice 与 review MUST 固定 taskId 与 artifactId，并接收结构化切片案例或明确评阅输入，结果不自动确认。任务输入不可变，状态为 pending、running、ready、failed、canceled、confirmed 或 discarded；result 只交付 ready 产物。请求取消只结束等待，显式取消结束本地任务并报告远端执行是否实际停止，MUST NOT 把 HTTP 中断当作远端停止。任务不从 Pi 会话历史自动恢复。

状态变化以实际完成的工作为依据：submit 接受输入后建立 pending 并返回 taskId；工作区 Fiber 取得原生 Semaphore 的执行许可后进入 running；远端完成、下载、确定性校验和候选存储全部成功才进入 ready。生成、下载、校验或存储失败进入 failed 并释放未完成内容，显式取消执行进入 canceled 并报告真实远端停止事实。ready 的 confirm 仅在 Editing.commit 与实际重读成功后进入 confirmed，discard 仅在所属候选释放后进入 discarded；两者是候选寿命的终态，不继续报告 ready 或允许再读取已释放内容。同一任务的候选消费操作经原生许可串行，成功确认后的再次确认因状态不符失败，不重复写入。确认失败仍交付实际错误并保留 ready 候选供检视；评阅、切片或一次读取失败只结束该请求，不把已校验候选改成生成失败。未知 taskId、产物不属于任务或状态不允许的操作直接失败。关闭 Host 释放任务、Fiber、许可和候选，不在新 Host 恢复。

整个管线是有状态业务；AssetTasks 拥有任务状态和跨请求编排，AssetGeneration/ComfyExecution 是无跨调用状态的远端副作用，不能称为纯函数。工作流参数绑定及 Canvas 文档装配是纯计算；AssetProcessing 的解码/CPU 绘制与 AssetCandidates 的 IO 使用各自请求和存储 Scope。调度复用 Effect Fiber、Scope 和 Semaphore，不新增调度器、状态机依赖或第二状态树。

游戏 UI 生成采用资产粒度的编排：为按钮、面板、图标等确定 prompt、风格参考、已验证工作流参数及明确尺寸/透明要求 → submit UI 贴图任务 → status 查询 → result 选定 artifactId → renderSlice 按原生切片向量和多个尺寸检视 → 需要时 review → 明确 confirm 或 discard。成套素材通过共享显式风格输入分别提交，复用同一四并发上限，不从营销名称推定风格一致性。入库后取得实际 GUID 和相对路径，Canvas 布局通过既有视图编辑、候选与提交链引用这些资源；图片生成成功不表示 Canvas 布局已经修改。VS Code 侧栏保持只读状态与调用展示，生成、切片、评阅及确认由 MCP/Agent 调用同一 Host，Pi 使用相同任务与结果；宿主 UI 不拥有远端队列、候选或独立生成管线。

LLM 反馈 MUST 来自具名结果和实际诊断：submit 返回 taskId，status 返回真实状态及诊断；pending/running 明确尚在执行并可查询或取消，ready 明确可读取、切片、评阅或确认，failed 保留失败阶段与原因，canceled 区分本地停止和远端停止事实，confirmed 返回实际资源身份，discarded 表达候选已释放。产物交付名称、MIME、格式和校验结果，切片交付同一 CPU renderer 的 PNG，评阅意见独立于确定性校验；失败引导核对对应输入或工作流，不自动重试或改写请求。协议的图像内容交付按后续 MCP 多模态合同接入，不把提示词中声明“请看图片”当作图像交付。

生成 MUST 使用注入参数和凭据、有来源工作流与明确能力白名单，通过 RunningHub 原生 ComfyUI 代理上传、提交、history 查询和下载；模型文件名与 graph 家族匹配，收藏及六个月创建时间窗口为使用条件。禁止标准模型 API、LLM API 生成通道及其工作流包装；无证据的节点、checkpoint、模型名和远端取消能力以 `#gap` 表达。执行模块只负责原子生成，不拥有跨请求调度或工程 IO。

产物 MUST 经实际格式解码与明确质量要求校验后成为候选；PNG 格式不能证明透明，视觉评阅意见不能代替确认。九宫格切片预览 MUST 复用 CPU renderer，MUST NOT 重复实现绘制。候选字节流暂存于工程外 Host 独占目录，内部只交付 artifactId 和内容，绝对路径限存储 IO。未完成产物失败时释放，ready 候选在入库成功、丢弃或 Host 关闭时释放。

confirm MUST 接收 taskId、选定 artifactId、目标工程 RelativePath 与显式确认信息，经 Editing.importAsset 读取目标及引用基准，准备资源、GUID 和必要引用操作的统一 EntityChange，再经 Editing.commit 提交。准备阶段不写工程；提交后实体重读实际文件，Host 主动调用 Views 刷新并交付资源身份。重复确认不得再次入库；提交失败保留实际错误与可检视候选。当前 confirm 尚未实现时明确失败，未实现能力不登记工具。

公开参数与凭据 MUST 分开注入；VS Code 使用 WorkspaceConfiguration，Pi 使用明确指定的配置输入。凭据是可选明文，不额外加密；未使用 RunningHub 时不要求凭据，远端执行时缺失则失败。凭据不得进入调用记录和日志。旧 C# TOML 的默认模型表及加密凭据格式不作为新版配置依据。

命令与结果 MUST 逐项对应：submit 携带 GenerationRequest 与明确产物要求；status 交付状态与诊断；result 只交付具名 AssetCandidate；cancel 用 Option 表达远端停止事实是否已知；discard 表达指定任务候选已释放；renderSlice 交付复用 renderer 成功类型的 PNG 集合；review 交付原 GenerationReview；confirm 交付实际重读的实体标识、GUID 和资源相对路径。每个方法只接受自己的命令种类，不接受字符串或完整命令联合代替具名输入，confirm 不返回未提交 EntityChange 冒充入库结果。

必要性与取舍：Effect 提供原生 Layer、Scope、Fiber 和 Semaphore，生成、处理、候选和 Editing 各有唯一原子职责；原生 ComfyUI history 提供 prompt 的实际完成事实，但不管理本地候选、校验、切片及入库。AssetTasks 因此保留唯一工作区任务编排，复用既有结果和原生并发机制，不复制 PNG、字节流或统一候选模型。confirmed/discarded 区分资源已释放的终态，避免仅五种生成状态不能表达候选已消费的问题；状态仍是一份任务记录，不另建候选业务状态机。UI 素材生成与 Canvas 布局各走已有业务，取舍是显式选择和确认换取可检视的质量及真实入库事实，而非一次生成自动修改整套布局。依据为已锁定 Effect 的 Scope/Fiber/Semaphore API、唯一平台索引的 ComfyUI history 和游戏 UI 资产生产案例。阶段二八个入口明确失败，不建立队列、远端执行或磁盘存储。真实 Host 回归验证各命令的具体 TODO、输入/字节保真、工程零写入和关闭后拒绝；后续实际验证状态顺序、四并发、请求取消隔离、候选消费串行、失败保留、确认终态与重复写入拒绝及关闭释放，不能把空状态或装配成功当作业务完成。

#### Scenario: 资产服务共享工作区依赖

- **WHEN** 通过工作区 Layer 装配资产服务
- **THEN** 复用同一 Editing、AssetProcessing、AssetCandidates 与显式执行配置端口；八项业务入口继续按各自合同明确 TODO

<!-- @anchor id=asset-service-contract -->
<!-- @architecture id=toolkit.assets -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=asset-service-contract level=M -->

#### Scenario: 任务生命周期端口明确失败

- **WHEN** 通过同一工作区服务提交、读取状态、读取结果、取消或丢弃任务
- **THEN** 各入口交付自己的具名 TODO，保留命令；任务状态与四并发调度继续按正文实现

<!-- @anchor id=asset-task-lifecycle-ports -->
<!-- @architecture id=toolkit.assets.scheduler -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=asset-task-lifecycle-ports level=M -->

#### Scenario: 候选切片与评阅端口明确失败

- **WHEN** 通过工作区服务请求选定产物的切片或评阅
- **THEN** 返回各自具名 TODO，保留任务、产物及操作输入

<!-- @anchor id=asset-task-review-ports -->
<!-- @architecture id=toolkit.assets.review -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=asset-task-review-ports level=M -->

#### Scenario: 任务确认端口明确失败

- **WHEN** 通过工作区服务确认选定产物
- **THEN** 返回确认 TODO，保留目标与确认输入，工程保持原值

<!-- @anchor id=asset-task-confirm-port -->
<!-- @architecture id=toolkit.assets.confirmation -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=asset-task-confirm-port level=M -->

#### Scenario: 任务生命周期命令保留具名输入

- **WHEN** 构造提交、状态、结果、取消或丢弃命令
- **THEN** 命令保留请求、要求或任务身份，各入口只接收对应命令

<!-- @anchor id=asset-task-lifecycle-input -->
<!-- @architecture id=toolkit.assets.contracts -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=asset-task-lifecycle-input level=M -->

#### Scenario: 候选操作命令保留选定产物

- **WHEN** 构造切片、评阅或确认命令
- **THEN** 保留任务与产物身份，以及明确切片案例、评阅指令或目标和确认信息

<!-- @anchor id=asset-task-artifact-input -->
<!-- @architecture id=toolkit.assets.contracts -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=asset-task-artifact-input level=M -->

#### Scenario: 任务结果分别表达实际业务合同

- **WHEN** 消费具名任务结果类型
- **THEN** 状态、候选、取消事实、切片、评阅和入库身份分别复用所属合同；当前执行入口继续 TODO

<!-- @anchor id=asset-task-output-contract -->
<!-- @architecture id=toolkit.assets.contracts -->
<!-- @budget id=asset-task-output-contract level=M -->

#### Scenario: Host 分派八类资产命令

- **WHEN** Host.assetTask 接收任一具名命令
- **THEN** 分派到同一工作区对应端口，当前返回其具体 TODO；分派与取消、关闭共享 Host 生命周期，十四条文件依赖采用 L 预算

<!-- @anchor id=host-asset-dispatch -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=host-asset-dispatch level=L -->

### Requirement: 产物校验消费内容与明确要求

AssetProcessing.inspect MUST 接收 GenerationArtifact、AssetRequirement 和 AbortSignal，实际解码内容后按要求交付 AssetInspection；图片、音频、视频和 3D 文件组的质量要求分别使用具名名义类，继承共同抽象根，以 instanceof 收窄，各自只携带该格式所需字段。校验失败以错误和实际诊断交付，不把 valid 布尔值或空诊断作为 ready 授权。调用方必须将同一产物的成功校验结果与候选关联，不能只提供 artifactId 而让处理模块自行寻找状态或工程资源。

renderSlice MUST 接收所选产物、原生四组切片向量和尺寸案例，以及注入的 CanvasKit 和信号。AssetSlice 直接保留 slicedBorderLB、slicedBorderRT、slicedRectLB、slicedRectRT，复用 renderer 的 Vector3；边框与矩形各有 LB/RT，不能压成一套四边数值或旧逗号字符串。装配 CanvasDocument 后调用唯一 CanvasRenderer.render，AssetSlicePreview.images 直接复用该入口的成功结果，输入顺序与案例顺序一致；本模块不生成另一种 PNG 合同、不启动浏览器、不修改原始产物。正式实现前以平台字段核对向量各分量、坐标单位及边界约束，不从字段名称猜测；当前只保留原生字段和原值。

必要性：生成产物已经交付字节流，renderer 已确定唯一 CPU PNG 合同，CanvasSchema 与 BoxFields 已登记原生九宫格字段；原生 Layer/Scope 承担资源边界。该模块只保留解码校验和切片装配端口，PNG/JPEG 解码复用已选 CanvasKit，音视频与模型文件解码依赖在正式实现时按真实格式盘点选型，不提前自研解析器。当前 inspect/renderSlice 明确 TODO 失败；骨架验证真实 Layer、合法内容与四组不相同向量的失败输入保真，测试初始化复用 CpuTestBackend，正式验收覆盖格式假冒、尺寸与透明错误、音视频信息、缺失 3D 文件及切片 PNG 像素结果。

#### Scenario: 处理骨架明确失败

- **WHEN** 请求产物校验或切片预览
- **THEN** 返回明确 TODO，原始内容不变，不交付虚构校验或 PNG

<!-- @anchor id=asset-processing-contract -->
<!-- @architecture id=toolkit.asset_processing -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=asset-processing-contract level=M -->

#### Scenario: 质量要求分别声明格式字段

- **WHEN** 构造图片、音频、视频或模型文件组要求
- **THEN** 各具名类型保留所属格式的实际要求，以共同抽象根传给 inspect；解码与校验继续 TODO

<!-- @anchor id=asset-inspection-requirements -->
<!-- @architecture id=toolkit.asset_processing.inspection -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=asset-inspection-requirements level=M -->

### Requirement: 候选资源归属工作区任务

AssetCandidatesService MUST 在同一 Host Scope 共享装配。store 接收 taskId、具名产物和其成功 AssetInspection，并消费 AbortSignal；read 必须同时固定 taskId 与 artifactId，返回字节流；release 释放指定任务的全部候选，不以单个 artifactId 代替任务寿命。AssetCandidate 保留任务标识、产物名称、MIME、格式及原校验结果，不交付内部绝对路径或复制字节。

存储只允许工程外 Host 独占暂存区；失败的部分写入、确认成功、丢弃及 Scope 关闭均释放所属资源。取消读取只结束本次等待，不能擅自丢弃 ready 候选。候选模块不依赖工程 Files 或 Entities，不建立任务调度。必要性：Node 文件系统与 Effect Scope 提供流式 IO 和释放，生成合同提供内容，处理合同提供校验结果；本模块只拥有任务所属候选的 IO 寿命和标识映射，不新增缓存、复制存储或路径兼容。当前所有入口明确 TODO，不创建暂存目录或泄漏资源；正式实现验证任务隔离、流式读取、失败及关闭释放。

#### Scenario: 候选骨架明确失败

- **WHEN** 装配候选服务并请求存储、读取或释放
- **THEN** 返回明确 TODO，不创建虚构候选或伪造释放成功

<!-- @anchor id=asset-candidates-contract -->
<!-- @architecture id=toolkit.asset_candidates -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=asset-candidates-contract level=M -->

### Requirement: 资产入库先准备统一候选

Editing.importAsset MUST 接收所选产物内容、目标工程相对路径和显式确认信息，返回既有 EditingCandidate。准备读取目标及引用基准，资源、GUID 与必要引用操作统一使用 EntityChange；本入口不写工程，实际提交继续通过 Editing.commit。执行错误自然暴露，未实现时明确 TODO，不生成伪 GUID、空候选或提交成功。

必要性：实体层已经拥有资源身份、基准与统一候选原语，Editing 已拥有准备和提交编排。新增入口仅表达资产导入意图，复用同一候选及提交链，不创建第二文件操作模型或视图 replace 旁路。正式实现验证准备零写入、目标冲突、依赖变化、GUID 引用和提交后实际重读。

骨架测试使用真实工作区 Layer、实体和文件服务，验证 TODO 失败、原始字节流可读、确认输入不变及工程目录和正文零写入；不以替身证明工程 IO 边界。

本入口与 Editing 共用候选、基准、计划及四项工作区服务合同，所属文件计入八条直接依赖；继续拆分端口会把同一候选准备职责移出 Editing，采用最小可容纳的 M 预算。

#### Scenario: 入库候选骨架明确失败

- **WHEN** 请求资产入库候选准备
- **THEN** 返回明确 TODO，工程不发生写入

<!-- @anchor id=asset-import-contract -->
<!-- @architecture id=toolkit.editing.asset_import -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=asset-import-contract level=M -->

### Requirement: patch 分析边界

patch MUST NOT 保留旧版的前后两次 Lint、独立字符串 diff 或 coderepo 特判链。更新文本只在共享反向转换后进入一次 ChangePlan 与同次语义分析；代码仓库脚本的引用影响由实体身份、CallGraph 和完整读取基准统一提供，不能按文件名、路径或“代码仓库”标签另建例外分支。匹配层只负责把明确的目标片段和替换文本交给 Host，不计算诊断、引用级联、差异摘要或写入结果；失败由对应结构化入口自然暴露，并保留原视图、候选和工程文件。

阶段二保留唯一 patch/Editing/ChangePlan 边界和明确 TODO，不为了表面可用增加前后 lint、文本 diff、force 参数、coderepo 白名单或兼容路径。正式接入时，代表性语义错误必须使共享分析失败，代表性代码仓库引用必须验证全部绑定影响，候选准备必须零写入；结果反馈只交付 ChangePlan/诊断的实际结构，不复制旧版提示字符串。

#### Scenario: patch 复用单一分析与候选边界

- **WHEN** 以视图文本或明确重构意图准备 patch
- **THEN** 请求沿匹配 → 反向转换 → ChangePlan/一次语义分析 → EntityChange 候选流转；当前未接入时明确失败，不运行前后 Lint、独立 diff 或 coderepo 特判，原输入和工程保持不变

<!-- @anchor id=patch-analysis-boundary -->
<!-- @architecture id=toolkit.editing -->
<!-- @architecture id=pure.change_plan -->
<!-- @architecture id=toolkit.diagnostics -->
<!-- @todo -->

### Requirement: patch 文本匹配合同

PatchMatcher.apply MUST 只消费调用方已经读取的原视图文本与非空、只读 PatchEdit 顺序集合，返回完整更新文本。每项编辑显式携带 oldText、newText 和 replaceAll；oldText 必须非空，newText 允许为空以表达删除。文本匹配是字面片段定位，不解析 TS/XML 语法，不按实体种类、路径或代码仓库身份选择算法。视图身份和完整来源基准由调用方保留，并与结果一同交给既有 Host.replace；匹配器不读取视图、不持有基准、不做工程 IO，不建立 Effect 服务、缓存或跨请求状态。

匹配 MUST 使用 JavaScript 原生字符串的精确、区分大小写的 UTF-16 文本语义。JSON 协议转义由 SDK/Zod 边界解码一次；字符串中的字面反斜杠、Unicode 字符、换行、缩进和行内空格保留原值。replaceAll 为 false 时必须恰有一个匹配；零匹配报告编辑序号及重新读取目标、按原文构造 oldText 的引导，多匹配报告命中数量与原文位置，并引导增加唯一上下文或显式使用 replaceAll。replaceAll 为 true 时替换全部精确命中，零匹配仍失败；候选区间互相重叠时报告冲突，不自行选择替换优先级。位置采用当前步骤输入中的 UTF-16 offset，供协议适配显示，不冒充实体或源脚本坐标。

编辑 MUST 按输入顺序应用到前一步产生的局部文本；本条编辑的替换文本不再次参与本条搜索，后续编辑消费前一步结果。任一步失败则整个调用失败，局部文本不交付给 Host，原输入保持原状。同值替换仍核对匹配，不能用跳过掩盖不存在或歧义目标。匹配完成后只产生更新文本，转换、语义分析和候选提交继续采用前述唯一链；文本成功不表示候选合法或工程已写入。新建实体属于实体创建意图，不以空 oldText 绕过该编辑合同。

必要性与取舍：旧参考实现包含精确匹配、顺序编辑、去缩进及 Unicode 转义交换；原生 String 提供字面搜索与文本组合，官方 SDK/Zod 负责协议解析，现有转换提供 TS/XML 语义解析。这些能力足以承担片段编辑，不需要 diff、模糊匹配依赖或自研语法解析。选择精确匹配使目标位置可解释，代价是 LLM 必须复制实际原文；失败反馈给出位置与修正动作，已有可读视图减少构造成本。去缩进会合并原本不同的片段，转义交换会混同字面转义与实际字符，当前需求未明确要求这类自动纠正，因此不迁入旧 fallback。该规则不把语法分析换成文本拼凑；替换后的合法性由官方解析器和共享分析验收。

阶段二只建立 PatchEdit 与 PatchMatcher 文件级合同，apply 明确 TODO 失败；patch 工具保持未注册，不执行匹配算法。正式实现验收必须实际验证唯一命中、零命中、多命中、全部替换、重叠冲突、删除、顺序依赖、同值替换校验、UTF-16 位置及换行/缩进/字面转义保真，并验证失败时无部分更新交付；当前空壳回归仅证明明确失败和输入保真。

#### Scenario: 匹配骨架保持明确失败与原文保真

- **WHEN** 将原视图文本和顺序片段编辑交给尚未实现的 PatchMatcher
- **THEN** 抛出具体 TODO，原文和编辑集合保持不变，不交付部分更新文本或伪造匹配成功

<!-- @anchor id=patch-matching-contract -->
<!-- @architecture id=toolkit.mcp.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=patch-matching-contract level=S -->

### Requirement: 资产编辑能力与技术依据

资产编辑 MUST 沿既有 GenerationRequest → AssetTasks → AssetGeneration/AssetProcessing → AssetCandidates → Editing.importAsset/commit 分工。图像生成与 UI 贴图消费 prompt 和明确风格输入；图像编辑消费原图片及已验证工作流声明的参考图、mask 和参数，保留原输入，产生新候选。局部重绘、扩图、背景处理与风格变换只能由具备相应输入及生产验证的原生工作流表达，不把任意 prompt 或 preserveAlpha 参数当作具备这些能力的证据。上传的每份 GenerationArtifact 必须按工作流绑定确定角色，不按数组位置猜测图像、参考图或 mask；具体绑定在正式 graph 验证时收敛，当前粗略输入不承诺已支持这些编辑。

确定性处理拥有格式、实际尺寸、alpha 与要求校验及九宫格预览；选择复用 CanvasKit 0.42.0 的 Skia 图片解码与 CPU 绘制，不新增第二图形引擎、逐像素抠图或启发式九宫格算法。透明 PNG 必须检查实际像素，不以扩展名或模型说明代替。slice 直接使用 Canvas 原生四组向量，尺寸案例经唯一 renderer 输出；预览只检视所选候选，修改切片不覆盖候选图片。评阅只输出观察意见，明确确认才进入同一实体候选与持久化链。图像编辑结果和新生成结果采用同一校验、候选与确认规则。

能力参照以 docs/ComfyUI_App_Platforms_Index.md 的允许通道及 Comfy-Org 官方 graph 为外部依据；其中 Scenario、Unity 和社区游戏资产案例只说明参考图、风格控制和后处理的产品流程，不接入其私有 API。docs/comfyui_workflows 是参考 UI graph，res/asset_generation/catalog.json 固定官方上游提交及 unavailable 状态；模板名称、历史模型表和旧 Python/C# 引擎不构成生产能力。RunningHub 使用 Node 原生 fetch/AbortSignal、Effect 4.0.0 和 Zod 4.6.4，原生上传、prompt、history、view 执行链保持唯一；checkpoint/LoRA 必须来自实际模型库并满足已有收藏及时间窗口合同，当前不凭旧列表选定模型。

可行性证据 MUST 区分边界：已有 Node 软件 surface 的 PNG 编解码及独立预期像素验证证明本地图片处理后端可行，透明像素回归证明 alpha 可保留与读取；真实工作区资产任务、处理、候选及导入空壳回归证明 TODO 失败、输入保真和工程零写入。它们不证明图像编辑、九宫格业务或远端模型可用。生产 graph、输入角色绑定、实际模型及各项质量结果仍须在正式实现中验证，目录保持 unavailable，资产 MCP 工具保持未注册；本阶段不提交计费任务制造可用性声明。

必要性与取舍：复用原生 ComfyUI 工作流可统一生成与编辑，但模型和 graph 必须实际匹配，远端 seed 不保证逐位一致，验收依据质量与修改区域的可观察结果。复用同一 CanvasKit 和 renderer 避免图像处理与游戏预览分裂，代价是音视频与 3D 的解码还需按实际产物选库；当前只声明这些格式的校验端口，不假定图形后端能处理它们。正式验收包含编辑区域变化与保留区域、透明边缘、切片角点/边缘/中心及不同尺寸、坏格式拒绝、明确确认与实际资源身份；业务实现继续留在第三阶段之后。

#### Scenario: CPU 图片后端保留真实 alpha 像素

- **WHEN** 在 Node 软件 surface 中绘制并编码、解码含透明像素的 PNG
- **THEN** 独立预期像素与 alpha 保留一致；图像编辑、业务校验、九宫格和入库继续按正文目标实现

<!-- @anchor id=asset-editing-capabilities -->
<!-- @architecture id=canvas.renderer_tests -->
<!-- @budget id=asset-editing-capabilities level=S -->

### Requirement: Lint 分层复用同次结构化输入

检查 MUST 按已有职责单向执行：实体层读取与格式解析 → 符号表和调用图 → 完整零件语义装配 → Diagnostics 准备范围及基准 → 每方法 ScriptLint → 一次 CallGraphLint → 具名结果交付。格式层负责解码、schema 和语法错误，实体层负责身份、层级、来源及结构诊断，分析层负责方法作用域、调用目标与未解析记录；Lint 消费这些结果，不再分别扫描磁盘、解析视图或重建调用关系。Diagnostics 复用实体层同次 semanticSnapshot 的分析，不能用旧 LintEngine 多轮零件/源码扫描再以末尾去重掩盖重复诊断。

ScriptLint.inspect MUST 只执行当前 methodKey 绑定的局部规则，输入保持 LocalAst、完整 Context 和原分析。CallGraphLint.inspect MUST 消费一次完整 CallGraph，输出相同只读 LintDiagnostic 合同，承担需要跨方法调用关系的规则；全图检查不放在每方法循环内，也不从目标范围裁掉依赖节点。请求范围只决定交付的相关诊断，完整依赖仍由实际读取基准覆盖。全图规则的位置来自实际边及方法身份，缺少可定位来源以已有 Option 表达，不填写行号 0。

共享源码只复用解析与 AST 字段收集，方法绑定及全局/局部作用域继续按各零件独立检查。调用图已有来源与 unresolved 诊断直接保留在原分析中，两个 Lint 入口只交付自己负责的规则诊断；由归属消除重复，不另造诊断字符串去重、第二缓存或第二调用图。正式规则按规范和平台结构化签名决定，旧规则表仅用来盘点结构、局部语义和跨方法规则，不整体迁入旧特判、全局内置名称集合或错误吞没策略。

必要性与选型：现有 Chevrotain/LocalAst、实体模型、SymbolTableBuilder、CallGraphBuilder 和 SemanticAssembly 已承担解析、身份、作用域、关系和 Context 装配。ESLint 检查开发 TypeScript，不提供 BEScript 游戏语义；原生集合已足够表达检查输入和诊断，阶段二不添加规则框架。旧 LintEngine 最后另建图并自研递归逻辑，现有方法级入口不足以承接全图规则，所以增加唯一 CallGraphLint 文件级端口。正式递归/可达性实现须先盘点受维护图算法依赖，不能自研通用图算法；本阶段只有纯入口及明确 TODO，不提前选不存在的消费规则或实现算法。收益是局部和全图计算次数、依赖及诊断归属明确，代价是正式接通仍需完整语义输入与逐规则证据。

效率合同以实际复用为准：实体读取得到的 AST、变量表和调用图在同次检查共享；局部规则按方法绑定执行，全图规则一次执行，不通过视图生成、PNG 渲染或提交前后各跑一套检查。未实现的准备、方法及全图入口各自明确失败，完整诊断链保持 TODO；纯分析可运行不等于 Lint 已交付。正式验收以独立预期验证合法与错误输入、不同绑定作用域、直接/间接调用风险、来源定位、范围与完整依赖以及错误传播；空壳回归只验证两个实际入口失败与同一分析输入保真。

#### Scenario: 方法与全图检查具有独立纯入口

- **WHEN** 以实际构建的共享源码多绑定分析调用方法级与全图级检查空壳
- **THEN** ScriptLint 与 CallGraphLint 分别抛出具体 TODO，原 AST、Context 与完整 CallGraph 保持不变，不返回空诊断或进行第二次解析、IO 与分析构建

<!-- @anchor id=lint-layered-pipeline -->
<!-- @architecture id=toolkit.lint -->
<!-- @architecture id=toolkit.lint_tests -->
<!-- @budget id=lint-layered-pipeline level=S -->

### Requirement: 费时操作的并发与资源生命周期

费时操作 MUST 按真实数据依赖和资源所有权确定并发边界。独立请求和独立 IO 使用 Effect 原生并发；同一输入的解析、符号表、调用图、语义装配、转换及结果交付按依赖顺序执行，复用同次分析和既有缓存。Promise、Effect、Fiber 与 Layer 的异步组合不等于 CPU 多线程执行，同步 TypeScript 解析、格式转换、哈希、WASM 绘制和编码仍占用宿主事件循环。

| 操作 | 并发边界与现状 |
| --- | --- |
| 冷启动及源变化刷新 | 监听 ready、首次装配和启动校验保持依赖顺序；唯一 watch 按批次完成实体重读再进入派生刷新，不并发发布相互覆盖的工作区状态。目录枚举及元数据读取当前顺序执行；正式实现仅在保持完整基准和稳定汇总结果时并发独立 IO。 |
| 实体读取、候选准备与提交 | 当前共享 Semaphore 的单许可串行边界覆盖实体读取、语义基准取得、候选准备、提交及刷新。基准复核、文件提交、实际重读和快照发布 MUST 保持一致；IO 并发不得绕过这一边界或让不同批次交错发布。 |
| TS/Canvas 转换与 Lint | 独立输入可使用原生并发，输入内部按依赖顺序计算。Canvas 资源依赖使用 concurrency: 4 的 Effect.forEach，但 entities.read 仍受上述许可约束，因此该参数不能证明文件读取或 CPU 计算已并行。方法级检查复用完整输入，全图检查只执行一次；当前 Lint 编排保持 TODO。 |
| 预览与本地资产处理 | 独立资源 IO 可并发，布局、绘制、编码保持依赖顺序；请求独占可变图形资源，宿主共享后端和固定字体。具体输入、缓存和释放继续遵循预览合同；完整预览业务和资产处理当前保持 TODO。 |
| 远端资产生成 | 工作区任务沿用资产合同的 4 并发上限，原子任务内部上传、提交、查询及下载按协议依赖执行。跨请求任务归 Host Scope，请求取消只结束等待；显式取消按真实远端停止事实反馈。调度及执行当前保持 TODO。 |

Host MUST 将受控调用 Fiber 绑定同一工作区 Scope；取消某次调用中断该 Fiber，释放该次请求或快照上下文，其他独立请求继续使用共享服务。Host 关闭结束所属调用和监听，再释放缓存及资源。可取消外部操作消费原生 AbortSignal；同步 CPU 工作及不接受取消的 Promise 不因信号产生而立即停止。资源成功、失败和中断路径均由所属 Scope 或既有 finally 释放。

必要性与取舍：现有 Effect 4 的 Fiber、Scope、Semaphore、forEach 和 Cache 已提供请求并发、取消、状态串行与复用；Chokidar 已提供唯一变化队列，CanvasKit 提供 CPU 渲染。阶段二已有端口足以表达这些边界，本项不增加执行器或业务空壳。直接复用原生机制保留当前可靠性边界；代价是串行实体读取和同步 CPU 热点尚未优化。正式业务接通后以实际 IO 等待、事件循环占用及峰值内存决定必要的并发调整；当前不新增线程池、队列、猜测限额或性能基准工具。

验收区分已实现与待实现：现有真实工作区回归验证请求取消后的可用性、独立上下文释放、关闭后拒绝、转换依赖变化和 Scope 中断；这些证据不代表全部计算已并行。后续业务实现须验证独立任务实际重叠、共享提交顺序与过期基准拒绝、取消隔离、图形资源独占，以及资产执行上限和成功/失败/中断释放，不能用异步签名、配置值或调用次数代替行为证据。

当前代码锚点绑定 Host.execute 的请求 Fiber 与 Scope 所有权。该入口复用 Host 唯一运行时和工作区服务，所属文件计入十四条直接依赖；拆出第二执行入口会分裂取消与关闭控制权，采用最小可容纳的 L 预算。表中其余业务并发目标沿各自合同实现。

#### Scenario: 并发调用保持所有权与依赖顺序

- **WHEN** 同一 Host 接收独立请求、源变化或资产任务，并发生请求取消或 Host 关闭
- **THEN** 独立工作按原生机制并发，实体基准与提交发布保持串行，同次计算遵循依赖顺序；取消只结束所属调用，关闭释放所属资源；阶段二未实现业务继续明确 TODO，不宣称同步 CPU 已脱离事件循环

<!-- @anchor id=workspace-computation-concurrency -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-computation-concurrency level=L -->

### Requirement: 两种部署形式与宿主生命周期

VS Code 与 Pi MUST 交付同一 Host、MCP 和 CPU 渲染实现，使用已经固定的 ESM 与依赖版本；部署差异只在宿主装载、原生配置、展示和生命周期边界。运行时工程是宿主实际打开的 CreatAI 工程，产品资源来自部署包；开发仓库、构建目录和测试夹具不成为玩家运行时资源来源。

| 边界 | VS Code | Pi |
| --- | --- | --- |
| 装载与运行时 | VS Code >=1.100 原生加载 ESM activate/deactivate，使用扩展宿主自带 Node；VSIX 包含插件和 Webview 产物 | 固定 Pi 1.0.0 在同一 Node 进程加载默认 ExtensionFactory；分发为官方 Pi package，以 pi.extensions 声明 ESM 入口，Pi SDK 为宿主 peer，不打包第二份 Pi |
| 创建工作区 | activate 读取原生 WorkspaceConfiguration，建立 files Host、前端组件和回环 HTTP MCP；无工作区保留侧栏 | 工厂只登记 flags 和事件；session_start 使用 ctx.cwd 与显式配置建立 memory Host、回环 HTTP，再 registerMcpServer；加载工厂本身不打开监听或 socket |
| 资源寿命 | 扩展会话持有工作区；面板关闭只释放所属展示资源，请求结束只释放请求上下文；deactivate 结束客户端与面板调用，再关闭 MCP、组件、Host 和前端进程容器 | Host 和任务候选归当前会话；session_shutdown、new/resume/fork/reload 先结束旧 MCP 连接与等待，再关闭 HTTP 和 Host；新会话使用新 Scope，不复活旧任务或缓存 |
| 配置与结果 | 原生 WorkspaceConfiguration 提供配置，Webview 展示 Host 的文本/PNG，原生输出通道接运行日志 | flags 与显式输入提供配置，官方 MCP 客户端交付文本/PNG/结构化结果，日志使用 Pi 入口拥有的同一注入输出；具体日志合同后续条目收敛 |
| 渲染与供给 | files 模式落盘派生内容，CPU 预览仍在 Host；Webview 不建立第二渲染链 | memory 模式保留真实工程读取及监听，短路派生落盘；CPU 预览使用相同输入和算法，Pi 不需要 Electron 或浏览器 |

正式部署包 MUST 在顶层 _build 的用途目录装配；VS Code 使用官方 VSIX 工具归档，Pi 使用官方包清单及安装机制。复用根 esbuild/Vite 生成 ESM 和 Webview，内部 workspace 包随产品构建并装入归档，发布清单不得保留 workspace:* 或指向包外仓库。VS Code 的 vscode 和 Pi 的宿主 SDK 由各自宿主提供；其余实际运行依赖、CanvasKit WASM、固定字体、语言资源、生成视图支持资源及同树版本的 MechToolkit Skill 必须在包内可定位。包内资源路径由宿主入口交付，绝对位置只用于文件系统读取；源文件中供开发构建使用的 ../../ 路径不能作为正式包的运行合同。共享后端按宿主生命周期初始化，工作区与请求按原所有权释放；包版本或工作区替换结束旧资源后重新建立，不制造第二服务实例来源。

启动失败 MUST 释放已创建的资源，关闭某项资源失败时仍完成其余已拥有资源的释放；客户端、HTTP 和进行中的请求先于所依赖 Host/后端结束。现有 VsCodeHost 已按分层创建及逆序失败释放、MCP→组件→Host→前端容器关闭；标准 LSP、Canvas 面板与后端装配仍 TODO，正式激活全链释放随接入验收。PiWorkspace.open/close、事件登记和 MCP 注册均明确 TODO，现有默认工厂回归只证明加载入口失败，不证明会话已建立。

当前开发清单、扩展入口的 Skill 路径与侧栏资源路径仍引用仓库构建位置；ESM 构建可运行但尚未交付独立 VSIX。Pi 清单已登记默认入口和精确 peer，但正式包归档、内部依赖打包和完整资源装配未实现。阶段二固定这些输入输出、资源来源和生命周期，保留已有文件级骨架，不提前实现发布、会话编排或资源装配，不以仓内运行冒充可部署产物。

必要性与取舍：VS Code 原生扩展与 Disposable、Pi 官方 package/会话事件、Effect Scope、现有 esbuild/Vite 和唯一 HTTP MCP 已承担装载、构建与释放机制；产品只连接同一 Host 和包内固定资源。进程内复用避免两种宿主各自持有业务实现，代价是同步 CPU 仍使用宿主事件循环；归档必须处理真实包内路径和 WASM 等非 JavaScript 资源，不能依靠 bundler 生成一个 mjs 就宣称部署完成。依据为 VS Code 官方 Extension Manifest/Publishing Extensions 文档与固定 Pi 的 packages.md/extensions.md。现有正式 ESM 扩展、真实 HTTP 与前端取消回归验证当前装载/释放边界；后续须在仓库外安装实际归档，验证原生装载、包内资源、工程切换、会话替换、失败释放及关闭后拒绝。

#### Scenario: VS Code 装配与关闭释放所属资源

- **WHEN** VS Code Host 创建失败或关闭
- **THEN** 创建失败释放此前已取得资源；关闭按 MCP、组件、Host、前端容器的依赖顺序执行，其余关闭继续由 finally 保障

<!-- @anchor id=deployment-lifecycle-contract -->
<!-- @architecture id=frontend.plugin -->
<!-- @budget id=deployment-lifecycle-contract level=M -->

#### Scenario: Pi 会话与独立部署资源保持待办

- **WHEN** 接入 Pi 会话替换或生成可独立安装的部署包
- **THEN** 按正文宿主事件结束旧会话资源，部署包携带真实依赖与固定资源；会话编排、归档和资源装配继续 TODO

<!-- @anchor id=deployment-session-packaging -->
<!-- @architecture id=frontend.packaging -->
<!-- @architecture id=toolkit.pi_plugin -->
<!-- @architecture id=toolkit.pi_plugin.logic -->
<!-- @todo -->
