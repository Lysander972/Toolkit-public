namespace Toolkit.Core.Persistence;

/// <summary>
/// 实体相关视图路径构建。
/// </summary>
public static class EntityViewPath
{
    /// <summary>
    /// 获取零件 TS 视图路径。
    /// </summary>
    /// <param name="deviceDirectoryPath">零件目录路径（device目录_完整相对路径）</param>
    public static string GetDeviceTsViewPath(string deviceDirectoryPath)
        => $"{ScriptPathHelper.ViewsPath}/{deviceDirectoryPath}.ts";

    /// <summary>
    /// 获取 Canvas HTML 视图路径。
    /// </summary>
    /// <param name="canvasFilePath">Canvas 文件路径（实体_完整相对路径标识）</param>
    internal static string GetCanvasHtmlViewPath(string canvasFilePath)
    {
        if (!canvasFilePath.EndsWith(ProjectEntityLayout.CanvasExt, StringComparison.Ordinal))
            throw new InvalidOperationException($"Canvas 路径必须以 {ProjectEntityLayout.CanvasExt} 结尾: {canvasFilePath}");
        string viewRelativePath = canvasFilePath[..^ProjectEntityLayout.CanvasExt.Length];
        return $"{ScriptPathHelper.ViewsPath}/{viewRelativePath}.html";
    }

    /// <summary>
    /// 获取代码仓库聚合视图路径（固定路径）。
    /// </summary>
    internal static string GetCodeRepoTsViewPath()
        => ScriptPathHelper.CodeRepoViewPath;

    /// <summary>
    /// 格式化视图位置为紧凑字符串。有行范围时为 "path:startLine-endLine"，无则仅 path。
    /// </summary>
    public static string FormatViewLocation(string path, int startLine, int endLine)
        => $"{path}:{startLine}-{endLine}";
}
