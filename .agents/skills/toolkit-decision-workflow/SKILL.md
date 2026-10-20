---
name: toolkit-decision-workflow
description: 使用 Sphinx-Needs 需求图追踪原文、解读、决策、设计与实现验证，并按迁移 stack 替换旧治理。
---

# Toolkit 需求图流程

先阅读根 AGENTS.md 的迁移说明、requirements/sources.rst 的完整原文，以及 requirements/issue-179.rst 和 requirements/migration.rst 的决策与阶段边界。

原文逐字登记为 source，ID 使用 UUID v4。允许直接纠偏原文，以 Git diff 审核变化。每份有效原文只有一个有效 interpretation；解读写明出发点、角度、推导与结论，区分原文约束和设计选择。decision 记录必要性、已有能力、候选方案与取舍。design、implementation、verification 各自指向上游，所有环节使用原生 implements 关系。

类型、关系、schema、工作流和评分配置共用 requirements/ubproject.toml。架构继续以 tools/*.c4 为权威。现有旧规范按能力域逐批迁移，迁移前保持原正文，不批量重写。新图不再要求平行的 REQ/INT/ADR 或 OpenSpec 四工件与归档。

环境通过 pnpm install --frozen-lockfile 建立，prepare 与 Git hooks 调用 pnpm requirements:prepare。唯一配置为 requirements/ubproject.toml；uv 使用项目默认 .venv 与系统原生缓存、托管解释器位置，ubCode 使用项目原生 .ub_cache，不增加转发配置或缓存 Junction。构建与独占输入仍进入根 _build。pnpm requirements:check 检查图与配置；pnpm requirements:build 严格构建文档。并发构建使用独占分组名。

Vite/Vitest/Storybook 项目缓存通过原生配置进入 _build；Jiti 和 tsx 关闭转译磁盘缓存，受控子进程执行同一策略。Python 字节码属于原生环境，沙箱不复制宿主 .venv 或 .ub_cache。任务 finally 释放自有输入；硬中断残留按 issue-179.rst 中的原生命令显式回收，不引入自动清理器。

检视按 ../review-traceability/SKILL.md 执行，通过 requirements review audit 原子领取关系两侧全文、标准及随机一次性令牌，逐轴评分后通过 review record --stdin 事务登记。pnpm requirements:verify 同时检查结构、原生评分和同树 receipt；未完成或过期意见保持失败，不伪造完成。

源码与 LikeC4 完整正文通过逐文件 bindings.json 和标准 needimport 进入原生上下文及指纹；已迁移 C4 范围漏绑直接失败。旧二进制正式账本及强制 OpenSpec 流程已移除，worktree 私有 SQLite 只承担领取、单次消费、待发布事务与所属 rebase 输入；正式评分和领取证明随源码同树传输。
提交前暂存源码、图配置、正式意见及 receipt，pnpm check:commit 绑定 index；pnpm requirements:verify:head 独立读取 HEAD 的完整输入，工作树修改不能替代或阻挡正式审计。Git 生命周期只继承有限输入中完全匹配的证明，变化或冲突保持待审。旧规范按 requirements/migration.rst 分批迁移。提交、推送、PR 与合并继续遵守用户授权。
