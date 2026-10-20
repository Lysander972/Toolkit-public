using System.Reflection;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Xml;
using Toolkit.Core.Canvas.Render;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Canvas;

public readonly record struct CanvasHtmlSupportAsset(
    string FileName,
    string Content
);

public static class CanvasHtmlViewAssets
{
    /// <summary>
    /// 支持的预览资源扩展名
    /// 【特例】此处允许大小写不敏感，因为文件系统跨平台兼容性需要。
    /// 这是唯一允许的例外，其他位置绝对禁止 OrdinalIgnoreCase。
    /// </summary>
    private static readonly HashSet<string> SupportedPreviewAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".atlas",
        ".json",
        ".skel"
    };
    // 嵌入资源前缀：canvas/render 目录下的 TS 文件
    private const string CanvasRenderResourcePrefix = "Toolkit.Core.canvas.render.";
    private const string TsFileExtension = ".ts";

    // 进程级静态缓存：嵌入资源在进程生命周期内不变，避免每次刷新重读重拼
    private static string? _cachedCss;
    private static string? _cachedScript;
    private static CanvasPreviewSettings _cachedScriptSettings;
    private static IReadOnlyList<CanvasHtmlSupportAsset>? _cachedTsSources;

    public const string CssFileName = "canvas-view.css";
    public const string ScriptFileName = "CanvasLayoutEngine.DO_NOT_READ_OR_EDIT.generated.js";
    public const string PaletteFileName = "canvas-palette.js";
    public const string StandaloneGuidMapFileName = "canvas-guid-map.js";
    // 第一层：静态 support 文件名（固定结构）
    public static IReadOnlyList<string> StaticSupportFileNames { get; } = [CssFileName, ScriptFileName];
    // 第二层：索引 support 文件名（按模型全量刷新）
    public static IReadOnlyList<string> IndexSupportFileNames { get; } = [PaletteFileName, StandaloneGuidMapFileName];
    public static IReadOnlyList<string> SupportFileNames { get; } = [CssFileName, ScriptFileName, PaletteFileName, StandaloneGuidMapFileName];
    private static IReadOnlyList<string>? _staticSupportFileNamesWithTs;
    public static IReadOnlyList<string> StaticSupportFileNamesWithTs => _staticSupportFileNamesWithTs ??= BuildStaticSupportFileNameList();
    private static IReadOnlyList<string> BuildStaticSupportFileNameList()
    {
        List<string> names = [CssFileName, ScriptFileName];
        Assembly asm = typeof(CanvasHtmlViewAssets).Assembly;
        foreach (string name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(CanvasRenderResourcePrefix, StringComparison.Ordinal)) continue;
            string fileName = name.Substring(CanvasRenderResourcePrefix.Length);
            if (!fileName.EndsWith(TsFileExtension, StringComparison.Ordinal)) continue;
            names.Add(fileName);
        }
        return names.AsReadOnly();
    }

    /// <summary>
    /// 第一层：构建静态 support 内容（固定结构，按全量刷新覆盖）。
    /// 【规范】渲染引擎 TS 源码与 JS 产物同级，均为静态支持资源。
    /// </summary>
    public static IReadOnlyList<CanvasHtmlSupportAsset> BuildStaticSupportAssets(
        IReadOnlyDictionary<string, PaletteColor> palette,
        CanvasPreviewSettings previewSettings = default)
    {
        CanvasPreviewSettings settings = previewSettings == default
            ? new CanvasPreviewSettings(1080, 2160)
            : previewSettings;

        // CSS + JS 产物（命中缓存时跳过重读和重拼）
        List<CanvasHtmlSupportAsset> assets =
        [
            new(CssFileName, BuildCss()),
            new(ScriptFileName, _cachedScript ??= CanvasPreviewScript.BuildLayoutEngineScript(settings)),
            // TS 源码（嵌入资源不变，首次读取后缓存）
            .. BuildTsSources(),
        ];

        return assets;
    }

    private static string BuildCss()
    {
        if (_cachedCss != null) return _cachedCss;

        string mediumFontDataUrl = CanvasFontAssets.GetMediumDataUrl();
        string lightFontDataUrl = CanvasFontAssets.GetLightDataUrl();

        // 字体CSS（动态生成，因为字体是base64嵌入的）
        string fontCss = string.Concat(
            "@font-face{font-family:\"ToolkitAlibabaPuHuiTiMedium\";src:url(\"", mediumFontDataUrl, "\") format(\"truetype\");font-display:swap;}",
            "\n@font-face{font-family:\"ToolkitAlibabaPuHuiTiLight\";src:url(\"", lightFontDataUrl, "\") format(\"truetype\");font-display:swap;}"
        );

        // 从嵌入资源读取主CSS文件
        string mainCss = LoadEmbeddedCss();

        _cachedCss = fontCss + "\n" + mainCss;
        return _cachedCss;
    }

    private static string LoadEmbeddedCss()
    {
        const string cssResourceName = "Toolkit.Core.canvas.render.canvas-view.css";
        Assembly assembly = typeof(CanvasHtmlViewAssets).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(cssResourceName);
        if (stream == null)
        {
            throw new InvalidDataException($"Canvas CSS资源不存在: {cssResourceName}");
        }
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

#pragma warning disable TK006 // ??= 懒加载是惯用模式，非一次性 Helper
    private static IReadOnlyList<CanvasHtmlSupportAsset> BuildTsSources()
    {
        return _cachedTsSources ??= BuildTsSourcesImpl();
    }
#pragma warning restore TK006

    private static IReadOnlyList<CanvasHtmlSupportAsset> BuildTsSourcesImpl()
    {
        List<CanvasHtmlSupportAsset> tsSources = [];
        Assembly asm = typeof(CanvasHtmlViewAssets).Assembly;
        foreach (string name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(CanvasRenderResourcePrefix, StringComparison.Ordinal)) continue;
            string fileName = name.Substring(CanvasRenderResourcePrefix.Length);
            if (!fileName.EndsWith(TsFileExtension, StringComparison.Ordinal)) continue;
            using Stream? stream = asm.GetManifestResourceStream(name);
            if (stream == null) continue;
            using StreamReader reader = new(stream);
            tsSources.Add(new CanvasHtmlSupportAsset(fileName, reader.ReadToEnd()));
        }
        return tsSources.AsReadOnly();
    }

    public static string BuildPaletteScript(IReadOnlyDictionary<string, PaletteColor> palette)
    {
        Dictionary<string, object> paletteColor255 = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, PaletteColor> entry in palette)
        {
            PaletteColor color = entry.Value;
            paletteColor255[entry.Key] = new
            {
                r = (int)Math.Round(color.R * 255f, MidpointRounding.AwayFromZero),
                g = (int)Math.Round(color.G * 255f, MidpointRounding.AwayFromZero),
                b = (int)Math.Round(color.B * 255f, MidpointRounding.AwayFromZero),
                a = (int)Math.Round(color.A * 255f, MidpointRounding.AwayFromZero)
            };
        }

        string paletteJson = JsonSerializer.Serialize(paletteColor255, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        });

        return "window.CANVAS_PALETTE = " + paletteJson + ";\n";
    }

    public static string BuildStandaloneGuidMapScript(ProjectModel model)
    {
        Dictionary<string, string> guidResourceMappings = new(StringComparer.Ordinal);
        foreach ((string guid, ProjectEntity entity) in model.EntitiesByGuid)
        {
            if (entity is not ResourceEntity resource)
                continue;

            string assetRelativePath = WorkspacePath.RequireRelativePath(resource.实体_完整相对路径标识);
            if (!SupportedPreviewAssetExtensions.Contains(Path.GetExtension(assetRelativePath)))
            {
                continue;
            }

            guidResourceMappings.Add(guid, assetRelativePath);
        }

        string mappingsJson = JsonSerializer.Serialize(guidResourceMappings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = true
        });
        string baseRootRelativePrefix = string.Concat(
            Enumerable.Repeat("../", ScriptPathHelper.ViewsPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length));
        return "(function () {\n"
               + "  const raw = " + mappingsJson + ";\n"
               + "  const marker = '/" + ScriptPathHelper.ViewsPath + "/';\n"
               + "  const pathname = window.location.pathname.replace(/\\\\/g, '/');\n"
               + "  const markerIndex = pathname.lastIndexOf(marker);\n"
               + "  if (markerIndex < 0) {\n"
               + "    throw new Error('canvas-guid-map.js 只能在 temp/toolkit/views 下使用');\n"
               + "  }\n"
               + "  const htmlRelativePath = pathname.slice(markerIndex + marker.length);\n"
               + "  const htmlFileNameIndex = htmlRelativePath.lastIndexOf('/');\n"
               + "  const htmlDirRelativePath = htmlFileNameIndex >= 0 ? htmlRelativePath.slice(0, htmlFileNameIndex) : '';\n"
               + "  const htmlDirDepth = htmlDirRelativePath.length === 0 ? 0 : htmlDirRelativePath.split('/').filter(part => part.length > 0).length;\n"
               + "  const baseRootRelativePrefix = '" + baseRootRelativePrefix + "';\n"
               + "  let projectRootRelativePrefix = baseRootRelativePrefix;\n"
               + "  for (let i = 0; i < htmlDirDepth; i++) {\n"
               + "    projectRootRelativePrefix += '../';\n"
               + "  }\n"
               + "  window.CANVAS_PREVIEW_MODE = 'standalone';\n"
               + "  window.CANVAS_STANDALONE_GUID_RESOURCE_URLS = Object.fromEntries(Object.entries(raw).map(([guid, relativePath]) => [guid, projectRootRelativePrefix + relativePath]));\n"
               + "})();\n";
    }

    /// <summary>
    /// 构建内联 GUID 映射脚本（用于 PNG 渲染）。
    /// 只包含实际使用到的 GUID，值为 base64 data URL。
    /// 结构上与 BuildStandaloneGuidMapScript 保持一致。
    /// </summary>
    public static string BuildInlineGuidMapScript(
        IReadOnlySet<string> usedGuids,
        ProjectModel model,
        Func<string, byte[]> readResourceBytes)
    {
        Dictionary<string, string> guidBase64Mappings = new(StringComparer.Ordinal);

        foreach (string guid in usedGuids)
        {
            ResourceEntity? resource = model.FindByGuid<ResourceEntity>(guid);
            // 原版行为：资源 GUID 找不到时跳过，TS 侧取不到映射则按白图 fallback
            if (resource == null)
                continue;

            string assetRelativePath = WorkspacePath.RequireRelativePath(resource.实体_完整相对路径标识);
            if (!SupportedPreviewAssetExtensions.Contains(Path.GetExtension(assetRelativePath)))
            {
                throw new InvalidOperationException($"资源不是可预览资源: {guid} => {assetRelativePath}");
            }

            byte[] assetBytes;
            try
            {
                assetBytes = readResourceBytes(assetRelativePath);
            }
            catch (FileNotFoundException)
            {
                continue;
            }

            guidBase64Mappings[guid] = $"data:{GetMimeTypeFromExtension(Path.GetExtension(assetRelativePath))};base64,{Convert.ToBase64String(assetBytes)}";
        }

        string mappingsJson = JsonSerializer.Serialize(guidBase64Mappings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        return "window.CANVAS_PREVIEW_MODE = 'inline';\n"
               + "window.CANVAS_STANDALONE_GUID_RESOURCE_URLS = " + mappingsJson + ";\n";
    }

    private static string GetMimeTypeFromExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".atlas" => "text/plain",
        ".json" => "application/json",
        ".skel" => "application/octet-stream",
        _ => throw new InvalidOperationException($"不支持的预览资源扩展名: {extension}")
    };

    /// <summary>
    /// 从 HTML 内容中提取所有预览资源 GUID。
    /// </summary>
    public static HashSet<string> ExtractUsedGuidsFromHtml(string html)
    {
        HashSet<string> guids = new(StringComparer.Ordinal);
        using XmlReader reader = XmlReader.Create(new StringReader(html), new XmlReaderSettings
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            IgnoreComments = true,
            IgnoreWhitespace = true
        });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || !reader.HasAttributes)
            {
                continue;
            }

            while (reader.MoveToNextAttribute())
            {
                // 无法模糊按"guid"匹配因为类型不同
                if (reader.Name is "slicedpicguid" or "fileguid" or "softmaskguid" or "atlasguid" or "skelguid" or "jsonguid")
                {
                    guids.Add(reader.Value);
                }
                else if (reader.Name == "pngguids")
                {
                    foreach (string guid in reader.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        guids.Add(guid);
                    }
                }
            }

            reader.MoveToElement();
        }

        return guids;
    }

    /// <summary>
    /// 构建自包含 HTML（用于 PNG 渲染）。
    /// 原则：直接生成完整 HTML，不替换、不依赖外部文件。
    /// </summary>
    public static string BuildSelfContainedHtml(
        string bodyHtml,
        IReadOnlyDictionary<string, PaletteColor> palette,
        CanvasPreviewSettings settings,
        ProjectModel model)
    {
        // 提取实际使用的 GUID
        HashSet<string> usedGuids = ExtractUsedGuidsFromHtml(bodyHtml);

        // 获取内联内容
        string cssContent = BuildCss();
        if (_cachedScript == null || _cachedScriptSettings != settings)
        {
            _cachedScript = CanvasPreviewScript.BuildLayoutEngineScript(settings);
            _cachedScriptSettings = settings;
        }
        string scriptContent = _cachedScript;
        string paletteScript = BuildPaletteScript(palette);
        string inlineGuidMapScript = BuildInlineGuidMapScript(
            usedGuids,
            model,
            relativePath => FileIO.ReadAllBytes(WorkspacePath.RequireRelativePath(relativePath)));

        // 直接构建完整 HTML
        // 【注意】脚本放在 body 底部，确保 DOM 就绪后再执行，避免异步等待。
        // 顺序要求：palette -> inlineGuidMap -> engine（引擎脚本必须最后）。
        System.Text.StringBuilder html = new();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"zh-CN\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"UTF-8\">");
        html.Append("<style>").Append(cssContent).AppendLine("</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine(bodyHtml);
        html.Append("<script>").Append(paletteScript).AppendLine("</script>");
        html.Append("<script>").Append(inlineGuidMapScript).AppendLine("</script>");
        html.Append("<script>").Append(scriptContent).AppendLine("</script>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return html.ToString();
    }

}
