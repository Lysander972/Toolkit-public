using Toolkit.Core.Refactor;

namespace Toolkit.Core.Persistence;

// ============================================================================
// Directory 实体定义
// ============================================================================
// 本文件包含所有目录类型的实体定义：
// - DirectoryEntity: 常规目录实体（只读）
// - WritableDirectoryEntity: 可写目录实体（支持重命名）
// - TempRootEntity: Temp 根目录实体
// - ModDirectoryEntity: 编辑器 Mod 目录实体
//
// 所有目录实体继承自 DirectoryEntityBase，标识符等于目录名
// ============================================================================

/// <summary>
/// 常规目录实体（只读版本）。
/// 无 GUID 独立实体。
/// </summary>
public sealed record DirectoryEntity : DirectoryEntityBase, IReadOnlyEntity<WritableDirectoryEntity>, IRenameable
{
    // ============================================================================
    // 构造函数
    // ============================================================================

    public DirectoryEntity(string directoryName)
    {
        _directoryName = directoryName;
    }

    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    public override ProjectEntityKind Kind => ProjectEntityKind.Directory;

    // ============================================================================
    // IX 公共方法
    // ============================================================================

    /// <summary>
    /// 提供常规目录的构建器集合。
    /// </summary>
    internal override IEnumerable<ISubEntityBuilder> GetEntityBuilders()
        => EntityBuilderSets.ForRegularDirectory();

    /// <summary>
    /// 加载自身：递增版本号。
    /// </summary>
    public override void LoadSelf(EntityLoadErrorCollector errors)
    {
        IncrementVersion();
    }

    /// <summary>
    /// 转换为可写副本。
    /// </summary>
    public WritableDirectoryEntity ToWritable()
    {
        WritableDirectoryEntity writable = new(DirectoryName)
        {
            Parent = Parent,
            Version = Version
        };
        writable.Children = CloneChildrenToWritable(Children, writable);
        return writable;
    }

    public override void Validate()
    {
        ValidateParentLink();
    }
}

/// <summary>
/// 可写目录实体。用完即丢弃。
/// </summary>
public sealed record WritableDirectoryEntity : DirectoryEntityBase, IWritableEntity
{
    // ============================================================================
    // 构造函数
    // ============================================================================

    public WritableDirectoryEntity(string directoryName)
    {
        _directoryName = directoryName;
    }

    // ============================================================================
    // 属性重写（支持修改）
    // ============================================================================

    /// <summary>标识符（可写，支持重命名）</summary>
    public new string 实体_无相对路径标识符
    {
        get => _directoryName;
        internal set => _directoryName = value;
    }

    /// <summary>子实体列表（可写）</summary>
    public new List<ProjectEntity> Children
    {
        get => _children;
        set => _children = value;
    }

    public override ProjectEntityKind Kind => ProjectEntityKind.Directory;

    internal override IEnumerable<ISubEntityBuilder> GetEntityBuilders()
        => EntityBuilderSets.ForRegularDirectory();

    /// <summary>
    /// 重命名目录。
    /// </summary>
    public void Rename(string newName)
    {
        实体_无相对路径标识符 = newName;
    }

    public override void Validate()
    {
        ValidateParentLink();
    }
}

// =============================================================================
// 特殊目录实体（只能直属于工程根）
// ============================================================================

/// <summary>
/// Temp 根目录实体。
/// 只能直属于工程根，内部包含 toolkit 目录和其他临时文件。
/// 层级规则：
/// - temp/                    ← 本实体
///   - toolkit/               ← ToolkitRootEntity
///   - 其他临时文件（无guid）  ← OtherTempFileEntity
///
/// 移动规则：绝对禁止移动（位置固定为工程根下的 temp）
/// </summary>
public sealed record TempRootEntity : DirectoryEntityBase, IReadOnlyEntity<WritableTempRootEntity>
{
    // ============================================================================
    // 构造函数
    // ============================================================================

    public TempRootEntity() : base(ProjectEntityLayout.TempDirName) { }

    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    public override ProjectEntityKind Kind => ProjectEntityKind.TempRoot;

    // ============================================================================
    // IX 公共方法
    // ============================================================================

    /// <summary>
    /// 提供Temp目录的构建器集合。
    /// </summary>
    internal override IEnumerable<ISubEntityBuilder> GetEntityBuilders()
        => EntityBuilderSets.ForTempRoot();

    /// <summary>
    /// 加载自身：递增版本号。
    /// </summary>
    public override void LoadSelf(EntityLoadErrorCollector errors)
    {
        IncrementVersion();
    }

    public WritableTempRootEntity ToWritable()
    {
        WritableTempRootEntity writable = new(DirectoryName)
        {
            Parent = Parent,
            Version = Version
        };
        writable.Children = CloneChildrenToWritable(Children, writable);
        return writable;
    }

    public override void Validate()
    {
        ValidateParentLink();
        string dirRelativePath = 实体_完整相对路径标识;

        if (!dirRelativePath.Equals(ProjectEntityLayout.TempDirName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Temp 根目录路径必须是 '{ProjectEntityLayout.TempDirName}': {dirRelativePath}");
        }
    }
}

/// <summary>
/// 编辑器 Mod 目录实体（MechEditorCode 或 MechEditorToolCode）。
/// 只能直属于工程根，仅接受独立脚本作为子实体。
/// 层级规则：
/// - MechEditorCode/          ← 本实体
///   - *.code                 ← CodeRepoScriptEntity（仅独立脚本）
///   - *.code.guid
///
/// 移动规则：绝对禁止移动（位置固定为工程根下的特殊目录）
/// </summary>
public sealed record ModDirectoryEntity : DirectoryEntityBase, IReadOnlyEntity<WritableModDirectoryEntity>
{
    // ============================================================================
    // 构造函数
    // ============================================================================

    public ModDirectoryEntity(string directoryName) : base(directoryName) { }

    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    public override ProjectEntityKind Kind => ProjectEntityKind.ModDirectory;

    // ============================================================================
    // IX 公共方法
    // ============================================================================

    /// <summary>
    /// 提供Mod目录的构建器集合。
    /// </summary>
    internal override IEnumerable<ISubEntityBuilder> GetEntityBuilders()
        => EntityBuilderSets.ForModDirectory();

    /// <summary>
    /// 加载自身：递增版本号。
    /// </summary>
    public override void LoadSelf(EntityLoadErrorCollector errors)
    {
        IncrementVersion();
    }

    public WritableModDirectoryEntity ToWritable()
    {
        WritableModDirectoryEntity writable = new(DirectoryName)
        {
            Parent = Parent,
            Version = Version
        };
        writable.Children = CloneChildrenToWritable(Children, writable);
        return writable;
    }

    public override void Validate()
    {
        ValidateParentLink();
        string dirRelativePath = 实体_完整相对路径标识;

        if (!ScriptPathHelper.IsEditorSegment(DirectoryName))
        {
            throw new InvalidOperationException($"Mod 目录名必须是 MechEditorCode 或 MechEditorToolCode: {dirRelativePath}");
        }
    }
}

/// <summary>
/// 可写 Mod 目录实体。
/// </summary>
public sealed record WritableModDirectoryEntity : DirectoryEntityBase, IWritableEntity
{
    // ============================================================================
    // 构造函数
    // ============================================================================

    public WritableModDirectoryEntity(string directoryName) : base(directoryName) { }
    /// <summary>子实体列表（可写）</summary>
    public new List<ProjectEntity> Children
    {
        get => _children;
        set => _children = value;
    }

    public override ProjectEntityKind Kind => ProjectEntityKind.ModDirectory;

    internal override IEnumerable<ISubEntityBuilder> GetEntityBuilders()
        => EntityBuilderSets.ForModDirectory();

    public void Rename(string newName) => throw new NotSupportedException("Mod 目录不支持重命名");

    public override void Validate()
    {
        ValidateParentLink();
        string dirRelativePath = 实体_完整相对路径标识;
        if (!ScriptPathHelper.IsEditorSegment(DirectoryName))
            throw new InvalidOperationException($"Mod 目录名必须是 MechEditorCode 或 MechEditorToolCode: {dirRelativePath}");
    }
}

// ============================================================================
// Temp 可写实体
// ============================================================================

/// <summary>
/// 可写 Temp 根目录实体。
/// </summary>
public sealed record WritableTempRootEntity : DirectoryEntityBase, IWritableEntity
{
    // ============================================================================
    // 构造函数
    // ============================================================================

    public WritableTempRootEntity(string directoryName) : base(directoryName) { }

    /// <summary>子实体列表（可写）</summary>
    public new List<ProjectEntity> Children
    {
        get => _children;
        set => _children = value;
    }

    public override ProjectEntityKind Kind => ProjectEntityKind.TempRoot;

    internal override IEnumerable<ISubEntityBuilder> GetEntityBuilders()
        => EntityBuilderSets.ForTempRoot();

    public void Rename(string newName) => throw new NotSupportedException("Temp 根目录不支持重命名");

    public override void Validate()
    {
        ValidateParentLink();
        string dirRelativePath = 实体_完整相对路径标识;
        if (!dirRelativePath.Equals(ProjectEntityLayout.TempDirName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Temp 根目录路径必须是 '{ProjectEntityLayout.TempDirName}': {dirRelativePath}");
    }
}
