// 脚本分析服务 - Logic 层（逻辑/行为层）
// 职责：按需解析脚本获取 AST，管理解析缓存
// 禁止：直接文件 IO，持久化状态修改

using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Analysis;

/// <summary>
/// 脚本分析服务 - 管理脚本的 AST 解析。
///
/// 核心职责：
/// - 按需解析脚本获取 AST（不常驻内存）
/// - 解析结果缓存（短期）
/// - 提供统一的解析入口
///
/// 数据来源：
/// - 从 Persistence 层（ProjectModel）读取脚本实体获取源码
///
/// 禁止：
/// - 直接文件 IO（由 Persistence 层负责）
/// - 修改实体状态
/// </summary>
public sealed class ScriptAnalysisService
{
    private static readonly IReadOnlyDictionary<string, string> EmptySourceTextOverrides =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly ProjectModel _data;
    private readonly IReadOnlyDictionary<string, string> _sourceTextOverrides;

    // 解析缓存（路径 -> 解析结果）
    // 注意：缓存是短期的，在数据变更后应调用 ClearCache()
    private readonly Dictionary<string, CacheEntry> _parseCache;

    public ScriptAnalysisService(ProjectModel data)
        : this(data, EmptySourceTextOverrides)
    {
    }

    public ScriptAnalysisService(ProjectModel data, IReadOnlyDictionary<string, string> sourceTextOverrides)
    {
        _data = data;
        _sourceTextOverrides = ValidateAndNormalizeSourceOverrides(sourceTextOverrides);
        _parseCache = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// 清除解析缓存。
    /// 在 Persistence 层数据发生变更后调用。
    /// </summary>
    public void ClearCache()
    {
        _parseCache.Clear();
    }

    /// <summary>
    /// 解析指定路径的脚本获取 AST。
    /// 优先从缓存获取，缓存未命中则实时解析。
    /// </summary>
    /// <param name="scriptRelativePath">脚本相对路径</param>
    /// <returns>解析结果，如果脚本不存在或解析失败则返回 null</returns>
    public BescriptParseData? ParseScript(string scriptRelativePath)
    {
        string validatedScriptPath = WorkspacePath.RequireRelativePath(scriptRelativePath);

        // 检查缓存
        if (_parseCache.ContainsKey(validatedScriptPath))
        {
            return _parseCache[validatedScriptPath].Data;
        }

        // 尝试获取源码并解析
        string? sourceText = GetScriptSourceText(validatedScriptPath);
        if (sourceText == null)
        {
            return null;
        }

        // 实时解析
        (BescriptParseData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = BescriptParser.Parse(validatedScriptPath, sourceText);

        // 缓存结果（即使解析失败也缓存，避免重复解析错误）
        _parseCache[validatedScriptPath] = new CacheEntry(data, errors.Count > 0);

        return data;
    }


    /// <summary>
    /// 从实体获取脚本源码。
    /// 依次尝试内联脚本、代码仓库脚本。
    /// </summary>
    public string? GetScriptSourceText(string scriptRelativePath)
    {
        string validatedScriptPath = WorkspacePath.RequireRelativePath(scriptRelativePath);

        if (_sourceTextOverrides.TryGetValue(validatedScriptPath, out string? overrideSource))
        {
            return overrideSource;
        }

        // 尝试内联脚本
        if (_data.Find<InlineScriptEntity>(validatedScriptPath) is InlineScriptEntity inlineScript)
        {
            return inlineScript.SourceText;
        }

        // 尝试代码仓库脚本
        if (_data.Find<CodeRepoScriptEntity>(validatedScriptPath) is CodeRepoScriptEntity repoScript)
        {
            return repoScript.SourceText;
        }

        return null;
    }

    /// <summary>
    /// 缓存条目
    /// </summary>
    private sealed record CacheEntry(BescriptParseData? Data, bool HasErrors);

    private static IReadOnlyDictionary<string, string> ValidateAndNormalizeSourceOverrides(IReadOnlyDictionary<string, string> sourceTextOverrides)
    {
        if (sourceTextOverrides.Count == 0)
        {
            return EmptySourceTextOverrides;
        }

        Dictionary<string, string> normalized = new(StringComparer.Ordinal);
        foreach ((string path, string content) in sourceTextOverrides)
        {
            string relativePath = WorkspacePath.RequireRelativePath(path);
            if (!normalized.TryAdd(relativePath, content))
            {
                throw new InvalidOperationException($"脚本文本覆盖路径重复: {relativePath}");
            }
        }
        return normalized;
    }
}
