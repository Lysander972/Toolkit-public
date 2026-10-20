using Xunit;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Tests.TsView;

public sealed class TsViewWhitespaceConstRegressionTests
{
    [Fact]
    public void BuildConstExpression_Should_Preserve_SingleSpace_StringConst()
    {
        ConstExpressionFillResult result = ConstFillMode.BuildConstExpression(new ConstFillContext
        {
            TypeName = nameof(BescriptVarType.String),
            Value = " ",
            MethodName = "String.split"
        });

        Assert.True(result.Success, result.Error);
        DeviceTsViewExpression expression = Assert.IsAssignableFrom<DeviceTsViewExpression>(result.Expression);
        IReadOnlyList<DeviceTsViewExpression> arguments = Assert.IsAssignableFrom<IReadOnlyList<DeviceTsViewExpression>>(expression.Arguments);
        Assert.Equal(" ", Assert.Single(arguments).Text);
    }

    [Fact]
    public void BuildConstExpression_Should_Preserve_MultipleSpaces_StringConst()
    {
        string spaces = "   ";
        ConstExpressionFillResult result = ConstFillMode.BuildConstExpression(new ConstFillContext
        {
            TypeName = nameof(BescriptVarType.String),
            Value = spaces,
            MethodName = "String.split"
        });

        Assert.True(result.Success, result.Error);
        DeviceTsViewExpression expression = Assert.IsAssignableFrom<DeviceTsViewExpression>(result.Expression);
        IReadOnlyList<DeviceTsViewExpression> arguments = Assert.IsAssignableFrom<IReadOnlyList<DeviceTsViewExpression>>(expression.Arguments);
        Assert.Equal(spaces, Assert.Single(arguments).Text);
    }

    [Fact]
    public void TsToBescript_Should_Preserve_SingleSpace_StringConst_In_Split()
    {
        const string space = " ";
        BescriptAst ast = MapTsBodyToBescriptAst(space);

        BescriptAstNode splitNode = Assert.Single(ast.RootNodes.Where(static n => n is IHasCall hasCall && hasCall.Call.FunctionName == "split"));
        BescriptCallSignature call = ((IHasCall)splitNode).Call;
        Assert.Single(call.Arguments);
        Assert.Equal(space, call.Arguments[0].DecodedText);
    }

    [Fact]
    public void TsToBescript_Should_Preserve_MultipleSpaces_StringConst_In_Split()
    {
        const string spaces = "   ";
        BescriptAst ast = MapTsBodyToBescriptAst(spaces);

        BescriptAstNode splitNode = Assert.Single(ast.RootNodes.Where(static n => n is IHasCall hasCall && hasCall.Call.FunctionName == "split"));
        BescriptCallSignature call = ((IHasCall)splitNode).Call;
        Assert.Single(call.Arguments);
        Assert.Equal(spaces, call.Arguments[0].DecodedText);
    }

    private static BescriptAst MapTsBodyToBescriptAst(string spaces)
    {
        string tsText = string.Join('\n', new[]
        {
            "var word: BeString;",
            "var splits: BeList;",
            "word = G.create.string(BeString.fromBeConst(\"abc\"));",
            $"splits = word.split(BeString.fromBeConst(\"{spaces}\"));",
            "return;"
        });

        List<(int, string line)> rawLines = TextLines.SplitLines(tsText)
            .Select(static (line, index) => (index + 1, line))
            .ToList();

        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        TsViewBodyParser.TsBodyParseResult parseResult = TsViewBodyParser.ParseTsBodyToAst(
            rawLines,
            ctx,
            expectedRetVar: null,
            []);
        Assert.True(parseResult.IsSuccess, parseResult.Error);
        return parseResult.Ast;
    }
}
