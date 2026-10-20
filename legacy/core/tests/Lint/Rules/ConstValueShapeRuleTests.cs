using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint.Rules;

/// <summary>
/// 常量值形状规则测试 - 使用实体模型。
/// </summary>
public sealed class ConstValueShapeRuleTests
{
    private static ScriptAnalysisService CreateAnalysisService(DeviceEntity device)
    {
        using ProjectModel model = ProjectModel.CreateForTesting();
        // 获取根实体并注册
        ProjectEntity? current = device;
        while (current is ChildProjectEntity child)
            current = child.Parent;

        if (current is ProjectRootEntity root)
            model.SetRootForTesting(root);

        return new ScriptAnalysisService(model);
    }

    [Fact]
    public void ValidateBuiltinMethodCallSignatures_Should_Error_When_UnsupportedConstType_HasValue()
    {
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall("fun mech.deviceNotNull(Device 1)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallConstValueMustBeBlank && d.Severity == LintSeverity.Error);
    }

    [Fact]
    public void ValidateBuiltinMethodCallSignatures_Should_NotError_When_EnumConst_HasValue()
    {
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall("fun mech.deviceNotNull(Enum 3)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallConstValueMustBeBlank);
    }

    [Fact]
    public void ValidateBuiltinMethodCallSignatures_Should_NotError_When_CreateCanvas_CanvasConst_HasValue()
    {
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall("fun create.canvas(Canvas abc)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallConstValueMustBeBlank);
    }
}
