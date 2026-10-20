using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint.Rules;

/// <summary>
/// 隐式转字符串规则测试 - 使用实体模型。
/// </summary>
public sealed class ImplicitToStringRuleTests
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
    public void ValidateBuiltinMethodCallSignatures_Should_Error_When_StringParam_Disallows_ImplicitToString()
    {
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall("fun debug.enableMethodCallLog(Float 1)");

        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallParamImplicitToStringNotAllowed && d.Severity == LintSeverity.Error);
    }
}
