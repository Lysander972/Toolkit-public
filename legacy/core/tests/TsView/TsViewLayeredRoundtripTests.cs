using Xunit;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.TsView;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.TsView;

/// <summary>
/// 分层回环测试：将全链路拆分为独立层，精确定位问题所在层。
/// </summary>
[Collection("WorkspaceState")]
public sealed class TsViewLayeredRoundtripTests
{
    /// <summary>
    /// 测试1：BeScript 文本 → Parse → Build → SerializeFull → 文本
    /// 验证 BescriptAst 层自身的序列化/反序列化一致性。
    /// </summary>
    [Fact]
    public void Layer1_BescriptAst_SerializeFull_ShouldMatch_Original()
    {
        foreach (ProjectDirectory project in TestProjectEnumerator.EnumerateProjects())
        {
            ProjectModel model = TestWorkspace.LoadProjectWithRecordingPersistence(project.Path);
            List<(string deviceDir, string codePath)> deviceScriptPaths = CollectDeviceScripts(project.Path, model);

            ParallelTestRunner.RunParallel(
                deviceScriptPaths,
                ParallelTestRunner.SafeTest<(string deviceDir, string codePath)>(item =>
                {
                    string originalText = SystemIOWrapper.ReadAllText(item.codePath);
                    (BescriptScriptInfo info, List<ToolError> errors, List<ToolError> _) = BescriptTextParser.Parse(item.codePath, originalText);
                    if (errors.Count > 0) throw new Exception($"BES 解析失败: {item.codePath}: {errors[0].Message}");

                    BescriptAst ast = info.Ast;
                    string serialized = BescriptAstSerializer.SerializeFull(ast);

                    string? diff = TestComparison.DiffText(originalText, serialized);
                    if (diff != null)
                        throw new Exception($"Layer1 BesAst roundtrip 不一致: [{project.Name}] {item.codePath}\n{diff}");
                }),
                contextName: $"Layer1_BesAst:{project.Name}");
        }
    }

    /// <summary>
    /// 测试2：写回路径内容一致性。
    /// .code → 生成 TS 视图 → TryParseDeviceTsViewForWriteback → ScriptUpdate → BuildScriptText → .code 文本
    /// </summary>
    [Fact]
    public void Layer2_WritebackPath_ShouldMatch_Original()
    {
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        foreach (ProjectDirectory project in TestProjectEnumerator.EnumerateProjects())
        {
            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(project.Path, browser);

            string[] devicePaths = services.Model.ListDevices()
                .Select(static d => d.实体_完整相对路径标识)
                .ToArray();

            foreach (string? devicePath in devicePaths)
            {
                (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read(devicePath);
                if (readErrors.Count > 0)
                    Assert.Fail($"[{project.Name}] {devicePath}: Read failed: {readErrors[0]}");
                if (readData == null)
                    Assert.Fail($"[{project.Name}] {devicePath}: Read returned null data");

                List<string> lines = TextLines.SplitLines(readData.TsView).ToList();
                List<ToolError> warnings = [];

                DeviceTsView.WritebackParseResult parseResult = DeviceTsView.ParseDeviceTsViewForWriteback(
                    services.Model, lines, devicePath, warnings,
                    collectedErrors: null);

                if (parseResult.Error != null)
                    Assert.Fail($"[{project.Name}] {devicePath}: Writeback parse failed: {parseResult.Error.Message}");

                IReadOnlyList<DeviceTsView.ScriptUpdate> bescriptUpdates = TsViewAsyncValidation.RewriteAsyncForBescriptUpdates(parseResult.Updates);

                // 对每个 ScriptUpdate，用 BuildScriptText 重建并与原始 .code 比较
                foreach (DeviceTsView.ScriptUpdate update in bescriptUpdates)
                {
                    string rebuilt = EntityWritebackDispatcher.BuildScriptText(update);
                    string originalPath = SystemIOWrapper.Combine(project.Path, update.SourcePath);
                    string originalText = SystemIOWrapper.ReadAllText(originalPath);

                    string? diff = TestComparison.DiffText(originalText, rebuilt);
                    if (diff != null)
                    {
                        string? mismatch = TestComparison.VerifyWarningCorrespondence(originalText, rebuilt, warnings);
                        if (mismatch is not null)
                            throw new Exception($"Layer2 警告-差异对应失败: [{project.Name}] {update.SourcePath}\n{mismatch}\n{diff}");
                    }
                }
            }
        }
    }

    [Fact]
    public void Layer3_OfficialTsParserPrinter_ShouldBe_Idempotent()
    {
        string[] bodies =
        {
            "const x: BeFloat = G.create.float(BeFloat.fromBeConst(\"1\"));\nreturn;",
            "if (G.create.bool(BeBool.fromBeConst(\"1\"))) {\n    G.log.print(BeString.fromBeConst(\"ok\"));\n}\nreturn;",
            "while (G.create.bool(BeBool.fromBeConst(\"1\"))) {\n    break;\n}\nreturn;"
        };

        foreach (string body in bodies)
        {
            MethodBodyFormatResult first = FormatMethodBody(body);
            MethodBodyFormatResult second = FormatMethodBody(first.Text);
            Assert.Equal(first.Text, second.Text);
        }
    }

    private static MethodBodyFormatResult FormatMethodBody(string body)
    {
        string json = TsCompilerHost.FormatMethodBody(body);
        MethodBodyFormatResult? result = System.Text.Json.JsonSerializer.Deserialize<MethodBodyFormatResult>(
            json,
            BodyWritebackJson.Options);
        if (result is null)
            throw new InvalidOperationException("TS 官方方法体格式化返回空结果。");
        if (result.Error is not null)
            throw new InvalidOperationException(result.Error);
        return result;
    }

    private sealed record MethodBodyFormatResult(string Text, string? Error, int? ErrorLine);

    private static List<(string deviceDir, string codePath)> CollectDeviceScripts(string projectPath, ProjectModel model)
    {
        List<(string, string)> result = [];
        foreach (string? devicePath in model.ListDevices().Select(static d => d.实体_完整相对路径标识))
        {
            string deviceDir = SystemIOWrapper.Combine(projectPath, devicePath);
            foreach (string codePath in TestProjectEnumerator.EnumerateDeviceScripts(deviceDir))
                result.Add((deviceDir, codePath));
        }
        return result;
    }

}
