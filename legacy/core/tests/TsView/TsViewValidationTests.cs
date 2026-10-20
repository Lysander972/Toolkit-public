using Xunit;

namespace Toolkit.Core.Tests.TsView;

public sealed class TsViewValidationTests : TsViewTestBase
{
    [Fact]
    public void Parser_UnmatchedOpeningBrace_ShouldFail()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "if (cond) {",
            "    return;");

        string? error = TryParseOnly(lines);
        Assert.NotNull(error);
    }

    [Fact]
    public void Parser_MismatchedIndent_ShouldPass()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "if (cond) {",
            "    return;",
            "  }");

        Assert.Null(TryParseOnly(lines));
    }

    [Fact]
    public void Parse_StringFormatWithJsonContent_ShouldPass()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "礼物消息 = G.string.Format(BeString.fromBeConst(\"[{\\\"msg_id\\\": \\\"1\\\",\\\"sec_openid\\\": \\\"%1\\\",\\\"sec_gift_id\\\": \\\"%2\\\",\\\"gift_num\\\": %3,\\\"gift_value\\\": 10000,\\\"avatar_url\\\": \\\"%4\\\",\\\"nickname\\\": \\\"%5\\\",\\\"timestamp\\\": 1}]\"), 玩家id, 礼物id, 礼物数量, 头像url, 玩家昵称);");

        Assert.Null(TryParseOnly(lines));
    }

    [Fact]
    public void MapBody_NumericLiteral_AutoConvertsToBeConst()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __f: BeFloat = G.create.float(42);");

        string? error = TryConvertMethodBody(lines);
        Assert.Null(error);
    }

    [Fact]
    public void MapBody_BooleanLiteral_AutoConvertsToBeConst()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __b: BeBool = G.create.bool(false);");

        string? error = TryConvertMethodBody(lines);
        Assert.Null(error);
    }

    [Fact]
    public void MapBody_SingleCommentOnly_ShouldPass()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "//这是一行注释");

        Assert.Null(TryConvertMethodBody(lines));
    }

    [Fact]
    public void MapBody_MultipleCommentsOnly_ShouldPass()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "// 第一行注释",
            "//第二行注释",
            "//|第三行注释");

        Assert.Null(TryConvertMethodBody(lines));
    }

    [Fact]
    public void MapBody_BlankLineAsComment_ShouldPass()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            string.Empty);

        Assert.Null(TryConvertMethodBody(lines));
    }

    [Fact]
    public void NewMethod_OnlyCommentBody_ShouldPass()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "//占位注释");

        Assert.Null(TryConvertMethodBody(lines));
    }

    [Fact]
    public void NewMethod_CommentThenRealCode_ShouldPass()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "// 这是一个待实现的方法",
            "return;");

        Assert.Null(TryConvertMethodBody(lines));
    }
}
