# Git 协作规范

### Requirement: 标准协作入口

分支、Issue、PR 与 Actions 使用 GitHub 官方 MCP 或 CLI，Git 使用原生命令与标准凭据管理。项目检查不接管远端协作、自动合并或 Git 事务。主库与文档仓库分别使用各自 GitHub 远端，工具自举不依赖文档子模块内容。

审计存储、同树传输、登记和 Git 生命周期统一遵循 [toolchain](../toolchain/spec.md)；子模块同步遵循 [repo-layout](../repo-layout/spec.md)。`check:ci` 与 CI tools 作业执行同一序列：并行通用检查、schema、gitlink，然后 `pnpm test:metrics`。CI 传入 `--base`，本地默认 HEAD。独立审计 job 报告有效性。就绪 PR 要求包含当前 main 且审计全部有效。

#### Scenario: 协作操作

- **WHEN** 使用原生 Git 与官方 GitHub 入口协作
- **THEN** 提交携带完整 SQLite 终态，合并覆盖失配按具体锚点实质检视；GitHub PR 合并仍由用户本人完成

<!-- @anchor id=standard-collaboration -->
<!-- @nonCode -->

### Requirement: CI 事件与审计就绪

治理检查、`pnpm test:metrics` 与静态预览构建由 `ci.yml` 的单一 tools 作业执行。`check:ci` 使用同一检查序列和 `pnpm test:metrics`；CI 传入 `--base`，本地默认 HEAD。workflow 级原生 paths 白名单先排除无关输入与审计数据库，无关变更不启动任何 Runner。工具测试包含在 `vitest.config.mjs` 这一套件里，不另建重复执行同套检查和测试的工作流；仅审计数据库变化只触发 audit。审计由 `audit.yml` 独立执行：PR 全部事件直连该 workflow 且不设 paths 过滤，保证任何变更范围的 ready-to-merge 审计门禁可达；`ci.yml` 内的 audit 作业仅在 push main 时经 workflow_call 串入发布依赖链。closed 事件以原生 concurrency 按工作流与 PR 编号分组取消同组旧运行并跳过全部执行作业。

草稿与尚未标记 ready-to-merge 的 PR 仅报告审计结果；正式且标记就绪的 PR 要求包含 main 并通过审计。GitHub Actions 定义触发和条件语义；项目只声明 job 选择。自动化测试用 GitHub 官方 workflow-parser 与 expressions 解析实际 YAML 并计算事件矩阵，验证 job 调度及报告/门禁行为；Vitest 的原生依赖转换负责解析器包中的 JSON schema。

tools 作业经根 `pnpm test:metrics --base` 完成插件与预览构建、该套件的一次 V8 覆盖率测量、一次 Stryker 变异与棘轮比较。PR 基线取目标分支基准提交，main push 取推送前提交。指标比较每次真实执行。Stryker incremental 文件放在 Git common dir；CI 用 `actions/cache@v4` 按 `github.sha` 保存、按 `stryker-incremental-` 前缀恢复该文件。指标步骤预算为 330 分钟，tools 作业预算为 360 分钟，为安装、检查及产物验证保留余量；各步骤预算必须小于作业总预算。Stryker 使用官方默认并发、Node 原生默认堆配置和 30 分钟初始测试预算。UI 交互由 Vitest/happy-dom 验证；CPU 产物生成使用独立正式入口，阶段二保持明确 TODO 失败。站点白名单随这次 V8 覆盖率测量执行。Stryker 初始测试按同一 `vitest.config.mjs` 再完整执行该套件，供 incremental 计算覆盖差异；未改动变异体不重测。main push 在测量之后执行 `build:preview:assets`，失败保留原始错误并停止上传和发布。publish job 仅在 main push 且 tools、audit 全部成功时消费本次运行的站点产物，串行单向推送到公开 MechToolkit gh-pages；发布合同见 preview 规范。

#### Scenario: 标签变更

- **WHEN** 添加或移除 ready-to-merge 标签
- **THEN** 只运行 audit；代码和测试保持原有结果，审计按草稿和最新标签状态决定报告或门禁
- **AND** 无关标签变更跳过全部作业且保持已有运行；代码更新按变更范围执行相关测试与审计；关闭或合并取消该 PR 的旧运行

<!-- @anchor id=ci-event-policy -->
<!-- @architecture id=repository.build -->
<!-- @budget id=ci-event-policy level=M -->

### Requirement: CI Runner 内并发与短路

CI 保持 `ci.yml` 单一 tools 作业承载通用检查、全部测试及静态预览构建。入选工作流 MUST 在该 Runner 内以 npm-run-all2 的 run-p 有界并发执行独立检查族。失败保留非零退出码并终止同组后代进程。站点产物校验在同次构建完成后执行。

作业 timeout 按全量负载余量声明。无相关变更输入的工作流 MUST 整体跳过，审计数据库 MUST 排除在变更判定外。

触发范围语义：PR 事件按 PR 累积变更文件对白名单求值，后续每个提交都按新 HEAD 重新完整验证，不存在沿用旧 HEAD 测试结果；push 按本次推送差异求值。标签事件 MUST NOT 取消或替换同 PR 正在运行的代码测试与审计：无关标签事件以独占分组空转跳过，保持已有运行与结果。

优化验收 MUST 以相同受检输入下的实际额度消耗、耗时与该跑、该跳、该取消行为为准；作业拓扑的调整 MUST 有实测数据支撑。

#### Scenario: 无关输入与标签短路

- **WHEN** 变更文件全部落在白名单之外或仅审计数据库，或到达无关标签事件
- **THEN** 相关工作流不启动 Runner 或以独占分组空转，已有运行与结果保持

<!-- @anchor id=ci-parallel-topology -->
<!-- @nonCode -->

### Requirement: 写操作边界

commit、push、发布和远端写操作分别遵守用户明确授权。当前工作树是唯一开发位置。GitHub PR 合并由用户本人完成。Agent 保留用户工作区修改，实际错误直接报告并按正式实现修复，回滚、重置、恢复或清理工作区须由用户明确指定具体动作。

检视必须阅读完整受审正文并给出具体意见，操作使用 toolchain 定义的正式 audit/record 入口。存储、令牌和继承算法以该规范为唯一权威。远端写操作与代码推送分别按用户点名授权执行。

#### Scenario: 授权范围

- **WHEN** 用户授权实现、commit、push 或远端操作
- **THEN** 操作限于点名范围，PR 合并仍由用户本人执行，所有源码修改留在当前工作区可追踪

<!-- @anchor id=write-boundary -->
<!-- @nonCode -->
