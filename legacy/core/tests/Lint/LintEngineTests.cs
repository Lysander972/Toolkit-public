using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint;

/// <summary>
/// LintEngine 测试 - 使用实体模型替代已删除的解析模型。
/// 统一入口：ProjectModel.Load() + LintEngine.AnalyzeProject。
/// </summary>
public class LintEngineTests
{
    [Fact]
    public void AnalyzeProject_Should_ProduceDiagnostics_WithRuleIds()
    {
        IEnumerable<string> projectDirs = TestProjectEnumerator.DiscoverMapRoots(ToolkitRepoPaths.ProjectsRoot);
        Assert.NotEmpty(projectDirs);

        HashSet<string> knownRuleIds =
        [
            LintRuleIds.AstParseError,
            LintRuleIds.LintEngineError,
            LintRuleIds.NamingReservedKeyword,
            LintRuleIds.NamingClashBuiltinMethod,
            LintRuleIds.CallParamSignatureMismatch,
            LintRuleIds.CallNotRecommended,
            LintRuleIds.CallParamImplicitToStringNotAllowed,
            LintRuleIds.CallParamAnyMustBeVar,
            LintRuleIds.CallConditionConstMustBeVar,
            LintRuleIds.CallConstValueMustBeBlank,
            LintRuleIds.EventDeviceMismatch,
            LintRuleIds.StructMissingIdentity,
            LintRuleIds.StructDeviceMissingJson,
            LintRuleIds.StructMechMissingJson,
            LintRuleIds.StructMechDeviceNotFound,
            LintRuleIds.StructMechOrphanDevice,
            LintRuleIds.StructScriptOrphan,
            LintRuleIds.StructGuidDuplicate,
            LintRuleIds.StructMapMultiple,
            LintRuleIds.StructUiNameMismatch,
            LintRuleIds.StructUiInlineBinary,
            LintRuleIds.CallRecursion,
            LintRuleIds.CallRecursionPotential,
            LintRuleIds.StructDeviceLogicNameDuplicate,
            LintRuleIds.CallActAliasNotUnique,
            LintRuleIds.CallActSelfMustUseThis,
            LintRuleIds.CallDynamicMethodNotFound,
            LintRuleIds.CallDynamicMapMethodFormat,
            LintRuleIds.CallDynamicMapMethodNotFound,
            LintRuleIds.ParamClashGlobal,
            LintRuleIds.GlobalClashScriptName,
            LintRuleIds.GlobalClashBuiltinParam,
            LintRuleIds.GlobalClashReturn,
            LintRuleIds.VarGlobalTypeMismatch,
            LintRuleIds.StructGetterNoCopy,
            LintRuleIds.StructTableFormat,
            LintRuleIds.StructColorPaletteFormat,
            LintRuleIds.StructCanvasMalformed,
            LintRuleIds.UnsupportedTextCharacter,
            LintRuleIds.FindExcel2KeyMustNotBeId,
            LintRuleIds.CallCanvasUiPathNotFound,
            LintRuleIds.NamingClashEventName,
            LintRuleIds.BirthBuildingEventNameClashCore,
            LintRuleIds.NamingApostropheDiscouraged,
            LintRuleIds.VarUnused,
            LintRuleIds.VarInlinableConst,
            LintRuleIds.VarInlinableExpr,
            LintRuleIds.CallFilenameNotFound
        ];

        foreach (string projectDir in projectDirs)
        {
            ProjectModel model = TestWorkspace.LoadProjectWithRecordingPersistence(projectDir);
            if (model.Root.Children.Count == 0)
            {
                continue;
            }

            ScriptAnalysisService analysisService = new ScriptAnalysisService(model);
            IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysisService);

            if (diagnostics.Count > 0)
            {
                Assert.All(diagnostics, d =>
                {
                    Assert.NotEmpty(d.Message);
                    Assert.Contains(d.RuleId, knownRuleIds);
                });
            }
        }
    }

    [Fact]
    public void LintEngine_Should_Detect_Keyword_Names()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "logic");
        device.SetVarsInternal(new VarsConfig
        {
            Variables =
            [
                new("if", "Void", null),
                new("变量", "Void", null)
            ]
        });

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.NamingReservedKeyword);
    }

    [Fact]
    public void ValidateCallGraphRecursion_Should_Detect_Static_Cycle()
    {
        CallGraphNode nodeA = new("A", "d1/", 1, 1001, "logic1", "d1/A.code", string.Empty, false);
        CallGraphNode nodeB = new("B", "d1/", 1, 1001, "logic1", "d1/B.code", string.Empty, false);

        CallGraphEdge edgeAB = new(nodeA, nodeB, CallEdgeType.Static, 1);
        CallGraphEdge edgeBA = new(nodeB, nodeA, CallEdgeType.Static, 2);

        CallGraphCycle cycle = new([nodeA, nodeB, nodeA], edgeBA);

        CallGraphReport report = new()
        {
            Nodes = [nodeA, nodeB],
            Edges = [edgeAB, edgeBA],
            EntryPoints = [],
            DeadCodeCandidates = [],
            Statistics = new CallGraphStatistics(2, 2, 0, 0, 2, 0, 0, 1),
            UnresolvedActCalls = [],
            Cycles = [cycle]
        };

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateCallGraphRecursion(report);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallRecursion && d.Severity == LintSeverity.Error);
    }

    [Fact]
    public void ValidateCallGraphRecursion_Should_Warn_On_Unresolved_Act_Call()
    {
        CallGraphNode from = new("A", "d1/", 1, 1001, "logic1", "d1/A.code", string.Empty, false);

        UnresolvedActCall unresolved = new(from, "d2/", "External", 10);

        CallGraphReport report = new()
        {
            Nodes = [from],
            Edges = [],
            EntryPoints = [],
            DeadCodeCandidates = [],
            Statistics = new CallGraphStatistics(1, 0, 0, 0, 0, 0, 0, 1),
            UnresolvedActCalls = [unresolved],
            Cycles = []
        };

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateCallGraphRecursion(report);

        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.CallRecursionPotential &&
            d.Location.Path == "d1/A.code" &&
            d.Location.Line == 10);
    }

    [Fact]
    public void FromDevice_Should_Report_ParamClashGlobal_When_Param_Matches_Global()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");
        device.SetVarsInternal(new VarsConfig
        {
            Variables = [new("hp", "Float", null)]
        });

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Test", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Test",
            "begincodeV2\nname:Test\nparams: Float hp\nlines:\nfun debug.enable(Bool 1)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, analysisService, model);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.ParamClashGlobal);
    }

    [Fact]
    public void FromDevice_Should_Report_GlobalClashReturn_When_Return_Matches_Global()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");
        device.SetVarsInternal(new VarsConfig
        {
            Variables = [new("result", "Float", null)]
        });

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Test", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Test",
            "begincodeV2\nname:Test\nreturn: Float result\nlines:\nfun debug.enable(Bool 1)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, analysisService, model);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.GlobalClashReturn);
    }

    [Fact]
    public void FromDevice_Should_Allow_Float_And_Long_Vars_For_Number_Param()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Test", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Test",
            "begincodeV2\nname:Test\nlines:\nFloat scoreFloat=fun create.float(Float 1)\nLong scoreLong=fun create.long(Long 1)\nDict dict=fun create.dict()\nfun map.setRank(var scoreFloat:var dict:Enum 0:Long 0)\nfun map.setRank(var scoreLong:var dict:Enum 0:Long 0)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, analysisService, model);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallParamSignatureMismatch);
    }

    [Fact]
    public void FromDevice_Should_Report_VarGlobalTypeMismatch_When_Local_Has_Different_Type()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");
        device.SetVarsInternal(new VarsConfig
        {
            Variables = [new("x", "Float", null)]
        });

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Test", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Test",
            "begincodeV2\nname:Test\nlines:\nLong x=fun create.long(Long 1)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, analysisService, model);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.VarGlobalTypeMismatch);
    }

    [Fact]
    public void ValidateUnusedLocalVars_Should_Not_Report_When_Param_Used_As_Arg()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Test", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Test",
            "begincodeV2\nname:Test\nparams: Float hp\nlines:\nfun debug.log(var hp)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.VarUnused);
    }

    [Fact]
    public void ValidateUnusedLocalVars_Should_Not_Report_When_Param_Used_In_Varf()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Test", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Test",
            "begincodeV2\nname:Test\nparams: Float hp\nlines:\nvarf Float.hp.getValue()\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.VarUnused);
    }

    [Fact]
    public void ValidateUnusedLocalVars_Should_Report_When_Param_Unused()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Test", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Test",
            "begincodeV2\nname:Test\nparams: Float hp\nlines:\nfun debug.enable(Bool 1)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.VarUnused && d.Message.Contains("hp"));
    }

    [Fact]
    public void ValidateUnusedLocalVars_Should_Not_Report_ReturnParam()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Test", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Test",
            "begincodeV2\nname:Test\nreturn: Float result\nlines:\nfun debug.enable(Bool 1)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.VarUnused && d.Message.Contains("result"));
    }

    [Fact]
    public void ValidateUnusedLocalVars_Should_Not_Report_ReturnParam_When_Assigned_By_Call()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Test", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Test",
            "begincodeV2\nname:Test\nreturn: Float ret\nlines:\nFloat ret=fun create.float(Float 0)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.VarUnused && d.Message.Contains("ret"));
    }

    [Fact]
    public void ValidateUnusedLocalVars_Should_Not_Report_Local_This_When_Used_As_Varf_Owner()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Test", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Test",
            "begincodeV2\nname:Test\nlines:\nDevice this=act 1.this.getDevice()\nvarf Device.this.laterCallFun0(String SpeedFix:Float 0)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.VarUnused && d.Message.Contains("this"));
    }

    [Fact]
    public void ValidateUnusedGlobalVars_Should_Not_Report_When_Assigned_And_Read_Via_Varf()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");
        device.SetVarsInternal(new VarsConfig
        {
            Variables = [new("score", "Float", null)]
        });

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Test", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Test",
            "begincodeV2\nname:Test\nlines:\nFloat score=fun create.float(Float 0)\nvarf Float.score.getValue()\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedGlobalVars(model, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.VarUnused && d.Message.Contains("score"));
    }

    [Fact]
    public void ValidateUnusedGlobalVars_Should_Not_Report_When_Read_As_Arg()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");
        device.SetVarsInternal(new VarsConfig
        {
            Variables = [new("hp", "Float", null)]
        });

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Test", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Test",
            "begincodeV2\nname:Test\nlines:\nfun debug.log(var hp)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedGlobalVars(model, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.VarUnused && d.Message.Contains("hp"));
    }

    [Fact]
    public void ValidateUnusedGlobalVars_Should_Not_Report_Global_This_When_Used_As_Varf_Owner()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");
        device.SetVarsInternal(new VarsConfig
        {
            Variables = [new("this", "Device", null)]
        });

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Test", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Test",
            "begincodeV2\nname:Test\nlines:\nvarf Device.this.laterCallFun0(String SpeedFix:Float 0)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedGlobalVars(model, analysisService);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.VarUnused && d.Message.Contains("this"));
    }

    [Fact]
    public void ValidateUnusedGlobalVars_Should_Report_When_Truly_Unused()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");
        device.SetVarsInternal(new VarsConfig
        {
            Variables = [new("unused_var", "Float", null)]
        });

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Test", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Test",
            "begincodeV2\nname:Test\nlines:\nfun debug.enable(Bool 1)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedGlobalVars(model, analysisService);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.VarUnused && d.Message.Contains("unused_var"));
    }

    [Fact]
    public void CallGraph_Should_Resolve_This_Act_Call_Within_Same_Device()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Init", string.Empty, false), new("Target", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Init",
            "begincodeV2\nname:Init\nlines:\nact 1001.this.Target()\nendcode");
        TestEntityFactory.CreateInlineScript(group, "Target",
            "begincodeV2\nname:Target\nlines:\nfun debug.enable(Bool 1)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        CallGraphBuilder builder = new(model, analysisService);
        CallGraphReport report = builder.Build();

        Assert.Empty(report.UnresolvedActCalls);
        Assert.Contains(report.Edges, e => e.EdgeType == CallEdgeType.Static
            && e.From.MethodName == "Init"
            && e.To.MethodName == "Target");
    }

    [Fact]
    public void CallGraph_Should_Resolve_Numeric_Act_Call_Within_Same_Device()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");

        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new("Init", string.Empty, false), new("Target", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Init",
            "begincodeV2\nname:Init\nlines:\nact 1001.1.Target()\nendcode");
        TestEntityFactory.CreateInlineScript(group, "Target",
            "begincodeV2\nname:Target\nlines:\nfun debug.enable(Bool 1)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new ScriptAnalysisService(model);

        CallGraphBuilder builder = new(model, analysisService);
        CallGraphReport report = builder.Build();

        Assert.Empty(report.UnresolvedActCalls);
        Assert.Contains(report.Edges, e => e.EdgeType == CallEdgeType.Static
            && e.From.MethodName == "Init"
            && e.To.MethodName == "Target");
    }

}
