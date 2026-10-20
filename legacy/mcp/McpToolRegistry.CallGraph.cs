using System.Text.Json;

using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.TsView;

namespace Toolkit.Mcp;

/// <summary>
/// MCP 工具注册表 — CallGraph 工具执行逻辑
/// </summary>
public sealed partial class McpToolRegistry
{
    /// <summary>零件 TS 视图脚本行号索引（缓存用）</summary>
    private sealed record DeviceScriptViewIndex(
        string DeviceViewPath,
        Dictionary<string, (int StartLine, int EndLine)> ScriptRangesByPath
    );

    /// <summary>获取或构建零件 TS 视图脚本索引（带缓存）</summary>
    private DeviceScriptViewIndex? GetDeviceScriptViewIndex(
        string deviceRelativePath,
        Dictionary<string, DeviceScriptViewIndex?> cache)
    {
        if (cache.TryGetValue(deviceRelativePath, out DeviceScriptViewIndex? cached))
            return cached;

        DeviceTsView tool = _services.DeviceTsView;
        (DeviceTsViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = tool.Read(deviceRelativePath);
        if (errors.Count > 0 || data == null)
        {
            cache[deviceRelativePath] = null;
            return null;
        }

        Dictionary<string, (int StartLine, int EndLine)> scriptRangesByPath = new Dictionary<string, (int StartLine, int EndLine)>(StringComparer.Ordinal);
        foreach (DeviceTsViewScriptIndex scriptIndex in data.ScriptIndexes)
        {
            if (scriptIndex.SourceRelativePath is not { Length: > 0 })
                continue;
            scriptRangesByPath[scriptIndex.SourceRelativePath] = (scriptIndex.StartLine, scriptIndex.EndLine);
        }

        DeviceScriptViewIndex built = new DeviceScriptViewIndex(
            EntityViewPath.GetDeviceTsViewPath(deviceRelativePath),
            scriptRangesByPath);
        cache[deviceRelativePath] = built;
        return built;
    }

    /// <summary>构建脚本槽位的 TS 视图位置字符串</summary>
    private string BuildScriptSlotViewLocation(CallGraphNode node, Dictionary<string, DeviceScriptViewIndex?> cache)
    {
        string viewPath = EntityViewPath.GetDeviceTsViewPath(node.DevicePath);

        if (GetDeviceScriptViewIndex(node.DevicePath, cache) is not { } viewIndex)
            return viewPath;

        if (viewIndex.ScriptRangesByPath.TryGetValue(node.ScriptPath, out (int StartLine, int EndLine) span))
            return EntityViewPath.FormatViewLocation(viewPath, span.StartLine, span.EndLine);

        return viewPath;
    }

    /// <summary>判断脚本槽位是否为 coderepo 链接</summary>
    private static bool IsCodeRepoSlot(CallGraphNode node)
        => ScriptPathHelper.ExtractDevicePathFromSourcePath(node.ScriptPath) is null;

    private async Task<object?> HandleReadCallGraphMethodCalls(JsonElement? args, CancellationToken cancellationToken)
    {
        await MCPWriteLock.WaitAsync(cancellationToken);
        try
        {
            string? methodNameValue = GetStringProperty(args, "method_name");
            if (methodNameValue == null)
                return BuildResponse<object>(null, [new ToolError(null, null, null, "必须提供 method_name")], []);

            CallGraphBuilder builder = new(_services.Model, _services.Analysis);
            CallGraphReport report = builder.Build();

            List<CallGraphNode> matchedNodes = report.Nodes
                .Where(n => n.MethodName == methodNameValue)
                .ToList();

            if (matchedNodes.Count == 0)
                return BuildResponse<object>(null, [new ToolError(null, null, null, "未找到指定方法")], []);

            Dictionary<string, DeviceScriptViewIndex?> viewIndexCache = [];

            var methodDetails = matchedNodes.Select(node =>
            {
                List<string> potentialCallers = report.Edges
                    .Where(e => e.To == node)
                    .GroupBy(e => e.From.MethodName)
                    .OrderByDescending(g => g.Count())
                    .Select(g => $"{g.Key}({g.Count()}次)")
                    .ToList();

                List<string> potentialCallees = report.Edges
                    .Where(e => e.From == node)
                    .GroupBy(e => e.To.MethodName)
                    .OrderByDescending(g => g.Count())
                    .Select(g => $"{g.Key}({g.Count()}次)")
                    .ToList();

                bool isCodeRepo = IsCodeRepoSlot(node);
                string identifier = node.DevicePath + "/" + node.MethodName;
                if (isCodeRepo)
                    identifier += "(virtual)";
                string? coderepoFilePath = null;
                if (isCodeRepo)
                    coderepoFilePath = node.ScriptPath;
                string locationInView = BuildScriptSlotViewLocation(node, viewIndexCache);

                return new
                {
                    identifier,
                    coderepo_file_path = coderepoFilePath,
                    location_in_view = locationInView,
                    potential_callers = potentialCallers,
                    potential_callees = potentialCallees
                };
            }).ToList();

            var data = new
            {
                method = methodNameValue,
                matched_count = matchedNodes.Count,
                methods = methodDetails
            };

            return BuildResponse(data, [], []);
        }
        finally
        {
            MCPWriteLock.Release();
        }
    }

    private async Task<object?> HandleReadCallGraphDeadMethods(JsonElement? args, CancellationToken cancellationToken)
    {
        await MCPWriteLock.WaitAsync(cancellationToken);
        try
        {
            string? devicePathFilter = GetStringProperty(args, "device_path");
            int limit = GetIntProperty(args, "limit").GetValueOrDefault(50);

            CallGraphBuilder builder = new(_services.Model, _services.Analysis);
            CallGraphReport report = builder.Build();

            Dictionary<string, DeviceScriptViewIndex?> viewIndexCache = [];

            var potentialDeadMethods = report.DeadCodeCandidates
                .Where(n => devicePathFilter == null || n.DevicePath.StartsWith(devicePathFilter, StringComparison.Ordinal))
                .Take(Math.Min(limit, 200))
                .Select(n =>
                {
                    bool isCodeRepo = IsCodeRepoSlot(n);
                    string identifier = n.DevicePath + "/" + n.MethodName;
                    string? coderepoFilePath = null;
                    if (isCodeRepo)
                    {
                        identifier += "(virtual)";
                        coderepoFilePath = n.ScriptPath;
                    }
                    return new
                    {
                        identifier,
                        coderepo_file_path = coderepoFilePath,
                        location_in_view = BuildScriptSlotViewLocation(n, viewIndexCache)
                    };
                })
                .ToList();

            bool truncated = potentialDeadMethods.Count < report.DeadCodeCandidates.Count;

            var data = new
            {
                total_count = report.DeadCodeCandidates.Count,
                truncated,
                potential_dead_methods = potentialDeadMethods
            };

            return BuildResponse(data, [], []);
        }
        finally
        {
            MCPWriteLock.Release();
        }
    }

    private async Task<object?> HandleReadCallGraphEntryPoints(JsonElement? args, CancellationToken cancellationToken)
    {
        await MCPWriteLock.WaitAsync(cancellationToken);
        try
        {
            string? devicePathFilter = GetStringProperty(args, "device_path");
            int limit = GetIntProperty(args, "limit").GetValueOrDefault(20);

            CallGraphBuilder builder = new(_services.Model, _services.Analysis);
            CallGraphReport report = builder.Build();

            Dictionary<string, DeviceScriptViewIndex?> viewIndexCache = [];

            var entryPoints = report.EntryPoints
                .Where(ep => devicePathFilter == null || ep.Node.DevicePath.StartsWith(devicePathFilter, StringComparison.Ordinal))
                .Take(Math.Min(limit, 100))
                .Select(ep =>
                {
                    CallGraphNode n = ep.Node;
                    bool isCodeRepo = IsCodeRepoSlot(n);
                    string identifier = n.DevicePath + "/" + n.MethodName;
                    string? coderepoFilePath = null;
                    if (isCodeRepo)
                    {
                        identifier += "(virtual)";
                        coderepoFilePath = n.ScriptPath;
                    }
                    return new
                    {
                        identifier,
                        coderepo_file_path = coderepoFilePath,
                        location_in_view = BuildScriptSlotViewLocation(n, viewIndexCache),
                        type = ep.Type.ToString(),
                        trigger = ep.TriggerDescription
                    };
                })
                .ToList();

            bool truncated = entryPoints.Count < report.EntryPoints.Count;

            var data = new
            {
                total_count = report.EntryPoints.Count,
                truncated,
                entry_points = entryPoints
            };

            return BuildResponse(data, [], []);
        }
        finally
        {
            MCPWriteLock.Release();
        }
    }

    private async Task<object?> HandleReadCallGraphStats(JsonElement? args, CancellationToken cancellationToken)
    {
        await MCPWriteLock.WaitAsync(cancellationToken);
        try
        {
            CallGraphBuilder builder = new(_services.Model, _services.Analysis);
            CallGraphReport report = builder.Build();

            var stats = new
            {
                total_methods = report.Nodes.Count,
                total_edges = report.Edges.Count,
                entry_points = report.EntryPoints.Count,
                potential_dead_methods = report.DeadCodeCandidates.Count,
                static_edges = report.Statistics.StaticEdgeCount,
                dynamic_edges = report.Statistics.DynamicEdgeCount
            };

            return BuildResponse(stats, [], []);
        }
        finally
        {
            MCPWriteLock.Release();
        }
    }
}
