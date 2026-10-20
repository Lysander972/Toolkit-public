using System.Security.Cryptography;

using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Analysis;

/// <summary>
/// 脚本 AST 代际缓存（L3 analysis 层）。
///
/// 核心安全属性：命中判据与淘汰判据解耦。
/// - 命中判据 = <see cref="Entry.ContentHash"/>（内容指纹）：决定正确性。内容不变复用，内容变化指纹不符自动重建。
/// - 淘汰判据 = <see cref="Entry.LastHitGeneration"/>（最近命中代际）：只决定何时回收内存。
/// 代际推进（<see cref="AdvanceGeneration"/>）永远不影响命中正确性——这是设计的安全基石。
///
/// 一轮视图刷新 = 一代：轮内访问更新 LastHitGeneration，轮结束 <see cref="PruneToCurrentGeneration"/>
/// 淘汰未被复用的条目。代际推进由 <c>ProjectLogicContext.RebuildIndexes</c> 在 RefreshVersion 变更时驱动，
/// 淘汰由 <c>McpViewWorkspace</c> 重建轮阶段四驱动。
/// </summary>
internal sealed class ScriptAstCache
{
    /// <summary>
    /// 缓存条目。<see cref="LastHitGeneration"/> 可变（锁内写），其余只读。
    /// </summary>
    internal sealed class Entry
    {
        internal required string ContentHash { get; init; }
        internal required BescriptScriptInfo ScriptInfo { get; init; }
        internal long LastHitGeneration;
    }

    /// <summary>
    /// 内容提供者：返回 (脚本文本, 内容指纹 hex)。
    /// 指纹来源由调用方决定——CodeRepo 用 <c>FileIO.GetFileHash</c>（零 IO），inline 用 <see cref="ComputeTextHash"/>。
    /// 缓存自身不直接调 FileIO，避免与文件层耦合。契约：返回的 Text 非 null。
    /// </summary>
    internal delegate (string Text, string Hash) ContentProvider(string scriptRelativePath);

    private readonly Dictionary<string, Entry> _byPath = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private long _currentGeneration;

    /// <summary>
    /// 获取或解析脚本。
    /// 锁内查指纹命中（命中则 in-place 更新代际并返回），未命中则锁外解析（避免阻塞并发读者），
    /// 再锁内幂等覆盖写。并发同 path 未命中会各自解析后覆盖——幂等（相同输入→相同条目），无脏读。
    /// </summary>
    internal Entry GetOrParse(string scriptRelativePath, ContentProvider contentProvider)
    {
        (string text, string hash) = contentProvider(scriptRelativePath);

        lock (_lock)
        {
            if (_byPath.ContainsKey(scriptRelativePath))
            {
                Entry existing = _byPath[scriptRelativePath];
                if (string.Equals(existing.ContentHash, hash, StringComparison.Ordinal))
                {
                    existing.LastHitGeneration = _currentGeneration;
                    return existing;
                }
            }
        }

        Entry entry = new()
        {
            ContentHash = hash,
            ScriptInfo = BescriptTextParser.Parse(scriptRelativePath, text).Script,
            LastHitGeneration = _currentGeneration
        };

        lock (_lock)
        {
            _byPath[scriptRelativePath] = entry;
        }
        return entry;
    }

    /// <summary>
    /// 推进代际（替代整表清空）。不清条目——内容寻址保证正确性，代际只影响淘汰时机。
    /// 新版本号 &lt;= 当前时忽略（幂等，防重复调用）。
    /// </summary>
    internal void AdvanceGeneration(long newGeneration)
    {
        lock (_lock)
        {
            if (newGeneration > _currentGeneration)
                _currentGeneration = newGeneration;
        }
    }

    /// <summary>
    /// 淘汰未被当前代复用的条目（内存回收）。判据 LastHitGeneration &lt; 当前代。
    /// </summary>
    internal void PruneToCurrentGeneration()
    {
        lock (_lock)
        {
            List<string>? stale = null;
            foreach (KeyValuePair<string, Entry> kvp in _byPath)
            {
                if (kvp.Value.LastHitGeneration < _currentGeneration)
                    (stale ??= new List<string>()).Add(kvp.Key);
            }
            if (stale is not null)
            {
                foreach (string path in stale)
                    _byPath.Remove(path);
            }
        }
    }

    /// <summary>
    /// 文本 SHA256 指纹（hex）。inline 脚本用（无独立文件 hash，实体 SourceText 即源头）。
    /// 与 McpViewWorkspace.ComputeTextHash 同算法。
    /// </summary>
    internal static string ComputeTextHash(string text)
    {
        byte[] bytes = TextEncoding.Utf8.GetBytes(text);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
