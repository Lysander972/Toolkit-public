using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

// =============================================================================
// 文件模型分层 / FILE MODEL LAYERING
// =============================================================================
//
// 层1: 基础设施 / 权威输入层
// - SystemWrapper: 包裹直接的 IO
// - IO: 工作区层面的抽象
// - Cache: 统一产出 FileTreeSnapshot / FileTreeNode
// - 数据权威来源只能是磁盘经缓存层产出的完整快照；上层禁止把局部推断写回快照对象
//
// 层2: 文件树流水线 / 中间层
// - TreeBinding: 只绑定和分析缓存层快照，不制造文件事实
// - GuidGuard: 只根据同目录同名伴生关系维护磁盘 .guid 文件，不修补 FileTreeNode
// - EntityBuildCoordinator/Builder: 只消费当前快照构建实体，不反向修改快照或缓存
// - 中间层允许产出影响分析、错误、写入请求；禁止越级篡改层1快照，禁止直接注册或改写层3模型
//
// 层3: 实体模型 / 投射结果层
// - 只读实体主干: 由层2基于快照一次性投射得到，实现 IReadOnlyEntity
// - 可写分支: 临时副本，用完即丢弃，不实现 IReadOnlyEntity
// - 模型缓存只能由实体树重建结果刷新，不能用中间层局部状态补丁拼接
//
// 单向数据流:
// 磁盘 → Cache/FileTreeSnapshot → TreeBinding/GuidGuard/Builders → ProjectEntity/ProjectModel。
// 任何反向写入必须先落到磁盘，再由缓存层统一刷新生成新快照，随后重建模型。
// 禁止为“同步”手工修补 FileTreeNode、跳过缓存刷新、从 GuidGuard 或 Builder 直接改 ProjectModel。
//
// 【禁止】
// - 禁止把可写实体注册到模型或绑定到文件树
// - 禁止在可写实体上调用 Load（可写实体根本没有 Load 方法）
// - 禁止使用 ToReadOnly()（写完后应重新加载只读实体）
// - 禁止 hack 式越级篡改上游快照或下游模型来伪造流水线同步
//
// =============================================================================

/// <summary>
/// 工程实体分类。
/// </summary>
public enum ProjectEntityKind
{
    Root,
    CodeRepoScript,
    Canvas,
    Resource,
    Map,
    Mech,
    Directory,
    Spine,
    Device,
    ScriptGroup,
    InlineScript,
    // 注意：Settings 已合并到 Root，不再作为独立实体类型

    // 特殊目录实体（只能直属于工程根）
    TempRoot,           // temp 目录
    ModDirectory,       // MechEditorCode/MechEditorToolCode 目录

    // Toolkit 内部实体（temp/toolkit/ 下的内容）
    ToolkitRoot,        // temp/toolkit 目录
    ToolkitInternalDirectory, // toolkit 内部目录
    ToolkitFile,        // toolkit 内部文件（无guid预期）

    // Temp 内部其他文件（无guid预期）
    OtherTempFile
}

/// <summary>
/// 有子实体的接口（协变）。
/// 提供只读的子实体访问，支持递归遍历。
/// </summary>
/// <typeparam name="TChild">子实体类型</typeparam>
/// <remarks>
/// out 协变修饰符使 IHasChildren&lt;子类型&gt; 可隐式转换为 IHasChildren&lt;ProjectEntity&gt;。
/// </remarks>
public interface IHasChildren<out TChild> where TChild : ProjectEntity
{
    /// <summary>子实体列表</summary>
    IReadOnlyList<TChild> Children { get; }
}

/// <summary>
/// 零件载体接口。
/// 仅由真正的载体实体（Map 和 Mech）实现，表示"我包含零件"。
/// Device 实体直接暴露 CarrierGuid/CarrierKind 属性，不实现此接口。
/// </summary>
public interface IHasGuid
{
    /// <summary>实体 GUID</summary>
    string Guid { get; }
}

public interface IDeviceCarrier
{
    /// <summary>载体 GUID（与 Guid 属性相同）</summary>
    string CarrierGuid { get; }

    /// <summary>载体类型标识："Map" 或 "Mech"</summary>
    string CarrierKind { get; }

    /// <summary>载体包含的零件列表（只读）</summary>
    IReadOnlyList<DeviceEntityBase> Devices { get; }
}

/// <summary>
/// 实体加载错误收集器。
/// 实体持久化层专用：统一收集实体加载过程中的错误。
/// </summary>
public sealed class EntityLoadErrorCollector
{
    private readonly List<ToolError> _errors = [];

    /// <summary>收集的所有错误</summary>
    public IReadOnlyList<ToolError> Errors => _errors;

    /// <summary>是否存在致命错误</summary>
    public bool HasFatal => _errors.Any(static e => e.Severity == ToolDiagnosticSeverity.Fatal);

    /// <summary>是否存在任何错误（不含警告）</summary>
    public bool HasErrors => _errors.Any(static e => e.Severity == ToolDiagnosticSeverity.Error);

    /// <summary>记录实体加载错误</summary>
    public void AddError(string path, string message, ToolDiagnosticSeverity severity = ToolDiagnosticSeverity.Error)
    {
        _errors.Add(new ToolError(path, null, null, $"[EntityLoad] {message}", severity));
    }

    /// <summary>记录文件不存在错误</summary>
    public void AddFileNotFound(string path, string fileType)
    {
        AddError(path, $"{fileType}文件不存在", ToolDiagnosticSeverity.Error);
    }

    /// <summary>记录 GUID 读取错误</summary>
    public void AddGuidError(string path, string reason)
    {
        AddError(path, $"GUID读取失败: {reason}", ToolDiagnosticSeverity.Error);
    }

    /// <summary>记录解析错误</summary>
    public void AddParseError(string path, string fileType, Exception ex)
    {
        AddError(path, $"{fileType}解析失败: {ex.Message}", ToolDiagnosticSeverity.Error);
    }

    /// <summary>合并另一个收集器的错误</summary>
    public void Merge(EntityLoadErrorCollector other)
    {
        _errors.AddRange(other._errors);
    }
}


public static class PathCombiner
{
    public static string CombineRelativePath(params string[] 路径片段)
    {
        if (路径片段.Length == 0)
            throw new ArgumentException("路径片段不能为空");

        string result = 路径片段[0];
        for (int i = 1; i < 路径片段.Length; i++)
        {
            string 本级 = 路径片段[i];
            if (本级.Equals(".")) // 保持本级不动,和根的写法语义不同
                continue;
            if (result.Equals(WorkspacePath.RootDirectory)) // 当前是根
                result = 本级;
            else
                result = result + "/" + 本级;
        }
        return result;
    }

}

/// <summary>
/// 工程实体抽象基类。
/// 持久化定位：封装游戏专用文件格式的序列化/反序列化逻辑。
///
/// 【可写副本语义】
/// ToWritable 必须执行深克隆：
/// - 子实体必须克隆为可写实体
/// - 子实体 Parent 必须重绑到新的可写父级
/// - 绝对禁止保留只读子实体引用
///
/// 详见 docs/Core_Entity_Model_Spec.md 第 7 章。
/// </summary>
public abstract record ProjectEntity
{
    // ============================================================================
    // I 版本号系统
    // ============================================================================
    // 版本号是 base64 字符串，长度按层级递增。
    // - 版本递增规则：改最后一节，取下一个值
    // - 子节点编码规则：复制父级版本号 + 追加一节起始字符
    //
    // 用途：防止过时的可写副本导致的错误回滚，确保并发安全。
    // 只有只读实体能递增版本号，可写实体的版本号是固定的。
    // ============================================================================

    /// <summary>Base64 字符集，用于版本号编码</summary>
    private const string VersionCharset = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    /// <summary>版本号起始字符</summary>
    private const char VersionStartChar = 'A';

    /// <summary>实体版本号</summary>
    public string Version { get; internal set; } = VersionStartChar.ToString();

    /// <summary>
    /// 递增版本号：改最后一节，取下一个值。
    /// 只读实体专用，可写实体不调用。
    /// </summary>
    internal void IncrementVersion()
    {
        char lastChar = Version[^1];
        int lastIndex = VersionCharset.IndexOf(lastChar);
        if (lastIndex < 0 || lastIndex >= VersionCharset.Length - 1)
        {
            // 到最大值则回到起始位置
            Version = Version[..^1] + VersionStartChar;
        }
        else
        {
            Version = Version[..^1] + VersionCharset[lastIndex + 1];
        }
    }

    /// <summary>
    /// 生成子实体版本号：复制父级版本号 + 追加起始字符。
    /// </summary>
    internal string GenerateChildVersion() => Version + VersionStartChar;

    // ============================================================================
    // II 路径核心属性
    // ============================================================================

    /// <summary>
    /// 实体标识符（无相对路径部分）。
    /// 标识符可以是目录名或文件名，用于唯一标识实体在其父级中的位置。
    /// 构造时一次性锁死，不可修改。
    /// </summary>
    public abstract string 实体_无相对路径标识符 { get; }

    // ============================================================================
    // III 路径计算属性
    // ============================================================================
    // 【计算规则】
    // 根节点: "."
    // 其他节点: PathCombiner.CombineRelativePath(Parent.本级计算起点, Parent.实体_无相对路径标识符)
    //
    // 【派生属性规范】
    // 命名格式: 实体组件_XXX_完整相对路径
    // 计算来源: 只能基于 本级计算起点 计算，禁止依赖其他中间层
    // ============================================================================

    /// <summary>
    /// 本级计算起点。
    /// 根节点返回 "."（默认实现），子实体通过 ChildProjectEntity 覆盖。
    /// </summary>
    internal protected virtual string 内部_禁止滥用_本级计算起点_完整相对路径_并非文件路径 => WorkspacePath.RootDirectory;

    /// <summary>实体完整相对路径 = 本级计算起点 + 标识符</summary>
    public string 实体_完整相对路径标识 => PathCombiner.CombineRelativePath(内部_禁止滥用_本级计算起点_完整相对路径_并非文件路径, 实体_无相对路径标识符);

    // ============================================================================
    // III-B 实体覆盖范围
    // ============================================================================

    /// <summary>收集自身直接覆盖的路径→实体映射（区分文件和目录）</summary>
    public abstract EntityCoverage GetSelfCoverage();

    /// <summary>收集递归覆盖的路径→实体映射（用于文件监听、覆盖度）</summary>
    public EntityCoverage GetAllCoverage()
    {
        EntityCoverage result = new();
        CollectAllCoverageRecursive(this, result);
        return result;
    }

    private static void CollectAllCoverageRecursive(ProjectEntity entity, EntityCoverage result)
    {
        EntityCoverage self = entity.GetSelfCoverage();
        foreach (KeyValuePair<string, ProjectEntity> kv in self.Files)
            result.Files[kv.Key] = kv.Value;
        foreach (KeyValuePair<string, ProjectEntity> kv in self.Directories)
            result.Directories[kv.Key] = kv.Value;
        // 递归遍历子实体（协变接口，无 switch）
        // 使用快照避免并发枚举时 List<T>.Enumerator 的 version 检查失败
        if (entity is IHasChildren<ProjectEntity> hasChildren)
        {
            ProjectEntity[] snapshot = hasChildren.Children.ToArray();
            foreach (ProjectEntity child in snapshot)
                CollectAllCoverageRecursive(child, result);
        }
    }

    /// <summary>
    /// 声明自身文件内容（持久化场景）。
    /// </summary>
    /// <remarks>
    /// <para><b>【强制规则】必须与 GetSelfCoverage() 完全一致</b></para>
    /// <list type="bullet">
    ///   <item>GetSelfCoverage.Directories 中的每个目录 → 必须调用 buffer.DeclareDirectory</item>
    ///   <item>GetSelfCoverage.Files 中的每个文件 → 必须调用 buffer.DeclareFile</item>
    ///   <item><b>绝对禁止</b>声明自身覆盖范围之外的任何路径（包括父目录）</item>
    /// </list>
    /// <para>违反此规则将破坏持久化系统的完整性。</para>
    /// </remarks>
    internal abstract void DeclareFileContents(ExpectedTreeBuffer buffer);

    /// <summary>收集递归文件具体内容（持久化场景）</summary>
    internal void DeclareAllFileContents(ExpectedTreeBuffer buffer)
    {
        Stack<ProjectEntity> pending = new();
        pending.Push(this);
        while (pending.Count > 0)
        {
            ProjectEntity entity = pending.Pop();
            entity.DeclareFileContents(buffer);
            if (entity is IHasChildren<ProjectEntity> hasChildren)
                foreach (ProjectEntity child in hasChildren.Children.Reverse())
                    pending.Push(child);
        }
    }

    /// <summary>获取绑定路径（目录+文件）</summary>
    public virtual IEnumerable<string> GetBindingPaths() => GetSelfCoverage().AllPaths;

    // ============================================================================
    // IV 非持久化内容
    // ============================================================================

    /// <summary>实体类型（运行时确定）</summary>
    public abstract ProjectEntityKind Kind { get; }

    // ============================================================================
    // V 持久化内容
    // ============================================================================

    // Guid 已从基类移除。独立实体通过实现 IHasGuid 提供非空 Guid。

    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    /// <summary>
    /// 校验实体有效性。
    /// 校验失败直接抛出异常（攻击性编程）。
    /// </summary>
    public abstract void Validate();

    /// <summary>
    /// 递归克隆子实体到可写父级。
    /// 供只读实体的 ToWritable() 调用。
    /// </summary>
    protected static List<ProjectEntity> CloneChildrenToWritable(IReadOnlyList<ProjectEntity> children, ProjectEntity writableParent)
    {
        List<ProjectEntity> writableChildren = new(children.Count);
        foreach (ProjectEntity child in children)
        {
            if (child is IReadOnlyEntity readOnlyChild &&
                readOnlyChild.ToWritableUntyped() is ChildProjectEntity writableChild)
            {
                writableChild.Parent = writableParent;
                writableChildren.Add(writableChild);
            }
        }
        return writableChildren;
    }

    // ============================================================================
    // IX 公共方法
    // ============================================================================

    /// <summary>
    /// 验证根实体的 Parent 为空（根实体专用）。
    /// </summary>
    protected static void ValidateRootHasNoParent()
    {
        // 根实体无 Parent 属性，无需检查
    }
}

// ============================================================================
// 子实体抽象基类
// ============================================================================

/// <summary>
/// 非根工程实体抽象基类。
/// 所有非根实体都继承此类，拥有非 nullable 的 Parent。
/// </summary>
public abstract record ChildProjectEntity : ProjectEntity
{
    /// <summary>父级实体链接</summary>
    public required ProjectEntity Parent { get; set; }

    /// <summary>
    /// 本级计算起点：基于 Parent 路径 + Parent 标识符。
    /// </summary>
    internal protected sealed override string 内部_禁止滥用_本级计算起点_完整相对路径_并非文件路径
        => PathCombiner.CombineRelativePath(Parent.内部_禁止滥用_本级计算起点_完整相对路径_并非文件路径, Parent.实体_无相对路径标识符);

    /// <summary>
    /// 验证父级链接：Parent 已由类型系统保证非空，此方法仅供一致性校验。
    /// </summary>
    protected void ValidateParentLink()
    {
        // Parent 非空由类型保证，无需运行时检查
    }
}

// ============================================================================
// 实体类型接口
// ============================================================================

/// <summary>
/// 只读实体接口（非泛型基接口）。
/// 用于统一只读生命周期和无 dynamic 的多态分发。
/// </summary>
public interface IReadOnlyEntity
{
    void Unload();
    void LoadSelf(EntityLoadErrorCollector errors);
    void LoadChildren(EntityLoadErrorCollector errors);
    bool Rebuild(EntityLoadErrorCollector errors);
    IWritableEntity ToWritableUntyped();
}

/// <summary>
/// 只读实体接口（泛型）。
/// 只有只读实体实现此接口，可写实体不实现。
/// </summary>
/// <typeparam name="TWritable">对应的可写实体类型</typeparam>
public interface IReadOnlyEntity<out TWritable> : IReadOnlyEntity where TWritable : IWritableEntity
{
    TWritable ToWritable();

    IWritableEntity IReadOnlyEntity.ToWritableUntyped() => ToWritable();
}

/// <summary>
/// 可写实体接口。
/// 只有可写实体实现此接口，只读实体不实现。
/// </summary>
public interface IWritableEntity
{
    void Rename(string newName);
}

// ============================================================================
// 层级实体抽象基类
// ============================================================================

/// <summary>
/// 层级实体抽象基类。
/// 提供子实体管理和构建器抽象。
/// 实现 IReadOnlyEntity 的 Unload/RebuildSelf/RebuildChildren/Rebuild。
/// </summary>
public abstract record HierarchyEntityBase : ChildProjectEntity, IHasChildren<ProjectEntity>
{
    // ============================================================================
    // IV 非持久化内容
    // ============================================================================

    /// <summary>子实体列表（内部字段）</summary>
    protected List<ProjectEntity> _children = [];

    /// <summary>子实体列表（只读）</summary>
    public IReadOnlyList<ProjectEntity> Children => _children;

    // IHasChildren<ProjectEntity> 实现（协变）
    IReadOnlyList<ProjectEntity> IHasChildren<ProjectEntity>.Children => _children;

    /// <summary>
    /// 获取该实体类型的构建器集合。
    /// 子类必须实现以提供正确的构建器。
    /// </summary>
    internal abstract IEnumerable<ISubEntityBuilder> GetEntityBuilders();

    // ============================================================================
    // IReadOnlyEntity 实现
    // ============================================================================

    /// <summary>
    /// 卸载实体：递归卸载子实体 → 解绑文件树 → 清空子实体列表。
    /// </summary>
    public void Unload()
    {
        // 递归卸载子实体
        foreach (ProjectEntity child in _children.ToList())
        {
            if (child is IReadOnlyEntity readOnlyChild)
                readOnlyChild.Unload();
        }

        // 清空子实体列表
        _children.Clear();
    }

    /// <summary>
    /// 加载子实体：创建子实体 → 调用子实体的 Rebuild。
    /// </summary>
    public virtual void LoadChildren(EntityLoadErrorCollector errors)
    {
        FileTreeNode? node = FileTreeBinding.Instance.FindNode(实体_完整相对路径标识);
        if (node == null)
        {
            errors.AddFileNotFound(实体_完整相对路径标识, Kind.ToString());
            return;
        }

        _children.Clear();

        EntityBuildContext buildContext = EntityBuildContext.CreateForChild(node, errors, 0, this);

        EntityBuildCoordinator coordinator = new();
        foreach (ISubEntityBuilder builder in GetEntityBuilders())
        {
            coordinator.Register(builder);
        }

        List<ProjectEntity> children = coordinator.BuildAll(buildContext);
        foreach (ProjectEntity child in children)
        {
            // 为子实体设置版本号（从父级生成）
            child.Version = GenerateChildVersion();
            _children.Add(child);
        }
    }

    /// <summary>
    /// 加载自身（不包含子实体）。子类实现具体加载逻辑。
    /// 可写实体不需要实现此方法。
    /// </summary>
    public virtual void LoadSelf(EntityLoadErrorCollector errors)
    {
        // 默认空实现，可写实体不需要重写
    }

    /// <summary>
    /// 完整重建：LoadSelf + LoadChildren。
    /// </summary>
    public virtual bool Rebuild(EntityLoadErrorCollector errors)
    {
        LoadSelf(errors);
        LoadChildren(errors);
        return true;
    }
}

// ============================================================================
// DirectoryEntityBase: 目录实体抽象基类
// ============================================================================
// 提供目录实体的公共特性：标识符 = 目录名
// ============================================================================

/// <summary>
/// 目录实体抽象基类。
/// 提供公共特性：标识符 = 目录名。
/// 只读实体：标识符构造时锁死。
/// 可写实体：通过 new 暴露 internal set 以支持重命名。
/// </summary>
public abstract record DirectoryEntityBase : HierarchyEntityBase
{
    // ============================================================================
    // I 数据存储
    // ============================================================================
    // _directoryName: 目录名（唯一数据源）
    // 只读实体：构造时锁死
    // 可写实体：可通过 new 属性修改
    // ============================================================================

    protected string _directoryName = string.Empty;

    // ============================================================================
    // 构造函数
    // ============================================================================

    protected DirectoryEntityBase() { }

    protected DirectoryEntityBase(string directoryName)
    {
        _directoryName = directoryName;
    }

    // ============================================================================
    // II 路径核心属性
    // ============================================================================

    /// <summary>标识符（目录名）</summary>
    public override string 实体_无相对路径标识符 => _directoryName;

    // ============================================================================
    // III-B 实体覆盖范围
    // ============================================================================

    public override EntityCoverage GetSelfCoverage()
        => new()
        { Directories = { [实体_完整相对路径标识] = this } };

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        // DeclareFileContents 必须和 GetSelfCoverage 声明的内容一样!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        // 用户已经不止一次被傻逼Agent强硬对抗硬往这里面塞扭曲规范的垃圾了!!!!!
        buffer.DeclareDirectory(实体_完整相对路径标识);
    }

    // ============================================================================
    // VIII 计算属性
    // ============================================================================

    /// <summary>目录名（映射到 _directoryName）</summary>
    public string DirectoryName => _directoryName;
}

// =============================================================================
// 实体覆盖范围 - 明确区分文件和目录
// =============================================================================

/// <summary>
/// 实体覆盖范围。
/// 明确区分文件路径和目录路径，避免混淆。
/// </summary>
public sealed class EntityCoverage
{
    public Dictionary<string, ProjectEntity> Files { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, ProjectEntity> Directories { get; } = new(StringComparer.Ordinal);

    public IEnumerable<string> AllPaths => Files.Keys.Concat(Directories.Keys);
}

// End of Entities.Core.cs
