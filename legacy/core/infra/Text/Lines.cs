namespace Toolkit.Core.Infra;

/// <summary>
/// 文本行操作工具。
/// 【设计规范】统一使用 LF 换行符，结尾无换行符。
/// 提供行分割、行插入、行删除和行构建操作。
/// </summary>
public static class TextLines
{
    /// <summary>
    /// 将文本分割为行数组。
    /// 【设计规范】按统一换行规范转为 LF 后分割。
    /// </summary>
    public static string[] SplitLines(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        return TextLineEndings.ToLf(text).Split('\n');
    }

    public static string[] InsertLines(string[] lines, int index, IReadOnlyList<string> toInsert)
    {
        if (lines is null)
        {
            throw new ArgumentNullException(nameof(lines));
        }

        if (toInsert is null)
        {
            throw new ArgumentNullException(nameof(toInsert));
        }

        if (index < 0 || index > lines.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        string[] result = new string[lines.Length + toInsert.Count];
        Array.Copy(lines, 0, result, 0, index);
        for (int i = 0; i < toInsert.Count; i++)
        {
            result[index + i] = toInsert[i];
        }

        Array.Copy(lines, index, result, index + toInsert.Count, lines.Length - index);
        return result;
    }

    public static string[] RemoveLines(string[] lines, int startIndex, int endIndexInclusive)
    {
        if (lines is null)
        {
            throw new ArgumentNullException(nameof(lines));
        }

        if (startIndex < 0 || startIndex > lines.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        if (endIndexInclusive < -1 || endIndexInclusive >= lines.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(endIndexInclusive));
        }

        if (endIndexInclusive < startIndex)
        {
            throw new ArgumentOutOfRangeException(nameof(endIndexInclusive));
        }

        int removeCount = endIndexInclusive - startIndex + 1;
        string[] result = new string[lines.Length - removeCount];
        Array.Copy(lines, 0, result, 0, startIndex);
        Array.Copy(lines, endIndexInclusive + 1, result, startIndex, lines.Length - endIndexInclusive - 1);
        return result;
    }

    /// <summary>
    /// 将行数组构建为文本。
    /// 【设计规范】统一使用 LF 换行符，结尾无换行符。
    /// 示例: ["a", "b", "c"] → "a\nb\nc"
    /// </summary>
    public static string BuildLines(string[] lines)
    {
        if (lines is null)
        {
            throw new ArgumentNullException(nameof(lines));
        }

        // 统一使用 LF，结尾无换行
        return string.Join("\n", lines);
    }
}
