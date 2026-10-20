using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Search;

/// <summary>
/// guid.references.search 请求（逻辑层）
/// <see cref="Limit"/> 为已由调用方给出的生效值。
/// </summary>
public sealed record GuidReferencesSearchRequest(
    /// <summary>目标 GUID（32 位十六进制）</summary>
    string Guid,
    /// <summary>可选路径过滤，工程相对路径（精确匹配命中路径）</summary>
    string[]? Paths,
    /// <summary>最大结果数（正整数）</summary>
    int Limit
);

/// <summary>
/// GUID 引用命中条目。
/// </summary>
public sealed record GuidReferenceMatch(
    string Kind,
    string? ReferrerGuid,
    string Path,
    int Line,
    string Note
);

/// <summary>
/// guid.references.search 响应数据
/// </summary>
public sealed record GuidReferencesSearchData(
    IReadOnlyList<GuidReferenceMatch> References
);

/// <summary>
/// GUID 引用搜索工具（Logic 层入口）。
/// </summary>
public sealed class GuidReferencesSearch
{
    private readonly ProjectModel _model;

    public GuidReferencesSearch(ProjectModel model)
    {
        _model = model;
    }

    public (GuidReferencesSearchData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Search(
        GuidReferencesSearchRequest request)
    {
        if (!GuidText.Validate(request.Guid)) return Fail(GuidText.FormatErrorMessage("guid", request.Guid));

        if (request.Limit <= 0)
        {
            return Fail("limit 必须为正整数");
        }

        HashSet<string> pathFilter = new(StringComparer.Ordinal);
        if (request.Paths is { Length: > 0 } pathList)
        {
            try
            {
                foreach (string raw in pathList)
                {
                    _ = pathFilter.Add(WorkspacePath.RequireRelativePath(raw));
                }
            }
            catch (Exception ex)
            {
                return Fail($"路径参数无效: {ex.Message}");
            }
        }

        List<GuidReferenceResult> references = GuidReferenceSearchCore.SearchGuidReferences(_model, request.Guid, request.Limit);

        List<GuidReferenceMatch> matches = references
            .Where(r => pathFilter.Count == 0 || pathFilter.Contains(r.Path))
            .Select(r => new GuidReferenceMatch(r.Kind, r.ReferrerGuid, r.Path, r.Line, r.Note))
            .ToList();

        return (new GuidReferencesSearchData(matches), Array.Empty<ToolError>(), Array.Empty<ToolError>());
    }

    private static (GuidReferencesSearchData?, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>) Fail(string message)
        => RegexSearchCommon.Fail<GuidReferencesSearchData>(message);
}
