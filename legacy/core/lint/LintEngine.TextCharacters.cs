using System.Globalization;
using System.Text;
using Toolkit.Core.Canvas;

namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    private const string UnsupportedTextCharacterRuleText = "BMP 非控制字符、Tab、换行、回车";

    internal static IReadOnlyList<LintDiagnostic> ValidateBescriptTextCharacters(string sourceText, string sourceRelativePath)
    {
        List<LintDiagnostic> diagnostics = [];
        AddUnsupportedTextDiagnostics(diagnostics, sourceRelativePath, 1, "BEScript 源码", sourceText);
        return diagnostics;
    }

    internal static IReadOnlyList<LintDiagnostic> ValidateCanvasTextCharacters(CanvasNode root, string sourceRelativePath, int line)
    {
        List<LintDiagnostic> diagnostics = [];
        ValidateCanvasNodeTextCharacters(root, sourceRelativePath, line, diagnostics);
        return diagnostics;
    }

    private static void ValidateCanvasNodeTextCharacters(
        CanvasNode node,
        string sourceRelativePath,
        int line,
        List<LintDiagnostic> diagnostics)
    {
        // name 是所有节点的用户可见文本
        AddUnsupportedTextDiagnostics(diagnostics, sourceRelativePath, line, $"Canvas 字符串字段 {node.Type}.name", node.name.v);

        if (node is UILabelNode { text: StringVar labelText })
        {
            AddUnsupportedTextDiagnostics(diagnostics, sourceRelativePath, line, $"Canvas 字符串字段 {node.Type}.text", labelText.v);
        }
        else if (node is UITextProNode { text: StringVar textProText })
        {
            AddUnsupportedTextDiagnostics(diagnostics, sourceRelativePath, line, $"Canvas 字符串字段 {node.Type}.text", textProText.v);
        }
        else if (node is UIButtonNode { label: StringVar buttonText })
        {
            AddUnsupportedTextDiagnostics(diagnostics, sourceRelativePath, line, $"Canvas 字符串字段 {node.Type}.label", buttonText.v);
        }
        else if (node is UIInputNode { text: StringVar inputText })
        {
            AddUnsupportedTextDiagnostics(diagnostics, sourceRelativePath, line, $"Canvas 字符串字段 {node.Type}.text", inputText.v);
        }

        foreach (CanvasNode child in node.Children)
            ValidateCanvasNodeTextCharacters(child, sourceRelativePath, line, diagnostics);
    }

    private static void AddUnsupportedTextDiagnostics(
        List<LintDiagnostic> diagnostics,
        string sourceRelativePath,
        int startLine,
        string scope,
        string text)
    {
        Dictionary<int, List<string>> unsupportedCharactersByLine = [];
        int line = 1;
        foreach (Rune rune in text.EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            bool supported = rune.Value is '\r' or '\n' or '\t'
                || rune.Value <= 0xFFFF && category != UnicodeCategory.Control;

            if (!supported)
            {
                if (!unsupportedCharactersByLine.TryGetValue(line, out List<string>? characters))
                {
                    characters = [];
                    unsupportedCharactersByLine[line] = characters;
                }

                string character = rune.ToString();
                if (!characters.Contains(character))
                    characters.Add(character);
            }

            if (rune.Value == '\n')
                line++;
        }

        foreach ((int unsupportedLine, List<string> characters) in unsupportedCharactersByLine)
        {
            diagnostics.Add(new LintDiagnostic(
                LintRuleIds.UnsupportedTextCharacter,
                LintSeverity.Error,
                $"{scope}包含不支持字符: {string.Join(", ", characters)}（标准: {UnsupportedTextCharacterRuleText}）",
                new LintLocation(sourceRelativePath, startLine + unsupportedLine - 1)));
        }
    }
}
