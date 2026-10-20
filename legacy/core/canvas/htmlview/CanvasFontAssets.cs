using Toolkit.Core.Infra;

namespace Toolkit.Core.Canvas;

internal static class CanvasFontAssets
{
    internal const string MediumResourceName = "Toolkit.Core.canvas.fonts.AlibabaPuhuiMedium.ttf.gz";
    internal const string LightResourceName = "Toolkit.Core.canvas.fonts.AlibabaPuhuiLight.ttf.gz";

    private static readonly Lazy<string> MediumDataUrlCache = new(static () => BuildFontDataUrl(MediumResourceName));
    private static readonly Lazy<string> LightDataUrlCache = new(static () => BuildFontDataUrl(LightResourceName));

    public static string GetMediumDataUrl() => MediumDataUrlCache.Value;

    public static string GetLightDataUrl() => LightDataUrlCache.Value;

    private static string BuildFontDataUrl(string resourceName)
    {
        byte[] bytes = EmbeddedResourceHelper.LoadBytes(resourceName);
        if (bytes.Length == 0)
            throw new InvalidDataException($"Canvas 字体资源为空: {resourceName}");
        return "data:font/ttf;base64," + Convert.ToBase64String(bytes);
    }
}
