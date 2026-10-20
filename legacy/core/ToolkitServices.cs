using Toolkit.Core.Analysis;
using Toolkit.Core.Canvas;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;
using Toolkit.Core.Refactor;
using Toolkit.Core.Sdk;
using Toolkit.Core.Search;
using Toolkit.Core.TsView;

namespace Toolkit.Core;

/// <summary>
/// 工具包服务统一门面。
///
/// 分层架构：
/// - Persistence（第1层）：ProjectModel - 文件 ↔ 数据模型投射
/// - Logic（第2层）：ProjectLogicContext - 实体间高层次关联分析
///
/// 此门面由外部调用者显式组装两层，确保分层边界清晰。
/// </summary>
public sealed class ToolkitServices
{
    private static readonly object SingletonLock = new();
    private static ToolkitServices? _singleton;

    /// <summary>
    /// 进程内唯一的 ToolkitServices 实例（必须先 InitializeSingleton）。
    /// </summary>
    public static ToolkitServices Singleton
    {
        get
        {
            if (_singleton == null)
            {
                throw new InvalidOperationException("ToolkitServices 单例未初始化");
            }
            return _singleton;
        }
    }

    /// <summary>
    /// 初始化进程内唯一的 ToolkitServices。
    /// 浏览器实例由调用方创建和管理生命周期，与 ProjectModel 同级注入。
    /// </summary>
    public static ToolkitServices InitializeSingleton(string projectRootFullPath, BrowserRenderService browser)
    {
        lock (SingletonLock)
        {
            if (_singleton != null)
            {
                throw new InvalidOperationException("ToolkitServices 单例已初始化");
            }

            ProjectModel.SetProjectRootFullPath(projectRootFullPath);
            ProjectModel model = ProjectModel.Load();
            ProjectLogicContext logic = new ProjectLogicContext(model);
            _singleton = new ToolkitServices(model, logic, browser);
            return _singleton;
        }
    }

    internal static ToolkitServices InitializeSingletonForTesting(string projectRootFullPath, BrowserRenderService browser)
    {
        lock (SingletonLock)
        {
            if (_singleton != null)
            {
                throw new InvalidOperationException("ToolkitServices 单例已初始化");
            }

            ProjectModel.SetProjectRootFullPath(projectRootFullPath);
            InMemoryWorkspaceOverlay overlay = new();
            Persistence.FileSystemCache.Instance.AttachOverlay(overlay);
            RecordingPersistenceOperationApplier recorder = new(overlay);
            ProjectModel model = ProjectModel.LoadForTesting(recorder);
            ProjectLogicContext logic = new ProjectLogicContext(model);
            _singleton = new ToolkitServices(model, logic, browser);
            return _singleton;
        }
    }

    /// <summary>
    /// 释放单例（用于测试或进程退出）。
    /// 注意：不释放浏览器，浏览器生命周期归调用方管理。
    /// </summary>
    public static void ShutdownSingleton()
    {
        lock (SingletonLock)
        {
            _singleton?.Model.Dispose();
            _singleton = null;
        }
        Persistence.FileSystemCache.Shutdown();
    }

    // Logic 索引最近一次重建对应的模型内容版本（见构造器中的 ModelRefreshed 订阅）
    private long _lastLogicRebuildVersion;

    /// <summary>Persistence 层（第1层）：文件 ↔ 数据模型投射</summary>
    public ProjectModel Model { get; }

    /// <summary>Logic 层（第2层）：实体间高层次关联分析</summary>
    public ProjectLogicContext Logic { get; }

    /// <summary>Canvas 预览设置（由插件通过 updateSettings 命令推送更新）</summary>
    public Canvas.Render.CanvasPreviewSettings CanvasPreviewSettings { get; set; } = new(1080, 2160);

    /// <summary>浏览器渲染服务 - 多子进程负载均衡</summary>
    public BrowserRenderService Browser { get; }

    /// <summary>脚本分析服务 - AST 解析与管理</summary>
    public ScriptAnalysisService Analysis { get; }

    // 需要模型+逻辑层的工具
    public DeviceTsView DeviceTsView { get; }
    public ScriptRegexSearch ScriptRegexSearch { get; }
    public EntityInspectTool EntityInspect { get; }
    public ScriptRepository ScriptRepository { get; }
    public LintResultsTool LintResults { get; }
    public LintTsviewResultsTool LintTsviewResults { get; }

    // 无状态工具
    public SdkDocsQueryTool SdkDocsQuery { get; }
    public CanvasHtmlView CanvasHtmlView { get; }
    public CanvasRefactor CanvasRefactor { get; }
    public EntitySearchTool EntitySearch { get; }
    public IdentifierRename IdentifierRename { get; }
    public GlobalVarOperation GlobalVarOperation { get; }
    public ScriptRenameTool ScriptRename { get; }
    public ScriptDelete ScriptDelete { get; }
    public ScriptReorder ScriptReorder { get; }
    public BescriptScanner BescriptScanner { get; }
    public UiRegexSearch UiRegexSearch { get; }
    public CodeRepoTsView CodeRepoTsView { get; }
    public CanvasPreview CanvasPreview { get; }
    public FileRename FileRename { get; }
    public ResourceImport ResourceImport { get; }

    /// <summary>
    /// 创建 ToolkitServices 实例。
    /// </summary>
    /// <param name="model">Persistence 层（第1层），文件 ↔ 数据模型投射。</param>
    /// <param name="logic">Logic 层（第2层），实体间高层次关联分析。</param>
    /// <param name="browser">浏览器进程管理器，由外部创建并注入。</param>
    public ToolkitServices(ProjectModel model, ProjectLogicContext logic, BrowserRenderService browser)
    {
        Model = model;
        Logic = logic;
        Browser = browser;

        // 需要先初始化的工具
        Analysis = new ScriptAnalysisService(model);

        // Logic 层订阅 Model 刷新事件：Model 刷新后自动重建索引。
        // 内容版本未变（无影响刷新）时索引与解析缓存仍然有效，跳过冗余重建。
        Model.ModelRefreshed += (_, _) =>
        {
            if (Model.RefreshVersion == _lastLogicRebuildVersion)
            {
                return;
            }
            Logic.RebuildIndexes();
            // 模型刷新后清理脚本解析缓存，避免读取到旧内容
            Analysis.ClearCache();
            _lastLogicRebuildVersion = Model.RefreshVersion;
        };

        // 需要模型+逻辑层的工具
        DeviceTsView = new DeviceTsView(model, logic);
        ScriptRegexSearch = new ScriptRegexSearch(model);
        EntityInspect = new EntityInspectTool(logic);
        LintResults = new LintResultsTool(model);
        LintTsviewResults = new LintTsviewResultsTool(LintResults, DeviceTsView);
        CodeRepoTsView = new CodeRepoTsView(logic);

        // 需要模型的工具（续）
        CanvasPreview = new CanvasPreview();
        EntitySearch = new EntitySearchTool(logic, CodeRepoTsView, DeviceTsView);

        // 无状态工具
        SdkDocsQuery = new SdkDocsQueryTool();
        CanvasHtmlView = new CanvasHtmlView(model);
        IdentifierRename = new IdentifierRename(model);
        GlobalVarOperation = new GlobalVarOperation(model);
        ScriptRename = new ScriptRenameTool(model);
        ScriptDelete = new ScriptDelete(model, logic);
        ScriptReorder = new ScriptReorder(model);
        BescriptScanner = new BescriptScanner();
        UiRegexSearch = new UiRegexSearch(model);
        FileRename = new FileRename();
        ResourceImport = new ResourceImport(model);

        // 需要依赖其他工具的
        CanvasRefactor = new CanvasRefactor(model, CanvasPreview);
        ScriptRepository = new ScriptRepository(model, CanvasPreview);
    }
}
