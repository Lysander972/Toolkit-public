using System.Text.Json;
using Toolkit.Core.Infra;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;
using Toolkit.Core.Refactor;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Mcp;

/// <summary>
/// MCP 工具注册表 — Read 工具执行逻辑
/// </summary>
public sealed partial class McpToolRegistry
{
    private Task<object?> HandleReadSdkDocs(JsonElement? args, CancellationToken _)
    {
        SdkDocsQueryTool tool = _services.SdkDocsQuery;
        string? mode = GetStringProperty(args, "mode");
        string? pattern = GetStringProperty(args, "pattern");
        string? path = GetStringProperty(args, "path");
        string? area = GetStringProperty(args, "area");
        string? nameKind = GetStringProperty(args, "name_kind");
        if (mode == null)
        {
            return Task.FromResult<object?>(BuildResponse<object>(null, [new ToolError(null, null, null, "必须提供 mode")], []));
        }

        SdkDocsQueryArgs request = new(mode, pattern, path, area, nameKind);
        (SdkDocsQueryData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = tool.Query(request);
        return Task.FromResult<object?>(BuildResponse(data, errors, warnings));
    }

    private Task<object?> HandleReadLint(JsonElement? args, CancellationToken _)
    {
        string? severity = GetStringProperty(args, "severity");
        bool? ignoreRecommended = GetBoolProperty(args, "ignore_recommended");
        int? start = GetIntProperty(args, "start");
        int? limit = GetIntProperty(args, "limit");
        LintResultsFilter? filter = null;
        if (args != null && args.Value.ValueKind == JsonValueKind.Object && args.Value.TryGetProperty("filter", out JsonElement filterEl))
        {
            filter = new LintResultsFilter(
                ExtractStringArray(filterEl, "paths"),
                ExtractStringArray(filterEl, "entity_guids"),
                ExtractStringArray(filterEl, "codes")
            );
        }
        LintResultsRequest request = new(severity, ignoreRecommended, filter, start.GetValueOrDefault(1), limit.GetValueOrDefault(100));
        (TsViewLintResultsData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = _services.LintTsviewResults.Query(request);
        // 获取 TS 类型检查诊断并合并
        object? tsDiagnostics = GetTscDiagnostics(timeoutMs: 10000, request.Start, request.Limit, request.Severity, request.Filter);
        object? result = data != null ? new LintMergedData(data.Items, data.Truncated, tsDiagnostics) : null;
        return Task.FromResult<object?>(BuildResponse(result, errors, warnings));
    }

    private Task<object?> HandleReadEntitiesByName(JsonElement? args, CancellationToken _)
    {
        string? namePattern = GetStringProperty(args, "name_pattern");
        if (namePattern == null)
        {
            return Task.FromResult<object?>(BuildResponse<object>(null, [new ToolError(null, null, null, "必须提供 name_pattern")], []));
        }

        EntitySearchArgs searchArgs = new(
            NamePattern: namePattern,
            EntityGuids: null,
            Kinds: GetStringArrayProperty(args, "kinds"),
            Limit: GetIntProperty(args, "limit").GetValueOrDefault(50)
        );

        return Task.FromResult<object?>(ExecuteEntitySearch(searchArgs, GetStringProperty(args, "detail")));
    }

    private Task<object?> HandleReadEntitiesByGuid(JsonElement? args, CancellationToken _)
    {
        string[]? entityGuids = GetStringArrayProperty(args, "entity_guids");
        if (entityGuids == null)
        {
            return Task.FromResult<object?>(BuildResponse<object>(null, [new ToolError(null, null, null, "必须提供 entity_guids")], []));
        }

        EntitySearchArgs searchArgs = new(
            NamePattern: null,
            EntityGuids: entityGuids,
            Kinds: null
        );

        return Task.FromResult<object?>(ExecuteEntitySearch(searchArgs, GetStringProperty(args, "detail")));
    }

    private Task<object?> HandleReadDeviceScriptOutline(JsonElement? args, CancellationToken _)
    {
        string? devicePath = GetStringProperty(args, "device_relative_path");
        if (devicePath == null)
        {
            return Task.FromResult<object?>(BuildResponse<object>(null, [new ToolError(null, null, null, "缺少 device_relative_path")], []));
        }

        ScriptReorder tool = _services.ScriptReorder;
        (DeviceScriptOutline? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = tool.ReadOutline(devicePath);
        return Task.FromResult<object?>(BuildResponse(data, errors, warnings));
    }

    private Task<object?> HandleReadPlayerLog(JsonElement? args, CancellationToken _)
    {
        ReadPlayerLogRequest request = Deserialize<ReadPlayerLogRequest>(args);
        (object? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = BuildReadPlayerLog(request);
        return Task.FromResult<object?>(BuildResponse(data, errors, warnings));
    }

    private (object? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) BuildReadPlayerLog(
        ReadPlayerLogRequest? request)
    {
        if (request == null || request.Mode.AsSpan().IsWhiteSpace())
        {
            return Fail<object>("缺少 mode");
        }
        if (!OperatingSystem.IsWindows())
        {
            return Fail<object>("仅 Windows 可用");
        }

        // 原版固定目录，不属于工程根目录，禁止走 FileIO（仅允许工程内相对路径）。
        string logPath = SystemIOWrapper.Combine(ToolkitUserPaths.DataDirFullPath, "Player.log");
        if (!SystemIOWrapper.FileExists(logPath))
        {
            return Fail<object>("Player.log 不存在");
        }

        string mode = request.Mode;
        if (mode.Equals("meta", StringComparison.Ordinal))
        {
            long fileSizeBytes = SystemIOWrapper.GetFileLength(logPath);
            int lineCount = 0;

            using (FileStream stream = SystemIOWrapper.OpenFileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(stream, TextEncoding.Utf8, detectEncodingFromByteOrderMarks: true))
            {
                while (reader.ReadLine() != null)
                {
                    lineCount++;
                }
            }

            return (new PlayerLogMetaData(fileSizeBytes, lineCount), Array.Empty<ToolError>(), Array.Empty<ToolError>());
        }

        if (mode.Equals("read", StringComparison.Ordinal))
        {
            if (request.StartLine == null || request.EndLine == null)
            {
                return Fail<object>("mode=read 必须提供 start_line/end_line");
            }

            int startLine = request.StartLine.Value;
            int endLine = request.EndLine.Value;
            if (startLine < 1 || endLine < 1 || startLine > endLine)
            {
                return Fail<object>("start_line/end_line 无效");
            }

            List<string> lines = new List<string>(Math.Min(4096, endLine - startLine + 1));
            int currentLine = 0;

            using (FileStream stream = SystemIOWrapper.OpenFileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(stream, TextEncoding.Utf8, detectEncodingFromByteOrderMarks: true))
            {
                while (true)
                {
                    string? line = reader.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    currentLine++;
                    if (currentLine < startLine)
                    {
                        continue;
                    }

                    if (currentLine > endLine)
                    {
                        break;
                    }

                    lines.Add(line);
                }
            }

            return (new PlayerLogReadData(lines), Array.Empty<ToolError>(), Array.Empty<ToolError>());
        }

        if (mode.Equals("search", StringComparison.Ordinal))
        {
            if (request.Keyword == null)
            {
                return Fail<object>("mode=search 必须提供 keyword");
            }

            string keyword = request.Keyword;
            List<string> allLines = new List<string>(4096);

            using (FileStream stream = SystemIOWrapper.OpenFileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(stream, TextEncoding.Utf8, detectEncodingFromByteOrderMarks: true))
            {
                while (true)
                {
                    string? line = reader.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    allLines.Add(line);
                }
            }

            List<PlayerLogSearchMatch> matches = [];
            for (int lineIndex = allLines.Count - 1; lineIndex >= 0; lineIndex--)
            {
                string line = allLines[lineIndex];
                if (!line.Contains(keyword, StringComparison.Ordinal))
                {
                    continue;
                }

                matches.Add(new PlayerLogSearchMatch(lineIndex + 1, line));
            }

            return (new PlayerLogSearchData(matches), Array.Empty<ToolError>(), Array.Empty<ToolError>());
        }

        return Fail<object>($"未知的 mode: {mode}（允许的值: meta/read/search，大小写敏感，必须完全匹配）");
    }

    /// <summary>
    /// 执行实体查询的共享逻辑
    /// </summary>
    private object? ExecuteEntitySearch(EntitySearchArgs searchArgs, string? detail)
    {
        (EntitySearchResponse? searchData, IReadOnlyList<ToolError> searchErrors, IReadOnlyList<ToolError> searchWarnings) = _services.EntitySearch.Search(searchArgs);

        if (searchData == null || searchErrors.Count > 0)
        {
            return BuildResponse(searchData, searchErrors, searchWarnings);
        }

        // full 模式：获取详细检查信息
        if (detail == "full")
        {
            const int fullModeLimit = 20;
            List<ToolError> warningsList = searchWarnings.ToList();
            string[] guids = searchData.Entities
                .Select(e => e.Guid)
                .Where(g => g is not null && GuidText.Validate(g))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .Take(fullModeLimit)
                .ToArray();

            if (searchData.Entities.Count > fullModeLimit)
            {
                warningsList.Add(new ToolError(null, null, null,
                    $"full 模式仅返回前 {fullModeLimit} 个实体的详情，共 {searchData.Entities.Count} 个匹配",
                    ToolDiagnosticSeverity.Warning));
            }

            if (guids.Length == 0)
            {
                return BuildResponse(new SearchPayload(searchData.Entities), Array.Empty<ToolError>(), warningsList);
            }

            EntityInspectArgs inspectArgs = new EntityInspectArgs(
                EntityGuids: guids,
                ScopeLevel: "entity",
                Include: new EntityInspectInclude(Graph: true, Events: false, DataView: true, KeyOrigins: false)
            );

            (EntityInspectData? inspectData, IReadOnlyList<ToolError> inspectErrors, IReadOnlyList<ToolError> inspectWarnings) = _services.EntityInspect.Inspect(inspectArgs);

            if (inspectErrors.Count > 0 || inspectData == null)
            {
                return BuildResponse<object>(null, inspectErrors, inspectWarnings);
            }

            warningsList.AddRange(inspectWarnings);
            return BuildResponse(inspectData, Array.Empty<ToolError>(), warningsList);
        }

        return BuildResponse(new SearchPayload(searchData.Entities), searchErrors, searchWarnings);
    }
}
