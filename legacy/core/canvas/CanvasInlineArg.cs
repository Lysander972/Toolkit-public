using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Canvas;

public enum CanvasInlineArgFormatKind
{
    LegacyBase64,
    NodeHjson
}

public sealed record CanvasInlineArgData(
    CanvasInlineArgFormatKind FormatKind,
    string CanvasName,
    string PayloadText
);

internal enum CanvasInlineArgParseErrorKind
{
    Empty,
    NotCanvasPrefix,
    MissingEquals,
    EmptyBase64,
    MissingNodeHjsonTerminator,
    EmptyNodeHjson
}

public static class CanvasInlineArg
{
    internal const string LegacyPrefix = "canvas:";

    /// <summary>
    /// 新版格式标记起始：==hjson:
    /// 完整格式：==hjson:&lt;canvasName&gt;:&lt;content&gt;==
    /// BES 编码后 : → @1，即 ==hjson@1&lt;name&gt;@1
    /// </summary>
    internal const string NodeHjsonMarker = "==hjson:";
    internal const string NodeHjsonSuffix = "==";

    /// <summary>BES 编码后的标记起始，用于多行检测。</summary>
    internal const string NodeHjsonRawMarker = "==hjson@1";
    internal const string NodeHjsonRawEndWithCloseParen = "==)";

    public static CanvasInlineArgData? Parse(string rawText)
    {
        if (rawText.StartsWith(LegacyPrefix, StringComparison.Ordinal))
        {
            int eqIndex = rawText.IndexOf('=', LegacyPrefix.Length);
            if (eqIndex < 0 || eqIndex == rawText.Length - 1)
                return null;

            string canvasName = rawText[LegacyPrefix.Length..eqIndex];
            string base64 = rawText[(eqIndex + 1)..];
            return new CanvasInlineArgData(CanvasInlineArgFormatKind.LegacyBase64, canvasName, base64);
        }

        if (rawText.StartsWith(NodeHjsonMarker, StringComparison.Ordinal))
        {
            if (!rawText.EndsWith(NodeHjsonSuffix, StringComparison.Ordinal))
                return null;

            int nameEnd = rawText.IndexOf(':', NodeHjsonMarker.Length);
            if (nameEnd < 0)
                return null;

            string canvasName = rawText[NodeHjsonMarker.Length..nameEnd];
            string payloadText = rawText[(nameEnd + 1)..^NodeHjsonSuffix.Length];
            return new CanvasInlineArgData(CanvasInlineArgFormatKind.NodeHjson, canvasName, payloadText);
        }

        return null;
    }

    internal static CanvasInlineArgParseErrorKind ClassifyError(string rawText)
    {
        if (rawText.StartsWith(LegacyPrefix, StringComparison.Ordinal))
        {
            int eqIndex = rawText.IndexOf('=', LegacyPrefix.Length);
            if (eqIndex < 0)
                return CanvasInlineArgParseErrorKind.MissingEquals;
            return CanvasInlineArgParseErrorKind.EmptyBase64;
        }

        if (rawText.StartsWith(NodeHjsonMarker, StringComparison.Ordinal))
        {
            if (!rawText.EndsWith(NodeHjsonSuffix, StringComparison.Ordinal))
                return CanvasInlineArgParseErrorKind.MissingNodeHjsonTerminator;
            int nameEnd = rawText.IndexOf(':', NodeHjsonMarker.Length);
            if (nameEnd < 0)
                return CanvasInlineArgParseErrorKind.MissingNodeHjsonTerminator;
            string payload = rawText[(nameEnd + 1)..^NodeHjsonSuffix.Length];
            return default;
        }

        return CanvasInlineArgParseErrorKind.NotCanvasPrefix;
    }

    internal static bool IsNodeHjson(string rawText)
    {
        return rawText.StartsWith(NodeHjsonMarker, StringComparison.Ordinal);
    }

    /// <summary>
    /// 构建旧版 Base64 内联文本（编码由调用方统一处理）。
    /// </summary>
    internal static string BuildLegacyBase64RawText(string canvasName, byte[] canvasBytes)
    {
        string base64 = Convert.ToBase64String(canvasBytes);
        return LegacyPrefix + canvasName + "=" + base64;
    }

    /// <summary>
    /// 构建新版 Node Hjson 内联文本（编码由调用方统一处理）。
    /// 格式：==hjson:&lt;canvasName&gt;:&lt;content&gt;==
    /// </summary>
    internal static string BuildNodeHjsonRawText(string canvasName, string hjsonText)
    {
        string[] lines = TextLines.SplitLines(hjsonText);
        string normalized = TextLines.BuildLines(lines);
        return NodeHjsonMarker + canvasName + ":\n" + normalized + NodeHjsonSuffix;
    }

    /// <summary>
    /// 将 Hjson 文本编码为 BES 内联格式。
    /// 格式：==hjson@1&lt;encodedName&gt;@1&lt;encodedContent&gt;\n==
    /// </summary>
    internal static string EncodeNodeHjsonForBescript(string canvasName, string hjsonText)
    {
        string encodedName = BescriptTextCodec.ReplaceInvalidChar(canvasName.AsSpan());
        string[] lines = TextLines.SplitLines(hjsonText);
        string[] encodedLines = new string[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            encodedLines[i] = BescriptTextCodec.ReplaceInvalidChar(lines[i].AsSpan());
        }

        return NodeHjsonRawMarker + encodedName + "@1\n" + TextLines.BuildLines(encodedLines) + NodeHjsonSuffix;
    }
}
