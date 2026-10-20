using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Tests.Lint.Rules;

public sealed class FilenameParamsRuleTests
{
    private static (DeviceEntity Device, ProjectModel Model) CreateDeviceWithModel(
        string rawText,
        string[]? resourceFileNames = null,
        int deviceType = 1001)
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: deviceType, logicName: "test");

        device.SetVarsInternal(new VarsConfig
        {
            Variables =
            [
                new("HP", "Float", null),
                new("Name", "String", null)
            ]
        });

        ScriptGroupEntity scriptGroup = TestEntityFactory.CreateScriptGroup(device);
        scriptGroup.SetScriptEntriesInternal([new("A", string.Empty, false)]);

        TestEntityFactory.CreateInlineScript(
            scriptGroup,
            "A",
            $"begincodeV2\nname:A\nlines:\n{rawText}\nendcode");

        if (resourceFileNames != null)
        {
            List<ProjectEntity> rootChildren = (List<ProjectEntity>)root.Children;
            foreach (string fileName in resourceFileNames)
            {
                ResourceEntity resource = new()
                {
                    Parent = root,
                    Guid = TestEntityFactory.NewGuid(),
                    Data = [0x89, 0x50, 0x4E, 0x47]
                };
                resource.SetIdentifierInternal(fileName);
                rootChildren.Add(resource);
            }
        }

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        return (device, model);
    }

    [Fact]
    public void Should_NotWarn_When_LoadPic_Filename_Matches_Resource()
    {
        (DeviceEntity device, ProjectModel model) = CreateDeviceWithModel(
            "varf UIRawImage.v.loadPic(String 贴图.png)",
            ["贴图.png"]);

        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateFilenameParams(device, analysis, model);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallFilenameNotFound);
    }

    [Fact]
    public void Should_Error_When_LoadPic_Filename_Not_Found()
    {
        (DeviceEntity device, ProjectModel model) = CreateDeviceWithModel("varf UIRawImage.v.loadPic(String 不存在的图片.png)");

        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateFilenameParams(device, analysis, model);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallFilenameNotFound && d.Severity == LintSeverity.Error);
    }

    [Fact]
    public void Should_NotWarn_When_Filename_Param_Is_Variable()
    {
        (DeviceEntity device, ProjectModel model) = CreateDeviceWithModel("varf UIRawImage.v.loadPic(var Name)");

        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateFilenameParams(device, analysis, model);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallFilenameNotFound);
    }

    [Fact]
    public void Should_Error_When_SetDynamicMatPic_Filename_Not_Found()
    {
        (DeviceEntity device, ProjectModel model) = CreateDeviceWithModel("varf Device.x.setDynamicMatPic(String _MainTex:String 不存在.png)");

        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateFilenameParams(device, analysis, model);

        LintDiagnostic diagnostic = Assert.Single(diagnostics, static d => d.RuleId == LintRuleIds.CallFilenameNotFound);
        Assert.Contains("扩展名", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("GUID", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("路径", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Error_When_Act_OwnerType_Is_Numeric()
    {
        (DeviceEntity device, ProjectModel model) = CreateDeviceWithModel(
            "act 362.this.setDynamicMatPic2(String _MainTex:String 不存在.png:Float 0)",
            deviceType: DeviceTypeIds.ObjDevice);

        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateFilenameParams(device, analysis, model);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallFilenameNotFound);
    }

    [Fact]
    public void Should_Error_When_Act_Resolves_Filename_Param_Through_Inheritance()
    {
        (DeviceEntity device, ProjectModel model) = CreateDeviceWithModel(
            "act 328.this.setTexture(String 不存在.png)",
            deviceType: DeviceTypeIds.RectDevice);

        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateFilenameParams(device, analysis, model);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallFilenameNotFound);
    }

    [Fact]
    public void Should_Error_When_EditorFunc_File_Not_Found()
    {
        (DeviceEntity device, ProjectModel model) = CreateDeviceWithModel("dvf d.Device.setPic(String 不存在.png)");

        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateFilenameParams(device, analysis, model);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallFilenameNotFound);
    }

    [Fact]
    public void Should_Error_When_Value_Uses_Path_Instead_Of_FileName()
    {
        (DeviceEntity device, ProjectModel model) = CreateDeviceWithModel(
            "varf UIRawImage.v.loadPic(String assets/贴图.png)",
            ["贴图.png"]);

        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateFilenameParams(device, analysis, model);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallFilenameNotFound);
    }

    [Fact]
    public void AnalyzeProject_Should_Report_CodeRepo_Filename_Param()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");
        device.SetVarsInternal(new VarsConfig { Variables = [] });

        ScriptGroupEntity scriptGroup = TestEntityFactory.CreateScriptGroup(device);
        string repoGuid = TestEntityFactory.NewGuid();
        scriptGroup.SetScriptEntriesInternal([new("Repo", repoGuid, true)]);
        TestEntityFactory.CreateCodeRepoScript(
            root,
            "Repo.code",
            repoGuid,
            "begincodeV2\nname:Repo\nlines:\nvarf UIRawImage.v.loadPic(String 不存在.png)\nendcode");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysis = new(model);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysis);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.CallFilenameNotFound);
    }

    [Fact]
    public void Should_NotWarn_When_NonFilename_Method_Param_Not_Found()
    {
        (DeviceEntity device, ProjectModel model) = CreateDeviceWithModel("varf Device.x.setDynamicMatFloat(String 不存在的参数值:Float 1)");

        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateFilenameParams(device, analysis, model);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.CallFilenameNotFound);
    }
}
