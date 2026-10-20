using System.Text.RegularExpressions;
using Toolkit.Core.Infra;
using Toolkit.Core.Search;
using Toolkit.Core.TsView;

using Toolkit.Core.Persistence;

namespace Toolkit.Core.Sdk;

/// <summary>
/// sdk.docs.query 输入参数
/// 按 MCP_Interfaces_Spec.md 规范定义
/// </summary>
public sealed record SdkDocsQueryArgs(
    /// <summary>查询模式：titleRegex / contentRegex / path / listPaths</summary>
    string Mode,
    /// <summary>正则模式（titleRegex/contentRegex 模式使用）</summary>
    string? Pattern,
    /// <summary>精确路径（path 模式使用）</summary>
    string? Path,
    /// <summary>区域过滤</summary>
    string? Area,
    /// <summary>名称匹配类型：ts / bes / both，默认 both</summary>
    string? NameKind = null
);

/// <summary>
/// SDK 文档命中项
/// </summary>
public sealed record SdkDocHit(
    /// <summary>TS 视图路径</summary>
    string TsPath,
    /// <summary>BES 标准路径</summary>
    IReadOnlyList<string> BesPaths,
    /// <summary>BES 标准签名</summary>
    string Snippet,
    /// <summary>全文（完整文档），listPaths/titleRegex 模式不返回</summary>
    string? Content,
    /// <summary>sdk.ts 中的方法声明位置（如 "temp/toolkit/bescript_reference/sdk.ts:692 callFun(...): Promise&lt;void&gt;;"）</summary>
    string? TsDeclLocation = null
);

/// <summary>
/// sdk.docs.query 返回数据
/// </summary>
public sealed record SdkDocsQueryData(
    /// <summary>listPaths 模式返回的路径列表</summary>
    IReadOnlyList<string>? Paths,
    /// <summary>其他模式返回的文档列表</summary>
    IReadOnlyList<SdkDocHit>? Docs,
    /// <summary>使用提示</summary>
    IReadOnlyList<string> Tips
);

/// <summary>
/// SDK 文档查询工具
/// 直接从 BuiltinMethodRegistry 获取方法定义，不自造额外文档
/// </summary>
public sealed class SdkDocsQueryTool
{
    /// <summary>
    /// 每次查询结果都带上的固定提示，引导 Agent 去 sdk.ts 取证
    /// </summary>
    private static readonly string[] DefaultTips =
    [
        "如果未找到你需要的内容，建议直接打开 temp/toolkit/bescript_reference/sdk.ts 查看完整声明",
    ];

    public (SdkDocsQueryData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Query(
        SdkDocsQueryArgs args)
    {
        return QueryCore(args);
    }

    private static (SdkDocsQueryData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Fail(string message)
    {
        return (null, new[] { new ToolError(null, null, null, message) }, Array.Empty<ToolError>());
    }

    private static readonly string[] AllowedModes = { "listPaths", "titleRegex", "contentRegex", "path" };

    private static (SdkDocsQueryData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) QueryCore(
        SdkDocsQueryArgs args)
    {

        string mode = args.Mode;
        (DocNameKind nameKind, string? nameKindError) = ParseNameKind(args.NameKind);
        if (nameKindError != null)
        {
            return Fail(nameKindError);
        }

        // 校验 area 参数
        (MethodCategory? category, string? areaError) = ParseAreaToCategory(args.Area);
        if (areaError != null)
        {
            return Fail(areaError);
        }

        return mode switch
        {
            "listPaths" => (new SdkDocsQueryData(
                CollectSignatures(category)
                    .SelectMany(sig => nameKind switch
                    {
                        DocNameKind.Ts => new[] { sig.TsPath },
                        DocNameKind.Bes => new[] { sig.BesPath },
                        DocNameKind.Both => new[] { sig.TsPath, sig.BesPath },
                        _ => throw new InvalidOperationException($"内部错误: 未知的 DocNameKind: {nameKind}")
                    })
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToArray(),
                null,
                DefaultTips), Array.Empty<ToolError>(), Array.Empty<ToolError>()),
            "titleRegex" => QueryByTitleRegex(args, nameKind, category),
            "contentRegex" => QueryByContentRegex(args, nameKind, category),
            "path" => QueryByPath(args, nameKind, category),
            _ => Fail($"未知的 mode: {args.Mode}（允许的值: {string.Join("/", AllowedModes)}，大小写敏感，必须完全匹配）")
        };
    }

    /// <summary>
    /// titleRegex 模式：按方法名正则匹配
    /// </summary>
    private static (SdkDocsQueryData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) QueryByTitleRegex(
        SdkDocsQueryArgs args,
        DocNameKind nameKind,
        MethodCategory? category)
    {
        if (string.IsNullOrWhiteSpace(args.Pattern))
            return Fail("titleRegex 模式需要提供 pattern 参数");

        Regex regex = TextSearch.Create(args.Pattern, caseSensitive: false);

        DocSignature[] signatures = CollectSignatures(category)
            .Where(sig => nameKind switch
            {
                DocNameKind.Ts => regex.IsMatch(sig.TsPath),
                DocNameKind.Bes => regex.IsMatch(sig.BesPath),
                DocNameKind.Both => regex.IsMatch(sig.TsPath) || regex.IsMatch(sig.BesPath),
                _ => throw new InvalidOperationException($"内部错误: 未知的 DocNameKind: {nameKind}")
            })
            .ToArray();

        IReadOnlyList<MergedDocSignature> merged = MergeByTsPath(signatures);
        SdkDocHit[] docs = merged.Select(static m => new SdkDocHit(
            m.TsPath,
            m.BesPaths,
            m.Signature.ToBesSdkDisplayString(),
            null,
            ResolveDeclLocation(m.TsPath))).ToArray();

        return (new SdkDocsQueryData(null, docs, DefaultTips), Array.Empty<ToolError>(), Array.Empty<ToolError>());
    }

    /// <summary>
    /// contentRegex 模式：按文档内容正则匹配（包括 Summary、Remarks、参数描述）
    /// </summary>
    private static (SdkDocsQueryData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) QueryByContentRegex(
        SdkDocsQueryArgs args,
        DocNameKind nameKind,
        MethodCategory? category)
    {
        if (string.IsNullOrWhiteSpace(args.Pattern))
            return Fail("contentRegex 模式需要提供 pattern 参数");

        Regex regex = TextSearch.Create(args.Pattern, caseSensitive: false);

        DocSignature[] signatures = CollectSignatures(category)
            .Where(sig => MatchContent(sig.Signature, sig, regex, nameKind))
            .ToArray();

        IReadOnlyList<MergedDocSignature> merged = MergeByTsPath(signatures);
        SdkDocHit[] docs = merged.Select(static m => new SdkDocHit(
            m.TsPath,
            m.BesPaths,
            m.Signature.ToBesSdkDisplayString(),
            m.Signature.GetBesSdkDocumentation(),
            ResolveDeclLocation(m.TsPath))).ToArray();

        return (new SdkDocsQueryData(null, docs, DefaultTips), Array.Empty<ToolError>(), Array.Empty<ToolError>());
    }

    /// <summary>
    /// path 模式：精确路径查询，返回完整文档
    /// </summary>
    private static (SdkDocsQueryData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) QueryByPath(
        SdkDocsQueryArgs args,
        DocNameKind nameKind,
        MethodCategory? category)
    {
        if (string.IsNullOrWhiteSpace(args.Path))
            return Fail("path 模式需要提供 path 参数");

        IReadOnlyList<DocSignature> signatures = CollectSignatures(category);
        DocSignature[] matched = signatures.Where(s => PathEquals(args.Path, s, nameKind)).ToArray();

        if (matched.Length == 0)
        {
            return (new SdkDocsQueryData(null, Array.Empty<SdkDocHit>(), DefaultTips), Array.Empty<ToolError>(), Array.Empty<ToolError>());
        }

        IReadOnlyList<MergedDocSignature> merged = MergeByTsPath(matched);
        SdkDocHit[] docs = merged.Select(static m => new SdkDocHit(
            m.TsPath,
            m.BesPaths,
            m.Signature.ToBesSdkDisplayString(),
            m.Signature.GetBesSdkDocumentation(),
            ResolveDeclLocation(m.TsPath))).ToArray();

        return (new SdkDocsQueryData(null, docs, DefaultTips), Array.Empty<ToolError>(), Array.Empty<ToolError>());
    }

    /// <summary>
    /// 检查方法签名的文档内容是否匹配正则
    /// </summary>
    private static bool MatchContent(MethodSignature sig, DocSignature entry, Regex regex, DocNameKind nameKind)
    {
        if (nameKind switch
        {
            DocNameKind.Ts => regex.IsMatch(entry.TsPath),
            DocNameKind.Bes => regex.IsMatch(entry.BesPath),
            DocNameKind.Both => regex.IsMatch(entry.TsPath) || regex.IsMatch(entry.BesPath),
            _ => throw new InvalidOperationException($"内部错误: 未知的 DocNameKind: {nameKind}")
        })
            return true;

        // 匹配 Summary
        if (!string.IsNullOrWhiteSpace(sig.Summary) && regex.IsMatch(sig.Summary))
            return true;

        // 匹配 Remarks
        if (!string.IsNullOrWhiteSpace(sig.Remarks) && regex.IsMatch(sig.Remarks))
            return true;

        // 匹配 Notes
        if (!string.IsNullOrWhiteSpace(sig.Notes) && regex.IsMatch(sig.Notes))
            return true;

        if (regex.IsMatch(sig.MethodName))
            return true;

        // 匹配参数描述
        foreach (MethodParameter param in sig.Parameters)
        {
            if (!string.IsNullOrWhiteSpace(param.Description) && regex.IsMatch(param.Description))
                return true;
            if (regex.IsMatch(param.EffectiveName))
                return true;
        }

        return false;
    }

    private static IReadOnlyList<DocSignature> CollectSignatures(MethodCategory? category)
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        IReadOnlyList<MethodSignature> signatures = registry.QueryAll(category);

        return signatures
            .Select(static sig => new DocSignature(sig, sig.GetTsSdkPath(), sig.GetBesSdkPath()))
            .ToArray();
    }

    /// <summary>
    /// 按 TS 路径分组（因压缩机制，多个 BES 方法可能映射到同一个 TS 方法）
    /// </summary>
#pragma warning disable TK006 // 文档签名按 TS 路径合并，独立数据转换逻辑
    private static IReadOnlyList<MergedDocSignature> MergeByTsPath(IReadOnlyList<DocSignature> signatures)
    {
        return signatures
            .GroupBy(static s => s.TsPath, StringComparer.Ordinal)
            .Select(static g => new MergedDocSignature(
                g.First().Signature,
                g.Key,
                g.Select(static s => s.BesPath)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static p => p, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }
#pragma warning restore TK006

    private static bool PathEquals(string input, DocSignature signature, DocNameKind nameKind)
    {
        return nameKind switch
        {
            DocNameKind.Ts => input.Equals(signature.TsPath, StringComparison.Ordinal),
            DocNameKind.Bes => input.Equals(signature.BesPath, StringComparison.Ordinal),
            DocNameKind.Both => input.Equals(signature.TsPath, StringComparison.Ordinal)
                                || input.Equals(signature.BesPath, StringComparison.Ordinal),
            _ => throw new InvalidOperationException($"内部错误: 未知的 DocNameKind: {nameKind}")
        };
    }
    private static string? ResolveDeclLocation(string tsPath)
    {
        BescriptTsViewDeclarationLibrary.GetSdkText();
        BescriptDtsGenerator.DeclLineRange? range = BescriptDtsGenerator.GetMethodDeclLineRange(tsPath);
        if (range is not null)
        {
            string lineRange = range.StartLine.ToString();
            if (range.StartLine != range.EndLine)
                lineRange = $"{range.StartLine}-{range.EndLine}";
            string sdkRelativePath = $"{ScriptPathHelper.BescriptReferenceRootPath}/{BescriptTsViewDeclarationLibrary.SdkFileName}";
            return $"{sdkRelativePath}:{lineRange} {range.Header}";
        }
        return null;
    }

    internal static string? ResolveDeclLocation(MethodSignature sig)
    {
        return ResolveDeclLocation(sig.GetTsSdkPath());
    }

    internal static string ResolveTsPath(MethodSignature sig)
    {
        return sig.GetTsSdkPath();
    }

    private static (DocNameKind Kind, string? Error) ParseNameKind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (DocNameKind.Both, null);

        return text switch
        {
            "ts" => (DocNameKind.Ts, null),
            "bes" => (DocNameKind.Bes, null),
            "both" => (DocNameKind.Both, null),
            _ => (DocNameKind.Ts, $"未知的 nameKind: {text}（允许的值: ts/bes/both，大小写敏感，必须完全匹配）")
        };
    }

    private sealed record DocSignature(
        MethodSignature Signature,
        string TsPath,
        string BesPath);

    /// <summary>
    /// 合并后的文档签名（按 TS 路径分组）
    /// </summary>
    private sealed record MergedDocSignature(
        MethodSignature Signature,
        string TsPath,
        IReadOnlyList<string> BesPaths);

    private enum DocNameKind
    {
        Ts,
        Bes,
        Both
    }

    private static readonly string[] AllowedAreas = { "fun", "globalfunc", "global", "varf", "varfunc", "var", "act", "deviceaction", "device", "dvf", "editorfunc", "editor" };

    /// <summary>
    /// 将 area 参数转换为 MethodCategory
    /// </summary>
    private static (MethodCategory? Value, string? Error) ParseAreaToCategory(string? area)
    {
        if (string.IsNullOrWhiteSpace(area))
            return (null, null);

        return area switch
        {
            "fun" or "globalfunc" or "global" => (MethodCategory.GlobalFunc, null),
            "varf" or "varfunc" or "var" => (MethodCategory.VarFunc, null),
            "act" or "deviceaction" or "device" => (MethodCategory.DeviceAction, null),
            "dvf" or "editorfunc" or "editor" => (MethodCategory.EditorFunc, null),
            _ => (null, $"未知的 area: {area}（允许的值: {string.Join("/", AllowedAreas)}，大小写敏感，必须完全匹配）")
        };
    }
}
