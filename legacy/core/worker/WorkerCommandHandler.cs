using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Worker;

/// <summary>
/// Worker 命令处理器 - 核心调度与公共基础设施
/// </summary>
public sealed partial class WorkerCommandHandler
{
    // 统一服务门面（由外部注入）
    private readonly ToolkitServices _services;

    // 插件设置缓存（由插件通过 updateSettings 命令推送）
    private Canvas.Render.CanvasPreviewSettings _canvasPreviewSettings = new(1080, 2160);


    /// <summary>
    /// 创建 WorkerCommandHandler 实例。
    /// </summary>
    /// <param name="services">由宿主层创建的服务门面，必须由调用方显式提供。</param>
    /// <exception cref="ArgumentNullException">services 为 null 时抛出。</exception>
    public WorkerCommandHandler(ToolkitServices services)
    {
        _services = services;
    }

    public WorkerResponse Handle(WorkerRequest? req)
    {
        try
        {
            if (req == null)
                return new WorkerResponse(Ok: false, Message: "缺少 cmd");

            switch (req.Cmd)
            {
                case "ping":
                    return new WorkerResponse(Ok: true, Message: "pong");
                // 文档查询
                case "getBuiltinMethodDoc":
                    return HandleGetBuiltinMethodDoc(req);
                case "getEventDocs":
                    return HandleGetEventDocs(req);
                case "findReferences":
                    return HandleFindReferences(req);
                // 工作区检查
                case "lintWorkspace":
                    return HandleLintWorkspace(req);
                case "resolveGuidAsset":
                    return HandleResolveGuidAsset(req);
                case "getAllGuidMappings":
                    return HandleGetAllGuidMappings(req);
                // Canvas 重构操作
                case "canvasExtract":
                    return HandleCanvasExtract(req);
                case "canvasImportByPath":
                    return HandleCanvasImportByPath(
                        RequirePath(req.ScriptRelativePath, "scriptPath"),
                        req.Line ?? throw new InvalidOperationException("缺少有效的 line 参数"),
                        RequirePath(req.CanvasRelativePath, "canvasPath"));
                case "canvasImportByGuid":
                    return HandleCanvasImportByGuid(
                        RequirePath(req.ScriptRelativePath, "scriptPath"),
                        req.Line ?? throw new InvalidOperationException("缺少有效的 line 参数"),
                        RequireParam(req.TargetGuid, "targetGuid"));
                case "canvasInline":
                    return HandleCanvasInline(req);
                // 脚本扫描
                case "scanActCalls":
                    return HandleScanActCalls(req);
                case "scanBuiltinMethodCalls":
                    return HandleScanBuiltinMethodCalls(req);
                case "scanCanvasArgs":
                    return HandleScanCanvasArgs(req);
                case "scanGlobalVarRefs":
                    return HandleScanGlobalVarRefs(req);
                // 重构操作
                case "identifierRename":
                    return HandleIdentifierRename(req);
                case "scriptRename":
                    return HandleScriptRename(req);
                case "resolveDeviceFromScript":
                    return HandleResolveDeviceFromScript(req);
                // 全局变量
                case "globalvarListForScript":
                    return HandleGlobalVarListForScript(req);
                case "globalvarBatch":
                    return HandleGlobalVarBatch(req);
                // 脚本仓库
                case "scriptToRepository":
                    return HandleScriptToRepository(req);
                case "scriptLinkRepository":
                    return HandleScriptLinkRepository(req);
                case "listScriptGroupEntries":
                    return HandleListScriptGroupEntries(req);
                // TS 视图
                case "codeRepoTsViewRead":
                    return HandleCodeRepoTsViewRead();
                // 实体
                case "entityInspect":
                    return HandleEntityInspect(req);
                // 设置同步
                case "updateSettings":
                    return HandleUpdateSettings(req);
                // Canvas 预览
                case "previewCanvasBinary":
                    return HandlePreviewCanvasBinary(req);
                case "previewCanvasInlineArg":
                    return HandlePreviewCanvasInlineArg(req);
                // 工程树
                case "getProjectTree":
                    return HandleGetProjectTree();
                default:
                    return new WorkerResponse(Ok: false, Message: $"未知命令: {req.Cmd}");
            }
        }
        catch (InvalidOperationException ex)
        {
            return new WorkerResponse(Ok: false, Message: ex.Message);
        }
    }

    #region 辅助方法：参数验证

#pragma warning disable TK006 // 参数校验方法，与其他 Require* 系列对称
    private static string RequireParam(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"缺少 {paramName} 参数");
        return value;
    }
#pragma warning restore TK006

#pragma warning disable TK006 // 参数校验方法，与 RequireParam 对称
    private string RequirePath(string? path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"缺少 {paramName} 参数");
        return WorkspacePath.RequireRelativePath(path);
    }
#pragma warning restore TK006

    #endregion

    #region 辅助方法：错误处理

    /// <summary>
    /// 将操作结果转换为 WorkerResponse。如果有错误或数据为空，返回失败响应。
    /// </summary>
    private static WorkerResponse? ToErrorResponse<T>(T? data, IReadOnlyList<ToolError>? errors, string defaultError)
    {
        if (errors is { Count: > 0 } || data == null)
        {
            string msg = defaultError;
            if (errors is { Count: > 0 })
            {
                msg = errors[0].Message;
            }
            return new WorkerResponse(Ok: false, Message: msg);
        }
        return null;
    }

    /// <summary>
    /// 统一的命令执行包装器，捕获所有异常并转换为错误响应
    /// </summary>
    private static WorkerResponse Execute(Func<WorkerResponse> action)
    {
        try
        {
            return action();
        }
        // Worker 顶层异常边界：防止未处理异常导致进程崩溃
        catch (Exception ex)
        {
            return new WorkerResponse(Ok: false, Message: StackTraceFormatter.FormatException(ex));
        }
    }

    #endregion

    #region 辅助方法：脚本归属验证

    private (string DeviceRelativePath, string? EventName) RequireDeviceOwnership(string scriptRelativePath)
    {
        ScriptOwnership ownership = _services.Logic.GetScriptOwnership(scriptRelativePath);
        if (!ownership.BelongsToDevice || ownership.DeviceRelativePath is not string devicePath)
            throw new InvalidOperationException("脚本不属于任何已索引的零件");
        return (devicePath, ownership.EventName);
    }

    #endregion

    #region 辅助方法：脚本文本解析

    private string ResolveScriptText(string? scriptText, string? scriptPath)
    {
        if (scriptText != null)
            return scriptText;

        if (string.IsNullOrWhiteSpace(scriptPath))
            throw new InvalidOperationException("缺少 scriptText 参数");

        string relativePath = WorkspacePath.RequireRelativePath(scriptPath);
        ProjectModel model = _services.Model;

        if (model.Find<InlineScriptEntity>(relativePath) is InlineScriptEntity inlineScript)
            return inlineScript.SourceText;

        if (model.Find<CodeRepoScriptEntity>(relativePath) is CodeRepoScriptEntity codeRepoScript)
            return codeRepoScript.SourceText;

        throw new InvalidOperationException($"未找到脚本实体: {relativePath}");
    }

    #endregion

    #region 设置同步

    /// <summary>
    /// 处理插件推送的设置更新
    /// </summary>
    private WorkerResponse HandleUpdateSettings(WorkerRequest req)
    {
        if (req.CanvasPreviewSettings != null)
        {
            CanvasPreviewSettingsInput settings = req.CanvasPreviewSettings;
            _canvasPreviewSettings = new Canvas.Render.CanvasPreviewSettings(
                settings.DefaultWidth,
                settings.DefaultHeight
            );
            // 同步到 ToolkitServices，供视图生成器使用
            _services.CanvasPreviewSettings = _canvasPreviewSettings;
        }
        return new WorkerResponse(Ok: true);
    }

    #endregion
}
