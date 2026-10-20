using System.Diagnostics;
using Toolkit.Core.Infra;
using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

[Collection("FileIO")]
public sealed class ProjectModelWorkspaceBlockTests : TempDirectoryTestFixture
{
    public ProjectModelWorkspaceBlockTests() : base("Toolkit.Core.Tests.ProjectModelWorkspaceBlock")
    {
    }

    [Fact]
    public void Load_When_File_IsBlocked_Should_Enter_Blocked_State()
    {
        CreateMinimalProject();
        string blockedFullPath = SystemIOWrapper.Combine(TempDir, "setting.json");
        using FileStream fileLock = SystemIOWrapper.OpenFileStream(blockedFullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        ProjectModel model = TestWorkspace.LoadProject(TempDir);
        WorkspaceBlockStateSnapshot state = model.GetWorkspaceBlockState();
        Assert.True(model.IsWorkspaceBlocked);
        Assert.Contains("setting.json", state.BlockedRelativePaths);
        model.Dispose();
    }

    [Fact]
    public void Blocked_File_Released_Should_Leave_Blocked_State_And_Reload_Project()
    {
        CreateMinimalProject();
        string blockedFullPath = SystemIOWrapper.Combine(TempDir, "setting.json");
        FileStream fileLock = SystemIOWrapper.OpenFileStream(blockedFullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        ProjectModel model = TestWorkspace.LoadProject(TempDir);
        fileLock.Dispose();
        Assert.True(SpinWaitUntil(() => !model.IsWorkspaceBlocked, TimeSpan.FromSeconds(8)));
        model.Dispose();
    }

    [Fact]
    public void Blocked_File_Queue_Should_Remove_Continuously_Until_First_Failure()
    {
        CreateMinimalProject();
        string firstFullPath = SystemIOWrapper.Combine(TempDir, "a.code");
        string secondFullPath = SystemIOWrapper.Combine(TempDir, "b.code");
        SystemIOWrapper.WriteAllText(firstFullPath, "code-a");
        SystemIOWrapper.WriteAllText(secondFullPath, "code-b");
        SystemIOWrapper.WriteAllText(firstFullPath + ProjectEntityLayout.GuidExt, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa01");
        SystemIOWrapper.WriteAllText(secondFullPath + ProjectEntityLayout.GuidExt, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa02");

        ProjectModel model = TestWorkspace.LoadProject(TempDir);
        using FileStream firstLock = SystemIOWrapper.OpenFileStream(firstFullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        FileStream secondLock = SystemIOWrapper.OpenFileStream(secondFullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<WorkspaceFileBlockedException>(() => FileIO.ReadAllBytes("a.code"));
        Assert.Throws<WorkspaceFileBlockedException>(() => FileIO.ReadAllBytes("b.code"));
        WorkspaceBlockStateSnapshot blockedState = model.GetWorkspaceBlockState();
        Assert.True(model.IsWorkspaceBlocked);
        Assert.Contains("a.code", blockedState.BlockedRelativePaths);
        Assert.Contains("b.code", blockedState.BlockedRelativePaths);
        firstLock.Dispose();
        Assert.True(SpinWaitUntil(() =>
        {
            WorkspaceBlockStateSnapshot state = model.GetWorkspaceBlockState();
            return model.IsWorkspaceBlocked
                   && state.BlockedRelativePaths.Count == 1
                   && state.BlockedRelativePaths[0] == "b.code";
        }, TimeSpan.FromSeconds(4)));
        secondLock.Dispose();
        model.Dispose();
    }

    [Fact]
    public void Workspace_State_Changed_Block_Should_Surface_Write_Message()
    {
        CreateMinimalProject();
        ProjectModel model = TestWorkspace.LoadProject(TempDir);
        WorkspaceFileBlockedException blockedEx = new(
            WorkspacePath.RootDirectory,
            "持久化覆盖面状态已变化，拒绝落盘: .",
            "当前工作区有外部进程进行写操作，已进入受阻状态，请稍后重试。");

        FileSystemCache.Instance.ReportBlockedException(blockedEx);

        WorkspaceBlockStateSnapshot state = model.GetWorkspaceBlockState();
        Assert.True(state.IsBlocked);
        Assert.Equal("当前工作区有外部进程进行写操作，已进入受阻状态，请稍后重试。", state.Message);
        Assert.Contains(WorkspacePath.RootDirectory, state.BlockedRelativePaths);
        model.Dispose();
    }

#pragma warning disable TK006 // 测试辅助：封装最小工程初始化
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

    [Fact]
    public void SaveChanges_When_Persist_Blocked_Should_Throw_Not_Fake_Success()
    {
        CreateMinimalProject();
        CreateMech("unit", "list:\n[\n 1-1101\n]");
        CreateDevice("unit", 1, 1101);
        CreateDevice("unit", 99, 9900); // orphan：list 无 99，SaveChanges 会产生删除 diff

        using ProjectModel model = TestWorkspace.LoadProjectWithPersistence(TempDir, new ThrowingPersistenceApplier());

        MechEntity mech = Assert.IsType<MechEntity>(model.EntitiesByPath["unit.mech_"]);
        WritableMechEntity writable = mech.ToWritable();

        // 受阻（独占失败落在 DrainQueue 的 try 内）时，SaveChanges 必须抛 WorkspaceFileBlockedException，
        // 而非吞成空列表假成功——空列表与"合法无操作"无法区分，下游会按成功处理。
        // 事务仍留在队列，受阻解除后由 OnWorkspaceBlockStateChanged 自动排空。
        Assert.Throws<WorkspaceFileBlockedException>(() => model.SaveChanges(writable));
    }

    [Fact(Timeout = 15000)]
    public void SaveChanges_When_Refresh_Blocked_Should_Throw_Not_Hang()
    {
        CreateMinimalProject();
        CreateMech("unit", "list:\n[\n 1-1101\n]");
        CreateDevice("unit", 1, 1101);
        CreateDevice("unit", 99, 9900); // orphan：list 无 99，SaveChanges 产生删除 diff

        // 真实持久化：Apply 成功落盘 → wroteToFileSystem=true → 进入 WaitForModelRefresh。
        using ProjectModel model = TestWorkspace.LoadProjectWithPersistence(TempDir, new PhysicalPersistenceOperationApplier());

        // 预设受阻态：模拟"写入成功后等待刷新期间工程被外部进程占用"。
        // 修复前 WaitForModelRefresh 的 Monitor.Wait 无超时会永久挂起（MCP 卡死根因）；
        // 修复后 while 内检测 IsWorkspaceBlocked 即抛，对称于 McpViewWorkspace.WaitUntilSynchronizedLocked。
        // Timeout=15s 兜底：若回归则断言失败而非拖垮测试进程。
        FileSystemCache.Instance.ReportBlockedException(new WorkspaceFileBlockedException(
            WorkspacePath.RootDirectory,
            "测试：等待刷新期间受阻",
            WorkspaceFileBlockedException.DefaultBlockMessage));
        Assert.True(model.IsWorkspaceBlocked);

        MechEntity mech = Assert.IsType<MechEntity>(model.EntitiesByPath["unit.mech_"]);
        WritableMechEntity writable = mech.ToWritable();

        Assert.Throws<WorkspaceFileBlockedException>(() => model.SaveChanges(writable));
    }

#pragma warning disable TK006 // 测试辅助：机械/零件构造
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
#pragma warning restore TK006

    /// <summary>
    /// 假持久化执行器：Apply 直接抛 WorkspaceFileBlockedException，模拟独占失败落在
    /// DrainQueue 的 try 内（同步 Persist 路径），用于验证受阻时报失败而非返回空列表假成功。
    /// </summary>
    private sealed class ThrowingPersistenceApplier : IPersistenceOperationApplier
    {
        public void Apply(
            LocalDiffResult diff,
            IReadOnlyList<AtomicOperation> operations,
            IReadOnlyCollection<string> exclusiveFilePaths,
            Action<IReadOnlyDictionary<string, FileStream>> beforeExecute)
            => throw new WorkspaceFileBlockedException(
                WorkspacePath.RootDirectory,
                "测试模拟：独占失败",
                WorkspaceFileBlockedException.DefaultBlockMessage);
    }
}
