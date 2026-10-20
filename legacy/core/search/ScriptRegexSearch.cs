using System.Text.RegularExpressions;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Search;

/// <summary>
/// script.regex.search 请求（逻辑层）
/// 限额与 CaseSensitive 为已由调用方给出的生效值。
/// </summary>
public sealed record ScriptRegexSearchRequest(
    /// <summary>ECMAScript 正则</summary>
    string Pattern,
    /// <summary>为 <c>true</c> 时区分大小写；否则忽略大小写（由调用方/MCP 将缺省规范化为 <c>false</c>）。</summary>
    bool CaseSensitive,
    /// <summary>限定实体范围</summary>
    string[]? EntityGuids,
    /// <summary>限定脚本 GUID</summary>
    string[]? ScriptGuids,
    /// <summary>限定路径范围</summary>
    string[]? Paths,
    /// <summary>每个脚本最大命中数</summary>
    int LimitPerScript,
    /// <summary>总命中上限</summary>
    int MaxMatches
);

/// <summary>
/// 文本匹配结果
/// </summary>
public sealed record TextMatch(
    /// <summary>脚本 GUID</summary>
    string? ScriptGuid,
    /// <summary>命中路径</summary>
    string Path,
    /// <summary>行号</summary>
    int Line,
    /// <summary>列号（1 起计）</summary>
    int Column,
    /// <summary>命中片段</summary>
    string Snippet
);

/// <summary>
/// script.regex.search 响应数据
/// </summary>
public sealed record ScriptRegexSearchData(
    IReadOnlyList<TextMatch> Matches
);

/// <summary>
/// 脚本正则搜索
/// </summary>
public sealed class ScriptRegexSearch
{
    private readonly ProjectModel _model;

    public ScriptRegexSearch(ProjectModel model)
    {
        _model = model;
    }

    public (ScriptRegexSearchData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Search(
        ScriptRegexSearchRequest request)
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

        List<TextMatch> matches = [];
        HashSet<string> scriptGuidSet = RegexSearchCommon.CreateGuidSet(request.ScriptGuids);
        string[] pathFilters = RegexSearchCommon.NormalizePathFilters(request.Paths);

        // 通过 ProjectModel 获取所有脚本实体（使用统一查询 API）
        IEnumerable<(InlineScriptEntity Script, string RelativePath, string? ScriptGuid)> scripts = CollectScriptEntities(pathFilters, request.EntityGuids);

        foreach ((InlineScriptEntity script, string relativePath, string? scriptGuid) in scripts)
        {
            if (matches.Count >= request.MaxMatches)
            {
                break;
            }

            string content = script.SourceText;

            if (!RegexSearchCommon.IsGuidAllowed(scriptGuid, scriptGuidSet))
            {
                continue;
            }

            IEnumerable<TextMatch> fileMatches = SearchInFile(content, relativePath, scriptGuid, regex, request.LimitPerScript);
            RegexSearchCommon.AddUntilLimit(matches, fileMatches, request.MaxMatches);
        }

        return (new ScriptRegexSearchData(matches), Array.Empty<ToolError>(), Array.Empty<ToolError>());
    }

    /// <summary>
    /// 收集脚本实体（使用 ProjectModel 统一查询 API）
    /// </summary>
    private IEnumerable<(InlineScriptEntity Script, string RelativePath, string? ScriptGuid)> CollectScriptEntities(
        string[] pathFilters,
        string[]? entityGuids)
    {
        // 获取所有内联脚本
        IEnumerable<InlineScriptEntity> inlineScripts = RegexSearchCommon.FilterByPathPrefix(
            _model.List<InlineScriptEntity>(),
            pathFilters,
            static script => script.实体_完整相对路径标识);

        foreach (InlineScriptEntity script in inlineScripts)
        {
            // 内联脚本没有 GUID，尝试从所属实体获取
            string scriptPath = script.实体_完整相对路径标识;
            string? scriptGuid = null;
            if (_model.Find<CodeRepoScriptEntity>(scriptPath) is CodeRepoScriptEntity codeRepoScript)
                scriptGuid = codeRepoScript.Guid;
            yield return (script, scriptPath, scriptGuid);
        }
    }

    private static IEnumerable<TextMatch> SearchInFile(
        string content,
        string relativePath,
        string? scriptGuid,
        Regex regex,
        int limitPerScript)
    {
        return RegexSearchCommon.SearchLines(content, regex, limitPerScript,
            (_, line, column, snippet) => new TextMatch(
                ScriptGuid: scriptGuid,
                Path: relativePath,
                Line: line,
                Column: column,
                Snippet: snippet
            ));
    }

    private static (ScriptRegexSearchData?, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>) Fail(string message)
        => RegexSearchCommon.Fail<ScriptRegexSearchData>(message);
}
