using Toolkit.Core.TsView;

namespace Toolkit.Core.Sdk;

/// <summary>
/// 底层方法映射表 - 初始化时从 Registry 全量生成，之后完全锁死。
/// <para>
/// 这是 BES ↔ TS 双向转换的唯一映射数据来源。
/// </para>
/// <para>
/// <b>核心设计：严格双射（Bijection）</b>
/// <list type="bullet">
///   <item>正向映射：BES方法名 → (TS方法名, 泛型类型) —— 一对一</item>
///   <item>反向映射：(TS方法名, 泛型类型, 参数数量) → BES方法名 —— 一对一</item>
/// </list>
/// 双射意味着：给定任意一侧的键，必定唯一确定另一侧的值，不存在"多候选"情况。
/// </para>
/// <para>
/// <b>设计原则：</b>
/// <list type="number">
///   <item>完整穷举：包含所有底层方法（从 Registry 全量生成）</item>
///   <item>初始化后锁死：Map 构建完成后不可变</item>
///   <item>构建时校验：如果存在双射冲突（两个 BES 方法映射到相同的 TS 键），启动时立即抛 InvalidOperationException</item>
///   <item>运行时直接查表：查询是简单的 Dictionary.TryGetValue，无任何消歧逻辑</item>
///   <item>禁止兜底：如果方法不在 Map 中，直接报错，不做猜测</item>
/// </list>
/// </para>
/// <para>
/// <b>为什么需要参数数量？</b>
/// <br/>
/// 某些 BES 方法经过压缩后会共享相同的 TS 方法名和泛型类型（如 add 和 add1 都压缩为 add），
/// 但它们的参数数量不同。参数数量作为反向映射键的一部分，确保这些方法仍能唯一区分。
/// </para>
/// </summary>
internal sealed class BuiltinMethodMap
{
    /// <summary>
    /// 正向映射条目：原BES方法名 → TS方法信息
    /// </summary>
    internal sealed record ForwardEntry(
        string TsMethodName,
        string? GenericBeTypeName
    );

    /// <summary>
    /// 反向映射键：(TS方法名, 泛型类型, 参数数量) 三元组。
    /// <para>
    /// 此键必须唯一映射到一个 BES 方法名（双射的右侧键）。
    /// 构建时如果发现两个不同的 BES 方法产生相同的 ReverseKey，将抛出异常。
    /// </para>
    /// </summary>
    internal readonly record struct ReverseKey(
        string TsMethodName,
        string? GenericBeTypeName,
        int ParameterCount
    );


    // ═══════════════════════════════════════════════════════════════════════════
    // 正向映射字典：BES方法名 → ForwardEntry
    // 查询复杂度 O(1)，无歧义
    // ═══════════════════════════════════════════════════════════════════════════
    private readonly IReadOnlyDictionary<string, ForwardEntry> _globalForward;

    // ═══════════════════════════════════════════════════════════════════════════
    // 反向映射字典：ReverseKey → BES方法名
    // 查询复杂度 O(1)，双射保证无歧义（构建时已校验唯一性）
    // ═══════════════════════════════════════════════════════════════════════════
    private readonly IReadOnlyDictionary<ReverseKey, string> _globalReverse;

    // VarFunc 映射（按 OwnerType 分组），同样遵循双射原则
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, ForwardEntry>> _varFuncForward;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<ReverseKey, string>> _varFuncReverse;

    // DeviceAction 映射（按 OwnerType 分组），同样遵循双射原则
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, ForwardEntry>> _deviceActionForward;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<ReverseKey, string>> _deviceActionReverse;


    // 单例实例
    private static readonly Lazy<BuiltinMethodMap> LazyInstance = new(
        static () => Build(BuiltinMethodRegistry.Instance),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static BuiltinMethodMap Instance => LazyInstance.Value;

    private BuiltinMethodMap(
        IReadOnlyDictionary<string, ForwardEntry> globalForward,
        IReadOnlyDictionary<ReverseKey, string> globalReverse,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ForwardEntry>> varFuncForward,
        IReadOnlyDictionary<string, IReadOnlyDictionary<ReverseKey, string>> varFuncReverse,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ForwardEntry>> deviceActionForward,
        IReadOnlyDictionary<string, IReadOnlyDictionary<ReverseKey, string>> deviceActionReverse)
    {
        _globalForward = globalForward;
        _globalReverse = globalReverse;
        _varFuncForward = varFuncForward;
        _varFuncReverse = varFuncReverse;
        _deviceActionForward = deviceActionForward;
        _deviceActionReverse = deviceActionReverse;
    }

    #region 正向映射（BES → TS）

    /// <summary>
    /// 获取全局函数的 TS 方法映射。
    /// </summary>
    public ForwardEntry? GetGlobalForward(string besMethodName)
    {
        if (_globalForward.TryGetValue(besMethodName, out ForwardEntry? entry))
            return entry;
        return null;
    }

    /// <summary>
    /// 获取 VarFunc 的 TS 方法映射。
    /// </summary>
    public ForwardEntry? GetVarFuncForward(string ownerType, string besMethodName)
    {
        if (_varFuncForward.TryGetValue(ownerType, out IReadOnlyDictionary<string, ForwardEntry>? byName) &&
            byName.TryGetValue(besMethodName, out ForwardEntry? entry))
        {
            return entry;
        }

        string? baseType = ownerType switch
        {
            "UILabel" or "UIButton" or "UIRect" or "UIInput" or "UIRawImage" or "UISpine" or "UILine" or "UICircle" or "UIAvatar" => "UI",
            _ => null
        };
        if (baseType is not null && _varFuncForward.TryGetValue(baseType, out byName) &&
            byName.TryGetValue(besMethodName, out entry))
        {
            return entry;
        }

        return null;
    }

    /// <summary>
    /// 获取 DeviceAction 的 TS 方法映射。
    /// </summary>
    public ForwardEntry? GetDeviceActionForward(string ownerType, string besMethodName)
    {
        return DeviceActionInheritance.FindInChain<ForwardEntry>(ownerType, type =>
        {
            if (_deviceActionForward.TryGetValue(type, out IReadOnlyDictionary<string, ForwardEntry>? byName) &&
                byName.TryGetValue(besMethodName, out ForwardEntry? e))
            {
                return (true, e);
            }
            return (false, default);
        });
    }

    /// <summary>
    /// 根据方法签名获取 TS 方法名。
    /// 如果在映射表中找到则返回映射后的名称，否则使用 BescriptDtsGenerator.ToTsMemberName 转换。
    /// </summary>
    public string GetTsMethodName(MethodSignature sig)
    {
        switch (sig.Category)
        {
            case MethodCategory.GlobalFunc:
                if (GetGlobalForward(sig.MethodName) is { } globalEntry)
                    return globalEntry.TsMethodName;
                break;
            case MethodCategory.VarFunc:
                if (!string.IsNullOrWhiteSpace(sig.OwnerType) &&
                    GetVarFuncForward(sig.OwnerType, sig.MethodName) is { } varFuncEntry)
                    return varFuncEntry.TsMethodName;
                break;
            case MethodCategory.DeviceAction:
                if (!string.IsNullOrWhiteSpace(sig.OwnerType) &&
                    GetDeviceActionForward(sig.OwnerType, sig.MethodName) is { } actEntry)
                    return actEntry.TsMethodName;
                break;
            case MethodCategory.EditorFunc:
                break;
            default:
                return BescriptDtsGenerator.ToTsMemberName(sig.MethodName);
        }

        return BescriptDtsGenerator.ToTsMemberName(sig.MethodName);
    }

    #endregion

    #region 反向映射（TS → BES）—— 双射查询，结果必定唯一

    /// <summary>
    /// 反向映射查询结果。
    /// </summary>
    internal readonly record struct ReverseResult(string BesMethodName, string Error)
    {
        public bool Found => BesMethodName.Length > 0;
    }

    /// <summary>
    /// 获取全局函数的原 BES 方法名。
    /// <para>
    /// <b>双射保证：</b>给定 (tsMethodName, genericBeTypeName, argCount) 三元组，
    /// 要么找到唯一对应的 BES 方法名，要么不存在。不会出现"多候选"情况。
    /// </para>
    /// </summary>
    public ReverseResult GetGlobalReverse(string tsMethodName, string? genericBeTypeName, int argCount)
    {
        string? convertedGeneric = null;
        if (genericBeTypeName is not null)
            convertedGeneric = TsViewTypeUtils.ConvertToTsTypeName(genericBeTypeName);
        ReverseKey key = new(tsMethodName, convertedGeneric, argCount);

        if (_globalReverse.TryGetValue(key, out string? found))
            return new ReverseResult(found, string.Empty);

        return new ReverseResult(string.Empty, $"未知全局方法: {tsMethodName}(泛型={genericBeTypeName}, 参数数量={argCount})");
    }

    /// <summary>
    /// 获取 VarFunc 的原 BES 方法名。
    /// <para>
    /// <b>双射保证：</b>给定 (ownerType, tsMethodName, genericBeTypeName, argCount)，
    /// 要么找到唯一对应的 BES 方法名，要么不存在。不会出现"多候选"情况。
    /// </para>
    /// </summary>
    public ReverseResult GetVarFuncReverse(string ownerType, string tsMethodName, string? genericBeTypeName, int argCount)
    {
        string? convertedGeneric = null;
        if (genericBeTypeName is not null)
            convertedGeneric = TsViewTypeUtils.ConvertToTsTypeName(genericBeTypeName);
        ReverseKey key = new(tsMethodName, convertedGeneric, argCount);

        if (_varFuncReverse.TryGetValue(ownerType, out IReadOnlyDictionary<ReverseKey, string>? byKey) &&
            byKey.TryGetValue(key, out string? found))
        {
            return new ReverseResult(found, string.Empty);
        }

        string? baseType = ownerType switch
        {
            "UILabel" or "UIButton" or "UIRect" or "UIInput" or "UIRawImage" or "UISpine" or "UILine" or "UICircle" or "UIAvatar" => "UI",
            _ => null
        };
        if (baseType is not null &&
            _varFuncReverse.TryGetValue(baseType, out byKey) &&
            byKey.TryGetValue(key, out found))
        {
            return new ReverseResult(found, string.Empty);
        }

        return new ReverseResult(string.Empty, $"未知 VarFunc 方法: {ownerType}.{tsMethodName}(泛型={genericBeTypeName}, 参数数量={argCount})");
    }

    /// <summary>
    /// 获取 DeviceAction 的原 BES 方法名。
    /// <para>
    /// <b>双射保证：</b>给定 (ownerType, tsMethodName, genericBeTypeName, argCount)，
    /// 要么找到唯一对应的 BES 方法名，要么不存在。不会出现"多候选"情况。
    /// </para>
    /// </summary>
    public ReverseResult GetDeviceActionReverse(string ownerType, string tsMethodName, string? genericBeTypeName, int argCount)
    {
        string? convertedGeneric = null;
        if (genericBeTypeName is not null)
            convertedGeneric = TsViewTypeUtils.ConvertToTsTypeName(genericBeTypeName);
        ReverseKey key = new(tsMethodName, convertedGeneric, argCount);

        string? result = DeviceActionInheritance.FindInChain<string>(ownerType, current =>
        {
            if (_deviceActionReverse.TryGetValue(current, out IReadOnlyDictionary<ReverseKey, string>? byKey) &&
                byKey.TryGetValue(key, out string? found))
            {
                return (true, found);
            }
            return (false, null);
        });

        if (result is not null)
            return new ReverseResult(result, string.Empty);

        string genericText = "无";
        if (genericBeTypeName is not null)
            genericText = genericBeTypeName;
        return new ReverseResult(string.Empty, $"未知 DeviceAction 方法: {ownerType}.{tsMethodName}(泛型={genericText}, 参数数量={argCount})");
    }

    #endregion

    #region 辅助方法

    #endregion

    #region 构建 —— 启动时执行，校验双射约束

    /// <summary>
    /// 从 Registry 构建完整的映射表。
    /// <para>
    /// <b>双射校验：</b>构建过程中会检查每个 ReverseKey 是否唯一。
    /// 如果发现两个不同的 BES 方法产生相同的 ReverseKey（即 TS方法名 + 泛型 + 参数数量 相同），
    /// 将立即抛出 <see cref="InvalidOperationException"/>，阻止应用启动。
    /// </para>
    /// <para>
    /// 这确保了运行时查询永远不会遇到"多候选"情况——所有冲突在启动时就被发现并报错。
    /// </para>
    /// </summary>
    private static BuiltinMethodMap Build(BuiltinMethodRegistry registry)
    {
        Dictionary<string, ForwardEntry> globalForward = new(StringComparer.Ordinal);
        Dictionary<ReverseKey, string> globalReverse = [];

        Dictionary<string, Dictionary<string, ForwardEntry>> varFuncForward = new(StringComparer.Ordinal);
        Dictionary<string, Dictionary<ReverseKey, string>> varFuncReverse = new(StringComparer.Ordinal);

        Dictionary<string, Dictionary<string, ForwardEntry>> deviceActionForward = new(StringComparer.Ordinal);
        Dictionary<string, Dictionary<ReverseKey, string>> deviceActionReverse = new(StringComparer.Ordinal);

        // 构建全局函数映射
        foreach (MethodSignature sig in QueryAllByCategory(registry, MethodCategory.GlobalFunc))
        {
            string besName = sig.MethodName;
            int paramCount = sig.Parameters.Count;

            // 计算 TS 方法名和泛型
            // 优先使用 MapAlias（防混淆别名），否则使用压缩名或原名
            string tsName;
            string? generic;
            if (!string.IsNullOrWhiteSpace(sig.MapAlias))
            {
                // 使用防混淆别名
                tsName = TsViewIdentifierMap.ToTsIdentifier(sig.MapAlias);
                generic = InferGenericFromSignature(sig);
            }
            else if (BescriptDtsGenerator.TryGetGlobalCompressedName(besName) is { Success: true } globalResult)
            {
                tsName = TsViewIdentifierMap.ToTsIdentifier(globalResult.CompressedName);
                generic = globalResult.GenericBeTypeName;
            }
            else
            {
                tsName = TsViewIdentifierMap.ToTsIdentifier(besName);
                generic = InferGenericFromSignature(sig);
            }

            // 正向映射：besName → (tsName, generic)
            globalForward[besName] = new ForwardEntry(tsName, generic);

            // ═══════════════════════════════════════════════════════════════════
            // 反向映射 + 双射校验
            // 如果 reverseKey 已存在，说明有两个 BES 方法映射到相同的 TS 键，
            // 这违反了双射约束，必须在启动时报错
            // ═══════════════════════════════════════════════════════════════════
            string? convertedGeneric = null;
            if (generic is not null)
            {
                convertedGeneric = TsViewTypeUtils.ConvertToTsTypeName(generic);
            }
            ReverseKey reverseKey = new(tsName, convertedGeneric, paramCount);
            if (globalReverse.TryGetValue(reverseKey, out string? existingBes))
            {
                throw new InvalidOperationException(
                    $"双射冲突 [GlobalFunc]: ({tsName}, {convertedGeneric}, {paramCount}) 已映射到 '{existingBes}'，无法再映射 '{besName}'");
            }
            globalReverse[reverseKey] = besName;
        }

        // 构建 VarFunc 映射
        foreach (MethodSignature sig in QueryAllByCategory(registry, MethodCategory.VarFunc))
        {
            if (string.IsNullOrWhiteSpace(sig.OwnerType))
            {
                continue;
            }

            string ownerType = sig.OwnerType;
            string besName = sig.MethodName;
            int paramCount = sig.Parameters.Count;

            // 优先使用 MapAlias（防混淆别名），否则使用压缩名或原名
            string tsName;
            string? generic;
            if (!string.IsNullOrWhiteSpace(sig.MapAlias))
            {
                // 使用防混淆别名
                tsName = TsViewIdentifierMap.ToTsIdentifier(sig.MapAlias);
                generic = InferGenericFromSignature(sig);
            }
            else if (BescriptDtsGenerator.TryGetVarFuncCompressedName(ownerType, besName) is { Success: true } varFuncResult)
            {
                tsName = TsViewIdentifierMap.ToTsIdentifier(varFuncResult.CompressedName);
                generic = varFuncResult.GenericBeTypeName;
            }
            else
            {
                tsName = TsViewIdentifierMap.ToTsIdentifier(besName);
                generic = InferGenericFromSignature(sig);
            }

            // 正向映射：besName → (tsName, generic)
            if (!varFuncForward.TryGetValue(ownerType, out Dictionary<string, ForwardEntry>? forwardByName))
            {
                forwardByName = new Dictionary<string, ForwardEntry>(StringComparer.Ordinal);
                varFuncForward[ownerType] = forwardByName;
            }
            forwardByName[besName] = new ForwardEntry(tsName, generic);

            // 反向映射 + 双射校验（同 GlobalFunc）
            if (!varFuncReverse.TryGetValue(ownerType, out Dictionary<ReverseKey, string>? reverseByKey))
            {
                reverseByKey = [];
                varFuncReverse[ownerType] = reverseByKey;
            }
            string? convertedGeneric = null;
            if (generic is not null)
            {
                convertedGeneric = TsViewTypeUtils.ConvertToTsTypeName(generic);
            }
            ReverseKey reverseKey = new(tsName, convertedGeneric, paramCount);
            if (reverseByKey.TryGetValue(reverseKey, out string? existingBes))
            {
                throw new InvalidOperationException(
                    $"双射冲突 [VarFunc.{ownerType}]: ({tsName}, {convertedGeneric}, {paramCount}) 已映射到 '{existingBes}'，无法再映射 '{besName}'");
            }
            reverseByKey[reverseKey] = besName;
        }

        // 构建 DeviceAction 映射
        foreach (MethodSignature sig in QueryAllByCategory(registry, MethodCategory.DeviceAction))
        {
            if (string.IsNullOrWhiteSpace(sig.OwnerType))
            {
                continue;
            }

            string ownerType = sig.OwnerType;
            string besName = sig.MethodName;
            int paramCount = sig.Parameters.Count;

            // 优先使用 MapAlias（防混淆别名），否则使用压缩名或原名
            string tsName;
            string? generic;
            if (!string.IsNullOrWhiteSpace(sig.MapAlias))
            {
                // 使用防混淆别名
                tsName = TsViewIdentifierMap.ToTsIdentifier(sig.MapAlias);
                generic = InferGenericFromSignature(sig);
            }
            else if (BescriptDtsGenerator.TryGetDeviceActionCompressedName(ownerType, besName) is { Success: true } deviceResult)
            {
                tsName = TsViewIdentifierMap.ToTsIdentifier(deviceResult.CompressedName);
                generic = deviceResult.GenericBeTypeName;
            }
            else
            {
                tsName = TsViewIdentifierMap.ToTsIdentifier(besName);
                generic = InferGenericFromSignature(sig);
            }

            // 正向映射：besName → (tsName, generic)
            if (!deviceActionForward.TryGetValue(ownerType, out Dictionary<string, ForwardEntry>? forwardByName))
            {
                forwardByName = new Dictionary<string, ForwardEntry>(StringComparer.Ordinal);
                deviceActionForward[ownerType] = forwardByName;
            }
            forwardByName[besName] = new ForwardEntry(tsName, generic);

            // 反向映射 + 双射校验（同 GlobalFunc）
            if (!deviceActionReverse.TryGetValue(ownerType, out Dictionary<ReverseKey, string>? reverseByKey))
            {
                reverseByKey = [];
                deviceActionReverse[ownerType] = reverseByKey;
            }
            string? convertedGeneric = null;
            if (generic is not null)
            {
                convertedGeneric = TsViewTypeUtils.ConvertToTsTypeName(generic);
            }
            ReverseKey reverseKey = new(tsName, convertedGeneric, paramCount);
            if (reverseByKey.TryGetValue(reverseKey, out string? existingBes))
            {
                throw new InvalidOperationException(
                    $"双射冲突 [DeviceAction.{ownerType}]: ({tsName}, {convertedGeneric}, {paramCount}) 已映射到 '{existingBes}'，无法再映射 '{besName}'");
            }
            reverseByKey[reverseKey] = besName;
        }

        // 转换为只读字典
        return new BuiltinMethodMap(
            globalForward,
            globalReverse,
            varFuncForward.ToDictionary(
                static kv => kv.Key,
                static kv => (IReadOnlyDictionary<string, ForwardEntry>)kv.Value,
                StringComparer.Ordinal),
            varFuncReverse.ToDictionary(
                static kv => kv.Key,
                static kv => (IReadOnlyDictionary<ReverseKey, string>)kv.Value,
                StringComparer.Ordinal),
            deviceActionForward.ToDictionary(
                static kv => kv.Key,
                static kv => (IReadOnlyDictionary<string, ForwardEntry>)kv.Value,
                StringComparer.Ordinal),
            deviceActionReverse.ToDictionary(
                static kv => kv.Key,
                static kv => (IReadOnlyDictionary<ReverseKey, string>)kv.Value,
                StringComparer.Ordinal));
    }

    private static string? InferGenericFromSignature(MethodSignature sig)
    {
        if (sig.CompressionHint is null)
        {
            return null;
        }

        CompressionHint hint = sig.CompressionHint;
        switch (hint.Kind)
        {
            case CompressionKind.NumericSuffix:
                return null;
            case CompressionKind.TypeSuffix:
            case CompressionKind.TypeSuffixWithNumeric:
                return TsViewTypeUtils.ConvertToTsTypeName(
                    sig.ReturnType ?? throw new InvalidOperationException($"CompressionHint={hint.Kind} 需要 ReturnType"));
            case CompressionKind.TypeSuffixParam:
                if (sig.Parameters.Count > 0)
                {
                    return TsViewTypeUtils.ConvertToTsTypeName(
                        sig.Parameters[0].TypeName);
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// 分页查询指定类别的所有方法。
    /// </summary>
    private static IEnumerable<MethodSignature> QueryAllByCategory(BuiltinMethodRegistry registry, MethodCategory category)
    {
        const int pageSize = 1000;
        int skip = 0;
        while (true)
        {
            MethodQueryResult result = registry.Query(category: category, skip: skip, take: pageSize);
            foreach (MethodSignature sig in result.Items)
            {
                yield return sig;
            }

            if (result.Items.Count < pageSize || skip + result.Items.Count >= result.Total)
            {
                break;
            }

            skip += pageSize;
        }
    }

    #endregion
}
