using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

/// <summary>
/// Act 目标查找类型。
/// </summary>
internal enum ActTargetKind
{
    /// <summary>this 引用</summary>
    Self,
    /// <summary>按名称查找</summary>
    ByName,
    /// <summary>按 ID 查找</summary>
    ById,
}

/// <summary>
/// Act 目标查找结果。
/// </summary>
internal readonly record struct ActTargetLookupResult(
    ActTargetKind Kind,
    ActTargetInfo Info,
    string? Name,   // ByName 时使用
    int? Id         // ById 时使用
);

/// <summary>
/// 表示一个 ACT 目标的信息。
/// </summary>
public sealed record ActTargetInfo(
    int DeviceId,
    int DeviceTypeId,
    string? LogicName,
    string TsTypeName,
    string ActionOwnerTypeName,
    IReadOnlyDictionary<string, string> ScriptRawNameToMethodName,
    IReadOnlyDictionary<string, string> ScriptMethodNameToRawName,
    IReadOnlyDictionary<string, string> ScriptRawNameToReturnType,
    IReadOnlySet<string> PrivateScriptRawNames,
    IReadOnlySet<string> AsyncRawNames,
    IReadOnlySet<string> AsyncWithReturnRawNames
);

/// <summary>
/// ACT 目标映射基础数据（用于缓存）
/// </summary>
public sealed record ActTargetMapBase(
    IReadOnlyDictionary<int, ActTargetInfo> ById,
    IReadOnlyDictionary<string, ActTargetInfo> ByName
);

/// <summary>
/// 表示 ACT 目标映射，包含 Self 和按名称/ID/类名索引的目标。
/// </summary>
/// <remarks>
/// <para><b>查找规则</b></para>
/// <list type="bullet">
///   <item>ByClassName：TS→BES 方向的唯一入口，类名↔ID双射，ID来源唯一是文件名</item>
///   <item>ByName/ById：BES→TS 方向使用，按 ownerNameIsAlias 决定用哪个</item>
///   <item>Self：当前零件自身，用于 this 引用</item>
/// </list>
/// <para><b>机械产生器规则</b></para>
/// <list type="bullet">
///   <item>机械产生器(1110)是地图级零件，ByClassName 中 DeviceTypeId 就是 1110</item>
///   <item>1110 内部：this 指向目标机械核心(类型1)，不可能出现 1110.this</item>
///   <item>其他零件：可以调用 1110./XXX.XXX</item>
/// </list>
/// </remarks>
internal sealed record ActTargetMap(
    ActTargetInfo Self,
    IReadOnlyDictionary<string, ActTargetInfo> ByName,
    IReadOnlyDictionary<int, ActTargetInfo> ById,
    IReadOnlyDictionary<string, ActTargetInfo> ByClassName
)
{
    internal static readonly ActTargetMap Empty = new(
        new ActTargetInfo(
            DeviceTypeIds.MapCoreDevice,
            DeviceTypeIds.MapCoreDevice,
            string.Empty,
            BescriptDtsGenerator.BuildDeviceTypeTypeName(DeviceTypeIds.MapCoreDevice),
            "MapCoreDevice",
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal)),
        new Dictionary<string, ActTargetInfo>(StringComparer.Ordinal),
        new Dictionary<int, ActTargetInfo>(),
        new Dictionary<string, ActTargetInfo>(StringComparer.Ordinal));
}

/// <summary>
/// 脚本转换的上下文，包含变量表、方法注册表等信息。
/// </summary>
/// <remarks>
/// <para><b>局部变量规则</b></para>
/// <list type="number">
///   <item>
///     <term>方法级作用域</term>
///     <description>
///       BeScript 不存在 TS 块级局部变量覆盖机制，TS 视图中的 var 声明统一映射到方法级局部变量表。
///     </description>
///   </item>
///   <item>
///     <term>重复声明</term>
///     <description>
///       同名同类型 var 允许重复出现并按同一局部变量处理；同名不同类型报错。
///     </description>
///   </item>
///   <item>
///     <term>形参与全局变量</term>
///     <description>
///       var 禁止与方法形参或全局变量重名；全局变量必须通过 this.xxx 访问。
///     </description>
///   </item>
///   <item>
///     <term>返回值变量</term>
///     <description>
///       有返回值的方法必须至少有一处返回值变量来源；若返回值变量是形参，形参声明即为来源。
///     </description>
///   </item>
/// </list>
/// </remarks>
internal sealed class ScriptConversionContext
{
    public Dictionary<string, BescriptVarType> DeclaredVariables { get; }

    private readonly Dictionary<string, MethodEnumType> _variableEnumTypes = new(StringComparer.Ordinal);

    /// <summary>方法参数名称集合。</summary>
    public HashSet<string> ParameterNames { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, BescriptVarType> GlobalVariables { get; }

    public BuiltinMethodRegistry MethodRegistry { get; }

    public IReadOnlyDictionary<string, string>? MethodRawNameMap { get; }

    public IReadOnlyDictionary<string, string>? MethodDisplayNameMap { get; }

    public ActTargetMap ActTargetMap { get; }

    /// <summary>
    /// 工程模型，用于 GUID 解析等操作
    /// </summary>
    public ProjectModel? Model { get; set; }

    public string? ExpectedReturnVarName { get; set; }

    public string? ExpectedReturnType { get; set; }

    /// <summary>嵌套调用展开的临时变量计数器（per-script，从 1 开始）。</summary>
    internal int NestedExpansionCounter { get; set; }

    internal string GenerateExpansionTempVarName()
    {
        string name;
        do
        {
            name = $"__n_{++NestedExpansionCounter}";
        } while (DeclaredVariables.ContainsKey(name) || GlobalVariables.ContainsKey(name) || ParameterNames.Contains(name));
        return name;
    }

    /// <summary>嵌套调用展开期间注入 Warning 的目标列表，由 TryMapBody 设置。</summary>
    internal List<ToolError> ExpansionWarnings { get; set; } = new();

    /// <summary>嵌套调用展开期间 Warning 的路径信息，由 TryMapBody 设置。</summary>
    internal string? ExpansionTargetPath { get; set; }

    public ScriptConversionContext(
        BuiltinMethodRegistry methodRegistry,
        ActTargetMap actTargetMap,
        IReadOnlyDictionary<string, BescriptVarType> globalVariables,
        IReadOnlyDictionary<string, string>? methodRawNameMap = null,
        IReadOnlyDictionary<string, string>? methodDisplayNameMap = null)
    {
        MethodRegistry = methodRegistry;
        DeclaredVariables = new Dictionary<string, BescriptVarType>(StringComparer.Ordinal);
        GlobalVariables = new Dictionary<string, BescriptVarType>(globalVariables, StringComparer.Ordinal);
        MethodRawNameMap = methodRawNameMap;
        ActTargetMap = actTargetMap;
        MethodDisplayNameMap = methodDisplayNameMap;
    }

    internal void RegisterParameter(string tsName, string tsTypeName)
    {
        BescriptVarType varType;
        if (string.Equals(tsTypeName, "BeAnyParam", StringComparison.Ordinal) ||
            string.Equals(tsTypeName, "AnyParam", StringComparison.Ordinal))
            varType = BescriptVarType.Any;
        else
        {
            varType = TsViewTypeUtils.TryParseBescriptVarType(tsTypeName) ?? throw new InvalidOperationException($"参数类型无法识别: {tsName}:{tsTypeName}");
        }
        if (varType == BescriptVarType.Enum)
        {
            RegisterParameter(tsName, varType, TsViewTypeUtils.ResolveMethodEnumTypeFromTsTypeName(tsTypeName));
            return;
        }

        RegisterParameter(tsName, varType);
    }

    internal void RegisterParameter(string tsName, BescriptVarType varType, MethodEnumType? enumType = null)
    {
        if (GlobalVariables.ContainsKey(tsName))
            throw new InvalidOperationException($"参数 '{tsName}' 与零件全局变量冲突，请重命名。");

        DeclaredVariables[tsName] = varType;
        ParameterNames.Add(tsName);
        if (enumType is not null)
            _variableEnumTypes[tsName] = enumType;
    }

    internal IReadOnlyDictionary<string, MethodEnumType> VariableEnumTypes => _variableEnumTypes;
}
