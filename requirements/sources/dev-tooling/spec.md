# dev-tooling Specification

## Purpose

规定开发期工具的实现纪律与执行约定：官方依据、复用优先、禁止试错、完成条件、文本编码与格式化要求；提交门禁机制引用 toolchain 规范。

## Requirements

### Requirement: 官方依据优先

任何实现、选型、协议对接、格式处理与平台能力判断 MUST 以对应产品、语言或库的官方文档为唯一行为依据；博客、搜索摘要、论坛回答、模型记忆与试错结果 MUST NOT 替代官方文档。官方文档未确认的能力 MUST NOT 假设存在。

#### Scenario: 以试错结果作为行为依据

- **WHEN** 实现依据来自试错结果而非官方文档
- **THEN** 违规，须先取得官方文档依据

<!-- @anchor id=official-docs-first -->
<!-- @nonCode -->

### Requirement: 复用优先

实现前 MUST 定位该能力的唯一归属模块与可用的成熟依赖；能复用 MUST 复用，官方提供解析器或推荐库时 MUST 使用。结构化格式、源码、配置与协议 MUST 用官方解析器或官方推荐的成熟库，MUST NOT 用字符串切分、替换链或正则拼凑语法与层级。仅当官方文档确认无可用解析器时才允许自研，且 MUST 以覆盖合法、非法与边界输入的测试钉死行为。

#### Scenario: 用正则拼凑语法结构

- **WHEN** 以正则或字符串替换解析结构化格式
- **THEN** 违规，须改用官方解析器或成熟库

<!-- @anchor id=reuse-first -->
<!-- @nonCode -->

### Requirement: 禁止试错与实验落库

失败时 MUST 先取得可复现的正式测试输入并确认根因，再改唯一责任模块；MUST NOT 用连续小改、fallback、自动修复、无依据的空值兜底或特判碰运气。为摸索接口而写的一次性脚本、调试开关与临时兼容逻辑 MUST NOT 落进受版本控制目录，实验 MUST 只在系统临时目录或 `_build/` 进行。

#### Scenario: 以兜底掩盖根因

- **WHEN** 以 fallback 或空值兜底让失败消失而未确认根因
- **THEN** 违规，须先复现并定位根因

<!-- @anchor id=no-trial-and-error -->
<!-- @nonCode -->

### Requirement: 完成条件

完成 MUST 同时满足：正式入口验证通过、无未使用代码或临时产物、无新增第二实现来源。「本机试过能跑」MUST NOT 构成完成或提交依据。

Tools 自举测试由独立 workflow 执行，业务 workflow 排除 Tools 测试；两者只在对应源码、测试、配置、规范或 CI 文件变化时触发。测试使用 forks、isolate 和固定的两个 worker；审计生命周期夹具 hook 复用 worker 内已加载的 CLI，生产 hook 回归必须经 Git hook run、原生 pnpm 与独立 CLI 进程验证实际入口。

#### Scenario: 以本机手跑作为完成依据

- **WHEN** 仅以本机手动试跑通过为由声明完成
- **THEN** 不成立，须经正式入口验证

<!-- @anchor id=done-criteria -->
<!-- @nonCode -->

### Requirement: 指令执行约定

指令执行 MUST 一律在仓库根目录。仓库根 MUST 经唯一方式获取，MUST NOT 以多层向上推导或按脚本位置相对定位求根。

#### Scenario: 在子目录执行指令

- **WHEN** 开发指令在非仓库根目录执行
- **THEN** 违规，须回到仓库根执行

<!-- @anchor id=repo-root-only -->
<!-- @nonCode -->

### Requirement: 文本编码与格式化

工具的文字输出与文件读写 MUST 锁定 UTF-8（无 BOM），MUST NOT 依赖控制台默认编码。文本收敛的判定范围、豁免与登记方式见 `openspec/specs/toolchain/spec.md` 的“文本收敛”。TS 与 JS 的格式化 MUST 调用官方 Prettier，MUST NOT 自研格式化逻辑。

#### Scenario: 文本层格式违规

- **WHEN** 出现 BOM 或尾随空白
- **THEN** 格式门禁拒绝该提交

<!-- @anchor id=text-encoding-normalization -->
<!-- @nonCode -->

### Requirement: 格式门禁

提交前门禁 MUST 以暂存树为唯一输入，对受治源码执行全量格式判定；未暂存的工作树内容 MUST NOT 代替暂存内容。未格式化即拒绝提交，且 MUST NOT 自动改写。全仓文本收敛 MUST 显式执行，MUST NOT 隐式发生。

#### Scenario: 提交未格式化文本

- **WHEN** 暂存区含未收敛的文本编码或行尾
- **THEN** 提交前门禁拒绝该提交

<!-- @anchor id=format-gate -->
<!-- @nonCode -->

### Requirement: 提交门禁形态

钩子安装与转发、执行输入隔离、审计登记及 SQLite 生命周期统一遵循 [toolchain](../toolchain/spec.md) 对应 Requirement。开发者在仓库根通过正式 scripts 执行检查；合并前满足 Actions 与本地 `check:commit`。GitHub PR 由用户本人合并。

#### Scenario: 提交信息不合规

- **WHEN** 提交标题不符合形态约定或超出长度限制
- **THEN** `commit-msg` 拒绝

<!-- @anchor id=commit-gate-shape -->
<!-- @nonCode -->

### Requirement: 指引文档唯一权威

仓库根指引文档 MUST 是 Agent 指引的唯一权威源，MUST NOT 存在独立内容的第二份指引。

#### Scenario: 出现独立内容的第二份指引

- **WHEN** 仓库内出现与指引文档内容独立的第二份 Agent 指引
- **THEN** 违规

<!-- @anchor id=agent-guide-authority -->
<!-- @nonCode -->

### Requirement: 密钥零输出纪律

密钥 MUST NOT 以任何形式输出到会话、终端、日志或文件（含系统临时目录与 `_build/`）。密钥消费 MUST 只经正式入口的内存上下文，MUST NOT 为探测或查看而手搓解密脚本。MUST NOT 因纯本地输出假设需要轮换本地凭据。

#### Scenario: 明文密钥出现在输出

- **WHEN** 解密后的密钥被打印、写盘或以任何形式进入会话
- **THEN** 判为密钥零输出纪律违规，停止继续输出；不得推断本地凭据须轮换

<!-- @anchor id=secret-zero-output -->
<!-- @nonCode -->
