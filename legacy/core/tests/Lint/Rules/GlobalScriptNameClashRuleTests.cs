using Xunit;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint.Rules;

public sealed class GlobalScriptNameClashRuleTests
{
    [Fact]
    public void FromDevice_Should_Warn_When_GlobalVar_And_UserScript_Have_Same_Name()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");
        device.SetVarsInternal(new VarsConfig
        {
            Variables = [new("Init", "Float", null)]
        });

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Init", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Init",
            "begincodeV2\nname:Init\nlines:\nfun debug.enable(Bool 1)\nendcode");

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device);

        LintDiagnostic diagnostic = Assert.Single(diagnostics, static d => d.RuleId == LintRuleIds.GlobalClashScriptName);
        Assert.Equal(LintSeverity.Warning, diagnostic.Severity);
        Assert.Equal(device.vars配置_完整相对路径, diagnostic.Location.Path);
        Assert.Contains("TS 视图", diagnostic.Message);
    }

    [Fact]
    public void FromDevice_Should_Warn_When_GlobalVar_And_EventScript_Have_Same_Name()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");
        device.SetVarsInternal(new VarsConfig
        {
            Variables = [new("Handler", "Float", null)]
        });

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device, "clientStart");
        group.SetScriptEntriesInternal([new("Handler", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Handler",
            "begincodeV2\nname:Handler\nlines:\nfun debug.enable(Bool 1)\nendcode");

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device);

        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.GlobalClashScriptName &&
            d.Severity == LintSeverity.Warning);
    }

    [Fact]
    public void FromDevice_Should_Warn_When_GlobalVar_And_CodeRepo_Link_Name_Have_Same_Name()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");
        device.SetVarsInternal(new VarsConfig
        {
            Variables = [new("RepoAction", "Float", null)]
        });

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("RepoAction", TestEntityFactory.NewGuid(), true)]);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device);

        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.GlobalClashScriptName &&
            d.Severity == LintSeverity.Warning);
    }
}
