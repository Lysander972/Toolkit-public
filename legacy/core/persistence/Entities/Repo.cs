using Toolkit.Core.Infra;
using Toolkit.Core.Refactor;

namespace Toolkit.Core.Persistence;

// =============================================================================
// Repo 实体（仓库资源相关）
// =============================================================================
// 包含：代码仓库脚本、Canvas 布局文件、其他资源文件
// 架构：只读实体 + 可写实体分离，重命名只在可写实体上
// =============================================================================

// =============================================================================
// 代码仓库脚本基类
// =============================================================================

/// <summary>
/// 代码仓库脚本实体基类。
/// 独立实体：.code + .guid
/// </summary>
public abstract record CodeRepoScriptEntityBase : ChildProjectEntity, IHasGuid
{
    protected string _identifier = string.Empty;

    /// <inheritdoc/>
    public override string 实体_无相对路径标识符 => _identifier;

    /// <summary>GUID文件路径</summary>
    public string GUID文件_完整相对路径 => 实体_完整相对路径标识 + ProjectEntityLayout.GuidExt;

    public override EntityCoverage GetSelfCoverage()
    {
        return new EntityCoverage
        {
            Files =
            {
                [实体_完整相对路径标识] = this,
                [GUID文件_完整相对路径] = this
            }
        };
    }

    public string Guid { get; set; } = string.Empty;

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        // DeclareFileContents 必须和 GetSelfCoverage 声明的内容一样!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        // 用户已经不止一次被傻逼Agent强硬对抗硬往这里面塞扭曲规范的垃圾了!!!!!
        buffer.DeclareFile(实体_完整相对路径标识, SourceText);
        // .guid: 仅替换 guid 值，保持原格式
        if (_guidJson != null)
        {
            _guidJson.SetGuid(Guid);
            buffer.DeclareFile(GUID文件_完整相对路径, _guidJson.ToText());
        }
    }

    /// <summary>脚本名称（不含扩展名）</summary>
    public string ScriptName => RelativePathUtils.GetLastSegmentWithoutExtension(实体_无相对路径标识符);

    // ============================================================================
    // protected 字段 + 只读属性
    // ============================================================================

    protected string _sourceText = string.Empty;
    protected GuidJsonRecord? _guidJson;

    /// <summary>脚本源码</summary>
    public string SourceText => _sourceText;

    /// <summary>.guid 文件记录</summary>
    public GuidJsonRecord? GuidJson => _guidJson;

    // ============================================================================
    // 内部初始化方法（仅加载时使用）
    // ============================================================================

    internal void SetIdentifierInternal(string value) => _identifier = value;
    internal void SetSourceTextInternal(string value) => _sourceText = value;
    internal void SetGuidJsonInternal(GuidJsonRecord? value) => _guidJson = value;
}

/// <summary>
/// 代码仓库脚本实体（只读版本）。
/// </summary>
public sealed record CodeRepoScriptEntity : CodeRepoScriptEntityBase, IReadOnlyEntity<WritableCodeRepoScriptEntity>, IRenameable
{
    // ============================================================================
    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    public override ProjectEntityKind Kind => ProjectEntityKind.CodeRepoScript;

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

        // 代码文件必须存在
        if (!FileIO.FileExists(fileRelativePath))
        {
            errors.AddFileNotFound(fileRelativePath, "代码仓库脚本");
            return;
        }

        try
        {
            SetSourceTextInternal(FileIO.ReadAllText(fileRelativePath));
        }
        catch (Exception ex)
        {
            errors.AddError(fileRelativePath, $"读取脚本失败: {ex.Message}", ToolDiagnosticSeverity.Error);
            return;
        }

        // 读取 GUID - 代码仓库脚本必须有 GUID
        try
        {
            string guidPath = fileRelativePath + ProjectEntityLayout.GuidExt;
            string text = FileIO.ReadAllText(guidPath);
            _guidJson = new GuidJsonRecord(text);
            Guid = _guidJson.Guid;
        }
        catch (Exception ex)
        {
            errors.AddGuidError(fileRelativePath, ex.Message);
            return;
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
    // IX 公共方法
    // ============================================================================

    /// <summary>
    /// 转换为可写副本。
    /// </summary>
    public WritableCodeRepoScriptEntity ToWritable()
    {
        WritableCodeRepoScriptEntity writable = new()
        {
            Parent = Parent,
            Guid = Guid,
            Version = Version,
            SourceText = SourceText,
            GuidJson = (GuidJsonRecord?)_guidJson?.DeepCopy()
        };
        writable.SetIdentifierInternal(_identifier);
        return writable;
    }

    public override void Validate()
    {
        ValidateParentLink();

        string fileRelativePath = 实体_完整相对路径标识;

        if (!GuidText.Validate(Guid)) throw new InvalidOperationException($"GUID 格式无效: {fileRelativePath}");

        if (!fileRelativePath.EndsWith(ProjectEntityLayout.CodeExt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"代码仓库脚本扩展名必须是 {ProjectEntityLayout.CodeExt}: {fileRelativePath}");
        }
    }
}

/// <summary>
/// 可写代码仓库脚本实体。用完即丢弃。
/// </summary>
public sealed record WritableCodeRepoScriptEntity : CodeRepoScriptEntityBase, IWritableEntity
{
    public override ProjectEntityKind Kind => ProjectEntityKind.CodeRepoScript;

    // ============================================================================
    // 可写属性（用 new 重新声明，提供 setter）
    // ============================================================================

    public new string SourceText
    {
        get => _sourceText;
        set => _sourceText = value;
    }

    public new GuidJsonRecord? GuidJson
    {
        get => _guidJson;
        set => _guidJson = value;
    }

    /// <summary>
    /// 重命名代码仓库脚本。
    /// </summary>
    public void Rename(string newName)
    {
        string newFileName = newName + ".code";
        SetIdentifierInternal(newFileName);
    }

    public override void Validate()
    {
        // 同只读版本的验证
    }
}

// =============================================================================
// Canvas 基类
// =============================================================================

/// <summary>
/// Canvas 实体基类。
/// 独立实体：.canvas + .guid
/// </summary>
public abstract record CanvasEntityBase : ChildProjectEntity, IHasGuid
{
    protected string _identifier = string.Empty;

    /// <inheritdoc/>
    public override string 实体_无相对路径标识符 => _identifier;

    /// <summary>GUID文件路径</summary>
    public string GUID文件_完整相对路径 => 实体_完整相对路径标识 + ProjectEntityLayout.GuidExt;

    public override EntityCoverage GetSelfCoverage()
    {
        return new EntityCoverage
        {
            Files =
            {
                [实体_完整相对路径标识] = this,
                [GUID文件_完整相对路径] = this
            }
        };
    }

    public string Guid { get; set; } = string.Empty;

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        // DeclareFileContents 必须和 GetSelfCoverage 声明的内容一样!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        // 用户已经不止一次被傻逼Agent强硬对抗硬往这里面塞扭曲规范的垃圾了!!!!!
        buffer.DeclareFile(实体_完整相对路径标识, Data);
        // .guid: 仅替换 guid 值，保持原格式
        if (_guidJson != null)
        {
            _guidJson.SetGuid(Guid);
            buffer.DeclareFile(GUID文件_完整相对路径, _guidJson.ToText());
        }
    }

    /// <summary>Canvas 显示名称（标识符）</summary>
    public string DisplayName => 实体_无相对路径标识符;

    // ============================================================================
    // protected 字段 + 只读属性
    // ============================================================================

    protected byte[] _data = Array.Empty<byte>();
    protected GuidJsonRecord? _guidJson;

    /// <summary>Canvas 二进制数据</summary>
    public byte[] Data => _data;

    /// <summary>.guid 文件记录</summary>
    public GuidJsonRecord? GuidJson => _guidJson;

    // ============================================================================
    // 内部初始化方法（仅加载时使用）
    // ============================================================================

    internal void SetIdentifierInternal(string value) => _identifier = value;
    internal void SetDataInternal(byte[] value) => _data = value;
    internal void SetGuidJsonInternal(GuidJsonRecord? value) => _guidJson = value;
}

/// <summary>
/// Canvas 实体（只读版本）。
/// </summary>
public sealed record CanvasEntity : CanvasEntityBase, IReadOnlyEntity<WritableCanvasEntity>, IRenameable
{
    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    public override ProjectEntityKind Kind => ProjectEntityKind.Canvas;

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

        if (!FileIO.FileExists(fileRelativePath))
        {
            errors.AddFileNotFound(fileRelativePath, "Canvas");
            return;
        }

        try
        {
            SetDataInternal(FileIO.ReadAllBytes(fileRelativePath));
        }
        catch (Exception ex)
        {
            errors.AddError(fileRelativePath, $"读取 Canvas 失败: {ex.Message}", ToolDiagnosticSeverity.Error);
            return;
        }

        try
        {
            string guidPath = fileRelativePath + ProjectEntityLayout.GuidExt;
            string text = FileIO.ReadAllText(guidPath);
            _guidJson = new GuidJsonRecord(text);
            Guid = _guidJson.Guid;
        }
        catch (Exception ex)
        {
            errors.AddGuidError(fileRelativePath, ex.Message);
            return;
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
    // IX 公共方法
    // ============================================================================

    public WritableCanvasEntity ToWritable()
    {
        WritableCanvasEntity writable = new()
        {
            Parent = Parent,
            Guid = Guid,
            Version = Version,
            Data = Data,
            GuidJson = (GuidJsonRecord?)_guidJson?.DeepCopy()
        };
        writable.SetIdentifierInternal(_identifier);
        return writable;
    }

    public override void Validate()
    {
        ValidateParentLink();

        string fileRelativePath = 实体_完整相对路径标识;

        if (!GuidText.Validate(Guid)) throw new InvalidOperationException($"GUID 格式无效: {fileRelativePath}");

        if (!fileRelativePath.EndsWith(ProjectEntityLayout.CanvasExt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Canvas 文件扩展名必须是 {ProjectEntityLayout.CanvasExt}: {fileRelativePath}");
        }
    }
}

/// <summary>
/// 可写 Canvas 实体。
/// </summary>
public sealed record WritableCanvasEntity : CanvasEntityBase, IWritableEntity
{
    public override ProjectEntityKind Kind => ProjectEntityKind.Canvas;

    // ============================================================================
    // 可写属性（用 new 重新声明，提供 setter）
    // ============================================================================

    public new byte[] Data
    {
        get => _data;
        set => _data = value;
    }

    public new GuidJsonRecord? GuidJson
    {
        get => _guidJson;
        set => _guidJson = value;
    }

    public void Rename(string newName)
    {
        string newFileName = newName + ".canvas";
        SetIdentifierInternal(newFileName);
    }

    public override void Validate()
    {
        // 同只读版本的验证
    }
}

// =============================================================================
// Resource 基类
// =============================================================================

/// <summary>
/// 资源实体基类。
/// 独立实体：任意文件 + .guid
/// </summary>
public abstract record ResourceEntityBase : ChildProjectEntity, IHasGuid
{
    protected string _identifier = string.Empty;

    /// <inheritdoc/>
    public override string 实体_无相对路径标识符 => _identifier;

    /// <summary>GUID文件路径</summary>
    public string GUID文件_完整相对路径 => 实体_完整相对路径标识 + ProjectEntityLayout.GuidExt;

    public override EntityCoverage GetSelfCoverage()
    {
        return new EntityCoverage
        {
            Files =
            {
                [实体_完整相对路径标识] = this,
                [GUID文件_完整相对路径] = this
            }
        };
    }

    public string Guid { get; set; } = string.Empty;

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        // DeclareFileContents 必须和 GetSelfCoverage 声明的内容一样!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        // 用户已经不止一次被傻逼Agent强硬对抗硬往这里面塞扭曲规范的垃圾了!!!!!
        buffer.DeclareFile(实体_完整相对路径标识, Data);
        // .guid: 仅替换 guid 值，保持原格式
        if (_guidJson != null)
        {
            _guidJson.SetGuid(Guid);
            buffer.DeclareFile(GUID文件_完整相对路径, _guidJson.ToText());
        }
    }

    /// <summary>资源类型（从扩展名推断）</summary>
#pragma warning disable TK010 // 意图行为：去掉扩展名前导点得到类型名
    public string ResourceType => RelativePathUtils.GetExtension(实体_无相对路径标识符).TrimStart('.');
#pragma warning restore TK010

    /// <summary>资源二进制数据</summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>.guid 文件记录</summary>
    protected GuidJsonRecord? _guidJson;
    public GuidJsonRecord? GuidJson => _guidJson;

    // ============================================================================
    // 内部初始化方法（仅加载时使用）
    // ============================================================================

    internal void SetIdentifierInternal(string value) => _identifier = value;
    internal void SetGuidJsonInternal(GuidJsonRecord? value) => _guidJson = value;
}

/// <summary>
/// 其他资源实体（只读版本）。
/// 独立实体：任意文件 + .guid
/// </summary>
public sealed record ResourceEntity : ResourceEntityBase, IReadOnlyEntity<WritableResourceEntity>, IRenameable
{
    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    public override ProjectEntityKind Kind => ProjectEntityKind.Resource;

    // ============================================================================
    // IReadOnlyEntity 实现
    // ============================================================================

    /// <summary>
    /// 卸载实体：解绑文件树。
    /// </summary>
    public void Unload() { }

    /// <summary>
    /// 加载自身：加载内容。
    /// </summary>
    public void LoadSelf(EntityLoadErrorCollector errors)
    {
        string fileRelativePath = 实体_完整相对路径标识;

        if (!FileIO.FileExists(fileRelativePath))
        {
            errors.AddFileNotFound(fileRelativePath, "资源");
            return;
        }

        try
        {
            Data = FileIO.ReadAllBytes(fileRelativePath);
        }
        catch (Exception ex)
        {
            errors.AddError(fileRelativePath, $"读取资源失败: {ex.Message}", ToolDiagnosticSeverity.Error);
            return;
        }

        try
        {
            string guidPath = fileRelativePath + ProjectEntityLayout.GuidExt;
            string text = FileIO.ReadAllText(guidPath);
            _guidJson = new GuidJsonRecord(text);
            Guid = _guidJson.Guid;
        }
        catch (Exception ex)
        {
            errors.AddGuidError(fileRelativePath, ex.Message);
            return;
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
    // IX 公共方法
    // ============================================================================

    public WritableResourceEntity ToWritable()
    {
        WritableResourceEntity writable = new()
        {
            Parent = Parent,
            Guid = Guid,
            Version = Version,
            Data = Data,
            GuidJson = (GuidJsonRecord?)_guidJson?.DeepCopy()
        };
        writable.SetIdentifierInternal(_identifier);
        return writable;
    }

    public override void Validate()
    {
        ValidateParentLink();

        string fileRelativePath = 实体_完整相对路径标识;

        if (!GuidText.Validate(Guid)) throw new InvalidOperationException($"GUID 格式无效: {fileRelativePath}");
    }
}

/// <summary>
/// 可写资源实体。用完即丢弃。
/// </summary>
public sealed record WritableResourceEntity : ResourceEntityBase, IWritableEntity
{
    public override ProjectEntityKind Kind => ProjectEntityKind.Resource;

    public new GuidJsonRecord? GuidJson
    {
        get => _guidJson;
        set => _guidJson = value;
    }

    public void Rename(string newName)
    {
        string oldExt = RelativePathUtils.GetExtension(实体_无相对路径标识符);
        string newFileName = newName + oldExt;
        SetIdentifierInternal(newFileName);
    }

    public override void Validate()
    {
        // 同只读版本的验证
    }
}
