using Toolkit.Core.Tests.Common;
using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Analysis;

/// <summary>
/// 逻辑内容指纹（ProjectLogicContext.ComputeLogicFingerprint）—— 临时工程集成测试。
/// 框住产物缓存键的边界：逻辑输入（脚本/device.json/list.json/代码仓库）变化必须改变指纹；
/// 非逻辑输入（vars.json）变化不得改变指纹，否则 dts/CodeRepo.ts 短路缓存失效或错杀。
/// </summary>
[Collection("WorkspaceState")]
public sealed class LogicFingerprintTests : TempDirectoryTestFixture
{
    private const string RealScript = "begincodeV2\nname:Real\nlines:\n//real\nendcode\n";
    private const string RepoGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    public LogicFingerprintTests() : base("Toolkit.Core.Tests.LogicFingerprint")
    {
    }

    private string CreateProjectWithScript()
    {
        string scriptsDir = ScriptGroupTestProject.CreateBaseProject(TempDir);
        File.WriteAllText(Path.Combine(scriptsDir, "list.json"), "[\n  Real\n]");
        File.WriteAllText(Path.Combine(scriptsDir, "Real.code"), RealScript);
        return scriptsDir;
    }

    private string ComputeFingerprint()
    {
        ProjectModel model = TestWorkspace.LoadProject(TempDir);
        return new ProjectLogicContext(model).ComputeLogicFingerprint();
    }

    [Fact]
    public void Fingerprint_Stable_Across_Reload_Without_Changes()
    {
        CreateProjectWithScript();

        string fp1 = ComputeFingerprint();
        string fp2 = ComputeFingerprint();

        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void Fingerprint_Changes_When_Inline_Script_Changes()
    {
        string scriptsDir = CreateProjectWithScript();
        string fp1 = ComputeFingerprint();

        File.WriteAllText(Path.Combine(scriptsDir, "Real.code"), RealScript + "\n//changed\n");

        Assert.NotEqual(fp1, ComputeFingerprint());
    }

    [Fact]
    public void Fingerprint_Changes_When_DeviceJson_Changes()
    {
        CreateProjectWithScript();
        string fp1 = ComputeFingerprint();

        File.WriteAllText(
            Path.Combine(TempDir, "map.map_", "0", "device.json"),
            "type: 1001\next:\n{\nlogicName: renamed\n}");

        Assert.NotEqual(fp1, ComputeFingerprint());
    }

    [Fact]
    public void Fingerprint_Changes_When_ListJson_Changes()
    {
        string scriptsDir = CreateProjectWithScript();
        string fp1 = ComputeFingerprint();

        // list.json 内容参与指纹（条目顺序/私有标记都是产物输入），此处改为空清单
        File.WriteAllText(Path.Combine(scriptsDir, "list.json"), "[\n]");

        Assert.NotEqual(fp1, ComputeFingerprint());
    }

    [Fact]
    public void Fingerprint_Changes_When_CodeRepo_Script_Changes()
    {
        CreateProjectWithScript();
        ScriptGroupTestProject.WriteCodeRepoScript(TempDir, "Foo", RepoGuid,
            "begincodeV2\nname:Foo\nlines:\n//foo\nendcode\n");
        string fp1 = ComputeFingerprint();

        ScriptGroupTestProject.WriteCodeRepoScript(TempDir, "Foo", RepoGuid,
            "begincodeV2\nname:Foo\nlines:\n//foo changed\nendcode\n");

        Assert.NotEqual(fp1, ComputeFingerprint());
    }

    [Fact]
    public void Fingerprint_Changes_When_CodeRepo_Guid_Changes()
    {
        CreateProjectWithScript();
        ScriptGroupTestProject.WriteCodeRepoScript(TempDir, "Foo", RepoGuid,
            "begincodeV2\nname:Foo\nlines:\n//foo\nendcode\n");
        string fp1 = ComputeFingerprint();

        ScriptGroupTestProject.WriteCodeRepoScript(TempDir, "Foo", "cccccccccccccccccccccccccccccccc",
            "begincodeV2\nname:Foo\nlines:\n//foo\nendcode\n");

        Assert.NotEqual(fp1, ComputeFingerprint());
    }

    [Fact]
    public void Fingerprint_Unaffected_By_VarsJson()
    {
        CreateProjectWithScript();
        string fp1 = ComputeFingerprint();

        // vars.json 不是 dts/CodeRepo.ts 的输入：变化不得击穿产物缓存
        File.WriteAllText(
            Path.Combine(TempDir, "map.map_", "0", "vars.json"),
            "[\n {\n  name:计数器\n  type:Int\n }\n]");

        Assert.Equal(fp1, ComputeFingerprint());
    }
}
