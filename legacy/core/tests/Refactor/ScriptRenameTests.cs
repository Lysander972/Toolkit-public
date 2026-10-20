using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Toolkit.Core.Tests.Common;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Refactor;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Refactor;

[Collection("WorkspaceState")]
public sealed class ScriptRenameTests : TempDirectoryTestFixture
{
    private ProjectModel? _model;
    private ScriptRenameTool? _rename;

    public ScriptRenameTests() : base("Toolkit.Core.Tests.ScriptRename")
    {
    }

    [MemberNotNull(nameof(_model), nameof(_rename))]
    private void Init(string projectRoot)
    {
        _model = TestWorkspace.LoadProject(projectRoot);
        _rename = new ScriptRenameTool(_model);
    }

    [Fact]
    public void RenameDeviceScript_Should_Persist_File_List_NameHeader_And_ActReference()
    {
        string projectRoot = CreateDeviceScriptProject();
        Init(projectRoot);

        RefactorResult<ScriptRenameData> result = _rename.Rename("map.map_/0/scripts/Target.code", "Renamed");

        Assert.True(result.IsSuccess, result.Errors.Count == 0 ? string.Empty : result.Errors[0].Message);
        ScriptRenameData data = Assert.IsType<ScriptRenameData>(result.Data);
        Assert.Equal("map.map_/0/scripts/Renamed.code", data.NewScriptRelativePath);
        Assert.Equal(1, data.UpdatedReferenceCount);
        Assert.Contains("map.map_/0/scripts/Caller.code", data.UpdatedReferenceFiles);

        string scriptsDir = SystemIOWrapper.Combine(projectRoot, "map.map_", "0", "scripts");
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(scriptsDir, "Target.code")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(scriptsDir, "Renamed.code")));

        string renamedText = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(scriptsDir, "Renamed.code"));
        Assert.Contains("name:Renamed", renamedText);
        Assert.DoesNotContain("name:Target", renamedText);

        string callerText = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(scriptsDir, "Caller.code"));
        Assert.Contains(".Renamed()", callerText);
        Assert.DoesNotContain(".Target()", callerText);

        List<ListJsonItem> items = HjsonText.ParseListItems(SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(scriptsDir, "list.json")));
        Assert.Contains(items, static item => item.Name == "Renamed" && !item.IsCodeRepo);
        Assert.DoesNotContain(items, static item => item.Name == "Target");
    }

    [Fact]
    public void RenameRepositoryScript_Should_Persist_File_Guid_List_NameHeader_And_ActReference()
    {
        const string repoGuid = "ffffffffffffffffffffffffffffffff";
        string projectRoot = CreateBaseProject();
        string initialScriptsDir = SystemIOWrapper.Combine(projectRoot, "map.map_", "0", "scripts");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRoot, "RepoOld.code"), "begincodeV2\nname:RepoOld\nlines:\n//repo\nendcode\n");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRoot, "RepoOld.code.guid"), repoGuid);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(initialScriptsDir, "list.json"), "[\n  Caller\n  {\n    guid:" + repoGuid + "\n    name:RepoOld\n  }\n]");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(initialScriptsDir, "Caller.code"), "begincodeV2\nname:Caller\nlines:\n=act 1001.this.RepoOld()\nendcode\n");
        Init(projectRoot);
        Assert.NotNull(_model.Find<InlineScriptEntity>("map.map_/0/scripts/Caller.code"));
        Assert.NotNull(_model.FindByGuid<CodeRepoScriptEntity>(repoGuid));

        RefactorResult<ScriptRenameData> result = _rename.Rename("RepoOld.code", "RepoNew");

        Assert.True(result.IsSuccess, result.Errors.Count == 0 ? string.Empty : result.Errors[0].Message);
        ScriptRenameData data = Assert.IsType<ScriptRenameData>(result.Data);
        Assert.Equal("RepoNew.code", data.NewScriptRelativePath);
        Assert.Equal(1, data.UpdatedReferenceCount);
        Assert.Contains("map.map_/0/scripts/Caller.code", data.UpdatedReferenceFiles);

        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "RepoOld.code")));
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "RepoOld.code.guid")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "RepoNew.code")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "RepoNew.code.guid")));
        Assert.Equal(repoGuid, SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "RepoNew.code.guid")));

        string repoText = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "RepoNew.code"));
        Assert.Contains("name:RepoNew", repoText);
        Assert.DoesNotContain("name:RepoOld", repoText);

        string scriptsDir = SystemIOWrapper.Combine(projectRoot, "map.map_", "0", "scripts");
        string callerText = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(scriptsDir, "Caller.code"));
        Assert.Contains(".RepoNew()", callerText);
        Assert.DoesNotContain(".RepoOld()", callerText);

        ListJsonItem repoEntry = Assert.Single(HjsonText.ParseListItems(SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"))), static item => item.Guid == repoGuid);
        Assert.Equal("RepoNew", repoEntry.Name);
        Assert.True(repoEntry.IsCodeRepo);
    }

    [Fact]
    public void McpRenameScript_Should_Persist_File_List_NameHeader_And_ActReference()
    {
        string projectRoot = CreateDeviceScriptProject();
        Init(projectRoot);
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices services = new(_model, new ProjectLogicContext(_model), browser);
        using McpToolRegistry registry = new(services, projectRoot, null, registerHandlers: false);

        using JsonDocument requestDoc = JsonDocument.Parse("""
            {
              "script_relative_path": "map.map_/0/scripts/Target.code",
              "new_name": "McpRenamed"
            }
            """);
        MethodInfo? executeMethod = typeof(McpToolRegistry)
            .GetMethod("ExecuteRenameScript", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(executeMethod);

        ITuple? resultTuple = Assert.IsAssignableFrom<ITuple>(executeMethod.Invoke(registry, [requestDoc.RootElement]));
        Assert.NotNull(resultTuple);
        IReadOnlyList<ToolError> errors = Assert.IsAssignableFrom<IReadOnlyList<ToolError>>(resultTuple[1]);
        IReadOnlyList<string> affectedPathList = Assert.IsAssignableFrom<IReadOnlyList<string>>(resultTuple[3]);
        Assert.Empty(errors);
        Assert.Contains("map.map_/0/scripts/McpRenamed.code", affectedPathList);

        string scriptsDir = SystemIOWrapper.Combine(projectRoot, "map.map_", "0", "scripts");
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(scriptsDir, "Target.code")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(scriptsDir, "McpRenamed.code")));
        Assert.Contains("name:McpRenamed", SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(scriptsDir, "McpRenamed.code")));
        Assert.Contains(".McpRenamed()", SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(scriptsDir, "Caller.code")));

        List<ListJsonItem> items = HjsonText.ParseListItems(SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(scriptsDir, "list.json")));
        Assert.Contains(items, static item => item.Name == "McpRenamed" && !item.IsCodeRepo);
        Assert.DoesNotContain(items, static item => item.Name == "Target");
    }

    private string CreateDeviceScriptProject()
    {
        string projectRoot = CreateBaseProject();
        string scriptsDir = SystemIOWrapper.Combine(projectRoot, "map.map_", "0", "scripts");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n  Target\n  Caller\n]");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Target.code"), "begincodeV2\nname:Target\nlines:\n//target\nendcode\n");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Caller.code"), "begincodeV2\nname:Caller\nlines:\n=act 1001.this.Target()\nendcode\n");
        return projectRoot;
    }

    private string CreateBaseProject()
    {
        string projectRoot = TempDir;

        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(projectRoot, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(projectRoot, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");

        string mapDir = SystemIOWrapper.Combine(projectRoot, "map.map_");
        SystemIOWrapper.CreateDirectory(mapDir);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRoot, "map.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRoot, "map.map"), [0x01, 0x02, 0x03, 0x04]);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "{\"list\":[\"0-1001\"]}");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "{\"list\":[]}");

        string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
        string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
        SystemIOWrapper.CreateDirectory(scriptsDir);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type: 1001\next:\n{\nlogicName: test\n}");
        return projectRoot;
    }
}
