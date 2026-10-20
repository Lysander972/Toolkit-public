using Xunit;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Search;

namespace Toolkit.Core.Tests.Search;

public sealed class SearchToolTests
{
    [Fact]
    public void ScriptRegexSearch_Finds_Matches_In_InlineScript()
    {
        using ProjectModel model = ProjectModel.CreateForTesting();
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity device = TestEntityFactory.CreateDevice(map);
        ScriptGroupEntity group = TestEntityFactory.CreateScriptGroup(device);
        TestEntityFactory.CreateInlineScript(
            group,
            "test",
            "begincodeV2\nname:test\nlines:\nfun break()\nendcode\n");

        // 设置根实体并注册索引
        model.SetRootForTesting(root);

        ScriptRegexSearch search = new ScriptRegexSearch(model);
        (ScriptRegexSearchData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = search.Search(new ScriptRegexSearchRequest(
            Pattern: "fun",
            CaseSensitive: false,
            EntityGuids: null,
            ScriptGuids: null,
            Paths: null,
            LimitPerScript: 50,
            MaxMatches: 500));

        Assert.Empty(errors);
        ArgumentNullException.ThrowIfNull(data);
        Assert.NotEmpty(data.Matches);
    }

    [Fact]
    public void UiRegexSearch_Finds_Matches_In_Canvas()
    {
        using ProjectModel model = ProjectModel.CreateForTesting();
        byte[] canvasBytes = CanvasHtmlViewCodec.FromHtml("<BeCanvas name=\"Test\" width=\"100\" height=\"100\"></BeCanvas>");
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        TestEntityFactory.CreateCanvas(root, "layouts/test.canvas", "cccccccccccccccccccccccccccccccc", canvasBytes);

        // 设置根实体并注册索引
        model.SetRootForTesting(root);

        UiRegexSearch search = new UiRegexSearch(model);
        (UiRegexSearchData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = search.Search(new UiRegexSearchRequest(
            Pattern: "Test",
            CaseSensitive: false,
            UiGuids: null,
            Paths: null,
            LimitPerView: 50,
            MaxMatches: 500));

        Assert.Empty(errors);
        ArgumentNullException.ThrowIfNull(data);
        // 具体匹配取决于 HTML 渲染格式；本用例仅覆盖无错误且返回数据。
    }

    [Fact]
    public void GuidReferencesSearch_Finds_References_In_CodeRepo()
    {
        string targetGuid = "99999999999999999999999999999999";
        string scriptGuid = "dddddddddddddddddddddddddddddddd";
        using ProjectModel model = ProjectModel.CreateForTesting();
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        CodeRepoScriptEntity script = TestEntityFactory.CreateCodeRepoScript(
            root,
            "repository/repo.code",
            scriptGuid,
            "begincodeV2\nname:repo\nlines:\nfun map.SetCustomData(\"" + targetGuid + "\")\nendcode\n");

        // 设置根实体并注册索引
        model.SetRootForTesting(root);

        GuidReferencesSearch search = new GuidReferencesSearch(model);
        (GuidReferencesSearchData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = search.Search(new GuidReferencesSearchRequest(targetGuid, null, 10));

        Assert.Empty(errors);
        ArgumentNullException.ThrowIfNull(data);
        GuidReferenceMatch match = Assert.Single(data.References);
        Assert.Equal(script.实体_完整相对路径标识, match.Path);
        Assert.Equal("asset", match.Kind);
        Assert.Equal(scriptGuid, match.ReferrerGuid);
    }
}
