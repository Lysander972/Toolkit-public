using System.Diagnostics;
using Xunit;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Common;

/// <summary>
/// ProcessRunner 契约测试：跨进程执行必须「超时 + 资源清理 + 进程树 Kill」，
/// 任何子进程作妖（慢、崩溃、残留持管道、取消）都不得永久阻塞调用线程。
/// </summary>
public sealed class ProcessRunnerTests
{
    private static bool IsWindows => OperatingSystem.IsWindows();

    private static ProcessStartInfo Cmd(string arguments)
    {
        ProcessStartInfo psi = new()
        {
            FileName = IsWindows ? "cmd.exe" : "/bin/sh",
            Arguments = (IsWindows ? "/c " : "-c ") + arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        return psi;
    }

    [Fact]
    public void Run_NormalProcess_ReturnsCompletedAndStdout()
    {
        if (!IsWindows)
        {
            return;
        }

        ProcessRunResult result = ProcessRunner.Run(Cmd("echo hi"), timeoutMs: 5000, CancellationToken.None);

        Assert.Equal(ProcessRunStatus.Completed, result.Status);
        Assert.Contains("hi", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_CrashingProcess_ReturnsNonZeroExitCode()
    {
        if (!IsWindows)
        {
            return;
        }

        ProcessRunResult result = ProcessRunner.Run(Cmd("exit 3"), timeoutMs: 5000, CancellationToken.None);

        Assert.Equal(ProcessRunStatus.Completed, result.Status);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Run_SlowProcess_TimesOutWithoutHanging()
    {
        if (!IsWindows)
        {
            return;
        }

        // ping 持续写 stdout 且不退出 → ReadToEndAsync 永不到 EOF（复现 tsc node 残留持管道的死锁路径）。
        // 实现若用无超时 GetResult() 会永久阻塞；正确实现必须在超时后 Kill 进程树并立即返回。
        Stopwatch sw = Stopwatch.StartNew();
        ProcessRunResult result = ProcessRunner.Run(Cmd("ping -n 60 127.0.0.1"), timeoutMs: 1000, CancellationToken.None);
        sw.Stop();

        Assert.Equal(ProcessRunStatus.TimedOut, result.Status);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"超时后应立即返回，实际 {sw.ElapsedMilliseconds}ms（疑似在 stdout 读取上永久阻塞）");
    }

    [Fact]
    public void Run_MissingExecutable_ReturnsFailedToStart()
    {
        ProcessStartInfo psi = new("definitely_nonexistent_exe_xyz_8f3a.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        ProcessRunResult result = ProcessRunner.Run(psi, timeoutMs: 5000, CancellationToken.None);

        Assert.Equal(ProcessRunStatus.FailedToStart, result.Status);
    }

    [Fact]
    public void Run_CanceledToken_ReturnsCanceledWithoutHanging()
    {
        if (!IsWindows)
        {
            return;
        }

        using CancellationTokenSource cts = new();
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            cts.Cancel();
        });

        Stopwatch sw = Stopwatch.StartNew();
        ProcessRunResult result = ProcessRunner.Run(Cmd("ping -n 60 127.0.0.1"), timeoutMs: 60000, cts.Token);
        sw.Stop();

        Assert.Equal(ProcessRunStatus.Canceled, result.Status);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"取消后应立即返回，实际 {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// 复现 GetTscDiagnostics 原始卡死模式（裸 Process + 无超时 GetResult，对应 McpToolRegistry.cs:1842）：
    /// 主进程正常退出，但孙进程装死、持有重定向的 stdout 管道不关 → ReadToEndAsync 永不到 EOF →
    /// GetResult() 永久阻塞，主进程跟着卡死。用「cmd 秒退 + start /b ping 持管道」精确构造该场景。
    /// </summary>
    [Fact]
    public async Task LegacyGetResult_Hangs_WhenGrandchildHoldsStdoutPipe()
    {
        if (!IsWindows)
        {
            return;
        }

        ProcessStartInfo psi = new()
        {
            FileName = "cmd.exe",
            // cmd 立即退出；后台 ping 继承 cmd 的 stdout 管道、原地装死 60 秒不退出、不报错。
            Arguments = "/c start /b ping -n 60 127.0.0.1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("启动 cmd 失败");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();

        // 主进程（cmd）秒退 —— 对应 tsc.cmd 正常退出，WaitForExit 返回 true。
        bool mainExited = process.WaitForExit(2000);
        Assert.True(mainExited, "cmd 应立即退出，把 stdout 管道写端留给后台 ping");

        // 原始 bug 现场（GetTscDiagnostics:1842）：主进程已退出，但 stdoutTask 因 ping 持管道永不完成，
        // 原始模式的 stdoutTask.GetAwaiter().GetResult() 会永久等它 → 主进程卡死。
        // 这里直接探测 stdoutTask 是否在 5 秒内完成：不完成即证明 GetResult 会永久阻塞。
        Task winner = await Task.WhenAny(stdoutTask, Task.Delay(5000));
        bool mainProcessHung = winner != stdoutTask;

        Assert.True(mainProcessHung, "BUG 复现失败：孙进程装死持管道时 stdoutTask 永不完成，无超时 GetResult 会永久阻塞——主进程应跟着卡死。若此处断言失败，说明测试构造未能复现管道残留场景，需调整。");
    }

    /// <summary>
    /// 与 LegacyGetResult_Hangs 完全相同的输入（cmd 秒退 + 后台 ping 装死持管道），
    /// 但走 ProcessRunner：drain deadline 并行兜底，主进程（调用线程）不卡死。
    /// </summary>
    [Fact]
    public void Run_GrandchildHoldsPipe_ReturnsWithoutHanging()
    {
        if (!IsWindows)
        {
            return;
        }

        Stopwatch sw = Stopwatch.StartNew();
        ProcessRunResult result = ProcessRunner.Run(Cmd("start /b ping -n 60 127.0.0.1"), timeoutMs: 10000, CancellationToken.None);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 8000,
            $"孙进程持管道时应由 drain deadline 并行兜底返回，实际 {sw.ElapsedMilliseconds}ms（疑似卡死）");
        Assert.True(result.Status == ProcessRunStatus.Completed || result.Status == ProcessRunStatus.TimedOut,
            $"不应卡死，实际 Status={result.Status}");
    }
}
