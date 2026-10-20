using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using Toolkit.Core.Infra;

namespace Toolkit.Mcp;

/// <summary>
/// MCP 资源注册表
/// </summary>
public sealed class McpResourceRegistry
{
    private readonly ConcurrentDictionary<string, ResourceEntry> _resources;

    public McpResourceRegistry()
    {
        _resources = new ConcurrentDictionary<string, ResourceEntry>(StringComparer.Ordinal);

        LoadDocResources();
    }

    public IList<Resource> ListResources()
    {
        return _resources.Values
            .Select(r => r.Descriptor)
            .OrderBy(r => r.Uri, StringComparer.Ordinal)
            .ToList();
    }

    public IList<ResourceTemplate> ListResourceTemplates()
    {
        return Array.Empty<ResourceTemplate>();
    }

    public ResourceContents? TryReadResource(string uri)
    {
        if (!_resources.ContainsKey(uri))
        {
            return null;
        }

        ResourceEntry entry = _resources[uri];

        // 资源内容来自插件安装目录的 gzip 容器，不是工程内路径，禁止走 FileIO。
        return new TextResourceContents
        {
            Uri = entry.Descriptor.Uri,
            MimeType = entry.Descriptor.MimeType,
            Text = entry.ContentText
        };
    }

    private void LoadDocResources()
    {
        // 从程序集位置推导 creatai-manual.gz 路径
        // 部署结构: src/frontend/vscode_plugin/host/win-x64/*.exe 或 src/frontend/vscode_plugin/host/*.dll
        // 目标: src/frontend/vscode_plugin/resources/creatai-manual.gz
        // 这些路径属于插件安装目录，不是工程内路径，禁止走 FileIO。
        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        // 向上查找直到找到 resources/creatai-manual.gz
        string dir = SystemIOWrapper.GetParentDirectory(assemblyPath);
        string? manualGzipPath = null;
        for (int i = 0; i < 4 && dir.Length > 0; i++)
        {
            string candidate = SystemIOWrapper.Combine(dir, "resources", "creatai-manual.gz");
            if (SystemIOWrapper.FileExists(candidate))
            {
                manualGzipPath = candidate;
                break;
            }
            dir = SystemIOWrapper.GetParentDirectory(dir);
        }

        if (manualGzipPath == null)
        {
            return;
        }

        ManualManifest? manifest = ReadManualManifest(manualGzipPath);
        if (manifest == null)
        {
            return;
        }

        // 对外 Skill 内容统一以 machinist/ 前缀进入容器；资源 URI 沿用 doc/<dirName>
        foreach (ManualFileEntry file in manifest.files)
        {
            if (!file.path.StartsWith("machinist/", StringComparison.Ordinal))
            {
                continue;
            }

            string relPath = file.path["machinist/".Length..];
            if (!string.Equals(relPath, "SKILL.md", StringComparison.Ordinal))
            {
                continue;
            }

            string dirName = "machinist";
            FrontMatter meta = ReadFrontMatter(file.content);
            string displayName = dirName;
            if (!string.IsNullOrWhiteSpace(meta.Name))
                displayName = meta.Name;

            Resource descriptor = new Resource
            {
                Uri = $"doc/{dirName}",
                Name = displayName,
                Description = meta.Description,
                MimeType = "text/markdown"
            };

            _resources[descriptor.Uri] = new ResourceEntry(descriptor, file.content);
        }
    }

    private static FrontMatter ReadFrontMatter(string text)
    {
        try
        {
            IParser parser = new Parser(new StringReader(text));
            parser.Consume<StreamStart>();
            DocumentStart documentStart = parser.Consume<DocumentStart>();
            if (documentStart.IsImplicit)
                return new FrontMatter(null, null);

            Dictionary<string, string?> meta = new DeserializerBuilder()
                .Build()
                .Deserialize<Dictionary<string, string?>>(parser);
            meta.TryGetValue("name", out string? name);
            meta.TryGetValue("description", out string? description);
            return new FrontMatter(name, description);
        }
        catch (Exception ex) when (ex is YamlException or IOException or UnauthorizedAccessException)
        {
            return new FrontMatter(null, null);
        }
    }

    private static ManualManifest? ReadManualManifest(string gzipPath)
    {
        try
        {
            using FileStream fileStream = SystemIOWrapper.OpenRead(gzipPath);
            using GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<ManualManifest>(gzipStream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private sealed record ResourceEntry(Resource Descriptor, string ContentText);

    private sealed record FrontMatter(string? Name, string? Description);

    // 记录参数名与 syncResources 生成的 camelCase JSON 精确对齐（System.Text.Json
    // 对 record 位置参数的匹配区分大小写，PascalCase 参数会把 path/content 反序列化为 null）
    private sealed record ManualManifest(int version, ManualStats stats, List<ManualFileEntry> files);

    private sealed record ManualStats(int fileCount, long totalBytes, string? skillMdHash);

    private sealed record ManualFileEntry(string path, string content);
}
