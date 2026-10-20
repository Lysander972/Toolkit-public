using Xunit;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Bescript.Base;

public sealed class BescriptAstTests
{
    [Fact]
    public void CallParser_Should_Parse_LeadingEquals_ReturnWithoutName()
    {
        BescriptCallSignature sig = BescriptCallParser.Parse("=act 1101.this.UI'玩家加入特效(var 玩家)");

        Assert.Equal(BescriptCallKind.Act, sig.Kind);
        Assert.Null(sig.ReturnVar);
        Assert.True(sig.DiscardsReturn);
        ActCallSignature act = Assert.IsType<ActCallSignature>(sig);
        Assert.Equal("1101", act.OwnerType);
        Assert.Equal("this", act.OwnerName);
        Assert.Equal("UI'玩家加入特效", sig.FunctionName);
        Assert.Single(sig.Arguments);
        Assert.True(sig.Arguments[0].IsVariable);
        Assert.Equal("玩家", sig.Arguments[0].DecodedText);
    }

    [Fact]
    public void AstBuilder_Should_Attach_CallSignature_For_If_Delay_Async()
    {
        string text = string.Join('\n', new[]
        {
            "begincodeV2",
            "name:test",
            "lines:",
            "if{fun bool.not(Bool 1)",
            "  fun break()",
            "}",
            "delay fun create.float(Float 1)",
            "async Dict ack=fun dynamicCallRet(String Foo:Bool 0)",
            "endcode"
        });

        (BescriptScriptInfo info, List<ToolError> errors, List<ToolError> warnings) = BescriptTextParser.Parse("in-memory.code", text);
        Assert.Empty(errors);

        BescriptAst ast = info.Ast;

        BescriptAstNode? ifNode = FindFirst(ast, BescriptStatementKind.If);
        Assert.NotNull(ifNode);
        Assert.Equal(BescriptCallKind.Fun, ((IHasCall)ifNode).Call.Kind);

        BescriptAstNode? breakNode = FindFirst(ast, BescriptStatementKind.Break);
        Assert.NotNull(breakNode);
        Assert.Equal(BescriptCallKind.Fun, ((IHasCall)breakNode).Call.Kind);
        Assert.Equal("break", ((IHasCall)breakNode).Call.FunctionName);

        BescriptAstNode? delayNode = FindFirst(ast, BescriptStatementKind.Delay);
        Assert.NotNull(delayNode);
        Assert.Equal(BescriptCallKind.Fun, ((IHasCall)delayNode).Call.Kind);

        BescriptAstNode? asyncNode = FindFirst(ast, BescriptStatementKind.Async);
        Assert.NotNull(asyncNode);
        Assert.Equal(BescriptCallKind.Fun, ((IHasCall)asyncNode).Call.Kind);
        ReturnVarDecl? returnVar = ((IHasCall)asyncNode).Call.ReturnVar;
        Assert.NotNull(returnVar);
        Assert.Equal("ack", returnVar.Name);
    }

    [Fact]
    public void CallParser_Should_Preserve_SingleSpace_StringConst()
    {
        BescriptCallSignature sig = BescriptCallParser.Parse("String splits=varf String.word.split(String  )");

        Assert.Equal(BescriptCallKind.Varf, sig.Kind);
        Assert.Single(sig.Arguments);
        Assert.False(sig.Arguments[0].IsVariable);
        Assert.Equal("String", sig.Arguments[0].TypeName);
        Assert.Equal(" ", sig.Arguments[0].RawText);
        Assert.Equal(" ", sig.Arguments[0].DecodedText);
    }

    [Fact]
    public void CallParser_Should_Preserve_MultipleSpaces_StringConst()
    {
        string spaces = "   ";
        BescriptCallSignature sig = BescriptCallParser.Parse($"String splits=varf String.word.split(String {spaces})");

        Assert.Equal(BescriptCallKind.Varf, sig.Kind);
        Assert.Single(sig.Arguments);
        Assert.False(sig.Arguments[0].IsVariable);
        Assert.Equal("String", sig.Arguments[0].TypeName);
        Assert.Equal(spaces, sig.Arguments[0].RawText);
        Assert.Equal(spaces, sig.Arguments[0].DecodedText);
    }

    private static BescriptAstNode? FindFirst(BescriptAst ast, BescriptStatementKind kind)
    {
        foreach (BescriptAstNode node in ast.RootNodes)
        {
            BescriptAstNode? hit = FindFirst(node, kind);
            if (hit is not null)
            {
                return hit;
            }
        }

        return null;
    }

    private static BescriptAstNode? FindFirst(BescriptAstNode node, BescriptStatementKind kind)
    {
        if (node.Kind == kind)
        {
            return node;
        }

        foreach (BescriptAstNode child in node.Children)
        {
            BescriptAstNode? hit = FindFirst(child, kind);
            if (hit is not null)
            {
                return hit;
            }
        }

        return null;
    }
}
