using Xunit;
using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Canvas;

/// <summary>
/// 旧版字段应作为统一 Canvas Lint 错误参与 diff，不再走文本特判。
/// </summary>
public sealed class CanvasHtmlViewLegacyFieldTests
{
    private const string BaseHtml = @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
<meta charset=""UTF-8"" />
</head>
<body>
<BeCanvas name=""test"" width=""1080"" height=""2160"">
<BeUILabel name=""label1"" pos=""0.5,0.5,0"" size=""200,50,1"" color=""rgba(255,255,255,255)"" text=""Hello""></BeUILabel>
</BeCanvas>
</body>
</html>";

    private const string HtmlWithLegacyPos = @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
<meta charset=""UTF-8"" />
</head>
<body>
<BeCanvas name=""test"" width=""1080"" height=""2160"">
<BeUILabel name=""label1"" legacypos=""0.3,0.3,0"" size=""200,50,1"" color=""rgba(255,255,255,255)"" text=""Hello""></BeUILabel>
</BeCanvas>
</body>
</html>";

    [Fact]
    public void ExistingLegacyField_ModifyValue_ShouldNotCreateNewLintError()
    {
        string modified = HtmlWithLegacyPos.Replace("legacypos=\"0.3,0.3,0\"", "legacypos=\"0.5,0.5,0\"", StringComparison.Ordinal);

        List<string> newErrors = ComputeNewErrors(HtmlWithLegacyPos, modified);

        Assert.Empty(newErrors);
    }

    [Fact]
    public void ExistingLegacyField_AddAnotherLegacyField_ShouldOnlyReportNewNode()
    {
        string modified = HtmlWithLegacyPos.Replace(
            "</BeCanvas>",
            "<BeUILabel name=\"label2\" legacypos=\"0.7,0.7,0\" size=\"100,30,1\" color=\"rgba(255,255,255,255)\" text=\"World\"></BeUILabel>\n</BeCanvas>",
            StringComparison.Ordinal);

        List<string> newErrors = ComputeNewErrors(HtmlWithLegacyPos, modified);

        string error = Assert.Single(newErrors);
        Assert.Contains("legacypos", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingLegacyField_AddOnlyNewStyleField_ShouldPass()
    {
        string modified = HtmlWithLegacyPos.Replace(
            "</BeCanvas>",
            "<BeUILabel name=\"label2\" pos=\"0.7,0.7,0\" size=\"100,30,1\" color=\"rgba(255,255,255,255)\" text=\"World\"></BeUILabel>\n</BeCanvas>",
            StringComparison.Ordinal);

        List<string> newErrors = ComputeNewErrors(HtmlWithLegacyPos, modified);

        Assert.Empty(newErrors);
    }

    [Fact]
    public void NoLegacyField_AddLegacyField_ShouldFail()
    {
        string modified = BaseHtml.Replace(
            "</BeCanvas>",
            "<BeUILabel name=\"label2\" legacypos=\"0.7,0.7,0\" size=\"100,30,1\" color=\"rgba(255,255,255,255)\" text=\"World\"></BeUILabel>\n</BeCanvas>",
            StringComparison.Ordinal);

        List<string> newErrors = ComputeNewErrors(BaseHtml, modified);

        string error = Assert.Single(newErrors);
        Assert.Contains("legacypos", error, StringComparison.Ordinal);
    }

#pragma warning disable TK006 // 测试辅助：封装 baseline/patched 双路校验+差异计算
    private static List<string> ComputeNewErrors(string baselineHtml, string patchedHtml)
    {
        (CanvasNode baselineRoot, _) = CanvasConverter.FromHtml(baselineHtml);
        (CanvasNode patchedRoot, _) = CanvasConverter.FromHtml(patchedHtml);
        List<string> baselineErrors = CanvasViewValidation.ValidateLayout(baselineRoot);
        List<string> patchedErrors = CanvasViewValidation.ValidateLayout(patchedRoot);
        return CanvasViewValidation.ComputeNewErrors(baselineErrors, patchedErrors);
    }
#pragma warning restore TK006
}
