using Xunit;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Bescript.Base;

public sealed class BescriptTextParserTests
{
    private static BescriptAstNode SingleRoot(string text)
    {
        (BescriptScriptInfo info, List<ToolError> errors, List<ToolError> _) = BescriptTextParser.Parse("in-memory.code", text);
        Assert.Empty(errors);
        BescriptAstNode node = Assert.Single(info.Ast.RootNodes);
        return node;
    }

    [Fact]
    public void Parse_Should_Not_Treat_Tab_As_Indentation()
    {
        string text = string.Join('\n', new[]
        {
            "begincodeV2",
            "name:test",
            "lines:",
            "\tfun create.float(Float 1)",
            "endcode"
        });

        BescriptAstNode node = SingleRoot(text);

        Assert.Equal(0, node.Depth);
        Assert.StartsWith("\tfun ", node.Text);
    }

    [Fact]
    public void Parse_Should_Not_Error_On_Odd_Leading_Spaces()
    {
        string text = string.Join('\n', new[]
        {
            "begincodeV2",
            "name:test",
            "lines:",
            " fun create.float(Float 1)",
            "endcode"
        });

        BescriptAstNode node = SingleRoot(text);

        Assert.Equal(0, node.Depth);
        Assert.StartsWith("fun ", node.Text);
    }

    [Fact]
    public void Parse_Should_Not_Treat_TabPrefixed_LineComment_As_Skip()
    {
        string text = string.Join('\n', new[]
        {
            "begincodeV2",
            "name:test",
            "lines:",
            "\t//comment",
            "endcode"
        });

        BescriptAstNode node = SingleRoot(text);

        Assert.False(node.IsDisabled);
        Assert.StartsWith("//", node.Text);
    }

    [Fact]
    public void Parse_Should_Collect_Multiline_NodeCanvas_As_Single_Command()
    {
        string text = string.Join('\n', new[]
        {
            "begincodeV2",
            "name:test",
            "lines:",
            "=fun create.canvas(Canvas ==hjson@1@1",
            "[",
            " {",
            "  type@1UILabel",
            "  name@1标题",
            "  Pos@1[0,0,0]",
            "  parentAnchor@14",
            "  size@1[100,30,1]",
            "  text@1Hello",
            " }",
            "]==)",
            "endcode"
        });

        BescriptAstNode node = SingleRoot(text);

        Assert.Contains("==hjson@1@1", node.Text, StringComparison.Ordinal);
        Assert.Contains("]==)", node.Text, StringComparison.Ordinal);
        Assert.Equal(4, node.Line);
        Assert.Equal(14, node.EndLine);
    }
}
