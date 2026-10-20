using Toolkit.Core.Persistence;

namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    internal static IReadOnlyList<LintDiagnostic> ValidateDuplicateGuids(ProjectModel model)
    {
        List<LintDiagnostic> diagnostics = [];
        foreach (GuidDuplicateIssue issue in model.GuidDuplicateIssues)
        {
            diagnostics.Add(new LintDiagnostic(
                LintRuleIds.StructGuidDuplicate,
                LintSeverity.Error,
                $"GUID 重复: {issue.Guid}，已存在于 {issue.FirstGuidFileRelativePath}",
                new LintLocation(issue.DuplicateGuidFileRelativePath, 1)));
        }

        return diagnostics;
    }
}
