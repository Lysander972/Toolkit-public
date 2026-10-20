using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Mcp;

/// <summary>
/// ApplyStringEdits 的 unicode 转义交换降级（第三级回退，模仿 Claude Code "swapping \uXXXX escapes and their characters"）。
/// 双向整体变体：反转义（\uXXXX→字符）与转义（非 ASCII→\uXXXX）；唯一命中才替换，多点视为失败。
/// </summary>
public sealed class ApplyStringEditsUnicodeSwapTests
{
    private static (string Patched, IReadOnlyList<ToolError> Errors) Run(
        string original, string oldString, string newString, bool replaceAll = false)
    {
        (string patched, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) =
            McpToolRegistry.ApplyStringEdits(original, new[] { new ViewEdit(oldString, newString, replaceAll) });
        return (patched, errors);
    }

    [Fact]
    public void Decode_OldStringEscapes_MatchRealCharacter()
    {
        // 文件为真实 →，old_string 为 literal →（模型把字符写成了转义）
        (string patched, IReadOnlyList<ToolError> errors) = Run(
            "she said → ok",
            "said \\u2192 ok",
            "said X ok");
        Assert.Empty(errors);
        Assert.Equal("she said X ok", patched);
    }

    [Fact]
    public void Encode_OldStringChar_MatchLiteralEscape()
    {
        // 文件为 literal →（6 字符），old_string 为真实 →（模型照抄了 Read 渲染后的字符）
        (string patched, IReadOnlyList<ToolError> errors) = Run(
            "said \\u2192 ok",
            "said → ok",
            "said X ok");
        Assert.Empty(errors);
        Assert.Equal("said X ok", patched);
    }

    [Fact]
    public void Decode_SurrogatePair_MatchEmoji()
    {
        // 文件为真实 😀（BMP 外），old_string 为 literal surrogate pair 转义
        (string patched, IReadOnlyList<ToolError> errors) = Run(
            "grin \U0001F600 done",
            "grin \\uD83D\\uDE00 done",
            "grin ok done");
        Assert.Empty(errors);
        Assert.Equal("grin ok done", patched);
    }

    [Fact]
    public void Encode_SurrogatePair_MatchLiteralEscape()
    {
        // 文件为 literal surrogate pair 转义，old_string 为真实 😀
        (string patched, IReadOnlyList<ToolError> errors) = Run(
            "grin \\uD83D\\uDE00 done",
            "grin \U0001F600 done",
            "grin ok done");
        Assert.Empty(errors);
        Assert.Equal("grin ok done", patched);
    }

    [Fact]
    public void Decode_MultipleHits_FailWithSwapHint()
    {
        // 解码变体在文件中有多处命中 → 视为失败（不报歧义位置），消息报告已尝试 unicode 交换
        (string _, IReadOnlyList<ToolError> errors) = Run(
            "→ →",
            "\\u2192",
            "X");
        Assert.NotEmpty(errors);
        Assert.Contains("已尝试 unicode 转义交换", errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Encode_MultipleHits_Fail()
    {
        // 编码变体在文件中有多处命中 → 视为失败
        (string _, IReadOnlyList<ToolError> errors) = Run(
            "\\u2192 \\u2192",
            "→",
            "X");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void NoEscapes_IndentFallbackStillWorks()
    {
        // 无 \uXXXX 无非 ASCII：unicode 交换不触发，现有缩进降级仍生效（回归）
        (string patched, IReadOnlyList<ToolError> errors) = Run(
            "a\n  b",
            "a\nb",
            "c\nd");
        Assert.Empty(errors);
        Assert.Equal("c\nd", patched);
    }

    [Fact]
    public void ReplaceAll_NoSwapFallback()
    {
        // replace_all=true 不做 unicode 交换降级：精确 0 命中直接失败并返回原文
        (string patched, IReadOnlyList<ToolError> errors) = Run(
            "→",
            "\\u2192",
            "X",
            replaceAll: true);
        Assert.NotEmpty(errors);
        Assert.Equal("→", patched);
    }
}
