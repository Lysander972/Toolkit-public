Issue #177：可执行规范
=======================

.. source:: Issue #177 原文
   :id: 2f6e3db8-9d08-4c55-9dd7-1f80c6a38770
   :status: active
   :source: https://github.com/Lysander972/Toolkit/issues/177

   .. code-block:: text

      # 目标

      1. 进一步完善规范本身的严谨性 采用形式化的表达和自然文本表达相结合的设计 提高表达能力和效率
      2. 在现有规范驱动开发,追踪需求和解读设计决策等前提下,让最终规范可以被形式化验证
      3. 避免第二文档来源这种分裂假设
      4. 对于非代码的或者难以改造为BDD的部分则保持原样照常自然语言即可
      5. 预期二者应该混合在一个文档里面 按照AST得到可执行规范的代码块,有就执行测试以验证;同时应精准提供缺陷位置的报告
      6. 必须复用成熟的靠谱库 绝非什么脆弱的特判的正则垃圾


.. interpretation:: #177 实施解读
   :id: 65fd8e61-d5cf-4e75-9e15-e4a88cbced1f
   :status: active
   :implements: 2f6e3db8-9d08-4c55-9dd7-1f80c6a38770, 2315b2d5-df57-41b9-bf2e-937160c30a1b

   出发点：规范正文必须同时承载可执行行为和不能形式化的自然语言约束，且不能产生第二份手工维护的 feature 文件。

   解读角度：第一批只建立真实、可定位的最小执行闭环。Sphinx/docutils AST 负责发现 design 节点中的 `code-block:: gherkin`，官方 Gherkin/Cucumber 工具链负责语法和执行，步骤定义调用现有领域能力。执行输入由 CLI 从正文派生到根 `_build` 独占目录，正文仍是唯一来源。

   推导：成熟解析器和 runner 已经定义语法、中文关键字、Outline、Examples、Doc String 和表格语义，因此不增加正则解析、特判语法或自研 runner。错误必须引用 RST AST 位置、所属需求条目、场景和步骤；文本存在或生成文件不能作为验收。

   结论：当前需求作者层由 `ubproject.toml` 的 `source.include = ["*.rst"]` 确定；在同一个 design 正文中混合自然语言与中文 Gherkin 是设计选择，并非用户指定 Markdown。根 CLI、Vitest、覆盖率和 Stryker 共用同一执行器与步骤注册入口，后续按能力域扩大可执行行为范围。

.. decision:: #177 第一批标准执行闭环
   :id: 7cb1ca4d-3d65-4b7a-9c7b-0bf8a9f2f236
   :status: active
   :implements: 65fd8e61-d5cf-4e75-9e15-e4a88cbced1f

   已有能力：根 CLI 统一运行需求工具，Sphinx-Needs 保存需求正文，Vitest 执行 TypeScript 行为测试，`_build` 提供独占构建目录。

   选择：使用 Sphinx/docutils AST 收集 RST `code-block:: gherkin` 节点，使用官方 `@cucumber/cucumber` 执行标准 Gherkin；执行接入保留为 CLI 的 TypeScript 入口，避免手写语法和第二规范文件。

   候选：Markdown AST 需要在现行 RST 之外再引入作者格式；独立 feature 文件造成第二规范来源；自研解析器和 runner 违背成熟工具优先原则。因此保留 RST 与 Sphinx-Needs 的原生结构，由窄适配器获取 literal_block 及原始行列，由官方 Cucumber 承担执行。代价是 BDD 依赖正式 prepare 已锁定的文档环境；CLI 内部受控调用该环境，不要求 Agent 主动调用 Python。

.. design:: #177 RST Gherkin 执行入口
   :id: 8dd6f4e1-0b9c-4bdf-8f5e-23c7d6a12459
   :status: active
   :implements: 7cb1ca4d-3d65-4b7a-9c7b-0bf8a9f2f236

   在根 CLI 增加 `requirements bdd lint` 与 `requirements bdd run`。命令读取 RST 正文，经 Sphinx/docutils AST 收集 design 节点内语言严格为 `gherkin` 的代码块；`lint` 校验 `# language: zh-CN` 与 Gherkin 语法，`run` 先 lint 再交给官方 Cucumber。解析消息保留文件、需求节点、行列和场景/步骤位置。语法错误、零场景、未定义步骤和断言失败返回非零退出码。

   标记大小写敏感，`python`、`typescript`、`Gherkin`、缺失语言和普通 literal block 不参与 BDD；RST 指令参数错误由 Sphinx 严格构建拒绝。已识别块必须位于 design 中，第一行严格为 `# language: zh-CN`；无匹配块的自然语言规范继续有效。语法错误、零可执行场景和 undefined、ambiguous、pending、skipped、断言失败均使执行失败，不自动纠正、不重试。

   `pnpm requirements bdd lint [HEAD|index]` 和 `pnpm requirements bdd run [HEAD|index]` 默认扫描同一作者层全部 RST。HEAD/index 复用需求 CLI 的 Git tree/blob 隔离、源码导入及结束时输入一致性校验。`requirements:check`、`requirements:verify` 纳入同一 BDD lint；`pnpm test` 中的 Vitest 测试实际执行规范块，coverage 和 Stryker 使用相同测试入口，步骤直接调用真实领域实现。

   唯一可执行块格式如下，块必须位于 `design` 节点正文中：

   .. code-block:: gherkin

      # language: zh-CN
      功能: 工程相对路径校验
        场景大纲: 保留合法路径
          当 校验工程相对路径 "<路径>"
          那么 路径校验成功并返回 "<路径>"
          例子:
            | 路径                 |
            | 地图/123/script.code |
            | .                    |
            | .hidden/code         |
        场景大纲: 拒绝非法路径
          当 校验工程相对路径 "<路径>"
          那么 路径校验失败
          例子:
            | 路径         |
            | /script.code |
            | ../code      |
            |              |
            | ./code       |
            | code/..      |
            | code/.       |
            | code//part   |
            | code/        |
            | C:/code      |
            | a:b          |
            | code\part    |

   执行输入由正文派生到 `_build/requirements/gherkin/<slot>`，成功和失败均释放，不写入源码树。Sphinx-Needs 原生 Need 节点确定 UUID 和 design 类型；标准 CodeBlock 的内容来源映射确定 RST 行列，官方 Gherkin 消息保存场景与步骤 AST 位置。runner 在 Vitest 所在进程执行，以使真实领域代码计入覆盖率并受到变异；失败报告通过官方消息收集器和 Gherkin 位置工具映射到原 RST 文件、需求 UUID、场景及步骤。

   后续实施按能力域进行：先在对应 design 的现行自然语言合同中挑选可执行行为，原地补充 Gherkin；再扩展统一步骤注册入口并调用该域真实 API，建立错误预期与边界反例；随后运行同一 lint、run、Vitest coverage 与 Stryker，确认结果和缺陷定位，再登记实现/验证绑定与检视。难以形式化的段落保留自然语言，不建立平行 feature 来源。

.. verification:: #177 第一批验收
   :id: 4fbd8e76-2c9e-44f3-8b2c-6a7e5d1c9034
   :status: active
   :implements: b8b1d2e3-4f5a-4678-9abc-def012345678, 5438cee7-8ab0-4524-8dc9-24e9a5dacfd8, 01531d5d-6978-4d1d-87eb-6ba4f6ec6999

   验收覆盖 RST `code-block:: gherkin` 收集、中文关键字和多块输入、语法错误位置、缺失步骤、零场景、错误预期失败，以及 CRLF、嵌套 design、表格、Examples 和 Doc String。测试通过真实 CLI 入口执行，不能以源码字符存在、生成文件存在或 mock 调用代替。

.. source:: 当前会话格式与严格校验要求原文
   :id: 2315b2d5-df57-41b9-bf2e-937160c30a1b
   :status: active
   :source: 当前会话用户消息

   .. code-block:: text

      是否明确了具体采用哪种语法让规范固化为可验证的BDD测试? 语言得确定,写在哪一层文件的哪个位置上得确定.错误情况不能被胡乱接受 若代码块出现语法错误等应被识别 且应该精准地识别仅特定语言的代码块被识别而其他语言或者缺失语言标记的块不应该被识别 此外把后续步骤也规划好 进行落实

      我原文哪要Markdown AST的啊? 造谣呢? 评论又不是我手写的! 按照当前的最新需求追踪的设计 文档应该怎么写?什么格式? 怎么解读出代码块是最自然最正常的? 确定下来啊

