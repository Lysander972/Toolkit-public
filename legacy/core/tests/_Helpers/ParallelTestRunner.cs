using System.Collections.Concurrent;
using Xunit;

namespace Toolkit.Core.Tests;

/// <summary>
/// 统一的并行测试执行，消除重复的 firstError + Parallel.ForEach 模式
/// </summary>
internal static class ParallelTestRunner
{
    /// <summary>
    /// 并行执行测试，遇到第一个错误即停止后续测试
    /// </summary>
    public static void RunParallel<T>(
        IEnumerable<T> items,
        Func<T, string?> testFunc,
        Action<int, int>? progressCallback = null,
        string contextName = "Test")
    {
        T[] itemsArray = items.ToArray();
        ConcurrentBag<string> firstError = [];
        int processed = 0;

        Parallel.ForEach(
            itemsArray,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            item =>
            {
                if (!firstError.IsEmpty) return;

                int current = System.Threading.Interlocked.Increment(ref processed);
                progressCallback?.Invoke(current, itemsArray.Length);

                string? error = testFunc(item);
                if (error != null)
                {
                    firstError.Add(error);
                }
            });

        if (!firstError.IsEmpty)
        {
            Assert.Fail($"[{contextName}] 并行测试失败:\n" + string.Join("\n", firstError));
        }
    }

    /// <summary>
    /// 包装测试函数，捕获异常并返回错误消息
    /// </summary>
    public static Func<T, string?> SafeTest<T>(Action<T> testAction)
    {
        return item =>
        {
            try
            {
                testAction(item);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        };
    }
}
