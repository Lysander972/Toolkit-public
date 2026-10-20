using System.Diagnostics;
using System.Text;
using Toolkit.Core.Infra;
using Xunit;

namespace Toolkit.Core.Tests.Common;

// =============================================================================
// AsyncConsoleErrorWriter 接缝回归套件
//
// 背景（线上事故）：下游读取停滞时，同步 Console.Error 写阻塞在管道 Flush 上，
// 持模型写锁的刷新线程被焊死 → 全进程级联死锁，且日志/推送共用同一通道导致
// 连"我卡死了"都写不出去。本套件钉死两条契约：
// 1. WriteLine 在下游完全堵死时绝不阻塞调用线程；
// 2. 队列溢出丢弃必须留下可观测的丢弃通告，绝不静默。
// =============================================================================

public sealed class AsyncConsoleErrorWriterTests
{
    /// <summary>
    /// 可手动闸门的 TextWriter：闸门关闭时 WriteLine 永久阻塞，模拟下游管道满。
    /// </summary>
    private sealed class GatedTextWriter : TextWriter
    {
        private readonly object _sync = new();
        private bool _gateOpen;
        private readonly StringBuilder _captured = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Captured
        {
            get { lock (_sync) { return _captured.ToString(); } }
        }

        public override void WriteLine(string? value)
        {
            lock (_sync)
            {
                while (!_gateOpen)
                {
                    Monitor.Wait(_sync);
                }
                _captured.AppendLine(value);
            }
        }

        public void OpenGate()
        {
            lock (_sync)
            {
                _gateOpen = true;
                Monitor.PulseAll(_sync);
            }
        }
    }

    [Fact]
    public void WriteLine_NeverBlocksCaller_WhenDownstreamJammed()
    {
        TextWriter original = Console.Error;
        GatedTextWriter gated = new();
        Console.SetError(gated);
        try
        {
            // 先投递一行让写线程启动并堵在闸门上。
            AsyncConsoleErrorWriter.WriteLine("[test] 首行（用于占用写线程）");
            SpinWait.SpinUntil(() => false, TimeSpan.FromMilliseconds(300));

            // 下游堵死状态下投递超过队列上界（256）的行：调用线程必须立即返回。
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < 300; i++)
            {
                AsyncConsoleErrorWriter.WriteLine($"[test] 溢出填充行 {i}");
            }
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 1000,
                $"下游堵死时 WriteLine 阻塞了调用线程（{sw.ElapsedMilliseconds}ms），异步隔离失效");

            // 放行下游：队列排空后必须出现丢弃通告（溢出必然发生：300 > 256）。
            gated.OpenGate();
            bool noticeSeen = SpinWait.SpinUntil(
                () => gated.Captured.Contains("已丢弃", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            Assert.True(noticeSeen, "队列溢出后未观察到丢弃通告——丢输出被静默，违背可观测性契约");
        }
        finally
        {
            gated.OpenGate();
            Console.SetError(original);
        }
    }
}
