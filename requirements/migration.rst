渐进迁移
========

.. interpretation:: 参考区首次引入基线
   :id: 3914e1f5-53f4-403e-b7c4-422b52a6a2ce
   :status: active
   :implements: 6f164446-12cf-4b65-9c34-4f5488f700af

   从参考区首次进入仓库的时点读取原文。首个提交已包含参考区时，该提交就是基线；后续才引入时，以首次引入的提交建立同样的冻结约束。基线由当前仓库历史确定。

   repo-layout 与 toolchain 原文中固定 SHA 登记及移动基线条款由本解读替代：模型唯一声明 ``freeze 'initial'``，门禁沿 HEAD 第一父历史取得 ``legacy/`` 首次出现的提交，再以 Git 原生 diff 比较受检树；首次仅在 index 引入而历史尚无参考区时，受检树本身是初始基线。后续增删改均拒绝，删除后重新引入不重新建立基线。该选择复用现有 Git 查询、模型登记和冻结检查，不增加基线文件或旧 SHA 兼容路径。

   CI 指标基线按事件确定：PR 使用目标分支基准提交，普通 push 使用推送前提交，新分支创建的首次 push 使用本次提交。首次 push 的 before 为全零对象，不能作为 Git 提交读取；使用当前提交仍要求当前测量达到该树已登记的模块指标，缺少模块基线仍失败。git-workflow 和 toolchain 对 push 基线的条款按此执行。

.. interpretation:: hjson-format 迁移解读
   :id: 1debd640-c6ca-44e1-828d-02fc324d74cc
   :status: active
   :implements: 1359c645-6001-4f72-80a0-da8b41d43527

   从语法标准、领域数据与字节保留的不同职责读取全部原文。官方 Hjson 3.2.2 负责统一解析与字符串写出，领域 Zod 负责结构校验；平台 SimpleJson 的有限布局职责不构成第二种解析器。原文的来源区间与未改动字节保留约束继续执行，规范化输出不能冒充无损往返。历史 OpenSpec 锚点治理由统一图替代，格式行为不随迁移变化。HjsonDocument 复用官方注释提取/合并及 stringify，Canvas 的 Hjson 与资源投影继续属于纯格式层。

.. interpretation:: bescript-format 迁移解读
   :id: 03dd7af4-46d2-4974-b1e4-7d503e6ffa23
   :status: active
   :implements: 62d374e7-2073-4fd3-900f-06d0cc5d52d9

   从三层转换的数据权威与控制权读取完整原文和所有 Scenario。CodeFile/LocalAst 由 Chevrotain lexer/CST、来源区间与保留式序列化承载；第一层不访问工作区。第二层显式接收 DeviceContext、basis、平台和目标闭包，使用官方 TS AST 表达绑定、私有、事件、异步与禁用结构。第三层使用官方 parser/factory/printer，片段更新遵守独立 printFile 一致、UTF-16 位置与原文恢复合同；canonical print 与来源恢复分别验证。ESM 实现不改变 namespace/class 视图 DSL。

   工作区 DeviceTsViews 统一持有 Scope 内缓存及完整修订和依赖；TsCanvasConversion 仅准备本次同步转换，不另建缓存或跨语言桥。2775 真实 code 的两轮字节往返仍是原文验收目标，本批代表测试不替代该完整验收。独立 d.ts 索引生成和成组发布、Views 实际调度、实体无损 encode 的 TODO 保持待实现。迁移选择保留完整原文并绑定转换入口，未绑定底层语法组件不获得完成声明。

.. interpretation:: canvas-conversion 迁移解读
   :id: 0215bfb7-af97-4651-b01b-c0d58b667b09
   :status: active
   :implements: ec8fc78b-1da0-457e-bb4d-046c9316ca3b

   从纯格式与工作区资源边界读取完整原文。binary32 按位最短十进制、负零、颜色整数优先以及封闭节点/字段/枚举约束保持；独立 binary、内联 Base64、内联 Hjson 和 XML 视图互转，XML 仅含 BeCanvas 子树，外壳与 PNG 分离。Effect Base64 规范重编码校验、resourceGuids 纯投影不引入 IO。CanvasConversion 提供六向服务、容量 128 与单项 1MiB 的缓存，原生依赖指纹及 invalidateWhen、失败不复用和 Scope 关闭继续约束结果生命周期。fromHtml 保留原始基准和来源，不直接持久化；独立 HtmlViewConversion 双向文档服务仍为明确失败的 TODO。原文逻辑完整保留，旧锚点流程结束。

.. interpretation:: analysis 迁移解读
   :id: 50567d0f-ed9c-4c1b-9712-b55c26e084b5
   :status: active
   :implements: dab0f53c-8a07-4c34-b262-79bce423190f

   从零件绑定身份、来源可用性和作用域读取原文全部合同。分析是实体引用与第一层 LocalAst 的纯函数，局部绑定复用 schema；共享源码不合并方法身份或变量归属。缺失来源和 AST 保留 Option 与诊断，不回退到同名平台方法；自定义优先，平台签名及回调参数位置显式输入。导入机械遵守核心闭包，私有、事件、禁用与 skip 规则保持；dynamic/potential-static 边、全部候选和原始位置分别保留。迁移不新增图算法或 Lint，现有 SymbolTable/CallGraph 与行为测试作为本批直接证据。

.. decision:: PR5 语言与视图合同迁移
   :id: b97dac77-ab12-4e39-891d-af2df8225e3a
   :status: active
   :implements: 1debd640-c6ca-44e1-828d-02fc324d74cc, 03dd7af4-46d2-4974-b1e4-7d503e6ffa23, 0215bfb7-af97-4651-b01b-c0d58b667b09, 50567d0f-ed9c-4c1b-9712-b55c26e084b5

   必要性是把格式来源、转换分层和消费者身份放进共同追踪图。已有官方 Hjson、Chevrotain、TypeScript AST、Effect 和 LikeC4 继续承担原职责；候选是双轨维护格式规范，或逐字迁移并建立唯一解读，选择后者。代价是历史源码注释和原文内路径只作来源，不批量改写；本批绑定入口与代表行为测试，底层完整专项覆盖仍须扩展，不用路径迁移宣称业务 TODO 完成。

.. design:: PR5 语言与视图追踪落地
   :id: d1d9a2e4-b398-4502-9b06-f8324961776a
   :status: active
   :implements: b97dac77-ab12-4e39-891d-af2df8225e3a

   四份原文按原字节移入 requirements/sources，Git blob 核对迁移前后相同；各 source 只有一个有效解读。LikeC4 的 specPath 和文件归属同步，源码绑定 Hjson 文档、BES 与工作区 TS/Canvas 转换入口及完整符号表和调用图；测试绑定 code 原文保留、四格式资源、真实依赖变化与 Scope 释放和调用图全文件。全文通过同一原生 implements、评分和上下文指纹检视。严格 Sphinx 构建、相关行为测试与 index/HEAD 门禁验证迁移；底层语法、完整往返专项与原文 TODO 的状态分别保留。

.. interpretation:: host-configuration 迁移解读
   :id: adbb836e-9731-4516-9e05-de7ba71ef61c
   :status: active
   :implements: 3c69ea32-5c37-4609-942b-af47fd781a6a

   从原文 Purpose 所界定的参考范围读取三项 Requirement 及全部 Scenario。TOML 路径、AES-256-GCM 密钥编码、默认模型及配置夹具继续作为 C# 旧实现的参考合同；原文明示它们不属于现役 VS Code 配置读写，不因迁移成为新版功能。现役配置由 toolkit-runtime 的只读 ConfigSource 与 vscode-frontend 的 WorkspaceConfiguration 合同约束，Pi 来源仍为 TODO。保全原文与重新启用旧实现是不同动作，本批只执行前者，不运行旧源码或旧测试。

.. interpretation:: toolkit-runtime 迁移解读
   :id: 3a56da01-d0fa-42ee-aa5b-f252e2f4664c
   :status: active
   :implements: 50fad8e3-2e26-4891-b879-a38eca38e7af

   从 Host 生命周期、控制权与实际交付边界读取全部原文。原文全部逻辑合同、Scenario 和显式 TODO 保留；PR3 已结束的锚点、复杂度预算与旧审计机制仅作来源，按统一图检视。LikeC4 继续唯一约束层级和依赖；Host 持有单一 Effect Runtime/Scope，files 与 memory 共用监听 ready、首次实体装配和源重读前缀。文件 IO 归实体层，转换缓存归各转换服务，Views 主动调度派生交付，Preview 不取得文件写权限。

   现有实现的直接证据限于绑定源码及测试：Host 装配、查询、取消和释放，源事件持续消费与实体重读，Pino 适配、只读配置。WorkspaceUpdates.refresh/openPreview 仍明确失败；格式实体装配、无损 encode、源/派生分类、派生纠偏和展示刷新不因链路存在成为完成。MCP 仅注册已实现的 workspace_status；Pi 默认工厂、会话/交付，调用记录与统计流，语言/诊断/编辑/预览的未实现端口，以及资产工作流目录、原生传输和任务业务仍按原文 TODO 约束。四并发、PatchMatcher 精确匹配及完整 ChangePlan 是目标，不能用骨架测试冒充实现验收。

   新配置是只读 ConfigSource；VS Code 使用 WorkspaceConfiguration，Pi 的 flags/文件接入待实现。可选明文凭据仅由远端执行边界读取，不引入旧密钥与加密层。此范围来自原文，不是迁移添加的兼容方案。本批选择保留完整合同并绑定 Host/配置/文件/实体主入口；格式转换、渲染和插件的专项绑定随 PR5/PR6 扩展，不把当前绑定声明为整个业务的完成证据。

.. interpretation:: workspace-files 迁移解读
   :id: bc57958b-7569-4e20-8c0f-4560bc5eb0dc
   :status: active
   :implements: 43445daa-9a1c-45ba-8178-d56c3a86b9ba

   从原始字节与真实文件系统边界读取四项 Requirement 及全部 Scenario，保留原文合同。WorkspacePaths 统一拒绝非法工程相对路径，PlatformFiles 以 realPath/stat 校验原生目标和版本；不在内部传递绝对路径。Effect Cache 容量 128、单项不超过 1MiB，失败与大文件不复用，读取前后版本变化须失败；目录事实不缓存。write-file-atomic 负责原子写入，同内容零写入，四类原语不代替实体层编排，失败保留已经完成的 IO。Chokidar 是唯一事件源，由 Host 主动取批次，Scope 释放监听；资源隔离与释放继续按原文，不新增事件旁路或缓存调度。

.. interpretation:: file-entities 迁移解读
   :id: f60b2116-3022-4848-9783-9f3b6709d2af
   :status: active
   :implements: 32189ba9-c813-449a-a288-1ba56fcf0ee7

   从文件权威、派生状态与一次候选边界读取全部原文，保留每项逻辑合同和 TODO。32 位小写十六进制 GUID、Hjson 伴生与原字节保留约束不变。MST 仅保存文件派生状态，工程文件是唯一权威；目录增量装配、Schematics 候选隔离、消费者原始基准以及 CRUD 矩阵继续有效。Host 单一消费 watch，实体主动重读；FormatEntity 共同根、完整 EntityReadBaseline、一次候选和提交后按实际 IO 重读仍按原文执行。

   当前绑定 ProjectEntities、EntitySources、EntityState 与公开文件/格式合同，核对实际 GUID 读取、目录重读、快照发布和失败状态；device/canvas/script 格式装配、classifyChanges、publishView 及无损 encode 的显式 TODO 均保留。详细逻辑操作与语义装配仍须由对应源码和行为测试证明，本批关系不宣称这些业务全部通过。本次选择统一保存来源与有效解读，结束旧锚点治理，其他行为边界未因平台迁移改变。

.. decision:: PR4 工程与 Host 合同迁移
   :id: 0241619e-5503-4099-b30b-3456be382f40
   :status: active
   :implements: adbb836e-9731-4516-9e05-de7ba71ef61c, 3a56da01-d0fa-42ee-aa5b-f252e2f4664c, bc57958b-7569-4e20-8c0f-4560bc5eb0dc, f60b2116-3022-4848-9783-9f3b6709d2af

   必要性是将工程生命周期、文件和实体的共同控制权放进同一需求图，并明确旧配置的参考身份。已有 Effect、Chokidar、write-file-atomic、MST、Pino、Zod 和 LikeC4 各自保有现有职责；本批不替换它们，也不实现原文 TODO。候选是继续维持两套规范入口，或逐字迁移四份来源并建立唯一解读；选择后者以减少分裂权威，代价是旧源码注释中的锚点仍只作历史文本。本次不批量改写源码和保全原文内的历史路径。Host 主入口先绑定，专项转换和前端在后续批次扩展，业务未完成项在检视意见中保留。

.. design:: PR4 工程与 Host 追踪落地
   :id: ead1b3ba-acd4-4d46-9c60-b72e540bf8d6
   :status: active
   :implements: 0241619e-5503-4099-b30b-3456be382f40

   四份完整原文移入 requirements/sources 下各自唯一目录，Git blob 必须与迁移前一致；每份 source 指向本文件唯一有效解读。LikeC4 的 specPath 和文件归属同步到新目录，架构仍只有原模型。源码绑定覆盖 Host/WorkspaceServices/WorkspaceUpdates、只读配置、文件缓存/平台/监听和文件实体入口的完整正文；验证绑定真实 Host 生命周期、文件边界、配置、GUID 及实体重读测试全文。统一 implements 关系和原生评分指纹使上游或实现变化要求复审。参考配置不绑定现役完成证据；明确失败的 TODO 不计作业务完成。严格构建、受检 index 与 HEAD 门禁验证本批迁移，托管 CI 未运行时仍保留该缺口。

.. interpretation:: repo-layout 迁移解读
   :id: cfac878d-3d45-4ec9-9f36-c801048a8d34
   :status: active
   :implements: 7f93e2da-ea20-45f0-91c5-d30392835bd7

   从文件归属、配置继承和可复现依赖的角度读取完整原文。所有 Requirement 及其 Scenario 继续约束实现，以下变化覆盖原文对应部分：Node workspace 的唯一锁仍为根 pnpm-lock.yaml；需求文档使用 requirements/uv.lock 锁定独立的 Python 工具环境，由同一根 prepare 安装，不是成员包安装旁路。旧 SQLite 发布回归随旧实现删除，其余原子写入测试保留。规范平台专属区改为 requirements；文件白名单仍只来自 C4。其余分区、资源、命名、测试归属、冻结、子模块、依赖方向、只读参考、工作区和术语合同保持原文约束，不以此次迁移取消。

.. interpretation:: dev-tooling 迁移解读
   :id: a801ae5d-c929-4c02-b8a8-ef0b17efdca7
   :status: active
   :implements: f2645cc2-8aef-4d50-9ecb-837cb0e8e622

   从正式入口与行为证据的角度读取原文。官方依据优先、复用优先、禁止试错实现、完成条件、根目录执行、UTF-8、格式检查、根指引唯一权威和密钥零输出继续按原文执行。完成条件中“独立 tools workflow、固定两个 worker、审计生命周期夹具”由同一 Vitest 配置、单一 tools 作业和 maxWorkers 50% 取代：git-workflow 与 toolchain 后续正文已规定该统一方式。提交门禁形态只替换 SQLite 登记为原生 needs 检视，其余消息、暂存输入、只读检查和用户合并边界保留。

.. interpretation:: git-workflow 迁移解读
   :id: ee53e7ee-b742-4cf6-888e-5241df5b2840
   :status: active
   :implements: e535d144-f2a6-419e-9d45-af4365550334

   从 Git 原生生命周期和 CI 事件语义读取原文。四项 Requirement 的协作入口、事件与就绪条件、Runner 并发及短路、写操作授权继续有效。涉及 SQLite 数据库的传输、路径过滤与正式状态判定改为 requirements 正文和原生 verdict 文件；audit 检查 HEAD 的图及意见新鲜度，不重建旧数据库。tools 仍执行同一根 CI 序列与测试指标，标签不取代代码运行、发布依赖成功、合并由用户执行；当前远端运行因账户额度未启动，不作为通过证据。

.. interpretation:: toolchain 迁移解读
   :id: c4af0b45-2af8-4585-ac18-6bcfc697bca7
   :status: active
   :implements: 8a882a69-526f-4071-b6b0-f813d179de98

   从“标准工具执行通用检查，项目只承载必要语义”的角度读取原文。单一入口、宿主进程、架构唯一来源及范围、架构依赖与派生契约、聚合模型、用途分类、文件与依赖边双向检查、Java 风格、ESLint 装配与结构规则、格式检查、第三阶段测试、指标棘轮、测试策略与隔离、文本收敛、Option、字符串豁免、运行时校验、防腐层、零增值转发、KV、标准配置范围、JSON 可读性、官方 Schema 镜像和环境访问/文本检查保留完整原文合同。

   规范结构、声明/Scenario 锚定、旧复杂度预算、二进制正式账本、REQ/INT/ADR 冻结与 archive 检查由统一 needs 图取代。两步审计、随机一次性令牌、整批事务、覆盖硬检查、快照隔离、变化诊断及 Git 有限继承必须保留，按 issue-179.rst 的审计协议执行。架构审计片段继续来自官方 LikeC4 CST，源码与测试按完整文件绑定；完整测试正文也参与原生检视指纹，不沿用旧测试排除和类框架裁剪。

   pre-commit/pre-merge-commit 先准备环境再检查同一 index；HEAD 验证独立读取同树原文、verdict 和 receipt。ubCode 检查图及评分，项目协议检查领取、覆盖和证明一致性。prepare 不安装旧数据库 merge driver；pre-rebase 保存所属输入，post-rewrite 通过 Git 映射校验后有限继承，失配保持待审，不自动暂存或提交。依赖环境只支持已验证的 Windows/Linux x64。

.. decision:: PR3 开发基础统一迁移
   :id: d1a844b7-4ccd-4768-bacf-4bb6e6b2fb26
   :status: active
   :implements: cfac878d-3d45-4ec9-9f36-c801048a8d34, a801ae5d-c929-4c02-b8a8-ef0b17efdca7, ee53e7ee-b742-4cf6-888e-5241df5b2840, c4af0b45-2af8-4585-ac18-6bcfc697bca7, 3914e1f5-53f4-403e-b7c4-422b52a6a2ce

   必要性来自四份原文的共同入口。保留 pnpm、uv、Sphinx-Needs、ubCode 与 Git 原生能力；worktree 私有 SQLite 仅承载原生工具缺少的领取及事务协议，不恢复旧二进制正式账本、OpenSpec archive 或自研关系引擎。影响范围是开发环境、CI、hooks、架构归属和验证入口，后续 PR 继续处理业务能力域。

.. design:: PR3 开发基础落地
   :id: 9ff69cc3-830a-4517-a9db-ae9a137d4ab9
   :status: active
   :implements: d1a844b7-4ccd-4768-bacf-4bb6e6b2fb26

   以 requirements/bindings.json 保存四份完整来源，以本文件保存唯一解读和决策；实现绑定根 CLI、hooks、CI、配置与架构模型，验证绑定真实测试、严格构建和 Git tree 输入。每份来源先完成图检视，再删除对应旧规范。


.. interpretation:: 实施顺序与范围
   :id: 0fc4f38b-f330-4c04-8bd2-ed320994276b
   :status: active
   :implements: 153c41b5-6127-4a42-91e0-5f3261d62a12

   出发点是先取得可运行的新系统，再移除被替代的旧系统。
   从可复现环境和连续迁移的角度，hooks 调用统一准备入口；CI 使用相同入口。
   新版端到端验收通过是拔除旧逻辑的先决条件。
   旧规范失去旧门禁绑定后继续保留，按能力域分多步 PR stack 迁移，原文来源可追溯。
   本次实现不将“迁移计划”登记成“迁移已完成”。

.. decision:: PR stack 分界
   :id: 0fea1910-bd2c-402a-a056-66bbdc955aa4
   :status: active
   :implements: 0fc4f38b-f330-4c04-8bd2-ed320994276b

   基础 PR：承载原计划的前两阶段。先完成新版环境、统一入口、原文与关系图、评分检视、差异与影响，以及 TypeScript/LikeC4 完整上下文和受检 Git 输入的替代验证，再整体移除旧审计、SQLite 生命周期、强制四工件及 archive 门禁，更新开发导航。
   PR 3 起：按规范能力域迁移逻辑合同、架构与实现/测试关系；每份原文只保留一个有效解读，每个能力只有一份现行逻辑权威。
   各 PR 基于前一 PR；合并由用户执行。未迁移规范保留原文件，批次迁移完成后删除对应旧文件。
   集中清理无用途 archive 是独立的显式变更，Git 保留历史，不维持永久归档副本。

.. design:: 新版验收与环境入口
   :id: ab563261-5497-473c-a849-a51894e83794
   :status: active
   :implements: 0fea1910-bd2c-402a-a056-66bbdc955aa4

   冻结 pnpm install 的 prepare 生命周期调用统一 requirements:prepare；Git 生命周期 hooks 使用同一入口，安装依赖后才启动依赖 tsx 的 CLI。
   ubCode 发行版与摘要固定，uv 使用受维护的三个目录变量和官方源，锁定解释器与完整依赖图，缓存及构建集中到根 _build。
   验收覆盖冷启动、重复并发准备、Sphinx 严格构建、断链、双重有效解读、正文/关系/评分标准变化拒绝，以及达到阈值但低于满分的提醒。
   CLI 只转接成熟工具；警告仅呈现原生意见及配置，评分通过与新鲜度由 ubCode 判断。

后续阶段验收
------------

基础替换验收必须同时覆盖真实 TS/LikeC4 全文、原子领取、令牌消费、整批失败、锁争用、恢复发布、覆盖漏项、独立 index/HEAD、变化诊断与 Git 有限继承；原生结构或评分通过不能替代这些协议验收。旧意见缺少新版领取证明时保持待审，须逐项重新领取和检视。
后续每批：逻辑合同与实现、测试及架构闭环，删除已替代规范，未迁移内容不重写。

能力批次
--------

PR 3：开发基础，迁移 repo-layout、dev-tooling、git-workflow 以及 toolchain 中仍在执行的安装、检查、编码、架构和测试合同；旧二进制正式账本与强制归档流程结束，两步审计及输入绑定合同由新版协议延续。
PR 4：工程与 Host，迁移 host-configuration、toolkit-runtime、workspace-files、file-entities，关联现役 Host、配置、文件和实体源码及行为测试。
PR 5：语言与视图，迁移 hjson-format、bescript-format、canvas-conversion、analysis，保持各格式完整原文与转换边界。
PR 6：渲染与前端，迁移 canvas-render、vscode-frontend、preview，关联 CPU 渲染、插件和预览验证。
各批先登记完整旧合同为来源并建立唯一有效解读，再记录终态设计和源码/架构/测试绑定，完成评分及严格构建后删除对应旧规范。旧文中的 TODO 继续保持待实现状态，迁移文档不将它变为已完成功能。

操作入口
--------

本地预置 pnpm 11.1.2 与 uv 0.11.8，执行 ``pnpm install --frozen-lockfile``。
prepare 生命周期和 Git hooks 均调用 ``pnpm requirements:prepare``；CI 安装相同 uv 版本并执行相同入口。
CPython 3.13.12、Sphinx 9.1.0、Sphinx-Needs 9.0.0 与全部传递依赖由 uv 管理，环境及缓存位于根 ``_build``。
ubCode 0.37.0 原生发行版按平台与 SHA256 固定，下载采用原子写入，当前支持 Windows x64 与 Linux x64。

``pnpm requirements:check`` 检查关系 schema 与工作流配置。
``pnpm requirements:build`` 严格构建文档；并行构建使用 ``pnpm requirements build <独占分组名>``。
``pnpm requirements:context --id <UUID>`` 输出关系两侧全文。
``pnpm requirements ubc agent review-brief <类型>`` 提供评分标准和正文指纹。
``pnpm requirements:verify`` 执行结构和评分门禁，缺少检视或草稿仍然失败。
``pnpm requirements verify index`` 检查暂存终态，``pnpm requirements:verify:head`` 检查已提交终态；两者独立读取各自 tree 的全部输入，执行期间所选 tree 变化失败。
``pnpm requirements:diff`` 使用标准 Git 输出需求目录的原文差异，``--staged`` 可检查暂存输入。
``pnpm requirements:impact --id <UUID>`` 使用 ubCode 原生多跳影响分析。
差异入口直接使用 Git，影响入口直接使用 agent impact。

原生 ``implements`` 表达下游对上游的依据关系，条目类型区分原文、解读、决策、设计、实现和验证。
同一关系字段使 ubCode 原生全文上下文与检视指纹覆盖每个环节，无需再开发关系上下文或摘要算法。
