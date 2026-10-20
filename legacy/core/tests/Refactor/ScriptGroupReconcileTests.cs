using Toolkit.Core.Tests.Common;
using Xunit;
using Toolkit.Core.Persistence;
using Toolkit.Core.Refactor;

namespace Toolkit.Core.Tests.Refactor;

/// <summary>
/// 脚本组一致性修复（保护性交集强刷）—— 纯内存单元测试。
/// 不加载文件、不污染 test/data/projects。覆盖 happy path（一致→noop、空工程）与异常（device 不存在）。
/// </summary>
public class ScriptGroupReconcileTests
{
    [Fact]
    public void ReconcileAllDevices_EmptyProject_ReturnsFalse()
    {
        // 边界: 工程无零件 → 无写盘
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);

        bool result = ScriptGroupReconcile.ReconcileAllDevices(model);

        Assert.False(result);
    }

    [Fact]
    public void ReconcileDeviceToIntersection_DeviceNotFound_ReturnsFalse()
    {
        // 异常: device 路径不存在 → Find 返回 null → 不写盘
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);

        bool result = ScriptGroupReconcile.ReconcileDeviceToIntersection(model, "main.map/999");

        Assert.False(result);
    }

    [Fact]
    public void ReconcileDeviceToIntersection_ConsistentGroup_ReturnsFalseNoChange()
    {
        // happy: 清单与实体一致 → 无幽灵 → 不裁剪 → 不写盘
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "logic");
        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new ScriptEntryInfo("Real", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Real", "begincodeV2\nname:Real\nlines:\nendcode");
        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);

        bool result = ScriptGroupReconcile.ReconcileDeviceToIntersection(model, device.实体_完整相对路径标识);

        Assert.False(result);
    }
}

/// <summary>
/// 脚本组一致性修复 —— 临时工程端到端集成测试（真落盘）。
/// 验证"会写"分支：失配触发交集裁剪，list.json 被重写、孤儿 .code 被硬删除。
/// 用 TempDirectoryTestFixture（%TEMP% 下，非 test/data/projects），用完自动清理。
/// </summary>
[Collection("WorkspaceState")]
public sealed class ScriptGroupReconcileIntegrationTests : TempDirectoryTestFixture
{
    public ScriptGroupReconcileIntegrationTests() : base("Toolkit.Core.Tests.ScriptGroupReconcile")
    {
    }

    [Fact]
    public void Reconcile_Prunes_Phantom_Entry_And_Rewrites_ListJson()
    {
        // 异常: list.json 登记 Real + Ghost，但 Ghost 无对应 .code（直写 list.json 加幽灵条目）
        string scriptsDir = ScriptGroupTestProject.CreateBaseProject(TempDir);
        File.WriteAllText(Path.Combine(scriptsDir, "list.json"), "[\n  Real\n  Ghost\n]");
        File.WriteAllText(Path.Combine(scriptsDir, "Real.code"), "begincodeV2\nname:Real\nlines:\n//real\nendcode\n");
        // 故意不建 Ghost.code

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        bool result = ScriptGroupReconcile.ReconcileDeviceToIntersection(model, ScriptGroupTestProject.DeviceRelativePath);

        Assert.True(result);
        List<ListJsonItem> items = HjsonText.ParseListItems(File.ReadAllText(Path.Combine(scriptsDir, "list.json")));
        Assert.Contains(items, static i => i.Name == "Real");
        Assert.DoesNotContain(items, static i => i.Name == "Ghost");
    }

    [Fact]
    public void Reconcile_Deletes_Orphan_CodeFile_When_Prune_Triggered()
    {
        // 异常: 幽灵条目 Ghost 触发裁剪（changed=true），SaveChanges 顺带按管辖范围硬删除孤儿 Orphan.code
        string scriptsDir = ScriptGroupTestProject.CreateBaseProject(TempDir);
        File.WriteAllText(Path.Combine(scriptsDir, "list.json"), "[\n  Real\n  Ghost\n]");
        File.WriteAllText(Path.Combine(scriptsDir, "Real.code"), "begincodeV2\nname:Real\nlines:\n//real\nendcode\n");
        File.WriteAllText(Path.Combine(scriptsDir, "Orphan.code"), "begincodeV2\nname:Orphan\nlines:\n//orphan\nendcode\n");
        // Orphan.code 是孤儿：磁盘存在但 list.json 未登记

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        bool result = ScriptGroupReconcile.ReconcileDeviceToIntersection(model, ScriptGroupTestProject.DeviceRelativePath);

        Assert.True(result);
        Assert.True(File.Exists(Path.Combine(scriptsDir, "Real.code")));   // 正常脚本保留
        Assert.False(File.Exists(Path.Combine(scriptsDir, "Orphan.code"))); // 孤儿被硬删除
    }

    [Fact]
    public void Reconcile_Deletes_Orphan_CodeFile_When_List_Consistent()
    {
        // 异常: 清单本身一致（无幽灵条目），但磁盘有未登记的孤儿 Orphan.code。
        // 纯孤儿场景也必须触发保存，让持久化层按管辖范围硬删除（双向清理的另一向）。
        string scriptsDir = ScriptGroupTestProject.CreateBaseProject(TempDir);
        File.WriteAllText(Path.Combine(scriptsDir, "list.json"), "[\n  Real\n]");
        File.WriteAllText(Path.Combine(scriptsDir, "Real.code"), "begincodeV2\nname:Real\nlines:\n//real\nendcode\n");
        File.WriteAllText(Path.Combine(scriptsDir, "Orphan.code"), "begincodeV2\nname:Orphan\nlines:\n//orphan\nendcode\n");

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        bool result = ScriptGroupReconcile.ReconcileDeviceToIntersection(model, ScriptGroupTestProject.DeviceRelativePath);

        Assert.True(result);
        Assert.True(File.Exists(Path.Combine(scriptsDir, "Real.code")));
        Assert.False(File.Exists(Path.Combine(scriptsDir, "Orphan.code")));
        // 清单无幽灵条目，不应被改动
        List<ListJsonItem> items = HjsonText.ParseListItems(File.ReadAllText(Path.Combine(scriptsDir, "list.json")));
        Assert.Single(items);
        Assert.Equal("Real", items[0].Name);
    }

    [Fact]
    public void Reconcile_Is_Idempotent_After_Success()
    {
        // 边界: 清理完成后再次执行应为 no-op（防御 Lint 推送轮重复触发造成抖动）
        string scriptsDir = ScriptGroupTestProject.CreateBaseProject(TempDir);
        File.WriteAllText(Path.Combine(scriptsDir, "list.json"), "[\n  Real\n  Ghost\n]");
        File.WriteAllText(Path.Combine(scriptsDir, "Real.code"), "begincodeV2\nname:Real\nlines:\n//real\nendcode\n");

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        Assert.True(ScriptGroupReconcile.ReconcileDeviceToIntersection(model, ScriptGroupTestProject.DeviceRelativePath));
        // SaveChanges 内部已等待刷新完成，同一 model 上再跑应无残留失配
        Assert.False(ScriptGroupReconcile.ReconcileDeviceToIntersection(model, ScriptGroupTestProject.DeviceRelativePath));
    }

    [Fact]
    public void Reconcile_Preserves_All_Scripts_When_Consistent_NoChange()
    {
        // happy: 工程一致 → 不裁剪、不写盘、不动任何文件
        string scriptsDir = ScriptGroupTestProject.CreateBaseProject(TempDir);
        File.WriteAllText(Path.Combine(scriptsDir, "list.json"), "[\n  Real\n]");
        File.WriteAllText(Path.Combine(scriptsDir, "Real.code"), "begincodeV2\nname:Real\nlines:\n//real\nendcode\n");

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        bool result = ScriptGroupReconcile.ReconcileDeviceToIntersection(model, ScriptGroupTestProject.DeviceRelativePath);

        Assert.False(result);
        Assert.True(File.Exists(Path.Combine(scriptsDir, "Real.code")));
        Assert.Contains("Real", File.ReadAllText(Path.Combine(scriptsDir, "list.json")));
    }
}
