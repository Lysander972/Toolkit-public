---
name: review-traceability
description: 阅读需求关系两侧全文，按仓内评分标准登记绑定正文与标准指纹的检视意见。
---

# 需求图检视

先暂存本次受审正文，执行 `pnpm requirements review status --pending` 定位变化。
逐项执行 `pnpm requirements review audit --id <UUID>`，同一次领取完整 context、评分标准、verdict_schema 与随机一次性 ticket。完整阅读 anchor 和 related 正文；相关源码与 LikeC4 正文也须实际阅读。只读 context 或 brief 查询不产生可登记凭据。
原文只引用原记录，不用摘要代替；原文允许直接纠偏，变化由 Git 留痕。

逐轴给出整数分数和具体理由，低于满分给出可执行建议。解读须核对出发点、角度、推导与结论；决策须核对已有能力、必要性、候选及取舍。
凡与原文目标、范围或验收相矛盾的内容，该轴为 0，先修正内容再复审。
按本次领取的 verdict_schema 构造 verdict，以非空数组保存 id、ticket.token 和 verdict，经 stdin 交给正式入口：

`pnpm requirements review record --stdin`

令牌绑定本次 HEAD、index、tree、合并父节点及正文、覆盖、评分标准；重新领取替代旧令牌，登记整批校验后事务消费。冲突后重新领取全文、重新评分。发布中断使用 `pnpm requirements review publish` 重试。
不直接编辑 `.pharaoh/verdicts` 或 `.pharaoh/receipts`，不批量套用同一意见，也不以通过结构检查代替检视。原生裸评分缺少领取证明，不能通过正式门禁。
执行 `pnpm requirements:verify`，检查结构缺口、缺失意见、低分和过期意见。
阈值由 `requirements/ubproject.toml` 定义，未满分建议保存在正式意见中，后续专项检视读取这些建议。

源码和 LikeC4 全文由 `requirements/bindings.json` 逐文件绑定并导入同一原生上下文；修改后重新领取。覆盖检查反向核对已迁移的 C4 范围，漏绑直接失败，未迁移规范单独报告。正式意见及 receipt 随源码暂存，`pnpm check:commit` 检查 index，`pnpm requirements:verify:head` 检查 HEAD；两者独立读取各自 blob，工作树内容不能代替或阻挡 HEAD 判断。

`pnpm requirements review reconcile` 只从当前 HEAD 与合并直接父继承正文、覆盖和标准完全匹配的证明；意见冲突保持待审。pre-rebase 记录所属输入，post-rewrite 校验 Git 重写映射后额外使用该输入；不扫描任意祖先，不自动提交或合并。
