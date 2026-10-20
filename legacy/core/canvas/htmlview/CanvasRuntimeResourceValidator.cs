using System.Buffers.Binary;
using Toolkit.Core.Canvas.Render;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Canvas;

internal static class CanvasRuntimeResourceValidator
{
    private static readonly object Lock = new();
    private static bool _validated;

    internal static void Validate()
    {
        lock (Lock)
        {
            if (_validated) return;

            ValidateFont(CanvasFontAssets.MediumResourceName);
            ValidateFont(CanvasFontAssets.LightResourceName);
            ValidateTextResource(CanvasPreviewScript.ScriptResourceName, "window.CanvasLayoutEngine");
            ValidateTextResource(CanvasPreviewScript.PixiScriptResourceName, "PIXI");
            ValidateTextResource(CanvasPreviewScript.PixiSpineScriptResourceName, "pixi-spine");

            _validated = true;
        }
    }

    private static void ValidateFont(string resourceName)
    {
        if (!EmbeddedResourceHelper.Exists(resourceName))
            throw new InvalidDataException($"Canvas 运行时资源不存在: {resourceName}");

        byte[] bytes = EmbeddedResourceHelper.LoadBytes(resourceName);
        if (bytes.Length < 4)
            throw new InvalidDataException($"Canvas 字体资源长度非法: {resourceName}");

        bool trueType = bytes[0] == 0x00 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00;
        bool openType = bytes[0] == 0x4F && bytes[1] == 0x54 && bytes[2] == 0x54 && bytes[3] == 0x4F;
        if (!trueType && !openType)
            throw new InvalidDataException($"Canvas 字体资源格式非法: {resourceName}");

        if (bytes.Length < 12)
            throw new InvalidDataException($"Canvas 字体资源表目录长度非法: {resourceName}");

        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2));
        int tableDirectoryLength = 12 + tableCount * 16;
        if (tableCount <= 0 || tableDirectoryLength > bytes.Length)
            throw new InvalidDataException($"Canvas 字体资源表目录非法: {resourceName}");

        HashSet<string> tableTags = [];
        for (int i = 0; i < tableCount; i++)
        {
            int recordOffset = 12 + i * 16;
            string tag = string.Concat(
                (char)bytes[recordOffset],
                (char)bytes[recordOffset + 1],
                (char)bytes[recordOffset + 2],
                (char)bytes[recordOffset + 3]);

            uint tableOffset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(recordOffset + 8, 4));
            uint tableLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(recordOffset + 12, 4));
            ulong tableEnd = (ulong)tableOffset + tableLength;
            if (tableLength == 0 || tableOffset > (uint)bytes.Length || tableEnd > (ulong)bytes.Length)
                throw new InvalidDataException($"Canvas 字体资源表记录非法: {resourceName} {tag}");

            tableTags.Add(tag);
        }

        string[] requiredTables = ["cmap", "head", "hhea", "maxp", "name"];
        foreach (string requiredTable in requiredTables)
        {
            if (!tableTags.Contains(requiredTable))
                throw new InvalidDataException($"Canvas 字体资源缺少必要表: {resourceName} {requiredTable}");
        }
    }

    private static void ValidateTextResource(string resourceName, string requiredText)
    {
        if (!EmbeddedResourceHelper.Exists(resourceName))
            throw new InvalidDataException($"Canvas 运行时资源不存在: {resourceName}");

        string text = EmbeddedResourceHelper.LoadText(resourceName);
        if (!text.Contains(requiredText, StringComparison.Ordinal))
            throw new InvalidDataException($"Canvas 脚本资源内容非法: {resourceName} 缺少 {requiredText}");
    }

    internal static void ResetForTesting()
    {
        lock (Lock)
        {
            _validated = false;
        }
    }
}
