# canvas-conversion Specification

## Purpose

Canvas 内联 Base64、内联 Hjson、独立二进制和 HTML 视图之间的转换。Base64 是受支持的原版旧数据。纯格式计算保持无状态，不读取文件、网络、配置、环境变量、时间或进程状态；工作区转换服务负责六向入口、结果隔离、缓存、实体资源核对与 Scope 生命周期。

## Requirements

### Requirement: float32 十进制

二进制浮点是 IEEE 754 binary32。JavaScript `number` 能精确保存全部有限 binary32 值。写文本时用 `Math.fround` 与 `DataView` 小端按位比较，选出能解析回同一 binary32 位型的十进制；颜色通道先尝试 0 到 255 的整数，整数按 `(float)(n/255)` 对不齐时改用截断后的小数。原生 `toPrecision` 提供十进制候选，`Math.fround` 与 `DataView` 提供位型验证，但不承担原版颜色通道整数及文本选择，所以项目仅保留这段选择。内部候选直接使用原生小写 `e` 科学计数格式，不重复验证原生输出。非有限值，以及四舍五入后超出 0 到 255 的颜色通道，MUST 失败。行为验证覆盖有限 float32、负零和不能表示成 k/255 的颜色通道的按位往返。

#### Scenario: 浮点文本按位往返

- **WHEN** 转换有限 binary32 值，包括不能表示成 k/255 的颜色通道
- **THEN** 文本解析回 binary32 后与原位型一致

<!-- @anchor id=canvas-float -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-float level=M -->

### Requirement: 视图数值可读性

Canvas 视图的浮点文本 MUST 使用能恢复原始 binary32 位型的最短十进制表示；不得通过固定小数位、无关尾零或二进制内部位型把正文写成反常识长数字。解析入口 MUST 接受 JSON 十进制数值语法并拒绝空串、前后空白、显式正号、缺少整数或小数部分的写法及非有限值。颜色通道优先使用可按位恢复的整数，无法恢复时使用最短可读小数；负零保持独立语义。

#### Scenario: 可读文本与严格数值边界

- **WHEN** 将 float32、Vector 和颜色通道写入视图并读回
- **THEN** 文本保持最短可读形式且按位恢复原值；无效数值在解析边界直接失败，不靠后续转换纠正

<!-- @anchor id=canvas-view-precision-readability -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @architecture id=pure.canvas.tests -->
<!-- @budget id=canvas-view-precision-readability level=M -->

### Requirement: 节点值

转换以节点为中心。变量按原版类型码区分布尔、float32、整数、字符串、三维向量、颜色、列表和字典。颜色要么是四个 binary32 通道，要么是调色板名称。读写遇到空的调色板名称 MUST 失败，不得改写成通道值。节点字段保持写入顺序，子节点单独保存。根 Canvas 的尺寸、位置和锚点默认值不进入字段，因此不进入二进制。

#### Scenario: 变量保持类型码和字段顺序

- **WHEN** 按顺序写入节点字段并加入子节点
- **THEN** 读回的类型码、顺序和子节点与写入一致

<!-- @anchor id=canvas-var -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-var level=L -->

#### Scenario: 文档只保存名称和根节点

- **WHEN** 创建 Canvas 文档
- **THEN** 名称与根节点可原样读回，根上未写入的默认字段不会出现

<!-- @anchor id=canvas-node -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-node level=M -->

### Requirement: 字段种类

九种节点类型接受的字段和类型码是封闭集合。`type` 与 `sons` 不是普通字段。`name` 只能是字符串。颜色字段接受颜色或调色板字符串。未知字段或类型码 MUST 失败。

#### Scenario: 字段类型封闭

- **WHEN** 查询节点类型是否接受某个字段和类型码
- **THEN** 只有封闭集合内的组合为真，其余为假

<!-- @anchor id=canvas-schema -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-schema level=S -->

### Requirement: 二进制布局

独立文件以 `02 76 31`、7-bit UTF-8 名称和子节点列表组成。内联字节没有文件头。整数、浮点、布尔、字符串、向量、颜色、列表和字典按小端原版布局读写。调色板颜色用负的首通道加字符串。`type` 写在字典最前，`sons` 写在最后。解析后剩余字节、缺字段、非法类型 MUST 失败。结构体只在二进制层按无键列表读取，Canvas 字段不使用它。

#### Scenario: 文件字节往返

- **WHEN** 把文档写成独立文件再读回
- **THEN** 再次写出的字节与第一次相同

<!-- @anchor id=canvas-binary -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-binary level=L -->

### Requirement: HTML 视图

HTML 是 XML。标签、属性和枚举名称按封闭表映射，UIButton 的 `label` 在 HTML 中写成 `label`，与 Canvas 渲染规范的唯一字段映射表一致。根元素必须是唯一的 `BeCanvas`。`width` 与 `height` 只校验有限数值，写出时固定为 1080 和 2160，不进入节点。自定义标签 MUST 显式闭合。枚举写出名称，读入必须是精确名称；没有名称的值 MUST 失败，不得改写成数字。颜色写出 `rgba` 或 `palette`，空的调色板名称 MUST 失败。布尔写出 `1` 或 `0`。向量用逗号分隔。列表只含不含逗号的字符串；空列表不能写成 HTML，MUST 失败，不得省略字段。节点上存在、但标签表没有的字段 MUST 失败。中文引号先换成 `\u201c` 与 `\u201d` 再做 XML 转义。

文档壳是固定文本：注释、`DOCTYPE`、`html lang="zh-CN"`、UTF-8 元数据和完整 BeCanvas 子树。壳不引用渲染脚本、样式或支持文件；PNG 由 CPU 渲染入口独立生成，完整 XML 布局文本与 PNG 结果分开交付。壳不读取外部文件，也不记录会漂移的行号。

该纯转换壳是布局文本，独立可视 HTML 由展示装配加入同次 PNG；本地打开的展示合同遵循 preview 的「CPU 图片预览链」。转换层不为浏览器再实现布局或读取图片。带展示壳的完整 HTML 经转换仍只取得唯一 BeCanvas：即使壳内图片不可用、脚本含其他标签文本，文档语义也只来自该子树；修改子树后必须取得修改后的值。

XML 解析使用 `@xmldom/xmldom`。`<!DOCTYPE html>` 不是 XML，解析前只去掉这一处声明。反向解析完整 HTML 时 MUST 只读取 `BeCanvas` 子树，展示壳中的脚本、样式和 PNG 引用不得进入 CanvasDocument。字段分量使用原生 `String.split`，已验证长度的数组使用 Effect `Array.getUnsafe` 取值，文本由原生字符串拼接与数组 `join` 组成。项目只承担封闭字段、枚举和标签表到 Canvas 节点的业务映射，不另造解析器、分隔器、取值包装或文本缓冲类。行为验证覆盖字段名称、枚举名称、文本字节与二进制往返。

#### Scenario: 字段名按封闭表对应

- **WHEN** 节点字段需要 HTML 属性名
- **THEN** 只使用封闭对照；没有对照的字段不能生成属性

<!-- @anchor id=canvas-names -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-names level=S -->

#### Scenario: 枚举名称精确对应

- **WHEN** 锚点、伸展、填充和输入内容类型在 HTML 中往返
- **THEN** 写出的是精确枚举名，读入拒绝大小写变体和裸数字

<!-- @anchor id=canvas-enums -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-enums level=S -->

#### Scenario: 标签只含该类型的字段

- **WHEN** 按节点类型取 HTML 标签
- **THEN** 九种类型各自得到封闭属性表，UIButton 的文本属性对应 `label`

<!-- @anchor id=canvas-tags -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-tags level=M -->

#### Scenario: HTML 与二进制按位一致

- **WHEN** 独立文档或内联片段经过 HTML 再写回二进制
- **THEN** 字节与直接写文件相同，内联片段不含自闭合标签

<!-- @anchor id=canvas-html -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @budget id=canvas-html level=L -->

### Requirement: 内联 Hjson 与 Base64

Base64 形态是 `canvas:`、名称、`=` 和标准 Base64，内容是没有文件头的子节点列表。编解码 MUST 使用现有 Effect `Base64`；非法字母、填充或长度 MUST 失败。Effect 解码允许 CR/LF 且不验证未使用填充位，因此项目在格式入口要求重新编码与输入完全一致，拒绝空白及非规范填充位，不自研编解码。行为验证覆盖标准编码、三种尾部长度、非法字符、错误填充和非规范文本。

Hjson 形态是 `==hjson:`、名称、`:`、换行、正文和结尾 `==`。正文顶层是子节点数组。原版布局按每层一个空格缩进，键值之间无空格，普通数组和对象换行，向量和颜色数值数组内联。对象键顺序保留到节点字段；写回时 `type` 在最前、`sons` 在最后。浮点文本由 float32 规则决定，整数超出安全范围 MUST 失败。解析使用 `hjson` 官方解析器，结构校验使用 `zod`。字符串写回使用官方 `Hjson.stringify`，设置 `multiline: off` 与 LF，不自研引号选择和转义。项目写出器仅组织字段顺序、节点结构和原版数值字面量，原生 JSON/Hjson 序列化不承担 float32 文本选择。行为验证覆盖字符串控制字符、真实 Hjson 文本一致和四向转换字节一致。

#### Scenario: 两种内联写回同一文件字节

- **WHEN** 同一文档写成 Base64 和 Hjson 再读回
- **THEN** 两种结果写出的独立文件字节相同

<!-- @anchor id=canvas-hjson -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @architecture id=pure.canvas.tests -->
<!-- @budget id=canvas-hjson level=L -->

### Requirement: 四向入口

调用者只通过一个入口在四种形态之间转换。入口检查根节点类型是 Canvas，再把工作交给对应格式。四种形态读回后写出的独立文件字节 MUST 相同。空输入、未知前缀、损坏的 XML、未知标签、缺字段和空的调色板名称 MUST 抛错。

#### Scenario: 四种形态汇合到同一二进制

- **WHEN** 同一文档分别写成文件、Base64、Hjson 和 HTML 再读回
- **THEN** 四次写出的文件字节一致，非法输入抛错

<!-- @anchor id=canvas-convert -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @architecture id=pure.canvas.tests -->
<!-- @budget id=canvas-convert level=M -->

### Requirement: 资源依赖纯投影

CanvasConverter.resourceGuids MUST 从已经解析的节点树提取实际资源引用，遍历全部子节点、去重并按字符串顺序返回不可变集合。引用白名单为 slicedPicGuid、fileGuid、softMaskGuid、atlasGuid、skelGuid、jsonGuid 和 pngGuids；空字符串不构成资源引用。节点整数 guid、名称及其它文本不构成资源依赖。该投影不读取资源、不维护索引或缓存，也不修改文档。

必要性：现有结构化解析和节点类型承担格式解释；文件元数据无法判断 Canvas 引用了哪些资源。纯投影只表达已声明资源字段的业务归属，缓存与静态预览复用同一实现，避免另行扫描 HTML 属性或重复维护引用收集。图片、Spine、嵌套、重复引用与四种输入形态验证投影。

#### Scenario: 只收集实际资源 GUID

- **WHEN** 从四种格式解析含图片和 Spine 的 Canvas，并提取资源引用
- **THEN** 获得相同的去重有序 GUID 集合，节点 guid 和普通文本被正确区分，原文档不变

<!-- @anchor id=canvas-resource-dependencies -->
<!-- @architecture id=pure.canvas.logic -->
<!-- @architecture id=pure.canvas.tests -->
<!-- @budget id=canvas-resource-dependencies level=M -->

### Requirement: Canvas 片段转换服务

CanvasConversion MUST 提供 toHtml(CanvasEntity)、base64ToHtml(string)、hjsonToHtml(string) 三个 HTML 片段入口；fromHtml(string, CanvasEntity)、htmlToBase64(string)、htmlToHjson(string) 分别返回语义实体及两种原版内联文本。明确格式入口必须校验对应原版前缀，禁止猜测输入或输出格式。正向输出 MUST 等于既有 CanvasConverter.toHtmlInline，完整 HTML 文档壳由独立 HTML 服务负责。

必要性：既有四向纯函数负责格式语义，Effect 提供错误边界与缓存；项目只连接明确方向、语义实体和共享转换结果。人工六向矩阵、错误形态及真实语料验证纯函数基线，转换不定位脚本或发布文件。

#### Scenario: 六个方向保持明确输入输出

- **WHEN** 将 Canvas 实体或原版内联文本转为片段，再反向输出实体或明确内联格式
- **THEN** 六个方向与纯函数结果一致，错形态及损坏输入失败，原始错误直接暴露

<!-- @anchor id=canvas-service-contract -->
<!-- @architecture id=toolkit.conversion.canvas -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=canvas-service-contract level=M -->

#### Scenario: 反向实体保留基准并隔离结果

- **WHEN** 对同一 HTML 使用不同基准实体，修改一次转换返回的模型或来源字节
- **THEN** 每次结果保留本次基准的声明、GUID、工程相对路径、版本和来源；语义来自当前 HTML，模型与来源字节副本隔离；原实体保持原值

反向转换只重建 Canvas 语义并携带基准来源，MUST NOT 调用未完成的格式实体 encode 或宣称完成持久化。格式实体装配和无损编码继续遵循实体规范的显式 TODO。

<!-- @anchor id=canvas-conversion-result -->
<!-- @architecture id=toolkit.conversion.canvas -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=canvas-conversion-result level=M -->

### Requirement: Canvas 转换结果有界缓存

每个工作区 MUST 经 CanvasConversionService.layer 使用原生 Effect Layer memoization 共享一个 Canvas 转换实例。转换结果 MUST 使用现有 Effect Cache，容量 128 项。键使用原生 Data 值相等合同，携带明确转换方向与完整输入：内联及 HTML 保留原文，实体使用当前语义经既有纯函数写出的二进制 Base64，不以对象身份或旧来源字节代替语义。

键按方向与内容元组的 JSON UTF-8 字节数计量；缓存结果为文本，反向实体的中间结果为原生 Base64。键、结果及依赖记录总计 MUST 不超过 1 MiB；超预算仍完整转换，输入键超预算直接计算，结果超预算及错误使用原生零 TTL，合预算成功使用无限 TTL。并发同键复用原生加载合并，淘汰使用原生策略。命中只核对实际引用的当前指纹，失配经 invalidateWhen 仅失效本次观察的旧项，不删除其它请求产生的新项。

实例及执行中的转换 Fiber MUST 属于工作区 Scope。关闭中断转换、标记关闭并清空缓存，关闭后包括已创建的 Effect 在内均失败，其它工作区继续独立使用。不另建时间失效、全工程索引、重试、降级或磁盘缓存。

必要性：Effect Cache 原生提供并发加载、条件失效、容量和按结果决定 TTL；Data 提供值相等，Scope 提供释放与中断。项目只连接完整语义输入、实际依赖和驻留预算。并发、容量、预算边界、输入及语义修改、共享装配和关闭验证该边界。

#### Scenario: 缓存复用、预算与生命周期

- **WHEN** 重复或并发转换相同输入，改变方向或语义，达到容量和预算边界，或关闭工作区
- **THEN** 有效输入持续命中，变化重新转换，超预算完整返回但不驻留，错误不驻留；关闭后失败且其它工作区隔离

<!-- @anchor id=canvas-conversion-cache -->
<!-- @architecture id=toolkit.conversion.canvas -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=canvas-conversion-cache level=M -->

#### Scenario: 图片与 Spine 的实际依赖指纹

- **WHEN** 实际引用资源内容或 GUID 映射变化、出现或消失，存在重复引用或无关资源变化
- **THEN** 仅核对去重后的实际引用；内容或映射变化失效相关结果，同内容改写和无关变化保持命中；IO、身份冲突与过期基准失败，关闭中断未完成核对

资源依赖 MUST 复用唯一 resourceGuids 投影。服务经 ProjectEntities.resolve 获取权威 FileEntity，再经 ProjectEntities.read 读取并校验主体和伴生版本；以工程相对路径及 Node 原生 SHA-256 内容摘要组成 Option 指纹。None 只表示当前权威身份缺席。监听尚未刷新的过期实体必须失败，不直接读取文件或重新解析 GUID 来绕过校验。

必要性：实体层已有身份查询、原始读取和版本校验；纯投影选择实际引用，Node 提供 SHA-256。转换服务只比较实际路径与内容，不复制 GUID 解析、监听、目录枚举或原始字节缓存。真实文件变更与过期基准测试验证此边界。

<!-- @anchor id=canvas-conversion-dependencies -->
<!-- @architecture id=toolkit.conversion.canvas -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=canvas-conversion-dependencies level=M -->

#### Scenario: 完整语料与纯函数基线一致

- **WHEN** 对完整独立及内联 Canvas 语料执行六向冷转换和命中
- **THEN** 文本与字节等于直接纯函数基线，核对真实转换次数并记录耗时和内存，语料只读且转换零写入

<!-- @anchor id=canvas-conversion-dispatch -->
<!-- @architecture id=toolkit.conversion.canvas -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=canvas-conversion-dispatch level=M -->

#### Scenario: 转换测试资源隔离并统计写入

- **WHEN** 六向转换及资源测试需要运行实体和监听服务
- **THEN** 复用既有独占文件测试资源并释放 Scope，成功和失败均释放自身文件；报告文件写入数、总字节、最大文件及累计写入量，格式实体构造只用于转换合同，不冒充工程装配

必要性：已有 FileTestWorkspace 负责独占目录、释放和实际写入统计；项目测试资源只装配真实实体与 Canvas Layer，复用真实纯函数构造转换基准，不另造生产资源指纹接口。

CanvasTestWorkspace.make MUST 在调用方的 Effect Scope 内取得独占文件资源并装配实体与转换 Layer，以只读成员交付。原生 acquireRelease 注册文件释放，Scope 按取得的逆序先释放转换与实体，再关闭目录；初始化和测试失败同样释放。测试直接执行文件变更、调用实体 refresh、读取快照并核对独立预期，资源对象不接受操作或断言回调。

<!-- @anchor id=canvas-conversion-test-resources -->
<!-- @architecture id=toolkit.host.test_support -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=canvas-conversion-test-resources level=M -->

### Requirement: 独立 HTML 视图转换服务

HtmlViewConversion MUST 接收独立 Canvas 实体并输出 HtmlViewEntity，反向接收视图和原基准并输出 CanvasEntity。该服务调用同一 CanvasConversion 片段能力，再装配完整 HTML 文档与支持资源引用；完整文档壳不进入 TS 内联片段。TODO：服务当前明确失败，未接入时不发布文件。

正向入口 `toHtml` 的输入为 CanvasEntity 和工程相对的 viewRelativePath。输出 HtmlViewEntity 携带 sourceEntityId、viewRelativePath、完整文档 text、CanvasDocument 与读取 baseline；源身份、语义和基准来自本次 CanvasEntity，视图路径由调用者确定。

反向入口 `fromHtml` 接收待修改的 HtmlViewEntity 和原 CanvasEntity 基准，MUST 核对源身份与读取基准对应，从当前 view.text 经共享 CanvasConversion 重新解析 BeCanvas 子树；view.document 不作为已编辑文本的替代来源。输出保持原 GUID、主体与伴生文件相对路径、revision 和原始来源字节，修改只体现在 Canvas 语义；完整文档壳中的展示资源不成为游戏资源或 Canvas 字段。候选生成、基准复核和持久化由 Editing 与 Entities 拥有。

服务通过 Effect Context/Layer 在同一工作区装配共享 CanvasConversion，利用已有片段转换与 XML 格式合同，完整文档壳复用既有纯库的固定格式。HtmlViewConversion 不建立第二份节点解析、缓存、监听、资源读取或渲染逻辑；PNG 由 Preview/Renderer 独立交付。空壳阶段只装配共享依赖，两个业务入口均明确失败；正式实现的验收覆盖完整文档与片段边界、编辑文本与旧 document 的差异、原身份和原始字节保留。

必要性与取舍：既有 CanvasConverter、CanvasHtml 和共享 CanvasConversion 已承担格式语义、XML 解析、固定文档壳及资源依赖核对；独立视图仍需要具名路径、源身份、完整文档和反向读取基准的合同。直接使用片段服务缺少完整视图封套，在本层再造解析器或缓存会重复已有职责；保留一个无状态实体组合边界，复用 Effect 原生 Layer 共享工作区依赖，代价是一次明确的服务调用。该模块随工作区 Layer 取得和释放，不单独持有缓存、后台任务或文件资源；正式执行沿用共享转换服务的 Scope，不增加第二套关闭状态。完整文档与 PNG 分开交付，视图落盘或内存供给由同一 Views 调度决定。

空壳实现 MUST 保留与接口相同的显式输入参数；未消费参数以 `_` 标识，两个入口均返回对应 TODO 错误。行为验收使用带真实 Canvas 语义、完整读取基准和来源字节的实体，分别调用带工程相对视图路径的正向入口及文本已编辑的反向入口；核对失败、输入保持原值与磁盘零写入，不把空壳失败当成完整转换能力验收。

#### Scenario: 独立视图双向入口

- **WHEN** 使用真实 Canvas 实体、工程相对视图路径或已编辑 HTML 文本调用尚未完成的双向转换
- **THEN** 两个入口返回对应 TODO 错误，输入实体及来源字节保持原值，磁盘零写入

<!-- @anchor id=html-service-contract -->
<!-- @architecture id=toolkit.conversion.html -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=html-service-contract level=M -->
