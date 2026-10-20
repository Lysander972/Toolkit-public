using Xunit;
using Toolkit.Core.Bescript;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Tests.TsView;

public sealed class BescriptToDeviceTsViewMapperTests
{
    [Fact]
    public void FormatMethodBody_ShouldKeepTwoSpaceIndentation()
    {
        string json = TsCompilerHost.FormatMethodBody("if (flag) {\nvalue.abs();\n}");
        MethodBodyFormatResult result = System.Text.Json.JsonSerializer.Deserialize<MethodBodyFormatResult>(json, BodyWritebackJson.Options)
            ?? throw new InvalidOperationException("TS 官方方法体格式化返回空结果。");

        Assert.Null(result.Error);
        Assert.Equal("if (flag) {\n  value.abs();\n}", result.Text);
        Assert.DoesNotContain("\n    value.abs();", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatMethodBody_ShouldKeepBlankLines()
    {
        string json = TsCompilerHost.FormatMethodBody("value.abs();\n\nvalue.ceil();");
        MethodBodyFormatResult result = System.Text.Json.JsonSerializer.Deserialize<MethodBodyFormatResult>(json, BodyWritebackJson.Options)
            ?? throw new InvalidOperationException("TS 官方方法体格式化返回空结果。");

        Assert.Null(result.Error);
        Assert.Contains("value.abs();\n\nvalue.ceil();", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_DisabledInitialDeclaration_ShouldEmitDeclarationBeforeFirstRead()
    {
        BescriptAst ast = BuildAst(
            "Float value=fun create.float(Float 1)",
            "varf Float.value.abs()");
        ScriptConversionContext context = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());

        DeviceTsViewScriptBody body = BescriptToDeviceTsViewMapper.Map(ast, context, 0, []);

        Assert.Equal("variable", body.Statements[0].Kind);
        Assert.True(body.Statements[0].IsDisabled);
        Assert.Equal("value", body.Statements[0].DeclarationList?.Declarations[0].Name);
        Assert.NotNull(body.Statements[0].DeclarationList?.Declarations[0].Initializer);
        Assert.Equal("variable", body.Statements[1].Kind);
        Assert.Null(body.Statements[1].SourceLine);
        Assert.Equal("value", body.Statements[1].DeclarationList?.Declarations[0].Name);
        Assert.Equal("BeFloat", body.Statements[1].DeclarationList?.Declarations[0].TypeName);
        Assert.Null(body.Statements[1].DeclarationList?.Declarations[0].Initializer);
        Assert.Equal("expression", body.Statements[2].Kind);

        string json = TsCompilerHost.FormatMethodBody("var value: BeFloat;\nvalue.abs();");
        MethodBodyFormatResult result = System.Text.Json.JsonSerializer.Deserialize<MethodBodyFormatResult>(json, BodyWritebackJson.Options)
            ?? throw new InvalidOperationException("TS 官方方法体格式化返回空结果。");
        Assert.Null(result.Error);

        ScriptConversionContext writebackContext = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());
        TsViewBodyParser.TsBodyParseResult writeback = TsViewBodyParser.ParseTsBodyToAst(
            new[]
            {
                (1, "/* var value: BeFloat = G.create.float(BeFloat.fromBeConst(\"1\")); */"),
                (2, "var value: BeFloat;"),
                (3, "value.abs();")
            },
            writebackContext,
            expectedRetVar: null,
            []);
        Assert.True(writeback.IsSuccess, writeback.Error);
        Assert.Equal(2, writeback.Ast.RootNodes.Count);
        Assert.True(writeback.Ast.RootNodes[0].IsDisabled);
        Assert.Equal(BescriptStatementKind.Call, writeback.Ast.RootNodes[1].Kind);
    }

    [Fact]
    public void Map_DisabledInitialDeclaration_WithNextWrite_ShouldNotEmitExtraDeclaration()
    {
        BescriptAst ast = BuildAst(
            "Float value=fun create.float(Float 1)",
            "Float value=fun create.float(Float 2)");
        ScriptConversionContext context = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());

        DeviceTsViewScriptBody body = BescriptToDeviceTsViewMapper.Map(ast, context, 0, []);

        Assert.Equal("variable", body.Statements[0].Kind);
        Assert.True(body.Statements[0].IsDisabled);
        Assert.Equal("variable", body.Statements[1].Kind);
        Assert.NotNull(body.Statements[1].DeclarationList?.Declarations[0].Initializer);
        Assert.DoesNotContain(body.Statements, static s => s.Kind == "variable" && s.SourceLine is null);
    }

    [Fact]
    public void Writeback_DisabledMultilineComment_ShouldParseAsDisabledTsStatement()
    {
        ScriptConversionContext writebackContext = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult writeback = TsViewBodyParser.ParseTsBodyToAst(
            new[]
            {
                (1, "/* var label: BeString = G.create.string("),
                (2, "  BeString.fromBeConst(\"称号区域\")"),
                (3, "); */")
            },
            writebackContext,
            expectedRetVar: null,
            []);

        Assert.True(writeback.IsSuccess, writeback.Error);
        Assert.Single(writeback.Ast.RootNodes);
        Assert.True(writeback.Ast.RootNodes[0].IsDisabled);
    }

    [Fact]
    public void Writeback_DisabledIfBlock_ShouldKeepChildrenActive()
    {
        ScriptConversionContext writebackContext = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult writeback = TsViewBodyParser.ParseTsBodyToAst(
            new[]
            {
                (1, "var value: BeFloat;"),
                (2, "/* if (G.create.bool(BeBool.fromBeConst(\"1\"))) {"),
                (3, "  value = G.create.float(BeFloat.fromBeConst(\"2\"));"),
                (4, "} */")
            },
            writebackContext,
            expectedRetVar: null,
            []);

        Assert.True(writeback.IsSuccess, writeback.Error);
        Assert.Single(writeback.Ast.RootNodes);
        Assert.True(writeback.Ast.RootNodes[0].IsDisabled);
        Assert.Single(writeback.Ast.RootNodes[0].Children);
        Assert.False(writeback.Ast.RootNodes[0].Children[0].IsDisabled);
    }

    [Fact]
    public void Writeback_DisabledWhileBlock_ShouldNotCreateEffectiveLoopScope()
    {
        ScriptConversionContext writebackContext = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult writeback = TsViewBodyParser.ParseTsBodyToAst(
            new[]
            {
                (1, "/* while (G.create.bool(BeBool.fromBeConst(\"1\"))) { */"),
                (2, "  break;"),
                (3, "/* } */")
            },
            writebackContext,
            expectedRetVar: null,
            []);

        Assert.False(writeback.IsSuccess);
        Assert.Contains("break 只能出现在有效 while 内部", writeback.Error);
    }

    [Fact]
    public void Writeback_DisabledIfBlockCommentShell_ShouldKeepDisabledChildComment()
    {
        ScriptConversionContext writebackContext = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult writeback = TsViewBodyParser.ParseTsBodyToAst(
            new[]
            {
                (1, "/* if (G.create.bool(BeBool.fromBeConst(\"1\"))) { */"),
                (2, "/* //原版注释 */"),
                (3, "/* } */")
            },
            writebackContext,
            expectedRetVar: null,
            []);

        Assert.True(writeback.IsSuccess, writeback.Error);
        Assert.Single(writeback.Ast.RootNodes);
        Assert.True(writeback.Ast.RootNodes[0].IsDisabled);
        Assert.Single(writeback.Ast.RootNodes[0].Children);
        Assert.Equal(BescriptStatementKind.Skip, writeback.Ast.RootNodes[0].Children[0].Kind);
        Assert.True(writeback.Ast.RootNodes[0].Children[0].IsDisabled);
        Assert.Equal("//原版注释", writeback.Ast.RootNodes[0].Children[0].Text);
    }

    [Fact]
    public void Writeback_DisabledIfBlockCommentShell_WithHoistedDeclaration_ShouldParse()
    {
        ScriptConversionContext writebackContext = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult writeback = TsViewBodyParser.ParseTsBodyToAst(
            new[]
            {
                (1, "/* if (G.bool.not(flag)) { */"),
                (2, "var req: BeDict = G.create.dict();"),
                (3, "req.insert(BeString.fromBeConst(\"list\"), list);"),
                (4, "/* } */")
            },
            writebackContext,
            expectedRetVar: null,
            []);

        Assert.True(writeback.IsSuccess, writeback.Error);
        Assert.Single(writeback.Ast.RootNodes);
        Assert.True(writeback.Ast.RootNodes[0].IsDisabled);
        Assert.Equal(2, writeback.Ast.RootNodes[0].Children.Count);
        Assert.All(writeback.Ast.RootNodes[0].Children, static child => Assert.False(child.IsDisabled));
    }

    [Fact]
    public void Map_DisabledBesComment_ShouldKeepDisabledCommentShell()
    {
        BescriptAst ast = ParseBody("/*  //原版注释");
        ScriptConversionContext context = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());

        DeviceTsViewScriptBody body = BescriptToDeviceTsViewMapper.Map(ast, context, 0, []);

        Assert.Equal("singleLineComment", body.Statements[0].Kind);
        Assert.True(body.Statements[0].IsDisabled);
        Assert.Equal("原版注释", body.Statements[0].Text);
    }

    [Fact]
    public void Writeback_DisabledBesCommentShell_ShouldRestoreDisabledComment()
    {
        ScriptConversionContext writebackContext = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult writeback = TsViewBodyParser.ParseTsBodyToAst(
            new[]
            {
                (1, "/* //原版注释 */")
            },
            writebackContext,
            expectedRetVar: null,
            []);

        Assert.True(writeback.IsSuccess, writeback.Error);
        Assert.Single(writeback.Ast.RootNodes);
        Assert.Equal(BescriptStatementKind.Skip, writeback.Ast.RootNodes[0].Kind);
        Assert.True(writeback.Ast.RootNodes[0].IsDisabled);
        Assert.Equal("//原版注释", writeback.Ast.RootNodes[0].Text);
    }

    [Fact]
    public void Map_SkipMultilineComment_ShouldKeepMultilineCommentKind()
    {
        BescriptAst ast = new(
            null,
            BescriptHeader.Empty,
            [
                new SkipAstNode(1, 0, 0, "/*原版块注释*/", Array.Empty<BescriptAstNode>(), LineStartColumns: [0])
            ],
            BescriptAst.EmptyDeclaredVariables);
        ScriptConversionContext context = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());

        DeviceTsViewScriptBody body = BescriptToDeviceTsViewMapper.Map(ast, context, 0, []);

        Assert.Equal("multiLineComment", body.Statements[0].Kind);
        Assert.False(body.Statements[0].IsDisabled);
        Assert.Equal("原版块注释", body.Statements[0].Text);
    }

    [Fact]
    public void Map_DisabledSkipMultilineComment_ShouldKeepDisabledMultilineCommentKind()
    {
        BescriptAst ast = new(
            null,
            BescriptHeader.Empty,
            [
                new SkipAstNode(1, 0, 0, "/*原版块注释*/", Array.Empty<BescriptAstNode>(), LineStartColumns: [0], true)
            ],
            BescriptAst.EmptyDeclaredVariables);
        ScriptConversionContext context = new(BuiltinMethodRegistry.Instance, ActTargetMap.Empty, new Dictionary<string, BescriptVarType>());

        DeviceTsViewScriptBody body = BescriptToDeviceTsViewMapper.Map(ast, context, 0, []);

        Assert.Equal("multiLineComment", body.Statements[0].Kind);
        Assert.True(body.Statements[0].IsDisabled);
        Assert.Equal("原版块注释", body.Statements[0].Text);
    }

    private static BescriptAst BuildAst(string disabledLine, string activeLine)
    {
        return ParseBody($"/* {disabledLine}", activeLine);
    }

    private static BescriptAst ParseBody(params string[] commandLines)
    {
        string text = string.Join('\n', new[] { "begincodeV2", "name:test", "lines:" })
            + "\n" + string.Join('\n', commandLines) + "\nendcode";
        BescriptScriptInfo info = BescriptTextParser.Parse("test.code", text).Script;
        return info.Ast;
    }

    private sealed record MethodBodyFormatResult(string Text, string? Error, int? ErrorLine);
}
