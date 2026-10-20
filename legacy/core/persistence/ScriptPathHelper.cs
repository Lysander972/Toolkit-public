// 脚本路径工具 - 路径构建和解析方法
// 这些方法与脚本结构相关，不属于布局常量


using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

/// <summary>
/// 脚本路径工具类。
/// 提供脚本路径的构建和解析方法。
/// </summary>
public static class ScriptPathHelper
{
    private static readonly string ScriptsSegment = "/" + ProjectEntityLayout.ScriptsDirName + "/";
    private static readonly string EventSegmentPrefix = "/" + ProjectEntityLayout.EventDirPrefix;

    // =============================================================================
    // 路径构建方法
    // =============================================================================

    /// <summary>
    /// 构建代码仓库脚本路径。
    /// </summary>
    public static string BuildCodeRepositoryScriptPath(string dir, string name)
        => RelativePathUtils.Combine(dir, name + ProjectEntityLayout.CodeExt);

    /// <summary>
    /// 从 list.json 路径构建脚本目录路径。
    /// </summary>
    public static string BuildScriptDirectoryFromListPath(string listPath)
        => RelativePathUtils.GetParentDirectory(listPath);

    /// <summary>
    /// 构建 scripts/list.json 路径。
    /// </summary>
    public static string BuildScriptsListJsonPath(string deviceDir)
        => PathCombiner.CombineRelativePath(
            RelativePathUtils.Combine(deviceDir, ProjectEntityLayout.ScriptsDirName),
            ProjectEntityLayout.ListJsonName);

    /// <summary>
    /// 构建 event_xxx/list.json 路径。
    /// </summary>
    public static string BuildEventListJsonPath(string deviceDir, string eventName)
        => PathCombiner.CombineRelativePath(
            RelativePathUtils.Combine(deviceDir, ProjectEntityLayout.EventDirPrefix + eventName),
            ProjectEntityLayout.ListJsonName);

    // =============================================================================
    // 路径解析方法
    // =============================================================================

    /// <summary>
    /// 解析 list.json 路径，提取零件路径和事件名。
    /// 返回 null 表示无法解析。
    /// </summary>
    public static (string DeviceRelativePath, string? EventName)? ParseListJsonPath(string listRelativePath)
    {
        string validatedPath = WorkspacePath.RequireRelativePathOrEmpty(listRelativePath);
        if (!validatedPath.EndsWith("/" + ProjectEntityLayout.ListJsonName, StringComparison.Ordinal))
            return null;

        string dirPath = validatedPath[..^("/" + ProjectEntityLayout.ListJsonName).Length];
        string dirName = RelativePathUtils.GetLastSegment(dirPath);
        string parentPath = WorkspacePath.RequireRelativePathOrEmpty(RelativePathUtils.GetParentDirectory(dirPath));

        if (string.Equals(dirName, ProjectEntityLayout.ScriptsDirName, StringComparison.Ordinal))
            return (parentPath, null);

        if (dirName.StartsWith(ProjectEntityLayout.EventDirPrefix, StringComparison.Ordinal) && dirName.Length > ProjectEntityLayout.EventDirPrefix.Length)
            return (parentPath, dirName[ProjectEntityLayout.EventDirPrefix.Length..]);

        return null;
    }

    /// <summary>
    /// 从脚本源码路径提取零件路径。返回 null 表示无法提取。
    /// </summary>
    public static string? ExtractDevicePathFromSourcePath(string sourceRelativePath)
    {
        int scriptsMarkerIndex = sourceRelativePath.IndexOf(ScriptsSegment, StringComparison.Ordinal);
        if (scriptsMarkerIndex > 0)
            return sourceRelativePath[..scriptsMarkerIndex];

        int eventMarkerIndex = sourceRelativePath.IndexOf(EventSegmentPrefix, StringComparison.Ordinal);
        if (eventMarkerIndex > 0)
            return sourceRelativePath[..eventMarkerIndex];

        return null;
    }

    /// <summary>
    /// 从脚本源码路径提取事件名。返回 null 表示无法提取。
    /// </summary>
    public static string? ExtractEventNameFromSourcePath(string sourceRelativePath)
    {
        int eventMarkerIndex = sourceRelativePath.IndexOf(EventSegmentPrefix, StringComparison.Ordinal);
        if (eventMarkerIndex < 0)
            return null;

        int start = eventMarkerIndex + EventSegmentPrefix.Length;
        if (start <= 0 || start >= sourceRelativePath.Length)
            return null;

        int end = sourceRelativePath.IndexOf('/', start);
        if (end <= start)
            return null;

        return sourceRelativePath.Substring(start, end - start);
    }

    /// <summary>
    /// 从脚本源码路径获取脚本名（不含扩展名）。
    /// </summary>
    public static string GetScriptStemFromSourcePath(string sourceRelativePath)
    {
        string fileName = sourceRelativePath;
        int lastSlash = sourceRelativePath.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash + 1 < sourceRelativePath.Length)
            fileName = sourceRelativePath[(lastSlash + 1)..];

        if (fileName.EndsWith(ProjectEntityLayout.CodeGuidExt, StringComparison.Ordinal))
            return fileName[..^ProjectEntityLayout.CodeGuidExt.Length];

        if (fileName.EndsWith(ProjectEntityLayout.CodeExt, StringComparison.Ordinal))
            return fileName[..^ProjectEntityLayout.CodeExt.Length];

        throw new ArgumentException($"脚本文件必须以 '{ProjectEntityLayout.CodeExt}' 或 '{ProjectEntityLayout.CodeGuidExt}' 结尾: {sourceRelativePath}", nameof(sourceRelativePath));
    }

    /// <summary>
    /// 检查是否为编辑器命名空间段。
    /// </summary>
    public static bool IsEditorSegment(string part)
        => part.Equals(ProjectEntityLayout.EditorNamespace, StringComparison.Ordinal)
        || part.Equals(ProjectEntityLayout.EditorToolNamespace, StringComparison.Ordinal);

    // =============================================================================
    // Toolkit 视图路径（按层级组合）
    // =============================================================================

    /// <summary>
    /// temp/toolkit 路径
    /// </summary>
    public static string TempToolkitPath => RelativePathUtils.Combine(ProjectEntityLayout.TempDirName, ProjectEntityLayout.ToolkitDirName);

    /// <summary>
    /// temp/toolkit/ 前缀
    /// </summary>
    public static string TempToolkitPrefix => TempToolkitPath + "/";

    /// <summary>
    /// temp/toolkit/views 路径（TS/HTML 视图根）
    /// </summary>
    public static string ViewsPath => RelativePathUtils.Combine(TempToolkitPath, ProjectEntityLayout.ViewsDirName);

    /// <summary>
    /// temp/toolkit/canvas_render 路径（Canvas 渲染支持资源）
    /// </summary>
    public static string CanvasRenderRootPath => RelativePathUtils.Combine(TempToolkitPath, "canvas_render");

    /// <summary>
    /// temp/toolkit/bescript_reference 路径（TS 参考文件）
    /// </summary>
    public static string BescriptReferenceRootPath => RelativePathUtils.Combine(TempToolkitPath, "bescript_reference");

    /// <summary>
    /// temp/toolkit/tsconfig.json 路径
    /// </summary>
    public static string ToolkitTsConfigPath => RelativePathUtils.Combine(TempToolkitPath, "tsconfig.json");

    /// <summary>
    /// temp/toolkit/bescript_reference/CodeRepo.ts 路径
    /// </summary>
    public static string CodeRepoViewPath => RelativePathUtils.Combine(BescriptReferenceRootPath, "CodeRepo.ts");

    /// <summary>
    /// temp/toolkit/lsp 路径（LSP adapter 支持文件）
    /// </summary>
    public static string LspSupportPath => RelativePathUtils.Combine(TempToolkitPath, "lsp");
}
