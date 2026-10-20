using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

public sealed class ProjectModelGuidIndexTests
{
    [Fact]
    public void FindByGuid_Should_Return_ModelGuidIndex_Winner_When_Guid_Is_Duplicated()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        string duplicateGuid = TestEntityFactory.NewGuid();
        CanvasEntity first = TestEntityFactory.CreateCanvas(root, "ui/a.canvas_", duplicateGuid, [1]);
        TestEntityFactory.CreateCanvas(root, "ui/b.canvas_", duplicateGuid, [2]);
        root.GuidDuplicateIssues =
        [
            new GuidDuplicateIssue(duplicateGuid, "ui/a.canvas_.guid", "ui/b.canvas_.guid")
        ];

        using ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);

        ProjectEntity? found = model.FindByGuid(duplicateGuid);

        Assert.Same(first, found);
        GuidDuplicateIssue issue = Assert.Single(model.GuidDuplicateIssues);
        Assert.Equal("ui/b.canvas_.guid", issue.DuplicateGuidFileRelativePath);
    }
}
