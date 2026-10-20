using System.Collections.Concurrent;
using Toolkit.Core.Infra;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.TsView;

public sealed record TsViewLintResultItem(
    string Location,
    string Message,
    string? TsView
);

public sealed record TsViewLintResultsData(
    IReadOnlyList<TsViewLintResultItem> Items,
    bool Truncated
);

public sealed class LintTsviewResultsTool
{
    private readonly LintResultsTool _lintResultsTool;
    private readonly DeviceTsView _deviceTsView;
    private readonly ConcurrentDictionary<string, Dictionary<string, Dictionary<int, int>>> _lineMappingCache = new(StringComparer.Ordinal);

    public LintTsviewResultsTool(LintResultsTool lintResultsTool, DeviceTsView deviceTsView)
    {
        _lintResultsTool = lintResultsTool;
        _deviceTsView = deviceTsView;
    }

    public (TsViewLintResultsData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Query(
        LintResultsRequest request)
    {
        (LintQueryResult? queryResult, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = _lintResultsTool.Query(request);
        if (errors.Count > 0 || queryResult == null)
        {
            return (null, errors, warnings);
        }

        TsViewLintResultItem[] items = queryResult.Diagnostics
            .Select(d => new TsViewLintResultItem(
                Location: $"{d.Location.Path}:{d.Location.Line}",
                Message: $"[{d.Severity}] {d.RuleId}: {d.Message}",
                TsView: TryMapToTsView(d.Location.Path, d.Location.Line)
            ))
            .ToArray();

        return (new TsViewLintResultsData(items, queryResult.Truncated), Array.Empty<ToolError>(), warnings);
    }

    private string? TryMapToTsView(string sourceRelativePath, int sourceLine)
    {
        string validatedSourcePath = WorkspacePath.RequireRelativePath(sourceRelativePath);
        string? deviceRelativePath = ScriptPathHelper.ExtractDevicePathFromSourcePath(validatedSourcePath);

        if (deviceRelativePath == null)
        {
            if (!WorkspacePath.ContainsHiddenSegment(validatedSourcePath)
                && (validatedSourcePath.EndsWith(ProjectEntityLayout.CodeExt, StringComparison.Ordinal)
                    || validatedSourcePath.EndsWith(ProjectEntityLayout.CodeGuidExt, StringComparison.Ordinal)))
            {
                return ScriptPathHelper.CodeRepoViewPath;
            }

            return null;
        }

        string tsViewPath = EntityViewPath.GetDeviceTsViewPath(deviceRelativePath);
        int? tsViewLine = TryMapBesLineToTsViewLine(deviceRelativePath, validatedSourcePath, sourceLine);

        if (tsViewLine.HasValue)
            return $"{tsViewPath}:{tsViewLine.Value}";
        return tsViewPath;
    }

    private int? TryMapBesLineToTsViewLine(string deviceRelativePath, string sourceRelativePath, int sourceLine)
    {
        if (sourceLine <= 0)
            return null;

        string scriptStem = ScriptPathHelper.GetScriptStemFromSourcePath(sourceRelativePath);

        Dictionary<string, Dictionary<int, int>> deviceMapping = GetOrBuildDeviceLineMapping(deviceRelativePath);

        if (!deviceMapping.TryGetValue(scriptStem, out Dictionary<int, int>? scriptMapping))
            return null;

        if (scriptMapping.TryGetValue(sourceLine, out int tsLine))
            return tsLine;
        return null;
    }

    private Dictionary<string, Dictionary<int, int>> GetOrBuildDeviceLineMapping(string deviceRelativePath)
    {
        return _lineMappingCache.GetOrAdd(deviceRelativePath, path =>
        {
            (DeviceTsViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _deviceTsView.Read(path);
            if (errors.Count > 0)
                return new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);

            DeviceTsViewReadData readData = data ?? throw new InvalidOperationException("DeviceTsView.Read returned no data without errors");
            Dictionary<string, Dictionary<int, int>> result = new(StringComparer.Ordinal);
            foreach (DeviceTsViewScriptIndex index in readData.ScriptIndexes)
            {
#pragma warning disable TK022 // LineMap 是记录可选字段，null 表示无映射数据
                if (index.LineMap is not { Count: > 0 })
#pragma warning restore TK022
                    continue;

                Dictionary<int, int> lineMapping = [];
                foreach (TsViewLineMapping mapping in index.LineMap)
                    lineMapping[mapping.BesLine] = mapping.TsLine;

                result[index.ScriptName] = lineMapping;
            }

            return result;
        });
    }
}
