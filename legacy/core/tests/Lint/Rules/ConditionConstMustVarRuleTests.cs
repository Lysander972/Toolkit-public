using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint.Rules;

public sealed class ConditionConstMustVarRuleTests
{
    private static ScriptAnalysisService CreateAnalysisService(DeviceEntity device)
    {
        using ProjectModel model = ProjectModel.CreateForTesting();
        ProjectEntity? current = device;
        while (current is ChildProjectEntity child)
            current = child.Parent;

        if (current is ProjectRootEntity root)
            model.SetRootForTesting(root);

        return new ScriptAnalysisService(model);
    }

    [Fact]
    public void ValidateBuiltinMethodCallSignatures_Should_Error_When_If_Condition_Uses_Bool_Const()
    {
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall(
            "if{fun create.bool(Bool 1)\n  fun debug.enable(Bool 1)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallConditionConstMustBeVar && d.Severity == LintSeverity.Error);
    }

    [Fact]
    public void ValidateBuiltinMethodCallSignatures_Should_NotError_When_If_Condition_Uses_Variable()
    {
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall(
            "if{fun create.bool(var HP)\n  fun debug.enable(Bool 1)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallConditionConstMustBeVar);
    }

    [Fact]
    public void ValidateBuiltinMethodCallSignatures_Should_NotError_On_Bool_Or_Const_Condition()
    {
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall(
            "if{fun bool.or(Bool 1:Bool 0)\n  fun debug.enable(Bool 1)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallConditionConstMustBeVar);
    }
}
