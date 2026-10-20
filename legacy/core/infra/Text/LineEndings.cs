namespace Toolkit.Core.Infra;

/// <summary>
/// 统一换行规范。
/// 先将 CRLF 统一为 LF，再移除残余 CR。
/// </summary>
public static class TextLineEndings
{
    public static string ToLf(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", string.Empty);
    }
}
