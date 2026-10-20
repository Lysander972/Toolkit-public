using Xunit;
using Toolkit.Core.Infra;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Common;

public sealed class BrowserProcessManagerTests : TempDirectoryTestFixture
{
    public BrowserProcessManagerTests() : base("Toolkit.Core.Tests.BrowserProcessManager")
    {
    }

    [Fact]
    public void RenderHtmlToPng_Before_Initialize_Should_Report_NotInitialized()
    {
        using BrowserRenderService service = new(1, Array.Empty<int>());

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.RenderHtmlToPng("<html></html>", 1, 1));

        Assert.Equal("浏览器渲染服务尚未初始化", ex.Message);
    }

    [Fact]
    public void AcquireSession_When_BrowserProcess_Exits_Should_Report_ExitCode_And_Stderr()
    {
        string fakeChromeFullPath = CreateFakeChromeFullPath();

        using BrowserProcessManager manager = new(0, browserExecutableOverride: fakeChromeFullPath);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => manager.AcquireSession());

        Assert.Contains("浏览器进程已退出", ex.Message);
        Assert.Contains("退出码", ex.Message);
        Assert.True(
            ex.Message.Contains("stderr", StringComparison.Ordinal)
            || ex.Message.Contains("stdout", StringComparison.Ordinal));
    }

    [Fact]
    public void Initialize_When_BrowserProcess_Exits_Should_Preserve_ExitDetail()
    {
        string fakeChromeFullPath = CreateFakeChromeFullPath();

        using BrowserRenderService service = new(1, Array.Empty<int>(), browserExecutableFullPath: fakeChromeFullPath);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.Initialize());

        Assert.Contains("浏览器进程已退出", ex.Message);
        Assert.Contains("退出码", ex.Message);
        Assert.True(
            ex.Message.Contains("stderr", StringComparison.Ordinal)
            || ex.Message.Contains("stdout", StringComparison.Ordinal));
    }

    private string CreateFakeChromeFullPath()
    {
        string fakeChromeFullPath = SystemIOWrapper.Combine(TempDir, "chrome.exe");
        string powershellFullPath = SystemIOWrapper.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        Assert.True(SystemIOWrapper.FileExists(powershellFullPath));
        SystemIOWrapper.CopyFile(powershellFullPath, fakeChromeFullPath, overwrite: true);
        return fakeChromeFullPath;
    }
}
