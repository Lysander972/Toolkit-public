# repo-layout Specification

## Purpose

规定仓库分区职责、架构文件登记与双向覆盖、命名与产物落点、子模块和共享缓存归属，以及组件边界。

## Requirements

### Requirement: 架构登记唯一来源

`tools/model.c4` 中架构元素直接登记的文件 MUST 构成文件白名单本身。文件归属、职责及逻辑关联 MUST 由同一模型维护，MUST NOT 另建清单、独立迁移树或生成后反向维护的副本。本规范与其他文档 MUST NOT 重抄登记内容。

登记范围 MUST 覆盖仓库根与各分区的受检文件。旧代码 MUST 统一位于 `legacy/` 参考区，并以模型中的 `document` 元素登记；MUST NOT 混入 `src/`，也 MUST NOT 借助临时目录、路径越界或独立白名单放行。文档身份 MUST NOT 改变文件双向覆盖要求，MUST NOT 建立第二套登记结构。文档身份与源码检查范围的约束见 `openspec/specs/toolchain/spec.md` 的“架构唯一来源”。

#### Scenario: 文档重抄清单

- **WHEN** 文档逐条列出被允许的路径、目录名或通配
- **THEN** 违规，须删除该副本并指向架构模型

<!-- @anchor id=layout-manifest-unique -->
<!-- @nonCode -->

### Requirement: 分区职责与禁止内容

产品源码 MUST 只放产品模块，MUST NOT 直放仓库顶层，MUST NOT 含运行产物、日志与 IDE 配置。跨组件夹具 MUST 只放夹具区；测试归属遵循本规范“测试归属”。开发工具与中央构建资产 MUST 只放工具区，MUST NOT 入产品包、MUST NOT 放组件私有资源副本。工具区 MUST NOT 被产品区或夹具区依赖。无需隔离的依赖直接使用官方 API，不建立空的防腐层包。非代码资源 MUST 只放资源区，MUST NOT 在组件内另存副本。规范文档 MUST 只放规范区。规范平台的规范文件与工作流数据 MUST 只放其专属区。构建输出与日志区 MUST 为版本控制忽略区，MUST NOT 含被跟踪文件。

#### Scenario: 模块、测试或产物落错区

- **WHEN** 产品模块直放仓库顶层，测试落在专用测试区域之外，或运行产物、日志落入源码区
- **THEN** 布局门禁拒绝该提交

<!-- @anchor id=zone-content-placement -->
<!-- @nonCode -->

#### Scenario: 共享基座与工具的依赖方向被打破

- **WHEN** 产品区或夹具区引用工具区的路径或构建目标，或工具区依赖产品区、夹具区
- **THEN** 架构约束在本仓扫描中报出禁止边，门禁拒绝该提交

<!-- @anchor id=zone-responsibilities -->
<!-- @nonCode -->

### Requirement: 产物与日志落点

日志 MUST 统一落构建输出区，MUST NOT 堆在仓库根或组件根。测试临时工程 MUST 使用 Node 原生独占临时目录，测试结束释放；MUST NOT 写入源夹具。依赖缓存由 pnpm 原生管理。Prettier、tsc 与 ESLint 的增量状态位于当前工作区 `_build` 下的各自官方目录。Stryker incremental 文件位于 Git common dir，供同一仓库的 worktree 复用。实际构建与测试产物仍只进入当前工作区顶层 `_build`，不手工搬运缓存或另建结果副本。

#### Scenario: 日志堆在仓库根

- **WHEN** 日志生成在仓库根或组件根
- **THEN** 落点违规，须改为构建输出区内的带时间戳单文件

<!-- @anchor id=artifact-log-placement -->
<!-- @nonCode -->

### Requirement: 依赖配置继承

包管理配置 MUST 唯一继承仓库根权威：registry 与构建许可等 pnpm 配置 MUST 只写 `pnpm-workspace.yaml`，MUST NOT 在任意层级另建 `.npmrc`（`.npmrc` 白名单为空）。`packageManager` 声明 MUST 只存在于根 `package.json`，成员包 MUST NOT 重复声明。锁文件 MUST 唯一为根 `pnpm-lock.yaml`，MUST NOT 存在任何其他锁文件。成员包 MUST 只声明依赖与脚本，MUST NOT 携带本地解析、本地安装或依赖旁路配置。`node_modules` 与依赖临时文件 MUST 只存在于被忽略的安装产物位置，MUST NOT 出现在工作区文件清单中。

#### Scenario: 局部包旁路根配置

- **WHEN** 成员包或任意层级出现 `.npmrc`、局部 `packageManager`、第二锁文件或本地安装配置，含被 ignore 藏住的分裂锁与 `node_modules/.package-lock.json`
- **THEN** 治理检查报错并拒绝该提交，须删除旁路配置与污染产物并继承根权威

<!-- @anchor id=deps-config-inheritance -->
<!-- @architecture id=tools.cli -->
<!-- @architecture id=tools.tests -->
<!-- @budget id=deps-config-inheritance level=M -->

#### Scenario: node_modules 或依赖临时文件乱飞

- **WHEN** `node_modules` 内任何文件或一次性依赖临时文件出现在工作区文件清单（含未跟踪文件）
- **THEN** 治理检查报错并拒绝该提交

<!-- @anchor id=deps-artifacts-quarantine -->
<!-- @architecture id=tools.cli -->
<!-- @architecture id=tools.tests -->
<!-- @budget id=deps-artifacts-quarantine level=M -->

#### Scenario: 全部继承根权威的完整清单

- **WHEN** 工作区文件清单只含根 `pnpm-workspace.yaml`、根 `pnpm-lock.yaml`、根 `package.json` 与各注册成员包清单，无任何旁路配置
- **THEN** 治理检查通过

<!-- @anchor id=deps-config-inheritance-positive -->
<!-- @architecture id=tools.cli -->
<!-- @architecture id=tools.tests -->
<!-- @budget id=deps-config-inheritance-positive level=M -->

### Requirement: 现役依赖选择与版本锁定

现役外部直接依赖和宿主 peerDependencies MUST 在所属 package.json 使用精确版本，根 packageManager 使用精确 pnpm 版本；内部包使用 workspace:*，由同树源码确定内容。VS Code engines 表达宿主最低支持范围，不是可漂移的库版本。传递依赖由唯一根 pnpm-lock.yaml 固定解析版本、peer 上下文与 integrity；安装与更新遵循 toolchain 的单一入口，不手改锁文件或复制上游依赖表。

依赖声明 MUST 对应当前消费者：产品运行依赖归所属成员包，根仅持有治理、构建与跨模块测试的实际依赖；根测试入口所用 Vitest、Vite 及插件不在无本地入口的成员包重复声明。Knip MUST 同时报告未使用依赖、未声明依赖及未解析引用，按真实消费者修正，不以排除报告保留旧依赖。根与成员包直接消费同一库时采用同一版本；上游锁定的传递依赖允许按其原生约束保留，不自造覆盖规则强制扁平化。

选型与取舍：继续使用已经接入的 Effect/平台 IO、TypeScript/Chevrotain/Hjson/XML、Schematics/MST、官方 MCP SDK/Zod、CanvasKit/Spine、标准 LSP 客户端和 Pino；版本权威是包清单与根锁文件，本规范不重抄版本表。Pi peer 固定官方 1.0.0，接口依据为已登记的同提交官方参考；其传递包按锁文件解析，不能把参考源码中的版本范围当作本项目任意升级的授权。RunningHub 工作流与模型仍按资产目录的来源和生产验证状态约束，npm 锁定不证明远端模型已经可用。

原子文件写入统一选用 write-file-atomic 7.0.0，其官方 API 保持 Promise 写入、同文件串行、异文件并发及失败临时文件释放。7 的 Node engines 为 ^20.17.0 或 >=22.9.0，满足既定 VS Code 1.100 与 Pi 1.0.0 宿主；8 要求 ^22.22.2、^24.15.0 或 >=26，超出这两个既定最低宿主，不作为统一依赖。保留成熟原子写入避免自研事务文件逻辑，统一直接版本消除原有三份实例；代价是暂不采用要求更高宿主的主版本。

必要性与验证：pnpm 已承担 workspace 链接、冻结解析、安装状态与锁文件，Knip 已承担消费者检查，不新增版本数据库或依赖解析器。使用原生锁定更新和冻结安装校验清单一致性，真实文件原语、前端配置写入与 SQLite 发布回归核对既有原子写入行为；这些回归不代替未实现业务的验收。

官方依据：[pnpm install](https://pnpm.io/cli/install)、[workspace protocol](https://pnpm.io/workspaces)、[Knip issue types](https://knip.dev/reference/issue-types)、[write-file-atomic](https://github.com/npm/write-file-atomic)，以及已核对 7/8 的官方 package.json engines 和 README、[VS Code 1.100.0 的 Electron 34.5.1 声明](https://github.com/microsoft/vscode/blob/1.100.0/.npmrc)、[Electron 34.5.1 的 Node 20.19.0 声明](https://github.com/electron/electron/blob/v34.5.1/DEPS)、Pi 固定参考的 Node >=22.19.0 engines。

#### Scenario: 同树依赖可重复建立

- **WHEN** 从当前包清单与唯一根锁文件安装并运行正式检查和相关回归
- **THEN** pnpm 冻结安装使用固定解析，Knip 拒绝未使用或未声明依赖；内部包消费同树源码，共用库版本一致，实际文件写入及错误传播符合原合同

<!-- @anchor id=active-dependency-locking -->
<!-- @nonCode -->

### Requirement: 测试归属

全部测试 MUST 收敛到 `test/modules/` 专用区域，并在区域内按模块组织；MUST NOT 在源码目录保留测试入口或模块私有测试命令。套件配置只有 `vitest.config.mjs`。测试发现只有该文件的 `test.include` 与 `test.exclude`。同一文件的 `test.coverage` 声明 V8、`TestMetrics.measuredFiles`、报告目录 `_build/testing/coverage` 和 `text`/`json-summary`，不设置 `enabled: true`。`pnpm test` 先构建前端插件与预览，再执行 `vitest run`，不传 `--coverage`。`pnpm test:metrics` 是唯一测量入口：`test:coverage` 在同一配置上只追加官方 `--coverage`，然后 `test:mutation` 的 `stryker run`，然后 `check:metrics`。Stryker `mutate` 与 `test.coverage.include` 都是 `TestMetrics.measuredFiles`，不是第二套测试发现。禁止第二份 Vitest 配置、第二份 `test.include`/`test.exclude` 或成员包 test 脚本。夹具 MUST 保留既有归属，测试 MUST NOT 修改原始夹具。

#### Scenario: 测试归属或夹具使用违规

- **WHEN** 测试放在源码目录、使用独立模块入口或修改原始夹具
- **THEN** 判为违规；测试必须位于专用测试区域、通过根 `pnpm test` 或 `pnpm test:metrics` 执行，并保持原始夹具只读

<!-- @anchor id=test-ownership -->
<!-- @nonCode -->

### Requirement: 命名与编码

组件与目录名 MUST 为小写 snake_case，组件名 MUST 与组件模块路径注册逐字一致。以标识符堆叠命名的目录 MUST NOT 出现。文件名与目录名 MUST 为 UTF-8。

产品源码目录 MUST 按实际职责直接组织，不因 domain、runtime、adapters 等分类人为增加层级，不在包内重复套 src。Host 内有状态服务共用一个运行时包，现有纯计算库与协议、插件入口保留独立包；包边界服从职责和现有消费者，不按每个空壳服务新建包。具体文件归属唯一见架构模型。

#### Scenario: 日期堆叠命名

- **WHEN** 出现带序号或日期堆叠的日志类命名
- **THEN** 视为垃圾信号，须改为带时间戳单文件

<!-- @anchor id=naming-encoding -->
<!-- @nonCode -->

### Requirement: 文件登记双向覆盖

每条被跟踪路径 MUST 唯一命中一条架构文件声明；每条声明 MUST 命中至少一条被跟踪路径，空声明、未命中与重复归属均 MUST 报错。组织层级 MUST NOT 隐含递归放行。工作流数据与规范 MUST 同受模型登记约束。架构目标可以在 `#gap` 节点中登记尚未创建的目标路径，但该声明不构成合法文件归属，检查 MUST 持续报告目标未命中，直到实际文件建立并通过双向覆盖。

#### Scenario: 文件与声明不能相互覆盖

- **WHEN** 文件不在登记范围、声明未命中文件或文件重复归属
- **THEN** 检查失败并定位文件和架构元素

<!-- @anchor id=manifest-four-semantics -->
<!-- @nonCode -->

### Requirement: 门禁豁免显式化

格式与检查门禁的豁免 MUST 按对应门禁规范显式登记对象与理由，MUST NOT 散落在实现脚本或另一清单中。

#### Scenario: 豁免藏在脚本里

- **WHEN** 某路径被实现脚本内置的忽略列表跳过而非在对应门禁规范声明
- **THEN** 违规，须把豁免对象与理由移入对应规范

<!-- @anchor id=explicit-gate-exemptions -->
<!-- @nonCode -->

### Requirement: 旧版参考区冻结

`legacy/` 参考区 MUST 整体冻结：架构模型在唯一元素上以 `metadata.freeze` 登记冻结基线提交的完整 SHA（40 位小写十六进制），全模型至多声明一次。门禁 MUST 以 `git diff --name-status --no-renames 基线 受检目标 -- legacy` 比对两树终态：任何新增、删除或修改 MUST 拒绝，提交历史过程不影响判定。普通检查与 CI 以 HEAD 为受检目标，兜住绕过钩子的提交；提交检查以受检 index 的 write-tree 终态为目标。基线提交不可解析 MUST 显式失败；受检树存在 `legacy/` 路径而模型未声明冻结基线 MUST 拒绝，防止删除声明放开冻结。基线位点变更本身是唯一豁免形态：须以经治理的模型修改显式移动，MUST NOT 借第二登记结构、散落常量或绕道暂存实现。`legacy/**` 变更 MUST 纳入 `ci.yml` 触发白名单并以完整历史检出（fetch-depth: 0），保证基线对象可解析。旧版正式代码本身没有测试；任何向 `legacy/` 补充测试、复制品或文档的尝试都按篡改拒绝。

必要性论证：Git 原生 diff 提供两树终态比对与历史无关语义，LikeC4 metadata 提供唯一受校验登记点；原生工具不校验目录终态与指定位点一致，自研仅保留冻结范围常量、基线读取与 diff 判定，不实现第二份文件清单。行为验证覆盖一致通过、增删改逐类拒绝、非 legacy 变更不触发、基线不可解析失败与未声明拒绝。

#### Scenario: 冻结区任何终态变化

- **WHEN** 提交、暂存终态或 CI HEAD 中的 `legacy/` 路径相对冻结基线出现新增、删除或修改
- **THEN** 门禁拒绝并定位路径与变更种类

<!-- @anchor id=legacy-freeze -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=legacy-freeze level=M -->

### Requirement: 子模块与共享缓存归属

本仓库当前没有子模块。`docs/MechToolkit` 的技能与文档 MUST 作为普通文件随本仓库提交维护，版本由同树提交确定；远端独立仓库保持原样。工具自举不消费技能与文档内容。

子模块实际内容需求或 pnpm sync:submodules 调用触发 Git 原生 update --init --recursive --checkout。先检查所有相关已初始化子模块 dirty，再针对所需路径一次同步，最后复核；匹配路径不 update。提交检查、post-checkout 与 post-merge 共用常规维护入口，只对已初始化的干净失配路径对齐，不物化未消费目录。安装时设置原生 on-demand push/fetch，运行检查不反复修改配置。Git 失败直接返回，不自旋、不连续重拉、不覆盖 dirty。

#### Scenario: 绕过 gitlink 改子模块

- **WHEN** 不经 gitlink 改动主库与子仓库的提交关联
- **THEN** 门禁拒绝

<!-- @anchor id=submodule-cache-ownership -->
<!-- @nonCode -->

#### Scenario: 子仓库脏或 gitlink 漂移

- **WHEN** 检查、维护或显式同步子模块
- **THEN** 未消费且未物化的 gitlink 直接通过；所有 dirty 在同步前暴露，已初始化的干净失配或显式所需目录通过一次原生 update 对齐 index，最后复核；匹配目录不重复 update

<!-- @anchor id=submodule-gate -->
<!-- @architecture id=tools.cli -->
<!-- @budget id=submodule-gate level=M -->

### Requirement: 只读外部参考

外部开源项目副本 MUST 只读：MUST NOT 删除其许可文件、MUST NOT 抄袭进产品源码、MUST NOT 新增无纪律声明的项目。SDK 数据以固定版本的结构化平台文档交付，工具开发、构建与验收只使用公开接口、签名、格式与可观察行为。旧版硬编码 SDK 方法签名与新结构化文档同源，继续保留其参考身份。来源说明只登记可公开访问的资料。

`docs/reference/` 中的可选本地资料 MUST 完全视为独立于本仓库结构的内容，不是任何子模块，不属于仓库依赖、受维护夹具或构建与测试输入。资料存在时允许按需查阅，并按其内部文档的指示开展相关活动；缺失不影响本仓库开发、检查与测试。

#### Scenario: 修改只读外部参考源码

- **WHEN** 只读外部参考源码被改动
- **THEN** 视为违规

<!-- @anchor id=readonly-external-reference -->
<!-- @nonCode -->

### Requirement: 跨组件引用对齐

跨组件路径引用对齐 MUST 经结构化校验引擎执行，提取器 MUST 用官方 TypeScript 编译器 API 解析配置与源码中的相对引用，MUST NOT 用文本模式匹配。新增引用形态 MUST 在提取器中登记。

#### Scenario: 相对引用未命中被跟踪文件

- **WHEN** 源码类相对路径引用未命中版本控制跟踪的文件或目录
- **THEN** 断裂即拒绝提交

<!-- @anchor id=relative-ref-integrity -->
<!-- @nonCode -->

#### Scenario: TS 层引用含依赖实体目录

- **WHEN** TS 层相对引用解析后含依赖实体目录
- **THEN** 违规，裸包名引用与构建期注入是唯一合法形态

<!-- @anchor id=cross-component-refs -->
<!-- @nonCode -->

### Requirement: 组件边界约束

组件边界 MUST 由架构模型加约束执行器强制：模型声明的容器对即允许的依赖，未声明即禁止。工具区 MUST NOT 依赖任何产品组件；产品组件 MUST NOT 依赖工具区。依赖实体链接 MUST 只允许根包或同包。组件根为组件名的实际目录前缀，前端容器内组件映射为其下子目录。

#### Scenario: 跨包借道依赖实体

- **WHEN** 某包经非根、非同包的路径引用依赖实体链接
- **THEN** 构建期边界检查失败

<!-- @anchor id=component-boundaries -->
<!-- @nonCode -->

### Requirement: 共享静态资源唯一源点

非代码资源 MUST 归 `res/`，按类型建子目录，且 MUST 只有唯一源点。产品组件、测试区与工具区对它的依赖 MUST 是单方面的（不构成反向依赖），每条边 MUST 逐条声明在架构模型内。新版产品按架构模型登记为现役源码或资源；旧产品代码仍作为参考文档维护，资源消费按实际登记的产品组件确定。

#### Scenario: 组件直读资源路径

- **WHEN** 组件制造未登记的第二份资源来源
- **THEN** 边界检查失败

<!-- @anchor id=resource-access-channel -->
<!-- @nonCode -->

#### Scenario: 资源源点出现第二份

- **WHEN** 同一资源在 `res/` 之外另存一份供组件消费
- **THEN** 违规，唯一源点须保持单一

<!-- @anchor id=shared-resource-origin -->
<!-- @nonCode -->

### Requirement: 组件封闭性

任何组件 MUST NOT 引用另一组件的内部文件或内部依赖；共享内容 MUST 抽离并经公开入口消费。宿主 MUST 经包名引用共享库，MUST NOT 用相对路径、别名或按内部文件逐个引用。共享库产物 MUST 唯一为 ESM 形态，且 MUST 只在构建输出树生成。

#### Scenario: 组件引用另一组件内部文件

- **WHEN** 某组件经相对路径或内部文件引用另一组件的内部实现
- **THEN** 封闭性违规，须抽离到公开入口消费

<!-- @anchor id=component-closure -->
<!-- @nonCode -->

### Requirement: 工作区边界

本仓库工作区（开发工具自身）与运行时工作区（工具运行时打开的用户工程）MUST NOT 相交。排查运行时问题时 MUST NOT 以本仓库内的指引文档或测试夹具推断运行时工程的内容与配置。

#### Scenario: 以本仓库夹具推断运行时工程

- **WHEN** 依据本仓库内的测试夹具或指引文档判断运行时工程的行为与配置
- **THEN** 违规，二者无任何关系

<!-- @anchor id=workspace-separation -->
<!-- @nonCode -->

### Requirement: 术语与命名

文档与代码 MUST 使用原版游戏既有的真实名词，MUST NOT 自造或意译替换：机械类与零件类实体 MUST 沿用源码既有类型名，MUST NOT 译作机甲、设备之类。零件目录名 MUST 为全数字，MUST NOT 出现字母加序号式命名。

#### Scenario: 意译术语或非数字零件目录

- **WHEN** 文档或代码把机械写成机甲、零件写成设备，或用字母加序号命名零件目录
- **THEN** 违规，须改用原版既有名词与全数字目录名

<!-- @anchor id=game-terminology -->
<!-- @nonCode -->
