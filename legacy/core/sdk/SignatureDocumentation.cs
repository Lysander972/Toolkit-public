using System.Text;

namespace Toolkit.Core.Sdk;

internal static class SignatureDocumentation
{
    public static void AppendSummary(StringBuilder sb, string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        sb.AppendLine(summary);
        sb.AppendLine();
    }

    public static void AppendParameters<T>(
        StringBuilder sb,
        IReadOnlyList<T> parameters,
        Func<T, string> getName,
        Func<T, string> getTypeName,
        Func<T, string?> getDescription)
    {
        if (parameters.Count == 0)
        {
            return;
        }

        sb.AppendLine("参数:");
        foreach (T? p in parameters)
        {
            sb.Append("  - ");
            sb.Append(getName(p));
            sb.Append(": ");
            sb.Append(getTypeName(p));
            string? desc = getDescription(p);
            if (!string.IsNullOrWhiteSpace(desc))
            {
                sb.Append(" - ");
                sb.Append(desc);
            }
            sb.AppendLine();
        }
        sb.AppendLine();
    }

    public static void AppendRemarks(StringBuilder sb, string? remarks)
    {
        if (string.IsNullOrWhiteSpace(remarks))
        {
            return;
        }

        sb.AppendLine("备注:");
        sb.AppendLine(remarks);
    }
}
