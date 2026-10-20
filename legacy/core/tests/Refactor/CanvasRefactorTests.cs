using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Toolkit.Core.Tests.Common;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Refactor;
using Toolkit.Core.Worker;

namespace Toolkit.Core.Tests.Refactor;

/// <summary>
/// Tests for CanvasRefactor - Canvas extraction and import operations.
/// </summary>
[Collection("WorkspaceState")]
public sealed class CanvasRefactorTests : TempDirectoryTestFixture
{
    // 真实的 Canvas 二进制数据（从测试工程提取，不含文件头和名称）
    // 解码后以 VarType.List (0x10) 开头，是有效的 Canvas 根类型
    // Base64 中的 '=' 已转义为 '@3' 以符合 Bescript 语法
    private const string RealCanvasDataBase64 = "EAEAEQgABHR5cGUGClVJUmF3SW1hZ2UEbmFtZQYJ6IOM5pmv5Zu+A1BvcwQAAAAAAAAAAAAAAAAMcGFyZW50QW5jaG9yGAQAAAAAAAAABmFuY2hvchgEAAAAAAAAAARzaXplBAAA+kQAAPpEAACAPwVjb2xvcgzV1FQ+1dRUPtXUVD4AAIA/CGZpbGVHdWlkBiAwNTI5YTM4YzViMDQ0NzdlODRlNGQxZTRkOTI1MWUyZA@3@3";

    private CanvasRefactor? _refactor;
    private ProjectModel? _model;

    public CanvasRefactorTests() : base("Toolkit.Core.Tests.CanvasRefactor")
    {
    }

    [MemberNotNull(nameof(_model), nameof(_refactor))]
#pragma warning disable TK006 // 测试辅助：封装 model+refactor 初始化
    private void InitRefactor(string projectRoot)
    {
        _model = TestWorkspace.LoadProject(projectRoot);
        CanvasPreview preview = new CanvasPreview();
        _refactor = new CanvasRefactor(_model, preview);
    }
#pragma warning restore TK006

    #region Extract - Error Cases

    [Fact]
    public void Extract_Fail_When_Line_Is_Zero()
    {
        // Arrange
        string projectRoot = CreateTestProject();
        InitRefactor(projectRoot);
        string scriptPath = "mech1/device1/script.code";

        // Act
        RefactorResult<CanvasExtractData> result = _refactor.Extract(
            scriptPath,
            0
        );

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("line", result.Errors[0].Message);
    }

    [Fact]
    public void Extract_Fail_When_Line_Is_Negative()
    {
        // Arrange
        string projectRoot = CreateTestProject();
        InitRefactor(projectRoot);

        // Act
        RefactorResult<CanvasExtractData> result = _refactor.Extract(
            "test.code",
            -1
        );

        // Assert
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Extract_Fail_When_Script_Not_Found()
    {
        // Arrange
        string projectRoot = CreateTestProject();
        InitRefactor(projectRoot);

        // Act
        RefactorResult<CanvasExtractData> result = _refactor.Extract(
            "nonexistent.code",
            1
        );

        // Assert
        Assert.False(result.IsSuccess);
    }

    #endregion

    #region ImportByGuid - Error Cases

    [Fact]
    public void ImportByGuid_Fail_When_Script_Not_Found()
    {
        // Arrange
        string projectRoot = CreateTestProject();
        InitRefactor(projectRoot);

        // Act
        RefactorResult<CanvasImportData> result = _refactor.ImportByGuid(
            "nonexistent.code",
            1,
            "0123456789abcdef0123456789abcdef"
        );

        // Assert
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ImportByGuid_Fail_When_TargetGuid_Is_Invalid()
    {
        // Arrange - 使用内联格式，测试 targetGuid 参数无效的情况
        string projectRoot = CreateTestProject($"fun create.canvas(Canvas canvas@1test@3{RealCanvasDataBase64})");
        InitRefactor(projectRoot);

        // Act
        RefactorResult<CanvasImportData> result = _refactor.ImportByGuid(
            TestScriptRelativePath,
            4,
            "not-a-guid"
        );

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("格式无效", result.Errors[0].Message);
    }

    #endregion

    #region ImportByPath - Error Cases

    [Fact]
    public void ImportByPath_Fail_When_CanvasGuidFile_Not_Found()
    {
        // Arrange
        string projectRoot = CreateTestProject("fun create.canvas(Canvas canvas@1test@3base64@3)");
        InitRefactor(projectRoot);

        // Act
        RefactorResult<CanvasImportData> result = _refactor.ImportByPath(
            TestScriptRelativePath,
            4,
            "layouts/test.canvas"
        );

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("无法加载 Canvas", result.Errors[0].Message);
    }

    #endregion

    #region CanvasName 规则

    [Theory]
    [InlineData("test.canvas", true)]
    [InlineData("test canvas", true)]
    [InlineData("UI_加载界面", true)]
    [InlineData("test/canvas", false)]
    [InlineData("test:canvas", false)]
    [InlineData("test*canvas?", false)]
    [InlineData("test...", false)]
    [InlineData("test ", false)]
#pragma warning disable TK009 // attribute 参数必须是编译时常量，空串字面量是唯一选择
    [InlineData("", false)]
#pragma warning restore TK009
    public void CanvasName_Rule_ShouldMatchFileNameCompatibility(string input, bool expectedValid)
    {
        Assert.Equal(expectedValid, CanvasName.IsValid(input));
    }
    #endregion

    #region Helper Methods

    private string CreateTestProject(string? scriptContent = null)
    {
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");

        // 创建 UI 目录（Canvas Extract 需要此目录存在）
        string uiDir = SystemIOWrapper.Combine(TempDir, "ui");
        SystemIOWrapper.CreateDirectory(uiDir);

        // 创建地图结构（地图派生目录下直接是零件目录，不含机械）
        string mapDir = SystemIOWrapper.Combine(TempDir, "map.map_");
        SystemIOWrapper.CreateDirectory(mapDir);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "{\"list\":[\"0-1001\"]}");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "{\"list\":[]}");

        // 地图 GUID 文件和主文件
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "map.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(TempDir, "map.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });

        // 零件目录（数字编号）
        string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
        string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
        SystemIOWrapper.CreateDirectory(scriptsDir);

        // device.json
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type: 1001\next:\n{\n}");

        // list.json（包含测试脚本）
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n  test\n]");

        // 如果提供了脚本内容，在项目加载前创建脚本
        if (scriptContent != null)
        {
            string scriptPath = SystemIOWrapper.Combine(scriptsDir, "test.code");
            string text = $"begincodeV2\nname:test\nlines:\n{scriptContent}\nendcode";
            SystemIOWrapper.WriteAllText(scriptPath, text);
        }

        return TempDir;
    }

    private string TestScriptRelativePath => "map.map_/0/scripts/test.code";

    #endregion

    #region Real Data Patterns

    [Fact]
    public void Real_Data_CreateCanvas_Pattern()
    {
        // Arrange - Real pattern from test project scripts with real Canvas data
        string projectRoot = CreateTestProject($"fun create.canvas(Canvas canvas@1UI_按钮背景@3{RealCanvasDataBase64})");
        InitRefactor(projectRoot);

        // Act
        RefactorResult<CanvasExtractData> result = _refactor.Extract(
            TestScriptRelativePath,
            4
        );

        // Assert - Should parse the canvas@ pattern successfully
        Assert.True(result.IsSuccess, $"Extract should succeed with real Canvas data: {string.Join(", ", result.Errors.Select(static e => e.Message))}");
    }

    #endregion

    #region Round-Trip Tests

    /// <summary>
    /// 测试 Extract→Inline 往返操作成功。
    /// 验证：Extract 成功生成 GUID 引用，Inline 成功恢复内联格式。
    /// </summary>
    [Fact]
    public void RoundTrip_Extract_Then_Inline_Restores_Original_Script()
    {
        // Arrange - 创建包含内联 canvas 的脚本（使用 LF 换行符，符合规范）
        string projectRoot = CreateTestProject($"fun create.canvas(Canvas canvas@1TestCanvas@3{RealCanvasDataBase64})");

        InitRefactor(projectRoot);

        // Act 1 - Extract: 内联→独立
        RefactorResult<CanvasExtractData> extractResult = _refactor.Extract(
            TestScriptRelativePath,
            4
        );

        // Assert Extract succeeded
        Assert.True(extractResult.IsSuccess, $"Extract failed: {string.Join(", ", extractResult.Errors.Select(static e => e.Message))}");
        Assert.NotNull(extractResult.Data);
        string extractedGuid = extractResult.Data.ExtractedGuid;
        Assert.True(GuidText.Validate(extractedGuid), $"Extracted GUID should be 32 hex chars: {extractedGuid}");

        // Verify script was modified to use GUID reference
        string afterExtractContent = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, TestScriptRelativePath));
        Assert.Contains($"guid@1{extractedGuid}", afterExtractContent);
        Assert.DoesNotContain("canvas@1TestCanvas", afterExtractContent);

        // Act 2 - Inline: 独立→内联
        RefactorResult<CanvasInlineData> inlineResult = _refactor.Inline(
            TestScriptRelativePath,
            4
        );

        // Assert Inline succeeded
        Assert.True(inlineResult.IsSuccess, $"Inline failed: {string.Join(", ", inlineResult.Errors.Select(static e => e.Message))}");
        Assert.NotNull(inlineResult.Data);
        Assert.Equal("TestCanvas", inlineResult.Data.InlinedCanvasName);

        // Assert - Inline 输出新版 Hjson 格式（非原始 base64），验证语义等价
        string restoredText = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, TestScriptRelativePath));
        Assert.Contains("==hjson@1TestCanvas@1", restoredText, StringComparison.Ordinal);
        Assert.Contains("==)", restoredText, StringComparison.Ordinal);

        // 验证 Inline 后的内联内容可正确解析，且与原始内容语义一致
        string originalInlineArg = $"canvas:TestCanvas={BescriptTextCodec.RecoverInvalidChar(RealCanvasDataBase64.AsSpan())}";
        CanvasNodeResult originalResult = CanvasConverter.FromInline(originalInlineArg);
        Assert.True(originalResult.Ok);

        // 从恢复的脚本中提取 Hjson 内联参数并解析
        int fullArgStart = restoredText.IndexOf("==hjson@1", StringComparison.Ordinal);
        int fullArgEnd = restoredText.IndexOf("==)", StringComparison.Ordinal) + "==".Length;
        string rawArg = restoredText[fullArgStart..fullArgEnd];
        string decodedArg = BescriptTextCodec.RecoverInvalidChar(rawArg.AsSpan());
        CanvasNodeResult restoredResult = CanvasConverter.FromInline(decodedArg);
        Assert.True(restoredResult.Ok);

        // 两次解析的二进制输出必须一致（语义等价）
        Assert.NotNull(originalResult.Root);
        Assert.NotNull(restoredResult.Root);
        byte[] originalBytes = CanvasConverter.ToBinary(originalResult.Root, originalResult.CanvasName);
        byte[] restoredBytes = CanvasConverter.ToBinary(restoredResult.Root, "TestCanvas");
        Assert.Equal(originalBytes, restoredBytes);
    }

    /// <summary>
    /// 测试 Extract 生成的 GUID 是标准 32 位十六进制。
    /// </summary>
    [Fact]
    public void Extract_Generates_Valid_32Char_Guid()
    {
        // Arrange - 使用真实 Canvas 数据
        string projectRoot = CreateTestProject($"fun create.canvas(Canvas canvas@1MyCanvas@3{RealCanvasDataBase64})");
        InitRefactor(projectRoot);

        // Act
        RefactorResult<CanvasExtractData> result = _refactor.Extract(
            TestScriptRelativePath,
            4
        );

        // Assert
        Assert.True(result.IsSuccess);
        CanvasExtractData data = Assert.IsType<CanvasExtractData>(result.Data);
        Assert.True(data.Applied);
        string guid = data.ExtractedGuid;
        Assert.Equal(32, guid.Length);
        Assert.True(GuidText.Validate(guid), $"GUID should be 32 hex chars: {guid}");

        Assert.Contains(TestScriptRelativePath, data.AffectedPaths);
        Assert.Contains(data.CanvasRelativePath, data.AffectedPaths);
        Assert.Contains(data.CanvasRelativePath + ProjectEntityLayout.GuidExt, data.AffectedPaths);
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, data.CanvasRelativePath)));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, data.CanvasRelativePath + ProjectEntityLayout.GuidExt)));

        string scriptText = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, TestScriptRelativePath));
        Assert.Contains($"guid@1{guid})", scriptText);
        Assert.DoesNotContain("canvas@1MyCanvas", scriptText);

        IReadOnlyList<BescriptCanvasArgRange> ranges = new BescriptScanner().ScanCanvasArgs(scriptText);
        BescriptGuidCanvasArgRange guidRange = Assert.IsType<BescriptGuidCanvasArgRange>(Assert.Single(ranges));
        Assert.Equal(guid, guidRange.Guid);
    }

    [Fact]
    public void WorkerCanvasExtract_Should_Persist_Canvas_And_Replace_Script_Guid_Reference()
    {
        string projectRoot = CreateTestProject($"fun create.canvas(Canvas canvas@1WorkerCanvas@3{RealCanvasDataBase64})");
        ProjectModel model = TestWorkspace.LoadProject(projectRoot);
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices services = new(model, new ProjectLogicContext(model), browser);
        WorkerCommandHandler handler = new(services);

        WorkerResponse response = handler.Handle(new WorkerRequest(
            "canvasExtract",
            ScriptRelativePath: TestScriptRelativePath,
            Line: 4));

        Assert.True(response.Ok, response.Message);
        CanvasExtractData data = Assert.IsType<CanvasExtractData>(response.CanvasExtract);
        Assert.True(data.Applied);
        Assert.Contains(TestScriptRelativePath, data.AffectedPaths);
        Assert.Contains(data.CanvasRelativePath, data.AffectedPaths);
        Assert.Contains(data.CanvasRelativePath + ProjectEntityLayout.GuidExt, data.AffectedPaths);
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, data.CanvasRelativePath)));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, data.CanvasRelativePath + ProjectEntityLayout.GuidExt)));

        string scriptText = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, TestScriptRelativePath));
        Assert.DoesNotContain("canvas@1WorkerCanvas", scriptText);
        BescriptGuidCanvasArgRange guidRange = Assert.IsType<BescriptGuidCanvasArgRange>(Assert.Single(new BescriptScanner().ScanCanvasArgs(scriptText)));
        Assert.Equal(data.ExtractedGuid, guidRange.Guid);
    }

    [Fact]
    public void McpCanvasExtract_Should_Persist_Canvas_And_Replace_Script_Guid_Reference()
    {
        string projectRoot = CreateTestProject($"fun create.canvas(Canvas canvas@1McpCanvas@3{RealCanvasDataBase64})");
        ProjectModel model = TestWorkspace.LoadProject(projectRoot);
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices services = new(model, new ProjectLogicContext(model), browser);
        using McpToolRegistry registry = new(services, projectRoot, null, registerHandlers: false);

        using JsonDocument requestDoc = JsonDocument.Parse("""
            {
              "script_relative_path": "map.map_/0/scripts/test.code",
              "line": 4
            }
            """);
        MethodInfo? executeMethod = typeof(McpToolRegistry)
            .GetMethod("ExecuteCanvasExtract", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(executeMethod);

        ITuple resultTuple = Assert.IsAssignableFrom<ITuple>(executeMethod.Invoke(registry, [requestDoc.RootElement]));
        Assert.NotNull(resultTuple);
        IReadOnlyList<ToolError> errors = Assert.IsAssignableFrom<IReadOnlyList<ToolError>>(resultTuple[1]);
        IReadOnlyList<string> affectedPathList = Assert.IsAssignableFrom<IReadOnlyList<string>>(resultTuple[3]);
        Assert.Empty(errors);

        using JsonDocument dataDoc = JsonDocument.Parse(JsonSerializer.Serialize(resultTuple[0], McpJsonOptions.Output));
        JsonElement payload = dataDoc.RootElement.GetProperty("data");
        JsonElement guidElement = payload.GetProperty("extracted_guid");
        JsonElement canvasPathElement = payload.GetProperty("canvas_path");
        Assert.Equal(JsonValueKind.String, guidElement.ValueKind);
        Assert.Equal(JsonValueKind.String, canvasPathElement.ValueKind);
        string guid = guidElement.ToString();
        string canvasRelativePath = canvasPathElement.ToString();
        Assert.Contains(TestScriptRelativePath, affectedPathList);
        Assert.Contains(canvasRelativePath, affectedPathList);
        Assert.Contains(canvasRelativePath + ProjectEntityLayout.GuidExt, affectedPathList);
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, canvasRelativePath)));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, canvasRelativePath + ProjectEntityLayout.GuidExt)));

        string scriptText = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, TestScriptRelativePath));
        Assert.DoesNotContain("canvas@1McpCanvas", scriptText);
        BescriptGuidCanvasArgRange guidRange = Assert.IsType<BescriptGuidCanvasArgRange>(Assert.Single(new BescriptScanner().ScanCanvasArgs(scriptText)));
        Assert.Equal(guid, guidRange.Guid);
    }

    /// <summary>
    /// 测试 Inline 后如果没有其他引用，canvas 文件应被删除。
    /// </summary>
    [Fact]
    public void Inline_Deletes_Canvas_File_When_No_Other_References()
    {
        // Arrange - 使用真实 Canvas 数据
        string projectRoot = CreateTestProject($"fun create.canvas(Canvas canvas@1DeleteMe@3{RealCanvasDataBase64})");
        InitRefactor(projectRoot);

        // Extract first
        RefactorResult<CanvasExtractData> extractResult = _refactor.Extract(TestScriptRelativePath, 4);
        Assert.True(extractResult.IsSuccess);
        // 新模型：CanvasRelativePath 替代 CanvasPath
        Assert.NotNull(extractResult.Data);
        string canvasPath = SystemIOWrapper.Combine(projectRoot, extractResult.Data.CanvasRelativePath);
        string guidPath = canvasPath + ".guid";
        Assert.True(SystemIOWrapper.FileExists(canvasPath), "Canvas file should exist after extract");
        Assert.True(SystemIOWrapper.FileExists(guidPath), "GUID file should exist after extract");

        // Act - Inline
        RefactorResult<CanvasInlineData> inlineResult = _refactor.Inline(TestScriptRelativePath, 4);

        // Assert
        Assert.True(inlineResult.IsSuccess);
        Assert.NotNull(inlineResult.Data);
        Assert.True(inlineResult.Data.CanvasFileDeleted, "Canvas file should be deleted when no other references");
        Assert.False(SystemIOWrapper.FileExists(canvasPath), "Canvas file should not exist after inline");
        Assert.False(SystemIOWrapper.FileExists(guidPath), "GUID file should not exist after inline");
    }

    #endregion
}
