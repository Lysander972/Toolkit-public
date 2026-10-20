using Xunit;
using Toolkit.Core.Canvas;
using Toolkit.Core.Lint;

namespace Toolkit.Core.Tests.Lint.Rules;

public sealed class TextCharacterRuleTests
{
    [Fact]
    public void ValidateBescriptTextCharacters_Should_Report_UnsupportedCharacters()
    {
        const string source = """
begincodeV2
name:Test🙂
lines:
fun debug.log(String Hello🙂世界)
endcode
""";

        IReadOnlyList<LintDiagnostic> diagnostics =
            LintEngine.ValidateBescriptTextCharacters(source, "scripts/Test.code");

        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.UnsupportedTextCharacter &&
            d.Location.Line == 2 &&
            d.Message.Contains("🙂", StringComparison.Ordinal));
        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.UnsupportedTextCharacter &&
            d.Location.Line == 4 &&
            d.Message.Contains("🙂", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, static d =>
            d.RuleId == LintRuleIds.UnsupportedTextCharacter &&
            d.Message.Contains("界", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateCanvasTextCharacters_Should_Report_ParsedStringVars()
    {
        const string html = """
<BeCanvas name="test" width="1080" height="2160">
  <BeUIRect name="root" parentanchor="MIDDLE_CENTER" anchor="MIDDLE_CENTER" pos="0,0,0" size="100,100,1">
    <BeUILabel name="Label🙂" parentanchor="MIDDLE_CENTER" anchor="MIDDLE_CENTER" pos="0,0,0" size="100,20,1" text="Hello🙂世界 机械" fontsize="12"></BeUILabel>
  </BeUIRect>
</BeCanvas>
""";

        (CanvasNode root, _) = CanvasConverter.FromHtml(html);
        IReadOnlyList<LintDiagnostic> diagnostics =
            LintEngine.ValidateCanvasTextCharacters(root, "ui/test.canvas", 1);

        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.UnsupportedTextCharacter &&
            d.Message.Contains("Canvas 字符串字段 UILabel.name", StringComparison.Ordinal) &&
            d.Message.Contains("🙂", StringComparison.Ordinal));
        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.UnsupportedTextCharacter &&
            d.Message.Contains("Canvas 字符串字段 UILabel.text", StringComparison.Ordinal) &&
            d.Message.Contains("🙂", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, static d =>
            d.RuleId == LintRuleIds.UnsupportedTextCharacter &&
            d.Message.Contains("界", StringComparison.Ordinal));
    }
}
