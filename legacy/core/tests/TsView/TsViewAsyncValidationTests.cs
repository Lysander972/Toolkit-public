
using Xunit;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Tests.TsView;

public sealed class TsViewAsyncValidationTests
{
    private const string DevicePath = "test.map_/0";

    private static BescriptAstNode MakeCallNode(BescriptStatementKind kind, BescriptCallKind callKind, string ownerName, string functionName, int line = 1)
    {
        BescriptCallSignature call = callKind switch
        {
            BescriptCallKind.Fun => new FunCallSignature(
                functionName,
                functionName,
                Array.Empty<BescriptCallArgument>(),
                SegmentSpans: [],
                null),
            BescriptCallKind.Act => new ActCallSignature(DeviceTypeIds.CoreDevice.ToString(), ownerName, false, functionName, functionName, Array.Empty<BescriptCallArgument>(), SegmentSpans: [], null),
            _ => throw new InvalidOperationException($"不支持的调用类型: {callKind}")
        };
        return kind switch
        {
            BescriptStatementKind.Call => new CallAstNode(line, 0, 0, string.Empty, call, Array.Empty<BescriptAstNode>(), LineStartColumns: [0]),
            BescriptStatementKind.Async => new AsyncAstNode(line, 0, 0, string.Empty, call, Array.Empty<BescriptAstNode>(), LineStartColumns: [0]),
            _ => throw new InvalidOperationException($"不支持的语句类型: {kind}")
        };
    }

    private static BescriptAst MakeAst(params BescriptAstNode[] nodes)
    {
        return new BescriptAst(null, new BescriptHeader("test", null, false, false, false, null, null, Array.Empty<BescriptParameter>(), null), nodes, BescriptAst.EmptyDeclaredVariables);
    }

    private static DeviceTsView.ScriptUpdate MakeUpdate(BescriptAst ast)
    {
        return new DeviceTsView.ScriptUpdate("source.code", "test", "test", false, Array.Empty<TsViewParamSpec>(), "void", null, ast, false, false, false, null, null, string.Empty, false);
    }

    [Fact]
    public void SdkAsyncFunc_WithoutAwait_ShouldWarn()
    {
        // map.getGlobalDataAsync 是 Async SDK 函数，不加 await 应报 Warning
        BescriptAstNode node = MakeCallNode(BescriptStatementKind.Call, BescriptCallKind.Fun, string.Empty, "map.getGlobalDataAsync", 5);
        BescriptAst ast = MakeAst(node);
        DeviceTsView.ScriptUpdate update = MakeUpdate(ast);

        (IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = TsViewAsyncValidation.Validate(new[] { update }, DevicePath, null);

        Assert.Empty(errors);
        Assert.Single(warnings);
        Assert.Contains("SDK 异步函数 G.map.getGlobalDataAsync 未使用 await，当前已自动将其纠正，建议到 SDK 当中查看明确方法声明", warnings[0].Message);
        Assert.Contains("sdk.ts:", warnings[0].Message);
        Assert.Contains("function getGlobalDataAsync(", warnings[0].Message);
        Assert.Contains("L5", warnings[0].Message);
    }

    [Fact]
    public void SdkSyncFunc_WithAwait_ShouldWarn()
    {
        // debug.print 是 Sync SDK 函数，加 await 应报 Warning
        BescriptAstNode node = MakeCallNode(BescriptStatementKind.Async, BescriptCallKind.Fun, string.Empty, "debug.print", 3);
        BescriptAst ast = MakeAst(node);
        DeviceTsView.ScriptUpdate update = MakeUpdate(ast);

        (IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = TsViewAsyncValidation.Validate(new[] { update }, DevicePath, null);

        Assert.Empty(errors);
        Assert.Single(warnings);
        Assert.Contains("SDK 同步函数 G.debug.print 使用了 await，当前已自动将其纠正，建议到 SDK 当中查看明确方法声明", warnings[0].Message);
        Assert.Contains("sdk.ts:", warnings[0].Message);
        Assert.Contains("function print(", warnings[0].Message);
    }

    [Fact]
    public void SdkAsyncFunc_WithAwait_ShouldPass()
    {
        // map.getGlobalDataAsync + await → 正确，无错误
        BescriptAstNode node = MakeCallNode(BescriptStatementKind.Async, BescriptCallKind.Fun, string.Empty, "map.getGlobalDataAsync");
        BescriptAst ast = MakeAst(node);
        DeviceTsView.ScriptUpdate update = MakeUpdate(ast);

        (IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = TsViewAsyncValidation.Validate(new[] { update }, DevicePath, null);

        Assert.Empty(errors);
    }

    [Fact]
    public void SdkSyncFunc_WithoutAwait_ShouldPass()
    {
        // debug.print + Call → 正确，无错误
        BescriptAstNode node = MakeCallNode(BescriptStatementKind.Call, BescriptCallKind.Fun, string.Empty, "debug.print");
        BescriptAst ast = MakeAst(node);
        DeviceTsView.ScriptUpdate update = MakeUpdate(ast);

        (IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = TsViewAsyncValidation.Validate(new[] { update }, DevicePath, null);

        Assert.Empty(errors);
    }

    [Fact]
    public void ActAsync_WithoutAwait_ShouldWarn()
    {
        // 自定义 Act 调用 async 方法不加 await → Warning
        BescriptAstNode node = MakeCallNode(BescriptStatementKind.Call, BescriptCallKind.Act, "this", "myAsyncMethod", 10);
        BescriptAst ast = MakeAst(node);
        DeviceTsView.ScriptUpdate update = MakeUpdate(ast);

        ActTargetInfo selfInfo = new ActTargetInfo(
            0, 1, string.Empty, "TestDevice", "CoreDevice",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["myAsyncMethod"] = "myAsyncMethod" },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["myAsyncMethod"] = "myAsyncMethod" },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["myAsyncMethod"] = "Float" },
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { "myAsyncMethod" },
            new HashSet<string>(StringComparer.Ordinal) { "myAsyncMethod" });

        ActTargetMap actMap = new ActTargetMap(selfInfo,
            new Dictionary<string, ActTargetInfo>(StringComparer.Ordinal),
            new Dictionary<int, ActTargetInfo>(),
            new Dictionary<string, ActTargetInfo>(StringComparer.Ordinal));

        (IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = TsViewAsyncValidation.Validate(new[] { update }, DevicePath, actMap);

        Assert.Empty(errors);
        Assert.Single(warnings);
        Assert.Contains("异步标记不一致", warnings[0].Message);
    }

    [Fact]
    public void MultipleWarnings_ShouldMergeIntoOne()
    {
        // 多处不一致 → 合并为 1 条 Warning
        BescriptAstNode node1 = MakeCallNode(BescriptStatementKind.Call, BescriptCallKind.Act, "this", "asyncA", 5);
        BescriptAstNode node2 = MakeCallNode(BescriptStatementKind.Call, BescriptCallKind.Act, "this", "asyncB", 8);
        BescriptAst ast = MakeAst(node1, node2);
        DeviceTsView.ScriptUpdate update = MakeUpdate(ast);

        ActTargetInfo selfInfo = new ActTargetInfo(
            0, 1, string.Empty, "TestDevice", "CoreDevice",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["asyncA"] = "asyncA", ["asyncB"] = "asyncB" },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["asyncA"] = "asyncA", ["asyncB"] = "asyncB" },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["asyncA"] = "Float", ["asyncB"] = "Float" },
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { "asyncA", "asyncB" },
            new HashSet<string>(StringComparer.Ordinal) { "asyncA", "asyncB" });

        ActTargetMap actMap = new ActTargetMap(selfInfo,
            new Dictionary<string, ActTargetInfo>(StringComparer.Ordinal),
            new Dictionary<int, ActTargetInfo>(),
            new Dictionary<string, ActTargetInfo>(StringComparer.Ordinal));

        (IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = TsViewAsyncValidation.Validate(new[] { update }, DevicePath, actMap);

        Assert.Empty(errors);
        Assert.Single(warnings);
        Assert.Contains("共 2 处", warnings[0].Message);
    }

    [Fact]
    public void TsAsyncSdkFunc_WithoutAwait_ShouldWarn()
    {
        // dynamicCall 是 TsAsync SDK 全局函数，不加 await 应报 Warning
        BescriptAstNode node = MakeCallNode(BescriptStatementKind.Call, BescriptCallKind.Fun, string.Empty, "dynamicCall", 7);
        BescriptAst ast = MakeAst(node);
        DeviceTsView.ScriptUpdate update = MakeUpdate(ast);

        (IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = TsViewAsyncValidation.Validate(new[] { update }, DevicePath, null);

        Assert.Empty(errors);
        Assert.Single(warnings);
        Assert.Contains("SDK 异步函数 G.dynamicCall 未使用 await，当前已自动将其纠正，建议到 SDK 当中查看明确方法声明", warnings[0].Message);
        Assert.Contains("sdk.ts:", warnings[0].Message);
        Assert.Contains("function dynamicCall(", warnings[0].Message);
        Assert.Contains("L7", warnings[0].Message);
    }

    [Fact]
    public void TsAsyncSdkFunc_WithAwait_ShouldPass()
    {
        // dynamicCall + await → 正确，无错误
        BescriptAstNode node = MakeCallNode(BescriptStatementKind.Async, BescriptCallKind.Fun, string.Empty, "dynamicCall");
        BescriptAst ast = MakeAst(node);
        DeviceTsView.ScriptUpdate update = MakeUpdate(ast);

        (IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = TsViewAsyncValidation.Validate(new[] { update }, DevicePath, null);

        Assert.Empty(errors);
    }

    [Fact]
    public void BuiltinActAsync_WithAwait_ShouldNotWarn()
    {
        ActCallSignature call = new ActCallSignature(
            DeviceTypeIds.CoreDevice.ToString(),
            "this",
            false,
            "findPathAsync",
            "findPathAsync",
            Array.Empty<BescriptCallArgument>(),
            SegmentSpans: [],
            null);
        AsyncAstNode node = new AsyncAstNode(12, 0, 0, string.Empty, call, Array.Empty<BescriptAstNode>(), LineStartColumns: [0]);
        BescriptAst ast = MakeAst(node);
        DeviceTsView.ScriptUpdate update = MakeUpdate(ast);

        ActTargetInfo selfInfo = new ActTargetInfo(
            0, DeviceTypeIds.CoreDevice, string.Empty, "TestDevice", "CoreDevice",
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));

        ActTargetMap actMap = new ActTargetMap(selfInfo,
            new Dictionary<string, ActTargetInfo>(StringComparer.Ordinal),
            new Dictionary<int, ActTargetInfo>(),
            new Dictionary<string, ActTargetInfo>(StringComparer.Ordinal));

        (IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = TsViewAsyncValidation.Validate(new[] { update }, DevicePath, actMap);

        Assert.Empty(errors);
        Assert.Empty(warnings);
    }

    [Fact]
    public void BescriptRewrite_TsAsyncSdkCall_ShouldBeCall()
    {
        // TsAsync SDK 函数：.code 侧重算为 Call
        BescriptAstNode node = MakeCallNode(BescriptStatementKind.Async, BescriptCallKind.Fun, string.Empty, "dynamicCall", 3);
        BescriptAst ast = MakeAst(node);
        DeviceTsView.ScriptUpdate update = MakeUpdate(ast);

        IReadOnlyList<DeviceTsView.ScriptUpdate> result = TsViewAsyncValidation.RewriteAsyncForBescriptUpdates(new[] { update });

        Assert.Equal(BescriptStatementKind.Call, result[0].BodyAst.RootNodes[0].Kind);
    }

    [Fact]
    public void CorrectView_MixedCalls_ShouldHaveZeroErrorsAndWarnings()
    {
        // 模拟未修改的自洽视图：Sync 不带 await，Async 带 await，TsAsync 带 await
        BescriptAstNode syncCall = MakeCallNode(BescriptStatementKind.Call, BescriptCallKind.Fun, string.Empty, "debug.print", 1);
        BescriptAstNode asyncCall = MakeCallNode(BescriptStatementKind.Async, BescriptCallKind.Fun, string.Empty, "map.getGlobalDataAsync", 2);
        BescriptAstNode tsAsyncCall = MakeCallNode(BescriptStatementKind.Async, BescriptCallKind.Fun, string.Empty, "dynamicCall", 3);
        BescriptAst ast = MakeAst(syncCall, asyncCall, tsAsyncCall);
        DeviceTsView.ScriptUpdate update = MakeUpdate(ast);

        (IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = TsViewAsyncValidation.Validate(new[] { update }, DevicePath, null);

        Assert.Empty(errors);
        Assert.Empty(warnings);
    }

    [Fact]
    public void BescriptRewrite_RealAsyncSdkCall_ShouldStayAsync()
    {
        // 真正的 Async SDK 函数（boolAsync=true）：.code 侧重算仍为 Async
        BescriptAstNode node = MakeCallNode(BescriptStatementKind.Async, BescriptCallKind.Fun, string.Empty, "map.getGlobalDataAsync", 3);
        BescriptAst ast = MakeAst(node);
        DeviceTsView.ScriptUpdate update = MakeUpdate(ast);

        IReadOnlyList<DeviceTsView.ScriptUpdate> result = TsViewAsyncValidation.RewriteAsyncForBescriptUpdates(new[] { update });

        // 未变化，返回原引用
        Assert.Same(update.BodyAst.RootNodes, result[0].BodyAst.RootNodes);
    }
}
