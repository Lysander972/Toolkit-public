using Toolkit.Core.Tests.Common;
using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint;

/// <summary>
/// 脚本组清单(list.json)↔实体(.code) 一致性检测（LINT-STRUCT-SCRIPT-ORPHAN）
/// 及 ResolveScriptPath 失配降级（LINT-LINT-ERROR）。
/// 用 TestEntityFactory 内存构造失配状态——不加载文件、不污染 test/data/projects。
/// ORPHAN 检测测试传 analysisService:null（ValidateScriptGroupConsistency 只判 model!=null，
/// 不进脚本解析块，避开源码/索引时机噪音）；降级测试传 analysisService 触发 ResolveScriptPath。
/// </summary>
public class LintScriptOrphanTests
{
    private static (DeviceEntity device, ProjectRootEntity root) CreateDeviceTree()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "logic");
        return (device, root);
    }

    private static ProjectModel CreateModel(ProjectRootEntity root)
    {
        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        return model;
    }

    // ── happy path：一致时不报 ORPHAN ──

    [Fact]
    public void ConsistentScriptGroup_ProducesNoOrphanDiagnostic()
    {
        (DeviceEntity device, ProjectRootEntity root) = CreateDeviceTree();
        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new ScriptEntryInfo("Real", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "Real", "begincodeV2\nname:Real\nlines:\nendcode");
        ProjectModel model = CreateModel(root);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, null, model);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.StructScriptOrphan);
    }

    [Fact]
    public void EmptyScriptGroup_ProducesNoOrphanDiagnostic()
    {
        // 边界: 空 ScriptEntries + 空 Scripts
        (DeviceEntity device, ProjectRootEntity root) = CreateDeviceTree();
        TestEntityFactory.CreateScriptGroup(device);
        ProjectModel model = CreateModel(root);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, null, model);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.StructScriptOrphan);
    }

    // ── 异常路径：失配产出诊断 ──

    [Fact]
    public void PhantomInlineEntry_ReportsOrphanAndLintErrorWithoutThrowing()
    {
        // 清单声明 "Ghost" 但无对应 .code 实体（直写 list.json 加幽灵条目的典型污染）。
        // 传 analysisService 触发 ResolveScriptPath：Ghost 无实体 → 返回 null → 下游降级 LINT-LINT-ERROR。
        // AllScripts 为空，ValidateCanvasUiPathReferences 不解析任何脚本，不崩。
        (DeviceEntity device, ProjectRootEntity root) = CreateDeviceTree();
        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new ScriptEntryInfo("Ghost", string.Empty, false)]);
        ProjectModel model = CreateModel(root);
        ScriptAnalysisService analysis = new(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, analysis, model);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.StructScriptOrphan);
        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.LintEngineError);
        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.StructScriptOrphan &&
            d.Message.Contains("Ghost", StringComparison.Ordinal));
    }

    [Fact]
    public void CodeRepoEntryWithNullGuid_ReportsOrphan()
    {
        // CodeRepo 条目但 GUID 缺失
        (DeviceEntity device, ProjectRootEntity root) = CreateDeviceTree();
        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new ScriptEntryInfo("Repo", string.Empty, true)]);
        ProjectModel model = CreateModel(root);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, null, model);

        Assert.Contains(diagnostics, static d =>
            d.RuleId == LintRuleIds.StructScriptOrphan &&
            d.Message.Contains("Repo", StringComparison.Ordinal));
    }

    [Fact]
    public void CodeRepoEntryWithUnknownGuid_ReportsOrphan()
    {
        // CodeRepo 条目有 GUID 但工程中无对应代码仓库脚本
        (DeviceEntity device, ProjectRootEntity root) = CreateDeviceTree();
        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new ScriptEntryInfo("Repo", "nonexistent-guid", true)]);
        ProjectModel model = CreateModel(root);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, null, model);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.StructScriptOrphan);
    }

    [Fact]
    public void CaseSensitiveNameMismatch_ReportsOrphan()
    {
        // Ordinal 精确匹配：清单 "Real" 与实体 "real" 视为失配
        (DeviceEntity device, ProjectRootEntity root) = CreateDeviceTree();
        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal([new ScriptEntryInfo("Real", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(group, "real", "begincodeV2\nname:real\nlines:\nendcode");
        ProjectModel model = CreateModel(root);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, null, model);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.StructScriptOrphan);
    }

    [Fact]
    public void MultipleGroups_OnlyMismatchedGroupReportsOrphan()
    {
        // 多脚本组：仅失配组报 ORPHAN，一致组不报
        (DeviceEntity device, ProjectRootEntity root) = CreateDeviceTree();

        ScriptGroupEntity goodGroup = TestEntityFactory.CreateScriptGroup(device);
        goodGroup.SetScriptEntriesInternal([new ScriptEntryInfo("Good", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(goodGroup, "Good", "begincodeV2\nname:Good\nlines:\nendcode");

        ScriptGroupEntity badGroup = TestEntityFactory.CreateScriptGroup(device, eventName: "evt");
        badGroup.SetScriptEntriesInternal([new ScriptEntryInfo("Bad", string.Empty, false)]);

        ProjectModel model = CreateModel(root);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, null, model);

        LintDiagnostic[] orphans = diagnostics
            .Where(static d => d.RuleId == LintRuleIds.StructScriptOrphan)
            .ToArray();
        Assert.Single(orphans);
        Assert.Contains("Bad", orphans[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MixedMatchAndMismatch_ReportsOnlyMismatchedEntries()
    {
        // 同一组内：一个匹配、一个失配——只报失配的那个
        (DeviceEntity device, ProjectRootEntity root) = CreateDeviceTree();
        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        group.SetScriptEntriesInternal(
        [
            new ScriptEntryInfo("Matched", string.Empty, false),
            new ScriptEntryInfo("Phantom", string.Empty, false)
        ]);
        TestEntityFactory.CreateInlineScript(group, "Matched", "begincodeV2\nname:Matched\nlines:\nendcode");
        ProjectModel model = CreateModel(root);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, null, model);

        LintDiagnostic[] orphans = diagnostics
            .Where(static d => d.RuleId == LintRuleIds.StructScriptOrphan)
            .ToArray();
        Assert.Single(orphans);
        Assert.Contains("Phantom", orphans[0].Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// LINT-STRUCT-SCRIPT-ORPHAN 双向检测 —— 临时工程集成测试（真文件树）。
/// 孤儿 .code 方向（磁盘存在但清单未登记）依赖 FileTreeBinding 目录枚举，
/// 内存构造的 TestEntityFactory 树没有文件树快照，必须用真实临时工程覆盖。
/// </summary>
[Collection("WorkspaceState")]
public sealed class LintScriptOrphanIntegrationTests : TempDirectoryTestFixture
{
    public LintScriptOrphanIntegrationTests() : base("Toolkit.Core.Tests.LintScriptOrphan")
    {
    }

    private static IReadOnlyList<LintDiagnostic> Analyze(string projectRoot)
    {
        ProjectModel model = TestWorkspace.LoadProject(projectRoot);
        ScriptAnalysisService analysis = new(model);
        return LintEngine.AnalyzeProject(model, analysis);
    }

    [Fact]
    public void OrphanCodeFile_OnDisk_ReportsOrphan()
    {
        // 异常: Orphan.code 存在于磁盘但未登记 list.json（越界直写污染的另一半）
        string scriptsDir = ScriptGroupTestProject.CreateBaseProject(TempDir);
        File.WriteAllText(Path.Combine(scriptsDir, "list.json"), "[\n  Real\n]");
        File.WriteAllText(Path.Combine(scriptsDir, "Real.code"), "begincodeV2\nname:Real\nlines:\n//real\nendcode\n");
        File.WriteAllText(Path.Combine(scriptsDir, "Orphan.code"), "begincodeV2\nname:Orphan\nlines:\n//orphan\nendcode\n");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(TempDir);

        LintDiagnostic[] orphans = diagnostics
            .Where(static d => d.RuleId == LintRuleIds.StructScriptOrphan)
            .ToArray();
        Assert.Single(orphans);
        Assert.Contains("Orphan.code", orphans[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GhostEntry_And_OrphanFile_BothReported()
    {
        // 异常: 幽灵条目 Ghost + 孤儿文件 Orphan.code 同时存在 → 两个方向各报一条
        string scriptsDir = ScriptGroupTestProject.CreateBaseProject(TempDir);
        File.WriteAllText(Path.Combine(scriptsDir, "list.json"), "[\n  Real\n  Ghost\n]");
        File.WriteAllText(Path.Combine(scriptsDir, "Real.code"), "begincodeV2\nname:Real\nlines:\n//real\nendcode\n");
        File.WriteAllText(Path.Combine(scriptsDir, "Orphan.code"), "begincodeV2\nname:Orphan\nlines:\n//orphan\nendcode\n");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(TempDir);

        LintDiagnostic[] orphans = diagnostics
            .Where(static d => d.RuleId == LintRuleIds.StructScriptOrphan)
            .ToArray();
        Assert.Equal(2, orphans.Length);
        Assert.Contains(orphans, static d => d.Message.Contains("Ghost", StringComparison.Ordinal));
        Assert.Contains(orphans, static d => d.Message.Contains("Orphan.code", StringComparison.Ordinal));
    }

    [Fact]
    public void ConsistentProject_ReportsNoOrphan()
    {
        // happy: 清单与磁盘完全一致 → 不报（含 list.json 自身不被误判为孤儿 .code）
        string scriptsDir = ScriptGroupTestProject.CreateBaseProject(TempDir);
        File.WriteAllText(Path.Combine(scriptsDir, "list.json"), "[\n  Real\n]");
        File.WriteAllText(Path.Combine(scriptsDir, "Real.code"), "begincodeV2\nname:Real\nlines:\n//real\nendcode\n");

        IReadOnlyList<LintDiagnostic> diagnostics = Analyze(TempDir);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.StructScriptOrphan);
    }
}
