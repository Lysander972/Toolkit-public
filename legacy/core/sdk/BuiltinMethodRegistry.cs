using Toolkit.Core.Infra;

namespace Toolkit.Core.Sdk;

/// <summary>
/// 内置方法注册表实现
/// </summary>
public sealed class BuiltinMethodRegistry
{
    private readonly Dictionary<MethodKey, MethodSignature> _byKey;
    private readonly Dictionary<string, List<MethodSignature>> _byPath;
    private readonly Dictionary<MethodCategory, List<MethodSignature>> _byCategory;
    private readonly Dictionary<string, List<MethodSignature>> _byOwnerType;
    private readonly object _lock = new();

    private static readonly MethodCategory[] DefaultPathResolveOrder =
    {
        MethodCategory.EditorFunc,
        MethodCategory.DeviceAction,
        MethodCategory.VarFunc,
        MethodCategory.GlobalFunc
    };

    private readonly record struct MethodKey(MethodCategory Category, string Path);

    private sealed class MethodKeyComparer : IEqualityComparer<MethodKey>
    {
        public bool Equals(MethodKey x, MethodKey y)
        {
            return x.Category == y.Category &&
                   StringComparer.Ordinal.Equals(x.Path, y.Path);
        }

        public int GetHashCode(MethodKey obj)
        {
            return HashCode.Combine(
                obj.Category,
                StringComparer.Ordinal.GetHashCode(obj.Path));
        }
    }

    /// <summary>单例实例</summary>
    public static BuiltinMethodRegistry Instance { get; } = new();

    private BuiltinMethodRegistry()
    {
        _byKey = new(new MethodKeyComparer());
        _byPath = new(StringComparer.Ordinal);
        _byCategory = [];
        _byOwnerType = new(StringComparer.Ordinal);

        // 初始化类别字典
        foreach (MethodCategory cat in Enum.GetValues<MethodCategory>())
        {
            _byCategory[cat] = [];
        }

        IEnumerable<MethodSignature> baseDefs = GlobalFuncDefinitions.GetAll()
            .Concat(VarFuncDefinitions.GetAll())
            .Concat(VarFuncDefinitions_Entity.GetAll())
            .Concat(DeviceActionDefinitions.GetAll())
            .Concat(EditorFuncDefinitions.GetAll());

        RegisterAll(baseDefs);
    }

    /// <summary>
    /// 创建一个新的空注册表实例（用于测试）
    /// </summary>
    internal static BuiltinMethodRegistry CreateEmpty()
    {
        return new BuiltinMethodRegistry(skipInit: true);
    }

    private BuiltinMethodRegistry(bool skipInit)
    {
        _byKey = new(new MethodKeyComparer());
        _byPath = new(StringComparer.Ordinal);
        _byCategory = [];
        _byOwnerType = new(StringComparer.Ordinal);

        foreach (MethodCategory cat in Enum.GetValues<MethodCategory>())
        {
            _byCategory[cat] = [];
        }
    }

    /// <summary>
    /// 注册一个方法（内部使用）
    /// </summary>
    internal bool Register(MethodSignature signature)
    {
        signature.Validate();

        string path = signature.GetFullPath();
        MethodKey key = new(signature.Category, path);

        lock (_lock)
        {
            // 检查重复注册（同类别+同路径）
            if (_byKey.ContainsKey(key))
            {
                RemoveSignature(_byKey[key], path);
            }

            _byKey[key] = signature;
            _byCategory[signature.Category].Add(signature);

            if (signature.OwnerType is not null)
            {
                if (!_byOwnerType.ContainsKey(signature.OwnerType))
                {
                    _byOwnerType[signature.OwnerType] = [];
                }
                _byOwnerType[signature.OwnerType].Add(signature);
            }

            if (!_byPath.ContainsKey(path))
            {
                _byPath[path] = [];
            }
            _byPath[path].Add(signature);
        }

        return true;
    }

    /// <summary>
    /// 批量注册方法
    /// </summary>
    internal void RegisterAll(IEnumerable<MethodSignature> signatures)
    {
        foreach (MethodSignature sig in signatures)
        {
            Register(sig);
        }
    }

    public MethodQueryResult Query(
        string? namePattern = null,
        MethodCategory? category = null,
        string? ownerType = null,
        int skip = 0,
        int take = 200)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 1000);

        return QueryInternal(namePattern, category, ownerType, skip, take);
    }

    /// <summary>
    /// 不分页全量查询（用于 SDK 文档查询等需要遍历全部结果的场景）
    /// </summary>
    public IReadOnlyList<MethodSignature> QueryAll(MethodCategory? category = null)
    {
        lock (_lock)
        {
            IEnumerable<MethodSignature> query = _byKey.Values;

            if (category.HasValue)
            {
                query = query.Where(s => s.Category == category.Value);
            }

            return query
                .OrderBy(s => s.Category)
                .ThenBy(s => s.OwnerType, StringComparer.Ordinal)
                .ThenBy(s => s.MethodName, StringComparer.Ordinal)
                .ToArray();
        }
    }

    private MethodQueryResult QueryInternal(
        string? namePattern,
        MethodCategory? category,
        string? ownerType,
        int skip,
        int take)
    {
        IEnumerable<MethodSignature> query;

        lock (_lock)
        {
            query = _byKey.Values;

            // 按类别过滤
            if (category.HasValue)
            {
                query = query.Where(s => s.Category == category.Value);
            }

            // 按 OwnerType 过滤
            if (ownerType is not null)
            {
                query = query.Where(s =>
                    s.OwnerType is not null &&
                    s.OwnerType.Equals(ownerType, StringComparison.Ordinal));
            }

            // 按名称模式过滤
            if (namePattern is not null)
            {
                string pattern = namePattern;
                query = query.Where(s =>
                    s.MethodName.Contains(pattern, StringComparison.Ordinal) ||
                    s.GetFullPath().Contains(pattern, StringComparison.Ordinal));
            }

            List<MethodSignature> all = query.ToList();
            int total = all.Count;

            // 排序: Category -> OwnerType -> MethodName
            MethodSignature[] sorted = all
                .OrderBy(s => s.Category)
                .ThenBy(s => s.OwnerType, StringComparer.Ordinal)
                .ThenBy(s => s.MethodName, StringComparer.Ordinal)
                .Skip(skip)
                .Take(take)
                .ToArray();

            return new MethodQueryResult(sorted, total);
        }
    }

    public MethodSignature? GetByPath(string path, MethodCategory? category = null)
    {
        lock (_lock)
        {
            if (category.HasValue)
            {
                MethodKey key = new(category.Value, path);
                return _byKey.ContainsKey(key) ? _byKey[key] : null;
            }

            if (!_byPath.ContainsKey(path))
            {
                return null;
            }

            List<MethodSignature> list = _byPath[path];
            if (list.Count == 0)
            {
                return null;
            }

            if (list.Count == 1)
            {
                return list[0];
            }

            foreach (MethodCategory cat in DefaultPathResolveOrder)
            {
                MethodSignature? match = list.FirstOrDefault(sig => sig.Category == cat);
                if (match is not null)
                {
                    return match;
                }
            }

            return list[0];
        }
    }

    public MethodSignature? GetByPathWithInheritance(string path, MethodCategory category)
    {
        if (category is not (MethodCategory.VarFunc or MethodCategory.DeviceAction))
        {
            return GetByPath(path, category);
        }

        MethodSignature? direct = GetByPath(path, category);
        if (direct is not null)
        {
            return direct;
        }

        if (OwnerPath.SplitOwnerPath(path) is not { } split)
        {
            return null;
        }

        if (category == MethodCategory.VarFunc)
        {
            return VarFuncInheritance.FindInChain(split.ownerType, type =>
            {
                MethodSignature? sig = GetByPath(type + "." + split.methodName, MethodCategory.VarFunc);
                return (sig is not null, sig);
            }, startFromParent: true);
        }

        return DeviceActionInheritance.FindInChain(split.ownerType, type =>
        {
            MethodSignature? sig = GetByPath(type + "." + split.methodName, MethodCategory.DeviceAction);
            return (sig is not null, sig);
        }, startFromParent: true);
    }

    public MethodValidationResult ValidateCall(string path, IReadOnlyList<string> argumentTypes)
    {
        MethodSignature? sig = GetByPath(path);
        if (sig is null)
        {
            return MethodValidationResult.Error($"未知方法: {path}");
        }

        int totalCount = sig.Parameters.Count;
        if (argumentTypes.Count != totalCount)
        {
            return MethodValidationResult.Error(
                $"参数数量不匹配: 期望 {totalCount} 个, 实际 {argumentTypes.Count} 个");
        }

        // 检查类型匹配
        List<string> warnings = [];
        for (int i = 0; i < argumentTypes.Count; i++)
        {
            string expected = sig.Parameters[i].TypeName;
            string actual = argumentTypes[i];

            // Any 类型接受任何参数
            if (expected.Equals("Any", StringComparison.Ordinal))
                continue;

            // 类型不匹配时添加警告
            if (!expected.Equals(actual, StringComparison.Ordinal))
            {
                warnings.Add($"参数 {i + 1} ({sig.Parameters[i].EffectiveName}): 期望 {expected}, 实际 {actual}");
            }
        }

        if (warnings.Count > 0)
            return MethodValidationResult.Success(warnings);

        return MethodValidationResult.Success();
    }

    public IReadOnlyDictionary<MethodCategory, int> GetCategoryCounts()
    {
        lock (_lock)
        {
            return _byCategory.ToDictionary(
                static kvp => kvp.Key,
                static kvp => kvp.Value.Count);
        }
    }

    /// <summary>
    /// 获取注册表中的方法总数
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _byKey.Count;
            }
        }
    }

    private void RemoveSignature(MethodSignature signature, string path)
    {
        _byCategory[signature.Category].Remove(signature);
        if (signature.OwnerType is not null && _byOwnerType.ContainsKey(signature.OwnerType))
        {
            List<MethodSignature> oldList = _byOwnerType[signature.OwnerType];
            oldList.Remove(signature);
            if (oldList.Count == 0)
            {
                _byOwnerType.Remove(signature.OwnerType);
            }
        }

        if (_byPath.ContainsKey(path))
        {
            List<MethodSignature> pathList = _byPath[path];
            pathList.Remove(signature);
            if (pathList.Count == 0)
            {
                _byPath.Remove(path);
            }
        }
    }
}
