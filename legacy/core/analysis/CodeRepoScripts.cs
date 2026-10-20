using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Analysis;

/// <summary>
/// 脚本文件信息
/// </summary>
public sealed record ScriptFileInfo(
    string RelativePath,
    string[] NamespaceDirSegments,
    string NamespaceFileStem,
    string FileStem,
    string? Guid,
    string? SourceText,
    BescriptScriptInfo ScriptInfo
);

internal static class CodeRepoPath
{
    internal static string NormalizeForNamespace(string sourceRelativePath)
    {
        string namespaceRelativePath = WorkspacePath.RequireRelativePath(sourceRelativePath);

        if (namespaceRelativePath.EndsWith(ProjectEntityLayout.CodeGuidExt, StringComparison.Ordinal))
        {
            namespaceRelativePath = namespaceRelativePath[..^ProjectEntityLayout.CodeGuidExt.Length];
        }
        else if (namespaceRelativePath.EndsWith(ProjectEntityLayout.CodeExt, StringComparison.Ordinal))
        {
            namespaceRelativePath = namespaceRelativePath[..^ProjectEntityLayout.CodeExt.Length];
        }

        return namespaceRelativePath;
    }

}

/// <summary>
/// 代码仓库脚本扫描工具。
/// 基于 ProjectModel 实体系统构建脚本索引，为 View 层提供加工后的脚本数据。
/// 注意：此类无状态无缓存，缓存由 ProjectLogicContext 统一管理。
/// </summary>
internal static class CodeRepoScripts
{
    /// <summary>
    /// 从 ProjectModel 构建脚本文件索引（无缓存，实时解析每个脚本）。
    /// 单一入口：所有代码仓库脚本数据来自 ProjectModel 实体系统。
    /// 生产视图路径应优先用 <see cref="ScanOrCache"/> 走代际缓存；本方法保留给 override/测试等需要无缓存语义的路径。
    /// </summary>
    internal static List<ScriptFileInfo> Scan(ProjectModel model, List<ToolError> warnings)
    {
        IReadOnlyList<CodeRepoScriptEntity> scripts = model.ListCodeRepoScripts();
        if (scripts.Count == 0)
        {
            return [];
        }

        List<ScriptFileInfo> result = new List<ScriptFileInfo>(scripts.Count);
        foreach (CodeRepoScriptEntity entity in scripts)
        {
            BescriptScriptInfo scriptInfo = BescriptTextParser.Parse(entity.实体_完整相对路径标识, entity.SourceText).Script;
            if (BuildScriptFileInfo(entity, scriptInfo) is ScriptFileInfo info)
            {
                result.Add(info);
            }
        }
        return result.OrderBy(static s => s.RelativePath, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 与 <see cref="Scan"/> 相同，但脚本解析走 <paramref name="cache"/> 代际缓存：
    /// 同一轮刷新内同内容只解析一次，内容变化按指纹自动重建。
    /// 路径派生字段（命名空间/文件名/GUID）不在缓存范围——它们是路径纯函数。
    /// </summary>
    internal static List<ScriptFileInfo> ScanOrCache(ProjectModel model, ScriptAstCache cache, List<ToolError> warnings)
    {
        IReadOnlyList<CodeRepoScriptEntity> scripts = model.ListCodeRepoScripts();
        if (scripts.Count == 0)
        {
            return [];
        }

        List<ScriptFileInfo> result = new List<ScriptFileInfo>(scripts.Count);
        foreach (CodeRepoScriptEntity entity in scripts)
        {
            // 内容提供者：文本来自实体 SourceText，指纹用 FileIO.GetFileHash（走 FileSystemCache，零 IO）
            ScriptAstCache.Entry entry = cache.GetOrParse(entity.实体_完整相对路径标识,
                path => (entity.SourceText, Convert.ToHexString(FileIO.GetFileHash(path))));
            if (BuildScriptFileInfo(entity, entry.ScriptInfo) is ScriptFileInfo info)
            {
                result.Add(info);
            }
        }
        return result.OrderBy(static s => s.RelativePath, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 由实体属性派生 ScriptFileInfo（路径切分 + .guid 旁路文件），不含文本解析。
    /// </summary>
    private static ScriptFileInfo? BuildScriptFileInfo(CodeRepoScriptEntity entity, BescriptScriptInfo scriptInfo)
    {
        string scriptRelativePath = entity.实体_完整相对路径标识;
        string namespaceRelativePath = CodeRepoPath.NormalizeForNamespace(scriptRelativePath);
        string[] parts = namespaceRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        string namespaceFileStem = parts[^1];
        string[] namespaceDirSegs = [];
        if (parts.Length > 1)
            namespaceDirSegs = parts[..^1];

        return new ScriptFileInfo(
            scriptRelativePath,
            namespaceDirSegs,
            namespaceFileStem,
            entity.ScriptName,
            entity.Guid,
            entity.SourceText,
            scriptInfo);
    }

    // 注意：ScanWithAsyncNames 已移除。
    // async 解析是 tsview 层的职责，不应在 analysis 层处理。
    // 如需 async 名称解析，请在 tsview 层调用 TsViewScriptAsyncResolver.ResolveAsyncScripts。

    internal static Dictionary<string, T> BuildGuidMap<T>(IReadOnlyList<ScriptFileInfo> scripts, Func<ScriptFileInfo, T> selector)
    {
        Dictionary<string, T> map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (ScriptFileInfo script in scripts)
        {
            if (!GuidText.Validate(script.Guid))
            {
                continue;
            }

            map[script.Guid] = selector(script);
        }

        return map;
    }
}
