using System.Text.RegularExpressions;

namespace Toolkit.Core.Search;

/// <summary>
/// GUID 引用搜索结果
/// </summary>
internal sealed record GuidReferenceResult(
    string Kind,
    string? ReferrerGuid,
    string Path,
    int Line,
    string Note
);

/// <summary>
/// 文本搜索工具。
/// 提供正则表达式创建和匹配片段提取。
/// </summary>
internal static class TextSearch
{
    public static string ExtractSnippet(string line, int matchIndex, int matchLength, int radius)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        if (matchIndex < 0 || matchLength < 0 || radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(matchIndex));
        }

        int endIndex = matchIndex + matchLength;
        if (matchIndex > line.Length || endIndex > line.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(matchIndex));
        }

        // 自动调整边界，避免越界
        int start = Math.Max(0, matchIndex - radius);
        int end = Math.Min(line.Length, endIndex + radius);

        return line.Substring(start, end - start);
    }

    /// <param name="pattern"></param>
    /// <param name="caseSensitive">为 <c>true</c> 时区分大小写；为 <c>false</c> 时使用忽略大小写匹配。</param>
    public static Regex Create(string pattern, bool caseSensitive)
    {
        RegexOptions options = RegexOptions.Compiled;
        if (!caseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return Create(pattern, options);
    }

    public static Regex Create(string pattern, RegexOptions options)
    {
#pragma warning disable TK025 // 用户明确请求的全文正则搜索条件
        return new Regex(pattern, options);
#pragma warning restore TK025
    }
}
