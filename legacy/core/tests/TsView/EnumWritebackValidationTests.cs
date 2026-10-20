using Xunit;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Tests.TsView;

public sealed class EnumWritebackValidationTests
{
    [Fact]
    public void TsToBescript_WhenGenericEnumTypeDiffersFromMemberOwner_ShouldStillExtractValue()
    {
        TsViewBodyParser.TsBodyParseResult result = ParseBody(
            "var ui: BeUIRect;",
            "ui.alignment(BeEnum.fromBeConst<EventCommentGiftLike>(UiAlignment.center));");

        Assert.True(result.IsSuccess, result.Error);
        IHasCall callNode = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        BescriptCallArgument arg = Assert.Single(callNode.Call.Arguments);
        Assert.Equal("Enum", arg.TypeName);
        Assert.Equal("4", arg.DecodedText);
    }

    [Fact]
    public void TsToBescript_WhenEnumTypeIsUnknown_ShouldFailWithClearMessage()
    {
        TsViewBodyParser.TsBodyParseResult result = ParseBody(
            "var ui: BeUIRect;",
            "ui.alignment(BeEnum.fromBeConst<EventCommentGiftLike>(Foo.Comment));");

        Assert.False(result.IsSuccess);
        Assert.Equal("方法体结构约定：未知枚举类型: Foo", result.Error);
    }

    [Fact]
    public void TsToBescript_WhenDatetimeConstHasNoArgument_ShouldWriteFixedBesValue()
    {
        TsViewBodyParser.TsBodyParseResult result = ParseBody(
            "G.map.setCustom<BeDatetime>(BeString.fromBeConst(\"key\"), BeDatetime.fromBeConst());");

        Assert.True(result.IsSuccess, result.Error);
        IHasCall callNode = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("Datetime", callNode.Call.Arguments[1].TypeName);
        Assert.Equal("0", callNode.Call.Arguments[1].DecodedText);
    }

    [Fact]
    public void TsToBescript_WhenNumberConstHasNoArgument_ShouldWriteFixedBesValue()
    {
        TsViewBodyParser.TsBodyParseResult result = ParseBody(
            "G.map.setRank(BeNumber.fromBeConst(), BeDict.fromBeConst(), BeEnum.fromBeConst<RankValueType>(RankValueType.score), BeLong.fromBeConst(\"0\"));");

        Assert.True(result.IsSuccess, result.Error);
        IHasCall callNode = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("Number", callNode.Call.Arguments[0].TypeName);
        Assert.Equal("?", callNode.Call.Arguments[0].DecodedText);
    }

    [Fact]
    public void TsToBescript_WhenNumberConstHasStringArgument_ShouldFail()
    {
        TsViewBodyParser.TsBodyParseResult result = ParseBody(
            "G.map.setRank(BeNumber.fromBeConst(\"?\"), BeDict.fromBeConst(), BeEnum.fromBeConst<RankValueType>(RankValueType.score), BeLong.fromBeConst(\"0\"));");

        Assert.False(result.IsSuccess);
        Assert.Contains("普通带参 fromBeConst 仅限 Bool/Float/Long/Vector3/Color/String", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TsToBescript_WhenIconConstHasStringArgument_ShouldParseConsumerScopedConst()
    {
        TsViewBodyParser.TsBodyParseResult result = ParseBody(
            "var __icon: BeIcon = G.create.icon(BeIcon.fromBeConst(\"icon_star\"));");

        Assert.True(result.IsSuccess, result.Error);
        IHasCall callNode = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        BescriptCallArgument arg = Assert.Single(callNode.Call.Arguments);
        Assert.Equal("Icon", arg.TypeName);
        Assert.Equal("icon_star", arg.DecodedText);
    }

    [Fact]
    public void BuildConstExpression_WhenNumberQuestionValue_ShouldUseFixedConst()
    {
        ConstExpressionFillResult result = ConstFillMode.BuildConstExpression(new ConstFillContext
        {
            TypeName = "Number",
            Value = "?",
            MethodName = "map.setRank"
        });

        Assert.True(result.Success, result.Error);
        DeviceTsViewExpression expression = Assert.IsAssignableFrom<DeviceTsViewExpression>(result.Expression);
        DeviceTsViewExpression callee = Assert.IsAssignableFrom<DeviceTsViewExpression>(expression.Expression);
        IReadOnlyList<DeviceTsViewExpression> arguments = Assert.IsAssignableFrom<IReadOnlyList<DeviceTsViewExpression>>(expression.Arguments);
        Assert.Equal("fromBeConst", callee.Name);
        Assert.Empty(arguments);
    }

    [Fact]
    public void BuildConstExpression_WhenDatetimeZeroValue_ShouldUseFixedConst()
    {
        ConstExpressionFillResult result = ConstFillMode.BuildConstExpression(new ConstFillContext
        {
            TypeName = "Datetime",
            Value = "0",
            MethodName = "time.datetime"
        });

        Assert.True(result.Success, result.Error);
        DeviceTsViewExpression expression = Assert.IsAssignableFrom<DeviceTsViewExpression>(result.Expression);
        DeviceTsViewExpression callee = Assert.IsAssignableFrom<DeviceTsViewExpression>(expression.Expression);
        IReadOnlyList<DeviceTsViewExpression> arguments = Assert.IsAssignableFrom<IReadOnlyList<DeviceTsViewExpression>>(expression.Arguments);
        Assert.Equal("fromBeConst", callee.Name);
        Assert.Empty(arguments);
    }

    [Fact]
    public void BuildConstExpression_WhenCanvasGuidOutsideCreateCanvas_ShouldFail()
    {
        ConstExpressionFillResult result = ConstFillMode.BuildConstExpression(new ConstFillContext
        {
            TypeName = "Canvas",
            Value = "==guid:0123456789abcdef0123456789abcdef==",
            MethodName = "map.setCustom"
        });

        Assert.False(result.Success);
    }

    [Fact]
    public void BuildConstExpression_WhenCanvasGuidInCreateCanvas_ShouldUseCanvasConst()
    {
        ConstExpressionFillResult result = ConstFillMode.BuildConstExpression(new ConstFillContext
        {
            TypeName = "Canvas",
            Value = "==guid:0123456789abcdef0123456789abcdef==",
            MethodName = "create.canvas"
        });

        Assert.True(result.Success, result.Error);
        DeviceTsViewExpression expression = Assert.IsAssignableFrom<DeviceTsViewExpression>(result.Expression);
        DeviceTsViewExpression callee = Assert.IsAssignableFrom<DeviceTsViewExpression>(expression.Expression);
        IReadOnlyList<DeviceTsViewExpression> arguments = Assert.IsAssignableFrom<IReadOnlyList<DeviceTsViewExpression>>(expression.Arguments);
        Assert.Equal("fromGuidConst", callee.Name);
        Assert.Equal("0123456789abcdef0123456789abcdef", Assert.Single(arguments).Text);
    }

    [Fact]
    public void BuildConstExpression_WhenIconNonEmptyOutsideCreateIcon_ShouldFail()
    {
        ConstExpressionFillResult result = ConstFillMode.BuildConstExpression(new ConstFillContext
        {
            TypeName = "Icon",
            Value = "icon_star",
            MethodName = "ui.icon"
        });

        Assert.False(result.Success);
    }

    [Fact]
    public void BuildConstExpression_WhenIconNonEmptyInCreateIcon_ShouldUseIconConst()
    {
        ConstExpressionFillResult result = ConstFillMode.BuildConstExpression(new ConstFillContext
        {
            TypeName = "Icon",
            Value = "icon_star",
            MethodName = "create.icon"
        });

        Assert.True(result.Success, result.Error);
        DeviceTsViewExpression expression = Assert.IsAssignableFrom<DeviceTsViewExpression>(result.Expression);
        DeviceTsViewExpression callee = Assert.IsAssignableFrom<DeviceTsViewExpression>(expression.Expression);
        IReadOnlyList<DeviceTsViewExpression> arguments = Assert.IsAssignableFrom<IReadOnlyList<DeviceTsViewExpression>>(expression.Arguments);
        Assert.Equal("fromBeConst", callee.Name);
        Assert.Equal("icon_star", Assert.Single(arguments).Text);
    }

    [Fact]
    public void TryMapToBescript_WhenFollowVarfOwnerEnumMatchesOwnerConcreteType_ShouldSucceed()
    {
        ScriptConversionContext context = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());
        context.RegisterParameter("kind", "BeEnum<EventCommentGiftLike>");

        TsViewCallExpr call = new(
            TsViewCallKind.Varf,
            null,
            null,
            "kind",
            "equal",
            null,
            null,
            new[]
            {
                new TsViewArg(
                    TsViewArgKind.BeConst,
                    "0",
                    "0",
                    nameof(BescriptVarType.Enum),
                    nameof(BuiltinEnumTypes.EventCommentGiftLike))
            });

        ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> mapResult = TsViewCallMapper.MapToBescript(call, context);
        Assert.True(mapResult.Success, mapResult.Error);
        BescriptCallSignature sig = mapResult.Value.Sig;
        BescriptCallArgument arg = Assert.Single(sig.Arguments);
        Assert.Equal("Enum", arg.TypeName);
        Assert.Equal("0", arg.DecodedText);
    }

    [Fact]
    public void TryMapToBescript_WhenFollowVarfOwnerEnumValueNotInRange_ShouldFail()
    {
        ScriptConversionContext context = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());
        context.RegisterParameter("kind", "BeEnum<EventCommentGiftLike>");

        TsViewCallExpr call = new(
            TsViewCallKind.Varf,
            null,
            null,
            "kind",
            "equal",
            null,
            null,
            new[]
            {
                new TsViewArg(
                    TsViewArgKind.BeConst,
                    "4",
                    "4",
                    nameof(BescriptVarType.Enum),
                    nameof(BuiltinEnumTypes.UiAlignment))
            });

        ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> mapResult = TsViewCallMapper.MapToBescript(call, context);
        Assert.False(mapResult.Success);
        Assert.Contains("不是枚举", mapResult.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TsToBescript_WhenBareBeEnumWithoutGeneric_ShouldInferFromMember()
    {
        TsViewBodyParser.TsBodyParseResult result = ParseBody(
            "var ui: BeUIRect;",
            "ui.alignment(BeEnum.fromBeConst(UiAlignment.center));");

        Assert.True(result.IsSuccess, result.Error);
        IHasCall callNode = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        BescriptCallArgument arg = Assert.Single(callNode.Call.Arguments);
        Assert.Equal("Enum", arg.TypeName);
        Assert.Equal("4", arg.DecodedText);
    }

    [Fact]
    public void TsToBescript_WhenEnumMemberUnknown_ShouldFail()
    {
        TsViewBodyParser.TsBodyParseResult result = ParseBody(
            "var ui: BeUIRect;",
            "ui.alignment(BeEnum.fromBeConst(EventCommentGiftLike.NoSuchMember));");

        Assert.False(result.IsSuccess);
        Assert.Contains("不存在成员", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TsToBescript_WhenArgNotPropertyAccess_ShouldFail()
    {
        TsViewBodyParser.TsBodyParseResult result = ParseBody(
            "var ui: BeUIRect;",
            "ui.alignment(BeEnum.fromBeConst<EventCommentGiftLike>(\"notAMember\"));");

        Assert.False(result.IsSuccess);
        Assert.Contains("必须是具体枚举成员", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TsToBescript_WhenArgIsNumericLiteral_ShouldFail()
    {
        TsViewBodyParser.TsBodyParseResult result = ParseBody(
            "var ui: BeUIRect;",
            "ui.alignment(BeEnum.fromBeConst(42));");

        Assert.False(result.IsSuccess);
        Assert.Contains("必须是具体枚举成员", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryMapToBescript_WhenEnumParamReceivesVariable_ShouldFail()
    {
        ScriptConversionContext context = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());
        context.DeclaredVariables["__posType"] = BescriptVarType.Enum;

        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __m: BeMech = G.createAIMech(\"spawn\", 1, \"0,0,0\", \"0,0,1\", __posType, true);"
        );
        List<ToolError> warnings = [];
        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, context, expectedRetVar: null, warnings);

        Assert.False(result.IsSuccess);
        Assert.Contains("Enum 参数位禁止使用变量", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryMapToBescript_WhenNonEnumConstAtEnumPosition_ShouldFail()
    {
        ScriptConversionContext context = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());

        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __m: BeMech = G.createAIMech(\"spawn\", 1, \"0,0,0\", \"0,0,1\", BeLong.fromBeConst(\"1\"), true);"
        );
        List<ToolError> warnings = [];
        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, context, expectedRetVar: null, warnings);

        Assert.False(result.IsSuccess);
        Assert.Contains("Long 常量但参数期望类型为 Enum", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryMapToBescript_WhenEnumNumericLiteralAtEnumPosition_ShouldAutoConvert()
    {
        ScriptConversionContext context = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());

        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __m: BeMech = G.createAIMech(\"spawn\", 1, \"0,0,0\", \"0,0,1\", 1, true);"
        );
        List<ToolError> warnings = [];
        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, context, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, result.Error);
        IHasCall callNode = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("Enum", callNode.Call.Arguments[4].TypeName);
        Assert.Equal("1", callNode.Call.Arguments[4].RawText);
    }

    private static TsViewBodyParser.TsBodyParseResult ParseBody(params string[] lines)
    {
        ScriptConversionContext context = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());
        return TsViewBodyParser.ParseTsBodyToAst(ToLines(lines), context, expectedRetVar: null, []);
    }

    private static IReadOnlyList<(int LineNumber, string LineText)> ToLines(params string[] lines)
    {
        return lines.Select((text, idx) => (idx + 1, text)).ToList();
    }
}
