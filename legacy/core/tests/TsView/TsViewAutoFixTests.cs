using Xunit;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Lint;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Tests.TsView;

/// <summary>
/// 自动修复测试：验证 patch 中不合规内容的自动纠正行为。
/// </summary>
public sealed class TsViewAutoFixTests : TsViewTestBase
{
    // ========== 空体自动修复（仅含隐式 return）==========

    [Fact]
    public void AutoFix_EmptyBody_OnlyImplicitReturn_ShouldProduceSkipNode()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines("    return;");
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"仅含 return 的方法体应自动修复: {result.Error}");
        Assert.Single(result.Ast.RootNodes);
        Assert.Equal(BescriptStatementKind.Skip, result.Ast.RootNodes[0].Kind);
    }

    [Fact]
    public void AutoFix_EmptyBody_WithReturnVar_ShouldProduceSkipNode()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines("    return retVal;");
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: "retVal", warnings);

        Assert.True(result.IsSuccess, $"仅含 return retVal 的方法体应自动修复: {result.Error}");
        Assert.Single(result.Ast.RootNodes);
        Assert.Equal(BescriptStatementKind.Skip, result.Ast.RootNodes[0].Kind);
    }

    [Fact]
    public void AutoFix_EmptyBody_NormalizedLines_ShouldContainCommentAndReturn()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines("    return;");
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"首次解析失败: {result.Error}");
        Assert.Equal(2, result.NormalizedLines.Count);
        Assert.Contains("//", result.NormalizedLines[0].LineText);
        Assert.Contains("return", result.NormalizedLines[1].LineText);
    }

    [Fact]
    public void AutoFix_MissingVarType_WithInitializerCall_ShouldInferType()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __f = G.create.float(BeFloat.fromBeConst(\"3.0\"));"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"缺类型 var 应按初始化调用补类型: {result.Error}");
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.NotNull(node.Call.ReturnVar);
        Assert.Equal("Float", node.Call.ReturnVar.TypeName);
        Assert.Equal(BescriptVarType.Float, ctx.DeclaredVariables["__f"]);
        Assert.Contains(warnings, static w => w.Message.Contains("var 声明缺少类型", StringComparison.Ordinal));
    }

    [Fact]
    public void AutoFix_MissingVarType_WithVariableCopy_ShouldInferType()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __f = __source;"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__source"] = BescriptVarType.Float;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"缺类型 var 应按变量复制自动改写补类型: {result.Error}");
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.NotNull(node.Call.ReturnVar);
        Assert.Equal("Float", node.Call.ReturnVar.TypeName);
        Assert.Equal(BescriptVarType.Float, ctx.DeclaredVariables["__f"]);
        Assert.Contains(warnings, static w => w.Message.Contains("var 声明缺少类型", StringComparison.Ordinal));
    }

    [Fact]
    public void AutoFix_MissingVarType_WithUnsupportedInitializer_ShouldFail()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __f = 1;"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.False(result.IsSuccess);
        Assert.Contains("赋值右侧必须为调用表达式", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void BeConstDirectAssignment_MustRewriteToCreateCall()
    {
        // 规范测试：fromBeConst 直接赋值是 TS 视图错误写法，但写回层必须自动改写为 create 调用。
        // 这不是可删的兼容路径；它防止 Agent 写错赋值位后卡死在无效补丁循环里。
        // AI幻觉总是导致滥用这个写法,这是高频触发路径必须!!!必须保留!!!! 否则实际使用将出现海量的失败!!!
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __v: BeVector3 = BeVector3.fromBeConst(\"0,0,1\");"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, result.Error);
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("creatVariable.Vector3", node.Call.FunctionName);
        Assert.Equal(3, node.Call.Arguments.Count);
        Assert.All(node.Call.Arguments, static a =>
        {
            Assert.False(a.IsVariable);
            Assert.Equal("Float", a.TypeName);
        });
        Assert.Contains(warnings, static w => w.Message.Contains("fromBeConst", StringComparison.Ordinal) && w.Message.Contains("create 调用", StringComparison.Ordinal));
    }

    [Fact]
    public void BeIconConstDirectAssignment_MustRewriteToCreateIcon()
    {
        // 规范测试：Icon 也走 fromBeConst 赋值位自动改写，结果必须是 create.icon。
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __icon: BeIcon = BeIcon.fromBeConst(\"icon_star\");"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, result.Error);
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("create.icon", node.Call.FunctionName);
        BescriptCallArgument arg = Assert.Single(node.Call.Arguments);
        Assert.False(arg.IsVariable);
        Assert.Equal("Icon", arg.TypeName);
        Assert.Equal("icon_star", arg.DecodedText);
        Assert.Contains(warnings, static w => w.Message.Contains("fromBeConst", StringComparison.Ordinal) && w.Message.Contains("create 调用", StringComparison.Ordinal));
    }

    [Fact]
    public void NonEmptyCanvasConstOutsideCreateCanvas_ShouldFail()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "__m.setCustom<BeCanvas>(BeString.fromBeConst(\"canvas\"), BeCanvas.fromGuidConst(\"0123456789abcdef0123456789abcdef\"));"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__m"] = BescriptVarType.Mech;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.False(result.IsSuccess);
        Assert.Contains("Canvas 消费位自由值仅允许用于 G.create.canvas", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateIcon_IconConst_ShouldPass()
    {
        // 规范测试：Icon 消费位自由值只允许作为 G.create.icon 的参数。
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __icon: BeIcon = G.create.icon(BeIcon.fromBeConst(\"icon_star\"));"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, result.Error);
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("create.icon", node.Call.FunctionName);
        BescriptCallArgument arg = Assert.Single(node.Call.Arguments);
        Assert.Equal("Icon", arg.TypeName);
        Assert.Equal("icon_star", arg.DecodedText);
    }

    [Fact]
    public void NonEmptyIconConstOutsideCreateIcon_ShouldFail()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "G.ui.icon(BeVector3.fromBeConst(\"0,0,0\"), BeVector3.fromBeConst(\"1,1,1\"), BeIcon.fromBeConst(\"icon_star\"), BeColor.fromBeConst(\"255,255,255,255\"));"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.False(result.IsSuccess);
        Assert.Contains("Icon 消费位自由值仅允许用于 G.create.icon", result.Error, StringComparison.Ordinal);
    }

    // ========== Any 参数位自动修复（常量 → 临时变量）==========

    [Fact]
    public void AutoFix_AnyParam_BeStringConst_ShouldExpandToTempVar()
    {
        // List.add 参数"数据"是 Any，传入 BeString 常量应自动修复
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__list"] = BescriptVarType.List;

        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "__list.add(BeString.fromBeConst(\"test\"));"
        );
        List<ToolError> warnings = [];

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"Any 参数位 BeString 常量应自动修复: {result.Error}");
        // create.string(临时变量) + list.add(使用临时变量)
        Assert.Equal(2, result.Ast.RootNodes.Count);
        Assert.Equal(BescriptStatementKind.Call, result.Ast.RootNodes[0].Kind);
        Assert.Contains("create.string", ((IHasCall)result.Ast.RootNodes[0]).Call.SaveName);
        Assert.True(((IHasCall)result.Ast.RootNodes[1]).Call.Arguments[0].IsVariable);
        Assert.Contains(warnings, static w => w.Message.Contains("Any") && w.Message.Contains("临时变量"));
    }

    [Fact]
    public void AutoFix_AnyParam_VariableArg_ShouldPassWithoutFix()
    {
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__list"] = BescriptVarType.List;
        ctx.DeclaredVariables["__str"] = BescriptVarType.String;

        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "__list.add(__str);"
        );
        List<ToolError> warnings = [];

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"变量引用应直接通过: {result.Error}");
        Assert.Single(result.Ast.RootNodes);
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("Any") && w.Message.Contains("临时变量"));
    }

    [Fact]
    public void AutoFix_AnyParam_DictInsert_FloatConst_ShouldExpandToTempVar()
    {
        // Dict.insert 参数 2"数据"是 Any，传入 BeFloat 常量应自动修复
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__dict"] = BescriptVarType.Dict;

        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "__dict.insert(BeString.fromBeConst(\"key\"), BeFloat.fromBeConst(\"3.0\"));"
        );
        List<ToolError> warnings = [];

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"Dict.insert BeFloat 常量应自动修复: {result.Error}");
        // create.float(临时变量) + dict.insert
        Assert.Equal(2, result.Ast.RootNodes.Count);
        Assert.Contains(warnings, static w => w.Message.Contains("临时变量"));
    }

    // ========== create.string 常量折叠 ==========

    [Fact]
    public void CreateString_FloatConst_ShouldRewriteToStringConst()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __s: BeString = G.create.string(BeFloat.fromBeConst(\"3.0\"));"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"G.create.string 的 BeFloat 常量应折叠为 String 常量: {result.Error}");
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.False(node.Call.Arguments[0].IsVariable);
        Assert.Equal("String", node.Call.Arguments[0].TypeName);
        Assert.Equal("3.0", node.Call.Arguments[0].DecodedText);
        Assert.Contains(warnings, static w => w.Message.Contains("BeString.fromBeConst", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateString_StringConst_ShouldPassWithoutFix()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __s: BeString = G.create.string(BeString.fromBeConst(\"hello\"));"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"BeString 常量应直接通过: {result.Error}");
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("String") && w.Message.Contains("临时变量"));
    }

    // ========== 布尔表达式降级 ==========

    [Theory]
    [InlineData("if")]
    [InlineData("while")]
    public void AutoFix_ControlBareBoolVariable_ShouldBecomeCreateBool(string controlKeyword)
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            $"{controlKeyword} (__flag) {{",
            "    // ok",
            "}"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__flag"] = BescriptVarType.Bool;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"{controlKeyword} 裸 Bool 变量应自动改写: {result.Error}");
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("create.bool", node.Call.FunctionName);
        Assert.Contains(warnings, static w => w.Message.Contains("G.create.bool", StringComparison.Ordinal));
    }

    [Fact]
    public void AutoFix_BoolOperators_ShouldLowerToSdkCalls()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __not: BeBool = !__a;",
            "var __and: BeBool = __a && __b;",
            "var __or: BeBool = __a || __b;",
            "var __eq: BeBool = __a == __b;",
            "var __seq: BeBool = __a === __b;",
            "var __xor: BeBool = __a != __b;",
            "var __sxor: BeBool = __a !== __b;"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__a"] = BescriptVarType.Bool;
        ctx.DeclaredVariables["__b"] = BescriptVarType.Bool;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"布尔运算符应降级为 SDK 调用: {result.Error}");
        string[] functionNames = result.Ast.RootNodes
            .OfType<IHasCall>()
            .Select(static n => n.Call.FunctionName)
            .ToArray();
        Assert.Equal(new[] { "bool.not", "bool.and", "bool.or", "bool.equal", "bool.equal", "bool.xor", "bool.xor" }, functionNames);
        Assert.Equal(7, warnings.Count(static w => w.Message.Contains("布尔表达式", StringComparison.Ordinal)));
    }

    [Fact]
    public void AutoFix_IfNestedBoolExpression_ShouldExpandWithoutNestedWarning()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "if (__a && !__b) {",
            "    // ok",
            "}"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__a"] = BescriptVarType.Bool;
        ctx.DeclaredVariables["__b"] = BescriptVarType.Bool;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"if 嵌套布尔表达式应自动展开: {result.Error}");
        Assert.Equal(2, result.Ast.RootNodes.Count);
        Assert.Equal("bool.not", ((IHasCall)result.Ast.RootNodes[0]).Call.FunctionName);
        Assert.Equal("bool.and", ((IHasCall)result.Ast.RootNodes[1]).Call.FunctionName);
        Assert.Equal(1, warnings.Count(static w => w.Message.Contains("if/while 条件中直接写布尔表达式是错误写法", StringComparison.Ordinal)));
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("临时变量", StringComparison.Ordinal));
    }

    [Fact]
    public void AutoFix_WhileSingleLayerBoolExpression_ShouldPass()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "while (__a && __b) {",
            "    // ok",
            "}"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__a"] = BescriptVarType.Bool;
        ctx.DeclaredVariables["__b"] = BescriptVarType.Bool;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"while 单层布尔表达式应通过: {result.Error}");
        WhileAstNode whileNode = Assert.IsType<WhileAstNode>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("bool.and", whileNode.Call.FunctionName);
    }

    [Fact]
    public void AutoFix_WhileNestedBoolExpression_ShouldFail()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "while (__a && !__b) {",
            "    // ok",
            "}"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__a"] = BescriptVarType.Bool;
        ctx.DeclaredVariables["__b"] = BescriptVarType.Bool;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.False(result.IsSuccess);
        Assert.Contains("while 条件不支持需要嵌套展开", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoFix_IfBoolConst_ShouldReachConditionConstValidation()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "if (true) {",
            "    // ok",
            "}"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"Bool 常量条件应进入条件常量校验路径: {result.Error}");
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateControlConditionConstMustBeVar(result.Ast, "script.code");
        Assert.Contains(diagnostics, static d => d.Message.Contains("if/while 条件调用", StringComparison.Ordinal));
    }

    // ═══════════════════════════════════════════════════════════════════
    // 嵌套 create 降级为 fromBeConst
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Downgrade_Float_NestedCreate_ShouldBecomeFromBeConst()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __f: BeFloat = G.create.float(G.create.float(BeFloat.fromBeConst(\"3.0\")));"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"Float 嵌套应降级: {result.Error}");
        Assert.Single(result.Ast.RootNodes);
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("临时变量"));
    }

    [Fact]
    public void Downgrade_Long_NestedCreate_ShouldBecomeFromBeConst()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __l: BeLong = G.create.long(G.create.long(BeLong.fromBeConst(\"42\")));"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"Long 嵌套应降级: {result.Error}");
        Assert.Single(result.Ast.RootNodes);
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("临时变量"));
    }

    [Fact]
    public void Downgrade_Bool_NestedCreate_ShouldBecomeFromBeConst()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __b: BeBool = G.create.bool(G.create.bool(BeBool.fromBeConst(\"1\")));"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"Bool 嵌套应降级: {result.Error}");
        Assert.Single(result.Ast.RootNodes);
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("临时变量"));
    }

    [Fact]
    public void Downgrade_String_NestedCreate_ShouldBecomeFromBeConst()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __s: BeString = G.create.string(G.create.string(BeString.fromBeConst(\"hello\")));"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"String 嵌套应降级: {result.Error}");
        Assert.Single(result.Ast.RootNodes);
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("临时变量"));
    }

    [Fact]
    public void Downgrade_Color_NestedCreate_ShouldBecomeFromBeConst()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __c: BeColor = G.create.color(G.create.color(BeColor.fromBeConst(\"1,0.5,0,1\")));"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"Color 嵌套应降级: {result.Error}");
        Assert.Single(result.Ast.RootNodes);
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("临时变量"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // Vector3 降级：纯 fromBeConst 分支
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Downgrade_Vector3_AllFromBeConst_ShouldBecomeBeVector3FromBeConst()
    {
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__device"] = BescriptVarType.Device;

        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "__device.setPosKinematic(G.creatVariable.Vector3(" +
                "BeFloat.fromBeConst(\"1\"), " +
                "BeFloat.fromBeConst(\"2\"), " +
                "BeFloat.fromBeConst(\"3\")" +
            "));"
        );
        List<ToolError> warnings = [];

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"Vector3 全 fromBeConst 应降级: {result.Error}");
        Assert.Single(result.Ast.RootNodes);
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("临时变量"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // Vector3 降级：混合 fromBeConst 和 G.create.float 分支（递归降级）
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Downgrade_Vector3_MixedFloatCreate_ShouldRecursivelyDowngrade()
    {
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__device"] = BescriptVarType.Device;

        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "__device.setPosKinematic(G.creatVariable.Vector3(" +
                "G.create.float(BeFloat.fromBeConst(\"1\")), " +
                "BeFloat.fromBeConst(\"2\"), " +
                "G.create.float(BeFloat.fromBeConst(\"3\"))" +
            "));"
        );
        List<ToolError> warnings = [];

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"Vector3 混合 Float 创建应递归降级: {result.Error}");
        Assert.Single(result.Ast.RootNodes);
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("临时变量"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // 深度递归：Vector3 内部全部为 G.create.float（三层嵌套）
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Downgrade_Vector3_AllFloatCreate_ShouldFullyRecursivelyDowngrade()
    {
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__device"] = BescriptVarType.Device;

        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "__device.setPosKinematic(G.creatVariable.Vector3(" +
                "G.create.float(BeFloat.fromBeConst(\"1\")), " +
                "G.create.float(BeFloat.fromBeConst(\"2\")), " +
                "G.create.float(BeFloat.fromBeConst(\"3\"))" +
            "));"
        );
        List<ToolError> warnings = [];

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"Vector3 全 Float 创建应完全递归降级: {result.Error}");
        Assert.Single(result.Ast.RootNodes);
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("临时变量"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // 退化场景：非可降级类型的嵌套调用仍正常展开为临时变量
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Downgrade_NonInlinableCall_ShouldStillExpandToTempVar()
    {
        // create.bytes 不在可降级集合中（无压缩、无别名，直接一对一映射）
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __bytes: BeBytes = G.create.bytes(G.create.bytes(BeBytes.fromBeConst()));"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"非可降级嵌套应展开: {result.Error}");
        // 仍应展开为临时变量（2 个根节点）
        Assert.Equal(2, result.Ast.RootNodes.Count);
        Assert.Contains(warnings, static w => w.Message.Contains("临时变量"));
    }

    [Fact]
    public void AutoFix_SdkLiteralArgs_ShouldUseExpectedConstTypes()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __m: BeMech = G.createAIMech(\"spawn\", 1, \"0,0,0\", \"0,0,1\", CreateMechPositionAlignment.Bottom, True);"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"SDK 裸字面量参数应自动转为常量: {result.Error}");
        IHasCall callNode = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("Float", callNode.Call.Arguments[1].TypeName);
        Assert.Equal("Vector3", callNode.Call.Arguments[2].TypeName);
        Assert.Equal("Enum", callNode.Call.Arguments[4].TypeName);
        Assert.Equal("1", callNode.Call.Arguments[4].RawText);
        Assert.Equal("Bool", callNode.Call.Arguments[5].TypeName);
        Assert.Equal("1", callNode.Call.Arguments[5].RawText);
    }

    [Fact]
    public void AutoFix_SdkLiteralArgs_StringForms_ShouldUseExpectedConstTypes()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __l: BeLong = G.create.long(42);",
            "G.debug.enable(false);",
            "G.debug.setColor(\"FF0000AA\");",
            "G.debug.setColor([255, 0, 0]);",
            "var __v: BeVector3 = G.vector3.add([1, \"2\", 3], [4, 5, 6]);",
            "var __m: BeMech = G.createAIMech(\"spawn\", \"2\", \"0,0,0\", \"0,0,1\", \"1\", False);"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"SDK 字符串形式常量应按签名转型: {result.Error}");
        IHasCall longNode = Assert.IsAssignableFrom<IHasCall>(result.Ast.RootNodes[0]);
        IHasCall boolNode = Assert.IsAssignableFrom<IHasCall>(result.Ast.RootNodes[1]);
        IHasCall colorNode = Assert.IsAssignableFrom<IHasCall>(result.Ast.RootNodes[2]);
        IHasCall colorArrayNode = Assert.IsAssignableFrom<IHasCall>(result.Ast.RootNodes[3]);
        IHasCall vectorNode = Assert.IsAssignableFrom<IHasCall>(result.Ast.RootNodes[4]);
        IHasCall mechNode = Assert.IsAssignableFrom<IHasCall>(result.Ast.RootNodes[5]);
        Assert.Equal("Long", longNode.Call.Arguments[0].TypeName);
        Assert.Equal("Bool", boolNode.Call.Arguments[0].TypeName);
        Assert.Equal("0", boolNode.Call.Arguments[0].RawText);
        Assert.Equal("Color", colorNode.Call.Arguments[0].TypeName);
        Assert.Equal("255,0,0,170", colorNode.Call.Arguments[0].RawText);
        Assert.Equal("Color", colorArrayNode.Call.Arguments[0].TypeName);
        Assert.Equal("255,0,0,255", colorArrayNode.Call.Arguments[0].RawText);
        Assert.Equal("Vector3", vectorNode.Call.Arguments[0].TypeName);
        Assert.Equal("1,2,3", vectorNode.Call.Arguments[0].RawText);
        Assert.Equal("Vector3", vectorNode.Call.Arguments[1].TypeName);
        Assert.Equal("4,5,6", vectorNode.Call.Arguments[1].RawText);
        Assert.Equal("Float", mechNode.Call.Arguments[1].TypeName);
        Assert.Equal("Enum", mechNode.Call.Arguments[4].TypeName);
        Assert.Equal("1", mechNode.Call.Arguments[4].RawText);
        Assert.Equal("Bool", mechNode.Call.Arguments[5].TypeName);
        Assert.Equal("0", mechNode.Call.Arguments[5].RawText);
    }

    [Theory]
    [InlineData("var __m: BeMech = G.createAIMech(\"spawn\", 1, \"0,0,0\", \"0,0,1\", BeLong.fromBeConst(\"1\"), true);", "Long 常量但参数期望类型为 Enum")]
    [InlineData("var __m: BeMech = G.createAIMech(\"spawn\", 1, \"0,0,0\", \"0,0,1\", __posType, true);", "Enum 参数位禁止使用变量")]
    [InlineData("var __m: BeMech = G.createAIMech(\"spawn\", 1, \"0,0,0\", \"0,0,1\", \"9\", true);", "不是枚举")]
    [InlineData("var __m: BeMech = G.createAIMech(\"spawn\", 1, \"0,0,0\", \"0,0,1\", ForceMode.Acceleration, true);", "不是枚举")]
    [InlineData("var __m: BeMech = G.createAIMech(\"spawn\", 1, \"0,0,0\", \"0,0,1\", 999, true);", "不是枚举")]
    public void AutoFix_EnumParam_InvalidShapes_ShouldFail(string line, string expectedError)
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(line);
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.False(result.IsSuccess);
        Assert.Contains(expectedError, result.Error, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════
    // if/while 条件冗余 G.create.bool 包装剥离
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("if")]
    [InlineData("while")]
    public void AutoFix_RedundantCreateBoolWrapper_ShouldStrip(string controlKeyword)
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            $"{controlKeyword} (G.create.bool(G.long.equal(__a, BeLong.fromBeConst(\"2\")))) {{",
            "    // ok",
            "}"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__a"] = BescriptVarType.Long;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"{controlKeyword} 冗余 create.bool 包装应自动剥离: {result.Error}");
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("long.equal", node.Call.FunctionName);
        Assert.Contains(warnings, static w => w.Message.Contains("冗余包装", StringComparison.Ordinal) && w.Message.Contains("create.bool", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("if")]
    [InlineData("while")]
    public void AutoFix_NonRedundantCreateBool_ShouldKeep(string controlKeyword)
    {
        // G.create.bool(__flag) 包装裸变量 → 不剥离（裸变量需要 create.bool 包装）
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            $"{controlKeyword} (G.create.bool(__flag)) {{",
            "    // ok",
            "}"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__flag"] = BescriptVarType.Bool;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"{controlKeyword} 裸变量 create.bool 包装应保留: {result.Error}");
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("create.bool", node.Call.FunctionName);
    }

    [Theory]
    [InlineData("if")]
    [InlineData("while")]
    public void AutoFix_DirectBoolCallWithoutCreateBool_ShouldKeep(string controlKeyword)
    {
        // 直接写 G.long.equal(...) 作为条件 → 无冗余，直接通过
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            $"{controlKeyword} (G.long.equal(__a, BeLong.fromBeConst(\"2\"))) {{",
            "    // ok",
            "}"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__a"] = BescriptVarType.Long;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"{controlKeyword} 直接 Bool 调用应通过: {result.Error}");
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("long.equal", node.Call.FunctionName);
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("create.bool 包装", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("if")]
    [InlineData("while")]
    public void AutoFix_CreateBoolWrappingBeConst_ShouldNotStrip(string controlKeyword)
    {
        // G.create.bool(BeBool.fromBeConst("1")) → 内层是常量构造，不剥离
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            $"{controlKeyword} (G.create.bool(BeBool.fromBeConst(\"1\"))) {{",
            "    // ok",
            "}"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, $"{controlKeyword} create.bool(BeBool.fromBeConst) 不应剥离: {result.Error}");
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("create.bool", node.Call.FunctionName);
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("create.bool 包装", StringComparison.Ordinal));
    }

    // ═══════════════════════════════════════════════════════════════════
    // 警告-改写对应验证：每条警告必须对应实际改写，无改写时无警告
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 验证：裸 Bool 变量条件 → create.bool 包装（改写）+ 警告。
    /// 警告描述必须提到 create.bool 和裸变量。
    /// </summary>
    [Theory]
    [InlineData("if")]
    [InlineData("while")]
    public void WarningCorrespondence_BareBoolVar_RewrittenAndWarned(string controlKeyword)
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            $"{controlKeyword} (__flag) {{",
            "    // ok",
            "}"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__flag"] = BescriptVarType.Bool;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, result.Error);
        // 改写验证：条件变成了 create.bool
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("create.bool", node.Call.FunctionName);
        // 警告验证：有且仅有关于此改写的警告
        Assert.Contains(warnings, w => w.Severity == ToolDiagnosticSeverity.Warning
            && w.Message.Contains("create.bool", StringComparison.Ordinal)
            && w.Message.Contains("裸", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证：if 条件直接写 Bool 返回函数调用 → 无改写、无警告。
    /// 正确写法不应触发任何东西。
    /// </summary>
    [Fact]
    public void WarningCorrespondence_DirectBoolCall_NoRewriteNoWarning()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "if (G.long.equal(__a, BeLong.fromBeConst(\"2\"))) {",
            "    // ok",
            "}"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__a"] = BescriptVarType.Long;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, result.Error);
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("long.equal", node.Call.FunctionName);
        Assert.DoesNotContain(warnings, w => w.Message.Contains("create.bool", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证：var 缺类型注解 → 自动推断类型（改写）+ 警告。
    /// 警告必须提到 var 声明缺少类型。
    /// </summary>
    [Fact]
    public void WarningCorrespondence_MissingVarType_InferredAndWarned()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __f = G.create.float(BeFloat.fromBeConst(\"3.0\"));"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, result.Error);
        // 改写验证：推断出 Float 类型
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.NotNull(node.Call.ReturnVar);
        Assert.Equal("Float", node.Call.ReturnVar.TypeName);
        // 警告验证
        Assert.Contains(warnings, w => w.Severity == ToolDiagnosticSeverity.Warning
            && w.Message.Contains("var 声明缺少类型", StringComparison.Ordinal));
    }

    [Fact]
    public void WarningCorrespondence_DuplicateSameTypeVar_ShouldNotWarn()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __data: BeDict = G.create.dict();",
            "var __data: BeDict = G.create.dict();"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(BescriptVarType.Dict, ctx.DeclaredVariables["__data"]);
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("局部变量重复 var 声明", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证：布尔运算符降级 → SDK 调用（改写）+ 每处改写有警告。
    /// </summary>
    [Fact]
    public void WarningCorrespondence_BoolOperatorLowering_EachRewriteHasWarning()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __result: BeBool = __a && __b;"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__a"] = BescriptVarType.Bool;
        ctx.DeclaredVariables["__b"] = BescriptVarType.Bool;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, result.Error);
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("bool.and", node.Call.FunctionName);
        Assert.Contains(warnings, w => w.Severity == ToolDiagnosticSeverity.Warning
            && w.Message.Contains("布尔表达式", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证：fromBeConst 直接赋值 → create 调用（改写）+ 警告。
    /// </summary>
    [Fact]
    public void WarningCorrespondence_FromBeConstDirectAssignment_RewrittenAndWarned()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __v: BeVector3 = BeVector3.fromBeConst(\"0,0,1\");"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, result.Error);
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("creatVariable.Vector3", node.Call.FunctionName);
        Assert.Contains(warnings, w => w.Severity == ToolDiagnosticSeverity.Warning
            && w.Message.Contains("fromBeConst", StringComparison.Ordinal)
            && w.Message.Contains("create 调用", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证：正确的 create 调用 → 无改写、无 fromBeConst 警告。
    /// </summary>
    [Fact]
    public void WarningCorrespondence_CorrectCreateCall_NoRewriteNoWarning()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var __f: BeFloat = G.create.float(BeFloat.fromBeConst(\"3.0\"));"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, result.Error);
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("create.float", node.Call.FunctionName);
        Assert.DoesNotContain(warnings, w => w.Message.Contains("fromBeConst", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证：嵌套 create.bool 包裹非 create 调用 → 剥离（改写）+ 冗余包装警告。
    /// </summary>
    [Theory]
    [InlineData("if")]
    [InlineData("while")]
    public void WarningCorrespondence_RedundantCreateBool_StrippedAndWarned(string controlKeyword)
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            $"{controlKeyword} (G.create.bool(G.long.equal(__a, BeLong.fromBeConst(\"2\")))) {{",
            "    // ok",
            "}"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__a"] = BescriptVarType.Long;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, result.Error);
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("long.equal", node.Call.FunctionName);
        Assert.Contains(warnings, w => w.Severity == ToolDiagnosticSeverity.Warning
            && w.Message.Contains("冗余包装", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证：create.bool 包裹裸变量 → 不剥离、无冗余警告。
    /// 裸变量需要 create.bool 包装是必要转换。
    /// </summary>
    [Theory]
    [InlineData("if")]
    [InlineData("while")]
    public void WarningCorrespondence_CreateBoolWrappingVar_NotStrippedNoStripWarning(string controlKeyword)
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            $"{controlKeyword} (G.create.bool(__flag)) {{",
            "    // ok",
            "}"
        );
        List<ToolError> warnings = [];
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        ctx.DeclaredVariables["__flag"] = BescriptVarType.Bool;

        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, warnings);

        Assert.True(result.IsSuccess, result.Error);
        IHasCall node = Assert.IsAssignableFrom<IHasCall>(Assert.Single(result.Ast.RootNodes));
        Assert.Equal("create.bool", node.Call.FunctionName);
        Assert.DoesNotContain(warnings, w => w.Message.Contains("冗余包装", StringComparison.Ordinal));
    }
}
