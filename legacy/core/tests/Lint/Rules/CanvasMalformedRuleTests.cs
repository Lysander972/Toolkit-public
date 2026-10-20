using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Canvas;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint.Rules;

public sealed class CanvasMalformedRuleTests
{
    [Fact]
    public void AnalyzeProject_Should_Report_Error_For_Malformed_Standalone_Canvas()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        byte[] malformedBytes = BuildMalformedInlineCanvasBytes();
        TestEntityFactory.CreateCanvas(root, "ui/bad.canvas_", TestEntityFactory.NewGuid(), malformedBytes);

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);

        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysis);

        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.StructCanvasMalformed &&
            d.Severity == LintSeverity.Error &&
            d.Message.Contains("padding", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeProject_Should_Report_Error_For_Malformed_Inline_Canvas()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "logic");
        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);

        string inlineArg = BuildMalformedInlineCanvasArg("bad_inline");
        string scriptText =
            "begincodeV2\n" +
            "name:test\n" +
            "lines:\n" +
            $"fun create.canvas(Canvas {inlineArg})\n" +
            "endcode";
        TestEntityFactory.CreateInlineScript(group, "test", scriptText);

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);

        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysis);

        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.StructCanvasMalformed &&
            d.Severity == LintSeverity.Error &&
            d.Location.Path.EndsWith("/test.code", StringComparison.Ordinal));
    }

#pragma warning disable TK006 // 测试辅助：构造畸形 canvas 内联参数
    private static string BuildMalformedInlineCanvasArg(string canvasName)
    {
        byte[] bytes = BuildMalformedInlineCanvasBytes();
        string base64 = Convert.ToBase64String(bytes).Replace("=", "@3", StringComparison.Ordinal);
        return $"canvas@1{canvasName}@3{base64}@3";
    }
#pragma warning restore TK006

    private static byte[] BuildMalformedInlineCanvasBytes()
    {
        DictVar root = new();
        root[new zchar("type")] = new StringVar { v = "UIRect" };
        root[new zchar("name")] = new StringVar { v = "content" };
        root[new zchar("size")] = new Vector3Var { X = 550, Y = 350, Z = 1 };
        root[new zchar("BorderWidth")] = new FloatVar { v = 0 };
        root[new zchar("Radius")] = new FloatVar { v = 0 };
        root[new zchar("verticle_spacing")] = new FloatVar { v = 15 };
        root[new zchar("childExpendWidth")] = new BoolVar { v = true };
        // 故意不写 padding：这是原版会出错的畸形结构

        using BinWriter writer = new(256);
        writer.Write((byte)VarType.List);
        writer.Write((ushort)1);
        root.SaveToBinary(writer, 0);
        return writer.ToArray();
    }
}
