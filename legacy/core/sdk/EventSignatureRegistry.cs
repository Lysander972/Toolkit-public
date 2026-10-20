namespace Toolkit.Core.Sdk;

/// <summary>
/// 事件签名注册表实现
/// </summary>
public sealed class EventSignatureRegistry
{
    private readonly Dictionary<string, List<EventSignature>> _byName;
    private readonly Dictionary<string, List<EventSignature>> _byDeviceType;
    private readonly object _lock = new();

    /// <summary>单例实例</summary>
    public static EventSignatureRegistry Instance { get; } = new();

    // ────────────────────────────────────────────────────────────────────────────
    // 【原版 SDK 注册入口】
    // 下方方法从 EventDefinitions 文件加载原版 SDK 的精确映射，
    // 这些定义文件中的内容不可修改，必须与原版保持完全一致。
    // ────────────────────────────────────────────────────────────────────────────
    private EventSignatureRegistry()
    {
        _byName = new(StringComparer.Ordinal);
        _byDeviceType = new(StringComparer.Ordinal);

        RegisterAll(EventDefinitions.GetAll());
    }

    /// <summary>
    /// 创建一个新的空注册表实例（用于测试）
    /// </summary>
    internal static EventSignatureRegistry CreateEmpty()
    {
        return new EventSignatureRegistry(skipInit: true);
    }

    private EventSignatureRegistry(bool skipInit)
    {
        _byName = new(StringComparer.Ordinal);
        _byDeviceType = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// 注册一个事件签名（内部使用）
    /// </summary>
    internal bool Register(EventSignature signature)
    {
        signature.Validate();

        lock (_lock)
        {
            // 检查是否已存在相同 DeviceType + EventName 的事件
            EventSignature? existing = null;
            if (_byDeviceType.ContainsKey(signature.DeviceType))
                existing = _byDeviceType[signature.DeviceType].FirstOrDefault(e => e.EventName.Equals(signature.EventName, StringComparison.Ordinal));
            if (existing is not null)
            {
                // 覆盖旧定义
                RemoveInternal(existing);
            }

            // 按事件名索引
            if (!_byName.ContainsKey(signature.EventName))
            {
                _byName[signature.EventName] = [];
            }
            _byName[signature.EventName].Add(signature);

            // 按零件类型索引
            if (!_byDeviceType.ContainsKey(signature.DeviceType))
            {
                _byDeviceType[signature.DeviceType] = [];
            }
            _byDeviceType[signature.DeviceType].Add(signature);
        }

        return true;
    }

    /// <summary>
    /// 批量注册事件签名
    /// </summary>
    internal void RegisterAll(IEnumerable<EventSignature> signatures)
    {
        foreach (EventSignature sig in signatures)
        {
            Register(sig);
        }
    }

    private void RemoveInternal(EventSignature signature)
    {
        if (_byName.ContainsKey(signature.EventName))
        {
            List<EventSignature> nameList = _byName[signature.EventName];
            nameList.Remove(signature);
            if (nameList.Count == 0)
                _byName.Remove(signature.EventName);
        }

        if (_byDeviceType.ContainsKey(signature.DeviceType))
        {
            List<EventSignature> deviceList = _byDeviceType[signature.DeviceType];
            deviceList.Remove(signature);
            if (deviceList.Count == 0)
                _byDeviceType.Remove(signature.DeviceType);
        }
    }

    public IReadOnlyList<EventSignature> GetByName(string eventName)
    {
        lock (_lock)
        {
            return _byName.ContainsKey(eventName)
                ? _byName[eventName].ToArray()
                : Array.Empty<EventSignature>();
        }
    }

    public IReadOnlyList<EventSignature> GetByDeviceType(string deviceType)
    {
        lock (_lock)
        {
            return _byDeviceType.ContainsKey(deviceType)
                ? _byDeviceType[deviceType].ToArray()
                : Array.Empty<EventSignature>();
        }
    }

    public EventSignature? Get(string deviceType, string eventName)
    {
        lock (_lock)
        {
            if (!_byDeviceType.ContainsKey(deviceType))
                return null;

            return _byDeviceType[deviceType].FirstOrDefault(e => e.EventName.Equals(eventName, StringComparison.Ordinal));
        }
    }

    public IReadOnlyList<EventSignature> GetAll()
    {
        lock (_lock)
        {
            return _byDeviceType.Values
                .SelectMany(static list => list)
                .ToArray();
        }
    }

    public IReadOnlyList<string> GetDeviceTypes()
    {
        lock (_lock)
        {
            return _byDeviceType.Keys.ToArray();
        }
    }

    /// <summary>
    /// 获取注册表中的事件总数
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _byDeviceType.Values.Sum(static list => list.Count);
            }
        }
    }

    public bool IsEventAllowedForDeviceType(string eventName, int deviceTypeId)
    {
        IReadOnlyList<EventSignature> signatures = GetByName(eventName);
        if (signatures.Count == 0)
            return false;  // 未知事件，拒绝

        return signatures.Any(sig => sig.IsAllowedForDeviceType(deviceTypeId));
    }
}
