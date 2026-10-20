# toolchain Specification

## Purpose

治理工具与新版产品使用统一检查。统一架构模型覆盖仓库根、治理工具、新版产品、旧产品代码、规范、资源与夹具的文件合法性及逻辑关联。旧产品代码统一位于 `legacy/`，作为参考文档登记；类型、风格、锚定与实际源码依赖检查覆盖模型声明的全部现役源码容器。

## Requirements

### Requirement: 单一入口

根 package.json scripts MUST 是执行入口。安装经 pnpm install --frozen-lockfile，锁定更新经 pnpm install --lockfile-only --no-frozen-lockfile；根 pnpm MUST 以 verifyDepsBeforeRun: install 与 frozenLockfile: true 在运行命令前原生校验并自动建立匹配依赖。TypeScript、ESLint、Prettier、Knip、Vitest MUST 经各自官方命令执行；项目 CLI 只处理项目特有规则和逐锚点检视，不管理依赖、容器、缓存或远端协作。未知命令 MUST 失败。CLI 执行异常 MUST 经 Node 原生错误输出保留错误类型、消息、调用栈及 cause，非 Error 拒绝值仍保留原值，退出码为 1。后台 Git 与 CLI 验证子进程 MUST 使用 Node 原生 windowsHide 选项，在 Windows 保持隐藏执行，输出仍归当前调用方。pnpm 的宿主启动只有一张平台表：`win32` 使用 `cmd.exe /d /s /c pnpm.cmd`，`aix`、`android`、`cygwin`、`darwin`、`freebsd`、`haiku`、`linux`、`netbsd`、`openbsd`、`sunos` 直接使用 `pnpm`。未列入的宿主直接失败，禁止改用条件写法或默认宿主填补缺席平台。子进程未能启动或没有退出码时返回 1。Node `child_process` 能隐藏窗口并执行给定文件，但不区分 pnpm 的 Windows 命令脚本与其余宿主的可执行文件；平台表只负责这一约束。

自研逻辑 MUST 在其所属 Requirement 正文中记录必要性论证：具体功能约束、已盘点的原生工具或受维护依赖、这些工具不承担的项目语义、保留实现的最小职责与对应行为验证。已有能力 MUST 直接复用；不能证明功能必要性的实现 MUST 删除，不得用现有代码规模或检查通过代替论证。此项是规范编写与实质检视纪律，不新增论证文件或执行入口；论证随功能终态维护，不保存实施流水账。决策记录、引用与已归档标准 change 的检查由「决策记录」承担，必要性仍写在对应 Requirement 正文。

需求权威来自用户明确要求；规范 MUST 如实表达该要求。每项设计与治理规则 MUST 说明必要性、现有能力、备选方案和 Trade-off，核对收益、复杂度及工程合理性；论证缺失的内容 MUST 删除，不能以先写入 spec 再要求服从的方式扩张需求。架构模型表达职责、文件归属、生命周期与模块依赖，源码类型与成员契约由 TypeScript 承担，不重复维护逐成员访问清单。

<!-- @decision id=ADR-0002 -->

#### Scenario: 调用工具

- **WHEN** 人工、钩子或 CI 调用工具
- **THEN** 经根 pnpm scripts 调用标准工具与项目规则，成功返回 0，违规与执行失败返回非 0，错误包含位置或具体修正要求；pnpm 按该平台表启动，未能启动或没有退出码时返回 1

环境变量 MUST 集中登记名称与真实用途；没有必要性依据的变量 MUST 删除。环境变量 API 访问 MUST 经 TypeScript AST 定位，包含直接访问、导入别名及动态环境对象访问，精确命中 MUST 登记或删除，不接受疑似项豁免。诊断 MUST 包含文件与行号。
疑似环境变量 MUST 使用正则补充检测：TS 只扫描字符串常量与模板文本，不把标识符、普通对象键或文件名当作环境变量；其他格式扫描文本内容。疑似命中 MUST 登记、删除或以紧邻的 `@non-environment-variable 理由` 标明非环境变量；标签 MUST NOT 豁免 API 命中。

<!-- @anchor id=cli-boundary -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=cli-boundary level=XL -->

### Requirement: 审计命令分派

Review CLI 复用 Node parseArgs 和 Zod 校验参数，将用户选择交给现有正式入口；不承载数据库或审计算法。未知命令、位置参数及多余选项必须失败。

#### Scenario: 审计命令参数与入口

- **WHEN** Review CLI 解析 audit、record、verify、rebuild 与合并事件参数
- **THEN** 只调用对应正式入口并拒绝未知或多余参数

<!-- @anchor id=review-cli-dispatch -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-cli-dispatch level=L -->

### Requirement: 宿主进程执行

Git 与 pnpm 复用 Node child_process。后台子进程使用 windowsHide；pnpm 按单一入口登记的平台表启动，启动错误或缺少退出码按失败返回，标准输出仍归调用方。

Windows 上 Agent、开发工具、钩子与测试启动的后台命令 MUST 从最外层入口到子进程全程隐藏窗口，执行期间 MUST NOT 新建、闪现或抢焦点的终端窗口。该要求覆盖命令解释器、包管理器及其调用的工具；只在仓库内某个子进程设置 windowsHide 不构成完整满足。只有用户明确要求打开的交互窗口才可显示。

Node 启动使用原生 windowsHide；PowerShell 启动后台命令使用原生 Start-Process -WindowStyle Hidden，并通过标准流重定向保留输出和错误。调用方 MUST 跟踪实际进程的完成状态与退出码，不能把启动成功当成检查通过。文件输出遵守顶层 _build 落点与任务隔离要求，命令仍使用根 scripts 和正式 CLI。MUST NOT 通过一次性包装脚本、改写依赖源码或另造执行器解决弹窗。

必要性与取舍：后台检查须在用户继续使用桌面时执行，隐藏启动属于每个进程创建边界的责任，不能由后代进程选项追溯控制祖先进程。Node child_process 与 PowerShell Start-Process 已提供隐藏窗口、输出重定向和退出状态，直接使用这些原生能力即可；代价是隐藏执行时通过调用方输出或日志观察进度。验收以真实命令执行期间的窗口行为、输出与失败退出码为准，配置字段存在不代替实际行为。

#### Scenario: 宿主进程与 Git 边界

- **WHEN** 工具启动 pnpm 或 Git
- **THEN** 使用登记的平台命令，从最外层入口到子进程全程隐藏窗口，输出及失败状态交还调用方

<!-- @anchor id=process-boundary -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=process-boundary level=M -->

### Requirement: 架构唯一来源

`tools` 目录树下的 LikeC4 源文件 MUST 共同构成架构层级、职责、文件登记、预算阈值与允许依赖的唯一权威。解析 MUST 把这些源文件按固定顺序拼成一次输入：`tools/model.c4`、`tools/pure.c4`，然后是其余 `tools/**/*.c4` 按工程相对路径排序。分层子图使用同一模型标识和解析入口。拼接结果交给现有 `ArchitectureRules.declarations`。规范、README、AGENTS 与实现代码 MUST NOT 另行定义架构层级、容器职责、文件归属或依赖边；纯函数系统放在独立源文件，MUST NOT 另写解析器或第二套元素 ID。

#### Scenario: 架构模型统一解析

- **WHEN** 解析聚合 LikeC4 源码
- **THEN** 只生成同一次官方模型的架构声明、文件归属与允许依赖

<!-- @anchor id=architecture-model -->
<!-- @architecture id=tools.architecture -->
<!-- @budget id=architecture-model level=XL -->

### Requirement: 架构范围与检查合同

元素的 `metadata.files` MUST 为非空字符串数组，组织元素可不登记文件；叶元素 MUST 登记文件。

每个 system/container MUST 使用 LikeC4 原生非空 `description` 写明职责与边界；注释只作补充，MUST NOT 替代原生描述。实现绑定 MUST 使用已登记的 `files`、`sourcePath`、`entries` 与 `code`，不得手写另一套字段语法。元素 metadata MUST 使用封闭白名单：files、sourcePath、entries、code、specPath、checks、isolates、pending、runtime、textExempt、freeze、gap、todo、testStrategy、decision，以及 S/M/L/XL 的五维 budget 字段；未知字段 MUST 在解析时拒绝。`decision` 是可选字符串，引用是否指向当前 ADR 由决策记录检查判定。`freeze` 在唯一元素上登记旧版参考区冻结基线提交（40 位小写十六进制），全模型至多声明一次，语义与门禁行为见 `openspec/specs/repo-layout/spec.md` 的「旧版参考区冻结」。职责与实际实现的语义一致性 MUST 阅读节点描述和完整关联源码检视，非空校验不代替正文判断。

自检系统以 `metadata.checks = selfcheck` 指定，持有公共预算阈值；`sourcePath` 与 `specPath` 指定源码容器和锚定规范范围。源码容器可以属于治理工具或产品系统，其文件声明 MUST 位于自身范围内；所有源码容器 MUST 纳入同一检查，彼此之间只有模型声明的依赖边允许通过，且每条声明的依赖边 MUST 与真实代码依赖对齐或携带显式豁免。

检查面 MUST 从同一次模型解析派生，MUST NOT 在代码、JSON 或另一模型中重抄。

tools 的工作区文件枚举 MUST 统一经过 `WorkspaceFiles`，范围由架构登记派生；消费方 MUST NOT 自行 glob、递归目录或维护源码路径清单。

内容 lint MUST 覆盖全部现役实现；标准工具（TypeScript、ESLint、Knip、锚定与测试）同时覆盖各组件的有效测试，复杂度对测试只计量与登记实测值、循环依赖除外（任何循环依赖 MUST 独立失败，不按区豁免）。生产语义规则只扫描架构用途为 `tool` 或 `common` 的实现文件，测试中的被测源码字符串与临时夹具不得被当作实现重解释。旧产品代码按文档登记。标准工具以原生配置声明其输入，项目规则从架构模型派生源码范围。文件登记检查 MUST 覆盖全仓当前文件。

pnpm check:lint、pnpm check:types、pnpm check:dead-code MUST 检查当前工作树；单项通过不构成完整门禁通过。

手动路径疑似检查 MUST 只检查 tools TS 字符串常量在文件系统、路径拼接及统一读取抽象调用参数中的位置，候选根由架构文件登记推导。命中若确为扫描范围 MUST 改用统一抽象；确认属于其他用途时，MUST 以紧邻的 `@non-manual-path 理由` 标明非手动枚举路径。该标签不授予环境变量或 fallback 豁免。

dependency-cruiser MUST 执行源码依赖约束；模型错误、源码容器范围重叠、未覆盖源码及未解析依赖 MUST 拒绝。反向对齐检查 MUST 与依赖约束共用同一次模型解析与文件归属。

工具实现与自身测试 MUST 全部受约束。

<!-- @decision id=ADR-0002 -->

旧代码 MUST 位于 `legacy/` 并登记于 `document` 元素，作为参考文档维护职责、归属及模型关联，MUST NOT 混入 `src/`、搬入临时放行区或另建忽略清单。

文档元素 MUST NOT 声明 `sourcePath` 或 `checks`。

文档 MUST 同受文件双向覆盖与模型关联合法性校验；其内容 MUST NOT 因源码扩展名被当作当前可执行源码，也 MUST NOT 因文档身份自动获得运行依赖许可。

文档之间的模型关联表达参考关系，实际源码依赖校验仍由显式源码容器派生。

语法依据：LikeC4 官方 Model 文档的 Element Kinds、Metadata 与 Relationships；解析位置：`tools/governance/derive-rules.ts` 的 `ArchitectureRules.declarations`。

#### Scenario: 架构约束执行

- **WHEN** 源码出现模型未允许的依赖，或文件不属于唯一容器
- **THEN** 检查失败；依赖库由库自身管理，仓库内代码不得借此绕过约束

<!-- @anchor id=architecture-scope -->
<!-- @architecture id=tools.architecture -->
<!-- @budget id=architecture-scope level=L -->

### Requirement: 架构依赖执行边界

源码文件归属与实际依赖通过 WorkspaceFiles、ArchitectureFiles 和 dependency-cruiser 检查；模型声明与实际依赖共用同一份 ArchitectureDeclarations。此边界复用已有文件匹配与官方依赖解析，仅负责执行及定位诊断。

#### Scenario: 架构依赖执行

- **WHEN** 检查源码文件归属、缺失登记和 dependency-cruiser 结果
- **THEN** 每个文件恰好属于一个容器，模型声明与实际依赖边一致

<!-- @anchor id=architecture-dependencies -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=architecture-dependencies level=L -->

### Requirement: 架构派生契约

模型解析提供统一的派生类型；标签、用途、文件归属与实现边界保持下列合同。

#### Scenario: 架构派生类型契约

- **WHEN** 模型解析向检查模块提供架构声明
- **THEN** 元素用途、容器范围、入口、依赖规则与审计片段采用同一 ArchitectureDeclarations 类型，不重抄配置
- **AND** 架构偏离以 `#gap` 与非空 `metadata.gap` 成对登记并使检查失败；已经建立声明合同但执行未实现的能力以 `#todo` 与非空 `metadata.todo` 成对登记并报告待办。标签或原因缺一 MUST 在解析时拒绝。两种状态均不豁免目录、文件、依赖、交互或职责边界检查。待办不得作为可调用能力，实际越界必须修正实现，不能以待办隐藏。此规则统一适用于所有检查，不设置阶段开关。

Issue #131 第二步原文：“仅 按照c4架构图 去搬迁 位置不正确的旧代码 并 机械修复引用 & 修复测试(仅限于修复已有的因迁移而损坏的测试)”；“具体实现全都是空壳 只需要推演和建立文件级别的容器骨架”；“这一步的schema是粗略的非最终版本的schema”。只有符合这些要求的执行未实现允许保留，其余违规 MUST 失败。`#todo` 标签与非空理由只登记状态，不构成符合要求的证明。粗略声明须表达模块职责、输入输出、控制权和生命周期，不要求最终字段全集、逐成员调用绑定或实际业务编排。未实现能力 MUST 明确失败，不交付伪成功。完整关联的架构、规范与源码按现有检视合同核对；结构检查和有效迁移测试照常执行，不因标签跳过。

必要性与取舍：LikeC4 解析器验证登记结构，TypeScript、dependency-cruiser 与现有治理规则验证声明、引用和约束，有效测试验证未实现入口的实际失败；职责语义沿用完整正文检视。复用这些能力可保留用户要求的空壳基线及其他违规的失败，不新增逐符号清单、阶段开关或第二套语义判定器。

<!-- @anchor id=architecture-metadata -->
<!-- @architecture id=tools.architecture -->
<!-- @budget id=architecture-metadata level=M -->

### Requirement: 架构源文件合并

LikeC4 官方入口 `fromSource` 解析一段模型文本，`fromWorkspace` 读取工作区文件。检视必须读取受检树里的 git blob。因此合并步骤只按上面的固定顺序取出 blob 或当前工作区文本，拼成一次 `declarations` 输入。原生解析器不承担提交版本选择，项目只保留这个顺序和读取边界。

#### Scenario: 纯函数源文件进入同一次模型

- **WHEN** 仓库同时存在 `tools/model.c4`、`tools/pure.c4` 和 `tools/canvas.c4`
- **THEN** 同一次解析得到 `pure` 与 `pure.canvas`，并且 `tools/model.c4` 仍在拼接文本的前面

<!-- @anchor id=architecture-project -->
<!-- @architecture id=tools.cli -->
<!-- @architecture id=tools.tests -->
<!-- @budget id=architecture-project level=M -->

### Requirement: 用途分类

登记文件的元素 MUST 恰好携带一个用途标签，封闭枚举：`doc`（退化为文档）、`build`（参与构建）、`test`（测试）、`tool`（内部工具）、`common`（内部通用）、`acl`（外部依赖防腐层）；标签 MUST 在 `specification` 块声明，未登记文件的组织元素 MUST NOT 携带用途标签。
`document` 元素的用途 MUST 为 `doc`；`acl` 用途与 `isolates` 被接管包登记 MUST 成对出现。
用途标签 MUST 使每一处代码的用途在架构图中可判定，缺失、多标或不成对 MUST 在模型解析期拒绝，MUST NOT 靠约定维持。
锚定比较 MUST 能解析先于本体系的历史模型：强制以模型自身 `specification` 块声明任一用途标签为生效信号，未声明用途标签的模型 MUST 跳过用途校验。

#### Scenario: 元素用途缺失或冲突

- **WHEN** 登记文件的元素未携带用途标签、携带多个用途标签，或 `acl` 用途与 `isolates` 登记不成对
- **THEN** 架构模型解析失败并定位元素标识

<!-- @anchor id=purpose-classification -->
<!-- @architecture id=tools.architecture -->
<!-- @budget id=purpose-classification level=M -->

### Requirement: 架构文件双向覆盖

架构元素 MUST 直接登记工程相对文件路径、以 `/` 结尾的递归目录或 glob，MUST 使用 `/` 分隔符。
目录匹配 MUST 按完整目录前缀，glob MUST 使用 Node `path.posix.matchesGlob`；语法依据为 LikeC4 官方 Model 文档的 Metadata 与 Array Values、Node 官方 Path API。
每个受检文件 MUST 且只能命中一条声明，每条声明 MUST 命中至少一个文件；父元素的包含关系 MUST NOT 隐含文件放行。
现役 TS/TSX 文件 MUST 归属声明 sourcePath 的源码容器；自检根系统直接登记的工具配置属于检查入口配置，不作为业务源码容器。sourcePath MUST 是实际目录，文件声明 MUST 位于其范围内，其目录中的现役 TS/TSX 源码 MUST 由该容器直接登记，其他节点不得冒领；同目录中的非源码资源仍按各节点 files 声明确定唯一归属。entries MUST 是当前存在且由该节点直接登记的文件。code MUST 指向现役源码中已锚定的真实符号，且引用节点 MUST 位于拥有该文件的源码容器内；跨模块冒领 MUST 拒绝。依赖边 metadata 允许 gap、todo、pending、runtime 原因字段。模型描述、文件归属和实际源码不一致 MUST 作为架构冲突修复，不得通过修改审计记录掩盖。
普通检查清单 MUST 来自 Git 列出的当前存在文件及未忽略的新文件；提交检查先验证治理输入已全部暂存。清单包含符号链接与 gitlink，MUST NOT 展开子模块或把构建输出混入源码清单。
检查 MUST 输出元素标识和工程相对路径。

#### Scenario: 文件与架构登记不一致

- **WHEN** 文件未登记、声明未命中文件或文件重复归属
- **THEN** 门禁失败；源码所在文件已被登记 MUST NOT 代替源码依赖校验

<!-- @anchor id=architecture-files -->
<!-- @architecture id=tools.architecture -->
<!-- @budget id=architecture-files level=L -->

### Requirement: 依赖边双向对齐

源码容器之间每条声明依赖 MUST 对应真实代码依赖；目标容器没有源码或目录时 MUST 明确失败，MUST NOT 因实际扫描为空而跳过。`#pending` 仅标记待实现状态，不豁免目标缺失。`#runtime` 表达没有静态 import 的资源或进程边界交互，必须以同名非空 metadata 声明原因；出现静态依赖时 MUST 报告形态冲突。标签与原因 MUST 成对声明；`#pending` 与 `#runtime` MUST NOT 同时声明；标签只用于源码容器关系。

正式独立入口 `pnpm check:architecture` MUST 读取同一聚合模型并执行文件、目录和模块依赖双向校验；通用检查继续调用同一实现。职责和数据流语义 MUST 阅读 C4 description 及完整关联源码检视，结构检查不冒充语义证明。图表达目标架构；源码违规阶段保留实际失败，MUST NOT 通过假调用或修改目标迎合现状制造通过。Pi 接入和资产生成管线 MUST 完整登记目标层级、配置、生命周期和数据流；合同齐备的执行待办通过 `#todo` 通报，实际缺失与偏离通过 `#gap` 阻断，架构登记不使业务成为可调用能力。

必要性与取舍：dependency-cruiser 提供真实模块依赖、循环和解析失败检查，LikeC4 提供职责与关系模型；项目只将实际依赖映射到文件所属容器并双向核对声明边。模块粒度约束拓扑，成员类型由 TypeScript 校验，职责和控制权按完整正文检视，不自研逐符号权限数据库。真实工程验证合法依赖通过、反向及缺失依赖失败、runtime 形态冲突失败。

dependency-cruiser 的 JSON reporter 返回结构化违规，不保证违规使 reporter 退出码非零。检查 MUST 同时核对 `summary.violations` 与执行退出码，任一违规或执行失败均返回非零；`#todo` 不改变这一判定。

外部运行时与服务 MUST 使用 `#external` system 表达；其职责描述必填，MUST NOT 登记仓库 metadata、用途标签或子元素。仓库源码、配置和参考原文仍由各自节点唯一登记，MUST NOT 使用外部标记逃避源码检查。外部边界不要求虚构本仓库文件；现役源码容器的目录与依赖检查保持严格。

LikeC4 原生标签负责表示外部系统，但不负责本仓库文件归属；项目解析器仅负责验证这个归属边界。必要性由模型解析和真实归属检查验证：合法外部 system 无需占位文件，container 或携带仓库 metadata 的伪外部节点必须拒绝。

#### Scenario: 声明边悬空或豁免失效

- **WHEN** 目标源码缺失、声明模块依赖没有实际引用，或 runtime 边出现静态依赖
- **THEN** 检查失败并列出该边

<!-- @anchor id=edge-alignment -->
<!-- @architecture id=tools.architecture -->
<!-- @budget id=edge-alignment level=M -->

### Requirement: 规范结构

规范 MUST 用 Markdown AST 解析为三级 Requirement 和四级 Scenario。

Scenario MUST 属于一个 Requirement，MUST 且只能直接持有一个锚点，ID MUST 在受治规范中全局唯一。

Requirement MUST 有 Scenario。

Requirement 与 Scenario 正文 MUST 支持多行段落、列表与表格，MUST NOT 强制长正文单行。

OpenSpec 1.13.0 的 `dist/core/parsers/requirement-text.js` 与 `dist/core/parsers/markdown-parser.js` 使用多行内容解析；`schemas/spec-driven/schema.yaml` 要求标题层级及 WHEN/THEN，没有正文单行限制。

仅锚定标签 MUST 保持单行。

每个 Scenario MUST 锚定架构目标或显式声明非代码内容；架构锚定与 @nonCode MUST NOT 同块混排。

架构关联形态 MUST 至少包含一个 `<!-- @architecture id=元素完整名称 -->` 标签。

ID MUST 直接引用上述唯一架构模型中的元素完整名称，MUST NOT 引入第二套架构 ID；同 Scenario 内 MUST NOT 重复引用，多个 Scenario 可引用同一元素。

引用仅由 Scenario 指向架构，架构 MUST NOT 反向保存规范 ID 或检视记录。

非代码声明形态 MUST 且只能是一个 `<!-- @nonCode -->` 裸标签，MUST 紧邻 @anchor 之后，MUST NOT 携带任何字段，声明该 Scenario 为不锚定架构与代码的纯文本条款。声明 @nonCode 的 Scenario MUST NOT 携带 @budget 预算标签；预算对锚定代码的体量与复杂度设限，非代码条款没有度量对象。

待实现声明形态 MUST 且只能是一个 `<!-- @todo -->` 裸标签，MUST 位于全部 @architecture 行之后、锚点块末尾，MUST NOT 携带任何字段，声明该 Scenario 的架构目标已定而实现尚未落地。声明 @todo 的 Scenario MUST NOT 携带 @budget 预算标签，MUST 锚定至少一个在架构模型中携带 #todo 的元素；实现落地时 MUST 移除 @todo 并补齐预算标签，代码锚定待实现 Scenario MUST 拒绝。

规范文件中的 XML 注释 MUST 只有两种合法块，其余一切形态（含位置、顺序、空行数、字段、标签名的任何偏离与行内形态）MUST 直接判违规，MUST NOT 静默吞并，MUST NOT 按前缀放行：

Scenario 锚点块 MUST 是 Scenario 正文的最后一个块级节点，与上方正文 MUST 恰好相隔一个空行，块后 MUST NOT 再有正文；块内首行 MUST 且只能是一个 @anchor，其后形态三选一：连续跟随至少一行 @architecture 并恰好含一行 @budget 预算标签；恰好一行 @nonCode 独占锚点块；连续跟随至少一行 @architecture 并以恰好一行 @todo 收尾；行间 MUST NOT 出现空行；每行 MUST 完整匹配固定字段并收敛为锚点与架构锚点列表或单一非代码、待实现声明的确定性结构。

Requirement 层级块 MUST 只含 @formatExempt 行、至多一行无 id 的 @budget 行，以及至多一行 @decision，MUST 只出现在 Requirement 层级，前后 MUST 各恰好相隔一个空行。

行内 HTML MUST NOT 出现在规范文件中；合法性判定 MUST 只依赖 Markdown AST 的节点类型与位置信息和 schema 完整匹配，MUST NOT 使用正则或文本扫描。

<!-- @decision id=ADR-0002 -->

#### Scenario: 规范结构检查

- **WHEN** 规范缺少层级、Scenario、锚点，或锚点不直接归属 Scenario，或锚点块位置、顺序、空行数不符确定性结构
- **THEN** 检查失败；代码围栏和示例文本不得被误识别为结构声明

<!-- @anchor id=spec-structure -->
<!-- @architecture id=tools.anchoring -->
<!-- @budget id=spec-structure level=XL -->

### Requirement: 极简标记

标记 MUST 仅含有意义的 ID；正式审计记录存于同树 tools/review.sqlite，归属由承载该树的提交表达。

代码采用单行 JSDoc 标签 @anchor 后的位置参数 ID；规范统一采用单行 XML 注释，内容为 @anchor id=名称。

非代码声明统一采用单行 XML 注释 `<!-- @nonCode -->`，MUST NOT 携带 ID 或任何其他字段。

待实现声明统一采用单行 XML 注释 `<!-- @todo -->`，MUST NOT 携带 ID 或任何其他字段。

Markdown AST 定位注释节点，固定字段经 schema 完整匹配解析得到锚点；白名单之外的一切形态 MUST 判违规，无法识别的 XML 标记 MUST NOT 被静默吞并。

决策引用 MUST 是 Requirement 层级的单行 `<!-- @decision id=ADR-编号 -->`，只承载 ADR 编号。其余标记 MUST NOT 接收额外字段，MUST NOT 并列支持第二套标记格式。

<!-- @decision id=ADR-0002 -->

#### Scenario: 标记格式检查

- **WHEN** 标记字段缺失、重复、多余
- **THEN** 报错；标记存在不证明实际检视，工具不得宣称已完成人工审阅

<!-- @anchor id=anchor-format -->
<!-- @architecture id=tools.anchoring -->
<!-- @budget id=anchor-format level=M -->

### Requirement: 声明覆盖

类、接口、枚举及其非 private 方法 MUST 各自锚定；公开构造器与访问器属于方法，字段不要求。
锚定为可选的声明：private 方法与非导出类型声明（type 别名及非导出 interface、enum 等类型级声明）允许锚定也允许不锚定；携带锚点时即产生锚定单元并受同一检视义务约束，MUST NOT 静默忽略或静默丢失。
类或接口的标记 MUST NOT 替代成员标记。
注释 token 以 /** 开始即 MUST 检查 JSDoc 语法宿主，即使解析器未将它挂到声明上，也 MUST NOT 当成合法普通块注释放行；同一宿主 MUST NOT 连续放置多块。
标签 MUST 明确分为三级：类及替代者级（类、接口、枚举、type 别名）、方法级（方法、构造器、访问器与顶层函数）和行级（普通单行注释）。字段、枚举成员、参数、局部变量、语句、表达式与对象字面量成员上的 JSDoc MUST 判违规。`@anchor` 与 `@format-exempt` 只能出现在类及替代者级或方法级，`@fallback` 与 `@passthrough-exempt` 只能出现在方法级，`@non-environment-variable` 与 `@non-manual-path` 只能出现在行级普通注释，错位 MUST 报错而非静默忽略。
JSDoc 中的每个标签 MUST 独占物理行，标签区 MUST 集中位于其他常规文本之后；标签行前 MUST NOT 出现同一行其他内容，标签开始后 MUST NOT 再出现正文或其他注释。单行 JSDoc 只能是完整的单一标签或不含标签的常规文本。
判定对象是当前源码中的 JSDoc，不是普通注释的编辑历史；MUST NOT 为猜测 /** 改成 /* 的行为增加转换追踪或防御。

#### Scenario: 声明与注释检查

- **WHEN** 必需声明未锚定，出现连续、无归属或错位 JSDoc，或标签出现在不允许的声明级别
- **THEN** 检查失败；字符串、模板和普通注释中的 JSDoc 字面文字不算声明；可选锚定声明缺锚不失败

<!-- @anchor id=code-coverage -->
<!-- @architecture id=tools.anchoring -->
<!-- @budget id=code-coverage level=L -->

### Requirement: 双向覆盖

代码锚点 MUST 指向存在的 Scenario。
Scenario 的代码锚定义务 MUST 由架构模型派生：其锚定的架构元素含源码承载（声明 sourcePath）时，该 Scenario MUST 有代码锚定；锚定目标均为纯文件登记元素时，该 Scenario 无代码锚定义务。
声明 @nonCode 的 Scenario 无代码锚定义务，代码锚定非代码 Scenario MUST 拒绝。
声明 @todo 的 Scenario 在摘除标签前无代码锚定义务，代码锚定待实现 Scenario MUST 拒绝；其锚定的架构元素无一携带 #todo 时 MUST 判违规，防止实现完成后标签残留。
义务判定 MUST NOT 按规范文件、目录或条目类型特判。
一个声明可以关联多个 Scenario，但 MUST NOT 重复同一 ID。

必要性论证：TypeScript、Markdown 与 LikeC4 的官方解析器提供声明和模型结构，不验证项目 Scenario 与代码锚点的双向义务；自研仅按模型中的源码承载关系核对存在性、唯一性及非代码约束，不另建路径分类或解析器。行为验证 MUST 覆盖悬空、重复、缺少代码锚定和非代码 Scenario 被代码锚定的拒绝，以及合法关联的通过。

取舍：只使用 TypeScript 无法核对 Scenario 归属；逐成员重抄到 C4 会形成第二份源码契约。采用官方 AST 定位声明、以既有锚点关联规范和正文，额外维护仅限关联标记；类型和调用合法性仍由编译器承担。这一边界满足需求追溯，保持架构的模块粒度，符合工程常识。

#### Scenario: 对照锚定

- **WHEN** ID 悬空、重复，或锚定含源码承载架构元素的 Scenario 未被代码锚定
- **THEN** 检查失败，并定位具体声明或 Scenario

<!-- @anchor id=anchor-coverage -->
<!-- @architecture id=tools.anchoring -->
<!-- @budget id=anchor-coverage level=XL -->

### Requirement: 修改触发检视

门禁 MUST 读取当前提交快照和匹配 index 的本地完整证明，并比较当前终态 contentId。声明、Scenario、所属 Requirement 正文和直接关联架构目标组成审计主体，主体收集限定直接关联；测试用途容器的代码单元参与收集与复杂度计量，但由标识与展示层统一排除，不产生检视义务。identifier 只由锚点与终态关联文本生成，规范侧关联文本按合成信息源规则加工，记录、时钟、提交号和分支不进入正文内容指纹；覆盖摘要只用于合并事件。

代码、规范和正式审计 SQLite 同属 Git tree。缺记录或 contentId、coverageId 失配时需要检视，匹配的记录直接继承。删除终态锚点后退出当前数据库，重命名按新锚点计算。失效记录保留原文、意见和来源于扁平一代 previous_records；合并覆盖变化按最终 index 复算并明确 pending。

锚定单元使用稳定名称路径：Requirement 与 Scenario 标题在文档内唯一，同文件同名声明拒绝。普通内容 identifier 与行号及集合遍历顺序无关；插入邻近正文不会波及无关锚点。合并覆盖摘要只包含主体结构身份，位置用于展示。

架构主体使用 LikeC4 官方 AST/CST 截取完整源码片段；文档主体使用 Markdown AST 定位完整片段。职责、描述、文件登记、元数据或关联变更使引用该片段的锚点重新检视，悬空架构 ID 直接暴露。模型视图排版不属于元素语义，检视记录不改变内容指纹。

必要性论证：Git 负责受检树读取，官方 AST 提供主体区间；项目只根据架构、规范与代码的直接关联收集正文并调用同一新鲜度判定。行为验证覆盖关联变化失配、无关变化继承、index 与 HEAD 分离，以及强制提交后的可验证债务。

#### Scenario: 关联内容变更

- **WHEN** 声明、Scenario、Requirement 或直接架构主体改变
- **THEN** 受检 tree 的记录 identifier 与当前正文匹配；失配锚点需要检视，其他锚点自然继承

<!-- @anchor id=review-propagation -->
<!-- @architecture id=tools.anchoring -->
<!-- @budget id=review-propagation level=L -->

### Requirement: 复杂度预算

每个锚定代码的 Scenario MUST 声明正式区复杂度预算等级，等级枚举 MUST 且只能是 S、M、L、XL；正式区计量生产代码并独立门禁。架构模型登记为 `test` 用途的源码容器只计量与登记实测值，MUST NOT 设预算等级、MUST NOT 进门禁：测试是棘轮本身，由规范单向决定，预算压力会反向挤删测试并架空棘轮；测试质量由测试纪律与检视保证，不由复杂度预算保证。
Scenario 预算标签 MUST 是单行 XML 注释 `<!-- @budget id=锚点ID level=等级 -->`，位于自身锚点块内 @anchor 之后，id MUST 与 @anchor 一致，每个 Scenario MUST 恰好一个；测试区预算形态（scope=test）MUST NOT 出现。
Requirement 可声明覆盖其全部 Scenario 的正式区总预算，形态 MUST 是单行 XML 注释 `<!-- @budget level=等级 -->`，位于 Requirement 层级的独立块，MUST NOT 携带 id，至多一个，等级 MUST 不低于其下任一 Scenario 的预算等级。

等级阈值的唯一来源 MUST 是 `tools/model.c4` 自举根系统的 metadata。每个 S、M、L、XL 等级 MUST 分别声明 `Declarations`、`Cyclomatic`、`Nesting`、`Dependencies`、`SideEffects` 五个正整数上限，字段名形态为 `budget等级维度`；每个维度的四级阈值 MUST 严格递增，阈值只约束正式区。MUST NOT 声明总分、权重或第二套阈值。
复杂度 MUST 按五个维度独立计量：声明数为区内唯一代码声明单元数；圈复杂度为任一函数声明的最大 McCabe 值；嵌套深度为任一函数声明的最大控制流嵌套层数；依赖数为区内源码文件经 dependency-cruiser 解析所得的唯一内部直接依赖边数；副作用数为区内源码文件静态导入的 Node 运行时系统能力类别数，纯类型导入不计。filesystem 包含 fs 及其 promises 子模块，network 包含 dgram、dns、http、http2、https、net、tls 及其子模块，process 包含 child_process 与 process，concurrency 包含 cluster 与 worker_threads；带 `node:` 前缀与不带前缀的标准模块写法归入同一类别。正式区任一维度超出声明等级上限 MUST 失败，MUST NOT 折算或相互抵消；测试区按同一五维计量实测值，只登记与报告。
圈复杂度与嵌套深度 MUST 分别经 ESLint 官方 complexity 与 max-depth 规则计算，计量层 MUST 通过规则的阈值判定进行指数定位和二分求得精确值，只读取结构化的 `ruleId` 与 `messageId`，MUST NOT 解析诊断文案、调用 ESLint 废弃或不稳定的内置规则入口或复刻规则算法。门禁判定 MUST 先以声明等级阈值做阈值判定，精确值仅在报告、登记与超限来源定位时求取；规则仍报告超限但 `messageId` 与计量层契约不符 MUST 显式失败，MUST NOT 静默按未超限计量。无执行体的类型容器与函数签名的圈复杂度和嵌套深度 MUST 为 0；类字段初始化器与 static block MUST 归属于类型声明单元，成员方法 MUST 只归属于自身声明单元；嵌套函数 MUST 在所属代码声明单元内独立计量，该单元取所有执行体的最大圈复杂度和最大嵌套深度，MUST NOT 将内层控制流叠加到外层执行体。
语法结构与类型声明归属 MUST 经 TypeScript 编译器 API 与 TypeScript ESLint parser 识别，依赖关系 MUST 经 dependency-cruiser 计量；MUST NOT 使用文本模式推断语法、依赖或复杂度。任何循环依赖 MUST 独立导致门禁失败，MUST NOT 通过预算等级放行。
Scenario MUST 收集自身全部代码锚点，以代码声明键去重后独立计量正式区与测试区。Requirement MUST 对其全部 Scenario 的声明单元与文件集合重新按唯一键聚合，MUST NOT 累加 Scenario 结果造成共享代码或依赖边重复计量。
代码承载义务 MUST 与“双向覆盖”使用同一架构来源：仅关联纯文件登记元素的 Scenario 可以没有代码单元，其两区按实际关联集合计量；关联源码承载元素却没有代码单元时 MUST 失败。必要性：规范结构并不意味着代码实现，统一模型判定避免复杂度检查凭空增加实现义务，同时保留实际缺失和超限诊断。
CLI MUST 提供复杂度报告命令，逐 Scenario 输出正式区声明等级、两区五维实测值、最大圈复杂度与最大嵌套的声明键、计入的内部依赖边、副作用类别及其源码文件、内聚度与测试锚定覆盖；超限消息 MUST 同时给出对应来源。内聚度与覆盖率只报告，MUST NOT 混入复杂度预算。
审计发布 MUST 把每个锚点的两区五维实测值随同一受检 index 写入正式审计 SQLite（表结构与生命周期见 SQLite 同树传输条款）；实测值是锚点级派生数据，不进入检视记录与审计文本，正文与覆盖双一致的继承沿用原值，合并中间态允许缺省，发布与正式终态校验 MUST 按受检输入重算并比对。
复杂度超限 MUST 触发设计审查，MUST NOT 以通过门禁为目的直接提高预算。审查 MUST 逐项依据报告来源确认功能必要性、职责内聚、重复实现、依赖方向与副作用边界；无必要逻辑 MUST 删除，可独立职责 MUST 沿架构边界拆分，重复实现 MUST 收敛为唯一实现。
只有当超限复杂度由已确认的功能约束内生、继续拆分会破坏职责内聚或增加跨边界依赖，且拟用等级是满足约束的最小等级时，方可提高预算。预算调整 MUST 同步修订需求规范、架构归属与代码锚点，使约束、报告来源与实现能够双向追溯；MUST NOT 以当前实现规模、历史预算或门禁通过作为提高预算的依据。

<!-- @budget level=XL -->

#### Scenario: 复杂度预算检查

- **WHEN** Scenario 缺少预算标签、标签形态或归属不符（含出现测试区预算形态）、阈值声明缺失或不递增、正式区实测复杂度超过声明预算，或提出提高预算等级
- **THEN** 结构、阈值与正式区超限检查失败，报告形态逐 Scenario 列出等级、两区实测值与来源且不拦截；提高预算前必须完成必要性与边界审查，可删除、可收敛或可拆分时不得提高预算

<!-- @anchor id=complexity-budget -->
<!-- @architecture id=tools.cli -->
<!-- @architecture id=tools.architecture -->
<!-- @architecture id=tools.anchoring -->
<!-- @architecture id=tools.tests -->
<!-- @budget id=complexity-budget level=XL -->

### Requirement: Java 风格子集

Pi 官方扩展加载器要求默认导出的 ExtensionFactory 函数。该原生边界使用显式 ExtensionFactory 类型的具名常量 piExtension，并默认导出同一常量；行为由 PiExtension 类承担。ESLint 仅允许该类型绑定的声明及对应默认导出，其他顶层函数和未注解常量仍受约束。

顶层 MUST 只含 import、class、interface、enum、type 别名及这些声明的具名直接导出，以及本节明确登记的原生入口；除 Pi 官方 ExtensionFactory 边界外 MUST NOT 使用 export default，包括默认导出的 class。
type 别名 MUST 只声明同类型基本字面量联合（全部为字符串、数值或布尔字面量，承载枚举域具名）；MUST NOT 承载对象结构、函数类型、泛型应用或其他类型运算。类型注解 MUST NOT 内联字面量联合，必须引用具名 type 别名。MUST NOT 含裸变量、裸表达式或独立函数。
对象结构 MUST 经具名 interface 表达；类型注解（含泛型参数与接口成员）MUST NOT 内联匿名对象结构。
对象结构 MUST 落到具名 class 或具名 interface：对象 schema MUST 以具名契约的 `z.ZodType<接口>` 注解声明，或使 parse 结果直接由具名类型承接；MUST NOT 以匿名推断类型持有或传递对象结构。生产代码的局部变量、参数、字段与解构 MUST 显式注解，MUST NOT 通过删除注解绕过可空与联合禁令。
类 MUST 以私有构造器或私有实例成员维持名义屏障：同构类间 MUST NOT 结构兼容；类与接口间的结构兼容（implements 或注解）允许。
变量 MUST 使用 let 或 const，MUST NOT 使用 var。
语法允许之处 MUST 明写类型注解与成员访问权限。
规则 MUST 复用 ESLint/typescript-eslint，配置经 API 构造；除本节明确允许的标准配置与测试声明差异外，MUST NOT 设置目录或配置文件豁免。必要的外部机器文本边界只允许使用带理由的 `eslint-disable-next-line no-restricted-properties`，并由 eslint-comments 插件拒绝其他行内指令。

类型与局部控制流 MUST 使用 typescript-eslint `strictTypeChecked` 和 TypeScript 严格检查。
可变状态 MUST 由所属对象通过 private 字段持有，公开或 protected 字段 MUST 为 readonly；不再赋值的 private 字段 MUST 使用 readonly。
调用方 MUST 通过所属对象的方法请求状态变更；数据查询 MUST 返回只读接口，MUST NOT 暴露可写集合。
方法的数据输入 MUST 使用只读契约，数组、Map、Set 输入 MUST 分别使用 readonly 数组、ReadonlyMap、ReadonlySet；新结果 MUST 显式返回。
MUST NOT 重新赋值参数或直接写入参数属性。函数内部新建的局部集合可修改，构造结果不得借修改调用方输入完成。
字段访问由 TypeScript 和 ESLint 声明规则检查，参数写入由 `no-param-reassign` 检查，私有字段只读性由 `prefer-readonly` 检查。
异步操作 MUST 显式等待、返回 Promise 或处理失败；MUST NOT 将 Promise 当作同步布尔值或同步回调返回值使用。
标准配置与测试声明 MUST 采用框架原生写法，但所有生产与测试代码的变量、参数、成员和返回值仍 MUST 保留显式类型注解；不得通过关闭 `typedef`、删除注解或使用裸 `undefined` 绕过可空语义。仅框架要求的顶层测试注册形态可按 ESLint 官方配置保留明确豁免，豁免不得扩大到类型覆盖。结构规则 MUST 仅检查明确禁止的语法，MUST NOT 根据回退层数或 catch 中出现 return 推断业务错误。
空 catch MUST 在 catch 体内用注释说明忽略错误的原因；体外注释不得替代说明。注释内容的合理性必须检视；catch 中的恢复值仍受 fallback 理由要求约束。
空 catch、if、循环与 switch 由 ESLint `no-empty` 检查，空函数体采用该规则的原生允许行为；纯重抛 catch 由 `no-useless-catch` 检查，错误重包装由 `preserve-caught-error` 的 requireCatchParameter=true 检查，包装后的 cause MUST 指向原捕获值，无参数 catch、遮蔽与解构丢失不得冒充保留原错。生产源码中的 Reflect 全局对象由 `no-restricted-globals` 检查，覆盖静态计算属性、方法引用与解构；字符串格式豁免 MUST NOT 放行 Reflect。
MUST 拒绝不必要的条件与可选访问、不安全断言、浮动 Promise、错误的 Promise 使用、不可达语句、不穷尽的联合类型 switch、隐式比较、精度丢失、错误的数组回调与数值排序。
类型谓词 `is` MUST 拒绝，类型判断使用 instanceof 或结构化解析；非空断言 `!` MUST 拒绝。
unicorn 插件的 no-unnecessary-string-trim、prefer-string-slice、prefer-code-point、no-hex-escape、escape-case、throw-new-error、no-instanceof-array MUST 保持启用；`no-useless-fallback-in-spread` 拒绝对象展开中的空对象兜底，数组展开与有字段的替代对象遵循该规则的原生边界。
ast-grep 不提供类型或数据流分析，MUST NOT 将结构匹配宣称为跨过程数据流证明。

成熟规则组合 MUST 逐项配置 ESLint、typescript-eslint、Unicorn 与 SonarJS，MUST NOT 整体启用 SonarJS preset。非空断言、空 catch、Reflect、trim 与大小写转换各保留一个规则入口；生产语义规则只覆盖模型用途为 `tool/common` 的源码容器，标准规则仍覆盖现役源码与测试。根配置复用架构范围和 tsx 官方加载器，继续由 ESLint CLI 使用原生缓存。

SonarJS 固定为根开发依赖 `eslint-plugin-sonarjs@4.2.2`，只使用公开插件接口。随包许可证为 Sonar Source-Available License v1.0（SSAL），其第 1(c) 与第 2 节限制外部 AI 读取、分析或参与程序，开发依赖身份不提供例外，不得宣称已获额外授权；MUST 启用 `no-ignored-exceptions`、`no-dead-store`、`no-duplicated-branches`、`no-all-duplicated-branches` 与 `no-identical-functions`，直接保留原生诊断。

必要性论证：成熟依赖负责通用语法、类型和局部控制流；自研结构规则仅保留具名对象/schema、Option 联合、名义屏障、KV 契约与方法级纯转发等项目约束。行为验证覆盖替换规则的正反例、声明豁免、范围隔离与原生诊断保留。

未使用文件与导出 MUST 由 Knip 检查；局部变量、参数和 private 成员 MUST 由 TypeScript 的 `noUnusedLocals`、`noUnusedParameters` 检查，ESLint 不重复执行 unused-vars。空壳完整签名按 TypeScript 原生规则用 `_` 前缀声明尚未消费的参数；执行保持明确 TODO 失败。必要性与取舍：编译器保留类型感知的未使用诊断，同时允许粗略声明在业务实现前建立，不需为消除参数诊断提前执行业务或制造同名重载。
Knip 入口 MUST 由根标准配置及官方插件识别，MUST NOT 将全部源码当入口规避可达性检查。
Knip 官方从根 scripts 识别的构建入口 MUST 直接复用，不再重复登记于显式 entry。其余用户声明入口 MUST 通过所属架构元素的 `entries` 登记，并与根 Knip 配置的显式入口一致；登记入口 MUST 属于受检 TypeScript 文件。测试入口仍由专用测试区域声明；project MUST 覆盖全部现役源码与测试，不能借入口去重缩小检查范围。
依赖方向和循环 MUST 由同一模型派生的 dependency-cruiser 规则约束。
Knip 不提供完整 public 方法可达性与跨过程污点证明，MUST NOT 将其文件或导出检查描述为该能力。

依赖版本 MUST 经 corepack 按根 packageManager 指定的 pnpm 执行 install --lockfile-only --no-frozen-lockfile 更新唯一根锁；安装冻结锁文件，执行前依赖同步复用 pnpm 原生能力。

#### Scenario: 风格检查

- **WHEN** 治理实现、配置或测试违反类型与风格约束，或 type 别名脱离字面量联合形态、注解内联字面量联合
- **THEN** 同一规则集拒绝；类内 lambda、interface、enum 与同类型字面量联合的具名 type 合法，不得将 Java 风格歪曲为只允许 class

<!-- @anchor id=java-style -->
<!-- @architecture id=tools.coding -->
<!-- @budget id=java-style level=XL -->

### Requirement: ESLint 配置装配

配置由 ESLint 和 typescript-eslint 官方 API 组合，覆盖现役源码及测试；生产语义规则范围从架构模型派生。配置仅声明规则和范围，AST 判定归结构规则。

#### Scenario: ESLint 规则与范围装配

- **WHEN** 创建现役源码与测试的 ESLint 配置
- **THEN** 复用官方规则并按架构范围生成配置，不承担 AST 业务判定

<!-- @anchor id=java-style-config -->
<!-- @architecture id=tools.coding -->
<!-- @budget id=java-style-config level=L -->

### Requirement: 项目结构规则执行

结构规则使用 TypeScript 与 ast-grep 的结构化语法树，执行具名对象/schema、联合类型和格式豁免等项目约束。成熟标准规则继续由 ESLint 执行；结构规则返回位置和诊断，不修改源码。

#### Scenario: 结构规则 AST 校验

- **WHEN** 检查项目结构限制、格式豁免和纯转发
- **THEN** 通过结构化 AST 定位违规并保留明确位置

<!-- @anchor id=structural-rules -->
<!-- @architecture id=tools.coding -->
<!-- @budget id=structural-rules level=L -->

#### Scenario: 成熟规则组合与项目边界

- **WHEN** 检查迁移的语法、错误处理、对象展开与 SonarJS 坏味道
- **THEN** 成熟规则按模型范围报告，带理由的逐行 ESLint 豁免仅放行格式成员，重复职责只保留一个入口

<!-- @anchor id=mature-rule-composition -->
<!-- @architecture id=tools.coding -->
<!-- @budget id=mature-rule-composition level=M -->

#### Scenario: 成熟规则替换与扩展检出

- **WHEN** 代码出现迁移的语法坏味道
- **THEN** 官方规则按直接调用、引用、解构、错误 cause 和展开边界的原生行为报告

<!-- @anchor id=mature-rule-replacements -->
<!-- @architecture id=tools.coding -->
<!-- @budget id=mature-rule-replacements level=S -->

#### Scenario: SonarJS 精选规则边界

- **WHEN** 代码出现未处理异常、死存储、重复分支或重复函数
- **THEN** 精选规则遵循同文件原生边界，空 catch 与嵌套条件的各条原生诊断直接保留

<!-- @anchor id=mature-sonar-rules -->
<!-- @architecture id=tools.coding -->
<!-- @budget id=mature-sonar-rules level=S -->

### Requirement: 自举门禁

现役代码由架构模型的源码容器声明，包括 tools 与新版产品；旧产品源码及测试均为参考文档，不参与依赖安装、编译或测试。pnpm check 在一次进程中加载架构模型，先启动根 scripts 中的 Prettier、tsc、ESLint 和 Knip，再进入治理检查的同步前缀，二者重叠执行。这些纯检查与确定性构建直接执行对应根 script。Prettier、tsc、ESLint 使用各自原生增量目录 `_build/prettier`、`_build/eslint`、`_build/tsc`。不引入任务图缓存，不自研指纹或缓存判定。任一命令失败则检查失败。pnpm check 覆盖全部现役代码的格式、类型、风格、死代码、架构文件归属、源码依赖、文本规范、锚定与复杂度，pnpm test 按唯一 Vitest 配置执行全部现役测试且不收集覆盖率。pnpm check:ci 与 CI tools 作业执行同一序列：并行 check、check:schemas、check:submodules，然后 pnpm test:metrics；CI 传入 --base，本地默认 HEAD。独立审计 job 校验 HEAD 源码及同树 SQLite。提交准备仅读取当前 index、直接父状态和本地确认，rebase 收尾增加单份输入。pnpm check:commit 先确认受检源码已暂存，再执行上述通用检查；通过后对同一受检 index 收集一次正文，据此完成架构归属、锚定、复杂度实测登记、审计发布与 gitlink。失败保留非零退出码。工作树检查、gitlink 或架构归属失败时不修改 index。审计失配仍发布并只暂存 tools/review.sqlite。

必要性论证：格式、类型、风格、死代码和测试直接复用标准工具；自研仅依据项目架构模型区分现役源码与参考文档、收集领域诊断并核对暂存输入。检查组合只并发启动已经声明的只读 scripts，启动发生在治理同步前缀之前，不实现缓存判定、重试或任务图。行为验证 MUST 覆盖新版源码入选、参考文档不入选、未暂存治理输入拒绝及各检查失败退出码保留。治理检查同时执行决策记录与标准 change 门禁。悬空引用、已取代引用、已接受正文被修改，以及没有本次归档标准 change 的规范直改 MUST 失败。`pnpm check:types` MUST NOT 承担这些不一致。

<!-- @decision id=ADR-0002 -->

#### Scenario: 自举验收

- **WHEN** 任一检查或核心回归失败
- **THEN** 门禁失败；未暂存的治理改动或文件不得代替受检快照通过检查，检查期间输入变化同样拒绝

<!-- @anchor id=selfcheck-gate -->
<!-- @architecture id=tools.cli -->
<!-- @architecture id=tools.tests -->
<!-- @budget id=selfcheck-gate level=XL -->

### Requirement: 钩子转发

post-checkout、post-merge 经根 pnpm 的执行前依赖校验自动安装当前分支的冻结依赖，再维护子模块并只读报告 HEAD 数据库中的待审状态。正文与覆盖复算由 review verify、提交门禁与独立 CI 审计执行。已匹配的依赖 MUST 复用 pnpm 原生状态直接执行；安装失败 MUST 保留原生错误和非零退出码，MUST NOT 改写锁文件、源码或审计数据库。post hook 失败时 Git 的分支切换或合并可能已完成，MUST NOT 自行恢复 Git 状态。

项目检查与检视经钩子调用根 pnpm scripts；prepare 自动运行 review install，幂等安装相对 hooksPath、SQLite merge driver、merge rebase backend、子模块 on-demand push/fetch。pre-commit 和 pre-merge-commit 执行 check:commit；commit-msg 仅校验人工消息，标题非空，每行至多 72 字符，总计至多 15 行。标题内容由用户任务确定。pre-push 与 post-commit 保留 Git LFS 原生步骤。合并由用户手动执行并确认本地门禁与 Actions；服务端保护只在权限支持时配置。

必要性论证：Git 与 pnpm 提供钩子执行和退出码传播，Git stripspace 提供正文与注释处理；项目只核对非空与消息长度，不以固定语法反向限制用户指定的标题。原生字符串计量足以完成该职责，无需标题解析器或任务名称清单。钩子不承担分支事务、同步或恢复，LFS 直接使用 Git LFS；行为验证 MUST 覆盖任务标题、空消息、消息长度及正式 CLI 的失败退出码。

#### Scenario: 钩子执行

- **WHEN** 提交信息校验失败或 Git LFS 传输失败
- **THEN** 对应原生步骤返回非零并阻断操作；合并必须保留 Actions 成功与本地门禁通过的证据，服务端分支保护仅在仓库权限支持时配置

post-commit 只执行 Git LFS，与 review 状态分离。post-checkout、post-merge 先执行 workspace 检查和依赖建立，再执行 Git LFS；workspace 失败时不调用后续 LFS 步骤。真实 Git hook run 测试验证 post hook 的顺序和失败传播；LFS 原生失败按 Git 自身语义报告。

提交消息由 Git 原生 stripspace --strip-comments 处理注释；标题、行长及行数约束核对人工正文，冲突继续时生成的注释按 Git 配置处理。

<!-- @anchor id=hook-boundary -->
<!-- @architecture id=tools.hooks -->
<!-- @budget id=hook-boundary level=M -->

#### Scenario: 切换或合并后自动确立环境

- **WHEN** 生产 post-checkout、post-merge 或沙箱中的首次 pnpm 命令遇到缺失、失配的已安装依赖
- **THEN** pnpm 原生安装冻结依赖并执行 prepare，随后进入正式 CLI；重复执行复用匹配依赖，待审状态仅报告
- **AND** 清单与锁文件失配时保留原生安装错误并阻断 CLI，HEAD、受检 index、源码、锁文件与审计数据库保持调用前状态

- **AND** workspace 只读取并验证 HEAD 数据库记录，报告缺库或库内 pending，二者不改变退出码；正文失配由 review verify 保持非零拒绝。workspace 与依赖安装的 review install 不加载架构和 TypeScript 解析器
- **AND** 真实 hook 回归通过异步子进程执行，取消信号直接取 Vitest 测试上下文；时间上限统一取根 Vitest 配置，耗时按命令报告，不以单次墙钟耗时判定正确性

必要性论证：pnpm 的 verifyDepsBeforeRun 与 frozenLockfile 已提供安装状态判定、同步和冻结语义，Git hook run 已提供生产 hook 调用；项目不实现依赖探测或安装调度。Node execFile 与 Vitest signal 提供异步等待和取消，避免同步进程阻塞测试生命周期。回归以小型本地链接包形成真实失配，经生产 hooks、pnpm、prepare 和独立 CLI 验证首次建立、版本切换、重复执行及失败传播。

<!-- @anchor id=workspace-hook-runtime -->
<!-- @architecture id=tools.hooks -->
<!-- @budget id=workspace-hook-runtime level=S -->

### Requirement: TypeScript 格式检查

TypeScript 与配置的格式 MUST 由官方 Prettier 判定，唯一配置位于根 package.json。pnpm check:format 只检查，pnpm format 为显式格式化命令；检查不得自动修改源码。范围覆盖全部现役源码与根标准配置，不覆盖参考文档。

Agent 官方参考按架构中的文档元素保存原始字节，原样 Schema 经根 .prettierignore 排除格式改写，来源记录仍参加格式检查。Git 属性对该原文设置 -text，避免换行转换破坏上游 blob 与内容校验；文件登记仍覆盖全部参考。这一边界复用 Prettier 原生 ignore 和 Git attributes，仅保留可复核的外部文本，不增加产品或治理源码豁免。

#### Scenario: 未格式化源码进入门禁

- **WHEN** 现役源码不符合根 Prettier 配置
- **THEN** 检查拒绝；显式格式化只修改所选现役源码

<!-- @anchor id=typescript-formatting -->
<!-- @architecture id=tools.coding -->
<!-- @budget id=typescript-formatting level=M -->

### Requirement: 第三阶段测试标准选型

第三阶段变异测试使用 StrykerJS 的 `@stryker-mutator/core`、`@stryker-mutator/vitest-runner` 与 `@stryker-mutator/typescript-checker`；属性测试使用 `fast-check`；覆盖率评估使用 Vitest 原生 V8 provider `@vitest/coverage-v8`。根开发依赖只登记实际消费者使用的包。普通测试不默认收集覆盖率。直接版本由根 package.json 精确声明，传递依赖由唯一根 pnpm-lock.yaml 固定。Stryker 三个包使用匹配版本，coverage-v8 与 Vitest 使用相同版本。

选型依据与取舍：已有 Vitest 承担测试发现、隔离和断言，但自身不提供源码变异及属性输入生成与收缩；Stryker 提供结构化变异、Vitest runner、类型检查和原生报告，fast-check 提供生成、收缩与 seed/path 失败复现，coverage-v8 复用 Vitest 的覆盖率集成。复用这些受维护能力满足测试有效性与可复现输入的需要；手工样例继续提供独立预期，属性测试扩展输入组合，变异测试检验断言发现错误的能力。代价是增加开发依赖和验证耗时，执行范围与资源使用遵循同一架构和构建输出合同。MUST NOT 自研变异器、随机生成与收缩算法、覆盖率收集器或测试调度器。

套件配置只有 `vitest.config.mjs`。测试发现只有该文件的 `test.include` 与 `test.exclude`。同一文件的 `test.coverage` 声明 V8、`TestMetrics.measuredFiles`、报告目录 `_build/testing/coverage` 和 `text`/`json-summary`，不设置 `enabled: true`。`pnpm test` 先构建前端插件与预览，再执行 `vitest run`，不传 `--coverage`，因此沿用 Vitest 官方默认不收集覆盖率。`test:coverage` 使用同一构建和同一配置，只追加官方 `--coverage`。Stryker `mutate` 与 `test.coverage.include` 同为 `TestMetrics.measuredFiles`。这两份构建产物同时是变异沙箱的输入。`test:mutation` 执行 `stryker run`，Vitest 配置仍是 `vitest.config.mjs`，并由官方 runner 关闭 V8。`test:metrics` 依次执行 `test:coverage`、`test:mutation`、`check:metrics`，不先跑无覆盖率的 `pnpm test`。覆盖率命令与变异命令各一次，该套件因此完整执行两遍：一遍收集 V8，一遍作为 Stryker 初始测试供 incremental 计算覆盖差异；未改动变异体不重测。V8 不产生变异覆盖图，Stryker 初始测试不产生 Istanbul 覆盖率摘要，两遍都是这两项官方测量的必要输入。`check:ci` 与 CI tools 作业都先并行执行 check、check:schemas、check:submodules，再执行 `test:metrics`；CI 传入 `--base`，本地默认 HEAD。check 与 record 的历史基线由 `--base` 指定。record、指标比较、审计、提交、子模块与发布不缓存测量结果。Vitest 使用原生 `maxWorkers: '50%'` 并行，为正式入口与 Git 子进程保留 CPU，避免满核 fork 饱和超时；Stryker 使用官方默认并发管理 checker 与 runner。属性失败保留官方 seed/path，变异报告保留存活、未覆盖、超时与编译失败的真实分类。

#### Scenario: 标准测试依赖锁定

- **WHEN** 建立第三阶段测试依赖或更新其版本
- **THEN** 根包清单精确声明上述标准工具，唯一根锁文件固定解析，Stryker 包版本匹配且 coverage-v8 与 Vitest 同版；安装使用 pnpm 原生冻结流程，第三阶段执行直接复用这些工具的官方能力

<!-- @anchor id=phase-three-test-dependencies -->
<!-- @architecture id=repository.build -->
<!-- @budget id=phase-three-test-dependencies level=S -->

#### Scenario: 按需进行覆盖率评估

- **WHEN** 测试入口声明需要覆盖率评估或登记覆盖指标
- **THEN** 该入口启用 `@vitest/coverage-v8`，生成官方覆盖率结果供评估使用；未声明覆盖率评估的测试入口不收集覆盖率

<!-- @anchor id=phase-three-test-selection -->
<!-- @architecture id=repository.build -->
<!-- @budget id=phase-three-test-selection level=M -->

### Requirement: 模块测试指标棘轮

测量范围由同一 C4 模型与 WorkspaceFiles 派生，覆盖全部已实现生产 TS/TSX 模块，排除测试与声明文件。元素及其父级标记 TODO 时跳过测量；去掉 TODO 后首次完整实测通过显式 record 建立基线。覆盖率按 Istanbul 官方计数合并 lines、statements、functions、branches，变异分数由 mutation-testing-metrics 聚合官方 Stryker 报告。没有可计分变异的模块以 null 表达，不伪造分数。

tools/test-metrics.json 保存模块实测值。check 只读，逐项阻断相对基线下降和已实现模块缺基线；record 是唯一更新入口，接受新增模块，并在 C4 元素实际删除时移除其基线。既有元素的缺模块、缺源码报告、coverage 变为 null、已有 mutation 分数变为 null 或退回 TODO 均须失败。每份报告必须完整对应当前模块的生产源码；无可计分变异以源码条目存在且 mutants 为空表达，缺源码条目不是无变异。record 必须先完成全部当前范围的报告校验。首次实测建立逐模块下限，之后持平或提升；策略标签表达验证重点，不额外捏造数值档位。

check 与 record 接受显式 --base 提交，使用 Git 原生读取该提交的基线；本地默认 HEAD，PR 传入目标分支基准提交，main push 传入推送前提交。显式提交必须可解析。当前测量和工作树基线均与该历史基线比较，同时降低源码指标和同提交基线仍须失败。必要性：Vitest 和 Stryker 分别测量覆盖率与变异，均不承担 C4 模块归属与 Git 历史比较；项目只保留报告解析、官方聚合、模块比较和原子写入，以行为夹具验证计数合并、首次登记、只读检查、报告缺失、null 和跨提交下降失败。

Stryker 使用官方逐测试覆盖分析与默认并发；Node 使用原生默认堆配置，原生沙箱生命周期释放临时源码。变异运行使用完整现役源码与官方 Vitest runner；Vitest related 的静态导入筛选不覆盖子进程加载，因此关闭该筛选。沙箱经 buildCommand 调用根脚本 `build:mutation:sandbox`：Git init 建立独立仓库边界，pnpm offline frozen install 从共享 store 建立沙箱内 workspace 依赖链接；ignore-scripts 避免在变异沙箱安装开发 Git hooks。依赖安装不另建 store，也不把裸包引用链接回原工作区源码。沙箱复用同次构建的前端与预览产物，其余构建目录不复制。Stryker incremental 开启。incremental 文件由 `git rev-parse --path-format=absolute --git-common-dir` 定位到同一仓库的 Git common dir；官方报告键使用工程相对路径，因此同一仓库的 worktree 复用未改动变异与测试的结果。同一 common dir 同时只跑一路变异测量，Stryker 在结束时整文件写回。该文件只跟踪变异文件与测试文件，不覆盖依赖、配置、环境及未纳入 mutate 或测试文件的变化；这些变化用官方 `--force` 重建。默认 test:mutation 不传 `--force`。CI 用 actions/cache 保存和恢复该文件，不另建 artifact 或下载流程。

线程导入必须实际可用。eslint-plugin-unicorn 经 super-regex 引入的 make-asynchronous 旧版依赖 web-worker，其 workerData 假设与 Vitest threads 冲突。根 pnpm overrides 仅将该依赖边固定到使用 Node 原生 worker_threads 的 make-asynchronous 2.1.0。根 CI 的 Node 24 满足该依赖的 Node >=22.15。

覆盖率命令与变异命令各执行一次，套件正文在这两条命令中各完整执行一遍，与第三阶段测试标准选型的执行合同相同。SQLite 审计状态及棘轮基线不参与测量输入，后者由每次真实执行的 check:metrics 比较。覆盖率报告按完整工程相对文件名后缀唯一匹配 C4 源码，歧义或缺文件直接失败。变异报告缺源码条目直接失败。测量失败保留原始退出码。

#### Scenario: 变异增量文件随 worktree 共用

- **WHEN** 同一仓库的主检出与链接 worktree 解析 Stryker incremental 文件
- **THEN** 两者得到 Git common dir 下的同一路径

<!-- @anchor id=mutation-incremental -->
<!-- @architecture id=repository.build -->
<!-- @architecture id=tools.tests -->
<!-- @budget id=mutation-incremental level=M -->

#### Scenario: 比较模块实测值

- **WHEN** 完成官方覆盖率与变异测量并执行 check
- **THEN** 任一模块任一覆盖率维度或 mutation score 下降即失败，持平与提升通过，基线字节保持不变

<!-- @anchor id=phase-three-metrics -->
<!-- @architecture id=tools.cli -->
<!-- @architecture id=tools.tests -->
<!-- @budget id=phase-three-metrics level=XL -->

#### Scenario: 显式登记

- **WHEN** 执行 check:metrics record
- **THEN** 新模块可建立首次基线，既有模块低于指定历史提交（本地默认 HEAD）时失败，成功后原子写入当前实测值

<!-- @anchor id=phase-three-record -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=phase-three-record level=L -->

### Requirement: 测试策略与反模式门禁

测试策略声明表达验证重点：`high` 验证自研逻辑的独立预期行为，`standard` 验证公开行为与错误路径，`boundary` 验证外部协议映射与边界错误。测试门禁 MUST 使用 TypeScript AST 或成熟 ESLint 规则拒绝以源码字符、配置形状、固定实现字面量、mock 调用次数或自证式断言冒充行为证据；合法的格式化文本、解析器输入和协议样例按所属模块合同保留。

结构化门禁检测从 Node 文件读取传播到子串检查的源码断言，以及实际值与预期值采用同一 AST 表达式的断言。完整文件字节相等可验证 IO 保真，解析后结构可验证格式合同；这两类断言不属于源码关键词检查。mock 可用于错误注入和输出捕获，调用次数只验证显式计算次数或生命周期约束，业务正确性仍须由可观察结果与独立预期验证。

#### Scenario: 测试策略缺失

- **WHEN** 解析现役架构元素的测试策略
- **THEN** 已声明的策略只接受 high、standard 或 boundary

<!-- @anchor id=test-strategy-contract -->
<!-- @architecture id=tools.architecture -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=test-strategy-contract level=L -->

#### Scenario: 测试反模式诊断

- **WHEN** 测试源码包含结构化门禁定义的反模式
- **THEN** 根检查以 error 级诊断定位 AST 节点并失败，诊断不依赖源码字符搜索或测试名称

<!-- @anchor id=test-antipattern-gate -->
<!-- @architecture id=tools.coding -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=test-antipattern-gate level=XL -->

### Requirement: 测试文件系统隔离

tools 测试 MUST 使用 Vitest 原生发现、进程隔离和报告，断言使用 Vitest 或 Node 标准库。文件系统行为使用 Node 原生独占临时目录，测试结束释放自有资源，不改写原始夹具。MUST NOT 自建文件系统接口、环境探测、测试调度器或阶段启停清单。没有测试 MUST 失败；纯规则测试不访问远端服务。

#### Scenario: 临时目录生命周期

- **WHEN** 测试创建、写入与释放其临时目录
- **THEN** 写入仅属于该测试的文件系统，释放后无本次残留且其他实例不受影响

<!-- @anchor id=isolated-filesystem -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=isolated-filesystem level=M -->

### Requirement: 执行输入隔离

普通提交检查在检查前后核对治理工作树与暂存内容一致，并比对排除 tools/review.sqlite 的源码 index 指纹；指纹不一致则拒绝。tools/review.sqlite 由本次检查发布，不计入人工暂存义务。Git 提供替代 index 时，以该 index 定义本次提交，保留其它用户暂存及工作树内容。源码由开发者暂存。普通代码检查读取工作树，正式审计检查读取受检 tree 与数据库。同一受检 tree 的锚定与审计正文只收集一次。CI 代码、schema、子模块及测试与审计各自独立运行；Vitest 和独占临时目录承担并发隔离。

#### Scenario: 工作树在执行期间改变

- **WHEN** 输入文件被新增、删除或改写
- **THEN** 提交检查前后核对工作树与暂存内容，源码 index 指纹变化则拒绝；普通检查不自动暂存，数据库只在工作树检查、gitlink 与架构归属通过后发布，CI 使用独占检出

<!-- @anchor id=execution-snapshot -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=execution-snapshot level=M -->

### Requirement: 文本收敛

受检文件 MUST 为 UTF-8 无 BOM 且 MUST NOT 含尾随空白；JSON 行尾遵循官方格式化器。
行尾 MUST 由 Git 属性承担，MUST NOT 在检查中重复实现。
判定范围 MUST 覆盖现役源码元素登记的文本；文档元素与声明 `textExempt` 元数据的元素 MUST NOT 进入范围，豁免对象与理由 MUST 只登记于架构模型。
判定对象 MUST 为当前工作树普通文件；符号链接、gitlink 与二进制内容 MUST NOT 判定。
检查 MUST 只读，MUST 纳入完整自举检查链。

#### Scenario: 文本层不合规

- **WHEN** 受检文件含 BOM、尾随空白
- **THEN** 门禁拒绝并定位文件与行号

<!-- @anchor id=text-convergence -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=text-convergence level=M -->

### Requirement: 可空收敛

空值 MUST 经 effect 官方依赖的 `Option` 容器表达：可空来源（可空 API 返回、查找、索引）MUST 经 `Option.fromNullishOr` 装箱，消费 MUST 经 `Option.match`、`Option.getOrElse` 或 `Option.isSome`/`Option.isNone` 守卫收窄。
类型注解 MUST NOT 出现 `undefined`；可空联合 `A | null` 与语义联合 MUST 拒绝（可空经 Option，枚举域用同类型字面量联合）；`as` 断言与可选形参 MUST 拒绝；形参注解对齐外部 API 签名时 MUST 在消费处立即收窄，MUST NOT 让可空穿透到其他标识符、字段或返回值。
布尔上下文 MUST 只接受布尔表达式；strict-boolean-expressions、no-non-null-assertion、no-unnecessary-condition、no-implicit-coercion MUST 保持启用。
字面量缺省兜底 MUST 拒绝；确有必要的缺省 MUST 在所属声明登记明确需求与理由。
fallback 坏味道 MUST 由成熟规则覆盖：类型已证明无效的空值合并与 guard、对象展开和集合构造器的无用空参数、空 catch 与丢失原始错误分别由 typescript-eslint、Unicorn、ESLint 与 SonarJS 诊断。业务默认值、Option 分支和异常恢复不由字符串或成员名称猜测；其语义由所属 API 和类型契约负责。
类型已证明无效的 `??`、`??=`、guard 与冗余可选访问由 typescript-eslint `no-unnecessary-condition` 检查，对象展开空兜底与集合构造器空参数由 Unicorn 检查，异常吞没与错误重抛由 ESLint/SonarJS 检查。成熟规则只报告可证明的坏味道；业务默认值由所属 API 和类型契约负责，不再使用自研 fallback 扫描器按成员名称或控制流猜测语义。复杂度直接采用 ESLint 原生计量，不叠加人为惩罚；禁止按目录、文件或库整体放行。

#### Scenario: 可空穿透进入受治源码

- **WHEN** 可空值绕过 Option 裸露到注解、形参或返回值，或以哨兵值、字面量缺省兜底二次传播
- **THEN** 检查拒绝并定位声明或调用点

<!-- @anchor id=nullable-convergence -->
<!-- @architecture id=tools.coding -->
<!-- @budget id=nullable-convergence level=S -->

#### Scenario: fallback 坏味道诊断

- **WHEN** 代码出现类型已证明无效的空值合并、对象展开或集合构造器空参数，或吞没异常
- **THEN** 由 typescript-eslint、Unicorn、ESLint 与 SonarJS 的成熟规则报告

<!-- @anchor id=fallback-diagnostics -->
<!-- @architecture id=tools.coding -->
<!-- @budget id=fallback-diagnostics level=S -->

### Requirement: 字符串格式化豁免

受治源码 MUST NOT 调用 trim、trimStart、trimEnd、toUpperCase、toLowerCase、toLocaleUpperCase、toLocaleLowerCase。
这些成员由 ESLint `no-restricted-properties` 定位，覆盖直接调用、静态计算属性、方法引用与解构；必要边界使用带理由的逐行 ESLint 豁免，正则继续复用 ast-grep。
空格是合法字符，MUST NOT 被特判为空白空值（对齐 C# 版 nullorempty 判定的取缔语义）；大小写不敏感比较 MUST 经显式规范化设计，MUST NOT 以临时大小写转换 hack 实现。
受治源码 MUST NOT 使用正则字面量或构造 RegExp 匹配器；语法与层级 MUST 经结构化解析（AST、官方解析器、schema）表达，MUST NOT 以正则或 indexOf/slice 位置特判拼凑。唯一例外是外部单行文本边界的锚定全串格式校验，且 MUST 经豁免登记表达；ast-grep 规则对叶节点的锚定全串等值匹配（如 `^private$`）属于结构化工具的指定用法，不在此禁令范围。
正则和项目结构规则继续使用 `@format-exempt` 声明；字符串格式成员的必要边界不再通过自研处理器过滤，直接使用带理由的逐行 ESLint 豁免。代码标签必须与规范登记双向匹配，未登记或无理由 MUST 拒绝。

必要性论证：eslint-comments 提供公开的指令白名单、理由和禁止规则校验；删除自研 processor 后，格式诊断直接由 ESLint 规则产生，项目只保留正则和结构规则所需的声明登记。

<!-- @formatExempt id=architecture-path-annotation -->
<!-- @formatExempt id=environment-text-patterns -->
<!-- @formatExempt id=toml-object-boundary -->
<!-- @formatExempt id=bescript-canvas-path-split -->
<!-- @formatExempt id=bescript-statement-category-split -->
<!-- @formatExempt id=pull-request-title -->

已登记豁免：`architecture-path-annotation`（架构模型固定路径声明）、`environment-text-patterns`（环境变量扫描规则文本）、`toml-object-boundary`（TOML 库 toJS() 返回值的 KV 类型边界，唯一收口于 ConfigurationEditor 的 TomlObject）、`bescript-canvas-path-split`（Canvas GUID 与 HTML 内联格式的独立纯转换分支）、`bescript-statement-category-split`（BEScript 空语句来源校验的独立语义分支）。

`pull-request-title` 仅用于 GitHub 外部单行标题的 Conventional Commits 锚定格式校验。项目没有提交语法解析需求，现有依赖没有该格式的解析入口；该单行字段无需引入正文解析器，正文层级不受此豁免覆盖。

#### Scenario: 裁剪与豁免登记

- **WHEN** 受治源码出现未豁免的裁剪、大小写转换或正则，或豁免标签与登记不匹配
- **THEN** 检查拒绝并定位声明或调用点

<!-- @anchor id=format-exemption -->
<!-- @architecture id=tools.coding -->
<!-- @budget id=format-exemption level=M -->

### Requirement: 运行时输入校验

进程外部数据 MUST 在进入内部类型前经 zod schema 校验，包括反序列化文本、HTTP 响应体、子进程输出、命令行参数、环境变量与标准输入。
`JSON.parse` 的结果 MUST 直接经 schema `parse` 消费，MUST NOT 先赋值、传递或返回再校验。
校验失败 MUST 抛错，错误自然暴露，MUST NOT 以默认值、部分字段或静默跳过兜底。
schema MUST 以具名契约的 `z.ZodType<接口>` 注解声明，parse 输出的对象结构 MUST 落到具名 class 或具名 interface。

#### Scenario: 未校验外部输入

- **WHEN** 外部数据未经 schema 校验进入内部类型，或 `JSON.parse` 结果脱离 schema `parse` 流动
- **THEN** 结构规则与类型感知规则拒绝并定位调用点；运行时校验失败立即抛错

<!-- @anchor id=runtime-validation -->
<!-- @architecture id=tools.coding -->
<!-- @budget id=runtime-validation level=M -->

### Requirement: 外部依赖防腐层

外部依赖实际消费面缺少精确类型覆盖时 MUST 在 shared 分区建立必要的类型边界。MUST 使用具名签名，MUST NOT 以 any 或未经校验的可空值泄漏到内部。无需包装的成熟依赖直接使用官方接口；没有消费者时删除包装与依赖，不保留空包。防腐层不依赖 tools 或产品实现。

#### Scenario: 绕过防腐层消费被接管包

- **WHEN** 治理系统源码直接 import 被接管包，或以字符串定位消费其资产而未经防腐层包装
- **THEN** 依赖检查拒绝并定位消费点；被接管包只出现在防腐层构建依赖中

<!-- @anchor id=dependency-acl -->
<!-- @architecture id=tools.architecture -->
<!-- @budget id=dependency-acl level=S -->

#### Scenario: 防腐层签名混沌

- **WHEN** 防腐层声明或包装以 any 直通未知形态，或出入参类型缺失
- **THEN** 防腐层验证构建拒绝；类型收窄必须落到精确签名

<!-- @anchor id=dependency-acl-signature -->
<!-- @nonCode -->

### Requirement: 零增值转发与单引用 helper 禁令

方法体仅含一条调用语句的纯透传形态 MUST 拒绝为纯转发壳：该语句是返回转发，或是唯一的表达式语句；调用可经 await 或括号包裹。转发接收者为 `this`、标识符或其属性链（MUST NOT 是调用结果链），实参为形参名的一一透传（MUST NOT 含组装、构造或字面量加工）。判定 MUST 排除正当转发形态：方法名命中同文件 interface 成员名（实现契约语义）或携带 `override` 修饰符。确需保留的转发 MUST 在方法级 JSDoc 使用 `@passthrough-exempt 理由`，理由不得为空；`@format-exempt` MUST NOT 豁免纯转发。

#### Scenario: 转发壳进入受治源码

- **WHEN** 方法以纯透传形态零加工转发调用，包括唯一的表达式语句
- **THEN** 结构规则拒绝并定位声明；实参加工、构造、调用结果链、实现契约、override 与方法级 `@passthrough-exempt` 不误报

<!-- @anchor id=forward-shell-usage -->
<!-- @architecture id=tools.coding -->
<!-- @budget id=forward-shell-usage level=XL -->

### Requirement: KV 容器类型边界

存储 KV 对的容器类型（Record、Map、ReadonlyMap、WeakMap、ReadonlyWeakMap 与索引签名）MUST NOT 以混沌类型充当领域契约：类型参数为 `any`、`unknown`、`object`，或类型参数本身又是 KV 容器（嵌套裸 KV 描述结构）MUST 拒绝。
键与值为原始类型、字面量联合或具名类型的字典使用 MUST NOT 被本规则限制；内联匿名对象结构由“Java 风格子集”的具名 interface 要求覆盖，MUST NOT 在本规则重复。外部输入收窄入口经 `@format-exempt` 登记。

#### Scenario: 混沌 KV 充当类型

- **WHEN** 类型注解、接口成员或泛型实参出现 KV 容器携带混沌参数或嵌套裸 KV
- **THEN** 结构规则拒绝并定位类型引用；原始值与具名值字典使用不误报

<!-- @anchor id=kv-type-contract -->
<!-- @architecture id=tools.coding -->
<!-- @budget id=kv-type-contract level=XL -->

### Requirement: 检视记录

审计原文 MUST 只包含终态文本，不拼入历史主体。类级审计文本保留类声明与成员签名，移除成员 JSDoc、方法、构造器、访问器实现体及字段初始化表达式，static block 只保留声明结构；方法级审计 MUST 包含方法全文及其 JSDoc。同文件的多个关联片段 MUST 按精确源码区间合并，重叠文本只保留一次；类框架与成员全文共用区间合并。不同文件或不同源码区间的相同字面文本 MUST NOT 合并。
规范侧审计文本 MUST 是压缩的合成信息源：标题与正文原样保留；@anchor、@architecture、@formatExempt 标签加工为裸 `@标签 名称` 形态进入文本；@budget、@nonCode、@todo 标签 MUST NOT 进入审计文本——预算等级改动不触发重新检视，非代码与待实现声明的增删必然伴随架构锚点或代码单元变化，由覆盖指纹捕获。标签识别 MUST 复用锚点块的结构化解析，MUST NOT 使用正则或文本扫描；加工后收敛空行。架构模型登记为 `test` 用途的源码容器中的代码单元不产生检视义务，MUST NOT 进入审计文本、正文指纹与覆盖摘要；过滤 MUST 发生在标识符与展示层，对存储记录的重算同样生效，使本规则的引入不要求既有记录重新检视。
展示、尺寸统计与标识符 MUST 使用同一审计文本。尺寸预算 MUST 使用 `gpt-tokenizer` 的 `cl100k_base` 计算正文 token 数；正文中的特殊令牌字面量按普通文本分词。通用锚点检查 MUST 报告全部终态锚点中最大的锚点 ID、token 数与编码名称；`review audit` MUST 报告所请求锚点的完整正文尺寸，分词范围与单锚点展示范围保持一致。后续 commit 检视记录检查与 `review record` 保持原有尺寸结果，由通用检查与 audit 分别负责各自范围的提示。超过 5000 tokens MUST 警告并提示拆分，但不阻断审计。超尺寸警告按 token 数降序列出最多 5 条，同尺寸按 ID 排序，更多 MUST 报告省略数量。

正式记录位于 tools/review.sqlite，包含 anchor、contentId、coverageId、非空意见、自动时刻、来源 HEAD/tree/index、规范化原文与主体位置；归属由承载树的 commit 表达。identifier 按 AuditIdentifier.content 契约，以 UTF-8 的 JSON.stringify([anchor, [[relativePath, text], ...]]) 计算 SHA-256，分组按文件和源码区间排序，text 为合并并按合成信息源规则加工后的审计正文；覆盖摘要对主体结构身份排序计算，二者都排除测试用途容器的代码单元。正文指纹与基线、分支、令牌及时钟无关。

必要性论证：Git 不知道项目声明、Scenario、Requirement 与架构元素的直接关联，也不定义类框架与成员全文的审计边界；自研仅组装这些终态正文并定义其确定性内容契约。源码与文档定位分别复用 TypeScript、Markdown AST，架构定位复用 LikeC4 AST/CST，摘要复用 Node crypto，外部记录校验复用 Zod；不得手写解析器、摘要算法或第二种记录格式。行为验证 MUST 覆盖直接关联变化、无关内容隔离、区间合并与确定性标识符，而非只核对配置或源码字符。

#### Scenario: 终态审计标识符复算与历史有效性

- **WHEN** 检查全部暂存终态锚点，或登记未提交内容的检视结果
- **THEN** identifier 只依赖锚点和终态直接关联文本；受检 tree 中记录匹配时继承，失配时报告当前锚点，无关变化保持有效。历史补审与当前暂存相同正文得到相同标识符。

<!-- @anchor id=review-freshness -->
<!-- @architecture id=tools.cli -->
<!-- @architecture id=tools.anchoring -->
<!-- @budget id=review-freshness level=L -->

#### Scenario: 终态关联文本合并与尺寸提示

- **WHEN** 生成或检查锚点的关联文本
- **THEN** MUST 对类只保留框架、对方法保留全文、对文档及架构保留官方 AST 定位的直接完整片段，同文件按精确区间合并且不丢失同一行片段；规范侧文本 MUST 按合成信息源规则加工，测试用途容器的代码单元不进入审计文本；通用检查与审计展示各固定报告一次最大尺寸，后续登记与 commit 记录检查不重复报告，对超过 5000 tokens 的锚点最多列出 5 条警告并报告其余数量

<!-- @anchor id=review-text -->
<!-- @architecture id=tools.anchoring -->
<!-- @budget id=review-text level=M -->

#### Scenario: Git 对象读取原语

- **WHEN** 读取受检 tree 的审计正文或 commit 的正式证明
- **THEN** 通过 Git 原生 ls-tree、cat-file 与 show 读取准确 tree/blob，SQLite 读取同树正式记录；原文保持完整字节，外部记录经 schema 校验，读取错误直接暴露。
- **AND** 只读取正式数据库时，ls-tree 或 ls-files 以该文件路径限定输入；Git 树读取与架构文件匹配分离，读取不加载架构或 TypeScript 解析器

<!-- @anchor id=review-notes-io -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-notes-io level=M -->

### Requirement: 标准配置与架构范围一致

标准工具配置的受检范围 MUST 由架构唯一来源约束。

只读外部参考的原文字节由其固定来源与哈希约束；Prettier 原生忽略配置 MAY 排除架构模型中 `document` 元素登记的文件，以免格式化改变原文。现役实现、测试和配置 MUST NOT 因忽略配置退出受检范围；范围检查 MUST 使用 Prettier 官方 `getFileInfo` 解析忽略语义，并逐项核对架构归属，非文档文件被忽略时失败并报告路径。Git 原生属性负责保留参考原文的行尾，不改变现役文件的检查义务。

必要性与取舍：Prettier 负责原生忽略语法，LikeC4 已持有文档归属，Git 属性负责字节传输；三者各自不校验忽略项是否越过项目架构边界。项目只核对原生忽略结果与既有文档归属，不维护第二份豁免路径表，不自研忽略解析器。行为回归验证文档原文可被排除、实现和测试被忽略均失败，未忽略时恢复受检。

#### Scenario: 标准配置与架构范围一致检查

- **WHEN** 标准工具配置与架构文件范围发生变化
- **THEN** TypeScript、ESLint、Prettier、Knip 与 Vitest 的实际输入 MUST 与模型登记的现役实现和测试一致，缺失、越界或入口参数不符 MUST 失败

<!-- @anchor id=configuration-model -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=configuration-model level=XL -->

### Requirement: JSON 配置与本地 Schema 可读性

受检 JSON MUST 通过原生 JSON.parse 读取。声明本地 $schema 时，MUST 按声明文件的位置解析显式相对引用，其他本地引用相对仓库根读取；格式错误或本地 Schema 无法读取时报告对应文件并失败。本检查负责输入可读性，标准工具范围归属由「标准配置与架构范围一致」约束。

必要性：JSON.parse 负责格式解析，文件系统负责可读性；项目只连接配置声明与其本地引用，不实现 JSON 或 Schema 解析器。

#### Scenario: 配置与本地引用读取

- **WHEN** 检查架构登记的 JSON 配置
- **THEN** 损坏 JSON 或无法读取的本地 Schema 失败，远端 Schema 引用不触发网络请求

<!-- @anchor id=configuration-json-input -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=configuration-json-input level=M -->

### Requirement: 官方 Schema 镜像完整性

官方 Schema 本地镜像 MUST 由唯一来源 URL 与固定 SHA-256 约束。根 check:schemas 入口读取官方源，核对固定哈希及本地原文字节；HTTP 失败、上游变动或本地镜像失配均报告并失败。

必要性：远端请求、字节读取与摘要分别复用原生 fetch、文件系统及 Node crypto；项目仅登记来源并比较结果，不实现 Schema 引擎，也不自动更新受检镜像。

#### Scenario: 官方来源与本地镜像核对

- **WHEN** 执行 check:schemas
- **THEN** 只有官方响应成功、固定哈希及本地镜像全部一致时通过

<!-- @anchor id=schema-mirror-integrity -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=schema-mirror-integrity level=S -->

### Requirement: 环境访问边界

环境变量检查 MUST 遵守单一入口的登记与定位规则。

#### Scenario: 环境访问边界检查

- **WHEN** 实现读取环境变量或出现疑似环境变量常量
- **THEN** 环境 API MUST 经 TypeScript AST 定位并核对集中登记，疑似项按单一入口的环境规则检查，诊断 MUST 精确定位；非环境变量标签 MUST NOT 豁免 API 访问

<!-- @anchor id=environment-boundary -->
<!-- @architecture id=tools.cli -->
<!-- @architecture id=tools.architecture -->
<!-- @budget id=environment-boundary level=XL -->

### Requirement: 复杂度执行与报告

复杂度门禁与报告 MUST 使用同一预算计量结果。

#### Scenario: 复杂度执行与报告检查

- **WHEN** 检查或报告 Scenario 与 Requirement 的复杂度
- **THEN** MUST 按复杂度预算规则分别聚合正式区与测试区，以唯一声明及文件集合计量五维；正式区超限 MUST 报告来源并失败，测试区只计量不判定；报告模式 MUST 展示两区精确值、内聚度与测试覆盖，审计发布 MUST 登记两区实测值

<!-- @anchor id=complexity-gate -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=complexity-gate level=XL -->

### Requirement: 依赖与副作用计量

dependency-cruiser 提供唯一内部直接依赖及循环边；TypeScript AST 提取运行时 Node 导入所属的 filesystem、network、process、concurrency 类别，纯类型导入不计。项目仅投影官方结果并去重，不另写依赖解析器。

#### Scenario: 文件依赖与系统能力

- **WHEN** 度量一组现役文件的直接依赖和副作用
- **THEN** 结果按来源文件保留唯一内部边、循环边和系统能力类别

<!-- @anchor id=complexity-dependencies -->
<!-- @architecture id=tools.architecture -->
<!-- @budget id=complexity-dependencies level=M -->

### Requirement: 代码复杂度计量

代码复杂度 MUST 遵守复杂度预算中的官方规则计量契约。

#### Scenario: 代码复杂度计量检查

- **WHEN** 计算代码执行体的圈复杂度与嵌套深度
- **THEN** MUST 使用 ESLint 官方规则的结构化阈值判定，类型容器与成员执行体按复杂度预算规则归属；精确值 MUST 经指数定位与二分计算，解析失败或规则消息契约漂移 MUST 失败

<!-- @anchor id=complexity-measure -->
<!-- @architecture id=tools.architecture -->
<!-- @budget id=complexity-measure level=L -->

### Requirement: 单锚点检视登记

`review audit --anchor <ID>` 展示单锚点受审 index 完整正文；`review audit --all` 和 `review audit --pending` 分别生成全部或待审锚点的集中审计包，三种选择互斥。集中包支持 `--json`，包含受审上下文、完整正文表、逐锚点正文引用、尺寸、独立 contentId/coverageId 和 10 位十六进制一次性令牌。仅同快照、同文件、同主体及精确区间的完整正文组可跨锚点复用；不同来源的相同文本保留独立。每锚点引用展开 MUST 与单锚点 `AuditText.groups` 完全一致。所有令牌 MUST 在一个事务成功落库后输出。

`review record --anchor <ID> --tag <token> --opinion <意见>` 和 `review record --stdin` 使用同一登记实现；stdin 只接受非空 JSON 数组，每项为独立 `{anchor, tag, opinion}`，同批 anchor 和 tag 各自唯一。一次准备受审输入，逐项核对 HEAD、受审 index、合并父上下文、anchor 和原文标识，在同一 SQLite 事务内消费全部令牌并写入私有 pending。任一校验或写入失败，整批令牌与确认保持原状态；成功后才报告已登记。有效且同意见记录复用原字节。完成确认后，reconcile 按实际正文和覆盖复算，允许 commit --only 采用匹配的单锚点确认。

必要性论证：Git 维护 tree、index、提交和父关系，官方 AST 收集锚点正文，Node SHA-256 复算标识；better-sqlite3 提供跨进程事务，write-file-atomic 原子发布单文件。项目职责仅为锚点关联、三方状态合并和有限终态继承。真实 Git 生命周期、SQLite 事务竞争、损坏记录及固定点测试验证行为。

#### Scenario: 单锚点登记完整记录

- **WHEN** 阅读完整单锚点正文后提交令牌与具体意见
- **THEN** 校验 HEAD、排除正式数据库的受审 index 指纹、合并父上下文、anchor、contentId、coverageId；成功登记本地确认并一次消费令牌
- **AND** 展示与登记消费同一完整原文，意见非空；同正文、意见和覆盖复用原记录
- **AND** 批量输入只集中传输独立意见；完整正文保留逐锚点可展开关系，事务失败整批保持可重试

<!-- @anchor id=review-records -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-records level=M -->

#### Scenario: 审计主体按直接关联汇集

- **WHEN** 从受检快照的声明、Scenario、Requirement 和架构片段汇集审计主体
- **THEN** 主体按锚点与直接关联归属，保留完整正文和来源，同一声明按稳定键去重

<!-- @anchor id=review-subjects -->
<!-- @architecture id=tools.anchoring -->
<!-- @budget id=review-subjects level=M -->

#### Scenario: 同树受检输入与检视准备

- **WHEN** 为 audit、record 或终态整理准备指定 tree 的检视输入
- **THEN** 模型、快照、审计主体与测试文件集合同源；当前记录、直接父及匹配本地确认按正文和覆盖解析为当前终态
- **AND** 完整快照先通过统一锚定校验再汇集主体与统计；未知架构引用报告 Scenario 来源与缺失 ID，工作树、index 和审计入口保持同一诊断语义

<!-- @anchor id=review-input -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-input level=L -->

#### Scenario: 受审 Git 上下文一致性

- **WHEN** 建立受审上下文或登记前复核上下文
- **THEN** 绑定 HEAD、排除正式数据库的 index 指纹、受检 tree 和直接合并父；HEAD、index 或合并父变化时登记失败

<!-- @anchor id=review-context -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-context level=L -->

#### Scenario: 真实 Git 检视夹具

- **WHEN** 治理测试需要独立仓库和检视流程
- **THEN** 共用最小真实 Git 夹具，通过正式 CLI 安装提交、数据库 merge driver 和 rebase 钩子，调用 audit/record；测试负责生命周期断言和独占资源释放

<!-- @anchor id=review-fixture -->
<!-- @architecture id=tools.testing -->
<!-- @budget id=review-fixture level=L -->

#### Scenario: 可复算审计记录

- **WHEN** 验证提交快照与完整审计原文
- **THEN** SQLite schema version、JSON schema、原文摘要与覆盖摘要必须一致，损坏数据直接失败
- **AND** MUST 先校验存储摘要与原文，再使用记录；MUST NOT 用重算摘要覆盖输入后宣称验证通过。schema v1 按其包含测试主体和未加工规范正文的原格式校验，通过后才转换为当前摘要；schema v2 按当前格式直接校验。valid 行摘要须与当前记录一致
- **AND** 当前及一代前次记录扁平保存，失效记录保留原标识、意见、来源和原文，更早记录由 Git 保存

<!-- @anchor id=review-ledger -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-ledger level=L -->

#### Scenario: worktree 私有原子登记

- **WHEN** 单个或多个进程并发 audit/record
- **THEN** 当前 worktree 私有 toolkit-review-local.sqlite 保存 tokens、pending、单槽 rebase_input，timeout=0；BEGIN IMMEDIATE 内删除令牌并要求 changes=1，再写 pending，竞争立即暴露 SQLITE_BUSY
- **AND** AST 和正文复算在事务外，事务失败保留令牌；无事务内令牌重读、超时或监听。令牌随 HEAD、index、父上下文变化失效，确认随提交失效；下一次写操作释放旧状态
- **AND** 同锚点重新签发替换旧令牌；成功提交后再次 audit 释放旧 HEAD 的令牌和确认

<!-- @anchor id=review-storage -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-storage level=L -->

#### Scenario: 正式审计数据库编解码

- **WHEN** 编码或读取正式 SQLite 审计状态
- **THEN** 在内存固定排序生成 schema_version=2 的 anchors 与 measurements 两表，使用固定 JSON 编码；读取 schema_version=1 时先按原格式验证记录再转换当前摘要，实测值为空，由下次发布填充；结构与记录由同一格式合同校验

<!-- @anchor id=review-database-format -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-database-format level=M -->

#### Scenario: 正式数据库同树读取与原子发布

- **WHEN** 读取指定 Git 版本的正式数据库、报告 HEAD 状态或发布数据库字节
- **THEN** 数据库从对应 tree 原样读取，inspect/verify 只读，workspace 报告只读 HEAD；发布前比较字节，相同零写入，变化以原子写替换唯一正式文件；缺库与 pending 如实报告

<!-- @anchor id=review-database-publication -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-database-publication level=M -->

#### Scenario: Git 审计生命周期安装

- **WHEN** 经正式 CLI 安装检视生命周期
- **THEN** Git 原生配置连接根 scripts 钩子、数据库 merge driver、merge rebase 后端及子模块同步

<!-- @anchor id=review-installation -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-installation level=S -->

### Requirement: 正式终态检视校验

`review verify` 先检查 HEAD gitlink，再只读取 HEAD 源码与 HEAD:tools/review.sqlite，复算全部正文、覆盖与实测值。缺数据库、缺记录、pending、实测值缺失或失配如实报告，本地确认仅在下一次提交整理时采用。Actions 审计 job 与代码测试独立；草稿和未标记 ready-to-merge 的 PR 报告结果，就绪 PR 要求 HEAD 已包含当前 main 且终态全部有效。

#### Scenario: 正式 HEAD 审计校验

- **WHEN** 执行正式 review verify
- **THEN** 检查 HEAD gitlink，只读取 HEAD 源码与同树数据库，复核全部正文、覆盖及实测值；缺失、pending 或失配如实报告并失败

<!-- @anchor id=review-verification -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-verification level=L -->

#### Scenario: 提交前 index 审计校验

- **WHEN** 本地已有待提交证明并检查受审 index
- **THEN** 普通源码提交要求准备结果中的锚点均有匹配审计；合并提交保存双方记录与 pending，rebase 重放由既有收尾合同整理；reconcile 纳入匹配本地确认后，正式 verify 继续检查原 HEAD

<!-- @anchor id=review-index-verification -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-index-verification level=L -->

### Requirement: SQLite 同树传输与 Git 生命周期

正式状态唯一为 tools/review.sqlite，表 meta(schema_version)、anchors(anchor, content_id, coverage_id, status, pending_reasons, current_record, previous_records) 和 measurements(anchor, metrics)。measurements 登记每锚点两区五维复杂度实测值，属派生数据：schema_version=1 的数据库读取为无实测值，发布重写为 v2；合并中间态允许缺省，发布与 verify MUST 按受检输入重算并比对，缺失或失配如实报告。.gitattributes 声明 -text merge=toolkit-review，原生 clone/push/fetch/pull 同步代码和数据库。reconcile 读取完整 index、直接父和本地确认，删除已不存在锚点，复用匹配审计，失配保持 pending；只自动暂存数据库。

pre-rebase 保存一份正式数据库，post-rewrite rebase 整理最终 HEAD、直接父和该输入。状态相同时结束；变化时 hash-object、write-tree 等价树替换、commit-tree 单父检查点和 update-ref 仅更新当前 HEAD，校验树差异只有数据库。检查点使用固定可读消息，至多一个；保护其它用户 index、工作树、分支和远端。原生 Git 2.51 active rebase 拒绝 commit --only，此检查点采用原生 plumbing，普通提交继续完整 hooks。失败保留输入和原始错误，后续 reconcile/rebuild 继续有限输入整理。

rebuild 从当前源码、可读的当前数据库、直接父、本地确认与单份 rebase 输入重建；明确报告被跳过的损坏数据库，无匹配意见的锚点保持 pending。所有未知命令和参数直接失败。

#### Scenario: 普通终态整理与同树同步

- **WHEN** 对当前受审 index 执行 reconcile，或提交经 Git 原生同步传输
- **THEN** 按同一受检输入复算终态及实测值并发布，只暂存正式数据库；接收端读取同树数据库，重复整理零写入、零暂存
- **AND** push/fetch 保持数据库 blob，fetch 不改变当前 HEAD；快进 merge/pull 同步完整终态，接收端没有签发端的私有令牌和确认
- **AND** 绕过提交钩子的源码失配由 HEAD 验证发现；reconcile 保留真实 pending，补审提交只更新数据库后验证通过

<!-- @anchor id=review-transfer -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-transfer level=L -->

#### Scenario: 从有限可读输入重建审计状态

- **WHEN** 正式数据库损坏或重写结果需要 rebuild
- **THEN** 从当前源码、可读当前数据库、直接父、匹配本地确认及属于当前 HEAD 的单份 rebase 输入重建；明确报告损坏输入，缺匹配意见保持 pending，按同一受检输入重算实测值并发布

<!-- @anchor id=review-rebuild -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-rebuild level=L -->

#### Scenario: worktree 保存唯一重写输入

- **WHEN** pre-rebase 保存输入或收尾登记结果 HEAD、读取及释放输入
- **THEN** 私有数据库只有一个 rebase_input 槽，保存输入 HEAD、tree 与正式数据库；结果 HEAD 明确绑定后续收尾，完成时只释放该槽

<!-- @anchor id=review-rebase-input -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-rebase-input level=M -->

#### Scenario: 分支与 worktree 独立审计

- **WHEN** 独立 clone、并行 worktree、detached HEAD 提交或 switch/checkout 切换分支
- **THEN** 各工作区读取所属 tree 的数据库，私有令牌和确认保持独立；HEAD 变化使原令牌失效
- **AND** commit --only 保留其它用户暂存文件；切换分支保留 Git 原生允许携带的未暂存内容，重复 reconcile 零暂存

<!-- @anchor id=review-worktree-workflow -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-worktree-workflow level=L -->

#### Scenario: 原生 index 操作与终态

- **WHEN** reset --hard/--mixed、stash push/apply/pop --index 或 revert --no-commit 后人工提交
- **THEN** 按当前 HEAD 与 index 核对终态，原生工作区和暂存区语义保持；旧 HEAD 的临时确认不进入目标数据库
- **AND** stash 保留源码暂存边界，同 HEAD 且正文仍匹配的本地确认可复用；revert 后的提交复用还原正文的审计，重复整理零写入

<!-- @anchor id=review-index-workflow -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-index-workflow level=L -->

#### Scenario: 原生重写终态与有界检查点

- **WHEN** Git 完成 rebase、autosquash/fixup、rebase-merges 或 cherry-pick，含 autostash 与 update-refs
- **THEN** 以当前 HEAD、直接父和单份输入复算审计；0 或 1 个检查点，仅修改数据库，保留用户源码、其它暂存文件和原生重写映射
- **AND** 文件或提交失败保留输入和原始错误，重试收敛并明确 pending，成功后释放该槽位
- **AND** 失败重试只属于已保存的结果 HEAD；当前 HEAD 不同时 reconcile/rebuild 处理当前状态并保留旧输入，显式 rebase-finish 报告所属 HEAD，检查点成为新的重试目标
- **AND** 收尾只释放 rebase 输入槽；同 HEAD 下新登记的确认和令牌保持可用，HEAD 改变后的失效与释放沿用普通临时状态生命周期

<!-- @anchor id=review-rebase-workflow -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-rebase-workflow level=L -->

### Requirement: 合并覆盖与未偿义务

contentId 由完整正文确定，coverageId 由主体 kind、key、relativePath 的结构身份集合确定，位置只展示来源。merge driver 只读取 base/ours/theirs 数据库；相同直接复用，单侧变化采用变化侧，双侧意见保留于扁平一代记录。文字意见差异自身不要求重审；正文或覆盖组合变化明确 pending，最终 reconcile 从完整 index 复算。源码冲突由 Git 处理，数据库语义合并自身成功。

普通整理只解析当前 tree；当前数据库已经完整匹配正文和覆盖时直接复用，其余情况下补读直接父，rebase 收尾增加单份输入。完整 pending 同样是可继承的真实终态，修复当前数据库后可继续提交而无须读取损坏旧父。合并提交先携带双方记录及 pending，合并后正常 audit/record 再提交完成审计；原生 rebase 逐提交重放只在最终 HEAD 整理。

必要性论证：Git 定义直接父关系，官方 AST 定义主体身份；项目只组合覆盖事件与有限证明继承。真实无冲突合并、父序、位置移动、强制提交后补审及重复普通提交验证这些语义。

#### Scenario: 合并覆盖变化

- **WHEN** 合并结果或直接父节点携带未偿覆盖义务
- **THEN** 三方数据库合并顺序保持确定性，最终正文和覆盖失配的锚点保持 pending；保留双方意见，匹配正文和覆盖的记录复用

<!-- @anchor id=review-inheritance -->
<!-- @architecture id=tools.cli -->
<!-- @architecture id=tools.anchoring -->
<!-- @budget id=review-inheritance level=L -->

#### Scenario: 原生三方合并集成

- **WHEN** Git 在 pre-merge-commit 执行自动合并提交，此时 MERGE_HEAD 尚未落地
- **THEN** 钩子调用专用 `review merge-prepare` 事件入口，复用完整提交检查与终态整理，保存当前数据库的双方记录和 pending，并报告待审数量；普通 `check:commit` 与 `commit` 拒绝额外 merge 参数。整理成功表达合并事件已处理，正式 `review verify` 始终严格检查 HEAD；最终源码覆盖变化通过合并后 audit/record 补审，正式 PR 就绪时归零
- **AND** 真实同意见、异意见和组合覆盖合并验证数据库自动收敛及失效记录保留
- **AND** 源码冲突解决后的 merge/cherry-pick continue 保留双方审计意见，最终正文失配明确 pending；squash 的组合覆盖在单父提交前完成补审

<!-- @anchor id=review-merge-workflow -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-merge-workflow level=L -->

### Requirement: 有限审计追溯

每锚点保存当前记录与扁平一代前次记录，合并可包含双方记录；新审计替换当前代时丢弃更早代，更早历史归 Git。inspect 展示数据库原文、意见、来源、标识和 pending 理由。0、10、100 次历史合并下，读取当前节点和直接父的数量保持固定。

`review status --target index|head [--json]` MUST 显式选择输入并只读报告：有效、首次待审、正文变化、覆盖变化、HEAD 已有 pending 积压及独立实测值失配；每份旧意见保留来源 HEAD/tree，定位新增、删除、修改主体的 kind/key/relativePath。共享变更按主体与变化种类汇总受影响锚点及数量，逐锚点保留独立诊断。位置平移自身不构成正文变化。HEAD 报告只判断 HEAD 数据库的实际当前证明，MUST NOT 通过父记录或本地确认把 HEAD pending 展示为有效；index 报告复用同树准备和有限继承。状态统计只描述证明与输入的对应关系，不将其扩张为功能质量结论。

必要性：Git、官方 AST 与既有审计摘要已提供输入、关联和证明，项目仅把失配原因及共享正文投影为可读报告，批量传输仍使用唯一记录格式和原生 SQLite 事务。状态查询不创建私有数据库，不发布或暂存正式数据库；架构读取复用 Git 批量 blob 原语，同次准备按锚点索引继承记录，发布比较受检 tree 的原始数据库字节。

#### Scenario: 查询当前与直接前代

- **WHEN** 查看单锚点检视状态
- **THEN** 只读展示数据库当前记录和扁平一代前次记录；读取范围固定，不创建私有数据库或初始化子模块
- **AND** status 按显式快照报告首次、正文、覆盖、已有积压和实测诊断，展示变化主体和意见来源；集中展示与批量登记保留逐锚点完整证明

<!-- @anchor id=review-inspection -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=review-inspection level=L -->

### Requirement: 环境变量文本疑似检查

疑似文本检查 MUST 与环境 API 访问检查保持独立，遵守单一入口的疑似项及行级标签规则。

#### Scenario: 环境变量文本疑似检查

- **WHEN** TS 字符串或模板文本、其他格式正文包含疑似环境变量
- **THEN** MUST 检查集中登记及紧邻行级非环境变量标签；TS 标识符、普通对象键及模块名称不计为疑似文本，API 精确命中 MUST NOT 被疑似项豁免

<!-- @anchor id=environment-text -->
<!-- @architecture id=tools.architecture -->
<!-- @budget id=environment-text level=L -->

### Requirement: 规范锚点块结构

规范锚点块 MUST 遵守规范结构与极简标记的归属、顺序和空行规则。

#### Scenario: 规范锚点块检查

- **WHEN** 解析 Scenario 尾部锚点块
- **THEN** MUST 检查锚点、架构目标、预算及非代码标记的顺序和归属，块与相邻正文间须恰好一个空行；未知或错位标签 MUST 定位并拒绝

<!-- @anchor id=spec-anchor-block -->
<!-- @architecture id=tools.anchoring -->
<!-- @budget id=spec-anchor-block level=L -->

### Requirement: 架构审计源片段

架构审计正文 MUST 直接来自官方 AST 定位的源片段。

#### Scenario: 架构审计片段提取

- **WHEN** 收集被 Scenario 引用的架构元素
- **THEN** MUST 经 LikeC4 官方解析器取得元素及其扩展的完整源码片段，按源码位置排序；嵌套与重叠片段交给统一区间合并，不生成派生 JSON 或文本摘要替代正文

<!-- @anchor id=review-architecture -->
<!-- @architecture id=tools.architecture -->
<!-- @budget id=review-architecture level=M -->

### Requirement: 决策记录

常驻记录位于 `openspec/records/`，与规范并列。编号分列 `REQ-`、`INT-`、`ADR-`，四位从 0001 连续递增；出现过的编号 MUST NOT 复用，记录文件 MUST NOT 删除。REQ 只保留原文，INT MUST 解释一条 REQ，ADR MUST 决定一条 INT 并列出受影响的 Requirement 锚点或架构元素。`status` 只有 `accepted` 与 `superseded`。`supersedes` 与 `supersededBy` MUST 互指且无环。接受后的正文与其余 frontmatter 冻结；同一次提交只允许把状态改为 `superseded` 并写上后继。

Requirement 可以有一行决策引用。架构元素可以有可选 metadata `decision`。编号不存在 MUST 失败。指向 `superseded` MUST 失败，诊断 MUST 写出 `supersededBy`。尚未标注的既有 Requirement 与元素保持合法；其受审正文相对直接父提交发生变化时，MUST 在同一提交引用本次新增的 ADR。

改 `openspec/specs` 的提交 MUST 在同一提交新增一份已归档标准 change，包含 `proposal.md`、`design.md`、`tasks.md` 与 specs delta，且官方 `openspec validate` 通过。change 只引用 ADR 编号。未归档 change MUST NOT 使规范直改合法，也 MUST NOT 进入正式检视。c4 元素改动只走 ADR 引用。记录进入检视，种类为 `record`；指纹只含冻结正文，不含 `status` 与 `supersededBy`。

必要性：官方 OpenSpec CLI 校验标准四工件，不冻结记录，也不认识 ADR。既有锚定检视不识别决策链。提交门禁因此只增加信封、编号、冻结、supersede、引用和归档 change 的确定性检查。

需求推导 MUST 区分原文约束与设计选择，首份记录同样核对完整需求和取舍。取代解读 MUST 保留原始需求的有效解读链，不能用另一条需求的解读替代。项目级 OpenSpec 技能 MUST 随仓库版本控制，根 AGENTS MUST 导航到记录、规划、实施、验证与归档流程，CLI 经根 pnpm 锁定依赖执行；已有用户授权不因技能模板重复索取。技能生成 MUST 在独占标准配置目录内显式固定 delivery 与 profile，不依赖或修改个人配置；完成后释放目录。

所有现存归档 MUST 保持四工件完整，新增或内容发生修改的归档 MUST 重新执行官方 validate，校验失败即门禁失败；已有归档的合法修改不能替代本次规范变更所需的新归档。Git 已提供变化识别，官方 OpenSpec 已提供 delta 校验，项目只组合判定；真实 Git 输入回归 MUST 覆盖规范修改、新 ADR、缺失新归档、合法新归档与无效旧归档的组合。

提交前检查 MUST 比较工作树与 HEAD；正式 `review verify` MUST 同时验证 HEAD 与其直接父的决策差异，所有记录、规范、架构、配置与归档均来自该 HEAD，未提交输入 MUST NOT 影响结果。首个提交没有前代，MUST 检查其当前结构与引用，以自身作为初始基线，不制造前代冻结义务；合并提交取第一父。Git 提供父列表、差异与批量 blob，现有官方校验与决策判定复用；不新增历史扫描或第二套检查器。

<!-- @decision id=ADR-0003 -->

#### Scenario: 决策记录检查

- **WHEN** 解析记录或核对 Requirement 与架构元素上的决策引用
- **THEN** 断链、冻结被破坏、supersede 不互指时失败，并在引用已取代记录时点名后继；未改动且无标签的 Requirement 通过

<!-- @anchor id=decision-record -->
<!-- @architecture id=tools.anchoring -->
<!-- @budget id=decision-record level=L -->

#### Scenario: 标准 change 门禁

- **WHEN** 规范正文相对直接父提交发生变化
- **THEN** 同一提交必须带有官方 validate 通过的新归档 change，且所引用 ADR 属于本次新增；`pnpm check:types` 不因该不一致失败
- **AND** 已提交的合法变更通过；缺新归档、冻结破坏和无效旧归档失败，未提交修补或破坏均不改变 HEAD 检查结果

<!-- @anchor id=decision-gate -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=decision-gate level=L -->
