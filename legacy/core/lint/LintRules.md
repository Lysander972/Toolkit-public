# MCP Lint 规则表

仅收录规范已明确的规则；未明确的条款不实现、不推测。

## 已实现（A 类：条件明确）

| Rule ID | 类型 | Severity | 规则内容 | 适用层级 | 来源 | 是否实现 |
| --- | --- | --- | --- | --- | --- | --- |
| LINT-AST-PARSE-ERROR | 语法 | Error | AST 构建失败（语法/结构错误），按 AST 层返回的定位直接报错 | 脚本 | openspec/specs/bescript-format/spec.md | 是 |
| LINT-LINT-ERROR | 引擎 | Error | Lint 引擎在校验过程中遇到基础数据缺失/读取失败等问题导致无法继续某项校验时记录错误，但不应中断其他规则执行 | 脚本/零件/机械/地图 | MCP_Lint_Spec.md “Lint 运行错误记录” | 是 |
| LINT-NAME-KEYWORD | 命名 | Error | 使用 BEScript 语法关键字作为标识符（变量/函数/脚本名等） | 脚本/零件 | MCP_Lint_Spec.md "命名/关键字" | 是 |
| LINT-PARAM-CLASH-GLOBAL | 语义 | Error | 自定义方法参数名称与零件全局变量同名 | 脚本 | MCP_Lint_Spec.md "变量作用域一致性" | 是 |
| LINT-GLOBAL-CLASH-SCRIPT-NAME | 语义 | Warning | 零件全局变量名称与同零件脚本条目同名；BEScript 允许该重名，但 TS 视图会把二者放入同一键空间并产生大量 TS 语法错误 | 零件 | MCP_Lint_Spec.md “变量作用域一致性” | 是 |
| LINT-GLOBAL-CLASH-BUILTIN-PARAM | 语义 | Error | 零件全局变量名称与底层事件参数同名（如 `$player` 等） | 脚本/零件 | MCP_Lint_Spec.md “变量作用域一致性” | 是 |
| LINT-GLOBAL-CLASH-RETURN | 语义 | Error | 零件全局变量名称与同零件脚本返回名同名 | 脚本/零件 | MCP_Lint_Spec.md “变量作用域一致性” | 是 |
| LINT-VAR-GLOBAL-TYPE-MISMATCH | 语义 | Error | 与全局变量同名的变量（参数/返回/局部）使用了与全局声明不同的类型 | 脚本 | MCP_Lint_Spec.md “变量作用域一致性” | 是 |
| LINT-CALL-PARAM-SIGNATURE-MISMATCH | 语义 | Error | 底层注册方法调用参数与签名不一致（数量/类型不匹配，仅允许 Any/UI 基类互通）；参数多于签名时一律报 Error，参数少于签名且仅缺少尾部参数时以 Warning 提示 | 脚本/零件 | MCP_Lint_Spec.md “方法参数签名一致性” | 是 |
| LINT-CALL-NOT-RECOMMENDED | 语义 | Warning | 调用被标记为“不推荐”或“已作废”的内置方法时给出警告，原因取 SDK 注册中的不推荐理由或已作废默认理由 | 脚本/零件 | SDK 注册规则（Recommend/IsObsolete） | 是 |
| LINT-CALL-PARAM-IMPLICIT-TOSTRING-NOT-ALLOWED | 语义 | Error | 底层签名参数为 String 但未标注允许隐式转换时，禁止传入非 String 参数（不允许隐式转为文字） | 脚本/零件 | MCP_Lint_Spec.md “方法参数签名一致性/文字(Text)参数” | 是 |
| LINT-CALL-PARAM-ANY-MUST-VAR | 语义 | Error | 内置方法签名中 Any 类型参数必须传变量或类型为 Any 的常量，禁止传其他类型常量 | 脚本/零件 | MCP_Lint_Spec.md “方法参数签名一致性” | 是 |
| LINT-CALL-CONDITION-CONST-MUST-VAR | 语义 | Error | if/while 条件中的 `create.bool`、`bool.not`、`bool.and`、`bool.equal`、`bool.xor` 调用禁止传 Bool 常量，必须传变量 | 脚本/零件 | `LintEngine.MethodCalls.cs` | 是 |
| LINT-CALL-CONST-VALUE-MUST-BLANK | 语义 | Error | 默认仅允许 Float/Long/Color/Vector3/String/Bool 写常量值；Enum 常量及 create.canvas 中 Canvas 常量除外，其它类型常量必须留空（不允许写值） | 脚本/零件 | MCP_Lint_Spec.md “方法参数签名一致性” | 是 |
| LINT-EVENT-DEVICE-MISMATCH | 结构 | Error | 事件类型不属于该零件类型的允许列表（底层硬编码的事件清单）：零件/机械/地图/注入零件不得包含未定义的事件 | 脚本/零件/机械/地图 | MCP_Lint_Spec.md "事件类型→零件类型映射" | 是 |
| LINT-STRUCT-MISSING-IDENTITY | 结构 | Error | Map/Mech/Device 目录名为空或无法获取时报错；从目录名推导标识是原版的正常行为，不视为错误 | 地图/机械/零件 | Core_Entity_Model_Spec.md "标识与溯源约束" | 是 |
| LINT-STRUCT-DEVICE-MISSING-JSON | 结构 | Error | mech.json 引用的零件目录缺少 device.json | 机械 | Core_Entity_Model_Spec.md “目录与结构完整性/零件目录” | 是 |
| LINT-STRUCT-MECH-MISSING-JSON | 结构 | Error | 机械根目录缺少 mech.json | 机械 | Core_Entity_Model_Spec.md “目录与结构完整性/机械目录” | 是 |
| LINT-STRUCT-MECH-DEVICE-NOT-FOUND | 结构 | Error | mech.json 中列出的零件目录在磁盘不存在 | 机械 | Core_Entity_Model_Spec.md “目录与结构完整性/机械目录” | 是 |
| LINT-STRUCT-MECH-ORPHAN-DEVICE | 结构 | Warning | 机械根目录下存在未在 mech.json 中引用的零件目录（悬挂零件） | 机械 | Core_Entity_Model_Spec.md “目录与结构完整性/机械目录” | 是 |
| LINT-STRUCT-SCRIPT-ORPHAN | 结构 | Warning | list.json 与脚本文件不一致（列表缺文件或文件未入列表），或存在未被任何 list/guid 引用的独立脚本 | 机械/地图/代码仓库 | MCP_Lint_Spec.md “脚本挂载完整性” | 是 |
| LINT-STRUCT-GUID-DUPLICATE | 结构 | Error | 工程内存在重复 GUID | 工程 | 规则增补：GUID 唯一性约束 | 是 |
| LINT-STRUCT-MAP-MULTIPLE | 结构 | Warning | 项目根存在多个 Map 目录或缺少名为 main 的 Map 目录时警告 | 工程 | MCP_Lint_Spec.md "项目根结构" | 是 |
| LINT-STRUCT-TABLE-FORMAT | 结构 | Error | 表格（.xlsx）不符合原版读取规则（列名/类型行/ID/单元格类型转换） | 工程 | `LintEngine.Tables.cs` | 是 |
| LINT-STRUCT-CANVAS-MALFORMED | 结构 | Error | Canvas 语义畸形：字段顺序/必填/过时字段/字段依赖契约（shadow/outline/fill/layout/九宫格五件套共存）/布局组合/TextPro 禁用字段/运行时基础值错误（含独立 .canvas、独立 HTML、内联 HTML、内联 create.canvas） | 工程/脚本 | `legacy/core/canvas/htmlview/CanvasLayoutValidator.cs` | 是 |
| LINT-TEXT-UNSUPPORTED-CHARACTER | 文本 | Error | BEScript 源码或 Canvas 解析出的字符串包含不支持字符；标准为 BMP 非控制字符、Tab、换行、回车 | 脚本/Canvas | MCP_Lint_Spec.md "文本字符支持范围" | 是 |
| LINT-STRUCT-UI-NAME-MISMATCH | 结构 | Warning | 独立 .canvas 文件名与布局名称不一致（或布局缺少名称）时警告 | 工程 | MCP_Lint_Spec.md "UI 布局规范/名称一致性" | 是 |
| LINT-STRUCT-UI-INLINE-BINARY | 结构 | Warning | 脚本内联二进制 UI 布局时警告，需迁移到独立 ui 文件 | 脚本 | MCP_Lint_Spec.md "UI 布局规范" | 是 |
| LINT-CALL-RECURSION | 调用关系 | Error | 调用图存在直接或间接递归（静态可确定的调用路径形成环路） | 机械/地图 | MCP_Lint_Spec.md "调用关系" | 是 |
| LINT-CALL-RECURSION-POTENTIAL | 调用关系 | Warning | 动态/无法静态展开的调用链存在潜在递归风险（需提示可能形成自调用），调用内置零件方法时跳过检查 | 机械/地图 | MCP_Lint_Spec.md "调用关系/潜在动态调用" | 是 |
| LINT-STRUCT-DEVICE-LOGICNAME-DUPLICATE | 结构 | Warning | 同一地图/机械内零件逻辑别名（device.json ext.logicName）重复 | 零件/地图/机械 | 规则增补：动态调用唯一性约束 | 是 |
| LINT-CALL-ACT-TARGET-ALIAS-NOT-UNIQUE | 调用关系 | Warning | `act` 按逻辑别名解析目标时命中多个零件，目标不唯一 | 脚本/零件 | 规则增补：静态调用目标唯一性 | 是 |
| LINT-CALL-ACT-TARGET-ALIAS-NOT-FOUND | 调用关系 | Error | `act` 按逻辑别名解析目标时在当前作用域内找不到任何匹配零件 | 脚本/零件 | 规则增补：静态调用目标存在性 | 是 |
| LINT-CALL-ACT-SELF-MUST-USE-THIS | 调用关系 | Error | `act` 静态解析目标为自身时，必须使用 `this`，禁止别名或数字写法 | 脚本/零件 | 规则增补：自身调用写法约束 | 是 |
| LINT-CALL-DYNAMIC-METHOD-NOT-FOUND | 调用关系 | Error | 常规动态调用的方法名参数是字符串常量时，若全工程找不到同名非私有自定义脚本则报错 | 脚本/零件 | 规则增补：动态调用目标存在性 | 是 |
| LINT-CALL-DYNAMIC-MAP-METHOD-FORMAT | 调用关系 | Error | 地图动态调用（分机带返回值/分机无返回值/主机）的方法字符串常量必须是 `零件名.方法名` | 脚本/零件 | 规则增补：地图动态调用格式约束 | 是 |
| LINT-CALL-DYNAMIC-MAP-METHOD-NOT-FOUND | 调用关系 | Error | 地图动态调用在限定零件类型范围内找不到同名非私有自定义脚本 | 脚本/零件 | 规则增补：地图动态调用目标存在性 | 是 |
| LINT-NAME-CLASH-BUILTIN | 命名 | Error | 用户脚本方法名与内置零件方法名完全相同直接报错（act 调用写法一致，禁止重名） | 脚本/零件 | MCP_Lint_Spec.md "命名/内置方法冲突" | 是 |

| LINT-VAR-INLINABLE-CONST | 语义 | Warning | 局部变量由 create 系列函数（float/long/bool/color/string/Vector3）用常量创建且仅赋值一次，所有消费处的实参位置均接受常量时，建议内联为常量 | 脚本/零件 | MCP_Lint_Spec.md "变量可内联" | 是 |
| LINT-VAR-INLINABLE-EXPR | 语义 | Warning | if 条件支持直接使用 Bool 返回值表达式，无需单独声明 Bool 变量并经 create.bool 包装；要求单赋值单消费 | 脚本/零件 | MCP_Lint_Spec.md "变量可内联" | 是 |

## 待规范补充（B 类：当前为空）
