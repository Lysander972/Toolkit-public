# canvas-render Specification

## Purpose

定义进程内确定性 CanvasDocument → CPU surface → PNG 管线。HTML/XML 只作为编辑和交换文本，不作为渲染执行载体；聚合后的 `tools/**/*.c4` 是源码与规范范围的唯一登记来源。

用户明确原则是最高权威：原始 Node 保留游戏字段与值类型；HTML 使用 LLM 友好名称；两者通过唯一固定表双射。名称可以不同，信息不得合并或丢失。按钮文本与非按钮文本特性独立，共享显示只接收归一化后的内容参数。解析精确、确定，不默许大小写折叠、trim、别名或自动修正。历史可读格式必须保留；HTML 精度不低于原始 Node，文本采用最短无损表示。旧 C# 仅供定位格式证据，冲突集中记 TODO，不据此改写用户原则。

## Requirements

### Requirement: CPU 渲染边界

渲染入口 MUST 接收结构化 `CanvasDocument`、viewport、资源字节/调色板、固定字体和显式时间，输出 PNG 字节、尺寸、资源指纹与诊断。渲染 MUST 在进程内 CPU 图形后端完成，MUST NOT 创建或读取浏览器 DOM、CSS、WebGL、Pixi renderer、Playwright、Chromium 或跨进程截图。Spine runtime 仅负责解析姿态和网格数据，绘制由 CPU 后端完成。HTML/XML 的解析由转换模块完成后再交给渲染器。

选型固定为根依赖 `canvaskit-wasm@0.42.0`。官方包 README 的 Node 与 ES6 entrypoints、`node.example.js` 的原生 surface 示例以及 `types/index.d.ts` 是依据：`MakeSurface(width, height)` 明确返回 CPU 内存 surface；使用 `getCanvas()` 绘制、`flush()`、`makeImageSnapshot()` 与 `encodeToBytes(backend.ImageFormat.PNG)` 编码。快照以 `delete()` 释放，软件 surface 以 `dispose()` 释放其额外像素内存；surface 拥有的 Canvas 不单独释放。初始化后的 `CanvasKit` 由组合根作为 render 的显式参数注入；渲染器不加载 WASM、不访问文件系统。使用 Skia 原生 Paragraph/字体与图形 API，不使用 Canvas2D 模拟层，其官方 README 明确列出文字 shaping 和对齐限制。

Spine 3.8 的姿态解析选型固定为 `@pixi-spine/runtime-3.8@4.0.6`，图集类型采用 `@pixi-spine/base@4.0.6`，替代旧 `all-3.8` 聚合包。其公开类型提供 SkeletonJson、SkeletonBinary、Skeleton、AnimationState 和附件顶点计算；base/core 提供数学及资源类型，不把 Spine 展示对象、Pixi 场景树或 renderer 用于 CPU 绘制。节点特性只消费姿态与附件网格，由 Skia 绘制纹理。3.8 数据版本是实际资源约束；4.x 的官方 spine-core 不能作为 3.8 数据运行时。该 runtime 采用 Y 向下坐标，真实 Node JSON 解析与父子骨骼坐标验证基础运行时可用；实际图集纹理、动画和网格到 PNG 的接入仍为 TODO，不为声明选型创建假调用。

CanvasKit 官方包以 CommonJS 发布，NodeNext 将其默认导入解释为模块类型，而 README 的运行时入口是初始化函数。选型测试在这个外部导入边界以 Zod 确认函数后使用具名初始化签名；不猜测第二条导入路径，也不读取 `.default` 属性兼容不同形态。

`CanvasRenderInput` MUST 包含唯一领域模型 `CanvasDocument`、正整数 viewport、带 GUID/工程相对路径/种类/revision/字节的资源集合、只读调色板、带名称/revision/字节的固定字体以及有限 `timeSeconds`。资源索引以 GUID 唯一，字体以名称唯一；调用方保证 revision 与该次字节一致。资源指纹 MUST 覆盖实际资源和字体字节、标识、revision 与调色板内容，不能仅凭 revision 判定相同。viewport、文档与时间仍是确定性输入，不被资源指纹代替。渲染诊断携带 code、message、节点索引链以及显式 Option 包装的资源 GUID 和工程相对路径。结果交付独立 PNG 字节、实际尺寸、资源指纹和只读诊断集合。

必要性：现有 Canvas 四向转换负责持久化格式，Zod 负责严格输入，Skia 负责光栅、字体度量和编码；它们不解释游戏锚点、排版、字段启用及富文本方言。项目代码只承担这些平台语义到原生 API 的映射和每次请求的资源所有权。以 Node 中真实软件 surface 的 PNG 编码/解码像素验证后端可用性；文档到 PNG 的行为由渲染测试验证。当前文件级空壳明确 TODO 失败，旧 DOM 实现和直接耦合测试作为 `legacy/canvas_render/` 只读参考；选型验证不能冒充业务渲染完成。

结果 MUST 同时携带本次布局的只读节点集合 `nodes`，供插件树形视图与选中高亮消费。每项保留节点索引链、名称、类型及最终矩形经全部祖先和自身变换后的四角，坐标相对于 PNG viewport；集合按文档深度优先原顺序输出，索引链表达父子关系，重名节点仍能区分。根 Canvas 由视图文档表达，不重复登记为八种 UI 节点。几何由同次 CPU 布局结果投影，不交付可变布局树、surface 或字体对象。PNG 与节点集合原子交付，展示端只缩放坐标，不重新求锚点、排版或旋转。

必要性与取舍：现有节点索引链表达身份和层级，同次布局已经取得最终几何，Webview 原生树与 SVG 覆盖层承担展示和高亮。仅交付 PNG 会丢失选择所需的信息；复制领域模型或在前端再次布局会增加第二份权威。结果只补充名称、类型、身份和四角；选择是界面状态，不进入 PNG 或持久化文档。

#### Scenario: 同一输入确定性输出

- **WHEN** 使用相同文档、资源字节、字体、viewport 和显式时间渲染两次
- **THEN** 输出尺寸、PNG 像素、资源指纹和诊断一致

<!-- @anchor id=canvas-render-cpu-boundary -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-cpu-boundary level=M -->

### Requirement: CPU 渲染行为验收

现役基线 MUST 将后端选型与业务入口分开验证：软件 surface 的 PNG 经实际解码后比较尺寸和独立预期 RGBA，两个独占 surface 的同一绘制输入产生相同编码；Spine 3.8 JSON 解析后按独立坐标预期验证父子姿态；未实现的业务入口 MUST 以带 `TODO:` 原因的 Error 拒绝交付 PNG，并保持调用方文档与请求输入不变。保真验证覆盖空文档以及带实际资源字节、固定字体、非空调色板与显式时间的非空文档，重复失败仍与调用前独立快照及文档序列化字节一致，捕获集合清空、字节改写、标识或时间被重置。空壳失败检查不锁定完整错误文案。共享的 CanvasKit 初始化结果只在该 Vitest 测试文件内使用，每次绘制独占 surface、Paint 和快照，成功与失败均释放；测试不写 PNG 文件或修改语料。

内部阶段端口 MUST 直接验证 TODO 失败和输入保真，覆盖字段合并、布局、诊断、节点调度、绘制、图片合成、编码与释放。测试使用真实 CanvasDocument、节点 Schema 和独占软件 surface，按公开结构合同组装会话输入，其方法通过原生 call 在该输入上执行现役原型方法；不实现替代渲染或资源结算。失败后独立快照和预置 surface 像素保持一致，捕获输入修改、提前绘制和伪成功；真实 surface 由测试 finally 释放。现有创建入口仍明确失败，不为获得测试实例改变生产生命周期。

文档到 PNG 的业务测试 MUST 经 `CanvasRenderer.render` 取得实际结果。预期像素、矩形、诊断和资源指纹变化依据由以下行为确定，不以被测实现生成期望值。当前业务保持空壳，以下目标不注册为跳过或伪通过的测试；正式实现时建立相应可执行证据。

| 行为目标 | 可观察结果与独立预期 | 必须捕获的代表性错误 |
| --- | --- | --- |
| 空 Canvas 与确定性 | 空文档输出 viewport 尺寸的有效 PNG；同一完整输入的像素、资源指纹和诊断一致；不同请求互不污染 | 拒绝合法空布局、忽略 viewport、读取隐式时间或残留前次状态 |
| 布局与子排版 | 依据锚点、伸展、当前位置优先级以及 Grid/Vertical/Horizontal 规则计算预期矩形；不同颜色子节点的实际像素定位与预期一致 | 历史位置覆盖当前位置、父尺寸过期、Grid 下对齐方向错误、绘制反向改变布局 |
| 八种节点与能力顺序 | 全部节点的白名单能力分别产生预期可见效果；重叠区域、遮罩边界及滚动指示按固定顺序合成 | 漏节点能力、错序、按钮与普通文本混用、把 Fill 用于 ScrollRect |
| 树形视图与选中几何 | 实际结果保留嵌套顺序、重名节点的不同索引链；平移、父子旋转及伸展后的四角与独立坐标预期一致，并对应同次 PNG 中的节点 | 名称充当唯一身份、漏子节点、遗漏祖先变换、几何来自前次请求、泄漏可变布局或图形资源 |
| 字段有效值与输入保真 | 启用且缺失的字段使用该节点已确认 DEFAULT，显式输入保留，禁用字段保持缺失；成功和失败后原文档序列化字节不变 | 输入被补值、其他节点默认值串入、禁用字段重新启用、持久化字段被绘制写回 |
| 固定字体与富文本 | 固定字体的 Skia 排版结果决定换行、行高、对齐、字距和自动字号；显式富文本字号保持独立，实际文本像素落在预期区域 | 使用环境字体、缺字静默替代、度量与绘制字体不一致、显式字号被自动字号改写 |
| 图像、九宫格与合成 | 使用小型真实图片和独立颜色预期验证角点、边缘、中心、透明度、颜色乘法、渐变及软遮罩 | 错误 GUID、九宫格边缘伸缩错误、alpha 合成错误、遮罩影响兄弟节点 |
| Spine 显式时间 | 实际 3.8 骨骼/图集/纹理在指定时间生成独立预期姿态与 PNG；姿态边界决定缩放居中 | 动画忽略时间、网格纹理错配、坐标方向错误、复用前次动画状态 |
| 资源指纹与失败诊断 | 分别改变实际资源字节、字体字节或调色板使指纹变化；缺失/损坏资源明确失败并定位节点、GUID 和工程相对路径 | 只按 revision 计算指纹、返回替代图片、漏依赖、泄漏绝对路径、诊断丢失资源身份 |
| 资源结算与并发请求 | 成功、资源失败及绘制失败均结算并释放本次资源，其他独立请求继续得到正确 PNG；绘制和释放同时失败保留两个原因 | 失败后资源未释放、提前释放仍在使用的资源、跨请求释放、后续完成覆盖失败 |

必要性：Vitest 提供隔离、生命周期及断言，CanvasKit 提供真实 PNG 编解码和软件绘制，Spine runtime 提供姿态解析，现有 CanvasConverter 提供文档输入与二进制保真比较。测试只组装小型明确输入并核对独立预期，不增加测试调度、渲染替身或自研图片解析器。原始 DOM 测试留在只读参考区；选型基线通过不证明以上业务目标已经实现。覆盖率、变异测试与指标棘轮仍按手写规划归阶段三。

#### Scenario: 选型与空壳基线分离

- **WHEN** 在 Node 中运行现役 CPU 渲染测试
- **THEN** 分别验证真实软件 surface 的尺寸、像素与重复编码，Spine JSON 的父子姿态，以及业务空壳明确失败且不修改请求输入；业务渲染缺口保持显式标记

<!-- @anchor id=canvas-render-test-baseline -->
<!-- @architecture id=canvas.renderer_tests -->
<!-- @architecture id=canvas.renderer_support -->
<!-- @budget id=canvas-render-test-baseline level=S -->

#### Scenario: 内部阶段空壳保真

- **WHEN** 在独占软件 surface 上直接调用内部阶段端口
- **THEN** 字段合并、布局、诊断、调度、绘制、图片合成、编码与释放明确 TODO 失败；节点、默认值、完整输入及预置蓝色像素保持独立预期，finally 释放测试资源

选型与入口验收、内部阶段验收分别使用内聚的测试文件，共用唯一 CanvasKit 初始化入口。复用阶段参数类型避免另建合同，分拆保持每类验收的原生依赖和资源生命周期清晰。

<!-- @anchor id=canvas-render-stage-baseline -->
<!-- @architecture id=canvas.renderer_tests -->
<!-- @architecture id=canvas.renderer_support -->
<!-- @budget id=canvas-render-stage-baseline level=S -->

### Requirement: 渲染模块与数据流

渲染引擎的模块划分、文件归属与依赖关系唯一以聚合架构模型为准。

CPU 文件级边界采用显式输入与独占会话，具体归属由 C4 定义。第二阶段只建立粗略合同和文件级空壳，公开入口直接明确 TODO 失败，不提前创建会话或执行阶段编排。后续正式实现采用校验、布局、绘制、编码与释放的单向顺序；下表表达目标行为，不构成本阶段的执行要求。必要性与取舍：CanvasKit 承担 CPU 绘制与编码，项目阶段边界只隔离平台语义和请求资源；保留骨架足以推演数据流，提前编排增加未验证的执行代码且不能交付业务结果，应删除。

节点调度、文档校验、有效值、布局、图像与文字投影、软件 surface 和会话端口均保留带实际输入的唯一声明，执行保持 TODO 明确失败；不借递归遍历或空结果模拟业务。同步字段计算与异步资源端口分别保留原生返回类型，临时布局和图形资源只归本次会话，缓存仍归 Preview。阶段二核对完整文档、viewport、资源、字体、调色板与显式时间的输入输出，不要求最终特性方法全集；后续逐特性以既定白名单和独立像素预期验收。

特性 Scenario 在本阶段锚定对应 Fields、节点 Schema 和阶段入口，表达输入边界与目标行为；锚定存在不表示目标绘制已实现。转换语法与字段映射锚定实际转换模块，渲染输入的二进制依赖校验锚定文档校验端口，视图读写锚定视图服务。阶段边界保留唯一合同，已有端口由直接失败验收消费；不通过额外公开入口或假编排制造可达性。取舍是保留可检视的文件级职责与真实失败证据，业务效果仍由后续独立预期的像素、布局和诊断验收。

| 阶段       | 类与方法                                                                                                         | 输入                                   | 输出与写入权限                                                       |
| ---------- | ---------------------------------------------------------------------------------------------------------------- | -------------------------------------- | -------------------------------------------------------------------- |
| 属性解析 | `CanvasDocumentValidator.validate` | CanvasDocument 节点 | 严格文档校验；缺失字段保留，不填 DEFAULT |
| 能力调度 | `CanvasNodeDispatch.draw` | 已完成布局的节点与会话 | 按下表顺序投影能力，不计算布局、不改持久化字段 |
| 统一取值 | `CanvasFieldValues.resolve` | 当前节点 Schema 输入、唯一 DEFAULT、显式启用条件 | effective 值；启用且缺失时补值，输入和 DEFAULT 不变 |
| 布局与子排版 | `CpuCanvasLayout.layout` | 文档、viewport、会话字体 | 完整布局树；矩形与 resolvedLayoutSize 同次确定，排版参数只在布局计算内持有 |
| CPU 绘制 | `CpuCanvasNodes.draw`、`CanvasImageComposition.draw` | effective 值、布局与资源 | 向 CPU surface 绘制，不写持久化字段 |
| 阶段驱动 | `CanvasRenderer.render` | 完整渲染输入与后端 | 校验、布局、递归绘制、编码；父最终尺寸沿递归方向传递 |
| 资源完成 | `CpuRenderSession.finish`、`CpuRenderSession.close` | 已登记任务与资源字节 | 资源结算与释放，交付 PNG 结果；不修改文档 |
| 失败诊断 | `CanvasRenderDiagnostic` | 失败输入、节点身份与计算结果 | 返回诊断，不纠正布局或补值 |

| 节点         | 固定能力顺序（均为对应 Renderer；ChildLayout 在布局阶段紧随 Layout 执行） |
| ------------ | ------------------------------------------------------------ |
| `UIRect`     | Layout → ChildLayout → Box → Fill → Mask → ShadowOutline     |
| `UIButton`   | Layout → Box → ShadowOutline → Button                        |
| `UILabel`    | Layout → TextStyle → Label                                   |
| `TextPro`    | Layout → TextStyle → TextEffect                              |
| `UIInput`    | Layout → Box → ShadowOutline → TextStyle → Input             |
| `UIRawImage` | Layout → RawImage → ShadowOutline                            |
| `UISpine`    | Layout → Spine → ShadowOutline                               |
| `ScrollRect` | Layout → ChildLayout → Box → Mask → ShadowOutline → Scroll   |

`Fields` MUST 定义持久化属性，文档 Schema 按节点组合契约，每种节点持有唯一默认值，特性投影负责启用规则和能力应用。按钮文本与非按钮文本字段独立；共享显示函数只接收内容值。布局 MUST 在 CPU 绘制前完成父到子最终矩形计算；`layout` 与 `resolvedLayoutSize` 为唯一布局事实。

#### Scenario: 模块与具体实现对应

- **WHEN** 检查渲染架构
- **THEN** 使用根与模块 C4 的统一模型，类和方法对应实际实现，源码依赖、能力调用顺序、字段写入者与上述数据流一致

<!-- @anchor id=canvas-render-module-flow -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-module-flow level=M -->

#### Scenario: 属性输入与 effective 隔离

- **WHEN** 特性按启用白名单合并输入和 DEFAULT
- **THEN** 只在独立 effective 值表中补全启用字段，持久化输入、节点 DEFAULT 与其他特性的字段保持不变

<!-- @anchor id=canvas-render-effective-values -->
<!-- @architecture id=renderer.fields -->
<!-- @budget id=canvas-render-effective-values level=M -->

### Requirement: 确定类型的 CanvasDocument Schema 提取

文档输入校验 MUST 使用 Zod 严格对象 Schema 分别定义根与八种 UI 节点；CanvasDocument 为唯一输入。必选 name 存在，viewport 的 width/height 显式提供；可选持久化字段保留 Option.none，不在提取阶段填默认值。XML/HTML 文本由转换模块解析，渲染器不读取 DOM。

Canvas 字段、标签、枚举和数值 MUST 使用明确语法，不提供大小写折叠、空白清洗、字段别名、过滤未知字段或自动修复。历史字段只按唯一映射表处理，不增加猜测格式的路径。

Canvas 历史字段的读写方向、Node 名称和 HTML 名称只以“HTML 字段映射”Requirement 的唯一表及其判定规则为准；其他 Requirement 不得重复声明字段映射或另行定义别名。

未知属性、属于其他节点类型的属性、大小写不一致的属性或枚举值 MUST 立即报错。转换层按 XML 标签原名和唯一字段表校验，不接受 `class`、`style`、`id` 或任意 `data-*` 作为持久化属性。渲染器只消费确定类型文档，不解析标签拼写。

值类型、数值精度、布尔编码与枚举语法 MUST 遵循唯一映射表及其数值规则；缺失可选属性 MUST 为 `Option.none`。节点 GUID 是持久化标识，不作为渲染能力输入；资源 GUID 不改写为资源路径。

#### Scenario: 从未知属性组合提取节点

- **WHEN** 结构化节点进入渲染管线
- **THEN** 按节点专属严格 Schema 提取，缺少必选属性、非法值、未知属性或大小写不一致立即报错；缺失可选属性保留 `Option.none`，节点只接收确定类型对象

<!-- @anchor id=canvas-render-dom-schema -->
<!-- @architecture id=renderer.dom_schema -->
<!-- @budget id=canvas-render-dom-schema level=L -->

### Requirement: 异步资源状态与失败诊断

资源任务 MUST 在启动前登记，会话等待已启动任务结算，并在成功和失败路径释放 CPU surface、字体、解码图片及 Spine 数据。失败保留原始原因，绘制与释放同时失败时使用 AggregateError；后续完成不得覆盖失败。缺失或损坏资源明确失败，不生成替代图片。会话终态不提供重置入口，测试使用独立会话。

#### Scenario: 异步任务完成与失败

- **WHEN** 同一会话的资源任务陆续完成，或渲染失败
- **THEN** 每个任务只释放一次登记，无任务且无失败时完成；失败原因进入完成 Promise，失败状态不会被后续完成覆盖

<!-- @anchor id=canvas-render-async-state -->
<!-- @architecture id=renderer.state -->
<!-- @budget id=canvas-render-async-state level=M -->

#### Scenario: 按需读取布局失败

- **WHEN** 布局计算记录非正尺寸，宿主读取诊断
- **THEN** 返回失败输入、计算结果和节点及祖先身份，不重渲染、不修改字段；无失败时显式返回 `Option.none`

<!-- @anchor id=canvas-render-layout-diagnostics -->
<!-- @architecture id=renderer.layout -->
<!-- @budget id=canvas-render-layout-diagnostics level=M -->

### Requirement: 层级职责

节点 MUST 保留原版二进制持久化字段；运行时布局与绘制结果由会话拥有。转换层按唯一 HTML 字段表双向映射；特性 Renderer 将有效值转为 CPU 绘制操作，不修改持久化字段、不增加 fallback。

#### Scenario: 持久化数据与绘制隔离

- **WHEN** 渲染节点
- **THEN** 持久化字段保持原表示，布局结果只存在会话，绘制不引入另一套持久化字段

<!-- @anchor id=canvas-render-layer-responsibilities -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-layer-responsibilities level=M -->

### Requirement: 节点继承与特性接口

节点使用已有 `CanvasNode` 持久化模型和 `CanvasLayoutNode` 只读布局结果，不重新建立 DOM 节点继承树。特性接口 MUST 不存在继承关系，使用英文 PascalCase 命名并以 `Fields` 结尾，遵循以下白名单：

| 接口                  | 节点类型                                                                          |
| --------------------- | --------------------------------------------------------------------------------- |
| `TextContentFields`   | `UILabel`, `TextPro`, `UIInput`                                                   |
| `LayoutFields`        | 全部节点                                                                          |
| `BoxFields`           | `UIRect`, `UIButton`, `ScrollRect`；`UIInput` 仅 `color` |
| `FillFields`          | `UIRect`                                                                          |
| `MaskFields`          | `UIRect`, `ScrollRect`                                                            |
| `ChildLayoutFields`   | `UIRect`, `ScrollRect`                                                            |
| `ShadowOutlineFields` | 全部节点 |
| `ButtonFields`        | `UIButton`                                                                        |
| `TextStyleFields`     | `UIButton`, `UILabel`, `TextPro`；`UIInput` 仅 `fontSize`、`textAlignment` |
| `ScrollFields`        | `ScrollRect`                                                                      |
| `InputFields`         | `UIInput`                                                                         |
| `LabelFields`         | `UILabel`                                                                         |
| `TextEffectFields`    | `TextPro`                                                                         |
| `RawImageFields`      | `UIRawImage`                                                                      |
| `SpineFields`         | `UISpine`                                                                         |

节点特性 MUST 消费确定 Schema 的只读输入，DEFAULT MUST 使用同一节点 Schema 类型；共享字段只定义一次，不再复制为节点实例属性。HTML 字段白名单 MUST 对齐本规范唯一映射表，不得通过组合接口扩大节点字段；参考 `CanvasHtmlTagSchema` 不得成为第二份权威。字段保留与视觉消费 MUST 区分：游戏支持读取的字段须完整保留，渲染效果以已确认的语义为准，不因保留字段推测新增效果。没有确认默认值的保留字段 DEFAULT MUST 为 `Option.none`。

富文本 MUST 由文本渲染共同处理，不引入没有持久化字段依据的 `richTextEnabled` 属性。`TextContentFields` MUST 只表达文本内容；富文本解析能力不得冒充独立持久化字段组。

#### Scenario: 节点组合

- **WHEN** 定义节点类型
- **THEN** 持久化与布局结果分离，特性输入由该节点 Schema 的 Fields 组合确定，不使用中文特性标识符或 `Nullable` 类型

<!-- @anchor id=canvas-render-node-interfaces -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-node-interfaces level=L -->

### Requirement: 特性渲染器边界

每项特性操作 MUST 唯一对应一个 `Fields` 接口，可按节点白名单接收该接口的 `Pick` 子集。CPU 节点投影与图片合成采用静态入口，状态由请求会话提供；单例不承担会话或节点状态。能力内部不能合并多个特性接口、使用 `T extends FieldsA & FieldsB` 组合契约或以纯转发代替自身操作。以下 Box、Label 等特性名称限定行为及字段边界，不要求保留旧 DOM 类名。

TextPro MUST 在 TextEffect 特性内将 `outlineWidth` 与 `faceDilate` 映射为 CPU 字形绘制，不引入独立持久化接口。

阴影与描边特性 MUST 按 Unity Shadow/Outline 语义绘制。纹理图形按 alpha 轮廓绘制偏移阴影及描边，矩形按形状绘制；仅影响当前图形，不影响文字和子节点。合成使用 Skia 原生能力，不自造逐像素光栅器。

#### Scenario: 每个特性唯一渲染器

- **WHEN** 节点调用特性渲染
- **THEN** 每次调用唯一对应一个白名单接口的渲染器，由该渲染器直接执行特性操作

<!-- @anchor id=canvas-render-renderer-boundaries -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-renderer-boundaries level=M -->

### Requirement: 运行时字段白名单

运行时状态 MUST 仅限下表。布局阶段完成后输出完整 `CanvasLayoutNode`，缺失状态只存在计算期间，以 `Option<T>` 包装，不以半成品布局进入绘制：

| 字段                 | 类型                          | 写入方                | 读取方                                                                       |
| -------------------- | ----------------------------- | --------------------- | ---------------------------------------------------------------------------- |
| surface | `CpuRasterSurface` | 渲染会话 | 节点特性绘制 |
| `layout` | `LayoutRect` | CPU 布局阶段 | 节点绘制、子节点布局 |
| 子排版参数 | 布局算法内的确定类型参数 | CPU 布局阶段 | 同次父到子排版 |
| `resolvedLayoutSize` | `Vector3` | CPU 布局阶段，与 layout 同次产出 | 自适应字号测量 |

布局树 MUST 只返回只读运行时快照，同时交付矩形和尺寸。节点类型、名称和会话引用为调度上下文；CPU surface 由会话唯一拥有，节点不得释放或保存为持久化字段。

#### Scenario: 运行时状态访问

- **WHEN** 重新计算子节点排版
- **THEN** 子节点布局只从父最终尺寸与规则字段计算，绘制结果不反向参与布局

<!-- @anchor id=canvas-render-runtime-fields -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-runtime-fields level=M -->

### Requirement: 节点渲染顺序

`CanvasNodeDispatch.draw` MUST 按节点类型选择规定的特性顺序，计算、有效值合并与绘制由对应阶段负责。

#### Scenario: 声明式节点渲染

- **WHEN** 执行 `CanvasNodeDispatch.draw`
- **THEN** 只执行固定能力调用，计算、CPU 绘制与有效值合并由对应阶段负责

<!-- @anchor id=canvas-render-node-render-order -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-node-render-order level=L -->

### Requirement: 白名单字段合并

每个特性操作 MUST 显式确定字段启用条件，使用 `CanvasFieldValues.resolve` 合并输入与当前节点 DEFAULT，并只消费确定类型的 effective 值。MUST NOT 生成字符串字段表、动态枚举属性或通过断言伪造类型。白名单内的 `Option.some` 输入 MUST 保留，`Option.none` 输入 MUST 使用节点 DEFAULT；未启用字段 MUST 为 `Option.none`，不得被 DEFAULT 重新启用。渲染 MUST 显式解包 `Option`。DEFAULT 只按节点 Schema 定义，特性操作 MUST NOT 定义自己的 DEFAULT。

fallback 填充 MUST 由统一字段合并层负责，默认值 MUST 按当前节点类型选取唯一 DEFAULT，并包含该类型全部持久化字段的默认值。调用特性时 MUST 显式传入该节点 DEFAULT；同一特性作用于不同节点类型时，MUST 使用各自节点类型的默认值，MUST NOT 使用其他节点的 DEFAULT 或额外的特性默认表。

统一填充层 MUST 只为白名单启用且为 `Option.none` 的字段填入已确认原版默认值，保留已有输入与未启用字段。特性只消费合并结果，不在读取、布局或 CPU 绘制追加默认值；资源缺失明确失败。

布局输入同时存在当前位置与历史位置时，显示 MUST 优先使用当前位置。只有历史位置时，渲染器 MUST 根据父锚点应用历史坐标语义。这是渲染取值规则，不是字段合并：输入中两字段的存在性和值 MUST 保持，转换不得据此丢弃历史字段。

#### Scenario: 启用与禁用字段

- **WHEN** 渲染器合并节点输入
- **THEN** 统一填充层只启用该渲染器白名单中的字段，保留已提供的值；缺失的启用字段使用当前节点类型显式传入的 DEFAULT，其他字段保持 `Option.none`，渲染器读取合并结果时不再补值

<!-- @anchor id=canvas-render-whitelist-merge -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-whitelist-merge level=M -->

### Requirement: 图片 GUID 解析

调用方 MUST 通过实体 GUID 索引读取实际字节，作为同次 CanvasRenderInput.resources 提供。渲染器只在该输入集合按 GUID 取用资源，不访问实体服务或工程 IO。未命中、解码失败或格式不受支持 MUST 报错，不生成替代图片。

#### Scenario: 图片资源缺失

- **WHEN** 白名单图片 GUID 无法解析
- **THEN** 返回带 GUID 和节点身份的资源错误，不输出伪成功图片

<!-- @anchor id=canvas-render-guid-resolution -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-guid-resolution level=S -->

### Requirement: 唯一渲染与入口

每次请求 MUST 通过唯一入口创建独立 RenderSession；会话只启动一次并在结束时释放。Host 显式提供完整输入，不依赖页面自动启动。文档或资源变化产生新请求，不重置旧会话。渲染器不生成 HTML，也不承担写回校验。

#### Scenario: 单次渲染生命周期

- **WHEN** Host 提供完整输入调用渲染入口
- **THEN** 独立会话完成一次 PNG 生产并释放，重复启动同一会话报错

<!-- @anchor id=canvas-render-single-entry -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-single-entry level=L -->

### Requirement: 三阶段 CPU 管线

管线 MUST 先计算直属子节点布局，再按父排版规则确定最终矩形，随后递归处理下一层。每个节点源布局只计算一次，Grid 修改矩形时同时更新 resolvedLayoutSize。最后深度优先按原顺序绘制到 CPU surface；绘制结果不得回流布局。

空 Canvas MUST 保留根预览尺寸并正常完成；不得以没有子组件为由拒绝合法布局。UILabel 显示裁剪 MUST 使用同次 Skia 文本排版结果，缺失时暴露内部契约错误，不静默跳过。

Grid MUST 按 `childAlignment` 对齐；LowerLeft、LowerCenter、LowerRight MUST 从下向上排列。父级启用自动排版时，子级 ToEdge 边距模式 MUST 被禁止，避免尺寸与排版循环依赖；ABSOLUTE、PERCENT 尺寸仍按布局字段计算。

渲染能力 MUST 包含锚点、伸展、颜色、固定字体、圆角、描边、九宫格、文本对齐、行高、字距、GUID 图片、颜色乘法及软遮罩。混色与遮罩使用 Skia 原生合成能力，不逐像素读回。

#### Scenario: 父到子布局与特性投影

- **WHEN** 渲染父节点及其子节点
- **THEN** 父节点布局先于子节点计算，Grid 应用固定单元格尺寸与坐标，Vertical 和 Horizontal 只调整坐标；布局完成后按固定能力顺序投影父节点及其子节点

<!-- @anchor id=canvas-render-dom-pipeline -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-dom-pipeline level=M -->

### Requirement: 资源构建与 schema 声明

非代码资源以 `res/` 为唯一源点；固定字体来自 `res/fonts/`。组合根加载字体和图形后端并注入会话，渲染输入不依赖 CSS 或页面字体加载。Spine 3.8 runtime 仅解析骨骼、动画与网格，CanvasKit/Skia 软件 surface 承担绘制和字体度量。

根 `build:canvas-render` 以 Node 平台打包唯一入口为 `_build/canvas_render/CanvasRenderer.mjs`，外部依赖保留标准包引用。固定字体和 WASM 的加载归组合根；业务空壳构建成功只证明 ESM 入口可产出，不证明渲染行为。展示层只消费 HTML/XML 文本与 PNG；独立 HTML 的本地打开与预览网页的手动打开遵循 preview 展示合同，CPU ESM 不作为页面引擎。构建与依赖版本由根 package.json、pnpm-lock.yaml 管理。

`pnpm check:types` MUST 统一检查渲染源码与 `CanvasViewSchema.ts`。`pnpm build:canvas-render-types` MUST 继承统一 TypeScript 配置，仅向 `_build/canvas_render/types/` 输出声明；使用者入口为 `_build/canvas_render/types/src/canvas_render/CanvasViewSchema.d.ts`，MUST NOT 在源码目录生成声明。

`CanvasViewSchema.d.ts` MUST 是用于使用者编写文件的 schema，明确列出完整 UI 节点白名单（`UIRect`、`UIButton`、`UILabel`、`TextPro`、`UIInput`、`UIRawImage`、`UISpine`、`ScrollRect`），并为每个节点列出支持的持久化属性及其 `Option<T>` 类型。MUST NOT 暴露运行时字段、渲染器内部实现或不完整的推导索引。`CanvasUISchemaOf<Type>` MUST 选择对应节点 schema，`CanvasUISchema` MUST 覆盖完整 `UIComponentType` 集合。

#### Scenario: schema 与声明输出

- **WHEN** 增加或删除 UI 节点类型
- **THEN** 生成声明明确告知使用者可以编写哪些 UI 节点和持久化属性；完整 `UIComponentType` 索引不一致时，类型检查或声明生成失败

<!-- @anchor id=canvas-render-schema-dts -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-schema-dts level=M -->

#### Scenario: 发布渲染静态支持

- **WHEN** 构建并发布预览支持
- **THEN** 固定字体从 `res/` 唯一源点提供，Host 加载 CPU 后端，展示层消费同次生成的 PNG

<!-- @anchor id=canvas-render-resource-build -->
<!-- @architecture id=renderer -->
<!-- @architecture id=resources -->
<!-- @budget id=canvas-render-resource-build level=M -->

### Requirement: 落盘分支的渲染产物边界

落盘分支 MUST 只写出视图与预览所需的派生产物；渲染引擎本身 MUST 由根构建入口产出，
不得把渲染 TypeScript 源码或旧版 `CanvasLayoutEngine` 源码直接写入运行时工程。使用者编写的
Canvas 文件只消费 `_build/canvas_render/types/src/canvas_render/CanvasViewSchema.d.ts` 提供的持久化
schema；预览执行只消费 `_build/canvas_render/CanvasRenderer.mjs` 的可运行 Node ESM 入口。

旧版源码直写、浏览器 IIFE 和 `CanvasLayoutEngine.DO_NOT_READ_OR_EDIT.generated.js` 仅属于 `legacy/`
参考，不构成新版落盘合同。新版构建产物统一进入顶层 `_build/canvas_render/`，工作区 files 分支不
建立第二份渲染引擎源码、声明索引或浏览器运行时。业务渲染尚未实现时，ESM 入口与声明构建只能证明
产物边界，调用必须以明确 TODO 失败，不得用空 PNG 或可执行假脚本冒充完成。

#### Scenario: 落盘渲染产物与旧版隔离

- **WHEN** 构建渲染支持并在 files 分支交付 Canvas 视图
- **THEN** 使用者得到独立 schema 声明，预览得到可运行 Node ESM 入口，工作区不写入旧版渲染源码或
  浏览器引擎；业务入口未接通时明确失败且请求保持不变

<!-- @anchor id=canvas-render-persistence-engine-convergence -->
<!-- @architecture id=renderer -->
<!-- @architecture id=toolkit.view_service -->
<!-- @architecture id=canvas.renderer_tests -->
<!-- @budget id=canvas-render-persistence-engine-convergence level=M -->

#### Scenario: 已登记的渲染器文件

- **WHEN** 架构检查枚举 Canvas 渲染器
- **THEN** 实现与规范范围由架构模型登记，引用只指向唯一 OpenSpec 规范

<!-- @anchor id=canvas-render-source-layout -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-source-layout level=M -->

### Requirement: 受控 Canvas 视图与隔离

Canvas HTML MUST 与 BeScript TypeScript 视图隔离。

视图编辑 MUST 只修改布局结构，不提供通用 HTML/JavaScript 编辑、智能推断或布局自动修正。渲染源码的统一 TypeScript 检查与使用者 HTML 文件的结构校验 MUST 区分；用户 schema 声明 MUST NOT 作为 HTML 页面运行时脚本加载。

#### Scenario: 视图隔离

- **WHEN** 读写 Canvas 视图
- **THEN** 按 Canvas HTML 处理，不作为 TypeScript 视图或渲染源码处理，路径保持相对于运行时视图工作区

<!-- @anchor id=canvas-render-view-isolation -->
<!-- @architecture id=toolkit.view_service -->
<!-- @budget id=canvas-render-view-isolation level=M -->

### Requirement: 统一预览资源映射

资源映射与调色板 MUST 在实体读取边界校验，作为当前渲染的只读输入快照；GUID 与颜色查询不重复解析整张表。文档 Schema 输出从实际 Zod 形状推导，不复制字段集合。

资源诊断 MUST 携带文档节点身份、GUID 和工程相对路径，不以 HTML data 属性传递内部状态。

预览 MUST 只有一条 CPU 渲染链。Preview 从实体层按 GUID 取得实际依赖字节与调色板，输入不携带 URL、绝对路径或页面全局变量。图片格式白名单为 `.jpg`、`.jpeg`、`.png`、`.webp`；MIME 为 `image/jpeg`、`image/png`、`image/webp`。Spine 的 atlas、骨骼及纹理作为同一依赖集合提供。展示站点和 Webview 只消费 PNG。

#### Scenario: 预览资源查找

- **WHEN** 渲染文档引用图片或 Spine GUID
- **THEN** 按实体 GUID 索引取实际字节，未命中或解码失败明确报错

<!-- @anchor id=canvas-render-guid-preview-modes -->
<!-- @architecture id=renderer -->
<!-- @architecture id=frontend.plugin -->
<!-- @budget id=canvas-render-guid-preview-modes level=M -->

### Requirement: 原版字段证据

原始字段 MUST 由游戏导出/加载证据核对，类型参考 `Vars.cs` 与 `CanvasNodeSchema`。字段名称、节点归属和中间层映射只在下方唯一映射表定义，不另立字段归属矩阵。

#### Scenario: 原版字段归属

- **WHEN** 定义 Canvas 持久化节点字段
- **THEN** 字段名称、节点归属与类型符合原版导出证据，不增加没有依据的字段

<!-- @anchor id=canvas-render-binary-field-evidence -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-binary-field-evidence level=L -->

### Requirement: HTML 字段映射（唯一 canonical 表）

HTML 属性 MUST 全小写。新版 TypeScript 转换、文档校验和内联 HTML 使用同一份带节点类型上下文的映射。旧 C# 只提供字段证据，冲突不构成第二份规范，不维护旧版适配层。

公共布局、文本与控件、资源与特效三张分表共同构成唯一字段映射表。每行直接列出适用节点、原始 Node 字段、渲染输入字段和 HTML 属性，不通过字段列表的位置配对。适用节点列只限定字段归属，不允许运行时按节点类型猜测字段来源。

映射锚点 MUST 直接关联实际节点字段声明和共用名称表，不能以表装配调用替代字段定义。名称表保持唯一；共用名称变化触发所有直接消费该表的映射检视。

原始 Node 与游戏格式一致；渲染输入是明确的中间层，允许使用友好名称。三列名称不要求相同，但每个字段只能有一个对应项。当前位置与历史位置分别映射为 `pos` 与 `legacyPos`，不得折叠。

`label` 与 `text` 分别属于按钮文本与非按钮文本特性；HTML 两侧也明确区分。各自 schema 解析后，将内容值以第三者参数 `content` 传入已有共享显示函数，函数不识别字段来源。

表中的字段类型参考 `CanvasNodeSchema` 与 `Vars.cs` 的声明；游戏字段归属必须核对原版导出和加载代码，不能只由工具旧 Schema 自证。游戏停止导出但仍可读取的字段不能因导出分支没有该字段而删除。证据不足或旧实现不一致项集中登记在 TODO 中，不用旧实现反向修改用户原则。

| 适用节点 | 原始 Node 字段 | 渲染输入字段 | HTML 属性 | 原版类型 | 方向/状态 |
| --- | --- | --- | --- | --- | --- |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `name` | `name` | `name` | String | 双向 |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `guid` | `guid` | `guid` | Long | 双向 |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `Pos` | `pos` | `pos` | Vector3 | 双向 |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `pos` | `legacyPos` | `legacypos` | Vector3 | 历史；保留读取、展示与往返 |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `size` | `size` | `size` | Vector3 | 双向 |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `rot` | `rot` | `rot` | Float | 双向 |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `parentAnchor` | `parentAnchor` | `parentanchor` | Long | 双向 |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `anchor` | `anchor` | `anchor` | Long | 双向 |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `widthExtend` | `widthExtend` | `widthextend` | Long | 双向 |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `heightExtend` | `heightExtend` | `heightextend` | Long | 双向 |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `color` | `color` | `color` | Color/String | 双向 |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `shadowColor` | `shadowColor` | `shadowcolor` | Color/String | 双向 |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `shadewDistance` | `shadewDistance` | `shadowdistance` | Vector3 | 双向 |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `outlineColor` | `outlineColor` | `outlinecolor` | Color/String | 双向 |
| UIRect、UIButton、UILabel、TextPro、UIInput、UIRawImage、UISpine、ScrollRect | `outlineDistance` | `outlineDistance` | `outlinedistance` | Vector3 | 双向 |
| UIRect、UIButton、ScrollRect | `BorderWidth` | `borderWidth` | `borderwidth` | Float | 双向 |
| UIRect、UIButton、ScrollRect | `Radius` | `radius` | `radius` | Float | 双向 |
| UIRect、ScrollRect | `mask` | `mask` | `mask` | Bool | 双向 |
| UIRect | `fillMethod` | `fillMethod` | `fillmethod` | Long | 双向 |
| UIRect | `fillAmount` | `fillAmount` | `fillamount` | Float | 双向 |
| UIRect、ScrollRect | `cellSize` | `cellSize` | `cellsize` | Vector3 | 双向 |
| UIRect、ScrollRect | `spacing` | `layoutSpacing` | `layoutspacing` | Vector3 | 双向 |
| UIRect、ScrollRect | `padding` | `layoutPadding` | `layoutpadding` | Vector3 | 双向 |
| UIRect、ScrollRect | `verticle_spacing` | `verticalSpacing` | `verticalspacing` | Float | 双向 |
| UIRect、ScrollRect | `horizontal_spacing` | `horizontalSpacing` | `horizontalspacing` | Float | 双向 |
| UIRect、ScrollRect | `childAlignment` | `childAlignment` | `childalignment` | Long | 双向 |
| UIRect、ScrollRect | `childExpendWidth` | `childExpandWidth` | `childexpandwidth` | Bool | 双向 |
| UIRect、ScrollRect | `childExpendHeight` | `childExpandHeight` | `childexpandheight` | Bool | 双向 |
| UIRect、UIButton、ScrollRect | `slicedPicGuid` | `slicedPicGuid` | `slicedpicguid` | String | 双向 |
| UIRect、UIButton、ScrollRect | `slicedBorderLB` | `slicedBorderLB` | `slicedborderlb` | Vector3 | 双向 |
| UIRect、UIButton、ScrollRect | `slicedBorderRT` | `slicedBorderRT` | `slicedborderrt` | Vector3 | 双向 |
| UIRect、UIButton、ScrollRect | `slicedRectLB` | `slicedRectLB` | `slicedrectlb` | Vector3 | 双向 |
| UIRect、UIButton、ScrollRect | `slicedRectRT` | `slicedRectRT` | `slicedrectrt` | Vector3 | 双向 |
#### Scenario: 公共布局字段映射

- **WHEN** 读取或写出公共身份、位置、布局与图片边框字段
- **THEN** 各列按节点归属一一对应，当前位置与历史位置独立保留

<!-- @anchor id=canvas-render-binary-field-mapping -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-render-binary-field-mapping level=M -->

### Requirement: Canvas 文本与控件字段映射

以下表格与公共布局、资源特效表共同构成唯一 canonical 映射，各表按原始字段划分且不重复。节点归属和方向保持逐行约束。

| 适用节点 | 原始 Node 字段 | 渲染输入字段 | HTML 属性 | 原版类型 | 方向/状态 |
| --- | --- | --- | --- | --- | --- |
| UIButton | `label` | `label` | `label` | String | 双向 |
| UILabel、TextPro、UIInput | `text` | `text` | `text` | String | 双向 |
| UIButton、UILabel、TextPro、UIInput | `FontSize` | `fontSize` | `fontsize` | Float | 双向 |
| UIButton、UILabel、TextPro、UIInput | `Alignment` | `textAlignment` | `alignment` | Long | 双向 |
| UIButton、UILabel、TextPro | `lineSpacing` | `lineSpacing` | `linespacing` | Float | 双向 |
| UIButton、UILabel、TextPro | `characterSpacing` | `characterSpacing` | `characterspacing` | Float | 双向 |
| UIButton、UILabel、TextPro | `fontStyle` | `fontStyle` | `fontstyle` | Long | 双向 |
| UIButton、UILabel、TextPro | `lightFont` | `lightFont` | `lightfont` | Bool | 双向 |
| UILabel、TextPro | `boolBestFit` | `boolBestFit` | `boolbestfit` | Bool | 双向 |
| UILabel、TextPro | `outlineWidth` | `outlineWidth` | `outlinewidth` | Float | 双向 |
| UIButton | `clickCBName` | `clickCBName` | `clickcbname` | String | 双向 |
| UIButton | `pressCBName` | `pressCBName` | `presscbname` | String | 双向 |
| UIButton | `releaseCBName` | `releaseCBName` | `releasecbname` | String | 双向 |
| UIInput | `multiLine` | `multiLine` | `multiline` | Bool | 双向 |
| UIInput | `contentType` | `contentType` | `contenttype` | Long | 双向 |
| UIInput | `labelColor` | `labelColor` | `textcolor` | Color/String | 双向 |
#### Scenario: 文本与控件映射

- **WHEN** 读写按钮文本、非按钮文本、文字样式与控件回调
- **THEN** label 和 text 保持独立，字段值和适用节点与表一致

<!-- @anchor id=canvas-text-field-mapping -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-text-field-mapping level=M -->

### Requirement: Canvas 资源与特效字段映射

以下表格是唯一 canonical 映射中的资源与 TextPro 特效分区；资源 GUID、节点 Long 身份和显示效果保持各自类型。

| 适用节点 | 原始 Node 字段 | 渲染输入字段 | HTML 属性 | 原版类型 | 方向/状态 |
| --- | --- | --- | --- | --- | --- |
| UIRawImage | `fileGuid` | `fileGuid` | `fileguid` | String | 双向 |
| UIRawImage | `softMaskGuid` | `softMaskGuid` | `softmaskguid` | String | 双向 |
| UISpine | `atlasGuid` | `atlasGuid` | `atlasguid` | String | 双向 |
| UISpine | `skelGuid` | `skelGuid` | `skelguid` | String | 双向 |
| UISpine | `jsonGuid` | `jsonGuid` | `jsonguid` | String | 双向 |
| UISpine | `pngGuids` | `pngGuids` | `pngguids` | `List<String>` | 双向 |
| UISpine | `animIndex` | `animIndex` | `animindex` | Long | 双向 |
| ScrollRect | `scrollHorizontal` | `scrollHorizontalDisabled` | `scrollhorizontaldisabled` | Bool | 双向 |
| ScrollRect | `scrollVertical` | `scrollVerticalDisabled` | `scrollverticaldisabled` | Bool | 双向 |
| TextPro | `FaceDilate` | `faceDilate` | `facedilate` | Float | 双向 |
| TextPro | `bUNDERLAY` | `underlayEnabled` | `underlayenabled` | Bool | 双向 |
| TextPro | `UnderlayColor` | `underlayColor` | `underlaycolor` | Color/String | 双向 |
| TextPro | `UnderlayOffset` | `underlayOffset` | `underlayoffset` | Vector3 | 双向 |
| TextPro | `UnderlaySoftness` | `underlaySoftness` | `underlaysoftness` | Float | 双向 |
| TextPro | `bGLOW` | `glowEnabled` | `glowenabled` | Bool | 双向 |
| TextPro | `GlowColor` | `glowColor` | `glowcolor` | Color/String | 双向 |
| TextPro | `GlowOffset` | `glowOffset` | `glowoffset` | Float | 双向 |
| TextPro | `GlowInner` | `glowInner` | `glowinner` | Float | 双向 |
| TextPro | `GlowOuter` | `glowOuter` | `glowouter` | Float | 双向 |
| TextPro | `GlowPower` | `glowPower` | `glowpower` | Float | 双向 |
| TextPro | `enableVertexGradient` | `vertexGradientEnabled` | `vertexgradientenabled` | Bool | 双向 |
| TextPro | `topLeft` | `gradientTopLeft` | `gradienttopleft` | Color/String | 双向 |
| TextPro | `topRight` | `gradientTopRight` | `gradienttopright` | Color/String | 双向 |
| TextPro | `bottomLeft` | `gradientBottomLeft` | `gradientbottomleft` | Color/String | 双向 |
| TextPro | `bottomRight` | `gradientBottomRight` | `gradientbottomright` | Color/String | 双向 |
| TextPro | `bBEVEL` | `bevelEnabled` | `bevelenabled` | Bool | 双向 |
| TextPro | `SpecularColor` | `specularColor` | `specularcolor` | Color/String | 双向 |
| TextPro | `LightAngle` | `lightAngle` | `lightangle` | Float | 双向 |
| TextPro | `SpecularPower` | `specularPower` | `specularpower` | Float | 双向 |
| TextPro | `Reflectivity` | `reflectivity` | `reflectivity` | Float | 双向 |
| TextPro | `Diffuse` | `diffuse` | `diffuse` | Float | 双向 |
| TextPro | `Ambient` | `ambient` | `ambient` | Float | 双向 |
| TextPro | `Bevel` | `bevel` | `bevel` | Float | 双向 |
| TextPro | `BevelOffset` | `bevelOffset` | `beveloffset` | Float | 双向 |
| TextPro | `BevelWidth` | `bevelWidth` | `bevelwidth` | Float | 双向 |
| TextPro | `BevelRoundness` | `bevelRoundness` | `bevelroundness` | Float | 双向 |
| TextPro | `BevelClamp` | `bevelClamp` | `bevelclamp` | Float | 双向 |

#### Scenario: 资源与特效映射

- **WHEN** 读写 RawImage、Spine、滚动与 TextPro 特效字段
- **THEN** 保留每个字段的类型、存在性与节点归属

<!-- @anchor id=canvas-resource-field-mapping -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-resource-field-mapping level=M -->

### Requirement: Canvas 映射与无损往返约束

**映射判定规则**

HTML 字段映射表是本规范唯一的 Node↔HTML 映射来源。原始 Node 字段保留游戏名称；中间层使用表中渲染输入名称；HTML 使用表中友好名称。每个节点内各列必须一一对应，禁止把不同字段映射到同一属性、别名猜测或按节点类型选择源字段的写入特判；大小写不同的原始字段不得折叠。

历史字段 MUST 保留读取和展示能力，MUST NOT 静默丢弃。游戏已淘汰的新写入与既有数据的保留、展示、往返是不同职责；禁止从游戏停止生成某字段推导出视图也应删除该字段。各 Schema 与实现按唯一映射表消费字段，其他 Requirement 不另立名称映射。

**集中纠偏 TODO**

旧 C# 文件是原版格式证据，但不是对映射原则的无条件豁免。发现旧代码违反本规则时 MUST 保留证据并登记 TODO，不得将违规复制到新版：

- TODO：旧 `CanvasHtmlTagSchema.TextCommon` 把 UIButton 的 HTML `text` 映射到 Node `label`，混淆了独立文本特性。新版 MUST 使用 `label ↔ label` 与 `text ↔ text` 的独立 schema；旧 C# 不修改。
- TODO-CANVAS-CONVERSION：`legacy/core/canvas/schema/CanvasHtmlTagSchema.cs` 与 `legacy/core/canvas/htmlview/CanvasHtmlViewCodec.cs` 是参考代码，不能充当新版四向转换和内联 HTML 已完成的证明。新版转换必须消费本表，使用真实原始字段值验证各方向及内联片段往返；不维护旧 C#。
- TODO-CANVAS-PRECISION：CPU 绘制不证明 Float、Vector、Long、颜色的序列化无损；正式转换必须验证原始类型范围、位精度、历史位置与最短无损文本，不能以渲染通过替代往返验证。
- TODO-CANVAS-RESOURCES：组合根从 res/ 固定字体和根依赖加载 CPU 后端，产物进入顶层 _build；旧 C# 嵌入 CSS 与拼接浏览器脚本不迁入新版。
- TODO-CANVAS-GAME-EVIDENCE：本仓参考 `CanvasNodeSchema` 对 UIButton、UILabel、TextPro 共用文字格式字段，字段实际归属仍须以平台生成的数据与加载行为核对，区分当前写入与历史可读字段；验收依据为公开格式、平台生成数据及可观察的加载行为。

TODO 只记录可定位的旧代码冲突、责任和收敛目标，不记录审计过程或阶段流水账。

**数值精度与文本表示**

HTML 数值精度 MUST 大于或等于 Node 原始类型精度，并满足 `decode(encode(nodeValue)) = nodeValue`。序列化 MUST 使用最短的无损表示，不得用固定小数位制造冗余文本；解析 MUST 拒绝会改变原始类型或造成隐式截断的表示。

- Long MUST 保持原始有符号 64 位整数值，禁止小数截断或经过不能精确承载该值的 JavaScript number 再写回；枚举值属于 Long 的明确有限域，不能据此缩小其他 Long 字段的范围。
- Float 和 Vector 分量 MUST 使用可恢复原始 float32 位值的最短表示；Vector 的三个分量均保留，二维渲染使用方式不得删除第三分量。普通值不得因一次读写被扩展为固定的长小数；正负零也不得无依据合并。
- RGBA 通道 MUST 先尝试输出整数通道值；仅当 `整数通道 / 255` 无法按位恢复原始通道时，才输出足够精确的浮点值。
- `1/255` 及其他非整除颜色值 MUST 以读回后的原始浮点位一致作为验收条件，而不是只比较显示文本。
- 允许去除冗余表示，不允许改变 Node 值或通过格式化丢失尾数。

数值 MUST 使用 JSON 十进制数值语法，支持负号、小数与科学计数法，拒绝首尾空白、十六进制、显式正号、缺少整数部分或小数部分的表示及非数值 JSON。解析使用原生 JSON.parse 与 Zod；XML 数值、向量、颜色分量、viewport 和富文本字号复用同一语法，不清洗后接收。

Canvas 节点 `guid` 是 Long 整数标识；图片、Spine 等资源 GUID 是 32 位字符串，两者 MUST 使用各自类型，不相互转换。渲染端以 JavaScript number 承载节点整数时 MUST 校验安全整数，拒绝静默舍入；完整 Long 的持久化往返要求仍遵循上述精度契约。

Bool 视图编码为 0 或 1；枚举使用精确名称，不大小写折叠。String 空串与缺失不同，不清洗内容。颜色保留原始 Color 或调色板引用；CPU 像素通道量化不参与持久化序列化，不将调色板引用写回颜色常量。

四向转换 MUST 以原始 Node 为中心，分别处理独立二进制、游戏内联格式、独立 HTML 文档与 HTML 内联片段。独立与内联 HTML 的节点属性、层级、顺序和映射相同，仅外壳不同。根预览宽高是展示元数据，不伪造对应的游戏节点字段；节点类型及子节点顺序通过固定标签和嵌套结构表达。结构化解析和输入校验在边界完成，转换不添加业务纠错、渲染默认值或按节点类型选择源字段的旁路。

验收 MUST 使用包含各节点字段、缺失与空串、当前与历史位置、Long 边界、float32 分量及非整数颜色的实际值，验证每个转换方向和两种 HTML 外壳读回后字段存在性、值类型、层级、顺序与原始浮点位一致。HTML 中按钮字段和非按钮字段各自接受且拒绝交叉；显示优先级不得改变往返结果。仅检查类型相等、源码字符或渲染截图不能替代该证据。

#### Scenario: 字段映射保持

- **WHEN** 持久化 Canvas 字段转换为 HTML 并读回
- **THEN** 规定的全小写属性映射回同一二进制字段，不新增字段或扩大节点字段归属

<!-- @anchor id=canvas-mapping-roundtrip -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-mapping-roundtrip level=L -->

### Requirement: HTML 属性格式边界

CanvasHtml 使用 CanvasTags 的唯一字段映射读写 XML 属性；标准 XML 解析器承担属性语法，转换层保留存在性、值类型与顺序，拒绝未知属性。

#### Scenario: HTML 属性读取与写出

- **WHEN** 读取或写出节点属性
- **THEN** 使用唯一字段表、保持字段存在性与原始值类型

<!-- @anchor id=canvas-html-attributes -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-html-attributes level=M -->

### Requirement: 持久化类型与颜色契约

节点持久化输入 MUST 使用 CanvasUISchema，枚举、向量与颜色采用统一确定类型。颜色按 RGBA、调色板及富文本语义解析；调色板未命中明确失败。

#### Scenario: 持久化字段契约

- **WHEN** 使用者编写 Canvas HTML 节点
- **THEN** 标签及属性属于规定白名单，全小写属性映射到持久化字段，不完整的依赖组被拒绝

<!-- @anchor id=canvas-render-field-contract -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-field-contract level=L -->

### Requirement: 二进制依赖组

原版导出端同一条件块中捆绑写出的字段 MUST 完整并存；原版加载端直接读取依赖字段，MUST NOT 用默认值掩盖不完整数据。历史字段的读写方向见“HTML 字段映射”表，依赖组本身不得重新定义字段名称或映射方向。

| 触发字段                                  | 必然并存字段                                                       | 适用节点                                       |
| ----------------------------------------- | ------------------------------------------------------------------ | ---------------------------------------------- |
| `shadowColor`                             | `shadewDistance`                                                   | 所有支持阴影的节点                             |
| `outlineColor`                            | `outlineDistance`                                                  | 所有支持描边的节点                             |
| `fillMethod`                              | `fillAmount`                                                       | UIRect                                         |
| `cellSize`                                | `spacing`、`padding`                                               | UIRect / ScrollRect（Grid 排版）               |
| `verticle_spacing`                        | `padding`                                                          | UIRect / ScrollRect（Vertical 排版）           |
| `horizontal_spacing`                      | `padding`                                                          | UIRect / ScrollRect（Horizontal 排版）         |
| `slicedPicGuid`                           | `slicedBorderLB`、`slicedBorderRT`、`slicedRectLB`、`slicedRectRT` | UIRect / UIButton / ScrollRect（九宫格五件套） |
| `childAlignment`                          | `cellSize` / `verticle_spacing` / `horizontal_spacing`（任一）     | UIRect / ScrollRect（启用排版时）              |
| `childExpendWidth` 或 `childExpendHeight` | `verticle_spacing` 或 `horizontal_spacing`                         | UIRect / ScrollRect（仅 Vertical/Horizontal 排版） |

> 对称关系（`shadowColor`↔`shadewDistance`、`outlineColor`↔`outlineDistance`、`fillMethod`↔`fillAmount`、`cellSize`↔`spacing`、排版模式↔`padding`）双向成立：任一字段单独出现即违规。`childAlignment`、`childExpendWidth/Height` 为单向触发依赖（存在时要求排版字段存在，反之不要求）。

ScrollRect 与 UIRect 共享 ChildLayoutFields，适用相同的排版依赖组；FillFields 仍仅属于 UIRect。依据为本规范字段映射表、现役 ScrollRectSchema，以及冻结参考 CanvasNodeSchema.LayoutProps 与 CanvasLayoutValidator.ValidateLayoutFieldShape。此处固定输入语义，CanvasDocumentValidator 的实际校验执行仍按阶段二保持 TODO。

互斥（同时至多一个）：

| 互斥组       | 字段                                                                                   |
| ------------ | -------------------------------------------------------------------------------------- |
| 自动排版类型 | `cellSize`（Grid）/ `verticle_spacing`（Vertical）/ `horizontal_spacing`（Horizontal） |

条件可选（缺失不致失败）：

以下字段导出受默认值门槛控制（与默认值不同才导出），加载端以存在性判断或默认值兜底，缺失不会导致加载失败：`parentAnchor`、`anchor`、`widthExtend`、`heightExtend`、`rot`、`childAlignment`、`mask`。

#### Scenario: 二进制依赖校验

- **WHEN** 出现阴影、描边、填充、排版或九宫格字段组
- **THEN** 必须并存的字段完整出现，互斥排版组被拒绝，只有规定的可选默认字段允许省略

<!-- @anchor id=canvas-render-binary-dependencies -->
<!-- @architecture id=renderer.dom_schema -->
<!-- @architecture id=legacy.core.lint -->
<!-- @budget id=canvas-render-binary-dependencies level=M -->

### Requirement: Canvas XML 语法

Canvas HTML MUST 由转换层结构化 XML 解析；只有 BeCanvas 子树参与语义，html/head 和展示图片属于交付外壳。标签名区分大小写，不以浏览器 HTML 容错、正则或子串提取替代解析器；序列化只写入实际持久化字段。

| 节点       | 标签                 |
| ---------- | -------------------- |
| Canvas     | `BeCanvas`           |
| UIRect     | `BeUIRect`           |
| UIButton   | `BeUIButton`         |
| UILabel    | `BeUILabel`          |
| TextPro    | `BeUILabelTextPro`   |
| UIInput    | `BeUIInput`          |
| UIRawImage | `BeUIRawImage`       |
| UISpine    | `BeUISpine`          |
| ScrollRect | `BeUIRectScrollRect` |

向量 MUST 使用逗号分隔的数值分量；颜色 MUST 使用通道范围为 0–255 的 `rgba(r,g,b,a)` 或 `palette(name)`；枚举 MUST 使用规定枚举域。`layoutType` MUST 从布局字段推导，MUST NOT 序列化为二进制字段。

#### Scenario: 受控 XML 输入

- **WHEN** 解析视图
- **THEN** 只接受白名单标签和属性，名称精确匹配，值类型符合字段契约，不通过解析容错改变请求布局

<!-- @anchor id=canvas-render-xml-grammar -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-render-xml-grammar level=M -->

### Requirement: 特性应用与专属能力

各能力 MUST 消费自身启用白名单的 effective 值与显式运行时上下文，MUST NOT 修改持久化属性、节点 DEFAULT 或其他特性的输入。能力调用顺序 MUST 使用本规范的节点顺序表。

#### Scenario: 节点特性投影

- **WHEN** 直接驱动节点的布局和特性渲染
- **THEN** 输入与 DEFAULT 保持只读，以真实 PNG 像素、布局结果和诊断验证；每个测试使用独立会话

<!-- @anchor id=canvas-render-feature-projection -->
<!-- @architecture id=renderer.nodes -->
<!-- @architecture id=renderer.features -->
<!-- @budget id=canvas-render-feature-projection level=M -->

#### Scenario: 布局计算

- **WHEN** 调用 `CpuCanvasLayout.layout`
- **THEN** 按父级尺寸、锚点、伸展方式与位置优先级计算 layout 和 resolvedLayoutSize，非法尺寸记录诊断后报错

<!-- @anchor id=canvas-render-layout -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-layout level=L -->

#### Scenario: 方框与九宫格

- **WHEN** 绘制方框特性
- **THEN** 应用颜色、圆角、边框；九宫格启用时禁用边框厚度，异步纹理结果只写图形层

方框颜色绘制 MUST 使用 CPU 图形后端；UIInput 只消费原版颜色字段，不填造边框、圆角或九宫格输入。项目负责字段白名单、默认值及原版映射，后端负责绘制；以输入框实际像素与额外字段拒绝验证该边界。

<!-- @anchor id=canvas-render-box -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-box level=M -->

#### Scenario: 矩形填充

- **WHEN** 绘制矩形填充特性
- **THEN** 只作用于 UIRect，按 fillMethod 与 fillAmount 应用裁剪

<!-- @anchor id=canvas-render-fill -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-fill level=M -->

#### Scenario: 裁剪遮罩

- **WHEN** 绘制裁剪遮罩特性
- **THEN** 按 mask 控制子元素裁剪与对应样式

<!-- @anchor id=canvas-render-mask -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-mask level=M -->

#### Scenario: 子布局参数

- **WHEN** 布局阶段处理子排版特性
- **THEN** 按明确提供的 Grid、Vertical 或 Horizontal 字段计算子布局参数，仅在同次布局计算内持有；未启用排版时不产生子排版参数

<!-- @anchor id=canvas-render-child-layout -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-child-layout level=M -->

#### Scenario: 阴影与描边

- **WHEN** 绘制阴影与描边特性
- **THEN** CPU 后端按矩形形状或图片 alpha 轮廓绘制阴影与描边，仅影响当前图形

<!-- @anchor id=canvas-render-shadow -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-shadow level=M -->

#### Scenario: 按钮文字与回调

- **WHEN** 绘制按钮特性
- **THEN** 消费按钮文本、颜色与三个回调名，按 resolvedLayoutSize 测量按钮文字

<!-- @anchor id=canvas-render-button -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-button level=M -->

#### Scenario: 共享文字样式

- **WHEN** 应用共享文字样式
- **THEN** 应用基础字号与对齐，节点专用文本能力消费行距与字体样式

TODO-CANVAS-CHARACTER-SPACING：`characterSpacing` 在字段、文档 Schema 与 DEFAULT 中保留；CPU 度量和绘制必须消费同一字距，随 CPU 后端实现。

<!-- @anchor id=canvas-render-text-style -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-text-style level=M -->

#### Scenario: 输入框能力

- **WHEN** 绘制输入框特性
- **THEN** 应用内容类型、多行、文本颜色并渲染节点文本

<!-- @anchor id=canvas-render-input -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-input level=M -->

#### Scenario: 常规文本能力

- **WHEN** 绘制常规文本特性
- **THEN** 应用文本、字体选择、自动字号、颜色与描边，文本测量使用已经计算的 resolvedLayoutSize

<!-- @anchor id=canvas-render-label -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-label level=M -->

#### Scenario: 特效文本能力

- **WHEN** 绘制特效文本特性
- **THEN** 应用文本、描边、顶点渐变、Underlay、Glow 与 Bevel，颜色与布局不反写持久化字段

<!-- @anchor id=canvas-render-text-effect -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-text-effect level=L -->

#### Scenario: 特效文本字段取值

- **WHEN** 特效文本进入渲染
- **THEN** 从输入与唯一 DEFAULT 解析确定类型的 TextEffectFields，不修改输入；CPU 绘制消费该结果

<!-- @anchor id=canvas-render-text-effect-values -->
<!-- @architecture id=renderer.fields -->
<!-- @architecture id=renderer.features -->
<!-- @budget id=canvas-render-text-effect-values level=M -->

#### Scenario: 四角渐变

- **WHEN** 生成四角渐变图像
- **THEN** Skia 原生 shader 表达四角双线性渐变，角点、中点和透明度符合独立预期，不自造逐像素光栅器

<!-- @anchor id=canvas-render-gradient -->
<!-- @architecture id=renderer.images -->
<!-- @budget id=canvas-render-gradient level=M -->

#### Scenario: 原始图像能力

- **WHEN** 绘制原始图像特性
- **THEN** 取得实际纹理，再以 color 正片叠底并应用软遮罩；资源错误直接失败

<!-- @anchor id=canvas-render-raw-image -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-raw-image level=M -->

#### Scenario: Spine 资源能力

- **WHEN** 绘制 Spine 特性
- **THEN** 按 GUID 读取 atlas、骨骼与纹理，Spine 3.8 runtime 在显式时间生成姿态与网格，CPU 后端绘制；按姿态边界缩放居中，非正高度报错，会话结束释放资源

<!-- @anchor id=canvas-render-spine -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-spine level=M -->

#### Scenario: 滚动指示能力

- **WHEN** 绘制滚动指示特性
- **THEN** 按水平和垂直禁用字段显示滚动指示，不包含填充能力

<!-- @anchor id=canvas-render-scroll -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-scroll level=M -->

#### Scenario: 图像合成

- **WHEN** 调用 `CanvasImageComposition.draw`
- **THEN** 按节点尺寸生成 CPU 图像层，使用 Skia 原生合成处理颜色乘法与软遮罩，不通过像素读回混色

<!-- @anchor id=canvas-render-image-composition -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-image-composition level=M -->

#### Scenario: 异步资源收束

- **WHEN** 图片或 Spine 资源任务启动
- **THEN** 任务由单次渲染状态登记并在成功、警告或失败后幂等释放；完成状态不依赖生成图层是否提交

<!-- @anchor id=canvas-render-resource-settlement -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-resource-settlement level=M -->

#### Scenario: 文本测量

- **WHEN** 调用 `RichTextParser`
- **THEN** 每次字号搜索只解析富文本一次；候选字号由同一 Skia 字体度量，显式 size 保持不变；会话结束释放文本排版对象

Legacy 行高 MUST 使用固定字体的 Skia 原生 ascent/descent；无有效度量直接报错，不替换近似值。字号、换行、字距和绘制使用同一字体及度量后端。

<!-- @anchor id=canvas-render-text-measurement -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-text-measurement level=L -->

#### Scenario: 富文本方言解析

- **WHEN** 解析原版 `<color=...>` 与 `<size=...>` 文本
- **THEN** 按固定方言生成 CPU 文本段；该方言不是 HTML/XML 属性语法，字号经 Zod 校验有限正数，拒绝非法后缀；显式字号独立于自动字号基础值

项目仅负责原版富文本方言和平台排版规则的映射；Skia 原生字体度量、文本排版和字形绘制承担通用能力，不自造这些能力。

<!-- @anchor id=canvas-render-rich-text -->
<!-- @architecture id=renderer.values -->
<!-- @budget id=canvas-render-rich-text level=L -->

#### Scenario: 错误显示与通知

- **WHEN** 调用 `CanvasRenderer.render`
- **THEN** 返回结构化实际错误，由 Host 交付展示层；资源失败不输出替代图片

<!-- @anchor id=canvas-render-error-report -->
<!-- @architecture id=renderer -->
<!-- @budget id=canvas-render-error-report level=M -->
