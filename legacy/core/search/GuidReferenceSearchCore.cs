using Toolkit.Core.Canvas;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Search;

/// <summary>
/// GUID 引用搜索（Logic 层实现）。
/// 遍历脚本与 Canvas 内容，查找包含指定 GUID 的文本片段。
/// </summary>
internal static class GuidReferenceSearchCore
{
    public static List<GuidReferenceResult> SearchGuidReferences(
        ProjectModel model,
        string targetGuid,
        int limit)
    {
        List<GuidReferenceResult> results = [];
        string target = targetGuid;

        void SearchInText(string? content, string relativePath, string kind, string? referrerGuid)
        {
            if (string.IsNullOrEmpty(content) || !content.Contains(target, StringComparison.Ordinal))
            {
                return;
            }

            string[] lines = content.Split('\n');
            for (int i = 0; i < lines.Length && results.Count < limit; i++)
            {
                string line = lines[i];
                int idx = line.IndexOf(target, StringComparison.Ordinal);
                if (idx < 0)
                {
                    continue;
                }

                string note = TextSearch.ExtractSnippet(line, idx, target.Length, RegexSearchCommon.SnippetContextLength);

                results.Add(new GuidReferenceResult(
                    Kind: kind,
                    ReferrerGuid: referrerGuid,
                    Path: relativePath,
                    Line: i + 1,
                    Note: note
                ));
            }
        }

        foreach (InlineScriptEntity script in model.List<InlineScriptEntity>())
        {
            if (results.Count >= limit) break;
            SearchInText(script.SourceText, script.实体_完整相对路径标识, "script", null);
        }

        foreach (CodeRepoScriptEntity script in model.ListCodeRepoScripts())
        {
            if (results.Count >= limit) break;
            SearchInText(script.SourceText, script.实体_完整相对路径标识, "asset", script.Guid);
        }

        foreach (CanvasEntity canvas in model.List<CanvasEntity>())
        {
            if (results.Count >= limit) break;

            if (canvas.Data.Length == 0)
            {
                continue;
            }

            // 直接从二进制转换为 HTML 视图进行搜索
            string html = CanvasConverter.BinaryToHtml(canvas.Data);
            SearchInText(html, canvas.实体_完整相对路径标识, "canvas", canvas.Guid);
        }

        return results;
    }
}
