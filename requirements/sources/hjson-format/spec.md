# Hjson 工程格式规范

## Purpose

统一 CreatAI 工程 JSON 文件和 Canvas 内联 Hjson 的语法、解析器与规范化写出规则。工程使用 Hjson 语法承载平台原生 `SimpleJson` 的写出布局；布局差异不构成第二种解析标准。

## Requirements

### Requirement: 唯一 Hjson 语法入口

所有现役工程 JSON 文件、伴生元数据和 Canvas 内联 Hjson MUST 使用根依赖锁定的 `hjson@3.2.2` 解析。结构类型、必需字段、字段白名单和业务标量 MUST 在解析结果上由对应 Zod 或领域 Schema 校验。业务模块 MUST NOT 自研 Hjson 词法、字符串转义、引号选择、数字解析或对象遍历解析器。

Hjson 的合法输入范围以该官方解析器为准：未加引号的键和值、可省略逗号、注释、JSON 写法和 Hjson 写法均属于同一语法入口。业务格式错误在结构校验阶段直接失败，不通过字符串扫描猜测或修补。

<!-- @budget level=M -->

#### Scenario: 工程文件共享解析语义

- **WHEN** 工程设置、GUID 伴生对象、脚本列表、变量列表、零件元数据或 Canvas 内联正文被读取
- **THEN** 所有入口调用同一版本的官方 Hjson 解析器，再执行各自的结构合同；相同 Hjson 文本不会因调用模块不同而得到不同的语法解释

<!-- @anchor id=hjson-parse -->
<!-- @nonCode -->

### Requirement: 原版 SimpleJson 规范化写出

需要写回平台原生工程文本时 MUST 遵循游戏本体 `SimpleJson` 的规范化布局：键值对使用 `key:value` 且冒号后无空格；每层使用一个 ASCII 空格缩进；对象字段保持模型提供的插入顺序；根对象省略花括号，嵌套对象在键后换行并使用花括号；根数组保留方括号并逐项换行。平台向量、颜色和其他有明确原生类型的数值数组按领域规范以内联数组写出，普通列表和对象数组按原版结构换行写出。

该布局是 Hjson 语法上的规范化输出，不改变 Hjson 的解析语义。官方 `Hjson.stringify` 不提供原版的类型感知数组布局，因此领域写出器只允许组织字段顺序、节点结构和原生数值字面量；所有字符串写出 MUST 委托官方 `Hjson.stringify(value, { multiline: 'off', eol: '\\n' })`，不得重新实现转义或引号判定。没有平台写回需求的模块不得新增序列化器。

原版参考为 `legacy/core/persistence/Json/HjsonText.cs`、`legacy/core/persistence/Json/SimpleJsonWriter.cs` 和 `legacy/core/canvas/CanvasInlineNodeHjsonCodec.cs`。这些文件是格式证据，不是现役实现入口。

<!-- @budget level=M -->

#### Scenario: 规范化写出可被唯一解析入口读回

- **WHEN** 领域写出器生成工程 Hjson 或 Canvas 内联 Hjson
- **THEN** 输出符合原版布局且可由同一官方 Hjson 解析器读回；字符串中的控制字符、关键字样式文本、数字样式文本和空字符串保持字符串类型，不由写出器手工改写

<!-- @anchor id=hjson-write -->
<!-- @nonCode -->

### Requirement: 不分叉的领域合同

Canvas、工程设置、GUID 元数据、脚本列表、变量列表和零件元数据 MUST 复用本规范的解析及写出边界。领域规范只能补充字段结构、平台类型和字节布局，不得重新定义 Hjson 的引号、转义、注释、逗号、缩进或换行语义。原始文件需要保留字节时 MUST 保留原文并只提供解析投影，不得为了投影重新序列化。

#### Scenario: 领域规范引用同一 Hjson 标准

- **WHEN** 新增或修改任一 Hjson 消费模块的 OpenSpec
- **THEN** 只引用本规范的语法与规范化写出规则，并在领域章节声明自身结构边界；不存在只对某个模块生效的 Hjson 方言

<!-- @anchor id=hjson-shared-contract -->
<!-- @nonCode -->
