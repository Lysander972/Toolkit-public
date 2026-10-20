using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint.Rules;

public sealed class CallTargetRuleTests
{
    [Fact]
    public void AnalyzeProject_Should_Warn_When_DeviceLogicName_Is_Duplicated_And_Act_Alias_Is_Ambiguous()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1101, logicName: "dup");
        DeviceEntity target = TestEntityFactory.CreateDevice(map, deviceId: 2, deviceType: 1101, logicName: "dup");

        AddUserScript(caller, "Caller", "act 1101.dup.Target()");
        AddUserScript(target, "Target", "fun debug.enable(Bool 1)");

        (ProjectModel model, ScriptAnalysisService analysisService) = CreateAnalysisContext(root);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysisService);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.StructDeviceLogicNameDuplicate && d.Severity == LintSeverity.Warning);
        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallActAliasNotUnique && d.Severity == LintSeverity.Warning);
    }

    [Fact]
    public void AnalyzeProject_Should_Error_When_Act_Target_Resolves_To_Self_Without_This()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1101, logicName: "selfAlias");
        AddUserScript(caller, "Caller", "act 1101.selfAlias.DoWork()");

        (ProjectModel model, ScriptAnalysisService analysisService) = CreateAnalysisContext(root);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysisService);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallActSelfMustUseThis && d.Severity == LintSeverity.Error);
    }

    [Fact]
    public void AnalyzeProject_Should_Error_When_Regular_Dynamic_Call_Target_Method_Does_Not_Exist()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1101, logicName: "caller");

        AddUserScript(caller, "Caller", "varf Device.this.callFun0(String MissingMethod)");
        AddUserScript(caller, "Existing", "fun debug.enable(Bool 1)");

        (ProjectModel model, ScriptAnalysisService analysisService) = CreateAnalysisContext(root);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysisService);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallDynamicMethodNotFound && d.Severity == LintSeverity.Error);
    }

    [Fact]
    public void AnalyzeProject_Should_Error_When_Map_Dynamic_Call_String_Format_Is_Invalid()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1101, logicName: "caller");

        AddUserScript(caller, "Caller", "act 1101.this.callClientMapRetBoolFunc0(String InvalidFormat)");

        (ProjectModel model, ScriptAnalysisService analysisService) = CreateAnalysisContext(root);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysisService);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallDynamicMapMethodFormat && d.Severity == LintSeverity.Error);
    }

    [Fact]
    public void AnalyzeProject_Should_Filter_Map_Host_Dynamic_Target_By_DeviceType_Zero()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1101, logicName: "caller");
        DeviceEntity type1101Only = TestEntityFactory.CreateDevice(map, deviceId: 2, deviceType: 1, logicName: "other");

        AddUserScript(caller, "Caller", "act 1101.this.callClientMapRetBoolFunc0(String core.HostMethod)");
        AddUserScript(type1101Only, "HostMethod", "fun debug.enable(Bool 1)");

        (ProjectModel model, ScriptAnalysisService analysisService) = CreateAnalysisContext(root);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysisService);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallDynamicMapMethodNotFound && d.Severity == LintSeverity.Error);
        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallDynamicMapMethodFormat);
    }

    private static InlineScriptEntity AddUserScript(DeviceEntity device, string scriptName, string line, bool isPrivate = false)
    {
        ScriptGroupEntity? scriptGroup = device.ScriptGroups.OfType<ScriptGroupEntity>().FirstOrDefault();
        if (scriptGroup == null)
        {
            scriptGroup = TestEntityFactory.CreateScriptGroup(device);
        }

        if (scriptGroup.ScriptEntries is not List<ScriptEntryInfo> entries)
        {
            throw new InvalidOperationException("ScriptEntries 不是可写列表");
        }

        entries.Add(new ScriptEntryInfo(scriptName, string.Empty, false, isPrivate));

        string sourceText = $"begincodeV2\nname:{scriptName}\nlines:\n{line}\nendcode";
        return TestEntityFactory.CreateInlineScript(scriptGroup, scriptName, sourceText);
    }

#pragma warning disable TK006 // 测试辅助：封装 model+analysis 创建
    private static (ProjectModel Model, ScriptAnalysisService AnalysisService) CreateAnalysisContext(ProjectRootEntity root)
    {
        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new(model);
        return (model, analysisService);
    }
#pragma warning restore TK006
}
