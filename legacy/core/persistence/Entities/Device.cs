using Hjson;
using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Persistence;

// ============================================================================
// Device 实体定义
// ============================================================================
// 本文件包含 Device 相关的实体类型：
// - DeviceEntityBase        : 零件实体抽象基类（公共逻辑）
// - DeviceEntity            : 只读零件实体
// - WritableDeviceEntity    : 可写零件实体
// ============================================================================

// ============================================================================
// DeviceEntityBase: 零件实体抽象基类
// ============================================================================
// 包含 DeviceEntity 和 WritableDeviceEntity 的所有公共逻辑：
// - 属性定义（DeviceId、DeviceType、LogicName、MechFileGuid、CarrierGuid、CarrierKind、Vars、ScriptGroups、Links）
// - 标识符实现
// - Scripts/EventScripts 计算属性
// - Validate 公共方法
// ============================================================================
//
// 文件处理规则：
// - device.json: 只解析 ext.logicName/ext.mechFileGUID，整体原样保持（DeviceJsonRecord）
// - vars.json  : 整体重建（VarsConfig）
// ============================================================================

public abstract record DeviceEntityBase : ChildProjectEntity, IHasChildren<ScriptGroupEntityBase>
{
    // ============================================================================
    // I 数据存储
    // ============================================================================
    // _deviceIdStr: 零件 ID 字符串形式（目录名，如 "123"，唯一数据源）
    // ============================================================================

    private string _deviceIdStr = string.Empty;
    protected int _deviceId;

    // ============================================================================
    // II 路径核心属性
    // ============================================================================

    /// <summary>标识符（零件 ID，目录名）</summary>
    public override string 实体_无相对路径标识符 => _deviceIdStr;

    // ============================================================================
    // III 路径计算属性
    // ============================================================================
    // 格式：实体组件_XXX_完整相对路径
    //
    // 【路径示例】Device 在 "folder/sub.mech_/255"
    //   device目录_完整相对路径 = "folder/sub.mech_/255"
    //   device配置_完整相对路径 = "folder/sub.mech_/255/device.json"
    //   vars配置_完整相对路径 = "folder/sub.mech_/255/vars.json"
    // ============================================================================

    /// <summary>零件目录路径</summary>
    public string device目录_完整相对路径 => PathCombiner.CombineRelativePath(内部_禁止滥用_本级计算起点_完整相对路径_并非文件路径, 实体_无相对路径标识符);

    /// <summary>device配置文件路径</summary>
    public string device配置_完整相对路径 => PathCombiner.CombineRelativePath(device目录_完整相对路径, ProjectEntityLayout.DeviceJsonName);

    /// <summary>vars配置文件路径</summary>
    public string vars配置_完整相对路径 => PathCombiner.CombineRelativePath(device目录_完整相对路径, ProjectEntityLayout.VarsJsonName);

    // ============================================================================
    // III-B 实体覆盖范围
    // ============================================================================

    public override EntityCoverage GetSelfCoverage()
    {
        EntityCoverage result = new()
        {
            Directories = { [device目录_完整相对路径] = this },
            Files = { [device配置_完整相对路径] = this }
        };
        if (Vars.ShouldWrite)
            result.Files[vars配置_完整相对路径] = this;
        return result;
    }

    // ============================================================================
    // IV 非持久化内容（protected 字段 + 只读属性）
    // ============================================================================

    protected List<ScriptGroupEntityBase> _scriptGroups = [];
    protected List<DeviceLinkInfo> _links = [];

    /// <summary>零件 ID（目录名的整数值）</summary>
    public int DeviceId => _deviceId;

    /// <summary>所属载体 GUID（机械或地图）</summary>
    public required string CarrierGuid { get; init; }

    /// <summary>所属载体类型："Map" 或 "Mech"</summary>
    public required string CarrierKind { get; init; }

    /// <summary>脚本组实体列表（从 Load 填充）</summary>
    public IReadOnlyList<ScriptGroupEntityBase> ScriptGroups => _scriptGroups;

    /// <summary>代码仓库链接列表</summary>
    public IReadOnlyList<DeviceLinkInfo> Links => _links;

    // ============================================================================
    // V 持久化内容（复用 HjsonRecord）
    // ============================================================================

    protected DeviceJsonRecord? _deviceJson;
    protected bool _hasVarsJson;
    protected VarsConfig _vars = new();

    /// <summary>零件类型ID（从 device.json 的 type）</summary>
    public int DeviceType => _deviceJson is DeviceJsonRecord deviceJson
        ? deviceJson.DeviceType
        : throw new InvalidOperationException("零件缺少 device.json，无法读取 type");

    /// <summary>
    /// 逻辑名称（从 device.json 的 ext.logicName）。
    /// 原版游戏行为：允许为空，为空时 device.json 不存储该字段，此时返回 null。
    /// 存储了但值为空串时同样归一化为 null（模型统一契约，消费方无需判空串）。
    /// </summary>
    public string? LogicName
    {
        get
        {
            string? value = _deviceJson?.LogicNameValue?.Qstr();
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }

    /// <summary>关联的机械文件 GUID（仅机械产生器 1110 使用）</summary>
    public string? MechFileGuid => _deviceJson?.MechFileGuid;

    /// <summary>
    /// 骨骼绑定字段（device.json 顶层 stickToBone）。
    /// 非空时该零件跟随骨骼节点，相关能力由 DeviceCapabilityResolver 判定。
    /// </summary>
    public string? StickToBone => _deviceJson?.StickToBone;

    /// <summary>变量配置（从 vars.json 解析）。无变量时为空列表，绝不为 null。</summary>
    public VarsConfig Vars => _vars;

    /// <summary>
    /// vars.json 是否在加载时存在（用于避免把"缺失"误写成"空文件"）。
    /// </summary>
    public bool HasVarsJson => _hasVarsJson;

    // ============================================================================
    // 内部初始化方法（仅加载时使用）
    // ============================================================================

    internal void SetDeviceIdStrInternal(string value) => _deviceIdStr = value;
    internal void SetDeviceIdInternal(int value) => _deviceId = value;
    internal void SetDeviceJsonInternal(DeviceJsonRecord? value) => _deviceJson = value;
    internal void SetScriptGroupsInternal(List<ScriptGroupEntityBase> value) => _scriptGroups = value;
    internal void SetLinksInternal(List<DeviceLinkInfo> value) => _links = value;
    internal void SetHasVarsJsonInternal(bool value) => _hasVarsJson = value;
    internal void SetVarsInternal(VarsConfig value) => _vars = value;

    // ============================================================================
    // VI 接口实现
    // ============================================================================

    // IHasChildren<ScriptGroupEntityBase> 实现（协变）
    IReadOnlyList<ScriptGroupEntityBase> IHasChildren<ScriptGroupEntityBase>.Children => _scriptGroups;

    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    public abstract override ProjectEntityKind Kind { get; }

    // ============================================================================
    // VIII 持久化声明
    // ============================================================================

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        buffer.DeclareDirectory(device目录_完整相对路径);
        // device.json: 整体原样保持
        if (_deviceJson != null)
            buffer.DeclareFile(device配置_完整相对路径, _deviceJson.ToText());
        if (Vars.ShouldWrite)
            buffer.DeclareFile(vars配置_完整相对路径, Vars.ToText());
    }

    // ============================================================================
    // VIII 计算属性
    // ============================================================================

    /// <summary>
    /// 非事件脚本组的内联脚本（自定义方法脚本）。
    ///
    /// 语义：自定义方法脚本可被 act 调用（act 设备.脚本名()），事件脚本不能被这样调用。
    /// 因此 act 调用解析（LSP goto-definition / hover / WorkerCommandHandler 链接扫描）
    /// 必须基于此属性，禁止改用 AllScripts。
    ///
    /// 如需扫描脚本内容（字符校验、签名校验、符号引用查找、脚本文档收集等），改用
    /// <see cref="AllScripts"/>，否则事件脚本中的非法调用会逃过 Lint 报告。
    /// </summary>
    public IEnumerable<InlineScriptEntityBase> Scripts =>
        ScriptGroups.Where(static g => string.IsNullOrEmpty(g.EventName)).SelectMany(static g => g.Scripts);

    /// <summary>
    /// 所有用户内联脚本（事件脚本组与非事件脚本组），用于校验/扫描类逻辑。
    ///
    /// 命名检查、签名校验、字符校验、Canvas 路径校验、符号引用查找、脚本文档收集
    /// 等必须覆盖事件脚本，否则事件脚本里的非法调用会逃过 Lint 报告。
    ///
    /// 注意：act 调用 → 脚本定义解析（自定义方法查找）禁止使用此属性，应使用
    /// <see cref="Scripts"/>，因为事件脚本不能被作为自定义方法调用。
    /// </summary>
    public IEnumerable<InlineScriptEntityBase> AllScripts =>
        ScriptGroups.SelectMany(static g => g.Scripts);

    /// <summary>事件脚本字典（从 ScriptGroups 聚合）</summary>
    public IReadOnlyDictionary<string, IEnumerable<InlineScriptEntityBase>> EventScripts
    {
        get
        {
            Dictionary<string, List<InlineScriptEntityBase>> dict = new(StringComparer.Ordinal);
            foreach (ScriptGroupEntityBase g in ScriptGroups)
            {
                if (g.EventName is not { Length: > 0 }) continue;
                if (!dict.TryGetValue(g.EventName, out List<InlineScriptEntityBase>? list))
                {
                    list = [];
                    dict[g.EventName] = list;
                }
                list.AddRange(g.Scripts);
            }
            return dict.ToDictionary(
                static kv => kv.Key,
                static kv => (IEnumerable<InlineScriptEntityBase>)kv.Value,
                StringComparer.Ordinal);
        }
    }

    // ============================================================================
    // IX 公共方法
    // ============================================================================

    public override void Validate()
    {
    }

    /// <summary>
    /// 创建可写副本（深克隆）。由只读实体实现。
    /// </summary>
    public abstract DeviceEntityBase CreateWritableClone();

}

/// <summary>
/// 零件实体（只读版本）。
/// </summary>
public sealed record DeviceEntity : DeviceEntityBase, IReadOnlyEntity<WritableDeviceEntity>
{
    public override ProjectEntityKind Kind => ProjectEntityKind.Device;

    /// <summary>
    /// 转换为可写副本（深克隆）。
    /// 递归创建所有子实体的可写副本，并设置 Parent 指向新的可写父级。
    /// </summary>
    public WritableDeviceEntity ToWritable()
    {
        WritableDeviceEntity writable = new()
        {
            Parent = Parent,  // 保持指向只读父级
            Version = Version,
            DeviceId = DeviceId,
            CarrierGuid = CarrierGuid,
            CarrierKind = CarrierKind,
            DeviceJson = (DeviceJsonRecord?)_deviceJson?.DeepCopy(),
            Vars = (VarsConfig)Vars.DeepCopy(),
            HasVarsJson = HasVarsJson,
            ScriptGroups = [],  // 稍后填充
            Links = _links.ToList()
        };
        writable.SetDeviceIdStrInternal(实体_无相对路径标识符);

        // 深克隆：递归创建子实体的可写副本
        foreach (ScriptGroupEntityBase scriptGroup in _scriptGroups)
        {
            ScriptGroupEntityBase writableScriptGroup = scriptGroup.CreateWritableClone();
            writableScriptGroup.Parent = writable;  // 指向新的可写父级
            writable.ScriptGroups.Add(writableScriptGroup);
        }

        return writable;
    }

    /// <summary>
    /// 创建可写副本（深克隆）。实现基类抽象方法。
    /// </summary>
    public override DeviceEntityBase CreateWritableClone() => ToWritable();

    // ============================================================================
    // IReadOnlyEntity 实现
    // ============================================================================

    /// <summary>
    /// 卸载实体：递归卸载子实体 → 解绑文件树 → 清空子实体列表。
    /// </summary>
    public void Unload()
    {
        // 递归卸载子实体
        foreach (ScriptGroupEntityBase scriptGroup in _scriptGroups.ToList())
        {
            if (scriptGroup is IReadOnlyEntity readOnlyScriptGroup)
                readOnlyScriptGroup.Unload();
        }
        // 清空子实体列表
        _scriptGroups.Clear();
    }

    /// <summary>
    /// 加载自身：递增版本号 + 加载零件自身内容。
    /// </summary>
    public void LoadSelf(EntityLoadErrorCollector errors)
    {
        // 递增版本号
        IncrementVersion();

        // 查找零件目录节点
        FileTreeNode? node = FileTreeBinding.Instance.FindNode(实体_完整相对路径标识);
        if (node == null)
        {
            errors.AddFileNotFound(实体_完整相对路径标识, "零件目录");
            return;
        }

        // 1. 读取 device.json
        string? deviceJsonFile = node.Files.FirstOrDefault(static f => f.Equals(ProjectEntityLayout.DeviceJsonName, StringComparison.Ordinal));
        if (deviceJsonFile == null)
        {
            // Offensive: 零件目录必须包含 device.json
            errors.AddError(实体_完整相对路径标识, $"零件目录缺少 {ProjectEntityLayout.DeviceJsonName}", ToolDiagnosticSeverity.Error);
            return;
        }

        // 原版行为：DeviceId 唯一权威来源是路径最后一级（目录名），device.json 不包含 saveIndex 字段！
        string dirName = 实体_无相对路径标识符;
        if (!int.TryParse(dirName, out int parsedId))
        {
            throw new InvalidOperationException(
                $"零件目录名 '{dirName}' 不是有效的整数 ID。路径: {实体_完整相对路径标识}");
        }
        SetDeviceIdInternal(parsedId);

        try
        {
            string text = FileIO.ReadAllText(device配置_完整相对路径);
            HjsonText.Parse(text); // 仅验证格式
            DeviceJsonRecord deviceJson = new(text);
            SetDeviceJsonInternal(deviceJson);
        }
        catch (Exception ex)
        {
            errors.AddParseError(device配置_完整相对路径, "device.json", ex);
            throw; // Offensive: 解析失败必须抛错误，禁止静默吞咽
        }

        // 1110 类型必须配置 mechFileGUID，否则无法解析作用域
        if (DeviceType == DeviceTypeIds.BirthBuilding && !GuidText.Validate(MechFileGuid))
        {
            errors.AddError(
                实体_完整相对路径标识,
                "机械产生器(1110)缺少 mechFileGUID 配置，将使用虚拟机械作用域。建议在 device.json 的 ext 对象中添加 mechFileGUID 字段指向目标机械。",
                ToolDiagnosticSeverity.Warning);
        }

        // 2. 读取 vars.json
        // 原版游戏中 vars.json 是可选文件，缺失时视为空变量配置
        string? varsJsonFile = node.Files.FirstOrDefault(static f => f.Equals(ProjectEntityLayout.VarsJsonName, StringComparison.Ordinal));
        if (varsJsonFile != null)
        {
            SetHasVarsJsonInternal(true);
            try
            {
                string text = FileIO.ReadAllText(vars配置_完整相对路径);
                SetVarsInternal(new VarsConfig(text));
            }
            catch (Exception ex)
            {
                errors.AddParseError(vars配置_完整相对路径, "vars.json", ex);
            }
        }
        else
        {
            SetHasVarsJsonInternal(false);
            // 原版允许 vars.json 缺失，明确设为空配置
            SetVarsInternal(new VarsConfig());
        }
    }

    /// <summary>
    /// 加载子实体：加载脚本组（scripts/ 和 event_*/）。
    /// </summary>
    public void LoadChildren(EntityLoadErrorCollector errors)
    {
        List<ScriptGroupEntityBase> scriptGroups = [];

        FileTreeNode? node = FileTreeBinding.Instance.FindNode(实体_完整相对路径标识);
        if (node == null) return;

        foreach (FileTreeNode childNode in node.Children)
        {
            if (childNode.Name.Equals(ProjectEntityLayout.ScriptsDirName, StringComparison.Ordinal))
            {
                // 用户脚本组（自定义方法）
                ScriptGroupEntity scriptGroup = new()
                {
                    Parent = this,
                    Version = GenerateChildVersion()
                };
                scriptGroup.SetScriptGroupNameInternal(ProjectEntityLayout.ScriptsDirName);
                scriptGroup.SetEventNameInternal(null);

                // Rebuild 内部处理 list.json 缺失情况
                if (scriptGroup.Rebuild(errors))
                    scriptGroups.Add(scriptGroup);
            }
            else if (childNode.Name.StartsWith(ProjectEntityLayout.EventDirPrefix, StringComparison.Ordinal))
            {
                // 事件脚本组
                string eventName = childNode.Name[ProjectEntityLayout.EventDirPrefix.Length..];
                ScriptGroupEntity scriptGroup = new()
                {
                    Parent = this,
                    Version = GenerateChildVersion()
                };
                scriptGroup.SetScriptGroupNameInternal(childNode.Name);
                scriptGroup.SetEventNameInternal(eventName);

                // Rebuild 内部处理 list.json 缺失情况
                if (scriptGroup.Rebuild(errors))
                    scriptGroups.Add(scriptGroup);
            }
        }

        SetScriptGroupsInternal(scriptGroups);
    }

    /// <summary>
    /// 完整重建：LoadSelf + LoadChildren。
    /// </summary>
    public bool Rebuild(EntityLoadErrorCollector errors)
    {
        LoadSelf(errors);
        LoadChildren(errors);
        return true;
    }
}

/// <summary>
/// 可写零件实体。
/// 通过 DeviceEntity.ToWritable() 创建。用完即丢弃。
/// </summary>
public sealed record WritableDeviceEntity : DeviceEntityBase, IWritableEntity
{
    public override ProjectEntityKind Kind => ProjectEntityKind.Device;

    // ============================================================================
    // 可写属性（用 new 重新声明，提供 setter）
    // ============================================================================

    public new int DeviceId
    {
        get => _deviceId;
        set => _deviceId = value;
    }

    public new List<ScriptGroupEntityBase> ScriptGroups
    {
        get => _scriptGroups;
        set => _scriptGroups = value;
    }

    public new List<DeviceLinkInfo> Links
    {
        get => _links;
        set => _links = value;
    }

    public DeviceJsonRecord? DeviceJson
    {
        get => _deviceJson;
        set => _deviceJson = value;
    }

    public new bool HasVarsJson
    {
        get => _hasVarsJson;
        set => _hasVarsJson = value;
    }

    public new VarsConfig Vars
    {
        get => _vars;
        set => _vars = value;
    }

    /// <summary>
    /// 可写实体不能再创建可写副本。抛出异常。
    /// </summary>
    public override DeviceEntityBase CreateWritableClone() =>
        throw new InvalidOperationException("可写实体不能再创建可写副本");

    public void Rename(string newName) => throw new NotSupportedException("零件不支持重命名");
}
