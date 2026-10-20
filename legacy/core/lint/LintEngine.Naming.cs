namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    private static readonly string[] BescriptKeywords =
    {
        "if",
        "while",
        "delay",
        "async",
        "break",
        "continue",
        "fun",
        "varf",
        "act"
    };

    /// <summary>
    /// 分析标识符是否使用了关键字。
    /// 返回: 关键字诊断列表（如果有）
    /// </summary>
    private static IEnumerable<LintDiagnostic> AnalyzeIdentifier(
        string name,
        string sourceRelativePath)
    {
        foreach (string keyword in BescriptKeywords)
        {
            if (name.Equals(keyword, StringComparison.Ordinal))
            {
                yield return new LintDiagnostic(
                    LintRuleIds.NamingReservedKeyword,
                    LintSeverity.Error,
                    $"标识符使用了脚本关键字: {name}",
                    new LintLocation(sourceRelativePath, 1));
                yield break;
            }
        }

        if (name.Contains('\''))
        {
            yield return new LintDiagnostic(
                LintRuleIds.NamingApostropheDiscouraged,
                LintSeverity.Info,
                $"标识符包含分组符号 '，建议使用 _ 替代。分组效果可通过重排序近似实现。",
                new LintLocation(sourceRelativePath, 1));
        }
    }
}
