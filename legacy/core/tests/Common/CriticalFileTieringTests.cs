// 支柱一：占据分级（静态关键集合前置阻断）测试
// - 关键文件占据 → 全局受阻（前置阻断）
// - 非关键文件占据 → 容错（快照占位/旧数据继续，不进入受阻）
// - 写/删目标占据 → 严格避让（即使非关键）

using System.Diagnostics;
using Toolkit.Core.Infra;
using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

[Collection("FileIO")]
public sealed class CriticalFileTieringTests : TempDirectoryTestFixture
{
    public CriticalFileTieringTests() : base("Toolkit.Core.Tests.CriticalFileTiering")
    {
    }

    [Fact]
    public void NonCritical_Occupied_Should_Not_Block_Workspace()
    {
        CreateMinimalProject();
        string occupiedFullPath = SystemIOWrapper.Combine(TempDir, "notes.txt");
        SystemIOWrapper.WriteAllText(occupiedFullPath, "temp-note");

        using FileStream fileLock = SystemIOWrapper.OpenFileStream(occupiedFullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        Assert.False(model.IsWorkspaceBlocked);
        model.Dispose();
    }

    [Fact]
    public void NonCritical_Occupied_Release_Should_Recover_Tolerated()
    {
        CreateMinimalProject();
        string occupiedFullPath = SystemIOWrapper.Combine(TempDir, "notes.txt");
        SystemIOWrapper.WriteAllText(occupiedFullPath, "temp-note-v1");

        using ProjectModel model = TestWorkspace.LoadProject(TempDir);
        using FileStream fileLock = SystemIOWrapper.OpenFileStream(occupiedFullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // 通过独占句柄写入使文件签名变化（占据 + 内容变化，快照扫描必须探测）
        fileLock.SetLength(0);
        fileLock.Write("temp-note-v2"u8);
        fileLock.Flush();

        // 签名变化触发重扫：占据被容错记录（旧签名复用），不进入受阻
        Assert.True(SpinWaitUntil(() => FileSystemCache.Instance.GetToleratedRelativePaths().Contains("notes.txt"), TimeSpan.FromSeconds(8)));
        Assert.False(model.IsWorkspaceBlocked);

        // 释放后容错路径解除，快照重扫恢复
        fileLock.Dispose();
        Assert.True(SpinWaitUntil(() => FileSystemCache.Instance.GetToleratedRelativePaths().Count == 0, TimeSpan.FromSeconds(8)));
        model.Dispose();
    }

    [Fact]
    public void WriteTarget_Occupied_Should_Block_Even_NonCritical()
    {
        CreateMinimalProject();
        CreateMech("unit", "list:\n[\n 1-1101\n]");
        CreateDevice("unit", 1, 1101);
        // 非关键资源实体（ogg）→ 写目标
        string writeTargetFullPath = SystemIOWrapper.Combine(TempDir, "sound.ogg");
        SystemIOWrapper.WriteAllText(writeTargetFullPath, "ogg-v1");
        SystemIOWrapper.WriteAllText(writeTargetFullPath + ".guid", "cccccccccccccccccccccccccccccccc");
        Assert.False(CriticalFilePolicy.IsCriticalPath("sound.ogg"), "sound.ogg 应为非关键");

        using ProjectModel model = TestWorkspace.LoadProjectWithPersistence(TempDir, new PhysicalPersistenceOperationApplier());
        using FileStream fileLock = SystemIOWrapper.OpenFileStream(writeTargetFullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        WritableProjectRootEntity writableRoot = model.Root.ToWritable();
        WritableResourceEntity resource = (WritableResourceEntity)writableRoot.Children.Single(c => c.实体_无相对路径标识符 == "sound.ogg");
        resource.Data = "ogg-v2"u8.ToArray();

        // 写目标被占据：即使非关键也必须严格避让（跳过 = 静默丢修改）
        Assert.Throws<WorkspaceFileBlockedException>(() => model.SaveChanges(writableRoot));
        Assert.True(model.IsWorkspaceBlocked);
        model.Dispose();
    }

    [Fact]
    public void SettingReferenced_Table_Occupied_Should_Block_And_Recover()
    {
        // setting.json 引用调色板表（paletteExcel）→ 引用解析为关键 → 占据全面退让。
        // 引用集合是静态全局状态（生产单例模型），并行测试下可能被其他测试的模型加载覆盖，
        // 故本测试显式设置引用集合并紧邻断言，不依赖跨测试时序。
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\npaletteExcel:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");
        string tableFullPath = SystemIOWrapper.Combine(TempDir, "调色板表.xlsx");
        SystemIOWrapper.WriteAllText(tableFullPath, "palette");
        SystemIOWrapper.WriteAllText(tableFullPath + ".guid", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        ProjectModel model = TestWorkspace.LoadProject(TempDir);
        Assert.False(model.IsWorkspaceBlocked);
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", model.Root.SettingJson?.PaletteExcel);

        CriticalFilePolicy.SetReferencedCriticalPaths(["调色板表.xlsx"]);
        Assert.True(CriticalFilePolicy.IsCriticalPath("调色板表.xlsx"));

        using FileStream fileLock = SystemIOWrapper.OpenFileStream(tableFullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<WorkspaceFileBlockedException>(() => FileIO.ReadAllBytes("调色板表.xlsx"));
        Assert.True(model.IsWorkspaceBlocked);

        fileLock.Dispose();
        Assert.True(SpinWaitUntil(() => !model.IsWorkspaceBlocked, TimeSpan.FromSeconds(8)));
        model.Dispose();
    }

    [Fact]
    public void NonCritical_Occupied_Should_Not_Fail_SaveChanges()
    {
        CreateMinimalProject();
        CreateMech("unit", "list:\n[\n 1-1101\n]");
        CreateDevice("unit", 1, 1101);
        // temp/ 下的无 .guid 文件由 OtherTempFileEntity 声明（非删除目标）→ 占据不影响写回
        string occupiedFullPath = SystemIOWrapper.Combine(TempDir, "temp", "note.txt");
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(TempDir, "temp"));
        SystemIOWrapper.WriteAllText(occupiedFullPath, "temp-note");

        using ProjectModel model = TestWorkspace.LoadProjectWithPersistence(TempDir, new PhysicalPersistenceOperationApplier());
        using FileStream fileLock = SystemIOWrapper.OpenFileStream(occupiedFullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        WritableProjectRootEntity writableRoot = model.Root.ToWritable();

        // 非关键占据不阻碍写回（python 临时文件共存场景）
        IReadOnlyList<string> affected = model.SaveChanges(writableRoot);
        Assert.Empty(affected);
        Assert.False(model.IsWorkspaceBlocked);
        model.Dispose();
    }

    [Fact]
    public void DeleteTarget_Occupied_Should_Block_Even_NonCritical()
    {
        CreateMinimalProject();
        CreateMech("unit", "list:\n[\n 1-1101\n]");
        CreateDevice("unit", 1, 1101);
        // 非关键资源实体（ogg 不在图片白名单/表格/代码）→ 删除目标
        string deleteTargetFullPath = SystemIOWrapper.Combine(TempDir, "sound.ogg");
        SystemIOWrapper.WriteAllText(deleteTargetFullPath, "ogg-data");
        SystemIOWrapper.WriteAllText(deleteTargetFullPath + ".guid", "cccccccccccccccccccccccccccccccc");
        Assert.False(CriticalFilePolicy.IsCriticalPath("sound.ogg"), "sound.ogg 应为非关键");

        using ProjectModel model = TestWorkspace.LoadProjectWithPersistence(TempDir, new PhysicalPersistenceOperationApplier());
        Assert.IsType<ResourceEntity>(model.EntitiesByPath["sound.ogg"]);

        using FileStream fileLock = SystemIOWrapper.OpenFileStream(deleteTargetFullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        WritableProjectRootEntity writableRoot = model.Root.ToWritable();
        writableRoot.Children.RemoveAll(c => c.实体_无相对路径标识符 == "sound.ogg");

        // 删除目标被占据：即使非关键也必须严格避让（跳过 = 静默残留）
        Assert.Throws<WorkspaceFileBlockedException>(() => model.SaveChanges(writableRoot));
        Assert.True(model.IsWorkspaceBlocked);
        model.Dispose();
    }

    [Fact]
    public void SettingJson_ReferencedGuid_Missing_Should_Not_Crash()
    {
        // 业务约束：可能有 guid 断裂导致按 guid 查找无资源——AddReferenced 必须不崩溃
        // （查不到跳过），禁止 _byGuid[guid] 抛 KeyNotFoundException。
        CreateMinimalProject();
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\ncoverPic:3fe0f79e7f6249449ed84d4a455eb247");

        using ProjectModel model = TestWorkspace.LoadProjectWithRecordingPersistence(TempDir);
        Assert.NotNull(model.Root);
        Assert.Equal("3fe0f79e7f6249449ed84d4a455eb247", model.Root.SettingJson?.CoverPic);
        model.UpdateCriticalReferences();
    }

#pragma warning disable TK006 // 测试辅助：最小工程初始化
    private void CreateMinimalProject()
    {
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");
    }
#pragma warning restore TK006

    private void CreateMech(string name, string mechJsonText)
    {
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, $"{name}.mech"), "payload");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, $"{name}.mech.guid"),
            "11111111111111111111111111111111");
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(TempDir, $"{name}.mech_"));
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, $"{name}.mech_", "mech.json"), mechJsonText);
    }

    private void CreateDevice(string mechName, int deviceId, int deviceType)
    {
        string deviceDir = SystemIOWrapper.Combine(TempDir, $"{mechName}.mech_", deviceId.ToString());
        SystemIOWrapper.CreateDirectory(deviceDir);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), $"type:{deviceType}\next:{{}}");
    }

    private static bool SpinWaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (predicate())
            {
                return true;
            }

            System.Threading.Thread.Sleep(100);
        }

        return predicate();
    }
}
