using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;

namespace Toolkit.Core.TsView;

/// <summary>
/// 统一的 TS 视图支持文件引用重写器，按视图文件所在深度补全前缀。
/// </summary>
public static class TsViewSupportReferenceRewriter
{
    private const string ReferencePathMarker = "/// <reference path=\"";

    public static string Rewrite(
        string tsView,
        string viewFileRelativePath,
        string viewFilesRootRelativePath,
        string referenceRootRelativePath)
    {
        string viewFilePath = WorkspacePath.RequireRelativePath(viewFileRelativePath);
        string viewRootPath = WorkspacePath.RequireRelativePath(viewFilesRootRelativePath);
        string referenceRootPath = WorkspacePath.RequireRelativePath(referenceRootRelativePath);

        string viewDir = RelativePathUtils.GetParentDirectory(viewFilePath);
        viewDir = WorkspacePath.RequireRelativePath(viewDir);
        WorkspacePath.RequirePathUnderRoot(viewRootPath, viewDir);

        string relativeTargetDir = CanvasHtmlRelativePath.BuildRelativePrefix(viewDir, referenceRootPath);
        string[] lines = tsView.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (!line.StartsWith(ReferencePathMarker, StringComparison.Ordinal))
            {
                continue;
            }

            int quoteStart = ReferencePathMarker.Length;
            int quoteEnd = line.IndexOf('"', quoteStart);
            if (quoteEnd <= quoteStart)
            {
                throw new InvalidOperationException($"TS 引用声明格式错误: {line}");
            }

            string oldPath = line.Substring(quoteStart, quoteEnd - quoteStart);
            string fileName = RelativePathUtils.GetLastSegment(oldPath);
            if (!IsSupportFileName(fileName))
            {
                continue;
            }

            string newPath = CombineCrossDirectoryPath(relativeTargetDir, fileName);
            lines[i] = line.Substring(0, quoteStart) + newPath + line.Substring(quoteEnd);
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// 【局部专用】拼接跨目录引用路径（可能包含 ".." 段）。
    ///
    /// 【禁止复用说明】
    /// 本方法用于拼接 TS 视图文件中的 /// <reference path="..." /> 引用路径。
    /// 这类路径是相对于视图文件的跨目录路径，可能包含 ".."（如 "../../bescript_reference/base.d.ts"）。
    ///
    /// 而 RelativePathUtils.Combine 与 WorkspacePath 体系要求路径必须符合工程根相对路径规范，
    /// 严禁包含 ".." 段（这是有意设计的强约束，防止工程根相对路径出现越界）。
    ///
    /// 因此，此处无法复用 RelativePathUtils.Combine，必须使用局部专用的简单拼接。
    /// </summary>
#pragma warning disable TK006 // 无法复用 RelativePathUtils.Combine（见上方注释说明），此处必须独立实现
    private static string CombineCrossDirectoryPath(string crossDirPrefix, string fileName)
#pragma warning restore TK006
    {
        return crossDirPrefix + "/" + fileName;
    }

#pragma warning disable TK006 // 支持文件名白名单，集中判定逻辑
    private static bool IsSupportFileName(string fileName)
#pragma warning restore TK006
    {
        return fileName.Equals(BescriptTsViewDeclarationLibrary.SdkFileName, StringComparison.Ordinal)
               || fileName.Equals(CustomLogicTsViewDeclarationLibrary.DtsFileName, StringComparison.Ordinal)
               || fileName.Equals("CodeRepo.ts", StringComparison.Ordinal);
    }
}
