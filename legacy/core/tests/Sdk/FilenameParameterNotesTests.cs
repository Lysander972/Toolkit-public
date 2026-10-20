using Xunit;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Tests.Sdk;

public sealed class FilenameParameterNotesTests
{
    [Theory]
    [InlineData(MethodCategory.DeviceAction, "Device.setDynamicMatPic")]
    [InlineData(MethodCategory.DeviceAction, "SpriteDevice.setTexture")]
    [InlineData(MethodCategory.DeviceAction, "ObjDevice.setDynamicMatPic2")]
    [InlineData(MethodCategory.DeviceAction, "FlareDevice.setTexture")]
    [InlineData(MethodCategory.DeviceAction, "DecalDevice.setTexture")]
    [InlineData(MethodCategory.DeviceAction, "GasPlanetDevice.setMainTex")]
    [InlineData(MethodCategory.DeviceAction, "PlanetDevice.setMainTex")]
    [InlineData(MethodCategory.DeviceAction, "SunDevice.setMainTex")]
    [InlineData(MethodCategory.VarFunc, "Device.setDynamicMatPic")]
    [InlineData(MethodCategory.VarFunc, "UIRect.loadPic")]
    [InlineData(MethodCategory.VarFunc, "UIRect.loadPicAsync")]
    [InlineData(MethodCategory.VarFunc, "UIRawImage.loadPic")]
    [InlineData(MethodCategory.VarFunc, "UIRawImage.loadPicAsync")]
    [InlineData(MethodCategory.VarFunc, "UIRawImage.LoadUserPic")]
    [InlineData(MethodCategory.EditorFunc, "Device.setPic")]
    public void Filename_Params_Should_Document_FileName_Not_Guid_Or_Path(MethodCategory category, string path)
    {
        MethodSignature sig = BuiltinMethodRegistry.Instance.GetByPath(path, category)
            ?? throw new InvalidOperationException($"未找到 SDK 方法: {path}");

        Assert.Contains("扩展名", sig.Notes, StringComparison.Ordinal);
        Assert.Contains("GUID", sig.Notes, StringComparison.Ordinal);
        Assert.Contains("路径", sig.Notes, StringComparison.Ordinal);
    }
}
