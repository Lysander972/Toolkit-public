using System.Text;
using Toolkit.Core.Infra;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests;

/// <summary>
/// 测试用共享浏览器实例。
/// 整个测试进程复用同一个 BrowserRenderService，避免每个测试重复创建 worker。
/// 进程退出时自动释放。
/// </summary>
internal static class SharedBrowser
{
    private const int TestRenderWorkerCount = 8;
    internal static readonly BrowserRenderService Instance;

    static SharedBrowser()
    {
        UTF8Encoding utf8 = new(encoderShouldEmitUTF8Identifier: false);
        Console.InputEncoding = utf8;
        Console.OutputEncoding = utf8;
        Log.Initialize(LogLevel.Warning);

        bool keepBrowserProcesses = string.Equals(Environment.GetEnvironmentVariable("TOOLKIT_KEEP_BROWSER_PROCESSES"), "1", StringComparison.Ordinal);
        int[] existingBrowserProcessIds = string.Concat(Environment.GetEnvironmentVariable("TOOLKIT_BROWSER_PROCESS_IDS"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToArray();

        Instance = new BrowserRenderService(
            TestRenderWorkerCount,
            existingBrowserProcessIds,
            keepBrowserProcesses: keepBrowserProcesses);
        Instance.Initialize();
        if (keepBrowserProcesses)
        {
            Console.WriteLine($"[TOOLKIT_BROWSER_PROCESS_IDS]{string.Join(",", Instance.BrowserProcessIds)}");
        }
        if (!keepBrowserProcesses)
        {
            AppDomain.CurrentDomain.ProcessExit += static (_, _) => Instance.Dispose();
        }
    }
}
