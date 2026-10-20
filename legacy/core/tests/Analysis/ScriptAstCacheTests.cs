using Toolkit.Core.Tests.Common;
using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Analysis;

public sealed class ScriptAstCacheTests
{
    private const string SampleBescript =
        "begincodeV2\nname:test\nlines:\nfun create.float(Float 1)\nendcode";

    /// <summary>固定 (text, hash) 的内容提供者，忽略 path 参数（测试用）。</summary>
    private static ScriptAstCache.ContentProvider Provider(string text, string hash) => _ => (text, hash);

    [Fact]
    public void Cache_Reuses_When_Content_Unchanged()
    {
        ScriptAstCache cache = new();
        ScriptAstCache.ContentProvider p = Provider(SampleBescript, "hash1");

        ScriptAstCache.Entry e1 = cache.GetOrParse("a.code", p);
        ScriptAstCache.Entry e2 = cache.GetOrParse("a.code", p);

        Assert.Same(e1, e2);
    }

    [Fact]
    public void Cache_Rebuilds_When_Content_Changes()
    {
        ScriptAstCache cache = new();
        ScriptAstCache.ContentProvider p1 = Provider(SampleBescript, "hash1");
        ScriptAstCache.ContentProvider p2 = Provider(SampleBescript + "\nfun create.long(Long 1)", "hash2");

        ScriptAstCache.Entry e1 = cache.GetOrParse("a.code", p1);
        ScriptAstCache.Entry e2 = cache.GetOrParse("a.code", p2);

        Assert.NotSame(e1, e2);
        Assert.NotSame(e1.ScriptInfo, e2.ScriptInfo);
    }

    /// <summary>核心安全属性：代际推进不清表，内容未变仍命中（正确性只取决于内容指纹）。</summary>
    [Fact]
    public void AdvanceGeneration_DoesNotInvalidate_HitByContent()
    {
        ScriptAstCache cache = new();
        ScriptAstCache.ContentProvider p = Provider(SampleBescript, "hash1");
        ScriptAstCache.Entry e1 = cache.GetOrParse("a.code", p);

        cache.AdvanceGeneration(99);

        ScriptAstCache.Entry e2 = cache.GetOrParse("a.code", p);
        Assert.Same(e1, e2);
    }

    [Fact]
    public void Prune_Removes_StaleGeneration()
    {
        ScriptAstCache cache = new();
        cache.GetOrParse("a.code", Provider(SampleBescript, "hash1")); // gen 0

        cache.AdvanceGeneration(1);
        // 本轮（gen 1）不访问 "a"
        cache.PruneToCurrentGeneration();

        // "a" 已被淘汰，下次访问重新解析（新 Entry，属于 gen 1）
        ScriptAstCache.Entry e = cache.GetOrParse("a.code", Provider(SampleBescript, "hash1"));
        Assert.Equal(1, e.LastHitGeneration);
    }

    [Fact]
    public void Prune_KeepsCurrentGeneration()
    {
        ScriptAstCache cache = new();
        ScriptAstCache.ContentProvider p = Provider(SampleBescript, "hash1");
        ScriptAstCache.Entry e1 = cache.GetOrParse("a.code", p); // gen 0

        cache.AdvanceGeneration(1);
        ScriptAstCache.Entry e2 = cache.GetOrParse("a.code", p); // 命中，提升到 gen 1
        Assert.Same(e1, e2);
        cache.PruneToCurrentGeneration();

        ScriptAstCache.Entry e3 = cache.GetOrParse("a.code", p); // gen 1 被保留，仍命中
        Assert.Same(e2, e3);
    }

    [Fact]
    public async Task Concurrent_GetOrParse_IsThreadSafe()
    {
        ScriptAstCache cache = new();
        ScriptAstCache.ContentProvider p = Provider(SampleBescript, "hash1");

        ScriptAstCache.Entry[] results = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(() => cache.GetOrParse("a.code", p))));

        // 并发不崩；幂等覆盖，所有结果指纹一致
        foreach (ScriptAstCache.Entry e in results)
            Assert.Equal("hash1", e.ContentHash);
    }
}

/// <summary>
/// CodeRepoScripts.ScanOrCache 真实指纹路径 —— 临时工程集成测试。
/// 与内存单测（假 hash Provider）互补：验证 FileIO.GetFileHash 指纹在真实工程重载间
/// 内容不变复用解析结果、内容变化自动重建。
/// </summary>
[Collection("WorkspaceState")]
public sealed class ScriptAstCacheIntegrationTests : TempDirectoryTestFixture
{
    private const string RepoGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    public ScriptAstCacheIntegrationTests() : base("Toolkit.Core.Tests.ScriptAstCacheIntegration")
    {
    }

    private ProjectModel LoadWithCodeRepo(string sourceText)
    {
        ScriptGroupTestProject.CreateBaseProject(TempDir);
        ScriptGroupTestProject.WriteCodeRepoScript(TempDir, "Foo", RepoGuid, sourceText);
        return TestWorkspace.LoadProject(TempDir);
    }

    [Fact]
    public void ScanOrCache_Reuses_Parse_When_Content_Unchanged()
    {
        const string source = "begincodeV2\nname:Foo\nlines:\n//foo\nendcode\n";
        ScriptAstCache cache = new();

        List<ScriptFileInfo> s1 = CodeRepoScripts.ScanOrCache(LoadWithCodeRepo(source), cache, []);
        List<ScriptFileInfo> s2 = CodeRepoScripts.ScanOrCache(TestWorkspace.LoadProject(TempDir), cache, []);

        Assert.Single(s1);
        Assert.Single(s2);
        Assert.Same(s1[0].ScriptInfo, s2[0].ScriptInfo);
    }

    [Fact]
    public void ScanOrCache_Rebuilds_When_Content_Changes_Across_Reload()
    {
        ScriptAstCache cache = new();
        List<ScriptFileInfo> s1 = CodeRepoScripts.ScanOrCache(
            LoadWithCodeRepo("begincodeV2\nname:Foo\nlines:\n//v1\nendcode\n"), cache, []);

        // 跨模型重载修改文件：指纹变化必须击穿缓存（即使路径相同）
        ScriptGroupTestProject.WriteCodeRepoScript(TempDir, "Foo", RepoGuid,
            "begincodeV2\nname:Foo\nlines:\n//v2\nendcode\n");
        List<ScriptFileInfo> s2 = CodeRepoScripts.ScanOrCache(TestWorkspace.LoadProject(TempDir), cache, []);

        Assert.Single(s2);
        Assert.NotSame(s1[0].ScriptInfo, s2[0].ScriptInfo);
    }
}
