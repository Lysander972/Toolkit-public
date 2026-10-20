using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;

namespace Toolkit.Core.TsView;

internal static class TsViewValidationRules
{
    internal static string BuildExpectedReturnStatement(string? expectedRetVar)
    {
        if (string.IsNullOrWhiteSpace(expectedRetVar))
            return "return;";
        return $"return {expectedRetVar};";
    }

    public static void CollectNonCompliantMarkersFromAst(
        BescriptAst ast,
        IReadOnlyList<int> asAssertionLines,
        string filePath,
        List<ToolError> errors)
    {
        foreach (BescriptAstNode node in ast.RootNodes)
            CollectNonCompliantMarkersFromNode(node, asAssertionLines, filePath, errors);
    }

    private static void CollectNonCompliantMarkersFromNode(
        BescriptAstNode node,
        IReadOnlyList<int> asAssertionLines,
        string filePath,
        List<ToolError> errors)
    {
        if (node.Kind == BescriptStatementKind.Comment || node.Kind == BescriptStatementKind.Skip || node.IsDisabled)
        {
            foreach (BescriptAstNode child in node.Children)
                CollectNonCompliantMarkersFromNode(child, asAssertionLines, filePath, errors);
            return;
        }

        if (node.Kind == BescriptStatementKind.Call && node is IHasCall { Call: var call })
        {
            foreach (BescriptCallArgument arg in call.Arguments)
            {
                if (string.Equals(arg.RawText, "undefined", StringComparison.Ordinal))
                {
                    errors.Add(new ToolError(filePath, node.Line, null,
                        "发现 undefined 占位符：原始脚本缺失参数。", ToolDiagnosticSeverity.Error));
                    break;
                }
            }
        }

        if (asAssertionLines.Contains(node.Line))
        {
            errors.Add(new ToolError(filePath, node.Line, null,
                "发现 as 类型断言：TS 类型推断与原始声明不一致。", ToolDiagnosticSeverity.Error));
        }

        foreach (BescriptAstNode child in node.Children)
            CollectNonCompliantMarkersFromNode(child, asAssertionLines, filePath, errors);
    }
}
