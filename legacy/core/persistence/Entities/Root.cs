// ============================================================================
// Root 实体定义
// ============================================================================
// 本文件包含工程根相关的实体类型：
// - ProjectRootEntity: 工程根实体，代表整个工程的入口点
//
// 拆分自原 Entities.cs，按实体类型分组管理
// ============================================================================

using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

public sealed record ProjectRootEntity : ProjectEntity, IHasChildren<ProjectEntity>, IReadOnlyEntity<WritableProjectRootEntity>, IHasGuid
{
    // ============================================================================
    // I 数据存储
    // ============================================================================

    private readonly string _identifier = ".";

    // ============================================================================
    // II 路径核心属性
    // ============================================================================

    /// <summary>标识符（固定为 "."，表示根目录）</summary>
    public override string 实体_无相对路径标识符 => _identifier;

    // ============================================================================
    // III 非持久化内容
    // ============================================================================

    /// <summary>直接子实体列表（内部字段）</summary>
    private List<ProjectEntity> _children = [];

    /// <summary>直接子实体列表（只读，地图、机械、独立资源等）</summary>
    public IReadOnlyList<ProjectEntity> Children => _children;

    // ============================================================================
    // V 持久化内容
    // ============================================================================

    /// <summary>
    /// setting.json 记录。只解析 paletteExcel 字段，其他原样保留。
    /// </summary>
    public SettingJsonRecord? SettingJson { get; set; }

    /// <summary>
    /// setting.json.guid 记录。仅替换 guid 值，保持原格式。
    /// </summary>
    public GuidJsonRecord? SettingGuidJson { get; set; }

    // ============================================================================
    // VI 接口实现
    // ============================================================================

    // IHasChildren<ProjectEntity> 实现（协变）
    IReadOnlyList<ProjectEntity> IHasChildren<ProjectEntity>.Children => _children;

    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    public override ProjectEntityKind Kind => ProjectEntityKind.Root;

    /// <summary>setting.json 路径</summary>
    public string settingJson_完整相对路径 => ProjectEntityLayout.SettingJsonName;

    /// <summary>GUID文件路径</summary>
    public string GUID文件_完整相对路径 => settingJson_完整相对路径 + ProjectEntityLayout.GuidExt;

    public override EntityCoverage GetSelfCoverage()
    {
        return new EntityCoverage
        {
            Directories = { [实体_完整相对路径标识] = this },
            Files =
            {
                [settingJson_完整相对路径] = this,
                [GUID文件_完整相对路径] = this
            }
        };
    }

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        buffer.DeclareDirectory(实体_完整相对路径标识);
        if (SettingJson == null)
            throw new InvalidOperationException("setting.json 未加载");
        if (SettingGuidJson == null)
            throw new InvalidOperationException("setting.json.guid 未加载");
        buffer.DeclareFile(settingJson_完整相对路径, SettingJson.ToText());
        buffer.DeclareFile(GUID文件_完整相对路径, SettingGuidJson.ToText());
    }

    /// <summary>
    /// 工程根实体不绑定自身路径。
    /// setting.json 的绑定在 Load 方法中手动处理。
    /// </summary>
    public override IEnumerable<string> GetBindingPaths() { yield break; }

    // ============================================================================
    // VIII 公共方法
    // ============================================================================

    public string Guid { get; set; } = string.Empty;

    public IReadOnlyList<GuidDuplicateIssue> GuidDuplicateIssues { get; internal set; } = [];

    public override void Validate()
    {
        // 根实体无 Parent 属性，无需验证
    }

    // ============================================================================
    // IReadOnlyEntity 实现
    // ============================================================================

    /// <summary>
    /// 卸载实体：递归卸载子实体 → 解绑文件树 → 清空子实体列表。
    /// </summary>
    public void Unload()
    {
        foreach (ProjectEntity child in _children.ToList())
        {
            if (child is IReadOnlyEntity readOnlyChild)
                readOnlyChild.Unload();
        }
        _children.Clear();
    }

    /// <summary>
    /// 加载自身：递增版本号 + 加载 setting.json + 刷新文件树快照。
    /// </summary>
    public void LoadSelf(EntityLoadErrorCollector errors)
    {
        // 递增版本号
        IncrementVersion();

        // 工程根目录检查
        if (!FileIO.DirectoryExists(WorkspacePath.RootDirectory))
        {
            errors.AddError(WorkspacePath.RootDirectory, "工程根目录不存在", ToolDiagnosticSeverity.Fatal);
            return;
        }

        // 加载 setting.json（工程必需）
        LoadSettings(errors);

        // 获取文件树快照
        FileTreeSnapshot fileTreeSnapshot = FileIO.GetFileTreeSnapshot(WorkspacePath.RootDirectory);
        FileTreeBinding.Instance.RefreshFilePaths(fileTreeSnapshot.Root);

        // 读取 setting.json.guid（必需）
        string settingGuidPath = settingJson_完整相对路径 + ProjectEntityLayout.GuidExt;
        try
        {
            string text = FileIO.ReadAllText(settingGuidPath);
            SettingGuidJson = new GuidJsonRecord(text);
            Guid = SettingGuidJson.Guid;
#pragma warning disable TK011 // 文件 IO 边界校验：.guid 文件内容不应为空
            if (Guid == string.Empty)
#pragma warning restore TK011
                throw new InvalidDataException("setting.json.guid 内容无效");
        }
        catch (Exception ex)
        {
            errors.AddGuidError(settingGuidPath, ex.Message);
        }
    }

    /// <summary>
    /// 加载子实体：使用构建器集合构建所有子实体。
    /// </summary>
    public void LoadChildren(EntityLoadErrorCollector errors)
    {
        FileTreeNode? node = FileTreeBinding.Instance.FindNode(".");
        if (node == null)
        {
            errors.AddFileNotFound(".", "工程根目录");
            return;
        }

        _children.Clear();
        EntityBuildContext buildContext = EntityBuildContext.CreateForChild(node, errors, 0, this);

        EntityBuildCoordinator coordinator = new();
        foreach (ISubEntityBuilder builder in EntityBuilderSets.ForProjectRoot())
        {
            coordinator.Register(builder);
        }

        List<ProjectEntity> children = coordinator.BuildAll(buildContext);
        foreach (ProjectEntity child in children)
        {
            child.Version = GenerateChildVersion();
            _children.Add(child);
        }
    }

    /// <summary>
    /// 完整重建：LoadSelf → GUID 守卫 → LoadChildren。
    /// 遵守三层单向数据流：本流程只消费缓存层快照并投射实体树。
    /// GuidGuard 维护磁盘 .guid 后，本轮流水线短路，等待缓存层统一刷新产出新快照。
    /// </summary>
    public bool Rebuild(EntityLoadErrorCollector errors)
    {
        LoadSelf(errors);
        FileTreeNode? snapshot = FileTreeBinding.Instance.FindNode(".");
        if (snapshot == null) return false;

        GuidGuardResult guidGuardResult = GuidGuard.Execute(snapshot, errors);
        GuidDuplicateIssues = guidGuardResult.DuplicateIssues;
        if (guidGuardResult.Modified) return false;

        LoadChildren(errors);
        return true;
    }

    public WritableProjectRootEntity ToWritable()
    {
        WritableProjectRootEntity writable = new()
        {
            Guid = Guid,
            Version = Version,
            SettingJson = (SettingJsonRecord?)SettingJson?.DeepCopy(),
            SettingGuidJson = (GuidJsonRecord?)SettingGuidJson?.DeepCopy()
        };
        writable.Children = CloneChildrenToWritable(_children, writable);
        return writable;
    }

    private void LoadSettings(EntityLoadErrorCollector errors)
    {
        string settingRelativePath = "setting.json";
        if (!FileIO.FileExists(settingRelativePath))
        {
            errors.AddError(settingRelativePath, $"缺少 {settingRelativePath}", ToolDiagnosticSeverity.Fatal);
            return;
        }

        string text = FileIO.ReadAllText(settingRelativePath);
        SettingJson = new SettingJsonRecord(text);
    }
}

// ============================================================================
// 可写工程根实体
// ============================================================================

/// <summary>
/// 可写工程根实体。
/// </summary>
public sealed record WritableProjectRootEntity : ProjectEntity, IHasChildren<ProjectEntity>, IWritableEntity, IHasGuid
{
    // ============================================================================
    // I 数据存储
    // ============================================================================

    private readonly string _identifier = ".";

    public string Guid { get; set; } = string.Empty;

    // ============================================================================
    // II 路径核心属性
    // ============================================================================

    /// <summary>标识符（固定为 "."，表示根目录）</summary>
    public override string 实体_无相对路径标识符 => _identifier;

    // ============================================================================
    // III 非持久化内容
    // ============================================================================

    /// <summary>直接子实体列表（可写）</summary>
    private List<ProjectEntity> _children = [];

    /// <summary>直接子实体列表</summary>
    public List<ProjectEntity> Children
    {
        get => _children;
        set => _children = value;
    }

    // IHasChildren<ProjectEntity> 实现（协变）
    IReadOnlyList<ProjectEntity> IHasChildren<ProjectEntity>.Children => _children;

    // ============================================================================
    // V 持久化内容
    // ============================================================================

    /// <summary>
    /// setting.json 记录。只解析 paletteExcel 字段，其他原样保留。
    /// </summary>
    public SettingJsonRecord? SettingJson { get; set; }

    /// <summary>
    /// setting.json.guid 记录。仅替换 guid 值，保持原格式。
    /// </summary>
    public GuidJsonRecord? SettingGuidJson { get; set; }

    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    public override ProjectEntityKind Kind => ProjectEntityKind.Root;

    public string settingJson_完整相对路径 => ProjectEntityLayout.SettingJsonName;
    public string GUID文件_完整相对路径 => settingJson_完整相对路径 + ProjectEntityLayout.GuidExt;

    public override EntityCoverage GetSelfCoverage()
    {
        return new EntityCoverage
        {
            Directories = { [实体_完整相对路径标识] = this },
            Files =
            {
                [settingJson_完整相对路径] = this,
                [GUID文件_完整相对路径] = this
            }
        };
    }

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        buffer.DeclareDirectory(实体_完整相对路径标识);
        if (SettingJson == null)
            throw new InvalidOperationException("setting.json 未加载");
        if (SettingGuidJson == null)
            throw new InvalidOperationException("setting.json.guid 未加载");
        buffer.DeclareFile(settingJson_完整相对路径, SettingJson.ToText());
        buffer.DeclareFile(GUID文件_完整相对路径, SettingGuidJson.ToText());
    }

    public override IEnumerable<string> GetBindingPaths() { yield break; }

    public void Rename(string newName) => throw new NotSupportedException("工程根不支持重命名");

    public override void Validate()
    {
        // 根实体无 Parent 属性，无需验证
    }
}
