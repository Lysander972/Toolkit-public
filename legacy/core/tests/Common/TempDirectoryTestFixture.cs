namespace Toolkit.Core.Tests.Common;

/// <summary>
/// 测试基类，提供临时目录管理
/// </summary>
public abstract class TempDirectoryTestFixture : IDisposable
{
    protected readonly string TempDir;
    private bool _disposed;

    protected TempDirectoryTestFixture(string prefix)
    {
        TempDir = TestTempDirectoryManager.CreateDirectory(prefix);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TestWorkspace.DisposeProject(TempDir);
        TestTempDirectoryManager.TryDeleteDirectory(TempDir);
    }
}
