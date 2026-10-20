using System.Text.Json;
using Toolkit.Core.Tests.Common;
using Xunit;
using Xunit.Abstractions;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Canvas;

[Collection("WorkspaceState")]
public sealed class CanvasGuidMapTests : TempDirectoryTestFixture
{
    private readonly ITestOutputHelper _output;

    public CanvasGuidMapTests(ITestOutputHelper output) : base("Toolkit.Core.Tests.CanvasGuidMap")
    {
        _output = output;
    }

    private void WriteSettingJson()
    {
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(TempDir, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");
    }

    private static void WriteResource(string dir, string fileName, string guid)
    {
        string filePath = SystemIOWrapper.Combine(dir, fileName);
        string guidPath = filePath + ".guid";
        SystemIOWrapper.WriteAllBytes(filePath, [1, 2, 3]);
        SystemIOWrapper.WriteAllBytes(guidPath, TextEncoding.GetBytesLfOnly(guid));
    }

    private static void WriteResourceKeyValueGuid(string dir, string fileName, string guid)
    {
        string filePath = SystemIOWrapper.Combine(dir, fileName);
        string guidPath = filePath + ".guid";
        SystemIOWrapper.WriteAllBytes(filePath, [1, 2, 3]);
        SystemIOWrapper.WriteAllBytes(guidPath, TextEncoding.GetBytesLfOnly("guid:" + guid + "\nquality:80"));
    }

    [Fact]
    public void NestedDirectoryResources_ShouldBeIncludedInGuidMap()
    {
        WriteSettingJson();

        // 根级资源
        WriteResource(TempDir, "root_image.png", "aa000000000000000000000000000000");

        // 一层嵌套: 图片/Colors.png
        string imagesDir = SystemIOWrapper.Combine(TempDir, "图片");
        SystemIOWrapper.CreateDirectory(imagesDir);
        WriteResource(imagesDir, "Colors.png", "aa000000000000000000000000000001");

        // 两层嵌套: 图片/占位资源/terrain.webp
        string placeholderDir = SystemIOWrapper.Combine(imagesDir, "占位资源");
        SystemIOWrapper.CreateDirectory(placeholderDir);
        WriteResource(placeholderDir, "terrain.webp", "aa000000000000000000000000000002");

        // 两层嵌套: Textures/bg.jpg
        string texturesDir = SystemIOWrapper.Combine(TempDir, "Textures");
        SystemIOWrapper.CreateDirectory(texturesDir);
        WriteResource(texturesDir, "bg.jpg", "aa000000000000000000000000000003");

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        foreach (ToolError err in model.LoadErrors.Errors)
            _output.WriteLine($"LoadError: {err.Severity} {err.Path}: {err.Message}");

        IReadOnlyList<ResourceEntity> resources = model.List<ResourceEntity>();
        foreach (ResourceEntity r in resources)
            _output.WriteLine($"Resource: {r.实体_完整相对路径标识} guid={r.Guid}");
        Assert.Equal(4, resources.Count);

        string guidMap = CanvasHtmlViewAssets.BuildStandaloneGuidMapScript(model);
        _output.WriteLine($"guidMap:\n{guidMap}");

        // 提取 JSON 映射验证
        int rawStart = guidMap.IndexOf("const raw = ", StringComparison.Ordinal) + "const raw = ".Length;
        int rawEnd = guidMap.IndexOf(";\n", rawStart, StringComparison.Ordinal);
        string jsonPart = guidMap[rawStart..rawEnd];
        Dictionary<string, string>? mappings = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonPart);
        Assert.NotNull(mappings);
        Assert.Equal(4, mappings.Count);
        Assert.Equal("root_image.png", mappings["aa000000000000000000000000000000"]);
        Assert.Equal("图片/Colors.png", mappings["aa000000000000000000000000000001"]);
        Assert.Equal("图片/占位资源/terrain.webp", mappings["aa000000000000000000000000000002"]);
        Assert.Equal("Textures/bg.jpg", mappings["aa000000000000000000000000000003"]);
    }

    [Fact]
    public void GuidMapScript_ShouldIncludeAllSupportedExtensions()
    {
        WriteSettingJson();

        WriteResource(TempDir, "image.png", "bb000000000000000000000000000000");
        WriteResource(TempDir, "photo.webp", "bb000000000000000000000000000001");
        WriteResource(TempDir, "data.json", "bb000000000000000000000000000002");
        WriteResource(TempDir, "anim.skel", "bb000000000000000000000000000003");

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        IReadOnlyList<ResourceEntity> allResources = model.List<ResourceEntity>();
        Assert.Equal(4, allResources.Count);

        string guidMap = CanvasHtmlViewAssets.BuildStandaloneGuidMapScript(model);
        // SupportedPreviewAssetExtensions 包含 .json/.skel/.atlas 等
        Assert.Contains("image.png", guidMap);
        Assert.Contains("photo.webp", guidMap);
        Assert.Contains("data.json", guidMap);
        Assert.Contains("anim.skel", guidMap);
    }

    [Fact]
    public void KeyValueGuidFiles_ShouldBeIncludedInGuidMap()
    {
        WriteSettingJson();

        // key-value 格式 .guid（原版游戏资源常见格式：guid:XXX\nquality:80）
        WriteResourceKeyValueGuid(TempDir, "terrain.webp", "cc0000000000000000000000000000aa");
        WriteResourceKeyValueGuid(TempDir, "hero.png", "cc0000000000000000000000000000bb");

        string imagesDir = SystemIOWrapper.Combine(TempDir, "图片");
        SystemIOWrapper.CreateDirectory(imagesDir);
        WriteResourceKeyValueGuid(imagesDir, "背景.png", "cc0000000000000000000000000000cc");

        ProjectModel model = TestWorkspace.LoadProject(TempDir);

        foreach (ToolError err in model.LoadErrors.Errors)
            _output.WriteLine($"LoadError: {err.Severity} {err.Path}: {err.Message}");

        IReadOnlyList<ResourceEntity> resources = model.List<ResourceEntity>();
        foreach (ResourceEntity r in resources)
            _output.WriteLine($"Resource: {r.实体_完整相对路径标识} guid={r.Guid}");

        Assert.Equal(3, resources.Count);

        string guidMap = CanvasHtmlViewAssets.BuildStandaloneGuidMapScript(model);
        _output.WriteLine($"guidMap:\n{guidMap}");

        // 提取 JSON 映射验证（JsonSerializer 会将中文转义为 \uXXXX）
        int rawStart = guidMap.IndexOf("const raw = ", StringComparison.Ordinal) + "const raw = ".Length;
        int rawEnd = guidMap.IndexOf(";\n", rawStart, StringComparison.Ordinal);
        string jsonPart = guidMap[rawStart..rawEnd];
        Dictionary<string, string>? mappings = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonPart);
        Assert.NotNull(mappings);
        Assert.Equal(3, mappings.Count);
        Assert.Equal("terrain.webp", mappings["cc0000000000000000000000000000aa"]);
        Assert.Equal("hero.png", mappings["cc0000000000000000000000000000bb"]);
        Assert.Equal("图片/背景.png", mappings["cc0000000000000000000000000000cc"]);
    }

    [Fact]
    public void DuplicateResourceGuid_ShouldNotCrashGuidMapBuild()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        string duplicateGuid = TestEntityFactory.NewGuid();
        TestEntityFactory.CreateResource(root, "a.png", duplicateGuid, [1, 2, 3]);
        TestEntityFactory.CreateResource(root, "b.png", duplicateGuid, [4, 5, 6]);
        root.GuidDuplicateIssues =
        [
            new GuidDuplicateIssue(duplicateGuid, "a.png.guid", "b.png.guid")
        ];

        using ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);

        string guidMap = CanvasHtmlViewAssets.BuildStandaloneGuidMapScript(model);

        Assert.Contains(duplicateGuid, guidMap);
        Assert.Contains("a.png", guidMap);
        Assert.DoesNotContain("b.png", guidMap);
    }
}
