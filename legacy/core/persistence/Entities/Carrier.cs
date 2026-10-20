using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

// =============================================================================
// Carrier 实体（Map 和 Mech）
// =============================================================================
// 本文件包含地图和机械相关的实体类型定义：
// - MapEntityBase / MapEntity / WritableMapEntity : 地图实体
// - MechEntityBase / MechEntity / WritableMechEntity : 机械实体
// - MechImportInfo : 地图导入机械信息
// =============================================================================
//
// 文件处理规则：
// - map.json    : 整体原样保持（MapJsonRecord）
// - mech.json   : 解析 list 作为零件索引，整体原样保持（MechJsonRecord）
//                 原版已知行为：删零件时只删 mech.json 条目，不删零件目录。
//                 因此磁盘上可能存在 mech.json 中没有的孤儿零件目录，加载时忽略，保存时清理。
// - .guid       : 仅替换 guid 值，保持原格式
// - setting.json: 整体原样保持（SettingJsonRecord）
// =============================================================================

// ============================================================================
// MapEntityBase: 地图实体抽象基类
// ============================================================================

public abstract record MapEntityBase : ChildProjectEntity, IDeviceCarrier, IHasChildren<DeviceEntityBase>, IHasGuid
{
    private string _标识符 = string.Empty;

    // ============================================================================
    // 路径计算
    // ============================================================================

    public override string 实体_无相对路径标识符 => _标识符;

    public string 派生目录_完整相对路径 => 实体_完整相对路径标识;
    public string map文件_完整相对路径 => PathCombiner.CombineRelativePath(内部_禁止滥用_本级计算起点_完整相对路径_并非文件路径, _标识符[..^1]);
    public string GUID文件_完整相对路径 => PathCombiner.CombineRelativePath(内部_禁止滥用_本级计算起点_完整相对路径_并非文件路径, _标识符[..^1] + ProjectEntityLayout.GuidExt);
    public string map配置_完整相对路径 => PathCombiner.CombineRelativePath(派生目录_完整相对路径, ProjectEntityLayout.MapJsonName);
    public string mech配置_完整相对路径 => PathCombiner.CombineRelativePath(派生目录_完整相对路径, ProjectEntityLayout.MechJsonName);

    public override EntityCoverage GetSelfCoverage()
    {
        return new EntityCoverage
        {
            Files =
            {
                [map文件_完整相对路径] = this,
                [GUID文件_完整相对路径] = this,
                [map配置_完整相对路径] = this,
                [mech配置_完整相对路径] = this
            },
            Directories = { [派生目录_完整相对路径] = this }
        };
    }

    // ============================================================================
    // 数据存储
    // ============================================================================

    protected List<MechImportInfo> _mechImports = [];
    protected List<DeviceEntityBase> _devices = [];

    // JSON 记录（复用 HjsonRecord）
    protected MapJsonRecord? _mapJson;
    protected MechJsonRecord? _mechJson;
    protected GuidJsonRecord? _guidJson;

    // 其他持久化内容
    protected byte[] _mapFileBytes = Array.Empty<byte>();

    public IReadOnlyList<MechImportInfo> MechImports => _mechImports;
    public IReadOnlyList<DeviceEntityBase> Devices => _devices;

    // 用于 ToWritable 复制基类字段
    internal void CopyBaseFieldsTo(MapEntityBase target)
    {
        target._mechImports = _mechImports.ToList();
        target._devices = [];
        target._mapJson = (MapJsonRecord?)_mapJson?.DeepCopy();
        target._mechJson = (MechJsonRecord?)_mechJson?.DeepCopy();
        target._guidJson = (GuidJsonRecord?)_guidJson?.DeepCopy();
        target._mapFileBytes = _mapFileBytes;
    }

    // ============================================================================
    // 内部初始化方法
    // ============================================================================

    internal void SetIdentifierInternal(string value) => _标识符 = value;
    internal void SetMechImportsInternal(List<MechImportInfo> value) => _mechImports = value;
    internal void SetDevicesInternal(List<DeviceEntityBase> value) => _devices = value;
    internal void SetMapJsonInternal(MapJsonRecord? value) => _mapJson = value;
    internal void SetMechJsonInternal(MechJsonRecord? value) => _mechJson = value;
    internal void SetGuidJsonInternal(GuidJsonRecord? value) => _guidJson = value;
    internal void SetMapFileBytesInternal(byte[] value) => _mapFileBytes = value;

    // ============================================================================
    // 接口实现
    // ============================================================================

    string IDeviceCarrier.CarrierGuid => Guid;
    string IDeviceCarrier.CarrierKind => "Map";
    IReadOnlyList<DeviceEntityBase> IDeviceCarrier.Devices => Devices;
    IReadOnlyList<DeviceEntityBase> IHasChildren<DeviceEntityBase>.Children => _devices;

    // ============================================================================
    // 持久化声明
    // ============================================================================

    public string Guid { get; set; } = string.Empty;

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        buffer.DeclareDirectory(派生目录_完整相对路径);
        buffer.DeclareFile(map文件_完整相对路径, _mapFileBytes);

        // .guid: 仅替换 guid 值，保持原格式
        if (_guidJson != null)
        {
            _guidJson.SetGuid(Guid);
            buffer.DeclareFile(GUID文件_完整相对路径, _guidJson.ToText());
        }

        // map.json: 整体原样保持
        if (_mapJson != null)
            buffer.DeclareFile(map配置_完整相对路径, _mapJson.ToText());

        // mech.json: 保持原文，零件索引以 mech.json 为准
        if (_mechJson != null)
        {
            buffer.DeclareFile(mech配置_完整相对路径, _mechJson.ToText());
        }
    }

    public override void Validate()
    {
        ValidateParentLink();
        if (!GuidText.Validate(Guid)) throw new InvalidOperationException($"GUID 格式无效: {实体_完整相对路径标识}");
    }

    public abstract override ProjectEntityKind Kind { get; }
}

// ============================================================================
// MapEntity: 地图实体（只读版本）
// ============================================================================

public sealed record MapEntity : MapEntityBase, IReadOnlyEntity<WritableMapEntity>
{
    public override ProjectEntityKind Kind => ProjectEntityKind.Map;

    public WritableMapEntity ToWritable()
    {
        WritableMapEntity writable = new()
        {
            Parent = Parent,
            Guid = Guid,
            Version = Version
        };
        writable.SetIdentifierInternal(实体_无相对路径标识符);
        CopyBaseFieldsTo(writable);

        foreach (DeviceEntityBase device in _devices)
        {
            DeviceEntityBase writableDevice = device.CreateWritableClone();
            writableDevice.Parent = writable;
            writable.Devices.Add(writableDevice);
        }

        return writable;
    }

    public void Unload()
    {
        foreach (DeviceEntityBase device in _devices.ToList())
        {
            if (device is IReadOnlyEntity readOnlyDevice)
                readOnlyDevice.Unload();
        }
        _devices.Clear();
    }

    public void LoadSelf(EntityLoadErrorCollector errors)
    {
        IncrementVersion();

        // 1. 加载 map.json（原样保持）
        try
        {
            string text = FileIO.ReadAllText(map配置_完整相对路径);
            HjsonText.Parse(text); // 仅验证格式
            _mapJson = new MapJsonRecord(text);
        }
        catch (Exception ex)
        {
            errors.AddParseError(map配置_完整相对路径, "map.json", ex);
            return;
        }

        // 2. 加载 mech.json（保存原始文本，写回时重建 list）
        try
        {
            string text = FileIO.ReadAllText(mech配置_完整相对路径);
            HjsonText.Parse(text); // 仅验证格式
            _mechJson = new MechJsonRecord(text);
        }
        catch (Exception ex)
        {
            errors.AddParseError(mech配置_完整相对路径, "mech.json", ex);
            return;
        }

        // 3. 读取 GUID 文件
        try
        {
            string text = FileIO.ReadAllText(GUID文件_完整相对路径);
            _guidJson = new GuidJsonRecord(text);
            Guid = _guidJson.Guid;
        }
        catch (Exception ex)
        {
            errors.AddGuidError(实体_完整相对路径标识, ex.Message);
            return;
        }

        // 4. 读取 map 文件
        try
        {
            _mapFileBytes = FileIO.ReadAllBytes(map文件_完整相对路径);
        }
        catch (Exception ex)
        {
            errors.AddError(实体_完整相对路径标识, $"读取 map 文件失败: {ex.Message}", ToolDiagnosticSeverity.Error);
        }
    }

    public void LoadChildren(EntityLoadErrorCollector errors)
    {
        FileTreeNode? node = FileTreeBinding.Instance.FindNode(派生目录_完整相对路径);
        if (node == null)
        {
            errors.AddFileNotFound(派生目录_完整相对路径, "地图派生目录");
            return;
        }

        if (_mechJson == null)
        {
            errors.AddError(mech配置_完整相对路径, "内部状态异常：_mechJson 为 null", ToolDiagnosticSeverity.Error);
            return;
        }
        SetDevicesInternal(CarrierDeviceLoader.LoadDevices(this, _mechJson, node, mech配置_完整相对路径, errors));
    }

    public bool Rebuild(EntityLoadErrorCollector errors)
    {
        LoadSelf(errors);
        if (_mechJson != null)
            LoadChildren(errors);
        return true;
    }
}

// ============================================================================
// WritableMapEntity: 可写地图实体
// ============================================================================

public sealed record WritableMapEntity : MapEntityBase, IWritableEntity
{
    public override ProjectEntityKind Kind => ProjectEntityKind.Map;

    public new List<MechImportInfo> MechImports
    {
        get => _mechImports;
        set => _mechImports = value;
    }

    public new List<DeviceEntityBase> Devices
    {
        get => _devices;
        set => _devices = value;
    }

    public MapJsonRecord? MapJson
    {
        get => _mapJson;
        set => _mapJson = value;
    }

    public void Rename(string newName) => throw new NotSupportedException("地图不支持重命名");
}

// ============================================================================
// MechImportInfo: 地图机械导入信息
// ============================================================================

public sealed record MechImportInfo(string MechGuid);

// ============================================================================
// MechEntityBase: 机械实体抽象基类
// ============================================================================

public abstract record MechEntityBase : ChildProjectEntity, IDeviceCarrier, IHasChildren<DeviceEntityBase>, IHasGuid
{
    private string _标识符 = string.Empty;

    // ============================================================================
    // 路径计算
    // ============================================================================

    public override string 实体_无相对路径标识符 => _标识符;

    public string 派生目录_完整相对路径 => 实体_完整相对路径标识;
    public string mech文件_完整相对路径 => PathCombiner.CombineRelativePath(内部_禁止滥用_本级计算起点_完整相对路径_并非文件路径, _标识符[..^1]);
    public string GUID文件_完整相对路径 => PathCombiner.CombineRelativePath(内部_禁止滥用_本级计算起点_完整相对路径_并非文件路径, _标识符[..^1] + ProjectEntityLayout.GuidExt);
    public string mech配置_完整相对路径 => PathCombiner.CombineRelativePath(派生目录_完整相对路径, ProjectEntityLayout.MechJsonName);

    /// <summary>机械名称（标识符去掉扩展名后缀）</summary>
    public string Name => _标识符[..^ProjectEntityLayout.MechDirExt.Length];

    public override EntityCoverage GetSelfCoverage()
    {
        return new EntityCoverage
        {
            Files =
            {
                [mech文件_完整相对路径] = this,
                [GUID文件_完整相对路径] = this,
                [mech配置_完整相对路径] = this
            },
            Directories = { [派生目录_完整相对路径] = this }
        };
    }

    // ============================================================================
    // 数据存储
    // ============================================================================

    protected List<DeviceEntityBase> _devices = [];

    // JSON 记录（复用 HjsonRecord）
    protected MechJsonRecord? _mechJson;

    // JSON 记录（复用 HjsonRecord）
    protected GuidJsonRecord? _guidJson;

    // 其他持久化内容
    protected byte[] _mechFileBytes = Array.Empty<byte>();

    public IReadOnlyList<DeviceEntityBase> Devices => _devices;

    // 用于 ToWritable 复制基类字段
    internal void CopyBaseFieldsTo(MechEntityBase target)
    {
        target._devices = [];
        target._mechJson = (MechJsonRecord?)_mechJson?.DeepCopy();
        target._guidJson = (GuidJsonRecord?)_guidJson?.DeepCopy();
        target._mechFileBytes = _mechFileBytes;
    }

    // ============================================================================
    // 内部初始化方法
    // ============================================================================

    internal void SetIdentifierInternal(string value) => _标识符 = value;
    internal void SetDevicesInternal(List<DeviceEntityBase> value) => _devices = value;
    internal void SetMechJsonInternal(MechJsonRecord? value) => _mechJson = value;
    internal void SetGuidJsonInternal(GuidJsonRecord? value) => _guidJson = value;
    internal void SetMechFileBytesInternal(byte[] value) => _mechFileBytes = value;

    // ============================================================================
    // 接口实现
    // ============================================================================

    string IDeviceCarrier.CarrierGuid => Guid;
    string IDeviceCarrier.CarrierKind => "Mech";
    IReadOnlyList<DeviceEntityBase> IDeviceCarrier.Devices => Devices;
    IReadOnlyList<DeviceEntityBase> IHasChildren<DeviceEntityBase>.Children => _devices;

    // ============================================================================
    // 持久化声明
    // ============================================================================

    public string Guid { get; set; } = string.Empty;

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        buffer.DeclareDirectory(派生目录_完整相对路径);
        buffer.DeclareFile(mech文件_完整相对路径, _mechFileBytes);

        // .guid: 仅替换 guid 值，保持原格式
        if (_guidJson != null)
        {
            _guidJson.SetGuid(Guid);
            buffer.DeclareFile(GUID文件_完整相对路径, _guidJson.ToText());
        }

        // mech.json: 保持原文，零件索引以 mech.json 为准
        if (_mechJson != null)
        {
            buffer.DeclareFile(mech配置_完整相对路径, _mechJson.ToText());
        }
    }

    public override void Validate()
    {
        ValidateParentLink();
        if (!GuidText.Validate(Guid)) throw new InvalidOperationException($"GUID 格式无效: {实体_完整相对路径标识}");
    }

    public abstract override ProjectEntityKind Kind { get; }
}

// ============================================================================
// MechEntity: 只读机械实体
// ============================================================================

public sealed record MechEntity : MechEntityBase, IReadOnlyEntity<WritableMechEntity>
{
    public override ProjectEntityKind Kind => ProjectEntityKind.Mech;

    public WritableMechEntity ToWritable()
    {
        WritableMechEntity writable = new()
        {
            Parent = Parent,
            Guid = Guid,
            Version = Version
        };
        writable.SetIdentifierInternal(实体_无相对路径标识符);
        CopyBaseFieldsTo(writable);

        foreach (DeviceEntityBase device in _devices)
        {
            DeviceEntityBase writableDevice = device.CreateWritableClone();
            writableDevice.Parent = writable;
            writable.Devices.Add(writableDevice);
        }

        return writable;
    }

    public void Unload()
    {
        foreach (DeviceEntityBase device in _devices.ToList())
        {
            if (device is IReadOnlyEntity readOnlyDevice)
                readOnlyDevice.Unload();
        }
        _devices.Clear();
    }

    public void LoadSelf(EntityLoadErrorCollector errors)
    {
        IncrementVersion();

        // 1. 加载 mech.json（保存原始文本，写回时重建 list）
        try
        {
            string text = FileIO.ReadAllText(mech配置_完整相对路径);
            HjsonText.Parse(text); // 仅验证格式
            _mechJson = new MechJsonRecord(text);
        }
        catch (Exception ex)
        {
            errors.AddParseError(mech配置_完整相对路径, "mech.json", ex);
            return;
        }

        // 2. 读取 GUID 文件
        try
        {
            string text = FileIO.ReadAllText(GUID文件_完整相对路径);
            _guidJson = new GuidJsonRecord(text);
            Guid = _guidJson.Guid;
        }
        catch (Exception ex)
        {
            errors.AddGuidError(实体_完整相对路径标识, ex.Message);
            return;
        }

        // 3. 读取 mech 文件
        try
        {
            _mechFileBytes = FileIO.ReadAllBytes(mech文件_完整相对路径);
        }
        catch (Exception ex)
        {
            errors.AddError(实体_完整相对路径标识, $"读取 mech 文件失败: {ex.Message}", ToolDiagnosticSeverity.Error);
        }
    }

    public void LoadChildren(EntityLoadErrorCollector errors)
    {
        FileTreeNode? node = FileTreeBinding.Instance.FindNode(派生目录_完整相对路径);
        if (node == null)
        {
            errors.AddFileNotFound(派生目录_完整相对路径, "机械派生目录");
            return;
        }

        if (_mechJson == null)
        {
            errors.AddError(mech配置_完整相对路径, "内部状态异常：_mechJson 为 null", ToolDiagnosticSeverity.Error);
            return;
        }
        SetDevicesInternal(CarrierDeviceLoader.LoadDevices(this, _mechJson, node, mech配置_完整相对路径, errors));
    }

    public bool Rebuild(EntityLoadErrorCollector errors)
    {
        LoadSelf(errors);
        if (_mechJson != null)
            LoadChildren(errors);
        return true;
    }
}

// ============================================================================
// WritableMechEntity: 可写机械实体
// ============================================================================

public sealed record WritableMechEntity : MechEntityBase, IWritableEntity
{
    public override ProjectEntityKind Kind => ProjectEntityKind.Mech;

    public new List<DeviceEntityBase> Devices
    {
        get => _devices;
        set => _devices = value;
    }

    public MechJsonRecord? MechJson
    {
        get => _mechJson;
        set => _mechJson = value;
    }

    public void Rename(string newName) => throw new NotSupportedException("机械不支持重命名");
}

// ============================================================================
// CarrierDeviceLoader: 载体零件加载（Map/Mech 共用）
// ============================================================================

internal static class CarrierDeviceLoader
{
    /// <summary>
    /// 以 mech.json 为权威索引加载零件。
    /// 校验零件目录存在性和类型一致性。
    /// 原版已知行为：删零件时只删 mech.json 条目不删目录，
    /// 因此磁盘上可能存在孤儿零件目录，只遍历 mech.json 条目即自然忽略。
    /// </summary>
    internal static List<DeviceEntityBase> LoadDevices(
        ProjectEntity carrier,
        MechJsonRecord mechJson,
        FileTreeNode carrierNode,
        string mechConfigRelativePath,
        EntityLoadErrorCollector errors)
    {
        IDeviceCarrier dc = (IDeviceCarrier)carrier;
        List<DeviceEntityBase> devices = [];

        foreach (MechJsonDeviceEntry entry in mechJson.DeviceEntries)
        {
            string deviceDirName = entry.DeviceId.ToString();
            FileTreeNode? deviceNode = carrierNode.Children.FirstOrDefault(
                child => child.Name.Equals(deviceDirName, StringComparison.Ordinal));
            if (deviceNode == null)
            {
                errors.AddError(mechConfigRelativePath, $"mech.json 引用的零件目录不存在: {deviceDirName}", ToolDiagnosticSeverity.Error);
                continue;
            }

            DeviceEntity device = new()
            {
                Parent = carrier,
                CarrierGuid = dc.CarrierGuid,
                CarrierKind = dc.CarrierKind
            };
            device.SetDeviceIdStrInternal(deviceDirName);

            int errorCountBefore = errors.Errors.Count(e => e.Severity == ToolDiagnosticSeverity.Error);
            device.LoadSelf(errors);
            if (errors.Errors.Count(e => e.Severity == ToolDiagnosticSeverity.Error) > errorCountBefore)
                continue;

            if (device.DeviceType != entry.DeviceType)
            {
                errors.AddError(
                    device.device配置_完整相对路径,
                    $"mech.json 中零件 {deviceDirName} 的类型与 device.json 不一致: mech.json={entry.DeviceType}, device.json={device.DeviceType}",
                    ToolDiagnosticSeverity.Error);
            }

            device.Version = carrier.GenerateChildVersion();
            device.LoadChildren(errors);
            devices.Add(device);
        }

        return devices;
    }
}

// ============================================================================
// DeviceMetadata: 零件元数据
// ============================================================================

/// <summary>
/// 零件元数据。
/// </summary>
public sealed record DeviceMetadata(
    int DeviceId,
    int Type,
    string? LogicName,
    string? MechFileGuid
);
