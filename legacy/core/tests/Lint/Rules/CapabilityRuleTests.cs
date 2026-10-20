using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Tests.Lint.Rules;

/// <summary>
/// 按零件实例能力校验 Act 方法是否可用。
/// 对应 LintRuleIds.CallActCapabilityUnavailable 与 DeviceCapability.Collidable。
/// setKinematic / enableCollide / setDensity 三个方法要求 Collidable 能力。
/// </summary>
public sealed class CapabilityRuleTests
{
    // ════════════════════════════════════════════════════════════════════
    // 正向：具备能力时不报错
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AnalyzeProject_Should_NotError_When_Collidable_Method_Called_On_Capable_Device()
    {
        // type=1 在 Map 中 stickToBone 空 → Device 基类规则 → HasCapability(Collidable) = true
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1, logicName: "caller");
        AddUserScript(caller, "Caller", "act 1.this.enableCollide(Bool 1)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallActCapabilityUnavailable);
    }

    [Fact]
    public void AnalyzeProject_Should_NotError_When_Collidable_Method_Called_Via_Self_WithCapability()
    {
        // 普通 type=1 在 Mech 中 stickToBone 空 → HasCapability = true
        // 使用 self 关键字路径
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MechEntity mech = TestEntityFactory.CreateMech(root, "m");
        DeviceEntity caller = TestEntityFactory.CreateDeviceOnMech(mech, deviceId: 1, deviceType: 1, logicName: "caller");
        AddUserScript(caller, "Caller", "act 1.this.setKinematic(Bool 1)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallActCapabilityUnavailable);
    }

    [Fact]
    public void AnalyzeProject_Should_NotError_When_TextBuilding_InMech_Calls_Collidable_Method()
    {
        // TextBuilding 在普通机械内具备 Collidable，与骨骼绑定无关
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MechEntity mech = TestEntityFactory.CreateMech(root, "m");
        DeviceEntity caller = TestEntityFactory.CreateDeviceOnMech(mech, deviceId: 1, deviceType: 1, logicName: "caller");
        TestEntityFactory.CreateDeviceOnMech(mech, deviceId: 2, deviceType: DeviceTypeIds.TextBuilding, logicName: "textdev", stickToBone: "bone");
        AddUserScript(caller, "Caller", "act 1102.textdev.setKinematic(Bool 1)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallActCapabilityUnavailable);
    }

    // ════════════════════════════════════════════════════════════════════
    // 反向：不具备能力时报错（覆盖三种 Collidable 方法）
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AnalyzeProject_Should_Error_When_EnableCollide_Called_On_BirthBuilding()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1, logicName: "caller");
        TestEntityFactory.CreateDevice(map, deviceId: 2, deviceType: DeviceTypeIds.BirthBuilding, logicName: "birth");
        AddUserScript(caller, "Caller", "act 1110.birth.enableCollide(Bool 1)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.CallActCapabilityUnavailable && d.Severity == LintSeverity.Error);
    }

    [Fact]
    public void AnalyzeProject_Should_Error_When_SetKinematic_Called_On_TextBuilding_InMap()
    {
        // TextBuilding 在地图内无 Collidable
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1, logicName: "caller");
        TestEntityFactory.CreateDevice(map, deviceId: 3, deviceType: DeviceTypeIds.TextBuilding, logicName: "textdev");
        AddUserScript(caller, "Caller", "act 1102.textdev.setKinematic(Bool 1)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.CallActCapabilityUnavailable && d.Severity == LintSeverity.Error);
    }

    [Fact]
    public void AnalyzeProject_Should_Error_When_SetDensity_Called_On_SLGLogicDevice()
    {
        // SLGLogicDevice 无 Collidable
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1, logicName: "caller");
        TestEntityFactory.CreateDevice(map, deviceId: 4, deviceType: DeviceTypeIds.SLGLogicDevice, logicName: "slgdev");
        AddUserScript(caller, "Caller", "act 1103.slgdev.setDensity(Float 1.0)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.CallActCapabilityUnavailable && d.Severity == LintSeverity.Error);
    }

    [Fact]
    public void AnalyzeProject_Should_Error_When_Collidable_Called_On_StickToBone_PlainDevice()
    {
        // 普通 type=1 绑定骨骼时无 Collidable
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1, logicName: "caller");
        TestEntityFactory.CreateDevice(map, deviceId: 5, deviceType: 1, logicName: "bonedev", stickToBone: "skull");
        AddUserScript(caller, "Caller", "act 1.bonedev.setKinematic(Bool 1)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.CallActCapabilityUnavailable && d.Severity == LintSeverity.Error);
    }

    // ════════════════════════════════════════════════════════════════════
    // PosDevice / TriggerBuilding（载体与骨骼绑定条件）
    // 地图内无碰撞能力，普通机械内由骨骼绑定状态决定
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AnalyzeProject_Should_Error_When_SetKinematic_Called_On_PosDevice_InMap()
    {
        // PosDevice 在地图内无 Collidable
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1, logicName: "caller");
        TestEntityFactory.CreateDevice(map, deviceId: 6, deviceType: DeviceTypeIds.PosDevice, logicName: "pos");
        AddUserScript(caller, "Caller", "act 360.pos.setKinematic(Bool 1)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.CallActCapabilityUnavailable && d.Severity == LintSeverity.Error);
    }

    [Fact]
    public void AnalyzeProject_Should_NotError_When_SetKinematic_Called_On_PosDevice_InMech()
    {
        // PosDevice 在普通机械内未绑定骨骼时具备 Collidable
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MechEntity mech = TestEntityFactory.CreateMech(root, "m");
        DeviceEntity caller = TestEntityFactory.CreateDeviceOnMech(mech, deviceId: 1, deviceType: 1, logicName: "caller");
        TestEntityFactory.CreateDeviceOnMech(mech, deviceId: 6, deviceType: DeviceTypeIds.PosDevice, logicName: "pos");
        AddUserScript(caller, "Caller", "act 360.pos.setKinematic(Bool 1)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallActCapabilityUnavailable);
    }

    [Fact]
    public void AnalyzeProject_Should_Error_When_EnableCollide_Called_On_TriggerBuilding_InMap()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1, logicName: "caller");
        TestEntityFactory.CreateDevice(map, deviceId: 7, deviceType: DeviceTypeIds.TriggerBuilding, logicName: "trig");
        AddUserScript(caller, "Caller", "act 1100.trig.enableCollide(Bool 1)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.CallActCapabilityUnavailable && d.Severity == LintSeverity.Error);
    }

    // ════════════════════════════════════════════════════════════════════
    // NoColliderBuilding 全部变体（载体条件）
    // 关键差异：在普通 Mech 内永远 true，与 stickToBone 无关
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(DeviceTypeIds.NoColliderBuilding_1030)]
    [InlineData(DeviceTypeIds.NoColliderBuilding_1032)]
    [InlineData(DeviceTypeIds.NoColliderBuilding_1058)]
    [InlineData(DeviceTypeIds.NoColliderBuilding_1061)]
    public void AnalyzeProject_Should_Error_When_Collidable_Called_On_NoColliderBuilding_InMap(int typeId)
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1, logicName: "caller");
        TestEntityFactory.CreateDevice(map, deviceId: 8, deviceType: typeId, logicName: "noc");
        AddUserScript(caller, "Caller", $"act {typeId}.noc.setDensity(Float 1.0)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.CallActCapabilityUnavailable && d.Severity == LintSeverity.Error);
    }

    [Theory]
    [InlineData(DeviceTypeIds.NoColliderBuilding_1030)]
    [InlineData(DeviceTypeIds.NoColliderBuilding_1032)]
    [InlineData(DeviceTypeIds.NoColliderBuilding_1058)]
    [InlineData(DeviceTypeIds.NoColliderBuilding_1061)]
    public void AnalyzeProject_Should_NotError_When_Collidable_Called_On_NoColliderBuilding_InMech_EvenWithStickToBone(int typeId)
    {
        // 绑定骨骼时仍具备 Collidable
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MechEntity mech = TestEntityFactory.CreateMech(root, "m");
        DeviceEntity caller = TestEntityFactory.CreateDeviceOnMech(mech, deviceId: 1, deviceType: 1, logicName: "caller");
        TestEntityFactory.CreateDeviceOnMech(mech, deviceId: 8, deviceType: typeId, logicName: "noc", stickToBone: "skull");
        AddUserScript(caller, "Caller", $"act {typeId}.noc.setKinematic(Bool 1)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallActCapabilityUnavailable);
    }

    // ════════════════════════════════════════════════════════════════════
    // 跨载体 1110 BirthBuilding：callerDevice=BirthBuilding 时 carrierDevices 切换到目标 mech
    // 这是用户描述中发现"继承链设计不符合原版场景"的核心场景
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AnalyzeProject_Should_NotError_When_BirthBuilding_Calls_Collidable_On_Capable_Device_InLinkedMech()
    {
        // BirthBuilding(1110) 在 Map 上，ext.mechFileGUID 指向 mech；
        // mech 内 type=1 stickToBone 空 → 具备 Collidable → 不报错
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        MechEntity mech = TestEntityFactory.CreateMech(root, "m");
        DeviceEntity caller = TestEntityFactory.CreateDevice(
            map, deviceId: 9, deviceType: DeviceTypeIds.BirthBuilding, logicName: "spawn", mechFileGuid: mech.Guid);
        TestEntityFactory.CreateDeviceOnMech(mech, deviceId: 1, deviceType: 1, logicName: "target");
        AddUserScript(caller, "Caller", "act 1.target.enableCollide(Bool 1)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallActCapabilityUnavailable);
    }

    [Fact]
    public void AnalyzeProject_Should_Error_When_BirthBuilding_Calls_Collidable_On_Incapable_Device_InLinkedMech()
    {
        // BirthBuilding(1110) callerDevice 切换 carrierDevices 到目标 mech；
        // mech 内 SLGLogicDevice（永远无 Collidable）→ 校验仍生效，应报错
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        MechEntity mech = TestEntityFactory.CreateMech(root, "m");
        DeviceEntity caller = TestEntityFactory.CreateDevice(
            map, deviceId: 9, deviceType: DeviceTypeIds.BirthBuilding, logicName: "spawn", mechFileGuid: mech.Guid);
        TestEntityFactory.CreateDeviceOnMech(mech, deviceId: 4, deviceType: DeviceTypeIds.SLGLogicDevice, logicName: "slg");
        AddUserScript(caller, "Caller", "act 1103.slg.setDensity(Float 1.0)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.CallActCapabilityUnavailable && d.Severity == LintSeverity.Error);
    }

    // ════════════════════════════════════════════════════════════════════
    // 反向不应触发：无能力要求的方法、无法静态解析的目标
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AnalyzeProject_Should_NotTriggerCapabilityCheck_For_MethodWithoutRequiredCapability()
    {
        // getPosition 不要求 capability → 即使在不具备 Collidable 的 BirthBuilding 上调用也不报本规则
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1, logicName: "caller");
        TestEntityFactory.CreateDevice(map, deviceId: 2, deviceType: DeviceTypeIds.BirthBuilding, logicName: "birth");
        AddUserScript(caller, "Caller", "act 1110.birth.getPosition()");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallActCapabilityUnavailable);
    }

    [Fact]
    public void AnalyzeProject_Should_SkipCapabilityCheck_When_AliasMatchesMultipleDevices()
    {
        // 别名重复匹配 → ResolveActTargetDevice 返回 null → capability 校验跳过
        // （由 CallActAliasNotUnique 单独报错，不重复触发能力校验）
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity caller = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1, logicName: "caller");
        TestEntityFactory.CreateDevice(map, deviceId: 2, deviceType: DeviceTypeIds.BirthBuilding, logicName: "dup");
        TestEntityFactory.CreateDevice(map, deviceId: 3, deviceType: DeviceTypeIds.BirthBuilding, logicName: "dup");
        AddUserScript(caller, "Caller", "act 1110.dup.enableCollide(Bool 1)");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(root);
        // 应触发别名重复警告，但不应触发能力校验
        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallActAliasNotUnique);
        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallActCapabilityUnavailable);
    }

    // ════════════════════════════════════════════════════════════════════
    // 辅助
    // ════════════════════════════════════════════════════════════════════

    private static IReadOnlyList<LintDiagnostic> Analyze(ProjectRootEntity root)
    {
        (ProjectModel model, ScriptAnalysisService analysisService) = CreateAnalysisContext(root);
        return LintEngine.AnalyzeProject(model, analysisService);
    }

    private static InlineScriptEntity AddUserScript(DeviceEntity device, string scriptName, string line)
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

        entries.Add(new ScriptEntryInfo(scriptName, string.Empty, false, false));

        string sourceText = $"begincodeV2\nname:{scriptName}\nlines:\n{line}\nendcode";
        return TestEntityFactory.CreateInlineScript(scriptGroup, scriptName, sourceText);
    }

#pragma warning disable TK006 // 测试辅助：封装 model+analysis 创建
    private static (ProjectModel Model, ScriptAnalysisService AnalysisService) CreateAnalysisContext(ProjectRootEntity root)
#pragma warning restore TK006
    {
        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysisService = new(model);
        return (model, analysisService);
    }
}
