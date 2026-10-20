using Toolkit.Core.Infra;

namespace Toolkit.Core.Canvas;

public static class CanvasHtmlRelativePath
{
    public static string BuildRelativePrefix(string baseDirRelativePath, string targetDirRelativePath)
    {
        List<string> baseSegments = SplitSegments(WorkspacePath.RequireRelativePath(baseDirRelativePath));
        List<string> targetSegments = SplitSegments(WorkspacePath.RequireRelativePath(targetDirRelativePath));

        int commonSegmentCount = 0;
        int max = Math.Min(baseSegments.Count, targetSegments.Count);
        while (commonSegmentCount < max &&
               string.Equals(baseSegments[commonSegmentCount], targetSegments[commonSegmentCount], StringComparison.Ordinal))
        {
            commonSegmentCount++;
        }

        List<string> parts = [];
        for (int i = commonSegmentCount; i < baseSegments.Count; i++)
        {
            parts.Add("..");
        }
        for (int i = commonSegmentCount; i < targetSegments.Count; i++)
        {
            parts.Add(targetSegments[i]);
        }

        if (parts.Count == 0)
        {
            return string.Empty;
        }

        return string.Join("/", parts);
    }

    private static List<string> SplitSegments(string relativePath)
    {
        if (relativePath == WorkspacePath.RootDirectory)
        {
            return [];
        }

        return new List<string>(relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }
}
