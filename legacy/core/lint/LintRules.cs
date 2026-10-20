namespace Toolkit.Core.Lint;

/// <summary>
/// 仅收录规范已明确的规则；未明确的条款不实现、不推测。
/// </summary>
public static class LintRuleIds
{
    /// <summary>
    /// AST 构建或语法错误
    /// </summary>
    public const string AstParseError = "LINT-AST-PARSE-ERROR";

    /// <summary>
    /// Lint 引擎自身错误（如基础数据缺失导致校验无法继续）
    /// </summary>
    public const string LintEngineError = "LINT-LINT-ERROR";

    /// <summary>
    /// 使用 BEScript 语法关键字作为标识符（变量/函数/脚本名等）
    /// </summary>
    public const string NamingReservedKeyword = "LINT-NAME-KEYWORD";

    /// <summary>
    /// 标识符包含 ' 分组符号，建议使用 _ 替代以减少转义负担
    /// </summary>
    public const string NamingApostropheDiscouraged = "LINT-NAME-APOSTROPHE";

    /// <summary>
    /// Map/Mech/Device 标识缺失时报错，禁止从目录名推导标识
    /// </summary>
    public const string StructMissingIdentity = "LINT-STRUCT-MISSING-IDENTITY";

    /// <summary>
    /// mech.json 引用的零件目录缺少 device.json
    /// </summary>
    public const string StructDeviceMissingJson = "LINT-STRUCT-DEVICE-MISSING-JSON";

    /// <summary>
    /// 机械根目录缺少 mech.json
    /// </summary>
    public const string StructMechMissingJson = "LINT-STRUCT-MECH-MISSING-JSON";

    /// <summary>
    /// mech.json 中列出的零件目录在磁盘不存在
    /// </summary>
    public const string StructMechDeviceNotFound = "LINT-STRUCT-MECH-DEVICE-NOT-FOUND";

    /// <summary>
    /// 机械根目录下存在未在 mech.json 中引用的零件目录（悬挂零件）
    /// </summary>
    public const string StructMechOrphanDevice = "LINT-STRUCT-MECH-ORPHAN-DEVICE";

    /// <summary>
    /// list.json 与脚本文件不一致（列表缺文件或文件未入列表），或存在未被任何 list/guid 引用的独立脚本
    /// </summary>
    public const string StructScriptOrphan = "LINT-STRUCT-SCRIPT-ORPHAN";

    /// <summary>
    /// 工程内存在重复 GUID
    /// </summary>
    public const string StructGuidDuplicate = "LINT-STRUCT-GUID-DUPLICATE";

    /// <summary>
    /// 项目根存在多个 Map 目录或缺少名为 main 的 Map 目录时警告
    /// </summary>
    public const string StructMapMultiple = "LINT-STRUCT-MAP-MULTIPLE";

    /// <summary>
    /// 独立 canvas 文件名与布局名称不一致时警告
    /// </summary>
    public const string StructUiNameMismatch = "LINT-STRUCT-UI-NAME-MISMATCH";

    /// <summary>
    /// 脚本内联二进制 UI 布局时警告，需迁移到独立 ui 文件
    /// </summary>
    public const string StructUiInlineBinary = "LINT-STRUCT-UI-INLINE-BINARY";

    /// <summary>
    /// 调用图存在直接或间接递归（静态可确定的调用路径形成环路）
    /// </summary>
    public const string CallRecursion = "LINT-CALL-RECURSION";

    /// <summary>
    /// 动态/无法静态展开的调用链存在潜在递归风险（需提示可能形成自调用）
    /// </summary>
    public const string CallRecursionPotential = "LINT-CALL-RECURSION-POTENTIAL";

    /// <summary>
    /// act 按逻辑别名解析目标时命中多个零件，目标不唯一
    /// </summary>
    public const string CallActAliasNotUnique = "LINT-CALL-ACT-TARGET-ALIAS-NOT-UNIQUE";

    /// <summary>
    /// act 按逻辑别名解析目标时在当前作用域内找不到任何匹配零件
    /// </summary>
    public const string CallActAliasNotFound = "LINT-CALL-ACT-TARGET-ALIAS-NOT-FOUND";

    /// <summary>
    /// act 调用静态解析到自身，但未使用 this 写法
    /// </summary>
    public const string CallActSelfMustUseThis = "LINT-CALL-ACT-SELF-MUST-USE-THIS";

    /// <summary>
    /// act 调用所需零件能力在目标零件实例上不可用
    /// </summary>
    public const string CallActCapabilityUnavailable = "LINT-CALL-ACT-CAPABILITY-UNAVAILABLE";

    /// <summary>
    /// 同一地图/机械内零件逻辑别名（device.json ext.logicName）重复
    /// </summary>
    public const string StructDeviceLogicNameDuplicate = "LINT-STRUCT-DEVICE-LOGICNAME-DUPLICATE";

    /// <summary>
    /// 常规动态调用的方法名为字符串常量，但全工程无同名非私有自定义脚本
    /// </summary>
    public const string CallDynamicMethodNotFound = "LINT-CALL-DYNAMIC-METHOD-NOT-FOUND";

    /// <summary>
    /// 地图动态调用的方法字符串格式错误，必须是"零件别名.方法名"
    /// </summary>
    public const string CallDynamicMapMethodFormat = "LINT-CALL-DYNAMIC-MAP-METHOD-FORMAT";

    /// <summary>
    /// 地图动态调用的方法名部分在目标零件类型范围内不存在
    /// </summary>
    public const string CallDynamicMapMethodNotFound = "LINT-CALL-DYNAMIC-MAP-METHOD-NOT-FOUND";

    /// <summary>
    /// 用户脚本方法名与内置零件动作重名（act 调用写法一致，禁止重名）
    /// </summary>
    public const string NamingClashBuiltinMethod = "LINT-NAME-CLASH-BUILTIN";

    /// <summary>
    /// 自定义方法参数与零件全局变量同名
    /// </summary>
    public const string ParamClashGlobal = "LINT-PARAM-CLASH-GLOBAL";

    /// <summary>
    /// 零件全局变量与同零件脚本同名，BEScript 支持但 TS 视图不推荐
    /// </summary>
    public const string GlobalClashScriptName = "LINT-GLOBAL-CLASH-SCRIPT-NAME";

    /// <summary>
    /// 零件全局变量名称与底层方法参数同名（如系统事件参数）
    /// </summary>
    public const string GlobalClashBuiltinParam = "LINT-GLOBAL-CLASH-BUILTIN-PARAM";

    /// <summary>
    /// 零件全局变量名称与同零件脚本的返回名同名
    /// </summary>
    public const string GlobalClashReturn = "LINT-GLOBAL-CLASH-RETURN";

    /// <summary>
    /// 局部/返回/参数等使用的变量名与全局变量同名但类型与全局声明不一致
    /// </summary>
    public const string VarGlobalTypeMismatch = "LINT-VAR-GLOBAL-TYPE-MISMATCH";

    /// <summary>
    /// 底层注册方法调用参数与签名不一致（数量/类型不匹配，仅允许 Any/UI 基类互通）
    /// </summary>
    public const string CallParamSignatureMismatch = "LINT-CALL-PARAM-SIGNATURE-MISMATCH";

    /// <summary>
    /// 调用了被标记为不推荐或已作废的内置方法
    /// </summary>
    public const string CallNotRecommended = "LINT-CALL-NOT-RECOMMENDED";

    /// <summary>
    /// String 参数不允许隐式转为文字（仅允许显式标注支持隐式转换的位置）
    /// </summary>
    public const string CallParamImplicitToStringNotAllowed = "LINT-CALL-PARAM-IMPLICIT-TOSTRING-NOT-ALLOWED";

    public const string FindExcel2KeyMustNotBeId = "LINT-FINDEXCEL2-KEY-MUST-NOT-ID";

    /// <summary>
    /// 内置方法签名中 Any 类型参数必须传变量，禁止传常量
    /// </summary>
    public const string CallParamAnyMustBeVar = "LINT-CALL-PARAM-ANY-MUST-VAR";

    /// <summary>
    /// if/while 条件中的指定 Bool 函数调用必须传变量，禁止传 Bool 常量
    /// </summary>
    public const string CallConditionConstMustBeVar = "LINT-CALL-CONDITION-CONST-MUST-VAR";

    /// <summary>
    /// 除 Float/Long/Color/Vector3/String/Bool 外，其它类型常量必须留空（不允许写值）
    /// </summary>
    public const string CallConstValueMustBeBlank = "LINT-CALL-CONST-VALUE-MUST-BLANK";

    /// <summary>
    /// Struct.get* 第二个参数未复制（Bool=0）时警告
    /// </summary>
    public const string StructGetterNoCopy = "LINT-STRUCT-GETTER-NO-COPY";

    /// <summary>
    /// 表格（.xlsx）格式不符合原版读取规则
    /// </summary>
    public const string StructTableFormat = "LINT-STRUCT-TABLE-FORMAT";

    /// <summary>
    /// setting.json 指向的颜色表存在但不符合颜色表格式
    /// </summary>
    public const string StructColorPaletteFormat = "LINT-STRUCT-COLOR-PALETTE-FORMAT";

    /// <summary>
    /// Canvas 语义畸形：字段顺序/必填/过时字段/布局组合/运行时基础值等统一归此规则
    /// </summary>
    public const string StructCanvasMalformed = "LINT-STRUCT-CANVAS-MALFORMED";

    /// <summary>
    /// BEScript 源码或 Canvas 字符串中包含不支持字符
    /// </summary>
    public const string UnsupportedTextCharacter = "LINT-TEXT-UNSUPPORTED-CHARACTER";

    /// <summary>
    /// Canvas.readUI* 使用的静态路径在已知布局中不存在
    /// </summary>
    public const string CallCanvasUiPathNotFound = "LINT-CALL-CANVAS-UI-PATH-NOT-FOUND";

    /// <summary>
    /// 事件类型不属于该零件类型的允许列表（底层硬编码的事件清单）：零件/机械/地图/注入零件不得包含未定义的事件
    /// </summary>
    public const string EventDeviceMismatch = "LINT-EVENT-DEVICE-MISMATCH";

    /// <summary>
    /// 自定义脚本名与该零件类型允许的事件名完全重名（act 调用写法一致，禁止重名）
    /// </summary>
    public const string NamingClashEventName = "LINT-NAME-CLASH-EVENT";

    /// <summary>
    /// 机械产生器(1110) 同名事件脚本组内存在与目标机械核心(CoreDevice) 同名的脚本，
    /// 注入时会产生覆盖冲突。仅"事件脚本组名重叠"不报警（底层硬编码常态）。
    /// </summary>
    public const string BirthBuildingEventNameClashCore = "LINT-BIRTH-BUILDING-EVENT-CLASH-CORE";

    /// <summary>
    /// 变量声明后未被使用（局部变量或全局变量）
    /// </summary>
    public const string VarUnused = "LINT-VAR-UNUSED";

    /// <summary>
    /// 局部变量由 create 系列函数用常量创建且仅赋值一次，所有消费处参数位置均接受常量时，可内联为常量
    /// </summary>
    public const string VarInlinableConst = "LINT-VAR-INLINABLE-CONST";

    /// <summary>
    /// if 条件支持直接使用 Bool 返回值表达式，无需单独声明 Bool 变量并经 create.bool 包装
    /// </summary>
    public const string VarInlinableExpr = "LINT-VAR-INLINABLE-EXPR";

    /// <summary>
    /// 设置贴图/加载图片等方法传入了工程中找不到对应文件的常量文件名
    /// </summary>
    public const string CallFilenameNotFound = "LINT-CALL-FILENAME-NOT-FOUND";

}
