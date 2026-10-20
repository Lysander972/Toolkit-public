using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint.Rules;

public sealed class DuplicateGuidRuleTests
{
    [Fact]
    public void AnalyzeProject_Should_Report_Error_When_Guid_Is_Duplicated()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        string duplicateGuid = TestEntityFactory.NewGuid();

        TestEntityFactory.CreateCanvas(root, "ui/a.canvas_", duplicateGuid, [1, 2, 3]);
        TestEntityFactory.CreateCanvas(root, "ui/b.canvas_", duplicateGuid, [4, 5, 6]);
        root.GuidDuplicateIssues =
        [
            new GuidDuplicateIssue(duplicateGuid, "ui/a.canvas_.guid", "ui/b.canvas_.guid")
        ];

        using ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);

        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysis);

        LintDiagnostic diagnostic = Assert.Single(diagnostics, static d => d.RuleId == LintRuleIds.StructGuidDuplicate);
        Assert.Equal(LintSeverity.Error, diagnostic.Severity);
        Assert.Equal("ui/b.canvas_.guid", diagnostic.Location.Path);
        Assert.Contains(duplicateGuid, diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("ui/a.canvas_.guid", diagnostic.Message, StringComparison.Ordinal);
    }
}
