using Xunit;

namespace Toolkit.Core.Tests.Mcp;

[Collection("McpBrowserTests")]
public sealed class McpBrowserWarmupTests
{
    [Fact]
    public void SharedBrowser_Should_Expose_ProcessIds()
    {
        Assert.NotEmpty(SharedBrowser.Instance.BrowserProcessIds);
    }
}
