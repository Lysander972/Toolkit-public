namespace Toolkit.Core.Bescript;

/// <summary>
/// 解析 BEScript 中的单行调用语句。
/// </summary>
public static class BescriptCallParser
{
    public static BescriptCallSignature Parse(string fullText)
    {
        return BescriptSyntaxParser.ParseCall(fullText);
    }
}
