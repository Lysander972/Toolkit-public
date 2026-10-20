# BEScript Format Specification

## Purpose

定义 `.code`、单脚本局部 AST、全局变量、零件元数据、脚本绑定和 `BEScript_Device_Context` 的数据合同，并约束三层转换的输入、输出和依赖。格式读取、结构校验、上下文组装、语言转换和运行时缓存是不同职责；格式校验不等于完成转换。

工程身份、文件归属和 GUID 关联属于工程模型。脚本包消费调用方提供的内容和结构化结果，不访问文件系统、工作区服务或运行时缓存。可选字段统一使用 Effect `Option<T>`；显式 `None` 表示缺席，显式 `null` 不是缺席。

## Requirements

### Requirement: 词汇与转义

变量类型、全局变量类型、参数、颜色和脚本头 MUST 使用具名结构。语言类型词表 MUST 在 `BescriptVocabulary` 中定义，全局变量只允许对应的子集。GUID MUST 是 32 位小写十六进制。参数和脚本标识符 MUST 非空且不含需要转义的字符。

编码表为 `:` → `@1`、`=` → `@3`、`(` → `@4`、`)` → `@5`、`@` → `@@`；回车和换行丢弃。解码还接受 `@2` → `;`、`@6` → `/`、`@7` → `\`、`@8` → 空格、`@9` → `.`。未知转义及末尾孤立 `@` MUST 原样保留，解码一次不得再次解释产生的文本。完整 `==guid:` 加合法 GUID 再加 `==` 的引用，只编码其中的冒号。

标量格式校验使用 Zod；GUID 校验直接使用工程模型的 `GuidFormat.validate`。有限转义替换使用 JavaScript 原生字符串替换。标量正则只描述 GUID、数字属主、颜色通道或有限转义码，不解析脚本、调用、Hjson 或 TS 结构。

<!-- @formatExempt id=bescript-scalar-text -->
<!-- @formatExempt id=bescript-escape-codec -->

#### Scenario: 词汇和有限文本编码

- **WHEN** 校验类型、标识符、GUID 或编码解码文本
- **THEN** 词表外类型和非法标识符失败；转义遵守替换表；解码 `@@1` 得到字面 `@1` 而不是冒号

<!-- @anchor id=bescript-vocabulary -->
<!-- @architecture id=toolkit.script.text.vocabulary -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-vocabulary level=M -->

### Requirement: .code 文件与第一层文本边界

`CodeFileReader.read` MUST 只读取文件版本、头部和正文原文，不解析正文语句。文本 MUST 使用 LF；空前导行后以 `begincode` 或 `begincodeV2` 开始，以单独一行 `endcode` 结束，其后只能是空行。头部只接受 `name`、`comment`、`guid`、`color`、`params`、`return`、`debug`、`skip`、`private` 和 `lines`。`name` 必需；其他字段不得重复。`params` 可跨行，遇到下一个头部字段即结束。标记值只能是 `1`；颜色是三个 0 到 255 的整数。

正文 MUST 是 `lines:` 后换行到 `endcode` 起点之间的原文，保留缩进、禁用前缀、注释、多行内联 Canvas 和结尾换行。`version`、`header`、`sourceText` 构成第一层解析的完整文本输入。分行和定界符分隔使用原生字符串操作；项目读取器只承担原生 API 不提供的版本、头部边界和原文区间识别，不维护第二份正文词法规则。

#### Scenario: 头部边界和原文保留

- **WHEN** 读取真实返回脚本、跨行参数或包含多行内联内容的文件
- **THEN** 文件版本和头部被提取，正文逐字保留；参数续行不能跨过其他头部字段，非法颜色、重复字段和缺结束行失败

<!-- @anchor id=bescript-code-file -->
<!-- @architecture id=toolkit.script.text.code_file -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-code-file level=L -->

### Requirement: 局部 AST 与变量来源

`LocalAst` MUST 包含文件版本、脚本头和语句树，不携带零件身份、引用名称、TS 输出位置或另一份可任意填写的变量声明表。一个源码 AST 可以由多个引用共享；转换不得修改共享 AST。

语句种类是 `if`、`while`、`delay`、`async`、`comment`、`skip`、`break`、`continue`、`call`。只有 `if` 和 `while` 可以有子节点，子节点深度比父节点大 1。除 `comment` 和 `skip` 外，语句必须携带调用。`fun` 和 `toolf` 不携带属主；`varf`、`act`、`dvf` 携带属主类型和名称，`act` 的属主类型是十进制数字。调用不能同时保存返回值并丢弃返回值。

方法名与属主名 MUST 符合词汇层的标识符约束；`varf` 的属主类型 MUST 属于变量类型词表。`saveName` 是来源签名文本而非另一个方法标识符，不将其作为结构化调用字段的替代权威；`dvf` 的属主类型与平台方法关系由显式平台语义输入判定，不臆造更窄的词表限制。

`sourceText` 是 `lines:` 后至 `endcode` 前的完整正文来源，第一层必须将其与 AST 绑定保存。`line` 是正文 `sourceText` 内的 1-based 起始行；根节点 `depth` 必须为 0，子节点 `depth` 必须比父节点大 1。`text` 是去掉行号、前导缩进和禁用前缀后的来源命令文本，多行命令保留内部换行。`text` 用于来源与定位，`call` 和子节点用于语义；修改语义后不得把旧 `text` 作为序列化权威。源码内的坐标与生成后的 TS 坐标必须分别维护。

形参是绑定来源，正文调用的 `returnVariable` 是赋值来源，其中类型表示调用结果类型，不一定等于目标变量类型。`LocalAstSchema.bindings` MUST 从形参和语句树按首次出现顺序收集唯一局部绑定候选；形参优先，重复赋值不重新声明或覆盖已有候选。脚本头的 `returnParameter` 是返回引用，不额外声明变量，因此可以与形参或正文绑定同名。全局绑定归属、具体变量类型及赋值和返回类型兼容性由携带平台类型关系的 Context 语义校验决定，局部结构校验不得把类型名不相等当成语义冲突。例如真实 `rect.code` 中先创建 `UIRect b` 再以方法返回类型 `UI` 给 `b` 赋值，局部绑定仍保留 `UIRect`，不是重新声明 `UI b`。

变量收集是脚本语义职责，Zod 只校验结构，不能代替语句树到方法级变量表的收集。公开输入禁止携带独立 `declaredVariables`；绑定表只由已有 AST 派生，不产生新的权威副本。

#### Scenario: 返回来源与方法级绑定

- **WHEN** 真实脚本的返回头和正文都使用 `ret`，或返回形参、重复赋值及嵌套绑定
- **THEN** 返回名称不被当成重名声明；候选从实际来源生成，重复赋值不覆盖候选；调用返回类型与候选类型分别保留，错误树结构失败

<!-- @anchor id=bescript-local-ast -->
<!-- @architecture id=toolkit.script.ast.local_ast -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-local-ast level=L -->

### Requirement: 全局变量的读取与结构校验

`vars.json` MUST 是遵循 `openspec/specs/hjson-format/spec.md` 的 Hjson 数组，每项只有 `name`、`type` 和可选 `comment`；类型属于全局变量词表，名称唯一且不含需要转义的字符。`read` 解释外部字段，`parse` 校验已解析的 `GlobalVariable[]`，结构化调用不得重新读取或重新解释 Hjson。字段收窄前的 Hjson 对象可使用 `unknown` 索引签名，签名只存在于外部边界。

<!-- @formatExempt id=hjson-object-boundary -->

#### Scenario: 文件和结构化变量使用同一合同

- **WHEN** 读取真实全局变量文件，或校验结构化变量集合
- **THEN** 名称、类型、注释被保留；重名、未知字段及词表外类型失败，缺席注释必须是 `None`

<!-- @anchor id=bescript-global-variable -->
<!-- @architecture id=toolkit.script.context.device_context -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-global-variable level=S -->

### Requirement: 零件元数据投影

`DeviceMetadata` MUST 只表达转换需要的 `deviceType`、`logicName`、`mechFileGuid`、`stickToBone`，不携带清单或函数集合。`read` 从 `device.json` 的 `type`、顶层可选 `stickToBone`、可选 `ext.logicName` 和 `ext.mechFileGUID` 读取投影；其他工程字段保持工程模型归属，不纳入转换元数据。公开 `schema` 校验已有投影，不要求原始文件文本。

`logicName` 的外部输入 MUST 接受平台允许的非空字符串或数字值；结构化投影按旧工程模型的 `Qstr` 语义统一为非空字符串，不能因数字输入直接失败。

工程模型提供已解析字段时 MUST 复用这个投影合同，不另造同语义字段或在上下文组装时重新读取文件。GUID 解析、工程根和零件 ID 不属于元数据读取器。

#### Scenario: 元数据与脚本组成分别表达

- **WHEN** 读取真实零件、机械引用和骨骼绑定，或校验已解析投影
- **THEN** 字段得到同一份结构化结果；无字段得到 `None`，显式 `null` 或非法 GUID 失败

<!-- @anchor id=bescript-device-metadata -->
<!-- @architecture id=toolkit.script.context.device_context -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-device-metadata level=M -->

### Requirement: 脚本清单与引用身份

`ScriptListSchema.read` MUST 只解释 `scripts` 或 `event_` 加非空事件名下的清单。字符串条目是内联来源；对象条目是代码仓库来源，只接受 `name`、`guid` 和可选布尔 `private`。代码仓库条目必须携带合法 GUID，内联条目不携带 GUID 或清单私有标记。

`ScriptEntry.name` 是当前零件上的引用名称，`LocalAst.header.name` 是源码名称，两者 MUST 独立保存且不得通用地要求相等。仓库引用按 GUID 解析，不按名称寻找源码。源码私有标记与引用私有标记分别保留；自定义方法的有效私有性 MUST 为两者逻辑或，事件方法 MUST 为 private，不能覆盖原始属性。

这里的事件私有性约束作用于第二层生成的 TS 方法，不要求原始事件脚本头或清单带私有标记；原始标记为 false 的事件条目 MUST 仍被格式合同接受。第二层由事件组决定 TS private，不能为满足 TS 约束改写原始标记。

`ScriptGroup<T>` 保存组种类、显式可选事件名和有序条目数组。同一源码可按不同引用名称或不同事件组复用，不改写共享 AST 的头部。

#### Scenario: 同一仓库源码按不同方法名引用

- **WHEN** 同一 GUID 的源码被清单以不同名称引用，或读取真实带私有标记的条目
- **THEN** 引用名称、GUID、顺序和引用私有属性保留；不以源码名替换引用名，非法目录和条目字段失败

<!-- @anchor id=bescript-script-list -->
<!-- @architecture id=toolkit.script.context.device_context -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-script-list level=M -->

### Requirement: 唯一的零件脚本组成

`BescriptDeviceContext` MUST 由元数据投影、结构化全局变量和有序 `ScriptGroup<DeviceFunction>[]` 组成。每个函数只包含 `binding` 与 `ast`；组种类和事件名只在组上保存。清单经工程关联后转为函数条目，不同时保留另一份待匹配的清单或平行函数数组。

自定义组不携带事件名，事件组必须携带事件名；同一组不能重复，组内引用名唯一，空事件组保留。组数组和条目数组是顺序的唯一权威。`DeviceContextSchema.parse` MUST 校验结构化 Context，正反转换均使用此合同，不能要求调用者再次提供 `deviceText`、`varsText` 或 `listText`。

上下文结构校验只检查已有组成关系；GUID 指向的文件是否存在、文件归属和关联解析由工程模型负责。领域组约束及引用唯一性由组装边界处理，不能用名称特判替代工程关联。

#### Scenario: Context 往返校验与共享 AST

- **WHEN** 校验不同引用名共享的非空 AST、引用私有标记、空事件组、重排或重复组
- **THEN** Context 可直接再次校验，共享 AST 不被改写，顺序和独立属性保留；重复组、组内重名及缺失 GUID 的仓库绑定失败

<!-- @anchor id=bescript-device-context -->
<!-- @architecture id=toolkit.script.context.device_context -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-device-context level=M -->

### Requirement: 三层转换职责与完整输入

三层转换的模块归属与依赖关系唯一见架构模型 `toolkit.script.conversion.first/second/third`。

第一层 MUST 将 `.code` 的版本、头部和正文转换为局部 AST，并提供反向的 BEScript 序列化。正文词法、调用文法、缩进、禁用结构和内联 Canvas 收集只属于这个入口，不在 Context 或第二层重新解释。无缓存调用必须完成相同工作。

第二层 MUST 在 `BEScript_Device_Context` 与官方 TypeScript AST 之间双向转换。Context 是零件业务数据，不是 TS AST，也不是第三层。第二层可接受第三层传入的同身份官方方法节点作为纯构造输入，以避免重复生成；该注入不持有缓存、不比较修订、不解释片段语义，缓存与失效由工作区 TS 转换服务管理。正反转换均由调用者显式提供下列转换依据，不能在转换函数中读取工程、加载注册表或访问 Host：

| 输入 | 唯一职责 |
| --- | --- |
| 当前零件工程相对路径、数字 ID 和所属地图或机械身份 | 工程模型提供身份；转换不从显示名猜测身份 |
| 已解析的仓库 GUID 关联与引用绑定 | 工程模型定位源码，Context 保存引用关系 |
| 必要 `act` 目标的 ID、类型、逻辑名、源码名与 TS 方法名映射、返回类型、私有性和异步性 | 调用方准备转换所需的关联闭包；转换不索取无关工程全集 |
| 已解析的平台方法、事件、类型继承、枚举及参数语义数据 | 使用唯一平台数据源，转换不复制另一套手写注册表 |
| 内联 Canvas 参数内容 | 第二层在常量映射内直接嵌套调用 `CanvasConverter`；GUID 与调色板名称保持符号，不查询资源 |

第一层的 AST 缓存可以跨引用复用；第二层结果还依赖上述语义输入。源码未变不能推出转换结果未变。工程文件模式和内存视图模式 MUST 使用同一套纯转换合同。

反向转换 MUST 保留文件版本、脚本头、全局变量、组与条目顺序、空事件组、仓库来源与 GUID、引用名称、两种私有标记、调用与控制结构、注释和禁用信息。输出位置映射单独产生，不把某次输出的绝对行号写入源码 AST。

#### Scenario: 无缓存转换和语义输入变化

- **WHEN** 相同 Context 在完整语义输入下转换，或关联目标、平台数据、引用名发生变化
- **THEN** 无缓存路径可独立运行；变化影响对应转换结果，共享源码 AST 仍不变；Context 经正反转换保留上述业务信息

<!-- @anchor id=bescript-conversion-boundary -->
<!-- @architecture id=toolkit.script.conversion.second -->
<!-- @budget id=bescript-conversion-boundary level=L -->

#### Scenario: 官方 TS AST 第二层双向转换

- **WHEN** 调用 `DeviceTsConverter.toTs` 或 `DeviceTsConverter.fromTs`，并显式提供完整 `DeviceConversionBasis`
- **THEN** 转换只使用官方 `ts.SourceFile`、parser、factory 和 printer；零件结构、变量、脚本组、来源标记、控制树、调用、事件签名、Canvas 内联和禁用壳按来源合同双向恢复；非法平台映射、来源标记、修改后的固定初始化及无法配对的禁用壳直接失败

<!-- @anchor id=bescript-ts-device -->
<!-- @architecture id=toolkit.script.conversion.second -->
<!-- @budget id=bescript-ts-device level=L -->

### Requirement: 脚本方法双向映射

MethodMapping MUST 独立拥有单个脚本方法的正反转换、来源合同、参数与返回签名、事件约束及有效可见性。DeviceTsConverter 组合零件外壳、全局变量和脚本组，调用同一 MethodMapping；方法转换不反向依赖零件装配入口。官方 TypeScript AST、factory、parser 和 printer 负责语法，项目仅映射其无法表达的领域信息，不维护另一套 TS 语法实现。

第二层 MUST 在可随官方打印和解析保留的视图来源元数据中分别记录源码私有性和引用私有性；反向转换从该元数据恢复两者，不能仅凭 TS 的单一修饰符反推。自定义方法未改变有效可见性时，两种标记保持原值；修改内联方法的可见性只修改其源码头，修改仓库引用的可见性只修改引用标记。源码本身为 private 的仓库引用不能仅通过改引用为 public 而公开，必须明确修改共享源码；不满足有效可见性规则的输入报错。事件方法的 private 是事件组规则，不回填任一原始私有标记，修改该修饰符报错。

官方方法节点只表达一份生效签名和修饰符，不表达仓库来源、引用身份与源码头差别。第二层保存这些不可替代的领域信息；事件原始签名不回填，仓库源码与引用私有性分别编辑。新增方法也 MUST 提供唯一来源合同及显式脚本关联，不根据 TS 方法名猜测源码。已有组与空组通过零件来源保留，方法声明顺序决定组内顺序。

#### Scenario: 脚本来源身份、事件签名与可见性

- **WHEN** 共享仓库 AST 被多个条目引用，或编辑自定义方法的可见性并增删重排方法
- **THEN** 源码不被修改；内联修改源码标记，仓库修改引用标记，源码 private 无法通过公开引用绕过；事件可见性不能更改，缺失和重复方法来源报错

<!-- @anchor id=bescript-ts-methods -->
<!-- @architecture id=toolkit.script.conversion.second -->
<!-- @budget id=bescript-ts-methods level=L -->

### Requirement: 当前方法目标与依赖作用域

官方 AST 不知道平台跨零件可见性或机械核心作用域。MethodMapping 从当前 TS 方法来源、签名、可见性与 async 修饰符重建当前目标；外部目标只使用输入闭包。机械核心与产生器外部入口身份独立，不能通过文件查询补齐。

#### Scenario: 当前方法依赖与机械核心作用域

- **WHEN** 反向转换当前方法签名，或在机械核心中执行 private 自身调用
- **THEN** 当前目标反映本次方法的签名、私有性及异步性；private 调用严格按当前作用域与 ID 判断，不与外部入口混淆

<!-- @anchor id=bescript-ts-targets -->
<!-- @architecture id=toolkit.script.conversion.second -->
<!-- @budget id=bescript-ts-targets level=L -->

### Requirement: 正向转换的异步依赖传播

DeviceTsConverter MUST 从本次 Context 建立当前目标，按有限当前方法集合传播调用产生的异步性，直到目标不再变化；再将同一转换依据交给方法映射。外部目标只消费输入闭包，禁止读取工程或持有跨次状态。该传播属于完整零件的组合阶段，单方法映射仅校验实际 await、async 修饰符与 Promise 返回类型的一致性。

#### Scenario: 当前方法调用关系与异步闭包

- **WHEN** 当前自定义方法调用另一当前方法或外部闭包方法，含循环调用关系
- **THEN** 正向转换得到完整且终止的异步传播结果，方法修饰符与返回类型一致

<!-- @anchor id=bescript-ts-async-dependencies -->
<!-- @architecture id=toolkit.script.conversion.second -->
<!-- @budget id=bescript-ts-async-dependencies level=L -->

### Requirement: 显式转换依据与平台映射

ConversionBasis MUST 校验调用者显式提供的转换依据与平台映射；查找和校验只组织领域对应关系，复用官方 TypeScript AST 与 Zod。

#### Scenario: 转换依据与平台类型关系

- **WHEN** 转换读取零件身份、类型继承、方法参数排列、枚举和 act 目标闭包
- **THEN** 只按显式双向映射解析；缺失、重复、路径非法和类型不兼容直接失败

<!-- @anchor id=bescript-ts-basis -->
<!-- @architecture id=toolkit.script.conversion.second -->
<!-- @budget id=bescript-ts-basis level=L -->

### Requirement: 视图来源的结构化编码

SourceComments MUST 使用结构化来源标记保存官方 AST 无法表达的领域来源；读取和写入复用官方注释 API 与 Zod，跨官方打印解析保留同一合同。

#### Scenario: 来源标记跨官方打印解析保留

- **WHEN** 官方 factory 生成的注释来源经过 printer 和 parser 再传入反向转换
- **THEN** 结构化来源标记按 Zod 合同恢复；重复、非法或缺失的必需标记直接暴露

<!-- @anchor id=bescript-ts-source -->
<!-- @architecture id=toolkit.script.conversion.second -->
<!-- @budget id=bescript-ts-source level=M -->

### Requirement: 语句控制树与禁用结构映射

StatementMapping MUST 双向映射局部 AST 的控制结构和禁用壳；仅解释 BEScript 控制语义，语法节点的构造与读取复用官方 TypeScript AST。

#### Scenario: 语句控制树与禁用壳

- **WHEN** 转换 if、while、delay、async、注释、skip、break、continue 和调用
- **THEN** 控制树、循环跳转、返回绑定、等待关系和跨启用语句的禁用壳按配对来源恢复；禁用父壳不提供有效循环层级，但不禁用子节点。启用 continue 必须存在启用 while，方法级 return 等价于循环外 BEScript break，循环内 return 无法表达而报错。if/while 条件必须产生兼容 Bool 的调用结果，不能丢弃结果或用 delay 取代条件。异步结果和控制调用的必需 await 被删除时必须报错，不自动补回

<!-- @anchor id=bescript-ts-statements -->
<!-- @architecture id=toolkit.script.conversion.second -->
<!-- @budget id=bescript-ts-statements level=L -->

### Requirement: 官方 TS 语法子集

TsSyntax MUST 收窄官方 TypeScript AST 到视图声明子集，项目只校验允许的节点形态，不维护语法解析或输出引擎。

#### Scenario: 官方 TS 语法边界

- **WHEN** 第二层构造、读取、打印或解析官方 TS AST
- **THEN** 不维护自定义 TS AST、tokenizer 或 emitter；不属于声明子集的语法直接失败

<!-- @anchor id=bescript-ts-syntax -->
<!-- @architecture id=toolkit.script.conversion.second -->
<!-- @budget id=bescript-ts-syntax level=M -->

### Requirement: 变量绑定与类型归属

VariableMapping MUST 统一全局、局部及参数的作用域和类型映射，复用 LocalAstSchema 的绑定分析与显式平台关系，不另建变量语义分析器。

#### Scenario: 局部绑定与变量归属

- **WHEN** 方法参数、首次赋值、重复赋值、全局遮蔽和调用结果被转换
- **THEN** 复用 `LocalAstSchema.bindings`；全局、局部、参数和返回类型按显式平台关系区分。官方赋值只有目标变量类型，调用记录的结果类型通过来源独立保存；新增赋值采用平台返回类型。结果赋给父类型变量不反向要求父类型兼容子类型，varf/dvf 的显式 as 属主必须与实际变量类型兼容

<!-- @anchor id=bescript-ts-bindings -->
<!-- @architecture id=toolkit.script.conversion.second -->
<!-- @budget id=bescript-ts-bindings level=M -->

### Requirement: 平台调用双向映射

CallMapping MUST 在同一模块内处理平台调用的正反映射，复用显式转换依据与官方 TypeScript AST；项目只解释平台名称、参数和目标语义，不另建调用语法解析器。

#### Scenario: 平台调用与 act 映射

- **WHEN** 转换 fun、toolf、varf、act 和 dvf 调用及其泛型、参数顺序和返回绑定
- **THEN** 使用显式 BES/TS 名称、泛型及参数排列和目标闭包，不猜测目标、不降级未注册方法；同一 TS 方法的不同固定泛型按完整键反查。当前 AST 的参数增删必须反映在恢复正文中，旧来源字段缺席不能视为未编辑

<!-- @anchor id=bescript-ts-calls -->
<!-- @architecture id=toolkit.script.conversion.second -->
<!-- @budget id=bescript-ts-calls level=L -->

### Requirement: 常量及内联 Canvas 映射

ConstantMapping MUST 按显式参数语义映射常量、枚举与 Canvas。Canvas 格式复用唯一纯转换入口，普通字符串处理复用官方 TypeScript AST，不复制 Canvas 编解码实现。

#### Scenario: 常量与 Canvas 纯嵌套转换

- **WHEN** 转换普通常量、枚举、Base64/Hjson 内联 Canvas、HTML 模板和 GUID 引用
- **THEN** 直接嵌套已有 Canvas 纯转换入口；不访问 Host、资源服务或缓存，语义编辑不回放旧原文

<!-- @anchor id=bescript-ts-constants -->
<!-- @architecture id=toolkit.script.conversion.second -->
<!-- @budget id=bescript-ts-constants level=L -->

### Requirement: 第一层无损来源合同

`LocalAst.source` MUST 显式使用 `Option<ScriptSource>`：解析产物是 `Some`，明确新建的 AST 是 `None`。每个语句的 `sourceRef` 同样使用 `Option<number>`，只引用同一份来源中的节点；新增节点必须为 `None`。版本、头部、语句树是当前语义，来源只携带原始文本、CST 定位的格式区间及不可编辑的字段比较值，不持有另一份语义树、变量表或可变缓存。格式对象由 Zod 校验，缺席、非法结构及重复使用同一来源节点不得静默修复。

`CodeFileReader` MUST 在识别版本、头部和正文的同一次读取中记录文件区间、头部字段及参数续行区间。完整原文 MUST 覆盖前导空行、头部顺序与空白、正文、`endcode` 和尾随空行。正文来源 MUST 保存命令前的行号与禁用前缀、语义字段区间、命令后空白、块尾和内联内容。保留格式是实现字节级逆转换的必要职责；Zod 只承担结构校验，Chevrotain 只给出 token/CST 位置，不替项目定义格式归属。来源合同经无缓存全量字节比较、独立 AST 断言及编辑差分验证，不用原文回放代替序列化。

#### Scenario: 来源作为数据而非隐藏状态

- **WHEN** 解析原文、复制共享 AST 或构造明确无来源的新节点
- **THEN** 来源合同显式存在；重复转换一致且输入不变；非法来源结构失败，不读取进程状态

<!-- @anchor id=bescript-source-layout -->
<!-- @architecture id=toolkit.script.ast.local_ast -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-source-layout level=M -->

### Requirement: 第一层正文文法

正文 MUST 由 Chevrotain 13.2.0 的多模式 lexer 和 `CstParser` 解释；词法模式区分命令、参数头、参数值与多行内联 Canvas。单 token 正则仅表达文法终结符，不用于扫描调用或控制树。空白保存在具名 trivia 分组，token/CST 的完整位置用于精确取原文区间；未匹配字符、非法产生式和缺结束符直接报错，关闭错误恢复。固定词法与文法初始化 MUST 仅执行一次并复用 Chevrotain 引擎，不缓存源码、CST 或 AST。每次同步解析 MUST 经原生 input setter 重置输入与错误状态，成功和失败路径均释放输入 token；词法模式栈与错误结果属于当次 tokenize 调用。现有类型、转义和头部合同 MUST 复用。

命令语法包括可选数字行号、空白、可选 `/*` 禁用前缀、注释或调用；`if{`、`while{` 打开由 `}` 结束的块，`delay`、`async` 修饰调用，`fun break()` 和 `fun continue()` 映射相应语句。`//` 后文本不再作为调用分析。BEScript 的 `/*` 是单行禁用标记，不是需 `*/` 闭合的注释。只有块语句拥有子树，语义深度由块结构决定，现有双空格缩进必须与结构一致；源码 `line` 是正文物理行，不是前缀行号。

参数 MUST 为头部类型加一个空白再加参数原值，以 `:` 分隔；空常量值合法，变量名非空并遵守词汇约束。原值中的编码字符只解码一次。多行 `==hjson@1` 内容直到其 `==` 结束标记被收集为一个 Canvas 参数，内部换行、空白及分隔字符不进入外层文法；其业务结构和资源解析不属于第一层。文件结束标记从最后非空行确定，内联 Canvas 中的同名 `endcode` 行不截断正文。

Chevrotain 负责匹配与产生式，自研部分只承担 BEScript 文法声明、CST 的具名收窄和局部 AST 投影，不重写词法执行器或解析器。词法、语法、投影分开检视：分别用特殊字符与多行内容往返、错误输入、独立语句树和坐标断言证明其职责。

<!-- @formatExempt id=bescript-lexer-patterns -->

#### Scenario: 多模式词法保存完整内容

- **WHEN** 输入行号、禁用前缀、空参数、编码字符或多行 Canvas
- **THEN** 词法保留全部来源位置，内联内容不被错误拆成外层 token，未知文本失败

<!-- @anchor id=bescript-body-lexer -->
<!-- @architecture id=toolkit.script.conversion.first -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-body-lexer level=L -->

#### Scenario: 控制结构按文法生成

- **WHEN** 解析嵌套控制块、禁用块和普通调用，或输入未闭合块与参数
- **THEN** 产生结构完整的 CST，词法与语法错误包含正文行列，不容错或丢弃内容

<!-- @anchor id=bescript-body-grammar -->
<!-- @architecture id=toolkit.script.conversion.first -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-body-grammar level=M -->

#### Scenario: CST 投影与局部坐标

- **WHEN** CST 投影到语句树与来源记录
- **THEN** 正文起始行、深度、禁用状态、注释和子树正确，空行及块尾按来源归属保存

<!-- @anchor id=bescript-body-projection -->
<!-- @architecture id=toolkit.script.conversion.first -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-body-projection level=L -->

### Requirement: 第一层调用语义与字段输出

`fun`、`toolf` 的完整名称可以由点连接；`varf`、`act`、`dvf` 的签名前两个部分分别投影属主与类型，其余部分共同组成方法名，必须与局部 AST 合同一致。方法名的各个点分隔部分和属主字段不含空白或块定界符，属主字段不含点。`act` 显式 `/` 和平台名字形式记录别名语义，数字属主与 `this` 不按别名解释；`varf`、`dvf` 的属主前导 `/` 是真实名称的一部分，不剥离。返回绑定与丢弃返回分别记录，不能同时成立。参数原值与解码后的语义分别保存；序列化使用当前 `decodedText`、参数类型、返回绑定及结构化签名，不使用旧 `rawText`、`saveName` 或来源命令充当语义。变量参数标记必须与 `var` 类型头一致，变量名非空且不需要转义；非内联参数禁止换行，内联 Canvas 只允许 LF 且必须包含结束标记，无法表达的编辑值直接拒绝，不通过编码静默丢弃。

调用投影与生成只表达 BEScript 的参数、属主和返回语义，Chevrotain 不承担这类领域映射。有限转义复用词汇层，Canvas 只保留自身多行边界。独立调用断言和编辑后重新解析验证字段的真实作用；不查询工程、平台数据或缓存。

#### Scenario: 调用字段独立投影

- **WHEN** 解析不同调用种类、变量与常量参数及返回绑定
- **THEN** 结构化字段、来源区间和解码值正确，非法属主与返回结构失败
- **AND** `act` 的数字属主仅由 ASCII 十进制数字组成，不经数值转换；`0x10`、`1e2` 和带正负号的名字按别名投影，显式 `/` 始终表示别名

<!-- @anchor id=bescript-call-projection -->
<!-- @architecture id=toolkit.script.conversion.first -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-call-projection level=L -->

#### Scenario: 调用编辑决定输出

- **WHEN** 编辑方法、属主、参数或返回绑定后序列化
- **THEN** 输出重新解析得到当前字段，未编辑字段保留原始拼写，不复活旧命令或旧参数

<!-- @anchor id=bescript-call-output -->
<!-- @architecture id=toolkit.script.conversion.first -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-call-output level=M -->

### Requirement: 第一层来源保留序列化

序列化 MUST 遍历当前 AST；来源只为当前字段提供未修改时的原始 token 拼写与相邻空白。字段比较使用具名、确定的语义生成值，不通过源码哈希、对象身份或进程缓存判断。调用参数携带相对于当前调用的显式 `Option<number>` 来源引用，新增参数为 `None`；增删或重排参数保持未编辑参数的原值拼写、参数头和附属空白，分隔符按当前顺序装配。改变调用种类时仅修改种类及必要的签名结构，返回和参数区间保留。新增内容按 LF、双空格缩进、无行号生成，完整新建文件使用 AST 版本、既有字段顺序和一个结尾换行。

头部的旧字段保持其位置、顺序及续行；未变头部字段逐字保留，修改字段使用当前值，新增字段置于 `lines:` 前。头部参数数量不变时按原生分隔定位的类型和名称区间分别编辑，不改写未编辑参数、空白或续行。不存在的可选字段不补默认值，删除字段不保留其旧语义。正文节点前空行归属该节点，块尾空白归属块，文件尾空白归属文件；删除节点同时删除其附属前导内容，移动节点保留其来源内容并按新深度调整缩进。块头禁用与原始块尾禁用各自保留。同层级仅切换禁用状态时只修改前缀标记，不重排空白。来源合同必须拒绝重复节点 ID、必需字段缺失或重复、区间越界；输出参与装配的区间不允许重叠。

来源区间装配是保持用户文本格式同时支持语义编辑的项目职责；Chevrotain 不提供 BEScript emitter，原生字符串切片只能按已解析位置取文本，不能承担结构识别。头部来源字段必须按顺序与读取器提供的字段键和区间一一对应，拒绝删除或改变来源关联后回放已删除语义。来源字段输出、头部生成和正文树输出分别检视，均以精确编辑差分和重新解析证明，不以源码字符检查代替行为测试。

#### Scenario: 来源区间只提供格式

- **WHEN** 修改某个语义字段后装配来源区间
- **THEN** 对应字段输出新值，其他区间逐字相同，不整体回放原文

<!-- @anchor id=bescript-source-output -->
<!-- @architecture id=toolkit.script.conversion.first -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-source-output level=S -->

#### Scenario: 头部编辑与显式新建

- **WHEN** 修改版本或头部字段，或序列化显式无来源 AST
- **THEN** 当前头部决定输出，其他字段位置保持，新建模式使用固定格式并可重新解析

<!-- @anchor id=bescript-header-output -->
<!-- @architecture id=toolkit.script.conversion.first -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-header-output level=M -->

#### Scenario: 当前节点树决定序列化

- **WHEN** 插入、删除、重排或跨块移动节点
- **THEN** 输出顺序及层级与当前 AST 一致，未编辑来源保留，重复来源引用失败

<!-- @anchor id=bescript-body-output -->
<!-- @architecture id=toolkit.script.conversion.first -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-body-output level=M -->

### Requirement: 第一层参数来源装配

参数数组中顺序、类型和解码值是唯一语义。`ArgumentWriter` 只按显式来源引用复用当前参数的类型、原值和相邻空白；未知引用、重复引用直接失败。参数的来源索引不随数组重排改变，新增参数明确使用 `None`，不读取进程缓存。成熟 parser 不提供来源保留 emitter，因此该装配保留为项目职责；精确增删重排文本差分与重新解析验证字段保真。

#### Scenario: 参数增删重排保留字段来源

- **WHEN** 参数集合变化或顺序改变
- **THEN** 每个未编辑参数保留类型、空白和原值拼写，新增参数明确无来源，重复引用失败

<!-- @anchor id=bescript-argument-output -->
<!-- @architecture id=toolkit.script.conversion.first -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-argument-output level=M -->

### Requirement: 第一层公开转换入口

脚本包 `conversion` MUST 导出 `BescriptConverter.parse(source: string): LocalAst` 和 `BescriptConverter.serialize(ast: LocalAst): string`。完整文件往返必须实际生成局部 AST 并按当前 AST 序列化；无修改时与输入 UTF-8 字节完全相同，两轮重复不漂移。共享 AST 不得被转换修改，每次调用错误与解析状态独立。读取文件由调用方负责，第一层没有 IO、Host、配置、平台语义或跨调用缓存依赖。

公开入口只组装已有头部读取、成熟正文解析、领域 AST 投影与来源序列化，不另造文本解析流程。真实语料验收覆盖测试工程中所有 `.code`，不筛选可解析文件、不跳过错误、不修改夹具；另外用人工语料独立断言 AST 与编辑结果，阻止原文回放冒充无损转换。测试只读输入并在内存比较字节，不写往返副本。

#### Scenario: 全量语料两轮字节级往返

- **WHEN** 现有全部 2,775 份真实脚本依次解析和序列化两轮
- **THEN** 每轮 UTF-8 字节都与输入相同，独立 AST 与编辑测试成立，转换测试在内存中完成

<!-- @anchor id=bescript-converter -->
<!-- @architecture id=toolkit.script.conversion.first -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-converter level=M -->

### Requirement: 官方 TS 语法与第三层缓存

TS AST MUST 使用官方 TypeScript 类型，解析使用官方 parser，构造使用官方 factory，序列化使用官方 printer，不维护手写 TS AST、TS tokenizer 或 emitter。TS 语法事实与 BEScript 领域语义分别归属官方语法层和第二层业务转换；注释位置不等于已解释的禁用业务结构。

全行块注释形式的禁用语句、禁用 `if` / `while` 壳和匹配的禁用 `}` 壳 MUST 由第二层识别并保留。禁用控制壳可以跨过正常启用的 TS 语句，不能因官方 parser 将其视为注释而丢弃或提前闭合。内联 Canvas 和 BEScript 常量语义也由业务转换处理，不能进入官方 TS 语法服务。解析、生成和业务校验失败 MUST 暴露明确错误，不提供另一套解析器、格式兼容配置或静默降级。

第三层 MUST 处理官方 AST 与 TS 文本之间的解析、打印和局部增量优化。跨请求缓存由所属 Host 的 TS 视图转换服务持有并随工作区生命周期释放；视图层只编排供给与发布，不重复持有转换缓存。缓存不进入第一、二层纯函数或进程全局 Map。失效必须对应内容和实际语义依赖，不能用 TTL 弥补缺失依赖，不能用可能碰撞的短哈希作为内容相等证明。

增量输出 MUST 与同一官方版本、同一官方选项下整树 `printFile` 直调的输出逐字节相同。全量基线不经生产路径的缩进、Unicode 或尾换行文本修补。缓存片段与最终位置映射分别维护；方法增删和重排不能沿用旧 TS 行号。差分测试必须覆盖冷启动、命中、局部修改、语义依赖变化、事件、空组、重排、注释、禁用结构及内联 Canvas；基准必须实际比较无缓存整树打印与局部更新的时间和内存。

文本来源信息 MUST 被保存，不能把官方打印归一化冒充输入原文恢复。字节级往返与官方输出等价是两个独立条件：原文恢复针对保留的输入来源，打印等价针对同一 AST 的官方输出；不能通过静默缩小输入范围或共同后处理降低任一要求。

第三层允许的项目职责仅是有明确输入与生命周期的片段复用、外壳装配及位置组合；每项优化必须有独立全量差分依据和性能依据，不重新实现官方语法或打印规则。

#### Scenario: 官方解析与来源保留

`TsViewText.parse` MUST 使用官方 parser 和同一官方树的语法诊断，不为诊断再次转译整份输入；不读取文件或加载类型库。`read` 保存官方树及规范打印状态；`restore` 与规范打印是两个独立输出。规范打印仍按当前 AST 生成；无编辑时恢复完整输入，包括 BOM、CRLF、空白、注释、字面量拼写与尾随空行。外壳及方法身份顺序未变时，只替换实际修改的方法体，其他原文保持；外壳编辑或方法增删重排时按当前官方外壳装配并保留仍未修改的方法体原文。第二层继续解释脚本来源与禁用业务。

- **WHEN** 输入经过读取和恢复，或编辑方法体后再次读取和恢复
- **THEN** 无修改时输入 UTF-8 字节完全相同；实际修改进入结果，两轮稳定；语法错误直接失败

<!-- @anchor id=bescript-ts-text -->
<!-- @architecture id=toolkit.script.conversion.third -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-ts-text level=M -->

#### Scenario: 官方方法体片段与外壳装配

公开输入为第二层的官方零件视图 AST 与一一对应的片段身份、完整修订内容及语义依赖。调用者 MUST 以当前不可变业务输入提供修订与依赖，禁止把短哈希、对象身份或过期修订作为内容证明。片段边界只来自官方 AST 的方法体区间。完整 namespace/class 父结构和方法签名由 factory 保留，官方 printer 分别生成无方法体的外壳及待更新方法；方法体原样提取并装配，不调整缩进、字面量或尾换行。字段、方法签名与前后来源注释每次由官方外壳打印。该结构使用第二层声明的语法子集；第三层不承担其他 TS 语言构造的业务转换。

官方 printer 没有公开片段缓存或文本写入钩子；局部方法体复用、原样外壳装配是避免每次遍历所有方法体的项目职责。方法体与父结构打印、结构坐标提取及无缓存官方整树差分共同验证这项自研，而不是自行实现 TS emitter。只保留当前片段，删除的方法从返回状态中消失。

- **WHEN** 冷启动、命中、局部修改、语义依赖变化，或新增、删除、重排方法及空组
- **THEN** 结果逐字节等于未经共同后处理的官方 `printFile`；命中不重打方法体，变化的修订或依赖使对应片段失效；注释、禁用结构、事件、机械核心及多行 Canvas 保真，旧状态不变

<!-- @anchor id=bescript-ts-fragments -->
<!-- @architecture id=toolkit.script.conversion.third -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-ts-fragments level=L -->

#### Scenario: 装配位置与性能依据

位置使用 UTF-16 偏移和 1-based 行号，表示方法声明起点至方法体终点。片段复用后仍按当前外壳区间及前序片段长度重新组合；位置映射只消费当前装配文本，不参与语义转换。官方 printer 不返回拼接后的业务位置，项目保留这项必要位置组合职责。基准在同一 AST 上直接比较官方全量打印与单方法更新，并在 Host 视图服务链中验证第二层重新生成、命中和局部修改，记录预热后的多轮时间、中位数、堆内存变化及保留片段字节；每轮先保证输出等价。测试仅在内存中生成输入，实际文件写入为零。

- **WHEN** 方法移动、增删及大文件单方法更新
- **THEN** 位置与独立官方解析所得位置一致；局部更新的中位耗时小于官方全量直调，内存成本如实报告

<!-- @anchor id=bescript-ts-positions -->
<!-- @architecture id=toolkit.script.conversion.third -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-ts-positions level=M -->

#### Scenario: Host 独占视图缓存生命周期

`DeviceTsViews` 属于 `TsViewConversionService`，由所属 Host 的 Effect Scope 持有，关闭时释放当前工作区的全部片段与官方 AST；不拥有工程文件或监听器。缓存键为零件工程相对路径，方法身份使用组种类、事件名和绑定名。修订使用当前完整 DeviceFunction，依赖使用现有第二层 `resolve` 得到的语义依据及全局变量，包含当前方法的异步依赖闭包；不另建平台或异步规则。第二层仍无缓存。转换服务 `print` 接收已准备的 Context 与完整依据；Views 的 `refreshTs` 通过同一工作区转换服务的 `toTs` 更新具名视图，删除零件时调用该服务的 `remove` 释放状态。文件视图与内存视图共用同一实现。失败时旧缓存不被替换。

Issue #61 的统一事件控制流由视图层调度：更新时准备当前完整 Context 与语义依据并调用 `print`，删除时调用 `remove`。转换服务只消费显式输入并更新缓存，不订阅实体流、不读取工程或扫描快照。实际外部调度按视图服务合同保留 TODO。每个零件的官方 AST 与打印状态 MUST 由同一缓存条目共同持有，转换成功后一次替换；失败保留上次有效条目，移除与关闭统一释放。

- **WHEN** 两个 Host 打印相同路径，当前内容或依赖变化，移除零件，或 Host 关闭
- **THEN** 两个缓存独立；内容变化与依赖变化生效；删除后再打印为冷启动，关闭后的访问失败；失败后合法打印仍成立
- **WHEN** 调用方以更新后的完整输入打印一个零件，或移除该零件
- **THEN** 结果与当前输入的官方全量打印逐字节相同；无关零件仍命中，移除后再次打印为冷启动

<!-- @anchor id=bescript-ts-cache -->
<!-- @architecture id=toolkit.conversion.ts -->
<!-- @architecture id=toolkit.host.logic -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @architecture id=toolkit.script.tests -->
<!-- @budget id=bescript-ts-cache level=L -->

### Requirement: 三层转换消除跨语言胶水

现役三层转换 MUST 保持单一 TypeScript 调用链：第一层 `BescriptConverter` 负责 `.code` 与 `LocalAst`，第二层 `DeviceTsConverter` 负责 `BEScript_Device_Context` 与官方 TypeScript AST，第三层 `TsViewText` 负责官方 AST 的解析、打印和片段装配；Host 只经 `TsViewConversion` 编排工作区输入与生命周期。第三层 MUST 直接调用官方 TypeScript 编译器 API，不启动 Python/C# 进程、不走手工行协议、不拼接行号或闭包文本，也不建立另一套跨语言数据格式。

`TsCanvasConversion` 仅是同一次 TS 转换的显式 Canvas 准备结果，不能成为跨语言桥接或第二条转换路径。视图缓存与失效继续归 `DeviceTsViews`，纯转换不读取工程；现役源码、包配置和测试不得依赖旧 MCP/Python 适配器。`legacy/` 中保留的 C#、Python 或旧协议实现只作历史参考，不属于现役迁移链，不能被新入口重新引用。

三层边界 MUST 由真实行为验证：直接调用 TypeScript 第一、二、三层完成语法解析、语义往返、官方打印、局部片段更新和 Canvas 内联往返；输出与独立官方打印逐字节一致，失败保留原始错误，转换过程不写文件。源码中出现旧适配器名称或文本不能替代上述行为证据。

#### Scenario: 第一层使用唯一 TypeScript 格式入口

- **WHEN** 将 `.code` 解析为局部 AST 或按当前 AST 序列化
- **THEN** 只经 `BescriptConverter` 完成第一层格式转换，保留来源并反映语义编辑，转换不启动跨语言进程或写派生文件

<!-- @anchor id=bescript-first-language-boundary -->
<!-- @architecture id=toolkit.script.conversion.first -->
<!-- @budget id=bescript-first-language-boundary level=M -->

#### Scenario: 第二层使用唯一 TypeScript 语义入口

- **WHEN** 在零件 Context 与官方 TypeScript AST 之间转换，包括内联 Canvas
- **THEN** 只经 `DeviceTsConverter` 与显式 Canvas 转换依据完成第二层语义映射，往返保留业务信息，失败保留原始错误

<!-- @anchor id=bescript-second-language-boundary -->
<!-- @architecture id=toolkit.script.conversion.second -->
<!-- @budget id=bescript-second-language-boundary level=L -->

#### Scenario: 第三层直接使用官方 TypeScript API

- **WHEN** 解析或打印官方 AST，或更新方法体片段
- **THEN** `TsViewText` 直接使用官方 parser、factory 与 printer，装配结果与独立官方打印逐字节一致，转换不经过跨语言协议或派生文件

<!-- @anchor id=bescript-third-language-boundary -->
<!-- @architecture id=toolkit.script.conversion.third -->
<!-- @budget id=bescript-third-language-boundary level=M -->

#### Scenario: 工作区服务编排唯一转换链

- **WHEN** Host 为同一零件执行冷启动 TS 生成、局部方法更新、反向 TS 转换和内联 Canvas 往返
- **THEN** 调用只经过 TypeScript 第一、二、三层及共享工作区服务；结果与官方 AST/打印独立预期一致，局部更新复用合法片段，失败不产生跨语言进程或派生文件写入

<!-- @anchor id=bescript-cross-language-glue -->
<!-- @architecture id=toolkit.conversion.ts -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=bescript-cross-language-glue level=M -->

### Requirement: 零件级 TS 转换服务

TS 视图转换服务 MUST 接收第二层 DeviceEntity 并返回 Toolkit TsViewEntity，反向 MUST 接收视图与同一读取基准，返回新的零件格式实体。DeviceEntity MUST 携带既有 BescriptDeviceContext 与显式 DeviceConversionBasis，二者由实体层的调用方准备；依据的零件相对路径必须与权威声明一致。业务服务不读取文件、不另建平台规则或关联表。

正向 MUST 经同一工作区缓存生成文本，并交付与该文本对应的官方 AST、源实体身份及读取基准。反向 MUST 对视图当前文本使用官方解析和纯双向转换，拒绝源身份或读取基准失配；返回新的 DeviceEntity，其声明同步更新元数据和全局变量，原始文件与版本基准保持不变。转换不得修改输入。

内联 Canvas 双向转换 MUST 调用共享 CanvasConversion 服务。纯转换通过显式 InlineCanvasCodec 消费同步的格式转换能力：独立纯函数调用使用原生无状态 Canvas 转换；工作区业务在调用前完成所需 Canvas 请求，传入本次调用的确定结果，不回退到另一条转换路径。请求从 LocalAst 的实际参数语义与官方 TS AST 提取；禁用壳复用既有结构化来源和官方解析。Canvas GUID 和空常量保持既有纯语义，不读取 GUID 指向的 Canvas 内容。

TsCanvasConversion 只保存一次转换调用的已准备结果，不持有工作区缓存。Canvas 缓存仍唯一属于 CanvasConversion，TS 官方 AST 与打印缓存仍唯一属于 DeviceTsViews。任何准备、解析或转换失败都暴露原始错误；失败不得替换已有 TS 缓存，Scope 关闭后实体正向与反向入口均拒绝调用。

必要性：官方 TypeScript 负责语法和打印，已有纯转换负责 BEScript 语义，CanvasConversion 负责异步依赖校验与共享缓存。同步纯映射不能执行有 Scope 的 Effect 服务，因此项目只在实体边界准备所需 Canvas 结果并注入纯映射；不复制格式算法或另建平台映射。真实实体往返、独立预期源码、官方全量差分、两种 Canvas 内联及禁用语句、Canvas 原始错误、基准失配和 Scope 释放共同验收。行为测试同时覆盖原生解析、转换、实体与 Layer 边界，采用 L 依赖预算。

#### Scenario: 零件与视图的双向边界

- **WHEN** 调用零件实体到 TS 视图或反向业务入口，并真实编辑 TS 字符串或内联 HTML
- **THEN** 交付当前文本与官方 AST；反向产生对应的新零件语义与声明，未改原始格式信息保留；两种 Canvas 形态及禁用壳保持往返，失败不替换缓存，关闭后拒绝调用

<!-- @anchor id=ts-service-contract -->
<!-- @architecture id=toolkit.conversion.ts -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=ts-service-contract level=M -->

### Requirement: TS 视图模块风格与映射边界

新版工具源码、工作区服务和构建产物统一使用 Node ESM；这项运行时模块风格不改变用户 TS 视图的领域语法。TS 视图 MUST 保持官方 TypeScript AST 可解析的 `namespace` 根和零件 `class` 结构，用于一一映射 BeScript 的零件、方法组和平台调用；视图正文不是 Node 可执行模块，不得凭空加入 ESM `import`/`export` 或 CommonJS `require`/`module.exports` 作为兼容层。

映射 MUST 由第二层的实体身份、命名空间/类名、方法身份和显式 `DeviceConversionBasis` 驱动，第三层只使用官方 parser、factory 与 printer 保留该结构及方法体片段。ESM/CJS 只约束实现和构建边界；视图到 BeScript 的反向转换继续消费同一 AST 与语义依据，不通过模块加载、动态执行或文本替换判断身份。旧版浏览器脚本和 C# 视图运行时不属于新版映射链。

#### Scenario: 视图 DSL 与实现模块解耦

- **WHEN** 生成并解析一个 TS 视图
- **THEN** 工具实现保持 ESM，视图 AST 保持单一 `namespace` 与零件 `class` 映射；正反向转换使用实体身份和官方 AST，不启动 CJS/ESM 模块加载或加入第二套语法

<!-- @anchor id=ts-view-module-style-convergence -->
<!-- @architecture id=toolkit.script.conversion.second -->
<!-- @architecture id=toolkit.script.conversion.third -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=ts-view-module-style-convergence level=M -->

### Requirement: 独立视图索引声明边界

TS 视图正文、官方 AST/打印状态与面向消费者的索引声明 MUST 分开。`TsPrintState.positions` 只属于 Host Scope 内的运行时位置依据，不能被序列化为视图正文，也不能冒充独立 `.d.ts` 文件。视图实体的编码 MUST 只交付视图相对路径及其 UTF-8 正文，不隐式追加索引文件或第二份声明内容。

独立视图索引 MUST 是同次 TS 视图生成结果的只读声明投影，路径固定为完整 `viewRelativePath` 追加 `.d.ts`，与视图正文一一对应。内容声明源实体身份、源文件工程相对路径，以及按当前方法顺序排列的方法身份与视图 UTF-16 起止偏移；行列由同一官方 SourceFile 换算，不保存第二份可变坐标。声明使用 TypeScript 官方 factory 与 printer 生成，以 ESM 导出的只读字面量类型表达实际身份及位置，供编辑器和 Agent 查看与定位；它不是可执行模块或工程输入。Host/LSP 继续直接消费同次内存中的位置结果，不读取声明反推业务状态。

files 模式由 Views 将正文和索引作为同一批派生内容交给实体层发布、纠偏和删除；源变化、方法增删重排及视图路径变化使相应声明一起更新。memory 模式保留相同内存位置依据，跳过索引落盘。转换服务生成内容，Views 决定供给，实体层承担 IO，不另建索引缓存、监听或注册表；视图实体的单文件 encode 仍只编码正文，由派生交付组合本次声明。当前索引生成和成组交付均保持 TODO。

必要性与取舍：现有官方 AST 和打印位置已经提供身份及坐标，TypeScript factory/printer 提供声明生成；独立声明只投影这些已有结果，满足用户要求的可查看索引，不重复解析视图或引入 JSON 索引。追加完整后缀避免与源文件及其他声明冲突；代价是 files 模式多一份须随同次视图失效的派生产物。正式验收须以独立官方解析验证声明语法、身份及位置，并覆盖方法重排、删除、路径变化、旁路纠偏和 memory 零写入，不能以文件存在代替索引正确性。

#### Scenario: 视图正文与索引声明隔离

- **WHEN** 生成一个 TS 视图实体并编码其派生产物
- **THEN** 返回的正文和运行时位置依据保持分离，编码只包含视图正文；独立索引 `.d.ts` 未接入时不生成伪文件或把旧版声明当作新版结果

<!-- @anchor id=ts-view-index-dts-convergence -->
<!-- @architecture id=toolkit.conversion.ts -->
<!-- @architecture id=toolkit.entities -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=ts-view-index-dts-convergence level=M -->
