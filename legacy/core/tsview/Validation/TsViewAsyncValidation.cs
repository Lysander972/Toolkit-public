using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

/// <summary>
/// 异步处理：校验 + 两侧独立重算。
///
/// 【TS 视图生成规则——纯按 SDK 注册重算，无视原始 BES 的 async 关键字】
/// 1. SDK 调用的 await：按 SDK 注册，AsyncMode 非 Sync 即需要 await
/// 2. delay：固定需要 await
/// 3. Act 调用的 await：目标签名为 Promise&lt;T&gt; 时需要（由 Mapper + ActTargetInfo 决定）
/// 4. 方法 async 关键字 / BeScriptAsync：函数内部存在 await 则需要（由 TsViewScriptAsyncResolver 决定）
///
/// 【.code 生成规则——纯按 SDK 注册重算，无视 TS 视图的 await】
/// 1. async 前缀完全取决于 SDK 的 boolAsync=true（SdkAsyncMode.Async），与 TsAsync 无关
/// 2. 无任何传播机制
///
/// 【校验】SDK async 校验、自定义 Act async 校验均生成 Warning。
/// </summary>
internal static class TsViewAsyncValidation
{
    /// <summary>
    /// 执行异步校验。返回阻断性错误和警告。
    /// </summary>
    public static (IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Validate(
        IReadOnlyList<DeviceTsView.ScriptUpdate> scriptUpdates,
        string deviceRelativePath,
        ActTargetMap? actTargetMap)
    {
        List<ToolError> errors = [];
        List<(int Line, string Detail)> warningDetails = [];

        foreach (DeviceTsView.ScriptUpdate update in scriptUpdates)
        {
            ValidateNodes(update.BodyAst.RootNodes, actTargetMap, warningDetails);
        }

        List<ToolError> warnings = [];
        if (warningDetails.Count > 0)
        {
            warnings.Add(BuildMergedWarning(deviceRelativePath, warningDetails));
        }

        return (errors, warnings);
    }

    private static void ValidateNodes(
        IReadOnlyList<BescriptAstNode> nodes,
        ActTargetMap? actTargetMap,
        List<(int Line, string Detail)> warningDetails)
    {
        foreach (BescriptAstNode node in nodes)
        {
            if (node.IsDisabled || node.Kind is BescriptStatementKind.Comment or BescriptStatementKind.Skip)
            {
                ValidateNodes(node.Children, actTargetMap, warningDetails);
                continue;
            }

            // Delay 节点内层不参与 SDK async 校验
            if (node.Kind == BescriptStatementKind.Delay)
            {
                ValidateNodes(node.Children, actTargetMap, warningDetails);
                continue;
            }

            if (node is not IHasCall)
            {
                ValidateNodes(node.Children, actTargetMap, warningDetails);
                continue;
            }

            BescriptCallSignature call = ((IHasCall)node).Call;

            bool isControlCondition = node.Kind is BescriptStatementKind.If or BescriptStatementKind.While;
            bool isAwait = node.Kind == BescriptStatementKind.Async;

            MethodSignature? sdkSignature = ResolveSdkSignature(call);

            // SDK async 校验（Warning）
            ValidateSdkAsync(sdkSignature, isAwait, isControlCondition, node.Line, warningDetails);

            // 自定义 Act async 校验（Warning）
            if (actTargetMap is not null && sdkSignature is null)
            {
                ValidateActAsync(call, isAwait, isControlCondition, node.Line, actTargetMap, warningDetails);
            }

            ValidateNodes(node.Children, actTargetMap, warningDetails);
        }
    }

    /// <summary>
    /// SDK async 校验。
    /// Async 节点调用 Sync SDK 函数 → Warning。
    /// Call 节点调用 Async/TsAsync SDK 函数 → Warning。
    /// </summary>
    private static void ValidateSdkAsync(
        MethodSignature? sig,
        bool isAwait,
        bool isControlCondition,
        int line,
        List<(int Line, string Detail)> warningDetails)
    {
        if (sig is null)
            return;

        // 原版 if/while 条件位只能直接写调用本体，不能写 async/delay 复合前缀。
        // 条件位是否合法由解析/回写阶段单独报错，这里不再把它混进普通语句 await 校验。
        if (isControlCondition)
            return;

        if (isAwait && sig.AsyncMode == SdkAsyncMode.Sync)
        {
            string tsPath = SdkDocsQueryTool.ResolveTsPath(sig);
            string? location = SdkDocsQueryTool.ResolveDeclLocation(sig);
            string locationText = location is null ? string.Empty : $"：{location}";
            warningDetails.Add((line, $"SDK 同步函数 {tsPath} 使用了 await，当前已自动将其纠正，建议到 SDK 当中查看明确方法声明{locationText}"));
        }
        else if (!isAwait && sig.AsyncMode is SdkAsyncMode.Async or SdkAsyncMode.TsAsync)
        {
            string tsPath = SdkDocsQueryTool.ResolveTsPath(sig);
            string? location = SdkDocsQueryTool.ResolveDeclLocation(sig);
            string locationText = location is null ? string.Empty : $"：{location}";
            warningDetails.Add((line, $"SDK 异步函数 {tsPath} 未使用 await，当前已自动将其纠正，建议到 SDK 当中查看明确方法声明{locationText}"));
        }
    }

    /// <summary>
    /// 自定义 Act async 校验（Warning）。
    /// </summary>
    private static void ValidateActAsync(
        BescriptCallSignature call,
        bool isAwait,
        bool isControlCondition,
        int line,
        ActTargetMap actTargetMap,
        List<(int Line, string Detail)> warningDetails)
    {
        if (isControlCondition)
        {
            return;
        }

        if (call.Kind != BescriptCallKind.Act)
        {
            return;
        }

        ActTargetInfo? targetInfo = ResolveActTarget(call, actTargetMap);
        if (targetInfo is null)
        {
            return;
        }

        string rawName = call.FunctionName;
        bool isAsync = targetInfo.AsyncRawNames.Contains(rawName);
        bool isAsyncWithReturn = targetInfo.AsyncWithReturnRawNames.Contains(rawName);

        if (isAsyncWithReturn && !isAwait)
        {
            warningDetails.Add((line, $"有返回值的异步方法 {rawName} 未使用 await"));
        }
        else if (!isAsync && isAwait)
        {
            warningDetails.Add((line, $"同步方法 {rawName} 使用了 await"));
        }
    }

    private static ActTargetInfo? ResolveActTarget(BescriptCallSignature call, ActTargetMap actTargetMap)
    {
        if (call is not ActCallSignature act)
        {
            return null;
        }

        if (!act.OwnerNameIsAlias && string.Equals(act.OwnerName, "this", StringComparison.Ordinal))
        {
            return actTargetMap.Self;
        }

        if (act.OwnerNameIsAlias && actTargetMap.ByName.TryGetValue(act.OwnerName, out ActTargetInfo? byName))
        {
            return byName;
        }

        if (!act.OwnerNameIsAlias && int.TryParse(act.OwnerName, out int id) && actTargetMap.ById.TryGetValue(id, out ActTargetInfo? byId))
        {
            return byId;
        }

        return null;
    }

    /// <summary>
    /// 根据调用签名查找 SDK 方法签名。None 返回 null。
    /// Act 调用尝试匹配 SDK DeviceAction（如 findPathAsync），未匹配则返回 null（自定义脚本）。
    /// </summary>
    internal static MethodSignature? ResolveSdkSignature(BescriptCallSignature call)
    {
        if (call.Kind == BescriptCallKind.None)
            return null;

        if (call is ActCallSignature act)
        {
            // Act 调用可能是 SDK DeviceAction（如 findPathAsync）或自定义脚本
            if (!int.TryParse(act.OwnerType, out int typeId))
                return null;
            string? actionOwnerType = DeviceTypeIds.FindActionOwnerTypeName(typeId);
            if (actionOwnerType == null)
                return null;
            return BuiltinMethodRegistry.Instance.GetByPathWithInheritance(
                actionOwnerType + "." + call.FunctionName, MethodCategory.DeviceAction);
        }

        string lookupPath = call.Kind switch
        {
            BescriptCallKind.Fun or BescriptCallKind.Toolf => call.FunctionName,
            BescriptCallKind.Varf =>
                $"{((VarfCallSignature)call).OwnerType}.{call.FunctionName}",
            _ => call.FunctionName
        };

        MethodCategory category = call.Kind switch
        {
            BescriptCallKind.Fun => MethodCategory.GlobalFunc,
            BescriptCallKind.Varf => MethodCategory.VarFunc,
            BescriptCallKind.Toolf => MethodCategory.GlobalFunc,
            BescriptCallKind.Dvf => MethodCategory.EditorFunc,
            _ => throw new InvalidOperationException($"未处理的 BescriptCallKind: {call.Kind}")
        };

        return BuiltinMethodRegistry.Instance.GetByPath(lookupPath, category);
    }

    /// <summary>
    /// .code 侧重算（批量版）。校验通过后、序列化前调用。
    /// </summary>
    public static IReadOnlyList<DeviceTsView.ScriptUpdate> RewriteAsyncForBescriptUpdates(
        IReadOnlyList<DeviceTsView.ScriptUpdate> scriptUpdates)
    {
        List<DeviceTsView.ScriptUpdate>? result = null;
        for (int i = 0; i < scriptUpdates.Count; i++)
        {
            DeviceTsView.ScriptUpdate update = scriptUpdates[i];
            IReadOnlyList<BescriptAstNode> rewritten = RewriteAsyncForBescript(update.BodyAst.RootNodes);
            if (!ReferenceEquals(rewritten, update.BodyAst.RootNodes))
            {
                if (result is null)
                    result = new List<DeviceTsView.ScriptUpdate>(scriptUpdates);
                BescriptAst newAst = update.BodyAst with { RootNodes = rewritten };
                result[i] = update with { BodyAst = newAst };
            }
        }
        if (result is not null)
            return result;
        return scriptUpdates;
    }

    /// <summary>
    /// TS 视图侧 SDK 调用重算：按 SDK 注册，非 Sync 即异步。非 SDK 调用不处理。
    /// </summary>
    public static IReadOnlyList<BescriptAstNode> RewriteAsyncForTsView(IReadOnlyList<BescriptAstNode> nodes)
    {
        List<BescriptAstNode>? result = null;
        for (int i = 0; i < nodes.Count; i++)
        {
            BescriptAstNode node = nodes[i];
            IReadOnlyList<BescriptAstNode> newChildren = RewriteAsyncForTsView(node.Children);
            bool childrenChanged = !ReferenceEquals(newChildren, node.Children);

            BescriptStatementKind? targetKind = null;
            if (node is IHasCall { Call: { } call } && ResolveSdkSignature(call) is { } sig)
            {
                bool wantAsync = sig.AsyncMode is SdkAsyncMode.Async or SdkAsyncMode.TsAsync;
                if (wantAsync && node.Kind == BescriptStatementKind.Call)
                    targetKind = BescriptStatementKind.Async;
                else if (!wantAsync && node.Kind == BescriptStatementKind.Async)
                    targetKind = BescriptStatementKind.Call;
            }

            if (targetKind is not null || childrenChanged)
            {
                if (result is null)
                    result = new List<BescriptAstNode>(nodes);
                if (targetKind is not null)
                    result[i] = RebuildNodeWithKind(node, targetKind.Value, newChildren);
                else
                    result[i] = node with { Children = newChildren };
            }
        }
        if (result is not null)
            return result;
        return nodes;
    }

    /// <summary>
    /// .code 侧重算：只有 boolAsync=true 的 SDK 调用保留 Async，其余全部 Call。
    /// </summary>
    public static IReadOnlyList<BescriptAstNode> RewriteAsyncForBescript(IReadOnlyList<BescriptAstNode> nodes)
    {
        List<BescriptAstNode>? result = null;
        for (int i = 0; i < nodes.Count; i++)
        {
            BescriptAstNode node = nodes[i];
            IReadOnlyList<BescriptAstNode> newChildren = RewriteAsyncForBescript(node.Children);
            bool childrenChanged = !ReferenceEquals(newChildren, node.Children);

            BescriptStatementKind? targetKind = null;
            if (node.Kind == BescriptStatementKind.Async)
            {
                // .code 中只有 boolAsync=true 的 SDK 调用才保留 Async
                bool keep = node is IHasCall { Call: { } call } && ResolveSdkSignature(call) is { AsyncMode: SdkAsyncMode.Async };
                if (!keep)
                    targetKind = BescriptStatementKind.Call;
            }

            if (targetKind is not null || childrenChanged)
            {
                if (result is null)
                    result = new List<BescriptAstNode>(nodes);
                if (targetKind is not null)
                    result[i] = RebuildNodeWithKind(node, targetKind.Value, newChildren);
                else
                    result[i] = node with { Children = newChildren };
            }
        }
        if (result is not null)
            return result;
        return nodes;
    }

    private static BescriptAstNode RebuildNodeWithKind(BescriptAstNode node, BescriptStatementKind kind, IReadOnlyList<BescriptAstNode> children)
    {
        if (node is not IHasCall hasCall)
            throw new InvalidOperationException($"无法为 {kind} 重建不携带调用的节点");

        BescriptCallSignature call = hasCall.Call;
        return kind switch
        {
            BescriptStatementKind.Call => new CallAstNode(node.Line, node.Depth, node.Index, node.Text, call, children, LineStartColumns: node.LineStartColumns, IsDisabled: node.IsDisabled),
            BescriptStatementKind.Async => new AsyncAstNode(node.Line, node.Depth, node.Index, node.Text, call, children, LineStartColumns: node.LineStartColumns, IsDisabled: node.IsDisabled),
            BescriptStatementKind.Delay => new DelayAstNode(node.Line, node.Depth, node.Index, node.Text, call, children, LineStartColumns: node.LineStartColumns, IsDisabled: node.IsDisabled),
            BescriptStatementKind.If => new IfAstNode(node.Line, node.Depth, node.Index, node.Text, call, children, LineStartColumns: node.LineStartColumns, IsDisabled: node.IsDisabled),
            BescriptStatementKind.While => new WhileAstNode(node.Line, node.Depth, node.Index, node.Text, call, children, LineStartColumns: node.LineStartColumns, IsDisabled: node.IsDisabled),
            BescriptStatementKind.Break => new BreakAstNode(node.Line, node.Depth, node.Index, node.Text, call, children, LineStartColumns: node.LineStartColumns, IsDisabled: node.IsDisabled),
            BescriptStatementKind.Continue => new ContinueAstNode(node.Line, node.Depth, node.Index, node.Text, call, children, LineStartColumns: node.LineStartColumns, IsDisabled: node.IsDisabled),
            _ => throw new InvalidOperationException($"不支持的节点类型转换: {kind}")
        };
    }

    private static ToolError BuildMergedWarning(
        string deviceRelativePath,
        List<(int Line, string Detail)> details)
    {
        string summary = string.Join("；", details.Select(d => $"L{d.Line}: {d.Detail}"));
        string message = $"异步标记不一致（共 {details.Count} 处）：{summary}\n" +
            "建议重读视图确认修改后视图符合预期时序。\n" +
            "底层等待规则：只有有返回值的方法会阻塞调用者；无返回值的方法会立即退出；占位返回值即使后续被丢弃也同样会阻塞；" +
            "注意: 靠强改 BeScript/BeScriptAsync 这类实时生成的结构无法控制异步；" +
            "方法在真正调用结束前重复调用会报错；方法状态与零件绑定；底层异步方法可以重入。";

        return new ToolError(deviceRelativePath, null, null, message, ToolDiagnosticSeverity.Warning);
    }
}
