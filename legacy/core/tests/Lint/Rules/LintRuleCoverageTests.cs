using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint.Rules;

public sealed class LintRuleCoverageTests
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
    public void ValidateBuiltinMethodCallSignatures_Should_Error_On_ParamCount_Mismatch()
    {
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall("fun debug.enableMethodCallLog(String 1:String 2)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallParamSignatureMismatch && d.Severity == LintSeverity.Error);
    }

    [Fact]
    public void ValidateBuiltinMethodCallSignatures_Should_Warn_When_StructGetter_NoCopy()
    {
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall("varf Struct.s.getFloat(String key:Bool 0)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.StructGetterNoCopy && d.Severity == LintSeverity.Warning);
    }

    [Fact]
    public void ValidateBuiltinMethodCallSignatures_Should_Warn_When_Call_NotRecommended_Or_Obsolete()
    {
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall("fun audio.playEffect()");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallNotRecommended && d.Severity == LintSeverity.Warning);
    }

    [Fact]
    public void ValidateBuiltinMethodCallSignatures_Should_Not_Warn_For_DeviceVarf_SwitchCollider()
    {
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall("varf Device.dev.switchCollider(Bool 0)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateBuiltinMethodCallSignatures(device, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallNotRecommended);
    }

    [Fact]
    public void ValidateEventDeviceMatch_Should_Error_When_Event_Not_Allowed_For_DeviceType()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 0, logicName: "logic");

        TestEntityFactory.CreateScriptGroup(device, "clientStart");

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateEventDeviceMatch(device);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.EventDeviceMismatch && d.Severity == LintSeverity.Error);
    }

    [Fact]
    public void FromDevice_Should_Surface_GlobalAndNaming_Clash_Rules()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1101, logicName: "logic");
        device.SetVarsInternal(new VarsConfig
        {
            Variables =
            [
                new("$player", "Player", null),
                new("dup", "Float", null)
            ]
        });

        ScriptGroupEntity scriptGroup = TestEntityFactory.CreateScriptGroup(device);
        TestEntityFactory.CreateInlineScript(
            scriptGroup,
            "setHP",
            @"begincodeV2
name:setHP
lines:
fun break()
endcode");

        ScriptAnalysisService analysisService = CreateAnalysisService(device);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, analysisService);
        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.GlobalClashBuiltinParam);
        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.NamingClashBuiltinMethod);
    }

    [Fact]
    public void FromDevice_Should_Not_Report_BuiltinNameClash_For_Normal_Act_Call()
    {
        DeviceEntity device = LintTestHelpers.CreateDeviceWithOutgoingCall("act 101.this.setHP(Float 1)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.NamingClashBuiltinMethod);
    }
}
