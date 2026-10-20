# 第二层文件实体基础设施

## Purpose

约束第二层文件实体基础设施及格式实体合同。实体服务在第一层文件 IO 之上管理文件派生快照、逻辑实体、唯一文件归属和一次性候选。Host 消费文件变化批次并主动调用实体重读与视图刷新，第三层主动读取实体基准。本层提供格式装配和实际持久化，不承担视图转换、渲染、业务引用保护、导入导出编排或外部调用。

## Requirements

### Requirement: 唯一 GUID 格式与伴生元数据

GUID MUST 是 32 位小写十六进制字符串；工程格式与既有 BEScript 词汇 MUST 共用 Zod 校验。Hjson 语法与规范化写出遵循 `openspec/specs/hjson-format/spec.md`。GUID 伴生文件 MUST 按两种实际原生形态解析：完整原始 GUID 标量，或 guid 字段为字符串的 Hjson 对象。纯数字标量按原文校验，Hjson 对象中纯数字 GUID 必须遵循 Hjson 字符串类型规则。不得把数值转换为字符串以补救类型错误。

主体替换 MUST 保留伴生文件全部原始字节，包括额外字段、顺序、空白和换行。解析投影不承担伴生序列化。

必要性：Hjson 解释对象结构，Zod 校验固定标量。项目只声明 CreatAI 身份格式和两种已有文件形态；不自研文本解析，也不复制 BEScript 已有 GUID 校验。实际标量、Hjson 附加元数据与错误输入验证格式边界。

<!-- @formatExempt id=guid-scalar-format -->

#### Scenario: 原生 GUID 元数据解析

- **WHEN** 输入原始 GUID 或包含字符串 guid 的 Hjson
- **THEN** 返回同一身份；错误长度、字符、类型和缺失字段失败

<!-- @anchor id=entity-guid-format -->
<!-- @architecture id=toolkit.project_model.logic -->
<!-- @architecture id=toolkit.project_model.tests -->
<!-- @budget id=entity-guid-format level=S -->

### Requirement: 原始文件实体与诊断合同

文件实体 MUST 只携带主体与伴生文件的工程相对路径、原生版本和 GUID。原始快照 MUST 同时携带全部源目录的直属成员、类型和版本，作为逻辑装配唯一的元数据输入；目录覆盖从这些声明派生。不得携带绝对路径、转换结果或长期原始正文副本。可选值 MUST 使用 Option。

原始资产身份装配范围 MUST 为工程根及普通目录。具有 .guid 伴生文件的主体按文件身份登记；独立 .code、.canvas、.png、.jpg、.jpeg、.atlas、.skel 的缺失伴生文件产生诊断。普通 JSON 仅在有伴生文件时登记。地图和机械的 .map_/.mech_ 派生目录由逻辑装配读取，不把内联脚本误认成独立仓库资产。Spine 资源可位于普通目录或 .spine_ 目录，身份登记不依赖该后缀。

缺失、悬空、非法、重复 GUID MUST 只诊断，不自动补齐、删文件或换身份。重复 GUID MUST 保留全部冲突路径，GUID 查询失败，不任意选取文件。实体合同不解释 Canvas 引用、Spine 页序或渲染行为。

#### Scenario: 身份查询与完整诊断

- **WHEN** 装配原始文件及其 GUID，存在缺失、孤立、非法或重复身份
- **THEN** 快照包含明确路径诊断；唯一身份可解析，缺失查询返回 Option，冲突查询失败，磁盘保持原状

<!-- @anchor id=entity-file-identity -->
<!-- @architecture id=toolkit.project_model.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=entity-file-identity level=S -->

### Requirement: MST 持有唯一文件派生实体快照

工程文件是唯一权威，实体服务私有派生状态 MUST 使用 MobX State Tree。物理文件节点以工程相对路径为 MST identifier，以容纳重复逻辑 GUID 的诊断；GUID 查询 MUST 从当前节点派生，并通过 MST 的 identifier 解析节点。逻辑实体 MUST 仅存为一个不可变 LogicalEntitySnapshot 节点，不建立逐逻辑实体的 MST 包装或额外快照副本。不得另建长期 GUID 注册表或预先装配业务引用。

内部消费者 MUST 主动读取普通不可变快照。更新保持历史快照隔离，语义相同的重装配复用现有快照。关闭所属作用域 MUST 销毁 MST 节点。

必要性：MST 原生承担节点身份、快照和状态替换。项目只声明文件节点字段、逻辑 GUID 的唯一查询和普通合同边界；Node 的结构比较用于实际快照的同内容短路，不自造状态树或缓存算法。真实文件版本变化、重复 GUID 与历史快照验证行为。

#### Scenario: 快照隔离与重复身份

- **WHEN** 更新文件版本或装配两个相同 GUID 的文件
- **THEN** 旧快照保持原值，当前查询解析新节点；重复逻辑身份可诊断而不破坏 MST 的物理节点唯一性

<!-- @anchor id=entity-state -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=entity-state level=M -->

### Requirement: 文件集合按目录范围装配

EntitySources MUST 唯一枚举全部源目录和文件元数据，原始资产身份只在工程根及普通目录解析伴生 GUID，资源正文仅在内容读取或候选准备时经 ProjectFiles 加载。刷新 MUST 只重装配变化目录的直属文件与成员声明，保留其它已登记目录的结果；新目录递归装配，消失目录移除对应覆盖集合。LogicalEntitySources MUST 消费该快照，不再枚举目录；结构正文按需读取，读取前后的实际版本必须与声明一致。删除期间来自旧子目录的通知 MUST 根据实际父目录条目定位当前覆盖，不读取已确定不存在的目录。

伴生 GUID 解析 MUST 复核目录枚举中的版本与读取后的版本；版本变化和 IO 失败明确暴露，不提交内容与版本不匹配的快照。GUID 格式错误是身份诊断，与 IO 失败分开表达。

必要性：ProjectFiles 提供实际元数据、直接枚举和稳定字节读取。项目只负责文件与伴生文件的归属、覆盖范围及装配边界，不建立目录内容缓存或全工程正文预读。真实源目录、非 UTF-8 资源、目录增删和伴生文件诊断验证装配。

#### Scenario: 增量装配与目录覆盖

- **WHEN** 普通目录出现、直属文件变化或目录删除
- **THEN** 按实际目录条目更新对应实体与诊断，未影响的其它目录实体保持原结果，资源正文不预读

<!-- @anchor id=entity-sources -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=entity-sources level=L -->

### Requirement: 一次候选文件变更使用 Schematics

候选 MUST 经 ProjectFiles 读取主体和伴生文件，核对消费者读取时持有的实体版本并物化到官方 SimpleMemoryHost。原始资源读写 MUST 显式接收同一 FileEntity 基准，准备候选不得按 GUID 重新解析最新实体替换该基准。同步调用只针对该内存后端，不新增同步磁盘 IO。Schematics 的虚拟根地址只在防腐边界存在，项目合同和实际 IO 保持工程相对路径。

候选 MUST 只物化本次实体及关联声明所需的文件。输入字节复制为候选内容；相同内容不登记覆盖 action。读取与动作 MUST 约束在基准文件、声明目录的直属条目和候选新建目录。分支 MUST 使用同一只读基准构造独立 HostTree，通过公开 actions/apply 复制结果，再由原生 merge(Error) 检查文件冲突。空目录是 Tree 不承载的项目声明，互相冲突的创建与删除 MUST 明确失败。

提交 MUST 先复核基准文件 revision 和声明目录成员，过期失败。目录创建按深度升序执行，文件动作先写完所有目标再删除来源，目录删除按深度降序执行。重命名内容 MUST 来自同一内存基准；覆盖重命名目标使用 Tree 最终动作内容。底层承担单文件原子替换与同内容零写入；多文件提交提供明确执行顺序，失败保留实际结果，不承诺跨进程事务或回滚。

必要性：Schematics 原生提供内存 Tree、候选隔离、action 记录与严格独立计划合并。项目只物化本次必要文件、约束资源替换范围、核对实际基准并连接已有 IO。真实二进制替换、独立分支冲突、伴生文件变化和零写入验证实际链路。

#### Scenario: 候选隔离、冲突与实际替换

- **WHEN** 准备、分支或合并资源候选，再提交到工程
- **THEN** 准备不写磁盘，冲突由 Schematics 暴露，过期基准失败；实际替换保持伴生原文，相同内容零写入

<!-- @anchor id=entity-candidates -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=entity-candidates level=L -->

### Requirement: 源文件监听属于 Host 作用域

Chokidar MUST 在首次实体装配前建立监听并等待 ready；初次装配期间的事件继续登记。第一层 ProjectWatcher.take MUST 以 Effect 返回 ProjectFileChanges，由 Host 在工作区 Scope 内单一消费。changedRelativePaths 保留实际工程相对变化路径，directoryRelativePaths 从同一批变化归并得到，不代表已完成源与派生分类。实体层按唯一派生归属划分源重读范围和派生纠偏输入；源文件、伴生文件和目录事件按工程相对目录归并后主动调用实体 refresh。派生事件只交付 Views 纠偏，不导入源实体。重复路径及目录通过 Set 归并，单项 sliding Queue 仅承载唤醒信号，不丢失已登记范围。监听采用原生事件，不配置轮询或 awaitWriteFinish 轮询，不建立时间失效或心跳重扫。精确批次已交付，派生归属分类保持 TODO 明确失败。

监听资源 MUST 经 Effect Scope 获取和释放，关闭时等待 watcher.close 并释放等待资源。Chokidar 原生错误 MUST 从 take 返回，不吞掉错误或自动重建监听。源文件与派生文件的归属由架构约束划分，派生文件事件不作为源重读输入。

必要性：Chokidar 承担递归监听、事件规范化和编辑器原子保存事件合并；Effect 提供 Queue 与作用域。项目只转换路径、登记实际变化和绑定生命周期。只保留目录会丢失派生产物身份，精确路径与其归并目录来自同一批次，兼顾纠偏输入及已迁移重读能力；实体层唯一负责归属判断。真实外部文件变更及关闭验证监听链路。

独立 Canvas 主体的外部二进制修改沿用该监听与 Host 主循环，实体重读发布主体新版本，保持未修改的 GUID 及伴生元数据。读取或准备候选仍按消费者的原基准核对实际版本，旧基准失败，历史快照保持原值。阶段二的 Canvas 格式实体装配、视图影响计算和预览交付保持明确 TODO；收到源事件不等于已经更新 HTML 或 PNG。验证使用既有 CanvasConverter 生成两份合法二进制，外部修改真实文件，再通过同一 WorkspaceUpdates.watch 观察实体新版本及实际字节、旧基准拒绝和交付错误；独占工程的文件集合、伴生原文与写入量证明没有派生写入。复用既有纯转换和监听避免第二套 Canvas 文件读取或刷新生命周期。

#### Scenario: 外部修改驱动实体更新

- **WHEN** 外部进程修改源文件或删除目录
- **THEN** 实际事件驱动刷新，关闭 Host 时监听与后台任务释放

<!-- @anchor id=entity-watch -->
<!-- @architecture id=toolkit.files -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=entity-watch level=L -->

### Requirement: 内部实体服务维护文件派生快照

实体服务 MUST 随 Host 自动建立，不增加对外 Host 业务 API、MCP 工具或手工刷新命令。工程文件是唯一权威；第三层在相同 Host Scope 内主动读取文件派生快照与原始内容，不订阅实体变更流。结果以 Result 显式区分快照和 IO/监听错误，查询错误状态必须失败，不返回旧成功结果。

刷新和候选提交 MUST 由同一 Effect Semaphore 串行拥有。本地提交无论成功或失败都重新装配实际目标目录，保留已完成磁盘结果。外部目录增删与元数据枚举交错导致 IO 失败时，本次错误 MUST 返回。只有下一次实际源文件事件到来后，Host 才主动调用 refresh 完整重新装配当前工程，以重新建立失去确定性的覆盖集合；不得隐藏错误、按时间重试或返回旧快照为成功。

提交收尾 MUST 使用 Effect 原生 onExit 执行实际重读，覆盖成功、失败与取消。重读先登记原始及逻辑快照的失败结果，再把错误返回调用方；IO 失败保持失败原因，不转成 defect。提交与重读同时失败时保留两个原生 Cause；两者均成功才返回实际变更布尔值。Effect 已承担收尾、中断与原因组合，本层只连接候选提交和唯一重读，避免自研错误聚合或隐式补偿。真实文件回归验证成功写入后重读失败、部分写入保留、双重失败和后续实际事件重新装配。

原始内容读取 MUST 在读取后复核主体与伴生文件的当前版本与传入基准一致，避免监听尚未刷新时使用过期内容或身份。候选准备 MUST 先确认服务处于可用状态，版本校验继续使用消费者传入的读取基准。关闭所属作用域 MUST 中止刷新、登记关闭状态并销毁实体快照，关闭后的内部查询、读取和候选操作失败。

必要性：Effect 提供错误 Result、串行资源与 Scoped Fiber。项目只连接文件监听、实体装配和一次候选提交，不自造执行器、长期正文缓存或跨模块发布订阅。真实外部增删、GUID 冲突、候选落盘和关闭验证读取合同；测试复用文件基础设施的独占资源。

实体内部当前结果 MUST 使用 Effect Ref 保存，由 snapshot 与 logicalSnapshot 主动读取；MUST NOT 导出变更流或持有 ProjectWatcher。make 只消费 ProjectFiles。refresh 接收 Host 提供的实际变化目录，更新失败时保存错误并返回失败；Host 记录本次错误，等待下一次真实文件事件再刷新。监听本身失败时，Host 通过实体 fail 入口登记错误并结束该监听消费任务，不能循环消费已失败的监听队列。

工程状态 MUST 经实体服务确认当前可用状态并读取实际 setting.json，复用唯一 ProjectSettingsParser；文件缓存以实际原生版本选择内容。Host 和接入层不得直接读取或解析该文件。实体服务关闭或 IO 失败时查询失败。派生视图写出由本服务承担；TODO：派生归属与源事件隔离尚未实现，publishView 明确失败。

#### Scenario: 主动读取、失败与关闭

- **WHEN** 外部变化到来，提交原始资源候选或关闭 Host 后，内部消费者主动读取
- **THEN** 交付文件派生快照；状态查询读取实际最新设置，未实现视图写出明确失败；失败明确交付，实际后续文件事件驱动重新装配；关闭释放所有资源并拒绝查询

<!-- @anchor id=entity-runtime -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=entity-runtime level=L -->

### Requirement: 逻辑实体按格式装配并保持唯一文件归属

逻辑实体装配 MUST 在原始文件实体之上建立 Root、Directory、Map、Mech、Device、ScriptGroup、InlineScript、CodeRepoScript、Canvas、Resource、Spine 和 RawFile 投影。实体的父子关系 MUST 来自真实格式声明：`mech.json.list` 是零件索引，`list.json` 是脚本顺序与来源；目录枚举不得反向重建这些声明。每个文件和目录 MUST 恰好属于一个逻辑实体；未识别文件、未索引零件、缺失结构和解析失败 MUST 以 RawFile 或诊断保留。

LogicalEntity MUST 统一派生身份、主体形状与文件/目录覆盖；Root 不接收父级，LogicalChildEntity 必须接收父级身份，子实体覆盖只能位于主体目录树内，GUID 资产只能声明主体与伴生 GUID，载体可声明其格式派生目录。LogicalGuidEntity 统一 GUID 与伴生文件，LogicalDirectoryContainer、载体、零件与脚本组提供各自结构约束；零件主体目录名 MUST 与索引数字 ID 一致。LogicalEntitySnapshot MUST 校验身份唯一、路径唯一归属、父级类型、直属路径和根连通性。派生路径从主体计算，不传递重复字段；零件与脚本组集合从权威树查询，不保存无人消费的第二份 ID 数组。

必要性：MST 管理状态节点，Schematics 管理候选文件动作，两者均不声明 CreatAI 的实体形状。项目基类、层级与覆盖合同承担不可替代的格式语义，复用既有 GUID 校验和路径合同；真实工程装配、冲突归属、错误父级与主体形状验证约束。逻辑元数据不重复扫描，解析模块继续拥有结构格式。

GUID 实体的合法身份在逻辑投影中保持不变。装配不得为既有实体生成身份，不得因缺失、孤立、非法或重复 GUID 自动修复。业务引用保护、导入导出编排、视图生成和视图写回属于上层消费者。

LogicalEntitySnapshot MUST 在构造时通过原生 Map 分组建立私有、只读的 GUID 索引，保留声明顺序及全部冲突对象；索引随快照创建，不采用延迟写入或跨快照缓存。带 GUID 的查询从索引取得候选，先校验该 GUID 唯一，再应用种类、主体路径和父级筛选；缺失返回空集合。`requireUniqueGuids()` MUST 在实体层复用同一唯一性判定检查全部 GUID，包括未被绑定引用的身份。错误快照仍可承载对象和诊断，查询或全局检查遇到冲突明确抛出 `GUID 重复`，不任取一个对象。必要性：原生 Map 提供分组和索引，项目仅负责 GUID 的唯一身份语义；组合筛选、跨类型冲突、未引用冲突及重复读取的行为测试验证该职责。

#### Scenario: 真实声明构成逻辑树

- **WHEN** 工程包含载体、零件、脚本组、独立资源和未识别文件
- **THEN** 逻辑快照按索引和清单建立父子关系，所有路径只有一个归属，未识别内容和结构错误仍可诊断

<!-- @anchor id=logical-entity-model -->
<!-- @architecture id=toolkit.project_model.logic -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=logical-entity-model level=L -->

#### Scenario: 基类统一实体形状与层级

- **WHEN** 实体声明、目录成员和父级关系装配成只读快照
- **THEN** 身份、覆盖、父级类型与路径由同一模型约束；形状冲突拒绝交付快照，逻辑装配不重复枚举目录

- **WHEN** 逻辑快照包含唯一、缺失或跨类型重复 GUID，并执行组合筛选或全局唯一性检查
- **THEN** 唯一对象按筛选返回，缺失返回空集合；冲突在其他筛选前报错，全局检查也覆盖未引用身份，重复读取保持同一结果

<!-- @anchor id=logical-entity-shape -->
<!-- @architecture id=toolkit.project_model.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=logical-entity-shape level=L -->

#### Scenario: 脚本与变量具有来源声明

- **WHEN** 装配零件变量、脚本组和内联脚本
- **THEN** 变量存在性使用 Option，脚本来源、顺序和所属组明确，快照集合不可变

<!-- @anchor id=logical-script-model -->
<!-- @architecture id=toolkit.project_model.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=logical-script-model level=M -->

#### Scenario: 载体索引按实际分组读取修改

- **WHEN** 读取 mech.json 的分组 list、增加或删除零件
- **THEN** 严格校验数字 ID 与类型，拒绝重复 ID，保留分组和未知字段，不按目录反推索引

<!-- @anchor id=logical-carrier-index -->
<!-- @architecture id=toolkit.project_model.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=logical-carrier-index level=M -->

### Requirement: 逻辑实体来源装配

逻辑装配按真实格式声明读取设置、载体索引、零件元数据、变量和脚本清单；现有解析器负责语法，装配负责来源、归属和诊断。读取必须绑定原文件 revision，IO 失败直接传播。EntityAssembly 私有持有实体、诊断、已声明路径和目录父级；来源读取只通过登记操作修改状态，查询只交付只读集合，最终快照由同次装配生成。目录父级直接取实体的目录声明，不由读取方维护第二份归属。

#### Scenario: 文件归属与装配入口

- **WHEN** 单次装配工程根、目录及 GUID 资产
- **THEN** 文件归属、父级和诊断只保留在本次装配状态中，未识别文件保留为 RawFile

<!-- @anchor id=entity-source-assembly -->
<!-- @architecture id=toolkit.entities.assembly -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=entity-source-assembly level=L -->

#### Scenario: 载体与零件格式来源

- **WHEN** 从载体索引装配零件及其元数据
- **THEN** 按索引数字 ID 与类型建立零件，缺失结构、类型冲突与未索引目录保留诊断

<!-- @anchor id=logical-entity-sources -->
<!-- @architecture id=toolkit.entities.assembly -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=logical-entity-sources level=L -->

### Requirement: 逻辑脚本来源装配

ScriptListSchema 解析脚本组清单，逻辑装配保留顺序、仓库引用与内联来源身份。缺失清单、内联文件及重复名称保留诊断，不根据目录枚举重建清单。文件归属沿用同次 EntityAssembly。

#### Scenario: 脚本组与内联来源

- **WHEN** 零件声明 scripts 或 event_ 脚本组
- **THEN** 按清单建立绑定与内联实体，缺失和重复来源明确诊断

<!-- @anchor id=logical-script-sources -->
<!-- @architecture id=toolkit.entities -->
<!-- @budget id=logical-script-sources level=M -->

### Requirement: 逻辑结构文件读取

ProjectFiles 提供原始字节，既有 ProjectSettingsParser 和 GlobalVariableSchema 负责解析。逻辑来源读取绑定原始快照 revision，保留解析诊断，IO 及读取期间变化直接失败；不建立第二解析器。

#### Scenario: 设置与变量读取

- **WHEN** 从文件快照读取工程设置或零件变量
- **THEN** 合法值保持格式解析结果，语法错误保留诊断，IO 错误直接传播

<!-- @anchor id=logical-structure-read -->
<!-- @architecture id=toolkit.entities -->
<!-- @budget id=logical-structure-read level=M -->

### Requirement: 逻辑实体操作使用显式强类型候选

创建、修改、删除和移动 MUST 显式接收消费者读取时持有的 LogicalEntitySnapshot 基准，通过强类型实体操作生成一次性 Schematics 候选。主体、子树与关联父级的文件及目录声明 MUST 全部从该基准选择，准备与提交均复核声明文件和索引文件的 revision、目录成员。Host 重读取得的新快照不得替换操作的原读取基准；监听尚未刷新时仍按实际磁盘拒绝过期基准。无关实体内容变化不得使本次仍有效的声明基准失效。创建新 GUID 资产时分配身份；移动或重命名保留既有合法 GUID；Root、Map、Mech、Device、ScriptGroup 和 Spine 按操作矩阵拒绝不允许的移动或重命名。

LogicalEntitySnapshot 是唯一交付的只读主干；EntityChange 是同一读取基准上的隔离可写分支，候选不会注册回主干。实体模型 MUST 统一负责子树与操作声明选择、目标空闲、父级及名称约束、移动适用范围。EntityOperations 只编排格式更新和候选动作，实际落盘继续调用第一层四类文件原语；不建立另一个可写实体树。

删除 MUST 执行实体子树完整文件声明，不检查外部业务引用或改写消费者。RawFile 仅随所属实体子树删除，不提供独立编辑操作。Device 创建和删除 MUST 更新 mech.json.list；脚本清单替换 MUST 保持所给顺序，只引用已有组内脚本或仓库 GUID，并删除被显式移除的内联文件。缺失 vars.json 使用 Option.none，未修改时不生成文件；显式变量操作可创建、替换或删除该文件。

| 实体           | 创建                             | 修改           | 删除           | 移动或重命名                        |
| -------------- | -------------------------------- | -------------- | -------------- | ----------------------------------- |
| Root           | Host 装配                        | 工程设置       | 禁止           | 禁止                                |
| Directory      | 目录声明                         | 子实体操作     | 完整子树       | 允许；子树含固定载体或 Spine 时禁止 |
| Map / Mech     | 主体字节、GUID、派生目录与空索引 | 零件操作       | 完整子树       | 禁止                                |
| Device         | 数字 ID、元数据与索引登记        | 具名字段、变量 | 子树与索引条目 | 禁止                                |
| ScriptGroup    | scripts 或 event_ 目录与清单     | 有序清单       | 完整子树       | 禁止                                |
| InlineScript   | 源码与清单条目                   | 源码           | 文件与清单条目 | 仅同组更名；源码由调用方显式修改    |
| CodeRepoScript | 合法代码与新 GUID                | 合法源码       | 主体与伴生文件 | 保持扩展名和 GUID                   |
| Canvas         | 合法二进制与新 GUID              | 合法二进制     | 主体与伴生文件 | 保持扩展名和 GUID                   |
| Resource       | 字节与新 GUID                    | 字节           | 主体与伴生文件 | 保持扩展名和 GUID                   |
| Spine          | .spine_ 容器与子资源             | 子资源操作     | 完整子树       | 禁止                                |

必要性：MST 和 Schematics 不解释 CreatAI 的目录声明、索引和清单关系，也不关联消费者的读取起点与实际磁盘版本。项目胶水只负责上述矩阵、原读取基准的声明选择和父级结构更新，解析复用现有格式模块与文件层元数据；版本复核沿用候选准备和提交的同一实现。真实创建、清单替换、移动、读取后外部修改、过期基准与失败落盘验证职责。

候选提交 MUST 按目标先写、源路径后删的顺序编译为 `ProjectFiles` 的四类原语；失败保留已经完成的磁盘操作，禁止回滚、重试或隐式业务补偿。提交成功或失败后 MUST 从实际文件系统重新装配逻辑快照。

#### Scenario: 单实体操作闭环

- **WHEN** 基于消费者读取快照创建、修改、删除或移动允许的逻辑实体
- **THEN** 候选不写磁盘，准备和提交均复核原读取基准并按声明更新文件和目录，Host 主动重读实际结果并调度派生刷新
- **AND** 读取后外部将操作涉及的主体、子树、索引或父级目录改为 B 时，无论实体是否已重读 B，原基准的候选准备均失败且零写入，B 保持；重新读取 B 后可准备并提交，无关实体内容变化允许沿用仍有效的原基准

<!-- @anchor id=logical-entity-operations -->
<!-- @architecture id=toolkit.project_model.logic -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=logical-entity-operations level=L -->

#### Scenario: 更新只修改实体明确字段

- **WHEN** 修改资产、源码、零件字段、全局变量或工程设置
- **THEN** 类型与所属实体相符，未经修改的字节与字段保持，禁止制造缺失文件或隐式业务引用更新

<!-- @anchor id=logical-update-operations -->
<!-- @architecture id=toolkit.project_model.logic -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=logical-update-operations level=M -->

### Requirement: 格式解析与写回复用现有纯计算能力

逻辑实体层 MUST 复用脚本元数据、脚本清单、代码文件和 Hjson/Canvas 的现有解析合同，不复制文本解析器或脚本语法转换器。实体服务负责读取结构文件、组合实体关系、创建候选和连接第一层文件原语；Host 负责装配服务与生命周期，第三层视图转换不进入实体层。

必要性：Hjson 官方 comments.extract/merge 与 keepWsc 承担结构改写后的注释、键顺序保留；项目只声明可编辑字段和索引标量规则。未修改文件逐字保持；修改文件保持字段语义与注释，不承诺空白逐字相同。数组按官方位置关联注释。CodeFileReader、ScriptListSchema、GlobalVariableSchema、ProjectSettingsParser 和 CanvasConverter 提供现有格式校验，实体层不复制解析器。

<!-- @formatExempt id=carrier-index-scalar -->

#### Scenario: 现有格式合同驱动读写

- **WHEN** 逻辑实体读取或修改设备元数据、脚本清单、代码文件和 Hjson
- **THEN** 使用现有格式解析与官方 round-trip，未知字段和注释保持，格式错误进入诊断或失败结果

<!-- @anchor id=logical-entity-formats -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=logical-entity-formats level=M -->

#### Scenario: Hjson 官方注释往返

- **WHEN** 修改对象具名字段或有序数组
- **THEN** 官方注释提取合并保留注释与未知字段，删除字段不序列化为 undefined 字符串

<!-- @anchor id=logical-hjson-document -->
<!-- @architecture id=toolkit.project_model.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=logical-hjson-document level=M -->

### Requirement: 格式实体承载语义与无损来源

FormatEntity MUST 作为格式实体与派生视图实体的共同抽象根，统一实体身份、工程相对路径、读取基准及文件编码出口。DeviceEntity、ScriptEntity、CanvasEntity 和 ToolkitEntity MUST 继承此根；TsViewEntity、HtmlViewEntity 经 ToolkitEntity 继承相同根约束。根合同中的路径描述该实体自身的文件位置。DeviceEntity、ScriptEntity、CanvasEntity MUST 分别携带零件 Context、LocalAst 和 CanvasDocument，并保留重建原文件所需的格式来源、未知字段与伴生信息。原始字节只作为来源信息，MUST NOT 代替语义。既有 LogicalEntitySnapshot 继续是身份、层级与文件归属的唯一状态；格式实体是按需内容，不建立第二套状态树。

未修改原版格式实体的 encode MUST 返回与原文件逐字节一致的内容，包括编码、BOM、顺序、注释和换行。派生视图实体的 encode 遵循派生 UTF-8 内容合同；两类均实现根实体的同一编码出口。Hjson keepWsc 只提供其实际支持的注释保留，MUST NOT 宣称其规范化输出满足字节级无损。TODO：完整语义装配与各原版格式无损编码尚未接通，装配和编码入口明确失败。

读取入口 MUST 显式提供完整 EntityReadBaseline；格式实体构造、语义编辑和视图反向转换 MUST 沿用该基准，不得用主体文件声明重建局部基准。基准包含实际读取的关联文件、目录与固定输入，实体自身的文件归属仍由 declaration 表达。

必要性与取舍：现有纯库已提供脚本 AST、零件语义和 Canvas 模型，MST 提供身份状态；二者不提供完整的多文件格式实体。复用共同根的 EntityReadBaseline 比各格式自行重建局部基准多一个显式构造参数，但保留关联依赖且避免第二份基准。项目合同只连接已有语义和无损来源；真实 TS 与 HTML 反向转换验证编辑后保留关联文件、目录及固定输入，未完成编码验证明确失败，完成后以真实文件字节和独立语义编辑结果验收。

#### Scenario: 未完成格式能力明确暴露

- **WHEN** 请求尚未实现的零件、脚本或 Canvas 格式实体，或调用其编码
- **THEN** 失败并指出对应 TODO，不把原文回放或空数据当成完成的编解码

<!-- @anchor id=entity-format-contract -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=entity-format-contract level=L -->

### Requirement: 派生视图实体与持久化归属

ToolkitEntity MUST 继承 FormatEntity，并派生 TS 与 HTML 两种具名视图实体，携带源实体身份、基准文件版本、视图相对路径、文本及相应官方 TS AST 或 Canvas 模型。身份、文件位置、读取基准和编码出口沿用根实体约束，基准字段只由根声明。视图自身以 viewRelativePath 标识和定位，关联的原版实体身份由 sourceEntityId 表达。视图编码只表达派生 UTF-8 内容，不宣称原版实体无损重建。写出 MUST 经实体层执行第一层 IO；派生文件归属与原始实体分开，派生文件事件不得循环导入为源实体变化。TODO：派生归属、写出和源事件隔离尚未实现，publishView 明确失败。

必要性：官方 TS AST 和 Canvas 模型表达内容语义；项目只连接源身份、基准与派生文件，复用原生 TextEncoder。视图编码用非 ASCII 文本验证实际字节，写出未接入时验证失败。

终态读取基准 MUST 统一为 EntityReadBaseline，覆盖实际使用的文件、目录和固定输入；格式实体与视图实体沿用共同根的唯一基准字段。实际读取与依赖闭包由实体读取入口装配，未实现时明确失败，不在 Preview 请求上追加第二份基准补丁。派生归属只用于源隔离和同一 Host 调度纠偏，不引入第二实体状态树。

ProjectEntities.classifyChanges MUST 接收工程相对变化路径，按 publishView 使用的同一派生归属交付 EntityFileChanges：源重读目录与派生视图纠偏路径。Host 唯一消费变化批次，分别主动调用实体 refresh 和 Views 纠偏；分类不执行刷新或调度。TODO：分类入口明确失败，Host 接入前保留现役目录批次入口。备选为 Host 自行推断归属，但会复制实体层的发布知识；显式分类合同增加一个调用边界，使写出和事件隔离保持唯一归属，验证未接入时失败而非交付空批次。

#### Scenario: 视图实体形状与 UTF-8 编码

- **WHEN** 构造 TS 或 HTML 视图实体并编码
- **THEN** 实体沿用共同根的身份、文件位置与完整读取基准，携带源身份及对应内容模型；编码产生其文本的 UTF-8 字节

<!-- @anchor id=view-entity-contract -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=view-entity-contract level=M -->

#### Scenario: 源文件与派生视图变化分类

- **WHEN** 请求分类工程相对变化路径
- **THEN** 按唯一派生归属交付源重读目录与派生视图纠偏路径，分类只交付结果；当前分类入口由 TODO 明确失败，磁盘不写入

<!-- @anchor id=view-source-classification -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=view-source-classification level=L -->

### Requirement: files 模式旁路派生文件纠偏

files 模式下，已登记派生视图被外部工具修改或删除时，Host MUST 沿同一 `ProjectWatcher` 批次保留精确派生相对路径；实体层 MUST 通过 `classifyChanges` 按唯一归属把它与源目录分开，源目录才进入 `refresh`，派生路径交给 Views 计算应恢复的视图。源实体本身未变化不能跳过这次派生纠偏。

Views 计算出应交付的视图后，files 模式 MUST 经实体层 `publishView` 写回派生文件；内容相同的恢复 MUST 产生零写入，恢复产生的监听事件 MUST 再次经过同一归属判断后收敛。派生文件缺失、内容被替换和源文件同时变化必须使用同一批次处理，不能另建监听、轮询或旁路写入。memory 模式不执行派生文件纠偏或写回。

当前 `classifyChanges`、`publishView`、`WorkspaceUpdates.refresh` 和 Views 派生刷新均保持明确 TODO；因此旁路篡改的正式纠偏能力尚未完成。阶段二只验证精确路径保真、输入不变、TODO 错误和零写入，不能把监听到事件或返回空集合表述为已经恢复视图。

#### Scenario: 实体层分类与派生视图发布

- **WHEN** 实体层收到实际变化路径或待发布的派生视图
- **THEN** 分类与发布使用同一派生归属，源重读目录与派生纠偏路径分开；写出经第一层 IO，内容相同零写入且自身通知仍归为派生变化；当前分类与发布入口由 TODO 明确失败，磁盘不写入

<!-- @anchor id=derived-view-entity-reconciliation -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=derived-view-entity-reconciliation level=L -->

#### Scenario: Views 计算纠偏影响与交付

- **WHEN** Views 收到源更新删除及已登记派生视图的实际变化
- **THEN** 按唯一源到视图关联计算应交付的视图，源未变化的派生路径仍参与纠偏；files 模式经实体层发布，memory 模式保持内存交付；当前刷新与发布入口由 TODO 明确失败，磁盘不写入

<!-- @anchor id=derived-view-delivery -->
<!-- @architecture id=toolkit.view_service -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=derived-view-delivery level=M -->

#### Scenario: Host 同批次编排派生纠偏

- **WHEN** files Host 监听到已登记视图被删除、被替换或与源文件同批变化
- **THEN** 同一监听批次保留精确路径，由实体分类后仅重读源目录，并把派生路径交给 Views 纠偏；memory 模式保持同一调度且不写入派生文件；当前刷新入口由 TODO 明确失败

<!-- @anchor id=derived-view-reconciliation -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=derived-view-reconciliation level=M -->
