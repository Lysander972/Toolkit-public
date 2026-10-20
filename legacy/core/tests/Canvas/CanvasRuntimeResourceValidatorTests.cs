using Toolkit.Core.Canvas.Render;
using Xunit;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Canvas;

public sealed class CanvasRuntimeResourceValidatorTests
{
    [Fact]
    public void Validate_Should_Accept_Packaged_Canvas_Runtime_Resources()
    {
        CanvasRuntimeResourceValidator.ResetForTesting();

        CanvasRuntimeResourceValidator.Validate();
    }

    [Fact]
    public void Packaged_Fonts_Should_Be_Git_Protected_Runtime_Inputs()
    {
        Assert.True(EmbeddedResourceHelper.Exists(CanvasFontAssets.MediumResourceName));
        Assert.True(EmbeddedResourceHelper.Exists(CanvasFontAssets.LightResourceName));

        byte[] medium = EmbeddedResourceHelper.LoadBytes(CanvasFontAssets.MediumResourceName);
        byte[] light = EmbeddedResourceHelper.LoadBytes(CanvasFontAssets.LightResourceName);

        Assert.False(HasUtf8ReplacementMarkerInHeader(medium));
        Assert.False(HasUtf8ReplacementMarkerInHeader(light));
        Assert.Equal("GPOS", GetFirstFontTableTag(medium));
        Assert.Equal("GPOS", GetFirstFontTableTag(light));
    }

    [Fact]
    public void Packaged_Render_Scripts_Should_Contain_Expected_Entrypoints()
    {
        Assert.Contains("window.CanvasLayoutEngine", EmbeddedResourceHelper.LoadText(CanvasPreviewScript.ScriptResourceName), StringComparison.Ordinal);
        Assert.Contains("PIXI", EmbeddedResourceHelper.LoadText(CanvasPreviewScript.PixiScriptResourceName), StringComparison.Ordinal);
        Assert.Contains("pixi-spine", EmbeddedResourceHelper.LoadText(CanvasPreviewScript.PixiSpineScriptResourceName), StringComparison.Ordinal);
    }

    private static string GetFirstFontTableTag(byte[] bytes)
    {
        return string.Concat((char)bytes[12], (char)bytes[13], (char)bytes[14], (char)bytes[15]);
    }

    private static bool HasUtf8ReplacementMarkerInHeader(byte[] bytes)
    {
        ReadOnlySpan<byte> header = bytes.AsSpan(0, 32);
        for (int i = 0; i <= header.Length - 3; i++)
        {
            if (header[i] == 0xEF && header[i + 1] == 0xBF && header[i + 2] == 0xBD)
                return true;
        }

        return false;
    }
}
