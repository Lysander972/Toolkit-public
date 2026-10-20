using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint.Rules;

/// <summary>
/// Any 参数规则测试 - 使用实体模型。
/// </summary>
public sealed class AnyParamRuleTests
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
    public void ValidateBuiltinMethodCallSignatures_Should_Error_When_AnyParam_IsNonAnyConstant()
    {
        // 使用新基础设施创建 DeviceEntity
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall("varf List.listVar.add(Float 1)");

        // 新模型：需要 ScriptAnalysisService
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallParamAnyMustBeVar && d.Severity == LintSeverity.Error);
    }

    [Fact]
    public void ValidateBuiltinMethodCallSignatures_Should_NotError_When_AnyParam_IsAnyConst()
    {
        // 使用新基础设施创建 DeviceEntity
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall("varf List.listVar.add(Any )");

        // 新模型：需要 ScriptAnalysisService
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallParamAnyMustBeVar);
    }
}
