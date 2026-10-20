using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

public sealed class ProjectEntityLayoutTests
{
    [Fact]
    public void ParseListJsonPath_Recognizes_Scripts_List()
    {
        (string DeviceRelativePath, string? EventName)? result = ScriptPathHelper.ParseListJsonPath("main.map_/1/scripts/list.json");

        Assert.NotNull(result);
        Assert.Equal("main.map_/1", result.Value.DeviceRelativePath);
        Assert.Null(result.Value.EventName);
    }

    [Fact]
    public void ParseListJsonPath_Recognizes_Event_List()
    {
        (string DeviceRelativePath, string? EventName)? result = ScriptPathHelper.ParseListJsonPath("main.map_/1/event_onStart/list.json");

        Assert.NotNull(result);
        Assert.Equal("main.map_/1", result.Value.DeviceRelativePath);
        Assert.Equal("onStart", result.Value.EventName);
    }
}
