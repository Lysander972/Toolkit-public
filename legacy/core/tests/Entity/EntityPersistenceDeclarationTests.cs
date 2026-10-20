using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Entity;

public sealed class EntityPersistenceDeclarationTests
{
    [Fact]
    public void ScriptGroup_Should_Skip_ListJson_When_ScriptEntries_Empty()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1);
        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([]);

        EntityCoverage coverage = group.GetSelfCoverage();
        ExpectedTreeBuffer buffer = new();
        group.DeclareAllFileContents(buffer);
        HashSet<string> declaredFiles = buffer.GetAllFiles()
            .Select(static f => f.RelativePath)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(group.listJson_完整相对路径, coverage.Files.Keys);
        Assert.DoesNotContain(group.listJson_完整相对路径, declaredFiles);
    }

    [Fact]
    public void ScriptGroup_Should_Declare_ListJson_When_ScriptEntries_Not_Empty()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1);
        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal(
        [
            new("Init", string.Empty, false)
        ]);

        EntityCoverage coverage = group.GetSelfCoverage();
        ExpectedTreeBuffer buffer = new();
        group.DeclareAllFileContents(buffer);
        HashSet<string> declaredFiles = buffer.GetAllFiles()
            .Select(static f => f.RelativePath)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(group.listJson_完整相对路径, coverage.Files.Keys);
        Assert.Contains(group.listJson_完整相对路径, declaredFiles);
    }

    [Fact]
    public void Device_Should_Skip_VarsJson_When_Vars_Empty()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1);
        device.SetVarsInternal(new VarsConfig());

        EntityCoverage coverage = device.GetSelfCoverage();
        ExpectedTreeBuffer buffer = new();
        device.DeclareAllFileContents(buffer);
        HashSet<string> declaredFiles = buffer.GetAllFiles()
            .Select(static f => f.RelativePath)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(device.vars配置_完整相对路径, coverage.Files.Keys);
        Assert.DoesNotContain(device.vars配置_完整相对路径, declaredFiles);
    }

    [Fact]
    public void Device_Should_Declare_VarsJson_When_Vars_Not_Empty()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1);
        device.SetVarsInternal(new VarsConfig
        {
            Variables =
            [
                new("A", "Float", null)
            ]
        });

        EntityCoverage coverage = device.GetSelfCoverage();
        ExpectedTreeBuffer buffer = new();
        device.DeclareAllFileContents(buffer);
        HashSet<string> declaredFiles = buffer.GetAllFiles()
            .Select(static f => f.RelativePath)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(device.vars配置_完整相对路径, coverage.Files.Keys);
        Assert.Contains(device.vars配置_完整相对路径, declaredFiles);
    }

}
