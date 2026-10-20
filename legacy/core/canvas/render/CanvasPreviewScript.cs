using Toolkit.Core.Infra;

namespace Toolkit.Core.Canvas.Render;

/// <summary>
/// Canvas 预览设置
/// </summary>
public readonly record struct CanvasPreviewSettings(int DefaultWidth, int DefaultHeight);

internal static class CanvasPreviewScript
{
    internal const string ScriptResourceName = "Toolkit.Core.canvas.render.CanvasLayoutEngine.DO_NOT_READ_OR_EDIT.generated.js.gz";
    internal const string PixiScriptResourceName = "Toolkit.Core.canvas.render.third-party.pixi.min.js.gz";
    internal const string PixiSpineScriptResourceName = "Toolkit.Core.canvas.render.third-party.pixi-spine-3.8.js.gz";
    private const string CanvasWidthPlaceholder = "var CANVAS_WIDTH = 1080;";
    private const string CanvasHeightPlaceholder = "var CANVAS_HEIGHT = 2160;";

    private static readonly string ScriptTemplate = EmbeddedResourceHelper.LoadText(ScriptResourceName);
    private static readonly string PixiScript = EmbeddedResourceHelper.LoadText(PixiScriptResourceName);
    private static readonly string PixiSpineScript = EmbeddedResourceHelper.LoadText(PixiSpineScriptResourceName);

    public static string BuildLayoutEngineScript(CanvasPreviewSettings settings)
    {
        string runtimeScript = ScriptTemplate
            .Replace(CanvasWidthPlaceholder, $"var CANVAS_WIDTH = {settings.DefaultWidth};")
            .Replace(CanvasHeightPlaceholder, $"var CANVAS_HEIGHT = {settings.DefaultHeight};");

        return string.Concat(
            PixiScript,
            "\n",
            PixiSpineScript,
            "\n",
            runtimeScript);
    }
}
