需求纠偏追踪
============

.. interpretation:: 缓存归属与任务生命周期
   :id: e5e699ca-f80d-4dc4-82d1-1025530ade58
   :status: active
   :implements: f9746bea-1617-4f82-90f7-43668a1e48ba

   出发点是用户要求消除缓存混乱，并选择将其余项目缓存集中到根 _build、硬中断残留显式回收，以及关闭 Jiti 和 tsx 转译磁盘缓存。从所有权和生命周期推导：每种缓存只有一个权威落点，原工具负责失效与复用；uv、托管 Python、pnpm store 和 ubCode 继续使用已确立的官方布局。Python 环境内字节码是环境的一部分，任务临时输入则必须在正常与异常结束时释放。

   目录集中不能通过复制宿主环境、全局 Temp 重定向或自研缓存层实现。强杀和断电跳过 finally，残留由明确的原生维护命令回收；不引入自动扫描、PID 判定或自研锁协议。验收核对真实命令、落点和资源生命周期，不能用配置字符串代替行为。

.. decision:: 使用原生配置收敛项目缓存
   :id: 420de4b0-7ab8-4154-9a41-ae7174d9c93d
   :status: active
   :implements: e5e699ca-f80d-4dc4-82d1-1025530ade58

   已有 Vite/Vitest cacheDir、Storybook CACHE_DIR 与 builder configLoader、Stryker ignorePatterns 和 cleanTempDir，以及任务 finally 释放。选择这些原生接口，将项目缓存集中至 _build，并从沙箱输入排除宿主 .venv 与 .ub_cache。uv 共享缓存继续支持沙箱内的独立环境，避免复制宿主环境及其已安装字节码。

   Knip 内部创建 Jiti，未公开 fsCache 目录参数；JITI_FS_CACHE 只支持布尔开关。tsx 同样没有原生缓存目录配置。用户明确选择关闭两者磁盘缓存，保留进程内复用，以额外转译开销换取确定的存储归属。拒绝修改第三方实现、创建加载器或重定向整个系统 Temp。持久依赖缓存按需使用原生 prune，不设置任意过期时间或定时清空。

.. design:: 项目缓存与临时资源布局
   :id: d6418ea2-cb52-4ad3-a819-6e46412a639d
   :status: active
   :implements: 420de4b0-7ab8-4154-9a41-ae7174d9c93d

   Vite 前端与预览缓存分别进入 _build/vite/frontend 和 _build/vite/preview，配置相对自身文件定位；Vitest 使用 _build/vitest。Storybook 命令局部设置 CACHE_DIR=_build，其原生版本化缓存进入 _build/storybook。主工作区 Vite/Vitest 与 Storybook builder 使用 native 配置加载，避免 bundle 加载器在 node_modules 创建 .vite-temp。Stryker 的官方 Vitest runner 仍使用原生加载器，其临时文件归属于 _build 内沙箱并随沙箱释放。

   根 tsx 命令使用 --no-cache；通过 tsx API 加载 TypeScript 的 ESLint、Knip、Vitest 和 Stryker 命令设置 TSX_DISABLE_CACHE=1。受控子进程环境固定同一开关，使直接通过 Node loader 启动的子进程遵守该策略；不继承任意外部目录设置。Knip 局部设置 JITI_FS_CACHE=false。这些变量在集中白名单登记，CACHE_DIR 只作用于 Storybook 命令。

   Stryker 不复制 requirements/.venv、requirements/.ub_cache 或外部工具生成的 .gitnexus 索引，使用已有 buildCommand 经冻结安装建立独立环境，并保持 cleanTempDir=always。AST、Cucumber、需求图输入和测试夹具使用任务独占目录；finally 只释放自有资源。真实行为验证覆盖正常、异常、并发隔离、原生构建及完整图读取；修改所涉正文和关系后重新领取正式检视。

缓存维护
--------

uv 缓存、托管 Python 和 pnpm store 采用官方系统位置；requirements/.venv 是安装环境，requirements/.ub_cache 是项目索引。Prettier、ESLint、tsc、Vite、Vitest、Storybook 和 Sphinx 缓存分别归属根 _build 的用途目录。字节码保留在原生 Python 环境或解释器安装目录内，不重定向字节码目录，不逐次清空依赖字节码。

正常和异常结束均释放任务独占输入。强杀、断电残留按需回收，先结束对应任务，再从仓库根执行平台原生命令。下列目标只有可再生的任务临时文件，不包含安装环境、正式证明或持久增量结果。

.. code-block:: powershell

   Remove-Item -LiteralPath './_build/requirements/inputs','./_build/requirements/gherkin','./_build/requirements/tests','./_build/tests','./_build/testing/stryker-sandbox' -Recurse -Force

.. code-block:: sh

   rm -rf -- ./_build/requirements/inputs ./_build/requirements/gherkin ./_build/requirements/tests ./_build/tests ./_build/testing/stryker-sandbox

持久共享缓存按需使用 uv cache prune 或 pnpm store prune；不在 prepare、检查、测试或 hooks 中自动清空。旧版 node_modules/.vite、node_modules/.vite-temp 和 node_modules/.cache/jiti 在配置迁移后按明确路径移除。系统 Temp 中既有 Jiti/tsx 缓存可能被其他工程共享，只有结束其使用者后才按确认的路径移除，不扫描或批量清理整个 Temp。

.. interpretation:: 原生目录与单一配置
   :id: be8e9b84-2ef1-4373-85ab-6b1e91e3d060
   :status: active
   :implements: 808d4bf6-0b1e-4829-9ec1-9bfccd604252

   出发点是用户要求清除垃圾临时文件，并采用官方推荐目录，CLI 只封装已经复现成功的行为。从配置权威与工具生命周期推导：保留 requirements/ubproject.toml 作为唯一配置，移除根目录 extend 转发；uv 使用项目默认 .venv、系统原生缓存与托管解释器目录，ubCode 使用项目原生 .ub_cache。CLI 不再重定向这些目录或建立缓存 Junction。

   此要求替代此前将文档环境及全部第三方缓存集中进 _build 的设计。实际文档构建、独占导入输入及测试产物仍进入根 _build；原生工具维护自己的环境及缓存。清除废弃目录须以实际路径与用途为依据，源码、锁文件和有效检视证明不属于垃圾。验收必须覆盖冷启动、重复并发准备和完整需求图索引，不能只验证小于许可上限的测试图。

.. decision:: 直接使用官方项目布局
   :id: a6123954-72ab-4a29-b79c-7eb40db8d859
   :status: active
   :implements: be8e9b84-2ef1-4373-85ab-6b1e91e3d060

   已有 uv sync --locked、项目默认 .venv、系统原生缓存和托管解释器，以及 ubCode 的项目配置与 .ub_cache。uv 0.11.8 源码的 create_base_files 会在缓存的 sdists-v9 下创建空 .git，以隔离源码包的 Git 元数据探测；ubCode 0.37.0 的开源检测会受仓内该标记影响。移除该标记前完整 333 节点图触发 100 节点许可限制，仅移除后同一入口读取成功。

   候选是每次准备后删除 uv 标记、禁用缓存，或使用工具原生布局。选择最后一项：保留 uv 的缓存语义与增量同步，消除仓内缓存重定向、Junction 和配置转发。代价是环境及第三方缓存遵循官方位置，而不是统一进入 _build。CLI 继续锁定版本与发行摘要，保留失败退出码；不改写第三方缓存结构、许可签名或开源检测逻辑。

.. design:: 原生准备入口与完整图验收
   :id: 1f643127-49d9-4f3a-825c-e550ab7763c7
   :status: active
   :implements: a6123954-72ab-4a29-b79c-7eb40db8d859

   根 pnpm install --frozen-lockfile 的 prepare 和 hooks 调用相同 requirements:prepare。uv sync 使用 requirements 项目、冻结锁文件与托管 CPython 3.13.12；不传环境、缓存或解释器目录变量。Sphinx 与 Gherkin 入口直接读取 requirements/.venv，ubCode 从 requirements 读取唯一配置及原生缓存。根转发 TOML、缓存 Junction 与废弃目录移除；.venv 和 .ub_cache 由 Git 忽略。

   CLI 保留原生帮助和许可 show、config-file、clean 入口，评分登记仍必须使用项目两步审计协议。HostProcess 仅提供原生隐藏启动、受维护环境及退出码传递，不再接收 uv 目录重定向。冷启动及重复并发 prepare 后，实际完整图的 status/context 均须成功读取；严格 Sphinx 构建及原有正文、评分失效、index/HEAD 与事务行为测试继续执行。图中缺失关系和未完成检视由正式 verify 原样报告，索引成功不代表审计通过。

.. interpretation:: 本地技能与可选参考资料的归属
   :id: ecaa6bd2-4e64-4480-ae44-33999eb818e5
   :status: active
   :implements: 8c272f53-5a94-4788-b19f-a8f8943c98dc

   出发点是用户取消本地 Skills 子模块并保留远端独立库。从版本控制与资料使用边界推导，docs/MechToolkit 的现有文件直接纳入主仓库，消费者继续使用原路径，由主仓库提交确定版本。docs/reference 中的资料完全独立于仓库结构，不登记为子模块或构建、测试依赖；存在时可按需查阅并按内部文档活动。用户已删除的测试工程保持删除，现役测试引用转向保留的受维护夹具。

.. decision:: 保留原路径并由主仓库管理技能正文
   :id: d89d0b40-7705-4c7c-a075-0ff077e18a34
   :status: active
   :implements: ecaa6bd2-4e64-4480-ae44-33999eb818e5

   已有 Git 普通文件、docs/MechToolkit 消费路径及 docs/reference 整体忽略规则足够表达要求。选择移除 gitlink 和子模块登记、保留全部技能文件及许可，避免移动消费路径或新增同步层。远端仓库不执行任何写操作。通用 gitlink 检查入口仍可验证本仓库没有子模块，不新增执行器。

.. design:: 同树技能与仓外资料边界
   :id: ce4bc401-637e-47c0-9e2f-a6f2bb423ada
   :status: active
   :implements: d89d0b40-7705-4c7c-a075-0ff077e18a34

   移除 docs/MechToolkit 的 gitlink、.gitmodules 及本地子模块工作树指针，技能正文与许可作为普通文件纳入。C4 以目录登记文件归属，CI 监听目录内容而非 gitlink，取消检出子模块。开发指引和合同明确技能版本随主仓库提交、reference 可选且独立。删除工程残留的两处现役测试路径改为保留工程的脚本，继续验证双链字节往返与源文件只读。

.. implementation:: 技能正文归并与参考边界说明
   :id: 23238e16-5bde-48b4-96c9-f02edba83cf9
   :status: active
   :implements: ce4bc401-637e-47c0-9e2f-a6f2bb423ada

   docs/MechToolkit 保留原提交的 51 个文件及字节；AGENTS、repo-layout、toolkit-runtime、C4 与 CI 使用同树技能合同。docs/reference 继续由 Git 忽略，存在性不影响仓库输入。TS 转换的真实脚本回归使用颜色对对碰的场景设置脚本与显式平台签名，不恢复已删除工程。

.. verification:: 普通文件与真实脚本往返验收
   :id: c9805758-d0e7-4d3d-9e71-85251b817af2
   :status: active
   :implements: 23238e16-5bde-48b4-96c9-f02edba83cf9

   Git index 中技能文件均为普通文件且没有 gitlink；逐文件对象与原子仓库提交一致。正式 gitlink 检查验证空子模块集合；相关 TS 转换和前端安装测试验证真实脚本的双链往返及原路径技能读取。架构检查和需求结构检查分别报告结果，结构通过不替代正式检视意见。

.. interpretation:: 唯一有效解读
   :id: c20c846a-557e-4fe7-8ed0-ba72f195689e
   :status: active
   :implements: 638c682e-d34a-4136-8e59-44ec8e540131

   出发点：原文要求纠正的是需求被断章取义后，错误解读仍能通过形式门禁进入设计的问题。
   解读角度：从原文到产物的每次转换，都需要可核对的完整依据和评分意见。

   推导：保全原文不能阻止授权纠偏，因此允许直接编辑，由 Git diff 留痕。
   唯一有效解读约束排除并行的矛盾结论；UUID 减少后续改号。
   UUID 是稳定条目身份；检视 key 使用原生正文与关联内容指纹，内容变化后旧意见失效。
   解读必须保存角度和推导，决策必须说明必要性与取舍，不能仅登记结论。
   关系两侧全文进入检视上下文；正文或关系变化使旧检视失效。
   各项评分达到阈值可以提交，低于满分的问题保留理由与建议供专项检视。
   已无用途的归档可以集中清除，历史由 Git 保留。

   结论：用同一需求图贯通原文、解读、决策、设计、实现和验证。
   OpenSpec 四工件和强制归档是否保留由其实际价值决定，不能成为额外的重复权威。
   RST、Sphinx-Needs、ubCode 与下述阈值是方案选择；内部 CLI 统一转接是原文明确要求。

.. decision:: 统一需求图
   :id: b616041f-b3d5-4fad-80eb-9de1d4b206ce
   :status: active
   :implements: c20c846a-557e-4fe7-8ed0-ba72f195689e

   已有能力：Git 保存正文变化；LikeC4 是架构权威；旧 SQLite 检视提供源码与架构全文、输入绑定及失效检查，旧 OpenSpec 强制四工件重复描述意图。

   选择：Sphinx-Needs 9.0.0 承载 RST 需求图与关系 schema；Sphinx 9.1.0 构建正式文档；ubCode 0.37.0 提供图索引、影响和 Pharaoh 评分检视。
   三者共用 ubproject.toml 中的类型与关系定义，避免双份配置漂移。
   uv 锁定文档环境；根 pnpm CLI 为唯一操作入口。

   取舍：继续扩展旧系统需要自研关系图和评分状态；继续强制 OpenSpec 产生重复权威。
   因此先接通新系统并验证真实失效行为，再移除旧门禁和实现；旧规范按能力域渐进迁移。
   ubCode 工作流仍为 alpha，源码及 LikeC4 全文提取、Git 输入绑定等既有能力须完成实测对照后再替换。
   原文差异使用标准 Git，关系影响使用原生 agent impact，不自研差异算法。
   ubCode 0.37.0 的 context 在无有效许可、且项目未被检测为公开项目时，最多索引 100 个节点。公开项目使用原生开源识别，完整审计不能通过合并正文、删减覆盖或分片规避限制。环境采用下文原生目录设计，Sphinx 严格构建通过不代表完整检视可用。
   评分采用 0/1/2 离散值，每轴下限为 1；这是可调整的设计选择。

.. design:: 文档需求图闭环
   :id: 96840b50-0dbe-41d9-8805-e7613236017f
   :status: active
   :implements: b616041f-b3d5-4fad-80eb-9de1d4b206ce

   使用 Sphinx-Needs 定义条目和关系，使用 ubCode/ubc 提供索引、影响分析和评分检视，标准 Git 提供原文差异；检视记录必须绑定正文及关联上下文指纹。

   所有下游条目使用原生 implements 关系指向上游，节点类型表达转换环节。
   原生 context 返回相邻节点全文，原生检视指纹覆盖节点正文和相邻依据；不另造上下文提取或指纹算法。
   原文直接修改、关系重定向及评分标准变化均要求重新检视。
   CLI 读取原生意见及同一评分配置，将低于满分的理由和建议输出为警告；是否通过仍完全由原生评分门禁决定。
   源码全文和既有 LikeC4 解析器提取的元素正文，通过标准 needs.json 与 needimport 进入同一图。绑定元数据保存在 bindings.json，实际正文每次由源文件生成。
   本批架构绑定完整 tools/model.c4，覆盖元素外声明的关系与视图；不能只绑定元素正文后遗漏依赖边的变化。
   原生 context 的 related_code 尚未实现，literalinclude 仅将指令文本纳入原生指纹；因此需要此窄适配层。不新增图解析器、指纹或评分状态。
   每次正式 CLI 调用使用独占输入目录及原生 import_keys 覆盖，结束释放目录；并发检查各自读取自己的正文。
   Sphinx 在载入 TOML 后通过 config-inited 接入同一独占导入文件，防止默认导入路径覆盖命令行字典子项。
   正式 verify HEAD 或 verify index 通过原生 Git tree/blob 独立选择全部受审输入；工作树修改不能替代或阻挡 HEAD 判断，检查期间所选 tree 变化直接失败。
   结构、评分标准、正式意见、全部 C4 文件及所绑定源码均纳入该一致性边界，不接受工作树修补替代已提交证明。
   原生 Git 管理合并、重写和意见 JSON 的同树传输。原子领取全文与一次性令牌、事务登记、主体覆盖与快照绑定必须保留；仅替换存储载体不能取消这些审计协议。强制 change/archive 和旧二进制正式账本不再使用。
   TypeScript 官方 AST 保留架构声明存在性和边界检查；独立编码纪律、依赖方向、冻结旧产品及测试指标门禁继续执行。旧 Scenario 聚合预算随旧审计删除，后续能力批次再将所需逻辑合同迁入统一图。

.. needimport:: toolkit_sources

.. interpretation:: 审计协议无损保留
   :id: 359e5305-fd2c-4f0f-b324-365bad6800fc
   :status: active
   :implements: 770b3b75-431d-408a-b906-46897a16db1f

   出发点是原文明确要求的两步审计：领取原文与难以伪造的令牌必须属于同一次操作。图、评分和文档载体的替换不能删除领取、单次消费、批量事务、覆盖、快照隔离及正式证明校验。完整上下文和标准从同一受检输入产生；正文、覆盖或标准改变须重新领取。结构漏项与评分阈值是不同义务，评分建议不能豁免漏绑。旧版完整正文、待审定位、输入变化失效和 Git 生命周期效果都需行为测试，不能以新工具的结构通过代替。

.. decision:: 复用原生图并保留窄审计协议
   :id: ac284ea6-39ad-4231-a09c-fbedf8a1d6bf
   :status: active
   :implements: 359e5305-fd2c-4f0f-b324-365bad6800fc

   ubCode 的 context 和 review-brief 已提供正文、关联与标准指纹，JSON Schema 与原生 verdict-check 已承担评分校验；它没有随机一次性凭据，verdict-submit 允许不带指纹。候选是恢复整套旧治理，或只补原生工具缺少的协议。选择后者：复用 Node SQLite 事务和密码学随机数保管 worktree 私有领取状态，原生 verdict 保存评分，同树 receipt 保存领取、快照与覆盖证明。正式门禁同时验证两者，不恢复旧图解析器、复杂度算法、二进制正式账本或 OpenSpec 工件流程。

.. design:: 原子领取与正式证明
   :id: e0be7742-fd1d-45bd-9c7f-7bb129150adb
   :status: active
   :implements: ac284ea6-39ad-4231-a09c-fbedf8a1d6bf

   requirements review audit 从同一 index 快照取得全文与评分标准，事务签发 256 位随机令牌后才统一输出。record --stdin 逐项校验令牌、HEAD、index、tree、合并父节点、正文、覆盖及标准；完整数组通过 schema 检查后才在同一事务消费令牌并记录待发布意见。重复、跨条目、过期和争用不得登记；批量失败不得部分消费。正式意见和 receipt 由同一待发布记录原子写入各自文件；中断后可通过 publish 重试，证明不完整时门禁保持失败。

   正式 verify HEAD/index 将图、配置、标准、意见及证明从相应 Git tree 载入根 _build 独占目录，源码导入同一 tree；工作树修改不能替代或阻挡 HEAD 判断。ubc 只透传只读查询，裸原生评分不能取得有效领取证明。正文或主体覆盖、关系、标准变化使证明过期；已删除条目的残留证明也阻断。源码与架构覆盖由 C4 范围反向核对；按文件和能力拆分正文，并报告真实审计尺寸。原生 Git 传输可保留匹配证明，失配必须待审；继承只读取当前树、直接父及所属 rebase 输入。

   status 输出首次审计、正文变化、覆盖变化、标准变化及意见失配；receipt 保存主体身份、关系及正文摘要，定位 added/removed/modified，摘要只用于诊断，授权仍绑定原生正文与标准指纹。发布在 SQLite 写事务内串行执行原子文件写入；失败保留待发布记录，重试不会消费新令牌或删除随后登记的意见。pre-rebase 仅保存本次原 HEAD；post-rewrite 先建立环境，校验 Git 提供的旧新 HEAD 映射后执行有限继承。reconcile 不扫描祖先，不自动暂存、改写提交或合并；多份匹配输入的意见冲突保持待审。

   已过期的待发布记录保留并明确提示重新领取，不阻塞其他快照匹配的意见发布。reconcile 移除已从受检图删除的正式意见与领取证明，并移除这些条目的私有凭据；工作树与受检输入不同时拒绝覆盖，历史由 Git 保留。

   Windows 子进程保留系统原生 APPDATA 与 LOCALAPPDATA，分别供包管理器读取用户配置与定位原生缓存；不改写用户目录。根检查保持 pnpm 工作区上下文，避免 --ignore-workspace 使每个并发子检查重复触发依赖安装。

.. verification:: 原生工具与文档配置验收
   :id: d2222e81-edec-451b-b808-fefe95ba4235
   :status: active
   :implements: a4c0ec2e-8724-43fe-8cf9-2f7016237b72, 2e7792a3-8506-4a01-8369-a8e4e743769e, 2bc177e3-b91f-46d2-8dc0-b62ec08b6125, 892a96bf-8815-43bb-8722-114fad25c3b8, c70b9022-0199-4f08-88b5-062f84e6fac9, 3ef95077-25e6-4689-9141-553f3ab23111

   配置验收执行根 TypeScript、ESLint、Knip、Vitest、check:schemas 和 requirements:build 正式入口，读取实际配置并核对退出码；requirements:build 必须在严格警告模式下生成完整 needs.json 与 HTML。pnpm 环境使用冻结锁文件，hooks 行为测试的独占工程同样建立独立工作区和锁文件。Schema 检查读取官方远端字节并与本地镜像及固定摘要比较。配置正文存在或工具能够加载不构成验收通过，检视意见须结合实际行为测试及这些命令的执行结果。

.. interpretation:: 维护者开源文档结构与 PR 格式
   :id: 8a7e4df6-41e9-48ce-9579-dbb25ef48de4
   :status: active
   :implements: 2488f326-090f-4802-a37e-47d57df6bf70

   出发点是公开仓库需要清晰的读者入口，但项目不接受外部贡献，也不提供安全报告或行为准则入口。文档按职责分层：README 负责公开入口，产品指南由同树普通文件提供，requirements 负责需求追踪，openspec 只保留待迁移规范。已无消费者的旧 records 和 archive 不再作为第二套历史权威，删除其架构登记和文件。当前 PR 正文和标题格式混乱，Markdown 使用 CommonMark/GitHub 风格并由锁定版本 Prettier 检查，requirements 继续使用 Sphinx-RST，LikeC4/C4 保持原生语法。PR 只服务维护者内部协作，标题使用 Conventional Commits，正文固定包含变更、验证、追踪和风险四段，由 GitHub Actions 自动拒绝缺失或空章节；不新增外部贡献流程。

.. decision:: 单一公开入口与维护者 PR 门禁
   :id: 0d7ca088-affe-4548-a689-a516a442c978
   :status: active
   :implements: 8a7e4df6-41e9-48ce-9579-dbb25ef48de4

   已有 README、产品文档、requirements 图、OpenSpec 待迁移规范、Prettier、LikeC4 和 GitHub Actions。候选是新增完整外部贡献治理，或只建立维护者入口与 PR 格式门禁；选择后者，避免为纯本地 CLI 引入安全报告、行为准则和外部贡献承诺。旧 OpenSpec records/archive 与 requirements 重复且无现行消费者，删除而不迁移为第二套记录。

.. design:: 文档格式和 PR 校验实现
   :id: 96b330a6-3ebb-422a-a237-952de74fbd1b
   :status: active
   :implements: 0d7ca088-affe-4548-a689-a516a442c978

   根 README 保留项目定位、安装、检查和文档地图；产品正文由 docs/MechToolkit 普通文件提供，版本由本仓库提交确定。新增 `.github/pull_request_template.md`，固定变更、验证、追踪、风险四个章节。新增 PullRequestFormat CLI，使用 Zod 读取 GitHub event JSON，校验 Conventional Commits 标题、四章节顺序和非空内容；ci workflow 的 pull_request tools job 调用同一根 script。格式脚本和 CheckConfiguration 同步检查 README 与 GitHub Markdown，格式回归测试覆盖通过和拒绝边界。LikeC4 模型登记模板、CLI、工作流和测试；requirements 继续由 Sphinx 构建，OpenSpec 仅登记待迁移规范。

.. implementation:: 公开文档结构与 PR 门禁
   :id: 3d79fc70-a490-4d47-855a-afa859515a4b
   :status: active
   :implements: 96b330a6-3ebb-422a-a237-952de74fbd1b

   删除 openspec/records 与 openspec/changes/archive，移除对应架构元素；更新 README、AGENTS、package scripts、格式配置、CI 工作流、模型和测试。PR 模板及校验 CLI 只约束维护者内部 PR，不声明外部贡献支持；不新增 SECURITY、CONTRIBUTING 或 CODE_OF_CONDUCT 文档。

.. verification:: 文档结构和 PR 格式验收
   :id: 6094ad0b-2a4f-4579-b53c-e8567f5e596b
   :status: active
   :implements: 3d79fc70-a490-4d47-855a-afa859515a4b

   验收执行需求结构检查、严格 Sphinx 构建、LikeC4 架构检查、Prettier 格式检查和 PR 格式行为测试。测试验证合法标题与四章节通过，标题不合规、章节缺失、顺序错误和空章节拒绝；外部 null 正文在 schema 边界转为 Option 并拒绝，空格不作为空值。工作流验证 pull_request tools job 调用格式入口。源码实现指向对应 design，文件级测试指向被测 implementation。格式范围核对包含 README 与 GitHub Markdown，与实际 Prettier 脚本一致。旧 OpenSpec 文件删除后不得存在模型或文档引用；HEAD 正式审计读取完整图，仍按实际追踪与检视缺口判定。

.. interpretation:: 公开资料白名单
   :id: ebca3961-c20e-4286-8d60-4e1cb10c468a
   :status: active
   :implements: 170132a3-9ff5-448e-b336-c3940cc8a1b3

   出发点是用户明确要求按白名单确定可公开内容。从读者可独立使用和验证的角度，正文范围为公开接口、签名、文件格式、可观察行为、可公开访问的第三方资料及 Toolkit 自身实现。来源说明遵守同一范围，仓库依赖只描述正式声明的依赖。结论是文档与参考代码均以读者能够独立核对的公开合同为依据。

.. decision:: 公开合同与参考代码边界
   :id: 7ea5f5d9-2d24-4f3e-90fa-17aae0366a9c
   :status: active
   :implements: ebca3961-c20e-4286-8d60-4e1cb10c468a

   必要性是明确文档、SDK 数据与参考代码的职责。已有结构化平台文档承担现役 SDK 数据边界，LikeC4 声明参考区归属和冻结基线。选择保留公开接口与格式参考，现役功能消费结构化文档；参考代码不承担生产数据入口。该方案复用现有合同与架构检查，无需增加数据注册表或治理依赖。

.. design:: 参考区内容与冻结
   :id: b62f6b1f-914f-4009-8b99-b3c8feb97bc9
   :status: active
   :implements: 7ea5f5d9-2d24-4f3e-90fa-17aae0366a9c

   Canvas、持久化、能力解析和诊断以公开接口、格式及行为表达。SDK 合同使用结构化平台数据，Canvas 验收使用平台生成数据与加载行为。仓库说明只登记正式依赖，LikeC4 模型维护参考区文件归属与唯一冻结基线。参考区不安装依赖、不构建、不执行。

.. implementation:: 参考代码与现役合同
   :id: a601bdee-d6f1-499d-8042-8c4ed7d920e3
   :status: active
   :implements: b62f6b1f-914f-4009-8b99-b3c8feb97bc9

   legacy 保存 Canvas 模型、持久化、能力解析、诊断和 SDK 方法定义的参考代码。repo-layout 与 toolkit-runtime 正文绑定现役合同，canvas-render 定义字段验收要求。README 与开发指引描述正式依赖；架构正文由完整模型绑定追踪，参考区冻结规则由模型声明。旧产品及其测试属于参考资料。

.. verification:: 公开合同与参考边界验收
   :id: c607b152-3385-4996-a903-48cc72547c38
   :status: active
   :implements: a601bdee-d6f1-499d-8042-8c4ed7d920e3

   内容检视依据公开接口、格式和可观察行为，核对 SDK 方法签名、参数、返回类型及枚举。冻结规则行为测试验证参考区增删改拒绝等边界，不运行旧产品或旧测试。需求结构、严格文档构建与 index/HEAD 正式检视分别执行，结构检查与评分证明分别报告实际结果。
