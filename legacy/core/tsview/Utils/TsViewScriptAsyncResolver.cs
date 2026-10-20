using Toolkit.Core.Bescript;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

/// <summary>
/// 异步脚本分析输入。
/// </summary>
internal sealed record TsViewScriptAsyncInput(
    string Name,
    string? SourcePath,
    BescriptScriptInfo ScriptInfo
);

/// <summary>
/// TS 视图侧方法级 async 判定（BeScriptAsync + async 关键字）。
///
/// 【规则】方法内部存在 await → 该方法标记为 async。
///
/// 【await 来源判定——纯按 SDK 注册重算，无视原始 BES 的 async 关键字】
/// - SDK 调用：按 SDK 注册，AsyncMode 非 Sync 即需要 await
/// - delay：固定需要 await
/// - Act 跨零件调用：目标方法的 async 属性由目标零件的 AsyncWithReturnRawNames 决定（源自 SDK 签名传播）
///
/// 【传播】
/// 脚本 A 调用了签名为 Promise&lt;T&gt; 的 async 脚本 B → A 也标记为 async。
/// 无返回值处阻断（调用方不等待，不产生 await）。
/// </summary>
internal static class TsViewScriptAsyncResolver
{
    private sealed record ScriptAnalysis(bool HasAsyncStatement, HashSet<string> CalledScriptNames);

    internal static HashSet<string> ResolveAsyncScripts(
        IReadOnlyList<TsViewScriptAsyncInput> scripts,
        IReadOnlySet<string>? seedAsyncNames = null,
        IReadOnlySet<string>? seedAsyncNamesWithReturn = null,
        IReadOnlySet<string>? externalAsyncWithReturnRawNames = null)
    {
        HashSet<string> knownNames = new(StringComparer.Ordinal);
        foreach (TsViewScriptAsyncInput script in scripts)
        {
            knownNames.Add(script.Name);
        }

        // 记录哪些脚本有返回值（有返回值的 async 脚本才会让调用方等待）
        HashSet<string> hasReturnValue = new(StringComparer.Ordinal);
        foreach (TsViewScriptAsyncInput script in scripts)
        {
            if (script.ScriptInfo.Header.ReturnParameter is { Name: { Length: > 0 } })
            {
                hasReturnValue.Add(script.Name);
            }
        }
        if (seedAsyncNamesWithReturn is not null)
        {
            foreach (string name in seedAsyncNamesWithReturn)
            {
                hasReturnValue.Add(name);
            }
        }

        Dictionary<string, ScriptAnalysis> analyses = new(StringComparer.Ordinal);
        foreach (TsViewScriptAsyncInput script in scripts)
        {
            ScriptAnalysis analysis = AnalyzeScript(script, externalAsyncWithReturnRawNames);
            if (analyses.TryGetValue(script.Name, out ScriptAnalysis? existing))
            {
                if (analysis.HasAsyncStatement)
                {
                    existing = existing with { HasAsyncStatement = true };
                }

                existing.CalledScriptNames.UnionWith(analysis.CalledScriptNames);
                analyses[script.Name] = existing;
                continue;
            }

            analyses[script.Name] = analysis;
        }

        foreach (ScriptAnalysis analysis in analyses.Values)
        {
            analysis.CalledScriptNames.RemoveWhere(name => !knownNames.Contains(name));
        }

        HashSet<string> asyncNames = new(StringComparer.Ordinal);
        foreach ((string name, ScriptAnalysis analysis) in analyses)
        {
            if (analysis.HasAsyncStatement)
            {
                asyncNames.Add(name);
            }
        }

        if (seedAsyncNames is not null)
        {
            foreach (string name in seedAsyncNames)
            {
                asyncNames.Add(name);
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach ((string name, ScriptAnalysis analysis) in analyses)
            {
                if (asyncNames.Contains(name))
                {
                    continue;
                }

                foreach (string callee in analysis.CalledScriptNames)
                {
                    // 只有调用了有返回值的 async 脚本，调用方才需要等待，才传播 async
                    if (asyncNames.Contains(callee) && hasReturnValue.Contains(callee))
                    {
                        asyncNames.Add(name);
                        changed = true;
                        break;
                    }
                }
            }
        }

        return asyncNames;
    }

    private static ScriptAnalysis AnalyzeScript(TsViewScriptAsyncInput script, IReadOnlySet<string>? externalAsyncWithReturnRawNames)
    {
        HashSet<string> calledScriptNames = new(StringComparer.Ordinal);
        bool hasAsync = false;

        if (script.ScriptInfo.Ast.RootNodes.Count == 0)
        {
            return new ScriptAnalysis(false, calledScriptNames);
        }

        BescriptAst ast = script.ScriptInfo.Ast;

        Stack<BescriptAstNode> stack = new(ast.RootNodes);
        while (stack.Count > 0)
        {
            BescriptAstNode node = stack.Pop();

            if (!hasAsync)
            {
                hasAsync = node.Kind == BescriptStatementKind.Delay
                    || (node is IHasCall { Call: { } c }
                        && TsViewAsyncValidation.ResolveSdkSignature(c) is { AsyncMode: not SdkAsyncMode.Sync })
                    || (externalAsyncWithReturnRawNames is not null
                        && node is IHasCall { Call: { Kind: BescriptCallKind.Act, FunctionName: not null } actCall }
                        && externalAsyncWithReturnRawNames.Contains(actCall.FunctionName));
            }

            if (node is IHasCall { Call: { FunctionName: not null } call } &&
                (call.Kind == BescriptCallKind.Fun || call.Kind == BescriptCallKind.Act))
            {
                calledScriptNames.Add(call.FunctionName);
            }

            foreach (BescriptAstNode child in node.Children)
            {
                stack.Push(child);
            }
        }

        return new ScriptAnalysis(hasAsync, calledScriptNames);
    }

}
