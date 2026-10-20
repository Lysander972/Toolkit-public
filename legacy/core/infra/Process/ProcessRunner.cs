using System.ComponentModel;
using System.Diagnostics;

namespace Toolkit.Core.Infra;

public enum ProcessRunStatus
{
    Completed,
    TimedOut,
    Canceled,
    FailedToStart
}

/// <param name="OutputDrained">stdout/stderr 是否均已在 deadline 内读到 EOF。
/// false 表示有残留进程持有管道导致输出被截断——调用方禁止把不完整的输出当作完整结果使用。</param>
public sealed record ProcessRunResult(
    ProcessRunStatus Status,
    int ExitCode,
    string Stdout,
    string Stderr,
    string? Error,
    bool OutputDrained = true);

/// <summary>
/// 跨进程执行的统一入口：超时 + 资源清理 + 进程树 Kill。
/// </summary>
/// <remarks>
/// 输出读取一律带 deadline，禁止无超时阻塞——子进程先于父退出但残留孙进程持有重定向管道时，
/// ReadToEndAsync 永不到 EOF，无超时 GetResult() 会永久焊死调用线程（曾导致 MCPWriteLock 死锁）。
/// </remarks>
public static class ProcessRunner
{
    private const int OutputDrainDeadlineMs = 5000;

    public static ProcessRunResult Run(ProcessStartInfo psi, int timeoutMs, CancellationToken cancellationToken)
    {
        return RunAsync(psi, timeoutMs, cancellationToken).GetAwaiter().GetResult();
    }

    private static async Task<ProcessRunResult> RunAsync(ProcessStartInfo psi, int timeoutMs, CancellationToken cancellationToken)
    {
        Process process = new() { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                return new ProcessRunResult(ProcessRunStatus.FailedToStart, -1, string.Empty, string.Empty, "Process.Start 返回 false", OutputDrained: false);
            }
        }
        catch (Win32Exception ex)
        {
            return new ProcessRunResult(ProcessRunStatus.FailedToStart, -1, string.Empty, string.Empty, ex.Message);
        }

        using (process)
        {
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            using CancellationTokenSource timeoutCts = new(timeoutMs);
            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillTree(process);
                return cancellationToken.IsCancellationRequested
                    ? new ProcessRunResult(ProcessRunStatus.Canceled, -1, string.Empty, string.Empty, null)
                    : new ProcessRunResult(ProcessRunStatus.TimedOut, -1, string.Empty, string.Empty, null);
            }

            (string Text, bool Completed)[] drained = await Task.WhenAll(
                DrainWithDeadlineAsync(stdoutTask),
                DrainWithDeadlineAsync(stderrTask)).ConfigureAwait(false);
            bool outputDrained = drained[0].Completed && drained[1].Completed;
            return new ProcessRunResult(ProcessRunStatus.Completed, process.ExitCode, drained[0].Text, drained[1].Text, null, outputDrained);
        }
    }

    private static async Task<(string Text, bool Completed)> DrainWithDeadlineAsync(Task<string> task)
    {
        Task winner = await Task.WhenAny(task, Task.Delay(OutputDrainDeadlineMs)).ConfigureAwait(false);
        return winner == task && task.IsCompletedSuccessfully ? (task.Result, true) : (string.Empty, false);
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 进程刚好自行退出，无需清理。
        }
        catch (Win32Exception)
        {
            // Kill 失败（权限/句柄），尽力而为：不应掩盖原始的 TimedOut/Canceled 结果。
        }
    }
}
