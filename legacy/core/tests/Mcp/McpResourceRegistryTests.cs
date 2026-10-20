using System.IO.Compression;
using System.Reflection;
using Toolkit.Mcp;
using Xunit;

namespace Toolkit.Core.Tests.Mcp;

/// <summary>
/// McpResourceRegistry 手册 gzip 容器解析回归测试：
/// syncResources 生成的 manifest 是 camelCase JSON（path/content/files/version），
/// 解析器 record 参数必须与之精确对齐——PascalCase 参数会把字段反序列化为 null，
/// 导致 LoadDocResources 抛 NullReferenceException（Host 启动即退）。
/// </summary>
public class McpResourceRegistryTests
{
    private static byte[] BuildCamelCaseManifestGzip()
    {
        string json = """
            {"version":1,"stats":{"fileCount":1,"totalBytes":10,"skillMdHash":null},
             "files":[{"path":"machinist/SKILL.md","content":"# 技能"}]}
            """;
        using MemoryStream ms = new();
        using (GZipStream gz = new(ms, CompressionMode.Compress, leaveOpen: true))
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            gz.Write(bytes, 0, bytes.Length);
        }
        return ms.ToArray();
    }

    [Fact]
    public void ReadManualManifest_解析camelCaseJson_字段非null()
    {
        string tmpPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mcp-manifest-{Guid.NewGuid():N}.gz");
        System.IO.File.WriteAllBytes(tmpPath, BuildCamelCaseManifestGzip());
        try
        {
            MethodInfo? method = typeof(McpResourceRegistry).GetMethod(
                "ReadManualManifest",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            object? manifest = method.Invoke(null, [tmpPath]);
            Assert.NotNull(manifest);

            object? files = manifest.GetType().GetProperty("files")?.GetValue(manifest);
            Assert.NotNull(files);
            int count = 0;
            foreach (object entry in (System.Collections.IEnumerable)files)
            {
                if (entry.GetType().GetProperty("path")?.GetValue(entry) is string path
                    && entry.GetType().GetProperty("content")?.GetValue(entry) is string content)
                {
                    Assert.NotEqual(string.Empty, path);
                    Assert.NotEqual(string.Empty, content);
                    count++;
                }
            }
            Assert.Equal(1, count);
        }
        finally
        {
            System.IO.File.Delete(tmpPath);
        }
    }
}
