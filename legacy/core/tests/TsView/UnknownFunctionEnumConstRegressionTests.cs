using Xunit;
using Toolkit.Core.Bescript;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Tests.TsView;

/// <summary>
/// 回归测试：未注册函数消费 Enum 常量时不应让 MCP 视图重建崩溃。
///
/// 触发场景：SDK 注册表已删除 str2File 等历史函数，但 BeScript 脚本中仍残留
/// 调用 `fun str2File(Enum 0)`。此前 BescriptToDeviceTsViewMapper.BuildArgument
/// → ConstFillMode.BuildConstExpression 因 Enum + null EnumType + 非空值直接拒绝
/// 并抛 InvalidOperationException，沿 Parallel.ForEach 一路传播到
/// McpViewWorkspace.ProcessViewRebuildLoop 的 Environment.FailFast，
/// 导致整个 MCP 视图重建进程崩溃。
///
/// 预期行为：未注册函数的参数（包括 Enum）应能正常转换；具体枚举值非法问题应在
/// Lint 中列出而非转换层抛错。
/// </summary>
public sealed class UnknownFunctionEnumConstRegressionTests
{
    [Fact]
    public void Map_UnknownGlobalFunctionWithEnumConstant_ShouldNotThrow()
    {
        BescriptAst ast = ParseBody("fun str2File(Enum 0)");

        ScriptConversionContext context = new(
            BuiltinMethodRegistry.Instance,
            ActTargetMap.Empty,
            new Dictionary<string, BescriptVarType>());

        DeviceTsViewScriptBody body = BescriptToDeviceTsViewMapper.Map(ast, context, 0, []);

        Assert.NotNull(body);
        Assert.NotEmpty(body.Statements);
    }

    [Fact]
    public void Map_UnknownGlobalFunctionWithEnumConstant_WhenNestedInIf_ShouldNotThrow()
    {
        // 复现实际崩溃路径：str2File 调用嵌套在 if 分支中（对应 main.map_/329/event_clientUpdate/结束统计.code）
        BescriptAst ast = ParseBody(
            "if fun bool.equal(Bool 1:Bool 1)",
            "  fun str2File(Enum 0)");

        ScriptConversionContext context = new(
            BuiltinMethodRegistry.Instance,
            ActTargetMap.Empty,
            new Dictionary<string, BescriptVarType>());

        DeviceTsViewScriptBody body = BescriptToDeviceTsViewMapper.Map(ast, context, 0, []);

        Assert.NotNull(body);
        Assert.NotEmpty(body.Statements);
    }

    /// <summary>
    /// 回归测试：未注册全局函数名命中 TS 保留字（public/class/return 等）时
    /// 必须走统一转义规则（TsViewIdentifierMap.ToTsIdentifier，X_ 前缀），
    /// 否则文本化层会直接撞 TS 语法导致整个 MCP 视图重建卡死。
    ///
    /// 预期：G.转义名(...) 形式，与已注册函数的命名规则保持一致。
    /// </summary>
    [Theory]
    [InlineData("public", "X_public")]
    [InlineData("class", "X_class")]
    [InlineData("return", "X_return")]
    [InlineData("default", "X_default")]
    public void Map_UnknownGlobalFunctionWithTsReservedName_ShouldApplyUnifiedEscape(string reservedName, string expectedTsName)
    {
        DeviceTsViewScriptBody body = MapSingleCall($"fun {reservedName}()");

        // 沿 G.<转义名>(...) 的表达式结构遍历：
        //   statement.Expression = call{ Expression = propertyAccess{ Name = expectedTsName, Expression = identifier{ Text = "G" } } }
        AssertCallOnGlobalProperty(body, expectedTsName);
        // 防御性：转义后的名不应等于原始保留字
        Assert.NotEqual(reservedName, expectedTsName);
    }

    /// <summary>
    /// 未注册函数名带路径分段时，每段都应独立转义；
    /// 整体保持原始分段数量（与已注册函数的 Split('.') 处理一致）。
    /// </summary>
    [Fact]
    public void Map_UnknownGlobalFunctionWithDottedReservedSegments_ShouldEscapeEachSegment()
    {
        DeviceTsViewScriptBody body = MapSingleCall("fun class.return()");

        // 期望表达式结构：G.X_class.X_return(...)
        //   call.Expression = propertyAccess{ Name = "X_return",
        //                                    Expression = propertyAccess{ Name = "X_class",
        //                                                                  Expression = identifier{ Text = "G" } } }
        DeviceTsViewExpression callExpr = body.Statements[0].Expression
            ?? throw new InvalidOperationException("missing expression statement");
        Assert.Equal("call", callExpr.Kind);

        DeviceTsViewExpression inner = callExpr.Expression
            ?? throw new InvalidOperationException("missing inner propertyAccess");
        Assert.Equal("propertyAccess", inner.Kind);
        Assert.Equal("X_return", inner.Name);

        DeviceTsViewExpression outer = inner.Expression
            ?? throw new InvalidOperationException("missing outer propertyAccess");
        Assert.Equal("propertyAccess", outer.Kind);
        Assert.Equal("X_class", outer.Name);

        DeviceTsViewExpression gIdent = outer.Expression
            ?? throw new InvalidOperationException("missing G identifier");
        Assert.Equal("identifier", gIdent.Kind);
        Assert.Equal("G", gIdent.Text);
    }

    private static BescriptAst ParseBody(params string[] commandLines)
    {
        string text = string.Join('\n', new[] { "begincodeV2", "name:test", "lines:" })
            + "\n" + string.Join('\n', commandLines) + "\nendcode";
        BescriptScriptInfo info = BescriptTextParser.Parse("test.code", text).Script;
        return info.Ast;
    }

    private static DeviceTsViewScriptBody MapSingleCall(string commandText)
    {
        ScriptConversionContext context = new(
            BuiltinMethodRegistry.Instance,
            ActTargetMap.Empty,
            new Dictionary<string, BescriptVarType>());

        DeviceTsViewScriptBody body = BescriptToDeviceTsViewMapper.Map(ParseBody(commandText), context, 0, []);
        Assert.NotNull(body);
        Assert.NotEmpty(body.Statements);
        return body;
    }

    private static DeviceTsViewExpression AssertCallOnGlobalProperty(DeviceTsViewScriptBody body, string expectedPropertyName)
    {
        DeviceTsViewExpression callExpr = body.Statements[0].Expression
            ?? throw new InvalidOperationException("missing expression statement");
        Assert.Equal("call", callExpr.Kind);

        DeviceTsViewExpression propertyAccess = callExpr.Expression
            ?? throw new InvalidOperationException("missing propertyAccess under call");
        Assert.Equal("propertyAccess", propertyAccess.Kind);
        Assert.Equal(expectedPropertyName, propertyAccess.Name);

        DeviceTsViewExpression gIdent = propertyAccess.Expression
            ?? throw new InvalidOperationException("missing G identifier under propertyAccess");
        Assert.Equal("identifier", gIdent.Kind);
        Assert.Equal("G", gIdent.Text);
        return callExpr;
    }
}
