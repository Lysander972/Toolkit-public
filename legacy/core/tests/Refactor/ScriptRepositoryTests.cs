using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Toolkit.Core.Tests.Common;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Refactor;
using Toolkit.Core.Worker;

namespace Toolkit.Core.Tests.Refactor;

/// <summary>
/// Tests for ScriptRepository - script repository management operations.
/// 使用新基础设施：ProjectModel + ScriptRepository
/// </summary>
[Collection("WorkspaceState")]
public sealed class ScriptRepositoryTests : TempDirectoryTestFixture
{
    private const string RealCanvasDataBase64 = "EAEAEQgABHR5cGUGClVJUmF3SW1hZ2UEbmFtZQYJ6IOM5pmv5Zu+A1BvcwQAAAAAAAAAAAAAAAAMcGFyZW50QW5jaG9yGAQAAAAAAAAABmFuY2hvchgEAAAAAAAAAARzaXplBAAA+kQAAPpEAACAPwVjb2xvcgzV1FQ+1dRUPtXUVD4AAIA/CGZpbGVHdWlkBiAwNTI5YTM4YzViMDQ0NzdlODRlNGQxZTRkOTI1MWUyZA@3@3";

    private ProjectModel? _model;
    private ScriptRepository? _repository;

    public ScriptRepositoryTests() : base("Toolkit.Core.Tests.ScriptRepo")
    {
    }

    [MemberNotNull(nameof(_model), nameof(_repository))]
    private void InitRepository(string projectRoot)
    {
        _model = TestWorkspace.LoadProject(projectRoot);
        CanvasPreview preview = new CanvasPreview();
        _repository = new ScriptRepository(_model, preview);
    }

    #region ToRepository - Error Cases

    [Fact]
    public void ToRepository_Fail_When_ScriptName_Is_Empty()
    {
        // Arrange
        string projectRoot = CreateTestProject();
        InitRepository(projectRoot);

        // Act
        RefactorResult<ScriptToRepositoryData> result = _repository.ToRepository(
            "map.map_/0",
            string.Empty,
            "scripts"
        );

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("名称不能为空", result.Errors[0].Message);
    }

    [Fact]
    public void ToRepository_Fail_When_DevicePath_Is_Invalid()
    {
        // Arrange
        string projectRoot = CreateTestProject();
        InitRepository(projectRoot);

        // Act - 使用一个不存在的零件路径
        RefactorResult<ScriptToRepositoryData> result = _repository.ToRepository(
            "nonexistent/device/path",
            "testScript",
            "scripts"
        );

        // Assert
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ToRepository_Fail_When_ListJson_Not_Found()
    {
        // Arrange
        string projectRoot = CreateTestProject(withListJson: false);
        InitRepository(projectRoot);

        // Act
        RefactorResult<ScriptToRepositoryData> result = _repository.ToRepository(
            "map.map_/0",
            "testScript",
            "scripts"
        );

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("脚本组", result.Errors[0].Message);
    }

    [Fact]
    public void ToRepository_Fail_When_Script_Not_In_List()
    {
        // Arrange
        string projectRoot = CreateTestProject();
        // list.json 需要放在 scripts 目录下
        string scriptsDir = SystemIOWrapper.Combine(projectRoot, "map.map_", "0", "scripts");
        SystemIOWrapper.CreateDirectory(scriptsDir);
        string listContent = "[]";
        string listPath = SystemIOWrapper.Combine(scriptsDir, "list.json");
        SystemIOWrapper.WriteAllText(listPath, listContent);

        InitRepository(projectRoot);

        // Act - 使用正确的地图零件路径（地图下只有数字编号零件目录）
        RefactorResult<ScriptToRepositoryData> result = _repository.ToRepository(
            "map.map_/0",
            "nonExistentScript",
            "scripts"
        );

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("list.json", result.Errors[0].Message);
    }

    #endregion

    #region LinkRepository - Error Cases

    [Fact]
    public void LinkRepository_Fail_When_ScriptName_Is_Empty()
    {
        // Arrange
        string projectRoot = CreateTestProject();
        InitRepository(projectRoot);

        // Act
        RefactorResult<ScriptLinkRepositoryData> result = _repository.LinkRepository(
            "map.map_/0",
            string.Empty,
            "scripts",
            "0123456789abcdef0123456789abcdef"
        );

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("名称不能为空", result.Errors[0].Message);
    }

    [Fact]
    public void LinkRepository_Fail_When_TargetGuid_Is_Empty()
    {
        // Arrange
        string projectRoot = CreateTestProject();
        InitRepository(projectRoot);

        // Act
        RefactorResult<ScriptLinkRepositoryData> result = _repository.LinkRepository(
            "map.map_/0",
            "testScript",
            "scripts",
            string.Empty
        );

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("targetGuid", result.Errors[0].Message);
    }

    [Fact]
    public void LinkRepository_Fail_When_TargetGuid_Is_Invalid()
    {
        // Arrange
        string projectRoot = CreateTestProject();
        InitRepository(projectRoot);

        // Act
        RefactorResult<ScriptLinkRepositoryData> result = _repository.LinkRepository(
            "map.map_/0",
            "testScript",
            "scripts",
            "invalid-guid"
        );

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("格式无效", result.Errors[0].Message);
    }

    #endregion

    #region DeleteRepository - Error Cases

    [Fact]
    public void DeleteRepository_Fail_When_TargetGuid_Is_Empty()
    {
        // Arrange
        string projectRoot = CreateTestProject();
        InitRepository(projectRoot);

        // Act
        RefactorResult<ScriptDeleteRepositoryData> result = _repository.DeleteRepository(
            string.Empty
        );

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("targetGuid", result.Errors[0].Message);
    }

    [Fact]
    public void DeleteRepository_Fail_When_TargetGuid_Is_Invalid_Format()
    {
        // Arrange
        string projectRoot = CreateTestProject();
        InitRepository(projectRoot);

        // Act
        RefactorResult<ScriptDeleteRepositoryData> result = _repository.DeleteRepository(
            "not-a-guid"
        );

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("格式无效", result.Errors[0].Message);
    }

    [Fact]
    public void DeleteRepository_Fail_When_Guid_Not_Found()
    {
        // Arrange
        string projectRoot = CreateTestProject();
        InitRepository(projectRoot);
        string nonExistentGuid = "0123456789abcdef0123456789abcdef";

        // Act
        RefactorResult<ScriptDeleteRepositoryData> result = _repository.DeleteRepository(
            nonExistentGuid
        );

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("未找到", result.Errors[0].Message);
    }

    #endregion

    #region Refresh Regression

    [Fact]
    public void ToRepository_Refresh_Should_Load_Root_CodeRepoScript()
    {
        // Arrange
        string projectRoot = CreateTestProject();
        string inlineScriptPath = SystemIOWrapper.Combine(projectRoot, "map.map_", "0", "scripts", "testScript.code");
        SystemIOWrapper.WriteAllText(inlineScriptPath, "name testScript\nbegincodeV2\nlines:\nendcode");
        InitRepository(projectRoot);

        // Act
        RefactorResult<ScriptToRepositoryData> result = _repository.ToRepository(
            "map.map_/0",
            "testScript",
            "scripts"
        );

        Assert.True(result.IsSuccess);
        ScriptToRepositoryData data = Assert.IsType<ScriptToRepositoryData>(result.Data);
        Assert.True(data.Applied);
        Assert.Equal("testScript.code", data.RepositoryRelativePath);
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "testScript.code")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "testScript.code.guid")));
        Assert.Equal(data.ScriptGuid, SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "testScript.code.guid")));
        Assert.False(SystemIOWrapper.FileExists(inlineScriptPath));

        string listText = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "map.map_", "0", "scripts", "list.json"));
        ListJsonItem item = Assert.Single(HjsonText.ParseListItems(listText));
        Assert.Equal("testScript", item.Name);
        Assert.Equal(data.ScriptGuid, item.Guid);
        Assert.True(item.IsCodeRepo);

        // Assert
        CodeRepoScriptEntity? repoScript = _model.Find<CodeRepoScriptEntity>("testScript.code");
        Assert.NotNull(repoScript);
        Assert.Equal("testScript", repoScript.ScriptName);
    }

    [Fact]
    public void WorkerScriptToRepository_Should_Persist_Repository_And_Replace_List_Entry()
    {
        string projectRoot = CreateTestProject();
        string inlineScriptPath = SystemIOWrapper.Combine(projectRoot, "map.map_", "0", "scripts", "testScript.code");
        SystemIOWrapper.WriteAllText(inlineScriptPath, "begincodeV2\nname:testScript\nlines:\n//test\nendcode\n");
        ProjectModel model = TestWorkspace.LoadProject(projectRoot);
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices services = new(model, new ProjectLogicContext(model), browser);
        WorkerCommandHandler handler = new(services);

        WorkerResponse response = handler.Handle(new WorkerRequest(
            "scriptToRepository",
            DeviceRelativePath: "map.map_/0",
            ScriptName: "testScript"));

        Assert.True(response.Ok, response.Message);
        ScriptToRepositoryData data = Assert.IsType<ScriptToRepositoryData>(response.ScriptToRepo);
        Assert.True(data.Applied);
        Assert.Equal("testScript.code", data.RepositoryRelativePath);
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "testScript.code")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "testScript.code.guid")));
        Assert.Equal(data.ScriptGuid, SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "testScript.code.guid")));
        Assert.False(SystemIOWrapper.FileExists(inlineScriptPath));

        string listText = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "map.map_", "0", "scripts", "list.json"));
        ListJsonItem item = Assert.Single(HjsonText.ParseListItems(listText));
        Assert.Equal("testScript", item.Name);
        Assert.Equal(data.ScriptGuid, item.Guid);
        Assert.True(item.IsCodeRepo);
    }

    [Fact]
    public void WorkerListScriptGroupEntries_Should_Return_Hjson_Parsed_Names()
    {
        string projectRoot = CreateTestProject();
        ProjectModel model = TestWorkspace.LoadProject(projectRoot);
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices services = new(model, new ProjectLogicContext(model), browser);
        WorkerCommandHandler handler = new(services);

        WorkerResponse response = handler.Handle(new WorkerRequest("listScriptGroupEntries", ListRelativePath: "map.map_/0/scripts"));

        Assert.True(response.Ok, response.Message);
        Assert.Contains("testScript", Assert.IsAssignableFrom<IReadOnlyList<string>>(response.ScriptNames));
    }

    [Fact]
    public void McpScriptToRepository_Should_Persist_Repository_And_Replace_List_Entry()
    {
        string projectRoot = CreateTestProject();
        string inlineScriptPath = SystemIOWrapper.Combine(projectRoot, "map.map_", "0", "scripts", "testScript.code");
        SystemIOWrapper.WriteAllText(inlineScriptPath, "begincodeV2\nname:testScript\nlines:\n//test\nendcode\n");
        ProjectModel model = TestWorkspace.LoadProject(projectRoot);
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices services = new(model, new ProjectLogicContext(model), browser);
        using McpToolRegistry registry = new(services, projectRoot, null, registerHandlers: false);

        using JsonDocument requestDoc = JsonDocument.Parse("""
            {
              "device_relative_path": "map.map_/0",
              "script_name": "testScript",
              "script_group": "scripts"
            }
            """);
        MethodInfo? executeMethod = typeof(McpToolRegistry)
            .GetMethod("ExecuteScriptToRepository", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(executeMethod);

        ITuple? resultTuple = Assert.IsAssignableFrom<ITuple>(executeMethod.Invoke(registry, [requestDoc.RootElement]));
        Assert.NotNull(resultTuple);
        IReadOnlyList<ToolError> errors = Assert.IsAssignableFrom<IReadOnlyList<ToolError>>(resultTuple[1]);
        IReadOnlyList<string> affectedPathList = Assert.IsAssignableFrom<IReadOnlyList<string>>(resultTuple[3]);
        Assert.Empty(errors);

        using JsonDocument dataDoc = JsonDocument.Parse(JsonSerializer.Serialize(resultTuple[0], McpJsonOptions.Output));
        JsonElement payload = dataDoc.RootElement.GetProperty("data");
        string guid = payload.GetProperty("script_guid").ToString();
        string repositoryRelativePath = payload.GetProperty("repository_path").ToString();
        Assert.Equal("testScript.code", repositoryRelativePath);
        Assert.Contains(repositoryRelativePath, affectedPathList);
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "testScript.code")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "testScript.code.guid")));
        Assert.Equal(guid, SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "testScript.code.guid")));
        Assert.False(SystemIOWrapper.FileExists(inlineScriptPath));

        string listText = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "map.map_", "0", "scripts", "list.json"));
        ListJsonItem item = Assert.Single(HjsonText.ParseListItems(listText));
        Assert.Equal("testScript", item.Name);
        Assert.Equal(guid, item.Guid);
        Assert.True(item.IsCodeRepo);
    }

    [Fact]
    public void LinkRepository_WithForce_Should_Update_ListJson_And_Remove_Inline_Script()
    {
        string projectRoot = CreateTestProject();
        string inlineScriptPath = SystemIOWrapper.Combine(projectRoot, "map.map_", "0", "scripts", "testScript.code");
        SystemIOWrapper.WriteAllText(inlineScriptPath, "begincodeV2\nname:testScript\nlines:\n//inline\nendcode\n");
        string repoGuid = "ffffffffffffffffffffffffffffffff";
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRoot, "repoScript.code"), "begincodeV2\nname:repoScript\nlines:\n//repo\nendcode\n");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRoot, "repoScript.code.guid"), repoGuid);
        InitRepository(projectRoot);

        RefactorResult<ScriptLinkRepositoryData> result = _repository.LinkRepository(
            "map.map_/0",
            "testScript",
            "scripts",
            repoGuid,
            force: true);

        Assert.True(result.IsSuccess);
        ScriptLinkRepositoryData data = Assert.IsType<ScriptLinkRepositoryData>(result.Data);
        Assert.True(data.Applied);
        Assert.Equal(repoGuid, data.LinkedGuid);
        Assert.Equal("repoScript.code", data.RepositoryRelativePath);
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "repoScript.code")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "repoScript.code.guid")));
        Assert.False(SystemIOWrapper.FileExists(inlineScriptPath));

        string listText = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "map.map_", "0", "scripts", "list.json"));
        ListJsonItem item = Assert.Single(HjsonText.ParseListItems(listText));
        Assert.Equal("testScript", item.Name);
        Assert.Equal(repoGuid, item.Guid);
        Assert.True(item.IsCodeRepo);
    }

    [Fact]
    public void DeleteRepository_Should_Delete_Code_And_Guid_When_Unreferenced()
    {
        string projectRoot = CreateTestProject();
        string repoGuid = "fefefefefefefefefefefefefefefefe";
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRoot, "lonely.code"), "begincodeV2\nname:lonely\nlines:\n//repo\nendcode\n");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRoot, "lonely.code.guid"), repoGuid);
        InitRepository(projectRoot);

        RefactorResult<ScriptDeleteRepositoryData> result = _repository.DeleteRepository(repoGuid);

        Assert.True(result.IsSuccess);
        ScriptDeleteRepositoryData data = Assert.IsType<ScriptDeleteRepositoryData>(result.Data);
        Assert.True(data.Applied);
        Assert.Equal(repoGuid, data.DeletedGuid);
        Assert.Equal("lonely.code", data.RepositoryRelativePath);
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "lonely.code")));
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "lonely.code.guid")));
    }

    #endregion

    #region Helper Methods

    private string CreateTestProject(bool withListJson = true)
    {
        string projectRoot = TempDir;

        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(projectRoot, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(projectRoot, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");

        // Create map structure
        // 注意：地图派生目录（.map_）下直接是零件目录，禁止嵌套机械目录！
        string mapDir = SystemIOWrapper.Combine(projectRoot, "map.map_");
        SystemIOWrapper.CreateDirectory(mapDir);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "{\"list\":[\"0-1001\"]}");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "{\"list\":[]}");

        // 地图 GUID 文件和主文件
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRoot, "map.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRoot, "map.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });

        // 零件直接在地图派生目录下（数字编号目录）
        // 禁止 map.map_/map.map_/0 这种纯属幻觉垃圾的嵌套结构！地图不含机械！
        string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
        string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
        SystemIOWrapper.CreateDirectory(scriptsDir);

        // Create device config
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type: 1001\next:\n{\nlogicName: test\n}");

        if (withListJson)
        {
            // Create list.json with simple script list
            string listContent = "[\n  testScript\n]";
            string listPath = SystemIOWrapper.Combine(scriptsDir, "list.json");
            SystemIOWrapper.WriteAllText(listPath, listContent);
        }

        return projectRoot;
    }

    #endregion

    #region Real Data Patterns

    [Fact]
    public void Real_Data_Script_Name_From_Test_Project()
    {
        // Arrange - Real script name from test project
        string scriptName = "使用帮助";

        // Act & Assert - Validate should pass for valid Chinese script name
        Exception exception = Record.Exception(() => RefactorHelper.ValidateScriptName(scriptName));
        Assert.Null(exception);
    }

    [Fact]
    public void Real_Data_Valid_Guid_Format()
    {
        // Arrange
        string validGuid = "0123456789abcdef0123456789abcdef";

        // Act & Assert
        Assert.True(GuidText.Validate(validGuid));
    }

    #endregion
}
