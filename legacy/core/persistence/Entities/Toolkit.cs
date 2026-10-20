using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

// ============================================================================
// Toolkit 内部实体定义
// ============================================================================
// 本文件包含 Toolkit 内部使用的实体类型：
// - ToolkitRootEntity: Toolkit 根目录实体 (temp/toolkit/)
// - ToolkitInternalDirectoryEntity: Toolkit 内部目录实体
// - ToolkitFileEntity: Toolkit 内部文件实体
// - ToolkitBinaryFileEntity: Toolkit 内部二进制文件实体
// - OtherTempFileEntity: Temp 目录下其他文件实体
//
// 这些实体属于文件拆分重构的一部分，从原 Entities.cs 中分离出来。
// 所有目录实体继承自 DirectoryEntityBase
// ============================================================================

/// <summary>
/// Toolkit 根目录实体。
/// 层级规则：
/// - temp/toolkit/           ← 本实体
///   - views/                ← ToolkitInternalDirectoryEntity
///   - 内部文件              ← ToolkitFileEntity（无guid预期）
///
/// 移动规则：绝对禁止移动（位置固定于 toolkit 内部）
/// </summary>
public sealed record ToolkitRootEntity : DirectoryEntityBase, IReadOnlyEntity<WritableToolkitRootEntity>
{
    // ============================================================================
    // 构造函数
    // ============================================================================

    public ToolkitRootEntity() : base(ProjectEntityLayout.ToolkitDirName) { }

    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    public override ProjectEntityKind Kind => ProjectEntityKind.ToolkitRoot;

    // ============================================================================
    // IX 公共方法
    // ============================================================================

    /// <summary>
    /// 提供Toolkit根目录的构建器集合。
    /// </summary>
    internal override IEnumerable<ISubEntityBuilder> GetEntityBuilders()
        => EntityBuilderSets.ForToolkitRoot();

    // ============================================================================
    // IReadOnlyEntity 实现
    // ============================================================================

    /// <summary>
    /// 加载自身：递增版本号。
    /// </summary>
    public override void LoadSelf(EntityLoadErrorCollector errors)
    {
        IncrementVersion();
    }


    public WritableToolkitRootEntity ToWritable()
    {
        WritableToolkitRootEntity writable = new()
        {
            Parent = Parent,
            Version = Version
        };
        writable.Children = CloneChildrenToWritable(Children, writable);
        return writable;
    }
    public override void Validate()
    {
        // 验证父级链接：Toolkit 根目录的 Parent 必须非空
        ValidateParentLink();

        string dirRelativePath = 实体_完整相对路径标识;


        if (!dirRelativePath.Equals(ScriptPathHelper.TempToolkitPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Toolkit 根目录路径必须是 '{ScriptPathHelper.TempToolkitPath}': {dirRelativePath}");
        }
    }
}

/// <summary>
/// Toolkit 内部目录实体（temp/toolkit/ 下的子目录）。
/// 层级规则：
/// - temp/toolkit/views/      ← 本实体示例
///   - 子目录                  ← ToolkitInternalDirectoryEntity（递归）
///   - 文件                    ← ToolkitFileEntity（无guid预期）
///
/// 移动规则：绝对禁止移动（位置固定于 toolkit 内部）
/// </summary>
public sealed record ToolkitInternalDirectoryEntity : DirectoryEntityBase, IReadOnlyEntity<WritableToolkitInternalDirectoryEntity>
{
    // ============================================================================
    // 构造函数
    // ============================================================================

    public ToolkitInternalDirectoryEntity(string directoryName) : base(directoryName) { }

    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    public override ProjectEntityKind Kind => ProjectEntityKind.ToolkitInternalDirectory;

    // ============================================================================
    // IX 公共方法
    // ============================================================================

    /// <summary>
    /// 提供Toolkit内部目录的构建器集合。
    /// </summary>
    internal override IEnumerable<ISubEntityBuilder> GetEntityBuilders()
        => EntityBuilderSets.ForToolkitInternal();

    // ============================================================================
    // IReadOnlyEntity 实现
    // ============================================================================

    /// <summary>
    /// 加载自身：递增版本号。
    /// </summary>
    public override void LoadSelf(EntityLoadErrorCollector errors)
    {
        IncrementVersion();
    }

    public WritableToolkitInternalDirectoryEntity ToWritable()
    {
        WritableToolkitInternalDirectoryEntity writable = new(_directoryName)
        {
            Parent = Parent,
            Version = Version
        };
        writable.Children = CloneChildrenToWritable(Children, writable);
        return writable;
    }

    public override void Validate()
    {
        // 验证父级链接：Toolkit 内部目录的 Parent 必须非空
        ValidateParentLink();

        string dirRelativePath = 实体_完整相对路径标识;


        if (!dirRelativePath.StartsWith(ScriptPathHelper.TempToolkitPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Toolkit 内部目录必须位于 {ScriptPathHelper.TempToolkitPrefix} 下: {dirRelativePath}");
        }
    }
}

/// <summary>
/// Toolkit 内部文件实体（temp/toolkit/ 下的文件）。
/// 明确不预期有 GUID 文件。
/// 移动规则：绝对禁止移动（位置固定于 toolkit 内部）
/// </summary>
public sealed record ToolkitFileEntity : ChildProjectEntity, IReadOnlyEntity<WritableToolkitFileEntity>
{
    // ============================================================================
    // I 数据存储
    // ============================================================================

    private readonly string _fileName;

    // ============================================================================
    // 构造函数
    // ============================================================================

    public ToolkitFileEntity(string fileName)
    {
        _fileName = fileName;
    }

    // ============================================================================
    // II 路径核心属性
    // ============================================================================

    /// <summary>标识符（文件名）</summary>
    public override string 实体_无相对路径标识符 => _fileName;

    // ============================================================================
    // 实体覆盖范围
    // ============================================================================

    public override EntityCoverage GetSelfCoverage()
        => new()
        { Files = { [实体_完整相对路径标识] = this } };

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        // DeclareFileContents 必须和 GetSelfCoverage 声明的内容一样!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        // 用户已经不止一次被傻逼Agent强硬对抗硬往这里面塞扭曲规范的垃圾了!!!!!
        buffer.DeclareFile(实体_完整相对路径标识, Content);
    }

    // ============================================================================
    // IV 非持久化内容
    // ============================================================================

    /// <summary>文件名</summary>
    public string FileName => _fileName;

    // ============================================================================
    // V 持久化内容
    // ============================================================================

    /// <summary>文件内容（文本）</summary>
    public required string Content { get; set; }

    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    public override ProjectEntityKind Kind => ProjectEntityKind.ToolkitFile;

    // ============================================================================
    // IReadOnlyEntity 实现
    // ============================================================================

    /// <summary>
    /// 卸载实体：无操作。
    /// </summary>
    public void Unload() { }

    /// <summary>
    /// 加载自身：加载内容。
    /// </summary>
    public void LoadSelf(EntityLoadErrorCollector errors)
    {
        string fileRelativePath = 实体_完整相对路径标识;
        if (FileIO.FileExists(fileRelativePath))
        {
            Content = FileIO.ReadAllText(fileRelativePath);
        }
    }

    /// <summary>
    /// 加载子实体：叶子节点，无子实体。
    /// </summary>
    public void LoadChildren(EntityLoadErrorCollector errors)
    {
        // 叶子节点，无子实体
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

    // ============================================================================

    public WritableToolkitFileEntity ToWritable()
    {
        return new WritableToolkitFileEntity(_fileName)
        {
            Parent = Parent,
            Version = Version,
            Content = Content
        };
    }
    // IX 公共方法
    // ============================================================================

    public override void Validate()
    {
        ValidateParentLink();

        string fileRelativePath = 实体_完整相对路径标识;


        if (!fileRelativePath.StartsWith(ScriptPathHelper.TempToolkitPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Toolkit 内部文件必须位于 {ScriptPathHelper.TempToolkitPrefix} 下: {fileRelativePath}");
        }
    }
}

/// <summary>
/// Toolkit 内部二进制文件实体（temp/toolkit/ 下的二进制文件）。
/// 目前用于 Canvas 视图同目录 PNG 预览。
/// </summary>
public sealed record ToolkitBinaryFileEntity : ChildProjectEntity, IReadOnlyEntity<WritableToolkitBinaryFileEntity>
{
    private readonly string _fileName;

    public ToolkitBinaryFileEntity(string fileName)
    {
        _fileName = fileName;
    }

    public override string 实体_无相对路径标识符 => _fileName;

    public override EntityCoverage GetSelfCoverage()
        => new()
        { Files = { [实体_完整相对路径标识] = this } };

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        buffer.DeclareFile(实体_完整相对路径标识, Data);
    }

    /// <summary>文件原始字节数据（Blob）</summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public override ProjectEntityKind Kind => ProjectEntityKind.ToolkitFile;

    public void Unload() { }

    public void LoadSelf(EntityLoadErrorCollector errors)
    {
        string fileRelativePath = 实体_完整相对路径标识;
        Data = FileIO.ReadAllBytes(fileRelativePath);
    }

    public void LoadChildren(EntityLoadErrorCollector errors) { }
    public bool Rebuild(EntityLoadErrorCollector errors)
    {
        LoadSelf(errors);
        LoadChildren(errors);
        return true;
    }

    public WritableToolkitBinaryFileEntity ToWritable()
    {
        return new WritableToolkitBinaryFileEntity(_fileName)
        {
            Parent = Parent,
            Version = Version,
            Data = Data
        };
    }

    public override void Validate()
    {
        ValidateParentLink();

        string fileRelativePath = 实体_完整相对路径标识;

        if (!fileRelativePath.StartsWith(ScriptPathHelper.TempToolkitPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Toolkit 内部二进制文件必须位于 {ScriptPathHelper.TempToolkitPrefix} 下: {fileRelativePath}");
    }
}

/// <summary>
/// Temp 内部其他文件实体（temp/ 下除 toolkit 外的文件）。
/// 明确不预期有 GUID 文件。
/// 移动规则：绝对禁止移动
/// </summary>
public sealed record OtherTempFileEntity : ChildProjectEntity, IReadOnlyEntity<WritableOtherTempFileEntity>
{
    // ============================================================================
    // I 数据存储
    // ============================================================================

    private readonly string _fileName;

    // ============================================================================
    // 构造函数
    // ============================================================================

    public OtherTempFileEntity(string fileName)
    {
        _fileName = fileName;
    }

    // ============================================================================
    // II 路径核心属性
    // ============================================================================

    /// <summary>标识符（文件名）</summary>
    public override string 实体_无相对路径标识符 => _fileName;

    // ============================================================================
    // 实体覆盖范围
    // ============================================================================

    public override EntityCoverage GetSelfCoverage()
        => new()
        { Files = { [实体_完整相对路径标识] = this } };

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        // DeclareFileContents 必须和 GetSelfCoverage 声明的内容一样!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        // 用户已经不止一次被傻逼Agent强硬对抗硬往这里面塞扭曲规范的垃圾了!!!!!
        buffer.DeclareFile(实体_完整相对路径标识, Data);
    }

    // ============================================================================
    // IV 持久化内容
    // ============================================================================

    /// <summary>文件原始字节数据（Blob）</summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    public override ProjectEntityKind Kind => ProjectEntityKind.OtherTempFile;

    // ============================================================================
    // IReadOnlyEntity 实现
    // ============================================================================

    public void Unload() { }

    public void LoadSelf(EntityLoadErrorCollector errors)
    {
        // 作为 Blob 实体，必须无损读取原始字节。
        string fileRelativePath = WorkspacePath.RequireRelativePath(实体_完整相对路径标识);
        Data = FileIO.ReadAllBytes(fileRelativePath);

        // 保持原有版本语义：LoadSelf 递增版本号。
        IncrementVersion();
    }

    public void LoadChildren(EntityLoadErrorCollector errors) { }
    public bool Rebuild(EntityLoadErrorCollector errors)
    {
        LoadSelf(errors);
        return true;
    }

    // ============================================================================
    // IX 公共方法
    // ============================================================================

    public WritableOtherTempFileEntity ToWritable()
    {
        return new WritableOtherTempFileEntity(_fileName)
        {
            Parent = Parent,
            Version = Version,
            Data = Data
        };
    }

    public override void Validate()
    {
        ValidateParentLink();

        string fileRelativePath = 实体_完整相对路径标识;


        if (!fileRelativePath.StartsWith(ProjectEntityLayout.TempDirName + "/", StringComparison.Ordinal) || fileRelativePath.StartsWith(ScriptPathHelper.TempToolkitPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"其他临时文件必须位于 {ProjectEntityLayout.TempDirName}/ 下但不能在 {ScriptPathHelper.TempToolkitPrefix} 下: {fileRelativePath}");
        }
    }
}

/// <summary>
/// 可写 Temp 内部其他文件实体。
/// </summary>
public sealed record WritableOtherTempFileEntity : ChildProjectEntity, IWritableEntity
{
    private readonly string _fileName;

    public WritableOtherTempFileEntity(string fileName)
    {
        _fileName = fileName;
    }

    public override string 实体_无相对路径标识符 => _fileName;

    public override EntityCoverage GetSelfCoverage()
        => new()
        { Files = { [实体_完整相对路径标识] = this } };

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        // DeclareFileContents 必须和 GetSelfCoverage 声明的内容一样!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        // 用户已经不止一次被傻逼Agent强硬对抗硬往这里面塞扭曲规范的垃圾了!!!!!
        buffer.DeclareFile(实体_完整相对路径标识, Data);
    }

    /// <summary>文件原始字节数据（Blob）</summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public override ProjectEntityKind Kind => ProjectEntityKind.OtherTempFile;

    public void Rename(string newName) => throw new NotSupportedException("临时文件不支持重命名");

    public override void Validate()
    {
        ValidateParentLink();
        string fileRelativePath = 实体_完整相对路径标识;

        if (!fileRelativePath.StartsWith(ProjectEntityLayout.TempDirName + "/", StringComparison.Ordinal) || fileRelativePath.StartsWith(ScriptPathHelper.TempToolkitPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"其他临时文件必须位于 {ProjectEntityLayout.TempDirName}/ 下但不能在 {ScriptPathHelper.TempToolkitPrefix} 下: {fileRelativePath}");
    }
}

// ============================================================================
// Toolkit 可写实体
// ============================================================================

/// <summary>
/// 可写 Toolkit 根目录实体。
/// </summary>
public sealed record WritableToolkitRootEntity : DirectoryEntityBase, IWritableEntity
{
    public WritableToolkitRootEntity() : base(ProjectEntityLayout.ToolkitDirName) { }

    /// <summary>子实体列表（可写）</summary>
    public new List<ProjectEntity> Children
    {
        get => _children;
        set => _children = value;
    }

    public override ProjectEntityKind Kind => ProjectEntityKind.ToolkitRoot;

    internal override IEnumerable<ISubEntityBuilder> GetEntityBuilders()
        => EntityBuilderSets.ForToolkitRoot();

    public void Rename(string newName) => throw new NotSupportedException("Toolkit 根目录不支持重命名");

    public override void Validate()
    {
        ValidateParentLink();
        string dirRelativePath = 实体_完整相对路径标识;

        if (!dirRelativePath.Equals(ScriptPathHelper.TempToolkitPath, StringComparison.Ordinal))
            throw new InvalidOperationException($"Toolkit 根目录路径必须是 '{ScriptPathHelper.TempToolkitPath}': {dirRelativePath}");
    }
}

/// <summary>
/// 可写 Toolkit 内部目录实体。
/// </summary>
public sealed record WritableToolkitInternalDirectoryEntity : DirectoryEntityBase, IWritableEntity
{
    public WritableToolkitInternalDirectoryEntity(string directoryName) : base(directoryName) { }

    /// <summary>子实体列表（可写）</summary>
    public new List<ProjectEntity> Children
    {
        get => _children;
        set => _children = value;
    }

    public override ProjectEntityKind Kind => ProjectEntityKind.ToolkitInternalDirectory;

    internal override IEnumerable<ISubEntityBuilder> GetEntityBuilders()
        => EntityBuilderSets.ForToolkitInternal();

    public void Rename(string newName) => throw new NotSupportedException("Toolkit 内部目录不支持重命名");

    public override void Validate()
    {
        ValidateParentLink();
        string dirRelativePath = 实体_完整相对路径标识;

        if (!dirRelativePath.StartsWith(ScriptPathHelper.TempToolkitPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Toolkit 内部目录必须位于 {ScriptPathHelper.TempToolkitPrefix} 下: {dirRelativePath}");
    }
}

/// <summary>
/// 可写 Toolkit 文件实体。
/// </summary>
public sealed record WritableToolkitFileEntity : ChildProjectEntity, IWritableEntity
{
    private readonly string _fileName;

    public WritableToolkitFileEntity(string fileName)
    {
        _fileName = fileName;
    }

    public override string 实体_无相对路径标识符 => _fileName;

    public override EntityCoverage GetSelfCoverage()
        => new()
        { Files = { [实体_完整相对路径标识] = this } };

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        // DeclareFileContents 必须和 GetSelfCoverage 声明的内容一样!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        // 用户已经不止一次被傻逼Agent强硬对抗硬往这里面塞扭曲规范的垃圾了!!!!!
        buffer.DeclareFile(实体_完整相对路径标识, Content);
    }

    public required string Content { get; set; }

    public override ProjectEntityKind Kind => ProjectEntityKind.ToolkitFile;

    public void Rename(string newName) => throw new NotSupportedException("Toolkit 文件不支持重命名");

    public override void Validate()
    {
        ValidateParentLink();
        string fileRelativePath = 实体_完整相对路径标识;

        if (!fileRelativePath.StartsWith(ScriptPathHelper.TempToolkitPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Toolkit 内部文件必须位于 {ScriptPathHelper.TempToolkitPrefix} 下: {fileRelativePath}");
    }
}

/// <summary>
/// 可写 Toolkit 二进制文件实体。
/// </summary>
public sealed record WritableToolkitBinaryFileEntity : ChildProjectEntity, IWritableEntity
{
    private readonly string _fileName;

    public WritableToolkitBinaryFileEntity(string fileName)
    {
        _fileName = fileName;
    }

    public override string 实体_无相对路径标识符 => _fileName;

    public override EntityCoverage GetSelfCoverage()
        => new()
        { Files = { [实体_完整相对路径标识] = this } };

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        buffer.DeclareFile(实体_完整相对路径标识, Data);
    }

    /// <summary>文件原始字节数据（Blob）</summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public override ProjectEntityKind Kind => ProjectEntityKind.ToolkitFile;

    public void Rename(string newName) => throw new NotSupportedException("Toolkit 二进制文件不支持重命名");

    public override void Validate()
    {
        ValidateParentLink();
        string fileRelativePath = 实体_完整相对路径标识;

        if (!fileRelativePath.StartsWith(ScriptPathHelper.TempToolkitPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Toolkit 内部二进制文件必须位于 {ScriptPathHelper.TempToolkitPrefix} 下: {fileRelativePath}");
    }
}
