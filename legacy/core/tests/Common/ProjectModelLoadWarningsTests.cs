using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

[Collection("WorkspaceState")]
public sealed class ProjectModelLoadWarningsTests : TempDirectoryTestFixture
{
    public ProjectModelLoadWarningsTests() : base("Toolkit.Core.Tests.ProjectModelLoadWarnings")
    {
    }

    [Fact]
    public void Load_Ignores_Invalid_Resource_Entity_And_Reports_Warnings()
    {
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "map"), "payload");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "map.guid"), "INVALID-GUID");

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        Assert.False(model.EntitiesByPath.ContainsKey("map"));
        Assert.False(model.EntitiesByPath.ContainsKey("map.guid"));
        Assert.Contains(model.LoadErrors.Errors, static e =>
            e.Severity == ToolDiagnosticSeverity.Error
            && e.Path == "map"
            && e.Message.Contains("GUID读取失败", StringComparison.Ordinal));
        Assert.Contains(model.LoadErrors.Errors, static e =>
            e.Severity == ToolDiagnosticSeverity.Warning
            && (e.Path == "map" || e.Path == "map.guid")
            && e.Message.Contains("文件未被任何实体覆盖", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_Drops_Invalid_Map_Entity_And_Falls_Back_To_Regular_Resource_And_Directory()
    {
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "broken.map"), "payload");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "broken.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(TempDir, "broken.map_"));

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        Assert.True(model.EntitiesByPath.TryGetValue("broken.map", out ProjectEntity? brokenResource));
        Assert.IsType<ResourceEntity>(brokenResource);
        Assert.True(model.EntitiesByPath.TryGetValue("broken.map_", out ProjectEntity? brokenDir));
        Assert.IsType<DirectoryEntity>(brokenDir);
        Assert.Contains(model.LoadErrors.Errors, static e =>
            e.Severity == ToolDiagnosticSeverity.Error
            && e.Path == "broken.map_/map.json"
            && e.Message.Contains("解析失败", StringComparison.Ordinal));
        Assert.DoesNotContain(model.LoadErrors.Errors, static e =>
            e.Severity == ToolDiagnosticSeverity.Warning
            && e.Message.Contains("文件未被任何实体覆盖", StringComparison.Ordinal));
        Assert.DoesNotContain(model.LoadErrors.Errors, static e =>
            e.Severity == ToolDiagnosticSeverity.Warning
            && e.Message.Contains("目录未被任何实体覆盖", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_Repairs_Duplicate_Guid_And_Records_GuidGuard_Issue()
    {
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");

        string duplicateGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(TempDir, "a.png"), [1]);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "a.png.guid"), duplicateGuid);
        SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(TempDir, "b.png"), [2]);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "b.png.guid"), duplicateGuid);

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        Assert.NotEqual(duplicateGuid, SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(TempDir, "b.png.guid")));
        Assert.Empty(model.GuidDuplicateIssues);
    }

    [Fact]
    public void Load_Does_Not_Match_Guid_File_From_Different_Directory()
    {
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");

        string orphanGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        string firstDirFullPath = SystemIOWrapper.Combine(TempDir, "A");
        string secondDirFullPath = SystemIOWrapper.Combine(TempDir, "B");
        SystemIOWrapper.CreateDirectory(firstDirFullPath);
        SystemIOWrapper.CreateDirectory(secondDirFullPath);
        string orphanGuidPath = SystemIOWrapper.Combine(firstDirFullPath, "same.png.guid");
        SystemIOWrapper.WriteAllText(orphanGuidPath, orphanGuid);
        SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(secondDirFullPath, "same.png"), [2]);

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        Assert.False(SystemIOWrapper.FileExists(orphanGuidPath));
        string generatedGuid = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(secondDirFullPath, "same.png.guid"));
        Assert.True(GuidText.Validate(generatedGuid));
        Assert.NotEqual(orphanGuid, generatedGuid);

        Assert.False(model.EntitiesByPath.ContainsKey("A/same.png.guid"));
        Assert.True(model.EntitiesByPath.TryGetValue("B/same.png", out ProjectEntity? entity));
        ResourceEntity resource = Assert.IsType<ResourceEntity>(entity);
        Assert.Equal(generatedGuid, resource.Guid);
        Assert.Same(resource, model.FindByGuid(generatedGuid));
    }

    [Fact]
    public void Load_Treats_Temp_Name_By_Parent_Scope()
    {
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");

        string rootTempFullPath = SystemIOWrapper.Combine(TempDir, "temp");
        SystemIOWrapper.CreateDirectory(rootTempFullPath);
        SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(rootTempFullPath, "root.png"), [1]);
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(rootTempFullPath, "temp"));
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(rootTempFullPath, "cache"));
        string toolkitFullPath = SystemIOWrapper.Combine(rootTempFullPath, "toolkit");
        SystemIOWrapper.CreateDirectory(toolkitFullPath);
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(toolkitFullPath, "temp"));
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(toolkitFullPath, "cache"));

        string nestedTempFullPath = SystemIOWrapper.Combine(TempDir, "folder", "temp");
        SystemIOWrapper.CreateDirectory(nestedTempFullPath);
        string nestedFileFullPath = SystemIOWrapper.Combine(nestedTempFullPath, "asset.png");
        SystemIOWrapper.WriteAllBytes(nestedFileFullPath, [2]);
        string nestedCacheFullPath = SystemIOWrapper.Combine(TempDir, "folder", "cache");
        SystemIOWrapper.CreateDirectory(nestedCacheFullPath);
        string nestedCacheFileFullPath = SystemIOWrapper.Combine(nestedCacheFullPath, "asset.png");
        SystemIOWrapper.WriteAllBytes(nestedCacheFileFullPath, [3]);

        SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(TempDir, "TestMap.map"), [4]);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "TestMap.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        string mapDirFullPath = SystemIOWrapper.Combine(TempDir, "TestMap.map_");
        SystemIOWrapper.CreateDirectory(mapDirFullPath);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDirFullPath, "map.json"), "{}");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDirFullPath, "mech.json"), "list:\n[\n 1-1001\n]");
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(mapDirFullPath, "temp"));
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(mapDirFullPath, "cache"));
        string mapDeviceFullPath = SystemIOWrapper.Combine(mapDirFullPath, "1");
        SystemIOWrapper.CreateDirectory(mapDeviceFullPath);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDeviceFullPath, "device.json"), "type:1001\next:{}");
        string scriptsFullPath = SystemIOWrapper.Combine(mapDeviceFullPath, "scripts");
        SystemIOWrapper.CreateDirectory(scriptsFullPath);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsFullPath, "list.json"), "[\n Hello\n]");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsFullPath, "Hello.code"), "begincodeV2\nname:Hello\nlines:\n// hello\nendcode\n");
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(scriptsFullPath, "temp"));
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(scriptsFullPath, "cache"));

        SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(TempDir, "TestMech.mech"), [5]);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "TestMech.mech.guid"), "cccccccccccccccccccccccccccccccc");
        string mechDirFullPath = SystemIOWrapper.Combine(TempDir, "TestMech.mech_");
        SystemIOWrapper.CreateDirectory(mechDirFullPath);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mechDirFullPath, "mech.json"), "list:\n[\n]");
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(mechDirFullPath, "temp"));
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(mechDirFullPath, "cache"));

        _ = TestWorkspace.LoadProject(TempDir);

        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(rootTempFullPath, "root.png.guid")));
        Assert.True(SystemIOWrapper.FileExists(nestedFileFullPath + ".guid"));
        Assert.True(SystemIOWrapper.FileExists(nestedCacheFileFullPath + ".guid"));

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        Assert.True(model.EntitiesByPath.TryGetValue("temp", out ProjectEntity? rootTempEntity));
        Assert.IsType<TempRootEntity>(rootTempEntity);

        Assert.False(model.EntitiesByPath.ContainsKey("temp/temp"));
        Assert.False(model.EntitiesByPath.ContainsKey("temp/cache"));
        Assert.Contains(model.LoadErrors.Errors, static e =>
            e.Severity == ToolDiagnosticSeverity.Warning
            && e.Path == "temp/temp"
            && e.Message.Contains("目录未被任何实体覆盖", StringComparison.Ordinal));
        Assert.Contains(model.LoadErrors.Errors, static e =>
            e.Severity == ToolDiagnosticSeverity.Warning
            && e.Path == "temp/cache"
            && e.Message.Contains("目录未被任何实体覆盖", StringComparison.Ordinal));

        Assert.True(model.EntitiesByPath.TryGetValue("temp/toolkit", out ProjectEntity? toolkitEntity));
        Assert.IsType<ToolkitRootEntity>(toolkitEntity);
        Assert.True(model.EntitiesByPath.TryGetValue("temp/toolkit/temp", out ProjectEntity? toolkitTempEntity));
        Assert.IsType<ToolkitInternalDirectoryEntity>(toolkitTempEntity);
        Assert.True(model.EntitiesByPath.TryGetValue("temp/toolkit/cache", out ProjectEntity? toolkitCacheEntity));
        Assert.IsType<ToolkitInternalDirectoryEntity>(toolkitCacheEntity);

        Assert.True(model.EntitiesByPath.TryGetValue("folder/temp", out ProjectEntity? nestedTempEntity));
        Assert.IsType<DirectoryEntity>(nestedTempEntity);
        Assert.True(model.EntitiesByPath.TryGetValue("folder/cache", out ProjectEntity? nestedCacheEntity));
        Assert.IsType<DirectoryEntity>(nestedCacheEntity);
        Assert.True(model.EntitiesByPath.TryGetValue("folder/temp/asset.png", out ProjectEntity? nestedFileEntity));
        Assert.IsType<ResourceEntity>(nestedFileEntity);
        Assert.True(model.EntitiesByPath.TryGetValue("folder/cache/asset.png", out ProjectEntity? nestedCacheFileEntity));
        Assert.IsType<ResourceEntity>(nestedCacheFileEntity);

        Assert.False(model.EntitiesByPath.ContainsKey("TestMap.map_/temp"));
        Assert.False(model.EntitiesByPath.ContainsKey("TestMap.map_/cache"));
        Assert.False(model.EntitiesByPath.ContainsKey("TestMech.mech_/temp"));
        Assert.False(model.EntitiesByPath.ContainsKey("TestMech.mech_/cache"));
        Assert.False(model.EntitiesByPath.ContainsKey("TestMap.map_/1/scripts/temp"));
        Assert.False(model.EntitiesByPath.ContainsKey("TestMap.map_/1/scripts/cache"));
        Assert.Contains(model.LoadErrors.Errors, static e =>
            e.Severity == ToolDiagnosticSeverity.Warning
            && e.Path == "TestMap.map_/temp"
            && e.Message.Contains("目录未被任何实体覆盖", StringComparison.Ordinal));
        Assert.Contains(model.LoadErrors.Errors, static e =>
            e.Severity == ToolDiagnosticSeverity.Warning
            && e.Path == "TestMap.map_/cache"
            && e.Message.Contains("目录未被任何实体覆盖", StringComparison.Ordinal));
        Assert.Contains(model.LoadErrors.Errors, static e =>
            e.Severity == ToolDiagnosticSeverity.Warning
            && e.Path == "TestMech.mech_/temp"
            && e.Message.Contains("目录未被任何实体覆盖", StringComparison.Ordinal));
        Assert.Contains(model.LoadErrors.Errors, static e =>
            e.Severity == ToolDiagnosticSeverity.Warning
            && e.Path == "TestMech.mech_/cache"
            && e.Message.Contains("目录未被任何实体覆盖", StringComparison.Ordinal));
        Assert.Contains(model.LoadErrors.Errors, static e =>
            e.Severity == ToolDiagnosticSeverity.Warning
            && e.Path == "TestMap.map_/1/scripts/temp"
            && e.Message.Contains("目录未被任何实体覆盖", StringComparison.Ordinal));
        Assert.Contains(model.LoadErrors.Errors, static e =>
            e.Severity == ToolDiagnosticSeverity.Warning
            && e.Path == "TestMap.map_/1/scripts/cache"
            && e.Message.Contains("目录未被任何实体覆盖", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_On_Non_Game_Directory_Refuses_GuidGuard_Disk_Maintenance()
    {
        // 非游戏工程根（缺 setting.json，如开发仓库被误当工程根打开）：加载必然失败（LoadSettings 致命），
        // 且 GuidGuard 不得创建/删除/改写任何 .guid。
        SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(TempDir, "a.png"), [1]);
        SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(TempDir, "b.code"), [2]);

        Assert.Throws<InvalidOperationException>(() => TestWorkspace.LoadProject(TempDir));

        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(TempDir, "a.png.guid")));
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(TempDir, "b.code.guid")));
    }

}
