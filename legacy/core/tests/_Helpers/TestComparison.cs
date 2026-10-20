using System.Text;
using DiffPlex.DiffBuilder.Model;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests;

internal static class TestComparison
{
    public static string? DiffText(string expected, string actual)
    {
        if (expected == actual) return null;
        string e = TextLineEndings.ToLf(expected);
        string a = TextLineEndings.ToLf(actual);
        if (e == a) return null;
        return UnifiedDiff(e, a);
    }

    public static string? DiffLines(string[] expectedLines, string[] actualLines)
        => DiffText(string.Join("\n", expectedLines), string.Join("\n", actualLines));

    /// <summary>
    /// 验证差异行与警告的对应关系：变化行块数不得超过警告数。
    /// 每处改写必须产出至少一条警告，防止"无声改写"。
    /// 返回 null 表示对应关系成立，否则返回不匹配的描述。
    /// </summary>
    public static string? VerifyWarningCorrespondence(
        string expected, string actual, IReadOnlyList<ToolError> warnings)
    {
        string e = TextLineEndings.ToLf(expected);
        string a = TextLineEndings.ToLf(actual);
        SideBySideDiffModel diff = DiffPlex.DiffBuilder.SideBySideDiffBuilder.Diff(e, a);

        // 收集变化行块数
        int changedBlockCount = 0;
        bool inBlock = false;
        for (int i = 0; i < diff.OldText.Lines.Count; i++)
        {
            bool changed = diff.OldText.Lines[i].Type != ChangeType.Unchanged
                        && diff.OldText.Lines[i].Type != ChangeType.Imaginary;
            if (changed && !inBlock)
            {
                changedBlockCount++;
                inBlock = true;
            }
            else if (!changed)
            {
                inBlock = false;
            }
        }

        if (changedBlockCount == 0)
            return "Diff 存在但未检测到差异行块";

        // 有警告即通过：一处改写可能产出多块 diff，1:N 对应关系合法
        if (warnings.Count > 0)
            return null;

        return $"差异 ({changedBlockCount} 个行块) 但无任何警告，存在未预期的无声改写。";
    }

    private static string UnifiedDiff(string expected, string actual)
    {
        SideBySideDiffModel diff = DiffPlex.DiffBuilder.SideBySideDiffBuilder.Diff(expected, actual);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("--- expected");
        sb.AppendLine("+++ actual");
        List<DiffPiece> oldLines = diff.OldText.Lines;
        List<DiffPiece> newLines = diff.NewText.Lines;
        for (int i = 0; i < oldLines.Count; i++)
        {
            DiffPiece o = oldLines[i];
            DiffPiece n = newLines[i];
            if (o.Type == ChangeType.Unchanged)
                sb.AppendLine($" {o.Text}");
            else
            {
                if (o.Type != ChangeType.Imaginary) sb.AppendLine($"-{o.Text}");
                if (n.Type != ChangeType.Imaginary) sb.AppendLine($"+{n.Text}");
            }
        }
        return sb.ToString();
    }
}
