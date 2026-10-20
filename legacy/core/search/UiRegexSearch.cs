using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Search;

/// <summary>
/// ui.regex.search 请求（逻辑层）
/// 限额与 CaseSensitive 为已由调用方给出的生效值。
/// </summary>
public sealed record UiRegexSearchRequest(
    /// <summary>ECMAScript 正则</summary>
    string Pattern,
    /// <summary>为 <c>true</c> 时区分大小写；否则忽略大小写（由调用方/MCP 将缺省规范化为 <c>false</c>）。</summary>
    bool CaseSensitive,
    /// <summary>限定 UI GUID</summary>
    string[]? UiGuids,
    /// <summary>限定路径范围</summary>
    string[]? Paths,
    /// <summary>每个视图最大命中数</summary>
    int LimitPerView,
    /// <summary>总命中上限</summary>
    int MaxMatches
);

/// <summary>
/// UI 文本匹配结果
/// </summary>
public sealed record UiTextMatch(
    /// <summary>UI GUID</summary>
    string? UiGuid,
    /// <summary>命中路径</summary>
    string Path,
    /// <summary>节点路径</summary>
    string? NodePath,
    /// <summary>命中行号（1 起计）</summary>
    int Line,
    /// <summary>命中片段</summary>
    string Snippet
);

/// <summary>
/// ui.regex.search 响应数据
/// </summary>
public sealed record UiRegexSearchData(
    IReadOnlyList<UiTextMatch> Matches
);

/// <summary>
/// UI 正则搜索
/// 搜索 .canvas 文件中的文本内容
/// </summary>
public sealed class UiRegexSearch
{
    private readonly ProjectModel _model;

    public UiRegexSearch(ProjectModel model)
    {
        _model = model;
    }

    public (UiRegexSearchData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Search(
        UiRegexSearchRequest request)
    {
        Regex regex;
        try
        {
            regex = RegexSearchCommon.CreateRegex(request.Pattern, request.CaseSensitive);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }

        List<UiTextMatch> matches = [];
        List<ToolError> warnings = [];
        HashSet<string> uiGuidSet = RegexSearchCommon.CreateGuidSet(request.UiGuids);
        string[] pathFilters = RegexSearchCommon.NormalizePathFilters(request.Paths);

        // 通过 ProjectModel 获取所有 Canvas 实体（使用统一查询 API）
        IEnumerable<CanvasEntity> canvasEntities = RegexSearchCommon.FilterByPathPrefix(
            _model.List<CanvasEntity>(),
            pathFilters,
            static entity => entity.实体_完整相对路径标识);

        foreach (CanvasEntity canvasEntity in canvasEntities)
        {
            if (matches.Count >= request.MaxMatches) break;

            string relativePath = canvasEntity.实体_完整相对路径标识;
            string? uiGuid = canvasEntity.Guid;
            if (!RegexSearchCommon.IsGuidAllowed(uiGuid, uiGuidSet))
            {
                continue;
            }

            // Canvas 数据已通过 ProjectModel 加载
            if (canvasEntity.Data.Length == 0)
            {
                warnings.Add(new ToolError(relativePath, null, null, "canvas 数据为空", ToolDiagnosticSeverity.Warning));
                continue;
            }

            // 直接从二进制转换为 HTML 视图进行搜索
            string html = CanvasConverter.BinaryToHtml(canvasEntity.Data);

            IEnumerable<UiTextMatch> fileMatches = SearchInHtml(html, relativePath, uiGuid, regex, request.LimitPerView);
            RegexSearchCommon.AddUntilLimit(matches, fileMatches, request.MaxMatches);
        }

        return (new UiRegexSearchData(matches), Array.Empty<ToolError>(), warnings);
    }

    private static IEnumerable<UiTextMatch> SearchInHtml(
        string html,
        string relativePath,
        string? uiGuid,
        Regex regex,
        int limitPerView)
    {
        IReadOnlyList<(int Line, string Name)> nodeNamesByLine = ReadNodeNamesByLine(html);
        return RegexSearchCommon.SearchLines(html, regex, limitPerView,
            (lineText, line, _, snippet) => new UiTextMatch(
                UiGuid: uiGuid,
                Path: relativePath,
                NodePath: FindNodeNameAtLine(nodeNamesByLine, line),
                Line: line,
                Snippet: snippet
            ));
    }

    private static IReadOnlyList<(int Line, string Name)> ReadNodeNamesByLine(string html)
    {
        XDocument document = XDocument.Parse(html, LoadOptions.SetLineInfo);
        List<(int Line, string Name)> nodes = [];
        foreach (XElement element in document.Descendants())
        {
            XAttribute? nameAttribute = element.Attribute("name");
            if (nameAttribute == null)
                continue;

            IXmlLineInfo lineInfo = element;
            if (lineInfo.HasLineInfo())
                nodes.Add((lineInfo.LineNumber, nameAttribute.Value));
        }

        return nodes;
    }

    private static string? FindNodeNameAtLine(IReadOnlyList<(int Line, string Name)> nodeNamesByLine, int line)
    {
        string? name = null;
        foreach ((int nodeLine, string nodeName) in nodeNamesByLine)
        {
            if (nodeLine > line)
                break;

            name = nodeName;
        }

        return name;
    }

    private static (UiRegexSearchData?, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>) Fail(string message)
        => RegexSearchCommon.Fail<UiRegexSearchData>(message);
}
