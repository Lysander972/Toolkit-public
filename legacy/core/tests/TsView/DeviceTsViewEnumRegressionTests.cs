using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.TsView;

[Collection("WorkspaceState")]
public sealed class DeviceTsViewEnumRegressionTests
{
    [Fact]
    public void Read_ColorGame_Map54_CreateAIMechEnum_ShouldResolveCreateMechPositionAlignment()
    {
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "颜色对对碰");

        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);

        (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("map.map_/54");

        Assert.Empty(readErrors);
        Assert.NotNull(readData);
        Assert.Contains("BeEnum.fromBeConst<CreateMechPositionAlignment>(CreateMechPositionAlignment.Core)", readData.TsView, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_MethodSummary_Map1_SetPixelPosEnum_ShouldResolveUiPixelAlignment()
    {
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "方法汇总");

        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);

        (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("map.map_/1");

        Assert.Empty(readErrors);
        Assert.NotNull(readData);
        Assert.Contains("setPixelPos(BeVector3.fromBeConst(\"0,0,0\"), BeEnum.fromBeConst<UiPixelAlignment>(UiPixelAlignment.center))", readData.TsView, StringComparison.Ordinal);
    }
}
