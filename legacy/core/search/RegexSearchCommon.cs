using System.Text.RegularExpressions;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Search;

internal static class RegexSearchCommon
{
    /// <summary>
    /// Snippet 上下文长度（单侧字符数）
    /// </summary>
    public const int SnippetContextLength = 20;

    /// <summary>
    /// 统一的失败响应构造
    /// </summary>
    public static (TData?, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>) Fail<TData>(string message)
        where TData : class
    {
        return (null, new[] { new ToolError(null, null, null, message) }, Array.Empty<ToolError>());
    }

    public static Regex CreateRegex(string pattern, bool caseSensitive)
    {
        return TextSearch.Create(pattern, caseSensitive);
    }

    /// <summary>
    /// 未指定或空 GUID 列表时返回空集；<see cref="IsGuidAllowed"/> 将空集视为「不按 GUID 过滤」。
    /// </summary>
    public static HashSet<string> CreateGuidSet(string[]? guids)
    {
        return guids is { Length: > 0 }
            ? new HashSet<string>(guids, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    public static bool IsGuidAllowed(string? guid, HashSet<string> guidSet)
    {
        if (guidSet.Count == 0)
        {
            return true;
        }

        return GuidText.Validate(guid) && guidSet.Contains(guid);
    }

    /// <summary>
    /// 未指定或空路径列表时返回 <see cref="Array.Empty{T}"/>；<see cref="MatchesPathFilters"/> 将空数组视为「不按路径前缀过滤」。
    /// </summary>
    public static string[] NormalizePathFilters(string[]? paths)
    {
        if (paths is not { Length: > 0 })
        {
            return Array.Empty<string>();
        }

        return paths.Select(WorkspacePath.RequireRelativePath).ToArray();
    }

    public static bool MatchesPathFilters(string relativePath, string[] pathFilters)
    {
        if (pathFilters.Length == 0)
        {
            return true;
        }

        return pathFilters.Any(filter => relativePath.StartsWith(filter, StringComparison.Ordinal));
    }

    public static IEnumerable<TEntity> FilterByPathPrefix<TEntity>(
        IEnumerable<TEntity> entities,
        string[] pathFilters,
        Func<TEntity, string> relativePathSelector)
    {
        if (pathFilters.Length == 0)
        {
            return entities;
        }

        return entities.Where(entity => MatchesPathFilters(relativePathSelector(entity), pathFilters));
    }

    public static IEnumerable<TMatch> SearchLines<TMatch>(
        string content,
        Regex regex,
        int limitPerItem,
        Func<string, int, int, string, TMatch> createMatch)
    {
        string[] lines = content.Split('\n');
        int matchCount = 0;

        for (int lineIndex = 0; lineIndex < lines.Length && matchCount < limitPerItem; lineIndex++)
        {
            string line = lines[lineIndex];
            Match match = regex.Match(line);

            while (match.Success && matchCount < limitPerItem)
            {
                string snippet = TextSearch.ExtractSnippet(line, match.Index, match.Length, SnippetContextLength);
                yield return createMatch(line, lineIndex + 1, match.Index + 1, snippet);

                matchCount++;
                match = match.NextMatch();
            }
        }
    }

    public static void AddUntilLimit<T>(List<T> matches, IEnumerable<T> toAdd, int maxMatches)
    {
        foreach (T m in toAdd)
        {
            if (matches.Count >= maxMatches)
            {
                break;
            }

            matches.Add(m);
        }
    }
}
