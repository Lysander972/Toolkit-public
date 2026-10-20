using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Lint;

/// <summary>
/// lint.results 请求
/// </summary>
public sealed record LintResultsRequest(
    string? Severity,
    bool? IgnoreRecommended,
    LintResultsFilter? Filter,
    int Start = 1,
    int Limit = 100
);

/// <summary>
/// lint.results 过滤条件
/// </summary>
public sealed record LintResultsFilter(
    string[]? Paths,
    string[]? EntityGuids,
    string[]? Codes
);

public sealed record LintQueryResult(
    IReadOnlyList<LintDiagnostic> Diagnostics,
    bool Truncated
);

/// <summary>
/// Lint 结果查询工具
/// </summary>
public sealed class LintResultsTool
{
    private const int MaxReturnItems = 100;
    private readonly ProjectModel _model;

    public LintResultsTool(ProjectModel model)
    {
        _model = model;
    }

    public (LintQueryResult? Result, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Query(
        LintResultsRequest request)
    {
        if (request.Start is <= 0)
        {
            return (null,
                new[] { new ToolError(null, null, null, "start 必须为 >= 1") },
                Array.Empty<ToolError>());
        }

        if (request.Limit is <= 0)
        {
            return (null,
                new[] { new ToolError(null, null, null, "limit 必须为 >= 1") },
                Array.Empty<ToolError>());
        }

        LintSeverity? severityFilter = null;
        if (!string.IsNullOrWhiteSpace(request.Severity))
        {
            (LintSeverity? parsed, string? severityError) = ParseSeverity(request.Severity);
            if (severityError != null)
            {
                return (null,
                    new[] { new ToolError(null, null, null, severityError) },
                    Array.Empty<ToolError>());
            }
            severityFilter = parsed;
        }

        IReadOnlyList<LintDiagnostic> diagnostics;
        _model.EnterReadLock();
        try
        {
            ScriptAnalysisService analysisService = new(_model);
            diagnostics = LintEngine.AnalyzeProject(_model, analysisService);
        }
        finally
        {
            _model.ExitReadLock();
        }

        List<LintDiagnostic> filtered = diagnostics
            .Where(d => !severityFilter.HasValue || d.Severity == severityFilter.Value)
            .Where(d => request.Filter?.Paths is not { Length: > 0 } || request.Filter.Paths.Any(p => d.Location.Path.Contains(p, StringComparison.Ordinal)))
            .Where(d => request.Filter?.Codes is not { Length: > 0 } || new HashSet<string>(request.Filter.Codes, StringComparer.Ordinal).Contains(d.RuleId))
            .ToList();

        int filteredCount = filtered.Count;
        int start = request.Start;
        int limit = Math.Min(request.Limit, MaxReturnItems);
        int skipCount = start - 1;

        List<LintDiagnostic> paged = filtered.Skip(skipCount).Take(limit).ToList();
        bool truncated = skipCount + paged.Count < filteredCount;

        return (new LintQueryResult(paged, truncated), Array.Empty<ToolError>(), Array.Empty<ToolError>());
    }

    private static (LintSeverity? Value, string? Error) ParseSeverity(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
            return (null, null);

        return severity switch
        {
            "error" => (LintSeverity.Error, null),
            "warning" => (LintSeverity.Warning, null),
            "info" => (LintSeverity.Info, null),
            _ => (null, $"未知的 severity: {severity}（允许的值: error/warning/info，大小写敏感，必须完全匹配）")
        };
    }
}
