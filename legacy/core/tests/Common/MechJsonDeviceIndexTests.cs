using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

[Collection("WorkspaceState")]
public sealed class MechJsonDeviceIndexTests : TempDirectoryTestFixture
{
    public MechJsonDeviceIndexTests() : base("Toolkit.Core.Tests.MechJsonDeviceIndex")
    {
    }

    [Fact]
    public void Load_Should_Use_MechJson_As_Device_Index_And_Save_Should_Delete_Orphan_Device_Directory()
    {
        CreateMinimalProject();
        CreateMech("unit", """
list:
[
 2-2202
 1-1101
]
""");
        CreateDevice("unit", 1, 1101);
        CreateDevice("unit", 2, 2202);
        CreateDevice("unit", 99, 9900);

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        MechEntity mech = Assert.IsType<MechEntity>(model.EntitiesByPath["unit.mech_"]);
        Assert.Collection(
            mech.Devices,
            static device => Assert.Equal(2, device.DeviceId),
            static device => Assert.Equal(1, device.DeviceId));
        Assert.DoesNotContain(mech.Devices, static device => device.DeviceId == 99);

        WritableMechEntity writable = mech.ToWritable();
        model.SaveChanges(writable);

        string orphanDir = SystemIOWrapper.Combine(TempDir, "unit.mech_", "99");
        Assert.False(SystemIOWrapper.DirectoryExists(orphanDir));
    }

    [Fact]
    public void Load_Should_Report_Error_When_DeviceType_Differs_From_MechJson()
    {
        CreateMinimalProject();
        CreateMech("unit", """
list:
[
 1-2202
]
""");
        CreateDevice("unit", 1, 1101);

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        MechEntity mech = Assert.IsType<MechEntity>(model.EntitiesByPath["unit.mech_"]);
        DeviceEntityBase device = Assert.Single(mech.Devices);
        Assert.Equal(1, device.DeviceId);
        Assert.Contains(model.LoadErrors.Errors, static error =>
            error.Severity == ToolDiagnosticSeverity.Error
            && error.Path == "unit.mech_/1/device.json"
            && error.Message.Contains("类型与 device.json 不一致", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_Should_Report_Error_When_MechJson_References_Missing_Device_Directory()
    {
        CreateMinimalProject();
        CreateMech("unit", """
list:
[
 5-5505
]
""");

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        MechEntity mech = Assert.IsType<MechEntity>(model.EntitiesByPath["unit.mech_"]);
        Assert.Empty(mech.Devices);
        Assert.Contains(model.LoadErrors.Errors, static error =>
            error.Severity == ToolDiagnosticSeverity.Error
            && error.Path == "unit.mech_/mech.json"
            && error.Message.Contains("零件目录不存在: 5", StringComparison.Ordinal));
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
}
