namespace Toolkit.Core.Infra;

/// <summary>
/// stderr 异步写入器 —— 宿主进程所有 stderr 输出的唯一落点。
///
/// 背景（线上事故）：下游（插件）读取停滞时，同步 Console.Error 写会阻塞在管道 Flush 上。
/// 曾发生持模型写锁的刷新线程堵在 [PROJECT_TREE] 推送的 Flush 里，
/// 30+ 刷新线程排队写锁、14 个 Parallel 读方饿死、全进程级联死锁——
/// 且因为日志与推送共用同一条被堵住的通道，连"我卡死了"都写不出去。
///
/// 设计：有界队列 + 单一后台写线程。队列满丢弃最旧行并计数，
/// 随后补发一条丢弃通告——"丢输出"本身永远可观测，绝不静默。
/// </summary>
public static class AsyncConsoleErrorWriter
{
    // 队列上界：状态类推送（PROJECT_TREE 等）本就该被最新值覆盖，丢弃最旧行语义正确。
    private const int MaxQueuedLines = 256;

    private static readonly object Sync = new();
    private static readonly Queue<string> Pending = new();
    private static Thread? _writerThread;
    private static int _droppedCount;

    /// <summary>
    /// 投递一行到 stderr。永不阻塞调用线程。
    /// </summary>
    public static void WriteLine(string line)
    {
        lock (Sync)
        {
            if (_writerThread == null)
            {
                _writerThread = new Thread(WriterLoop)
                {
                    IsBackground = true,
                    Name = "AsyncConsoleErrorWriter"
                };
                _writerThread.Start();
            }

            while (Pending.Count >= MaxQueuedLines)
            {
                Pending.Dequeue();
                _droppedCount++;
            }
            if (_droppedCount > 0)
            {
                Pending.Enqueue($"[toolkit] [core] [ERR] stderr 输出通道溢出，已丢弃 {_droppedCount} 行（下游读取停滞，排查插件侧事件循环阻塞）");
                _droppedCount = 0;
            }
            Pending.Enqueue(line);
            Monitor.Pulse(Sync);
        }
    }

    private static void WriterLoop()
    {
        while (true)
        {
            string line;
            lock (Sync)
            {
                while (Pending.Count == 0)
                {
                    Monitor.Wait(Sync);
                }
                line = Pending.Dequeue();
            }

            try
            {
                // 写线程允许在管道满时阻塞——这正是隔离的意义：阻塞被收敛到这唯一的线程。
                Console.Error.WriteLine(line);
            }
            catch (Exception)
            {
                // 控制台已断开（父进程死亡/管道关闭）：丢弃本行继续排空，
                // 绝不能让写线程死亡导致后续输出永久积压。
            }
        }
    }
}
