using Toolkit.Core.Infra;
using Toolkit.Core.Refactor;

namespace Toolkit.Core.Persistence;

// ============================================================================
// Script 实体定义
// ============================================================================
// 本文件包含与脚本相关的实体类型：
// - ScriptGroupEntityBase         : 脚本组实体抽象基类（公共逻辑）
// - ScriptGroupEntity             : 脚本组实体（只读）
// - WritableScriptGroupEntity     : 脚本组实体（可写）
// - ScriptEntryInfo               : 脚本条目信息
// - InlineScriptEntityBase        : 内联脚本实体抽象基类（公共逻辑）
// - InlineScriptEntity            : 内联脚本实体（只读）
// - WritableInlineScriptEntity    : 内联脚本实体（可写）
// ============================================================================

// ============================================================================
// ScriptGroupEntityBase: 脚本组实体抽象基类
// ============================================================================
// 包含 ScriptGroupEntity 和 WritableScriptGroupEntity 的所有公共逻辑：
// - 属性定义（EventName、ScriptEntries、Scripts、ListJsonText）
// - 标识符实现
// - Validate、GetDirectoryName 等公共方法
// ============================================================================

public abstract record ScriptGroupEntityBase : ChildProjectEntity, IHasChildren<InlineScriptEntityBase>
{
    // ============================================================================
    // I 数据存储
    // ============================================================================
    // _scriptGroupName: 脚本组目录名（如 "scripts" 或 "event_onStart"，唯一数据源）
    // ============================================================================

    private string _scriptGroupName = string.Empty;

    // ============================================================================
    // II 路径核心属性
    // ============================================================================

    /// <summary>标识符（脚本组目录名）</summary>
    public override string 实体_无相对路径标识符 => _scriptGroupName;

    // ============================================================================
    // III 路径计算属性
    // ============================================================================
    // 格式：实体组件_XXX_完整相对路径
    //
    // 【路径示例】ScriptGroup 在 "folder/sub.mech_/255/scripts"
    //   脚本组目录_完整相对路径 = "folder/sub.mech_/255/scripts"
    //   listJson_完整相对路径 = "folder/sub.mech_/255/scripts/list.json"
    // ============================================================================

    /// <summary>脚本组目录路径</summary>
    public string 脚本组目录_完整相对路径 => PathCombiner.CombineRelativePath(内部_禁止滥用_本级计算起点_完整相对路径_并非文件路径, 实体_无相对路径标识符);

    /// <summary>list.json 文件完整相对路径</summary>
    public string listJson_完整相对路径 => PathCombiner.CombineRelativePath(脚本组目录_完整相对路径, ProjectEntityLayout.ListJsonName);

    // ============================================================================
    // III-B 实体覆盖范围
    // ============================================================================

    public override EntityCoverage GetSelfCoverage()
    {
        EntityCoverage coverage = new()
        {
            Directories = { [脚本组目录_完整相对路径] = this }
        };
        if (_scriptEntries.Count > 0)
        {
            coverage.Files[listJson_完整相对路径] = this;
        }
        return coverage;
    }

    // ============================================================================
    // IV 非持久化内容（protected 字段 + 只读属性）
    // ============================================================================

    protected string? _eventName;
    protected List<ScriptEntryInfo> _scriptEntries = [];
    protected List<InlineScriptEntityBase> _scripts = [];

    /// <summary>事件名称（event_xxx 时有效，scripts/ 时为 null）</summary>
    public string? EventName => _eventName;

    /// <summary>脚本条目列表（从 list.json 解析）</summary>
    public IReadOnlyList<ScriptEntryInfo> ScriptEntries => _scriptEntries;

    /// <summary>内联脚本实体列表（从 Load 填充）</summary>
    public IReadOnlyList<InlineScriptEntityBase> Scripts => _scripts;

    // ============================================================================
    // V 持久化内容（protected 字段 + 只读属性）
    // ============================================================================

    protected string? _listJsonText;

    /// <summary>list.json 原始文本（用于行号解析）</summary>
    public string? ListJsonText => _listJsonText;

    // ============================================================================
    // 内部初始化方法（仅加载时使用）
    // ============================================================================

    internal void SetScriptGroupNameInternal(string value) => _scriptGroupName = value;
    internal void SetEventNameInternal(string? value) => _eventName = value;
    internal void SetScriptEntriesInternal(List<ScriptEntryInfo> value) => _scriptEntries = value;
    internal void SetScriptsInternal(List<InlineScriptEntityBase> value) => _scripts = value;
    internal void SetListJsonTextInternal(string? value) => _listJsonText = value;

    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    /// <summary>实体类型</summary>
    public abstract override ProjectEntityKind Kind { get; }

    // ============================================================================
    // VII-B 持久化声明
    // ============================================================================

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        // DeclareFileContents 必须和 GetSelfCoverage 声明的内容一样!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        // 用户已经不止一次被傻逼Agent强硬对抗硬往这里面塞扭曲规范的垃圾了!!!!!
        buffer.DeclareDirectory(脚本组目录_完整相对路径);
        if (ScriptEntries.Count > 0)
        {
            buffer.DeclareFile(listJson_完整相对路径, HjsonText.BuildListJsonText(ScriptEntries.Select(static s => new ListJsonItem(s.Name, s.Guid, s.IsCodeRepo, s.IsPrivate)).ToList()));
        }
    }

    // ============================================================================
    // VIII 公共方法
    // ============================================================================

    public override void Validate()
    {
        if (Parent is not DeviceEntityBase)
        {
            throw new InvalidOperationException($"脚本组的父级必须是零件实体: {脚本组目录_完整相对路径}, Parent type: {Parent.GetType().Name}");
        }
    }

    // ============================================================================
    // VI 接口实现
    // ============================================================================

    // IHasChildren<InlineScriptEntityBase> 实现（协变）
    IReadOnlyList<InlineScriptEntityBase> IHasChildren<InlineScriptEntityBase>.Children => Scripts;

    /// <summary>
    /// 创建可写副本（深克隆）。由只读实体实现。
    /// </summary>
    public abstract ScriptGroupEntityBase CreateWritableClone();
}

/// <summary>
/// 脚本组实体（只读版本）。
/// </summary>
public sealed record ScriptGroupEntity : ScriptGroupEntityBase, IReadOnlyEntity<WritableScriptGroupEntity>
{
    public override ProjectEntityKind Kind => ProjectEntityKind.ScriptGroup;

    /// <summary>
    /// 转换为可写副本（深克隆）。
    /// 递归创建所有子实体的可写副本，并设置 Parent 指向新的可写父级。
    /// </summary>
    public WritableScriptGroupEntity ToWritable()
    {
        WritableScriptGroupEntity writable = new()
        {
            Parent = Parent,  // 保持指向只读父级
            Version = Version,
            EventName = EventName,
            ScriptEntries = _scriptEntries.ToList(),  // ScriptEntryInfo 是值类型，直接复制
            Scripts = [],  // 稍后填充
            ListJsonText = ListJsonText
        };
        writable.SetScriptGroupNameInternal(实体_无相对路径标识符);

        // 深克隆：递归创建子实体的可写副本
        foreach (InlineScriptEntityBase script in _scripts)
        {
            InlineScriptEntityBase writableScript = script.CreateWritableClone();
            writableScript.Parent = writable;  // 指向新的可写父级
            writable.Scripts.Add(writableScript);
        }

        return writable;
    }

    /// <summary>
    /// 创建可写副本（深克隆）。实现基类抽象方法。
    /// </summary>
    public override ScriptGroupEntityBase CreateWritableClone() => ToWritable();

    // ============================================================================
    // IReadOnlyEntity 实现
    // ============================================================================

    /// <summary>
    /// 卸载实体：递归卸载子实体 → 解绑文件树 → 清空子实体列表。
    /// </summary>
    public void Unload()
    {
        // 递归卸载子实体
        foreach (InlineScriptEntityBase script in _scripts)
        {
            if (script is IReadOnlyEntity readOnlyScript)
                readOnlyScript.Unload();
        }
        // 清空子实体列表
        _scripts.Clear();
        _scriptEntries.Clear();
    }

    /// <summary>
    /// 加载自身：递增版本号 + 加载 list.json。
    /// </summary>
    public void LoadSelf(EntityLoadErrorCollector errors)
    {
        // 递增版本号
        IncrementVersion();
        List<ScriptEntryInfo> scriptEntries = [];

        // 从单例获取节点
        FileTreeNode? node = FileTreeBinding.Instance.FindNode(脚本组目录_完整相对路径);
        if (node == null)
        {
            errors.AddFileNotFound(脚本组目录_完整相对路径, "脚本组目录");
            return;
        }

        // 读取 list.json
        bool hasListJson = node.Files.Any(static f => f.Equals(ProjectEntityLayout.ListJsonName, StringComparison.Ordinal));
        if (!hasListJson)
        {
            // 原版允许脚本组缺少 list.json，视为空列表
            SetListJsonTextInternal(null);
            SetScriptEntriesInternal(scriptEntries);
            return;
        }

        List<ListJsonItem> items;
        try
        {
            string text = FileIO.ReadAllText(listJson_完整相对路径);
            SetListJsonTextInternal(text);
            items = HjsonText.ParseListItems(text);
        }
        catch (Exception ex)
        {
            errors.AddParseError(listJson_完整相对路径, "list.json", ex);
            return;
        }

        // 加载脚本条目映射（条目有效性由 HjsonText.ParseListItems 解析边界统一校验）
        foreach (ListJsonItem item in items)
        {
            scriptEntries.Add(new ScriptEntryInfo(item.Name, item.Guid, item.IsCodeRepo, item.IsPrivate));
        }

        SetScriptEntriesInternal(scriptEntries);
    }

    /// <summary>
    /// 加载子实体：加载内联脚本。
    /// </summary>
    public void LoadChildren(EntityLoadErrorCollector errors)
    {
        List<InlineScriptEntityBase> scripts = [];

        FileTreeNode? node = FileTreeBinding.Instance.FindNode(脚本组目录_完整相对路径);
        if (node == null) return;

        foreach (ScriptEntryInfo entry in ScriptEntries)
        {
            // CodeRepo 引用不创建 InlineScriptEntity
            if (entry.IsCodeRepo)
                continue;

            // 无 guid = 内联脚本，加载文件
            string codeFileName = entry.Name + ProjectEntityLayout.CodeExt;
            if (!node.Files.Contains(codeFileName))
            {
                string codeFileRelativePath = PathCombiner.CombineRelativePath(脚本组目录_完整相对路径, codeFileName);
                errors.AddError(codeFileRelativePath, $"脚本文件不存在: {entry.Name}", ToolDiagnosticSeverity.Error);
                continue;
            }

            InlineScriptEntity script = new()
            {
                Parent = this,
                Version = GenerateChildVersion()
            };
            script.SetScriptFileNameInternal(codeFileName);
            script.SetEventNameInternal(EventName);
            if (script.Rebuild(errors))
                scripts.Add(script);
        }

        SetScriptsInternal(scripts);
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
/// 可写脚本组实体。
/// 通过 ScriptGroupEntity.ToWritable() 创建。用完即丢弃。
/// </summary>
public sealed record WritableScriptGroupEntity : ScriptGroupEntityBase, IWritableEntity
{
    public override ProjectEntityKind Kind => ProjectEntityKind.ScriptGroup;

    // ============================================================================
    // 可写属性（用 new 重新声明，提供 setter）
    // ============================================================================

    public new string? EventName
    {
        get => _eventName;
        set => _eventName = value;
    }

    public new List<ScriptEntryInfo> ScriptEntries
    {
        get => _scriptEntries;
        set => _scriptEntries = value;
    }

    public new List<InlineScriptEntityBase> Scripts
    {
        get => _scripts;
        set => _scripts = value;
    }

    public new string? ListJsonText
    {
        get => _listJsonText;
        set => _listJsonText = value;
    }

    /// <summary>
    /// 可写实体不能再创建可写副本。抛出异常。
    /// </summary>
    public override ScriptGroupEntityBase CreateWritableClone() =>
        throw new InvalidOperationException("可写实体不能再创建可写副本");

    public void Rename(string newName) => throw new NotSupportedException("脚本组不支持重命名");
}

/// <summary>
/// 脚本条目信息（list.json 中单个条目的内存表示）。
///
/// 【职责边界】ScriptEntryInfo 只是 list.json 中的链接映射，不代表真正的脚本实体：
/// - 内联脚本：ScriptEntryInfo 对应一个 InlineScriptEntity 子实体
/// - CodeRepo：ScriptEntryInfo 只是一条链接记录，真正的 CodeRepoScriptEntity 由使用者通过 ProjectModel 独立管理
///
/// 字段映射：
/// - Name: 脚本名称（list.json 中的 name 字段或字符串值）
/// - Guid: CodeRepo 脚本的 GUID（list.json 中的 guid 字段），仅 IsCodeRepo=true 时有业务含义
/// - IsCodeRepo: true=CodeRepo引用(对象格式{"guid":..., "name":...})，false=内联脚本(字符串格式)
/// - IsPrivate: list.json 的 private 标记（缺省 false）
///
/// list.json 格式对照：
/// - 内联脚本: "脚本名" → new ScriptEntryInfo("脚本名", string.Empty, IsCodeRepo: false)
/// - CodeRepo:  {guid:"xxx", name:"脚本名", private:true} → new ScriptEntryInfo("脚本名", "xxx", IsCodeRepo: true, IsPrivate: true)
///
/// 【重要】使用 CodeRepo 脚本时，使用者必须：
/// 1. 通过 ProjectModel.TryGet/CodeRepoScriptEntity 获取/管理真正的代码仓库脚本实体
/// 2. 同时更新 ScriptGroupEntity 中的 ScriptEntryInfo 链接条目
/// </summary>
public sealed record ScriptEntryInfo(string Name, string Guid, bool IsCodeRepo, bool IsPrivate = false);

// ============================================================================
// InlineScriptEntityBase: 内联脚本实体抽象基类
// ============================================================================
// 包含 InlineScriptEntity 和 WritableInlineScriptEntity 的所有公共逻辑：
// - 属性定义（EventName、SourceText）
// - 标识符/ScriptName 实现
// - Validate、MoveFile 公共方法
// ============================================================================

/// <summary>
/// 内联脚本实体抽象基类。
///
/// 【层级结构 - 必须明确】
/// 内联脚本是叶子节点，位于 ScriptGroup 脚本组目录内：
///   main.map_/5/scripts/                 ← 脚本组目录
///     list.json                          ← 脚本列表
///     存档版本号检测.code                ← 内联脚本文件（.code）
///     工具是GM.code
///
/// 关键特征：
/// - 无 .guid 文件（内联实体，通过 list.json 引用）
/// - 文件名即脚本名（不含扩展名 .code）
///
/// 示例路径：
///   - main.map_/5/event_clientStart/GM判断.code
///   - mech/临时测试/测试1.mech_/0/scripts/初始化.code
///
/// 移动规则：仅允许重命名式移动（可在同一脚本组内更名，不改变父级目录）
/// </summary>
public abstract record InlineScriptEntityBase : ChildProjectEntity
{
    // ============================================================================
    // I 数据存储
    // ============================================================================
    // _scriptFileName: 脚本文件名（如 "初始化.code"，唯一数据源）
    // ============================================================================

    private string _scriptFileName = string.Empty;

    // ============================================================================
    // II 路径核心属性
    // ============================================================================

    /// <summary>标识符（脚本文件名）</summary>
    public override string 实体_无相对路径标识符 => _scriptFileName;

    // ============================================================================
    // III 路径计算属性
    // ============================================================================

    private string 脚本_完整相对路径 => PathCombiner.CombineRelativePath(内部_禁止滥用_本级计算起点_完整相对路径_并非文件路径, _scriptFileName);  // .../123/scripts/abc.code

    // ============================================================================
    // III-B 实体覆盖范围
    // ============================================================================

    public override EntityCoverage GetSelfCoverage()
        => new()
        { Files = { [实体_完整相对路径标识] = this } };

    // ============================================================================
    // IV 非持久化内容（protected 字段 + 只读属性）
    // ============================================================================

    protected string? _eventName;

    /// <summary>事件名称（event_xxx 时有效，scripts/ 时为 null）</summary>
    public string? EventName => _eventName;

    // ============================================================================
    // V 持久化内容（protected 字段 + 只读属性）
    // ============================================================================

    protected string _sourceText = string.Empty;

    /// <summary>脚本源码</summary>
    public string SourceText => _sourceText;

    // ============================================================================
    // 内部初始化方法（仅加载时使用）
    // ============================================================================

    internal void SetScriptFileNameInternal(string value) => _scriptFileName = value;
    internal void SetEventNameInternal(string? value) => _eventName = value;
    internal void SetSourceTextInternal(string value) => _sourceText = value;

    // ============================================================================
    // VII 抽象成员
    // ============================================================================

    public abstract override ProjectEntityKind Kind { get; }

    // ============================================================================
    // VII-B 持久化声明
    // ============================================================================

    internal override void DeclareFileContents(ExpectedTreeBuffer buffer)
    {
        // DeclareFileContents 必须和 GetSelfCoverage 声明的内容一样!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        // 用户已经不止一次被傻逼Agent强硬对抗硬往这里面塞扭曲规范的垃圾了!!!!!
        buffer.DeclareFile(实体_完整相对路径标识, SourceText);
    }

    // ============================================================================
    // VIII 计算属性
    // ============================================================================

    /// <summary>脚本名称（不含扩展名）</summary>
    public string ScriptName => RelativePathUtils.GetLastSegmentWithoutExtension(_scriptFileName);

    // ============================================================================
    // IX 公共方法
    // ============================================================================

    public override void Validate()
    {
        string fileRelativePath = 实体_完整相对路径标识;


        // 验证父级类型：内联脚本的 Parent 必须是脚本组实体
        if (Parent is not ScriptGroupEntityBase)
        {
            throw new InvalidOperationException($"内联脚本的父级必须是脚本组实体: {fileRelativePath}, Parent type: {Parent.GetType().Name}");
        }

        if (!fileRelativePath.EndsWith(ProjectEntityLayout.CodeExt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"内联脚本扩展名必须是 {ProjectEntityLayout.CodeExt}: {fileRelativePath}");
        }
    }

    /// <summary>
    /// 创建可写副本（深克隆）。由只读实体实现。
    /// </summary>
    public abstract InlineScriptEntityBase CreateWritableClone();
}

/// <summary>
/// 内联脚本实体（只读版本）。
/// </summary>
public sealed record InlineScriptEntity : InlineScriptEntityBase, IReadOnlyEntity<WritableInlineScriptEntity>, IRenameable
{
    public override ProjectEntityKind Kind => ProjectEntityKind.InlineScript;

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

        // 脚本文件必须存在
        if (!FileIO.FileExists(fileRelativePath))
        {
            errors.AddFileNotFound(fileRelativePath, "内联脚本");
            return;
        }

        try
        {
            SetSourceTextInternal(FileIO.ReadAllText(fileRelativePath));
        }
        catch (Exception ex)
        {
            errors.AddError(fileRelativePath, $"读取内联脚本失败: {ex.Message}", ToolDiagnosticSeverity.Error);
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

    /// <summary>
    /// 转换为可写副本。
    /// </summary>
    public WritableInlineScriptEntity ToWritable()
    {
        WritableInlineScriptEntity writable = new()
        {
            Parent = Parent,
            Version = Version,
            EventName = EventName,
            SourceText = SourceText
        };
        writable.SetScriptFileNameInternal(实体_无相对路径标识符);
        return writable;
    }

    /// <summary>
    /// 创建可写副本（深克隆）。实现基类抽象方法。
    /// </summary>
    public override InlineScriptEntityBase CreateWritableClone() => ToWritable();
}

/// <summary>
/// 可写内联脚本实体。
/// 通过 InlineScriptEntity.ToWritable() 创建。用完即丢弃。
/// </summary>
public sealed record WritableInlineScriptEntity : InlineScriptEntityBase, IWritableEntity
{
    public override ProjectEntityKind Kind => ProjectEntityKind.InlineScript;

    // ============================================================================
    // 可写属性（用 new 重新声明，提供 setter）
    // ============================================================================

    public new string? EventName
    {
        get => _eventName;
        set => _eventName = value;
    }

    public new string SourceText
    {
        get => _sourceText;
        set => _sourceText = value;
    }

    /// <summary>
    /// 重命名内联脚本（仅支持同父级重命名）。
    /// 内联脚本不能跨脚本组移动。
    /// </summary>
    public void Rename(string newName)
    {
        string newFileName = newName + ProjectEntityLayout.CodeExt;
        SetScriptFileNameInternal(newFileName);
    }

    /// <summary>
    /// 可写实体不能再创建可写副本。抛出异常。
    /// </summary>
    public override InlineScriptEntityBase CreateWritableClone() =>
        throw new InvalidOperationException("可写实体不能再创建可写副本");
}
