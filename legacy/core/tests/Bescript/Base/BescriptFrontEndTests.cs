using Xunit;
using Toolkit.Core.Bescript;

namespace Toolkit.Core.Tests.Bescript.Base;

public sealed class BescriptFrontEndTests
{
    [Fact]
    public void Lexer_Should_Emit_File_And_Command_Tokens()
    {
        string text = string.Join('\n', new[]
        {
            "begincodeV2",
            "name:test",
            "lines:",
            "12:  Float score=fun create.float(Float 1)",
            "endcode"
        });

        BescriptTokenStream stream = BescriptLexer.Lex(text);

        Assert.Contains(stream.Lines[0].Tokens, t => t.Kind == BescriptTokenKind.BeginCode && t.Text == "begincodeV2");
        Assert.Contains(stream.Lines[1].Tokens, t => t.Kind == BescriptTokenKind.HeaderKey && t.Text == "name");
        Assert.Contains(stream.Lines[3].Tokens, t => t.Kind == BescriptTokenKind.LineNumber && t.Text == "12");
        Assert.Contains(stream.Lines[3].Tokens, t => t.Kind == BescriptTokenKind.TypeName && t.Text == "Float");
        Assert.Contains(stream.Lines[3].Tokens, t => t.Kind == BescriptTokenKind.CallKind && t.Text == "fun");
    }

    [Fact]
    public void Lexer_Should_Recognize_Flag_With_Value()
    {
        string text = string.Join('\n', new[]
        {
            "begincodeV2",
            "skip:1",
            "private:1",
            "name:test",
            "lines:",
            "endcode"
        });

        BescriptTokenStream stream = BescriptLexer.Lex(text);

        Assert.Contains(stream.Lines[1].Tokens, t => t.Kind == BescriptTokenKind.HeaderKey && t.Text == "skip");
        Assert.Contains(stream.Lines[2].Tokens, t => t.Kind == BescriptTokenKind.HeaderKey && t.Text == "private");
    }

    [Fact]
    public void Lexer_Should_Emit_RawCanvasBlock_For_MultiLineCanvas()
    {
        string text = string.Join('\n', new[]
        {
            "begincodeV2",
            "name:test",
            "lines:",
            "=fun create.canvas(Canvas ==hjson@1TestCanvas@1",
            "[",
            " {",
            "  type@1UILabel",
            " ]",
            "]==)",
            "endcode"
        });

        BescriptTokenStream stream = BescriptLexer.Lex(text);

        Assert.Equal(BescriptTokenKind.RawCanvasBlock, stream.Lines[4].Tokens[0].Kind);
        Assert.Equal(BescriptTokenKind.RawCanvasBlock, stream.Lines[8].Tokens[0].Kind);
        Assert.Equal(BescriptTokenKind.EndCode, stream.Lines[9].Tokens[0].Kind);
    }

    [Fact]
    public void Lexer_Should_Keep_Tab_In_Command_Tokens()
    {
        string text = string.Join('\n', new[]
        {
            "begincodeV2",
            "name:test",
            "lines:",
            "\tfun create.float(Float 1)",
            "endcode"
        });

        BescriptTokenStream stream = BescriptLexer.Lex(text);

        Assert.DoesNotContain(stream.Lines[3].Tokens, t => t.Kind == BescriptTokenKind.Indent);
        Assert.Contains(stream.Lines[3].Tokens, t => t.Kind == BescriptTokenKind.CallKind && t.Text == "fun");
    }
}
