namespace Toolkit.Core.Tests;

/// <summary>
/// 统一的测试进度报告器，消除重复的"每5秒打印进度"逻辑
/// </summary>
internal sealed class TestProgressReporter
{
    private readonly string _contextName;
    private readonly object _lock = new();
    private DateTimeOffset _lastLogAt = DateTimeOffset.UtcNow;

    public TestProgressReporter(string contextName)
    {
        _contextName = contextName;
    }

    /// <summary>
    /// 尝试报告进度（节流：最多每5秒报告一次）
    /// </summary>
    public void TryReport(int current, int total, string phase)
    {
        if ((DateTimeOffset.UtcNow - _lastLogAt).TotalSeconds < 5)
        {
            return;
        }

        lock (_lock)
        {
            if ((DateTimeOffset.UtcNow - _lastLogAt).TotalSeconds >= 5)
            {
                Console.WriteLine($"[{_contextName}] {phase}: {current}/{total}");
                _lastLogAt = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <summary>
    /// 强制报告进度（无节流）
    /// </summary>
    public void Report(string message)
    {
        Console.WriteLine($"[{_contextName}] {message}");
    }
}
