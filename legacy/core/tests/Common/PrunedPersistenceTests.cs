// 支柱二：脏标记剪枝持久化测试
// 验证：SaveChanges 时仅标记实体分支全量收集/落盘，
// 未标记分支按剪枝声明处理（文件保留、内容不变、不产生 diff 操作）。
// 剪枝等价性：剪枝保存后磁盘状态必须与全量收集一致。

using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

[Collection("FileIO")]
public sealed class PrunedPersistenceTests : TempDirectoryTestFixture
{
    public PrunedPersistenceTests() : base("Toolkit.Core.Tests.PrunedPersistence")
    {
    }

    [Fact]
    public void Pruned_Save_Should_Write_Dirty_Branch_And_Preserve_Others()
    {
        CreateMinimalProject();
        CreateMech("unit", "list:\n[\n 1-1101\n 2-1102\n]");
        CreateDevice("unit", 1, 1101);
        CreateDevice("unit", 2, 1102);

        using ProjectModel model = TestWorkspace.LoadProjectWithPersistence(TempDir, new PhysicalPersistenceOperationApplier());

        WritableProjectRootEntity writableRoot = model.Root.ToWritable();
        WritableDeviceEntity device1 = FindDevice(writableRoot, "unit.mech_/1");
        device1.Vars = new VarsConfig
        {
            Variables = [new VarInfo("hp", "int", "血量")]
        };
        device1.HasVarsJson = true;

        model.MarkDirtyRecursive(device1);
        IReadOnlyList<string> affected = model.SaveChanges(writableRoot);

        // device1 分支落盘
        Assert.Contains("unit.mech_/1/vars.json", affected);
        Assert.Contains("hp", FileIO.ReadAllText("unit.mech_/1/vars.json"), StringComparison.Ordinal);

        // 未标记兄弟分支：不产生任何操作、文件保留、内容不变
        Assert.DoesNotContain("unit.mech_/2/device.json", affected);
        Assert.True(FileIO.FileExists("unit.mech_/2/device.json"));
        Assert.Equal("type:1102\next:{}", FileIO.ReadAllText("unit.mech_/2/device.json"));

        // mech.json 不受影响
        Assert.Equal("list:\n[\n 1-1101\n 2-1102\n]", FileIO.ReadAllText("unit.mech_/mech.json"));
    }

    [Fact]
    public void Save_Without_Dirty_Marks_Should_Behave_As_Full_Save()
    {
        CreateMinimalProject();
        CreateMech("unit", "list:\n[\n 1-1101\n 2-1102\n]");
        CreateDevice("unit", 1, 1101);
        CreateDevice("unit", 2, 1102);

        using ProjectModel model = TestWorkspace.LoadProjectWithPersistence(TempDir, new PhysicalPersistenceOperationApplier());

        WritableProjectRootEntity writableRoot = model.Root.ToWritable();
        WritableDeviceEntity device1 = FindDevice(writableRoot, "unit.mech_/1");
        device1.Vars = new VarsConfig
        {
            Variables = [new VarInfo("hp", "int", "血量")]
        };
        device1.HasVarsJson = true;

        // 黑盒修改未传播：退化为全量收集，修改仍必须落盘
        IReadOnlyList<string> affected = model.SaveChanges(writableRoot);

        Assert.Contains("unit.mech_/1/vars.json", affected);
        Assert.True(FileIO.FileExists("unit.mech_/1/vars.json"));
        Assert.True(FileIO.FileExists("unit.mech_/2/device.json"));
        Assert.Equal("type:1102\next:{}", FileIO.ReadAllText("unit.mech_/2/device.json"));
    }

    [Fact]
    public void Pruned_Save_Should_Propagate_Along_Parent_Chain()
    {
        CreateMinimalProject();
        CreateMech("unit", "list:\n[\n 1-1101\n 2-1102\n]");
        CreateDevice("unit", 1, 1101);
        CreateDevice("unit", 2, 1102);

        using ProjectModel model = TestWorkspace.LoadProjectWithPersistence(TempDir, new PhysicalPersistenceOperationApplier());

        WritableProjectRootEntity writableRoot = model.Root.ToWritable();
        WritableDeviceEntity device1 = FindDevice(writableRoot, "unit.mech_/1");
        device1.Vars = new VarsConfig
        {
            Variables = [new VarInfo("hp", "int", "血量")]
        };
        device1.HasVarsJson = true;

        // 传播沿 Parent 链：device1 → mech → root 全标记，
        // 但未修改的 device2 分支（mech 之下）仍应剪枝——剪枝按实体粒度判定
        model.MarkDirtyRecursive(device1);
        model.SaveChanges(writableRoot);

        Assert.True(FileIO.FileExists("unit.mech_/1/vars.json"));
        Assert.True(FileIO.FileExists("unit.mech_/2/device.json"));
        Assert.Equal("type:1102\next:{}", FileIO.ReadAllText("unit.mech_/2/device.json"));
    }

    [Fact]
    public void Dirty_Session_Should_Reset_After_SaveChanges()
    {
        CreateMinimalProject();
        CreateMech("unit", "list:\n[\n 1-1101\n 2-1102\n]");
        CreateDevice("unit", 1, 1101);
        CreateDevice("unit", 2, 1102);

        using ProjectModel model = TestWorkspace.LoadProjectWithPersistence(TempDir, new PhysicalPersistenceOperationApplier());

        // 第一轮：修改 device1 并传播
        WritableProjectRootEntity writableRoot = model.Root.ToWritable();
        WritableDeviceEntity device1 = FindDevice(writableRoot, "unit.mech_/1");
        device1.Vars = new VarsConfig { Variables = [new VarInfo("hp", "int", "血量")] };
        device1.HasVarsJson = true;
        model.MarkDirtyRecursive(device1);
        model.SaveChanges(writableRoot);

        // 第二轮（新会话）：修改 device2 并传播——上一轮脏路径必须已清空，
        // device2 分支落盘，device1 分支（含其 vars.json）保持原样
        WritableProjectRootEntity writableRoot2 = model.Root.ToWritable();
        WritableDeviceEntity device2 = FindDevice(writableRoot2, "unit.mech_/2");
        device2.Vars = new VarsConfig { Variables = [new VarInfo("mp", "int", "魔法")] };
        device2.HasVarsJson = true;
        model.MarkDirtyRecursive(device2);
        IReadOnlyList<string> affected2 = model.SaveChanges(writableRoot2);

        Assert.Contains("unit.mech_/2/vars.json", affected2);
        Assert.DoesNotContain("unit.mech_/1/vars.json", affected2);
        Assert.Contains("hp", FileIO.ReadAllText("unit.mech_/1/vars.json"), StringComparison.Ordinal);
        Assert.Contains("mp", FileIO.ReadAllText("unit.mech_/2/vars.json"), StringComparison.Ordinal);
    }

    [Fact]
    public void Pruned_Save_Should_Collect_Modified_Subtree_Children()
    {
        CreateMinimalProject();
        CreateMech("unit", "list:\n[\n 1-1101\n 2-1102\n]");
        CreateDevice("unit", 1, 1101);
        CreateDevice("unit", 2, 1102);
        CreateScripts("unit", 1);

        using ProjectModel model = TestWorkspace.LoadProjectWithPersistence(TempDir, new PhysicalPersistenceOperationApplier());

        WritableProjectRootEntity writableRoot = model.Root.ToWritable();
        WritableDeviceEntity device1 = FindDevice(writableRoot, "unit.mech_/1");
        WritableScriptGroupEntity group = (WritableScriptGroupEntity)device1.ScriptGroups.Single();
        WritableInlineScriptEntity script = (WritableInlineScriptEntity)group.Scripts.Single();
        script.SourceText = "begincodeV2\nname:Hello\nlines:\nfun debug.enable(Bool 0)\nendcode\n";

        model.MarkDirtyRecursive(device1);
        IReadOnlyList<string> affected = model.SaveChanges(writableRoot);

        // 修改点在 device 子树内：其子孙（脚本组/脚本）必须全量收集落盘，剪枝不得跳过
        Assert.Contains("unit.mech_/1/scripts/Hello.code", affected);
        Assert.Contains("Bool 0", FileIO.ReadAllText("unit.mech_/1/scripts/Hello.code"), StringComparison.Ordinal);
        Assert.Equal("type:1102\next:{}", FileIO.ReadAllText("unit.mech_/2/device.json"));
    }

    [Fact]
    public void Pruned_Save_Should_Propagate_CodeRepo_Branch()
    {
        CreateMinimalProject();
        CreateMech("unit", "list:\n[\n 1-1101\n 2-1102\n]");
        CreateDevice("unit", 1, 1101);
        CreateDevice("unit", 2, 1102);
        CreateCodeRepo("repo1");

        using ProjectModel model = TestWorkspace.LoadProjectWithPersistence(TempDir, new PhysicalPersistenceOperationApplier());

        WritableProjectRootEntity writableRoot = model.Root.ToWritable();
        WritableDeviceEntity device1 = FindDevice(writableRoot, "unit.mech_/1");
        device1.Vars = new VarsConfig
        {
            Variables = [new VarInfo("hp", "int", "血量")]
        };
        device1.HasVarsJson = true;
        WritableCodeRepoScriptEntity codeRepo = (WritableCodeRepoScriptEntity)writableRoot.Children.Single(c => c.实体_无相对路径标识符 == "repo1.code");
        codeRepo.SourceText = "begincodeV2\nname:Repo1\nlines:\nfun debug.enable(Bool 0)\nendcode\n";

        model.MarkDirtyRecursive(device1);
        model.MarkDirtyRecursive(codeRepo);
        IReadOnlyList<string> affected = model.SaveChanges(writableRoot);

        // 上提保存（起点=根）：device 分支与 CodeRepo 分支均落盘，兄弟分支剪枝
        Assert.Contains("unit.mech_/1/vars.json", affected);
        Assert.Contains("repo1.code", affected);
        Assert.Contains("Bool 0", FileIO.ReadAllText("repo1.code"), StringComparison.Ordinal);
        Assert.Equal("type:1102\next:{}", FileIO.ReadAllText("unit.mech_/2/device.json"));
    }

#pragma warning disable TK006 // 测试辅助：沿路径定位可写子实体
    private static WritableDeviceEntity FindDevice(ProjectEntity root, string relativePath)
    {
        ProjectEntity current = root;
        foreach (string segment in relativePath.Split('/'))
        {
            current = ((IHasChildren<ProjectEntity>)current).Children
                .Single(child => child.实体_无相对路径标识符 == segment);
        }

        return (WritableDeviceEntity)current;
    }
#pragma warning restore TK006

    private void CreateMinimalProject()
    {
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");
    }

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

#pragma warning disable TK006 // 测试辅助：脚本组/代码仓库夹具构造
    private void CreateScripts(string mechName, int deviceId)
    {
        string scriptsDir = SystemIOWrapper.Combine(TempDir, $"{mechName}.mech_", deviceId.ToString(), "scripts");
        SystemIOWrapper.CreateDirectory(scriptsDir);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n Hello\n]");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Hello.code"),
            "begincodeV2\nname:Hello\nlines:\nfun debug.enable(Bool 1)\nendcode\n");
    }

    private void CreateCodeRepo(string name)
    {
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, $"{name}.code"),
            "begincodeV2\nname:Repo1\nlines:\nfun debug.enable(Bool 1)\nendcode\n");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, $"{name}.code.guid"),
            "dddddddddddddddddddddddddddddddd");
    }
#pragma warning restore TK006
}
