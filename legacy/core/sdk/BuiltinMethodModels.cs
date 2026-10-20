using Toolkit.Core.Bescript;

namespace Toolkit.Core.Sdk;

/// <summary>方法类别枚举</summary>
public enum MethodCategory
{
    /// <summary>fun - 全局函数</summary>
    GlobalFunc,
    /// <summary>varf - 变量方法</summary>
    VarFunc,
    /// <summary>act - 零件动作</summary>
    DeviceAction,
    /// <summary>dvf - 编辑器函数</summary>
    EditorFunc
}

public enum CompressionKind
{
    NumericSuffix,
    TypeSuffix,
    TypeSuffixWithNumeric,
    TypeSuffixParam
}

/// <summary>推荐等级</summary>
public enum RecommendLevel
{
    /// <summary>默认</summary>
    Default,
    /// <summary>不推荐（用于后续检查/提示）</summary>
    NotRecommended
}

internal static class RecommendationReasons
{
    public const string DefaultNotRecommendedReason = "标记为不推荐使用";
}

/// <summary>SDK 方法的异步模式。</summary>
public enum SdkAsyncMode
{
    /// <summary>同步方法</summary>
    Sync,
    /// <summary>异步方法（原版 boolAsync=true，调用方需要 await）</summary>
    Async,
    /// <summary>原版未标 boolAsync 但实际是异步的（动态调用等）。TS 视图按异步处理（Promise 包装 + 需要 await），.code 序列化不生成 async 关键字。</summary>
    TsAsync
}

/// <summary>方法作用域</summary>
public enum MethodScope
{
    /// <summary>通用：编辑器和局内均可用</summary>
    Universal,
    /// <summary>仅局内可用</summary>
    InGameOnly,
    /// <summary>编辑器专用：仅在编辑模式下可用，生成TS视图时跳过</summary>
    EditorOnly
}

public sealed record CompressionHint(
    CompressionKind Kind,
    string BaseName,
    IReadOnlyList<string> AllowedTypes);

/// <summary>
/// 枚举成员定义。
/// 【设计原则】
/// 1. 本名（Name）必须与底层反射名称完全一致，用于注册匹配。
/// 2. 别名（Alias）仅用于视图生成，不参与注册匹配。
/// 3. 若本名形如 A@B，则 A@B 整体仍是本名；B 只是可选的读名候选，不得反过来污染 Name。
/// 4. 对外读取名称时必须走 EffectiveName，优先取别名，无别名用本名。
/// </summary>
public sealed record EnumMember(
    /// <summary>
    /// 本名：必须与底层反射名称完全一致。
    /// 【严禁！严禁！严禁！】直接访问此字段！
    /// 显示名称通过本记录的 EffectiveName 属性读取。
    /// 除此之外任何地方直接访问 _internal_raw_name 都是绝对禁止的破坏行为！
    /// 严禁！严禁！严禁！添加任何所谓"RawName"转发属性！那是最愚蠢的绕过！
    /// 严禁！严禁！严禁！添加任何所谓"RawName"转发属性！那是最愚蠢的绕过！
    /// 严禁！严禁！严禁！添加任何所谓"RawName"转发属性！那是最愚蠢的绕过！
    /// </summary>
    string _internal_raw_name,
    /// <summary>别名：用于视图生成时替代本名。</summary>
    string? Alias = null,
    /// <summary>成员说明。</summary>
    string? Description = null
)
{
    /// <summary>对外读取名称唯一入口：优先取别名，无别名用本名。</summary>
#pragma warning disable TK005 // Alias 是可选参数，?? 是合法偏好链，非幻觉防御
    public string EffectiveName => Alias ?? _internal_raw_name;
#pragma warning restore TK005

}

/// <summary>
/// 底层方法枚举分组定义。
/// 【设计原则】
/// 1. 本名（Name）必须与底层反射名称完全一致，用于注册匹配。
/// 2. 本名一致即说明 SDK 与底层对齐，必须保持精准一致性。
/// 3. 别名（Alias）仅用于视图生成，不参与注册匹配。
/// 4. 本名与别名的映射关系在注册时手动指定，依赖手动管理维护。
/// 5. 若本名形如 A@B，则 A@B 整体仍是本名；B 或按语义整理出的读名只能放在 Alias。
/// 别名是显示专用的美化，集中在SDK注册处手动指定完成。
/// 方法名级别必须是全程本名！无别名概念！
/// 别名仅针对：参数位、枚举成员。
/// </summary>
public sealed record MethodEnumType(
    /// <summary>本名：必须与底层反射名称完全一致，用于注册匹配。</summary>
    string Name,
    string Summary,
    /// <summary>枚举成员列表，支持别名。</summary>
    IReadOnlyList<EnumMember>? Options = null
)
{
    /// <summary>虚拟分组：参数枚举分组跟随 Varf 调用主体，不是真实枚举分组</summary>
    public static readonly MethodEnumType FollowVarfOwner = new("@FollowVarfOwner", string.Empty);

    public string DisplayTypeName => $"Enum<{Name}>";

    /// <summary>枚举成员有效名称列表。</summary>
#pragma warning disable TK005 // Options 因 C# record 默认参数值限制无法用非空集合，?? [] 是合法偏好链
    public IReadOnlyList<string> EffectiveOptions => Options?.Select(static o => o.EffectiveName).ToList() ?? [];
#pragma warning restore TK005

    public MethodEnumType(string Name, string Summary, IReadOnlyList<string> stringOptions)
        : this(Name, Summary, stringOptions.Select(static n => new EnumMember(n)).ToList()) { }

    public void Validate()
    {
        if (!BeScriptNaming.Identifier.IsMatch(Name))
        {
            throw new ArgumentException($"枚举分组名 '{Name}' 不符合标识符规则", nameof(Name));
        }
    }
}

/// <summary>
/// 参数定义。
/// 【设计原则】
/// 1. 本名（Name）必须与底层反射名称完全一致，用于注册匹配。
/// 2. 本名一致即说明 SDK 与底层对齐，必须保持精准一致性。
/// 3. 别名（Alias）仅用于视图生成，不参与注册匹配。
/// 4. 本名与别名的映射关系在注册时手动指定，依赖手动管理维护。
/// 5. 若本名形如 A@B，则 A@B 整体仍是本名；B 或按语义整理出的读名只能放在 Alias。
/// 别名是显示专用的美化,集中在SDK注册处手动指定完成
/// 方法名级别必须是全程本名!无别名概念
/// 别名仅针对参数位
/// </summary>
public sealed record MethodParameter(
    /// <summary>
    /// 本名：必须与底层反射名称完全一致，用于注册匹配。
    /// 【严禁！严禁！严禁！】直接访问此字段！
    /// 显示名称通过本记录的 EffectiveName 属性读取。
    /// 除此之外任何地方直接访问 _internal_raw_name 都是绝对禁止的破坏行为！
    /// 严禁！严禁！严禁！添加任何所谓"RawName"转发属性！那是最愚蠢的绕过！
    /// 严禁！严禁！严禁！添加任何所谓"RawName"转发属性！那是最愚蠢的绕过！
    /// 严禁！严禁！严禁！添加任何所谓"RawName"转发属性！那是最愚蠢的绕过！
    /// </summary>
    string _internal_raw_name,
    string TypeName,
    string? Description = null,
    bool AllowImplicitToString = false,
    MethodEnumType? EnumType = null,
    /// <summary>别名：用于视图生成时替代本名，不参与注册匹配。</summary>
    string? Alias = null,
    /// <summary>
    /// 参数位接收自定义脚本方法名。只给真实 fun/varf/act 回调或动态调用方法名参数使用：
    /// TS 视图必须写原始 BeScript 名称，不能写 X_... 转义名；是否检查只能由 SDK 显式标记决定，
    /// 禁止用参数名包含 method / callback 等文本猜语义。UIRect.scrollView.method 是枚举参数，不能标记。
    /// </summary>
    bool IsScriptMethodName = false
)
{
    /// <summary>对外显示类型名：优先取枚举名，无枚举用原始类型名。</summary>
#pragma warning disable TK005 // EnumType 是可选参数，?? 是合法偏好链，非幻觉防御
    public string DisplayTypeName => EnumType?.DisplayTypeName ?? TypeName;
#pragma warning restore TK005

    /// <summary>
    /// 对外读取名称唯一入口：优先取别名，无别名用本名。
    /// 【使用场景】视图生成（BescriptDtsGenerator 等），不用于注册匹配。
    /// </summary>
#pragma warning disable TK005 // Alias 是可选参数，?? 是合法偏好链，非幻觉防御
    public string EffectiveName => Alias ?? _internal_raw_name;
#pragma warning restore TK005

    /// <summary>验证参数定义的有效性</summary>
    public bool IsValid()
    {
        if (EnumType is not null &&
            !string.Equals(TypeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal))
        {
            return false;
        }

        // IsValid 是公共 API 边界校验，空名称的参数定义无效
#pragma warning disable TK011
        return EffectiveName != string.Empty;
#pragma warning restore TK011
    }
}

/// <summary>
/// 方法签名定义。
/// 【设计原则】
/// 1. 方法名（MethodName）必须与底层反射名称完全一致，用于注册匹配。
/// 2. 方法名级别必须是全程本名！无别名概念！
/// 3. MapAlias 不是"别名"，是 TS 视图生成的技术映射标识，仅用于 BuiltinMethodMap 双射冲突解决。
/// 4. 别名仅针对：参数位、枚举成员。
/// </summary>
public sealed record MethodSignature(
    MethodCategory Category,
    string? OwnerType,
    /// <summary>方法名：必须与底层反射名称完全一致，全程本名！</summary>
    string MethodName,
    IReadOnlyList<MethodParameter> Parameters,
    string? ReturnType = null,
    string? Summary = null,
    string? Remarks = null,
    RecommendLevel Recommend = RecommendLevel.Default,
    MethodScope Scope = MethodScope.Universal,
    CompressionHint? CompressionHint = null,
    /// <summary>
    /// 双射冲突规避专用,非参数位那种常用"别名",极端情况下重名才用!
    /// 【注意】这不是别名！这是 TS 方法名生成的技术映射标识。
    /// 仅用于 BuiltinMethodMap 解决双射冲突，不参与注册匹配。
    /// 当此字段非空时，Map 中使用此名作为 TS 方法名，而非压缩后的名称。
    /// 这用于解决双射冲突：当两个不同的 BES 方法压缩后名称相同但不属于同一压缩组时，可以为其中一个指定别名以避免冲突。
    /// 预防非压缩的"A"方法和"A1"/"A2"/"A3"...等方法压缩出来的"A"组产生冲突
    /// </summary>
    string? MapAlias = null,
    /// <summary>是否为原版标记的已作废方法。</summary>
    bool IsObsolete = false,
    /// <summary>不推荐原因（用于文档和 d.ts 标注）。</summary>
    string? NotRecommendedReason = null,
    /// <summary>SDK 异步模式。底层 SDK 要么同步要么异步，无模糊地带。</summary>
    SdkAsyncMode AsyncMode = SdkAsyncMode.Sync,
    /// <summary>面向 Agent 的额外笔记：底层文档没直说的特性、坑点、行为陷阱。注入到 SDK 文档查询和 TS d.ts JSDoc 中，压制幻觉。</summary>
    string? Notes = null
)
{
    /// <summary>
    /// 调用此方法所需的零件能力。Lint 与 TS 视图通过 DeviceCapabilityResolver
    /// 共用能力规则，空列表表示无能力要求。
    /// </summary>
    public IReadOnlyList<DeviceCapability> RequireCapabilities { get; init; } = [];
    /// <summary>
    /// 统一"不推荐"判定标准：显式 NotRecommended 或原版已作废。
    /// </summary>
    public bool ShouldMarkNotRecommended => Recommend == RecommendLevel.NotRecommended || IsObsolete;

    public string ResolvedNotRecommendedReason
    {
        get
        {
            if (NotRecommendedReason is not null)
                return NotRecommendedReason;
            if (IsObsolete)
                return BuiltinRecommendationRules.DefaultObsoleteReason;
            return RecommendationReasons.DefaultNotRecommendedReason;
        }
    }

    /// <summary>验证方法名是否符合 BEScript 命名规则（全局函数路径或单标识符）</summary>
    public static bool IsValidMethodName(string name) => BeScriptNaming.GlobalFuncPath.IsMatch(name);

    /// <summary>按方法类别校验方法名：GlobalFunc 走路径规则，其余走标识符规则。</summary>
    private static bool IsMethodNameValid(MethodCategory category, string methodName) =>
        category == MethodCategory.GlobalFunc
            ? BeScriptNaming.GlobalFuncPath.IsMatch(methodName)
            : BeScriptNaming.Identifier.IsMatch(methodName);

    /// <summary>验证方法签名的有效性</summary>
    public bool IsValid()
    {
        if (!IsMethodNameValid(Category, MethodName)) return false;

        // VarFunc 和 DeviceAction 需要 OwnerType
        if (Category is MethodCategory.VarFunc or MethodCategory.DeviceAction)
        {
            if (OwnerType is null)
                return false;
        }

        // 验证所有参数
        foreach (MethodParameter param in Parameters)
        {
            if (!param.IsValid())
                return false;
        }

        return true;
    }

    /// <summary>验证方法签名，如果无效则抛出异常</summary>
    public void Validate()
    {
        if (!IsMethodNameValid(Category, MethodName))
        {
            string hint = Category == MethodCategory.GlobalFunc
                ? "（字母或下划线开头，允许点号/斜杠路径，段内仅包含字母、数字、下划线）"
                : "（字母或下划线开头，仅包含字母、数字、下划线）";
            throw new ArgumentException($"方法名 '{MethodName}' 不符合命名规则{hint}", nameof(MethodName));
        }

        if (Category is MethodCategory.VarFunc or MethodCategory.DeviceAction)
        {
            if (OwnerType is null)
                throw new ArgumentException($"类别 {Category} 的方法必须指定 OwnerType", nameof(OwnerType));
        }

        for (int i = 0; i < Parameters.Count; i++)
        {
            MethodParameter param = Parameters[i];
            // Validate 是公共 API 边界校验，空名称/类型的参数将导致下游生成无效内容
#pragma warning disable TK011
            if (param.EffectiveName == string.Empty)
                throw new ArgumentException($"参数 {i + 1} 的名称不能为空");
            if (param.TypeName == string.Empty)
                throw new ArgumentException($"参数 '{param.EffectiveName}' 的类型不能为空");
#pragma warning restore TK011
            if (param.EnumType is not null)
            {
                if (!string.Equals(param.TypeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal))
                    throw new ArgumentException($"参数 '{param.EffectiveName}' 只有在类型为 Enum 时才能声明枚举分组");
                if (!ReferenceEquals(param.EnumType, MethodEnumType.FollowVarfOwner))
                    param.EnumType.Validate();
            }
        }

    }
}

/// <summary>方法查询结果</summary>
public sealed record MethodQueryResult(
    IReadOnlyList<MethodSignature> Items,
    int Total
);

/// <summary>方法调用验证结果</summary>
public sealed record MethodValidationResult(
    bool IsValid,
    string? ErrorMessage,
    IReadOnlyList<string> Warnings
)
{
    public static MethodValidationResult Success()
        => new(true, null, Array.Empty<string>());

    public static MethodValidationResult Success(IEnumerable<string> warnings)
        => new(true, null, warnings.ToArray());

    public static MethodValidationResult Error(string message)
        => new(false, message, Array.Empty<string>());
}
