using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

/// <summary>
/// SaveChangesIfVersionMatches 版本守卫测试。
/// 模拟视图重建期间模型被并发刷新的场景：
/// - FileSystemCache 定时器触发刷新 → RefreshVersion 递增
/// - MCP 写操作触发刷新 → 实体版本号变化
/// 验证守卫方法优雅返回而非抛出致命异常。
/// </summary>
[Collection("FileIO")]
public sealed class SaveChangesVersionGuardTests
{
    /// <summary>
    /// 场景：FileSystemCache 定时器在 writable 创建和 SaveChanges 之间触发模型刷新，
    /// RefreshVersion 递增。调用方持有的 expectedRefreshVersion 已过期。
    /// 预期：返回 (empty, false)，不触发 Persist。
    /// </summary>
    [Fact]
    public void Guard_Returns_False_When_RefreshVersion_Diverges()
    {
        using ProjectModel model = ProjectModel.CreateForTesting();

        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        DirectoryEntity dir = TestEntityFactory.CreateDirectory(root, "temp");
        model.SetRootForTesting(root);

        WritableDirectoryEntity writable = dir.ToWritable();

        // 模拟：调用方在 writable 创建后记录了 RefreshVersion=0，
        // 但刷新已将 _refreshVersion 推进到 1。
        // 通过传一个过期的 expectedRefreshVersion 来模拟。
        long staleExpectedVersion = model.RefreshVersion + 1;

        (IReadOnlyList<string> affected, bool matched) =
            model.SaveChangesIfVersionMatches(writable, staleExpectedVersion);

        Assert.False(matched);
        Assert.Empty(affected);
    }

    /// <summary>
    /// 场景：模型刷新在 writable 创建后改变了同路径只读实体的版本号
    /// （DirectoryEntity.LoadSelf 每次调用 IncrementVersion）。
    /// writable 仍持有旧版本号。
    /// 预期：版本不匹配，返回 (empty, false)。
    /// </summary>
    [Fact]
    public void Guard_Returns_False_When_Entity_Version_Diverges()
    {
        using ProjectModel model = ProjectModel.CreateForTesting();

        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        DirectoryEntity dir = TestEntityFactory.CreateDirectory(root, "temp");
        model.SetRootForTesting(root);

        string originalVersion = dir.Version;
        WritableDirectoryEntity writable = dir.ToWritable();
        Assert.Equal(originalVersion, writable.Version);

        // 模拟：模型刷新，DirectoryEntity.LoadSelf 递增了版本号
        dir.Version = "B";

        long refreshVersion = model.RefreshVersion;
        (IReadOnlyList<string> affected, bool matched) =
            model.SaveChangesIfVersionMatches(writable, refreshVersion);

        Assert.False(matched);
        Assert.Empty(affected);
    }

    /// <summary>
    /// 场景：RefreshVersion 和实体版本同时不匹配。
    /// 模拟定时器刷新 + 写操作的双重并发。
    /// 预期：RefreshVersion 检查先触发，直接返回 (empty, false)。
    /// </summary>
    [Fact]
    public void Guard_Returns_False_When_Both_Versions_Diverge()
    {
        using ProjectModel model = ProjectModel.CreateForTesting();

        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        DirectoryEntity dir = TestEntityFactory.CreateDirectory(root, "temp");
        model.SetRootForTesting(root);

        WritableDirectoryEntity writable = dir.ToWritable();

        dir.Version = "C";
        long staleVersion = model.RefreshVersion + 5;

        (IReadOnlyList<string> affected, bool matched) =
            model.SaveChangesIfVersionMatches(writable, staleVersion);

        Assert.False(matched);
        Assert.Empty(affected);
    }

    /// <summary>
    /// 基线验证：ProjectPersister.Persist 在版本不匹配时抛出 InvalidOperationException。
    /// 这正是 SaveChangesIfVersionMatches 要拦截的致命异常。
    /// </summary>
    [Fact]
    public void Persister_Throws_On_Version_Mismatch_Without_Guard()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        DirectoryEntity dir = TestEntityFactory.CreateDirectory(root, "temp");

        WritableDirectoryEntity writable = dir.ToWritable();
        writable.Version = "A";

        DirectoryEntity current = new DirectoryEntity("temp")
        {
            Parent = root,
            Version = "B"
        };

        bool getByPathCalled = false;
        ProjectPersister persister = new(_ =>
        {
            getByPathCalled = true;
            return current;
        }, new PhysicalPersistenceOperationApplier());

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => persister.Persist(writable, new HashSet<string>(StringComparer.Ordinal)));

        Assert.Contains("版本号冲突", ex.Message);
        Assert.True(getByPathCalled);
    }

    /// <summary>
    /// 验证：版本完全匹配时守卫不会误拦截。
    /// 无法直接调用 SaveChangesIfVersionMatches 验证"通过"（Persist 需要真实文件系统），
    /// 但可以验证守卫的前置条件：Find 能找到路径匹配的只读实体且版本一致。
    /// </summary>
    [Fact]
    public void Guard_Precondition_Versions_Match_After_ToWritable()
    {
        using ProjectModel model = ProjectModel.CreateForTesting();

        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        DirectoryEntity dir = TestEntityFactory.CreateDirectory(root, "temp");
        model.SetRootForTesting(root);

        WritableDirectoryEntity writable = dir.ToWritable();

        // ToWritable 后，writable 和只读实体的版本应该一致
        Assert.Equal(dir.Version, writable.Version);

        // Find 能找到同路径的只读实体
        ProjectEntity? found = model.Find(writable.实体_完整相对路径标识);
        Assert.NotNull(found);
        Assert.Equal(writable.Version, found.Version);
    }
}
