using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint.Rules;

/// <summary>
/// 探针：未注册全局函数 + 残留 Enum 参数时，LintEngine 是否会输出
/// "内置方法签名未注册" Error。
/// 复现 main.map_/329/event_clientUpdate/结束统计.code 中
/// fun str2File(Enum 0) 残留调用在 VSCode 端 Lint 报告为空的现象。
/// </summary>
public sealed class UnknownFunctionRegistrationRuleTests
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
    public void ValidateBuiltinMethodCallSignatures_Should_Error_When_GlobalFunNotRegistered()
    {
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall("fun str2File(Enum 0)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.LintEngineError &&
            d.Severity == LintSeverity.Error &&
            d.Message.Contains("str2File", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateBuiltinMethodCallSignatures_Should_Error_When_NestedInIf_And_GlobalFunNotRegistered()
    {
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall(
            "if{fun bool.equal(Bool 1:Bool 1)\n  fun str2File(Enum 0)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.LintEngineError &&
            d.Severity == LintSeverity.Error &&
            d.Message.Contains("str2File", StringComparison.Ordinal));
    }

    /// <summary>
    /// 回归测试：复现 main.map_/329/event_clientUpdate/结束统计.code 中
    /// fun str2File(Enum 0) 残留调用在 VSCode 端 Lint 报告为空的现象。
    ///
    /// 根因：DeviceEntityBase.Scripts 曾以 EventName 是否为空过滤脚本组，
    /// 导致事件脚本组（event_clientUpdate 等）下的脚本从不被
    /// LintEngine.ValidateBuiltinMethodCallSignatures 扫描，签名错误无法被报告。
    /// 修复后 Scripts 含全部用户脚本（事件 + 非事件），诊断必须能从顶层 AnalyzeProject 输出。
    /// </summary>
    [Fact]
    public void AnalyzeProject_Should_Error_When_UnknownGlobalFunCalledInsideEventScriptGroup()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "m");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 329, deviceType: 1001, logicName: "evdevice");

        ScriptGroupEntity eventGroup = TestEntityFactory.CreateScriptGroup(device, "clientUpdate");
        eventGroup.SetScriptEntriesInternal([new("结束统计", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(
            eventGroup,
            "结束统计",
            "begincodeV2\nname:结束统计\nlines:\nfun str2File(Enum 0)\nendcode");

        using ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysisService);

        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.LintEngineError &&
            d.Severity == LintSeverity.Error &&
            d.Message.Contains("str2File", StringComparison.Ordinal) &&
            d.Location.Path.Contains("结束统计", StringComparison.Ordinal));
    }
}
