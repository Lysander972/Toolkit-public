using System.IO.Compression;
using System.Reflection;

namespace Toolkit.Core.Infra;

/// <summary>
/// 从程序集嵌入资源加载内容。资源名以 .gz 结尾时自动 gzip 解压。
/// </summary>
internal static class EmbeddedResourceHelper
{
    private static readonly Assembly Asm = typeof(EmbeddedResourceHelper).Assembly;

    internal static bool Exists(string resourceName)
    {
        return Asm.GetManifestResourceInfo(resourceName) != null;
    }

    internal static string LoadText(string resourceName)
    {
        using Stream stream = OpenDecompressedStream(resourceName);
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    internal static byte[] LoadBytes(string resourceName)
    {
        using Stream stream = OpenDecompressedStream(resourceName);
        using MemoryStream ms = new();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static Stream OpenDecompressedStream(string resourceName)
    {
        Stream raw = Asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"嵌入资源未找到: {resourceName}");
        if (resourceName.EndsWith(".gz", StringComparison.Ordinal))
            return new GZipStream(raw, CompressionMode.Decompress);
        return raw;
    }

}
