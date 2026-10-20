using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Entity;

public sealed class DeviceEntityScriptGroupTests
{
    [Fact]
    public void Scripts_Exclude_EventScripts_And_Group_By_EventName()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1);

        ScriptGroupEntity userGroup = TestEntityFactory.CreateScriptGroup(device);
        TestEntityFactory.CreateInlineScript(userGroup, "action");

        ScriptGroupEntity eventGroup = TestEntityFactory.CreateScriptGroup(device, "onStart");
        TestEntityFactory.CreateInlineScript(eventGroup, "handler");

        List<InlineScriptEntityBase> userScripts = device.Scripts.ToList();
        Assert.Single(userScripts);
        Assert.Equal("action", userScripts[0].ScriptName);

        Assert.True(device.EventScripts.ContainsKey("onStart"));
        List<InlineScriptEntityBase> eventScripts = device.EventScripts["onStart"].ToList();
        Assert.Single(eventScripts);
        Assert.Equal("handler", eventScripts[0].ScriptName);
        Assert.Equal("onStart", eventScripts[0].EventName);
    }
}
