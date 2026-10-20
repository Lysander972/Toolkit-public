namespace Toolkit.Core.TsView;

internal static class TsViewTemplateLiteralText
{
    internal static string Dedent(string text)
    {
        if (!text.Contains('\n')) return text;

        string[] lines = text.Split('\n');
        int minIndent = int.MaxValue;
        for (int index = 1; index < lines.Length; index++)
        {
            string line = lines[index];
            int spaces = 0;
            while (spaces < line.Length && line[spaces] == ' ') spaces++;
            if (spaces < minIndent) minIndent = spaces;
        }

        if (minIndent == 0 || minIndent == int.MaxValue) return text;

        System.Text.StringBuilder builder = new();
        builder.Append(lines[0]);
        for (int index = 1; index < lines.Length; index++)
        {
            builder.Append('\n');
            string line = lines[index];
            builder.Append(line.Length >= minIndent ? line[minIndent..] : line);
        }
        return builder.ToString();
    }
}
