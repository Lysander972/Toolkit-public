using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Analysis;

public sealed class ProjectLogicContextCacheTests
{
    [Fact]
    public void FindScriptPathsByName_Updates_After_Rebuild()
    {
        using ProjectModel model = ProjectModel.CreateForTesting();
        ProjectRootEntity root = TestEntityFactory.CreateRoot();

        // 创建第一个脚本
        TestEntityFactory.CreateCodeRepoScript(
            root,
            "repository/a.code",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "begincodeV2\nname:a\nlines:\nfun break()\nendcode\n");

        // 首次设置根实体 → 缓存包含 a
        model.SetRootForTesting(root);

        ProjectLogicContext logic = new ProjectLogicContext(model);
        IReadOnlyList<string> scripts1 = logic.FindScriptPathsByName("a");
        Assert.Single(scripts1);

        // 创建第二个脚本（直接操作实体树）
        TestEntityFactory.CreateCodeRepoScript(
            root,
            "repository/b.code",
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "begincodeV2\nname:b\nlines:\nfun break()\nendcode\n");

        // 实体树变更后，必须重新设置根实体以刷新缓存
        // 这符合规范：缓存在实体树构建/重建后全量更新
        model.SetRootForTesting(root);

        // 重建逻辑索引
        logic.RebuildIndexes();

        // 现在可以找到 b
        IReadOnlyList<string> scripts2 = logic.FindScriptPathsByName("b");
        Assert.Single(scripts2);
    }
}
