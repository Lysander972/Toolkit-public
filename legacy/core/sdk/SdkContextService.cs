using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Sdk;

/// <summary>
/// SDK 上下文注入请求（传入被读取文件路径，以及可选的 transcript 路径用于历史去重）
/// </summary>
public sealed record SdkContextRequest(string FilePath, string? TranscriptPath = null);

/// <summary>
/// SDK 上下文注入响应（需注入的 SDK 声明片段，空串表示无需注入）
/// </summary>
public sealed record SdkContextResponse(string Context);

public sealed record SdkContextHookResponse(string Body, string ContentType);

/// <summary>
/// 从被读取的 TS 视图 / .code 文件路径出发，通过权威 BeScript AST 提取精确调用目标，
/// 再直接从 BuiltinMethodRegistry 内存索引精确命中对应签名及文档，按请求去重，
/// 并可依据 transcript 做历史过滤后返回。
/// </summary>
public sealed class SdkContextService
{
    private readonly ToolkitServices _services;
    private readonly string _projectRootFullPath;

    private const string ViewsPrefix = "temp/toolkit/views/";

    public SdkContextService(ToolkitServices services, string projectRootFullPath)
    {
        _services = services;
        _projectRootFullPath = Path.TrimEndingDirectorySeparator(projectRootFullPath);
    }

    public SdkContextResponse GetContext(SdkContextRequest request)
    {
        SdkContextBuildResult result = BuildContext(request);
        return new SdkContextResponse(result.Context);
    }

    private SdkContextBuildResult BuildContext(SdkContextRequest request)
    {
        string relativePath = ResolveProjectRelativePath(request.FilePath);

        List<string> scriptPaths = ResolveScriptPaths(relativePath);
        if (scriptPaths.Count == 0)
            return new SdkContextBuildResult(false, string.Empty);

        HashSet<CallTarget> callTargets = [];
        foreach (string scriptPath in scriptPaths)
        {
            BescriptParseData? parseData = _services.Analysis.ParseScript(scriptPath);
            if (parseData?.Ast == null)
                continue;

            foreach (BescriptAstNode node in parseData.Ast.RootNodes.FlattenNodes())
            {
                if (node is IHasCall hasCallNode && hasCallNode.Call.Kind != BescriptCallKind.None)
                {
                    string? ownerType = hasCallNode.Call switch
                    {
                        VarfCallSignature v => v.OwnerType,
                        ActCallSignature a => a.OwnerType,
                        DvfCallSignature d => d.OwnerType,
                        _ => null
                    };
                    callTargets.Add(new CallTarget(hasCallNode.Call.Kind, ownerType, hasCallNode.Call.FunctionName));
                }
            }
        }

        if (callTargets.Count == 0)
            return new SdkContextBuildResult(false, string.Empty);

        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        List<string> snippets = [];
        HashSet<string> requestDedup = new(StringComparer.Ordinal);

        foreach (CallTarget target in callTargets)
        {
            MethodSignature? sig = target.Kind switch
            {
                BescriptCallKind.Fun => registry.GetByPath(target.FunctionName, MethodCategory.GlobalFunc),
                BescriptCallKind.Varf when !string.IsNullOrEmpty(target.OwnerType)
                    => registry.GetByPathWithInheritance($"{target.OwnerType}.{target.FunctionName}", MethodCategory.VarFunc),
                BescriptCallKind.Dvf when !string.IsNullOrEmpty(target.OwnerType)
                    => registry.GetByPath($"{target.OwnerType}.{target.FunctionName}", MethodCategory.EditorFunc),
                BescriptCallKind.Act when !string.IsNullOrEmpty(target.OwnerType)
                    => registry.GetByPathWithInheritance($"{target.OwnerType}.{target.FunctionName}", MethodCategory.DeviceAction),
                _ => null
            };

            if (sig is null)
                continue;

            string doc = sig.GetBesSdkDocumentation();
            string? location = SdkDocsQueryTool.ResolveDeclLocation(sig);
            if (location is not null)
                doc += Environment.NewLine + "SDK声明: " + location;

            if (requestDedup.Add(StripWhitespace(doc)))
                snippets.Add(doc);
        }

        if (snippets.Count == 0)
            return new SdkContextBuildResult(false, string.Empty);

        string? transcriptStripped = null;
        if (!string.IsNullOrEmpty(request.TranscriptPath))
        {
            try
            {
                string fullTranscriptPath = Path.IsPathRooted(request.TranscriptPath)
                    ? request.TranscriptPath
                    : Path.Combine(_projectRootFullPath, request.TranscriptPath);
                string transcriptText = File.ReadAllText(fullTranscriptPath);
                transcriptStripped = StripWhitespace(transcriptText);
            }
            catch
            {
                transcriptStripped = null;
            }
        }

        StringBuilder sb = new();
        bool appended = false;
        foreach (string snippet in snippets)
        {
            if (transcriptStripped != null && transcriptStripped.Contains(StripWhitespace(snippet), StringComparison.Ordinal))
                continue;

            if (sb.Length > 0)
                sb.AppendLine().AppendLine();
            sb.Append(snippet);
            appended = true;
        }

        return new SdkContextBuildResult(appended, sb.ToString());
    }

    public SdkContextHookResponse BuildHookResponse(string agent, string rawInput)
    {
        return agent switch
        {
            "claude" => BuildClaudeHookResponse(rawInput),
            "factory" => BuildFactoryHookResponse(rawInput),
            "cline" => BuildClineHookResponse(rawInput),
            "kimi" => BuildKimiHookResponse(rawInput),
            _ => JsonHookResponse("{}")
        };
    }

    private SdkContextHookResponse BuildClaudeHookResponse(string rawInput)
    {
        if (DeserializeHookInput<ReadToolHookInput>(rawInput) is not ReadToolHookInput input)
            return JsonHookResponse("{}");

        if (input.ToolName != "Read")
            return JsonHookResponse("{}");

        SdkContextBuildResult context = BuildContextForHookFile(input.ToolInput?.FilePath, input.TranscriptPath);
        if (!context.Found)
            return JsonHookResponse("{}");

        return JsonHookResponse(JsonSerializer.Serialize(new
        {
            hookSpecificOutput = new
            {
                hookEventName = "PostToolUse",
                additionalContext = context.Context
            }
        }));
    }

    private SdkContextHookResponse BuildFactoryHookResponse(string rawInput)
    {
        if (DeserializeHookInput<ReadToolHookInput>(rawInput) is not ReadToolHookInput input)
            return JsonHookResponse("{}");

        if (input.ToolName != "Read")
            return JsonHookResponse("{}");

        SdkContextBuildResult context = BuildContextForHookFile(input.ToolInput?.FilePath, string.Empty);
        if (!context.Found)
            return JsonHookResponse("{}");

        return JsonHookResponse(JsonSerializer.Serialize(new
        {
            hookSpecificOutput = new
            {
                hookEventName = "PostToolUse",
                additionalContext = context.Context
            }
        }));
    }

    private SdkContextHookResponse BuildClineHookResponse(string rawInput)
    {
        if (DeserializeHookInput<ClineHookInput>(rawInput) is not ClineHookInput input)
            return JsonHookResponse("{\"cancel\":false}");

        if (input.PostToolUse is not ClinePostToolUse postToolUse || postToolUse.ToolName != "read_file")
            return JsonHookResponse("{\"cancel\":false}");

        SdkContextBuildResult context = BuildContextForHookFile(postToolUse.Parameters?.Path, string.Empty);
        if (!context.Found)
            return JsonHookResponse("{\"cancel\":false}");

        return JsonHookResponse(JsonSerializer.Serialize(new
        {
            cancel = false,
            contextModification = context.Context
        }));
    }

    private SdkContextHookResponse BuildKimiHookResponse(string rawInput)
    {
        if (DeserializeHookInput<KimiReadToolHookInput>(rawInput) is not KimiReadToolHookInput input)
            return TextHookResponse(string.Empty);

        if (input.ToolName != "ReadFile")
            return TextHookResponse(string.Empty);

        SdkContextBuildResult context = BuildContextForHookFile(input.ToolInput?.Path, string.Empty);
        return TextHookResponse(context.Context);
    }

    private SdkContextBuildResult BuildContextForHookFile(string? filePath, string? transcriptPath)
    {
        if (filePath is null ||
            !(filePath.EndsWith(".ts", StringComparison.Ordinal) || filePath.EndsWith(".code", StringComparison.Ordinal)))
            return new SdkContextBuildResult(false, string.Empty);

        return BuildContext(new SdkContextRequest(filePath, transcriptPath));
    }

    private string ResolveProjectRelativePath(string inputPath)
    {
        string normalized = WorkspacePath.ToUnixSeparator(inputPath);
        if (WorkspacePath.IsAbsolutePath(normalized))
        {
            string rootNormalized = WorkspacePath.ToUnixSeparator(_projectRootFullPath);
            if (normalized.StartsWith(rootNormalized + '/', StringComparison.Ordinal))
                return normalized[(rootNormalized.Length + 1)..];

            return string.Empty;
        }

        (string result, string? error) = WorkspacePath.TryRequireRelativePath(normalized);
        return error is null ? result : string.Empty;
    }

    private static T? DeserializeHookInput<T>(string rawInput)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(rawInput);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static SdkContextHookResponse JsonHookResponse(string body)
    {
        return new SdkContextHookResponse(body, "application/json; charset=utf-8");
    }

    private static SdkContextHookResponse TextHookResponse(string body)
    {
        return new SdkContextHookResponse(body, "text/plain; charset=utf-8");
    }

    private List<string> ResolveScriptPaths(string relativePath)
    {
        if (relativePath.StartsWith(ViewsPrefix, StringComparison.Ordinal) && relativePath.EndsWith(".ts", StringComparison.Ordinal))
        {
            string devicePath = relativePath[ViewsPrefix.Length..^".ts".Length];
            DeviceEntityBase? device = _services.Model.Find<DeviceEntityBase>(devicePath);
            if (device == null)
                return [];

            List<string> paths = [];
            foreach (ScriptGroupEntityBase group in device.ScriptGroups)
            {
                foreach (InlineScriptEntityBase script in group.Scripts)
                    paths.Add(script.实体_完整相对路径标识);

                foreach (ScriptEntryInfo entry in group.ScriptEntries)
                {
                    if (entry.IsCodeRepo && GuidText.Validate(entry.Guid))
                    {
                        CodeRepoScriptEntity? repo = _services.Model.FindByGuid<CodeRepoScriptEntity>(entry.Guid);
                        if (repo != null)
                            paths.Add(repo.实体_完整相对路径标识);
                    }
                }
            }
            return paths;
        }

        if (relativePath.EndsWith(".code", StringComparison.Ordinal))
            return [relativePath];

        return [];
    }

    private static string StripWhitespace(string text)
    {
        StringBuilder sb = new(text.Length);
        foreach (char c in text)
            if (!char.IsWhiteSpace(c))
                sb.Append(c);
        return sb.ToString();
    }

    private sealed record SdkContextBuildResult(bool Found, string Context);

    private sealed record ReadToolHookInput(
        [property: JsonPropertyName("tool_name")] string? ToolName,
        [property: JsonPropertyName("tool_input")] ReadToolInput? ToolInput,
        [property: JsonPropertyName("transcript_path")] string? TranscriptPath);

    private sealed record ReadToolInput(
        [property: JsonPropertyName("file_path")] string? FilePath);

    private sealed record KimiReadToolHookInput(
        [property: JsonPropertyName("tool_name")] string? ToolName,
        [property: JsonPropertyName("tool_input")] KimiReadToolInput? ToolInput);

    private sealed record KimiReadToolInput(
        [property: JsonPropertyName("path")] string? Path);

    private sealed record ClineHookInput(
        [property: JsonPropertyName("postToolUse")] ClinePostToolUse? PostToolUse);

    private sealed record ClinePostToolUse(
        [property: JsonPropertyName("toolName")] string? ToolName,
        [property: JsonPropertyName("parameters")] ClineToolParameters? Parameters);

    private sealed record ClineToolParameters(
        [property: JsonPropertyName("path")] string? Path);

    private sealed record CallTarget(BescriptCallKind Kind, string? OwnerType, string FunctionName);
}
