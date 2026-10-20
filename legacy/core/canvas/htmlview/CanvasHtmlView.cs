using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Canvas;

/// <summary>
/// Canvas HTML 视图读取结果
/// </summary>
public sealed record CanvasHtmlViewReadData(
    /// <summary>canvas 文件路径</summary>
    string RelativePath,
    /// <summary>HTML 视图文本</summary>
    string Html
);

/// <summary>
/// Canvas HTML 视图写入结果
/// </summary>
public sealed record CanvasHtmlViewWriteData(
    /// <summary>canvas 文件路径</summary>
    string RelativePath,
    /// <summary>是否成功写入</summary>
    bool Applied,
    /// <summary>受影响的文件路径列表</summary>
    IReadOnlyList<string> AffectedPaths,
    /// <summary>新建分支分配的 GUID（修改分支为 null），供调用方回传给 Agent 减少读操作</summary>
    string? CreatedGuid = null
);

/// <summary>
/// Canvas HTML 视图读写 - 用于 Canvas 文件预览
///
/// 使用规范：
/// ============
/// 用于 Canvas 文件视图读取与写回（供 MCP 视图与插件预览复用）
///
/// HTML 视图文件路径：temp/toolkit/views/{canvasRelativePathWithoutExt}.html
///
/// 与内联 Canvas 预览的区别：
/// - Canvas 文件预览：使用 HTML 视图文件，图片用相对路径引用
/// - 内联 Canvas 预览：动态生成 HTML，允许 base64 内联图片
/// </summary>
public sealed class CanvasHtmlView
{
    private static readonly Log CanvasLog = Log.For(LogModule.Canvas);
    private readonly ProjectModel _model;

    /// <summary>
    /// Canvas 视图根目录相对路径：temp/toolkit/views
    /// </summary>
    public static readonly string CanvasViewsRootPath = ScriptPathHelper.ViewsPath;

    public CanvasHtmlView(ProjectModel model)
    {
        _model = model;
    }

    public (CanvasHtmlViewReadData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Read(
        string path)
    {
        string relativePath = WorkspacePath.RequireRelativePath(path);
        if (_model.Find<CanvasEntity>(relativePath) is not CanvasEntity canvasEntity)
            return (null, new[] { new ToolError(null, null, null, $"未找到 Canvas 实体: {relativePath}") }, Array.Empty<ToolError>());
        if (canvasEntity.Data.Length == 0)
            return (null, new[] { new ToolError(null, null, null, $"Canvas 数据为空或无法加载: {relativePath}") }, Array.Empty<ToolError>());

        string viewDirectoryRelativePath = GetCanvasViewDirectoryRelativePath(relativePath);
        try
        {
            string supportPrefix = CanvasHtmlRelativePath.BuildRelativePrefix(viewDirectoryRelativePath, ScriptPathHelper.CanvasRenderRootPath);
            string html = CanvasConverter.BinaryToHtml(canvasEntity.Data, supportPrefix, relativePath);
            return (new CanvasHtmlViewReadData(RelativePath: relativePath, Html: html), Array.Empty<ToolError>(), Array.Empty<ToolError>());
        }
        catch (Exception ex)
        {
            CanvasLog.Exception(ex, "CanvasHtmlView.Read.ToHtml");
            return (null, new[] { new ToolError(null, null, null, $"Canvas 转 HTML 视图失败: {ex.Message}") }, Array.Empty<ToolError>());
        }
    }

    /// <summary>
    /// 校验 + 转换（HTML→Node→二进制），不写盘。
    /// 用于 patch 路径：补丁链先做 diff 校验，再在这里做纯转换。
    /// </summary>
    public (bool Success, byte[] CanvasBytes, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Validate(
        string path,
        string html)
    {
        WorkspacePath.RequireRelativePath(path);
        return ValidateCore(html);
    }

    /// <summary>
    /// 纯持久化。校验必须已在调用前完成。
    /// </summary>
    public (bool Success, CanvasHtmlViewWriteData Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) WritePersist(
        string canvasRelativePath,
        byte[] canvasBytes)
    {
        string relativePath = WorkspacePath.RequireRelativePath(canvasRelativePath);
        return PersistCore(relativePath, canvasBytes, _model);
    }

    public (bool Success, CanvasHtmlViewWriteData Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Write(
        string path,
        string html)
    {
        string relativePath = WorkspacePath.RequireRelativePath(path);
        (bool validateSuccess, byte[] canvasBytes, IReadOnlyList<ToolError> validateErrors, IReadOnlyList<ToolError> validateWarnings) = ValidateCore(html);
        if (!validateSuccess)
            return (false, new CanvasHtmlViewWriteData(relativePath, Applied: false, AffectedPaths: Array.Empty<string>()), validateErrors, validateWarnings);

        return PersistCore(relativePath, canvasBytes, _model);
    }

    private static (bool Success, byte[] CanvasBytes, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) ValidateCore(
        string html)
    {
        try
        {
            (CanvasNode rootNode, string canvasName) = CanvasConverter.FromHtml(html);
            byte[] canvasBytes = CanvasConverter.ToBinary(rootNode, canvasName);
            return (true, canvasBytes, Array.Empty<ToolError>(), Array.Empty<ToolError>());
        }
        catch (Exception ex)
        {
            CanvasLog.Exception(ex, "CanvasHtmlView.ValidateCore");
            return (false, Array.Empty<byte>(),
                new[] { new ToolError(null, null, null, $"HTML 视图转 Canvas 失败: {ex.Message}") },
                Array.Empty<ToolError>());
        }
    }

    private static (bool Success, CanvasHtmlViewWriteData Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) PersistCore(
        string relativePath,
        byte[] canvasBytes,
        ProjectModel model)
    {
        List<string> affectedPaths = [];
        string? createdGuid = null;

        try
        {
            if (model.Find<CanvasEntity>(relativePath) is CanvasEntity existingCanvas)
            {
                IReadOnlyList<string> paths = EntityWriteScope.ApplyContentChange<CanvasEntity, WritableCanvasEntity>(
                    model,
                    existingCanvas,
                    canvas => canvas.ToWritable(),
                    writable => writable.Data = canvasBytes);
                affectedPaths.AddRange(paths);
            }
            else
            {
                string parentDir = RelativePathUtils.GetParentDirectory(relativePath);
                string fileName = RelativePathUtils.GetLastSegment(relativePath);
                if (model.Find<DirectoryEntity>(parentDir) is not DirectoryEntity parentEntity)
                    throw new InvalidOperationException($"父目录不存在: {parentDir}");

                // 新建 canvas 必须分配 Guid，否则 GuidText.Validate 校验拒绝；与 CanvasRefactor.Extract 的新建范式一致。
                string guid = GuidText.NewGuid();
                IReadOnlyList<string> paths = EntityWriteScope.ApplyCreate<DirectoryEntity, WritableDirectoryEntity, WritableCanvasEntity>(
                    model,
                    parentEntity,
                    parent => parent.ToWritable(),
                    writableParent =>
                    {
                        WritableCanvasEntity writableCanvas = new()
                        {
                            Parent = writableParent,
                            Guid = guid,
                            Data = canvasBytes,
                            GuidJson = new GuidJsonRecord()
                        };
                        writableCanvas.GuidJson.SetGuid(guid);
                        writableCanvas.SetIdentifierInternal(fileName);
                        return writableCanvas;
                    },
                    (writableParent, writableCanvas) => writableParent.Children.Add(writableCanvas));
                affectedPaths.AddRange(paths);
                createdGuid = guid;
            }
        }
        catch (Exception ex) when (ex is not WorkspaceFileBlockedException and not WorkspaceStateChangedException)
        {
            CanvasLog.Exception(ex, "CanvasHtmlView.Write.SaveChanges");
            return (false,
                new CanvasHtmlViewWriteData(relativePath, Applied: false, AffectedPaths: Array.Empty<string>()),
                new[] { new ToolError(null, null, null, $"保存变更失败: {ex.Message}") },
                Array.Empty<ToolError>());
        }

        return (true,
            new CanvasHtmlViewWriteData(RelativePath: relativePath, Applied: affectedPaths.Count > 0, AffectedPaths: affectedPaths, CreatedGuid: createdGuid),
            Array.Empty<ToolError>(),
            Array.Empty<ToolError>());
    }

    private static string GetCanvasViewDirectoryRelativePath(string canvasRelativePath)
    {
        string normalized = WorkspacePath.RequireRelativePath(canvasRelativePath);
        if (!normalized.EndsWith(ProjectEntityLayout.CanvasExt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Canvas 路径必须以 {ProjectEntityLayout.CanvasExt} 结尾: {normalized}");
        }

        string viewRelativePath = normalized[..^ProjectEntityLayout.CanvasExt.Length];
        string viewDir = RelativePathUtils.GetParentDirectory(viewRelativePath);
        return RelativePathUtils.Combine(CanvasViewsRootPath, viewDir);
    }
}
