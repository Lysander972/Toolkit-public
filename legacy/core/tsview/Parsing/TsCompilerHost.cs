using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.ClearScript.V8;
using Toolkit.Core.Infra;

namespace Toolkit.Core.TsView;

/// <summary>
/// ClearScript V8 封装：进程内多引擎多线程，并发请求并行执行，同步阻塞返回。
/// 每个引擎绑定一条专属线程，一引擎只被其线程串行访问；无子进程、无 IPC。
/// </summary>
internal static class TsCompilerHost
{
    private sealed record TsViewTask(Func<V8ScriptEngine, object> Work, TaskCompletionSource<object> Completion);

    private static readonly int EngineCount = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
    private static readonly object InitLock = new();
    private static V8ScriptEngine[]? _engines;
    private static Thread[]? _threads;
    private static Channel<TsViewTask>[]? _queues;
    private static int _roundRobin;

    // 纯函数结果缓存（高频小调用，消除引擎往返；软上限，超限停止写入）
    private const int PureFunctionCacheLimit = 65536;
    private static readonly ConcurrentDictionary<string, bool> IdentifierCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> BeEnumTypeCache = new(StringComparer.Ordinal);

    // 测试覆盖：设置后 Initialize 用此 traversal 脚本（生产源码 + 全量参考 superset）替代生产 bundle。
    // 生产 bundle 不含全量参考实现；测试经此注入 superset 以运行字节级等价性门禁。须在首次调用前设置。
    internal static string? OverrideTraversalScript { get; set; }

    private static void Initialize()
    {
        lock (InitLock)
        {
            if (_engines is not null) return;

            string tsJs = EmbeddedResourceHelper.LoadText("Toolkit.Core.tsview.v8.typescript.js.gz");
            string traversalJs = OverrideTraversalScript
                ?? EmbeddedResourceHelper.LoadText(
                    "Toolkit.Core.tsview.v8.tsview-traversal.DO_NOT_READ_OR_EDIT.generated.js.gz");

            _engines = new V8ScriptEngine[EngineCount];
            _threads = new Thread[EngineCount];
            _queues = new Channel<TsViewTask>[EngineCount];

            for (int i = 0; i < EngineCount; i++)
            {
                V8ScriptEngine engine = new(V8ScriptEngineFlags.DisableGlobalMembers);
                engine.Execute("typescript.js", tsJs);
                engine.Execute("tsview-traversal.js", traversalJs);
                _engines[i] = engine;

                _queues[i] = Channel.CreateBounded<TsViewTask>(new BoundedChannelOptions(EngineCount * 16)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                });

                int engineIndex = i;
                Thread thread = new(() => EngineLoop(engineIndex))
                {
                    IsBackground = true,
                    Name = $"TsView-Engine-{engineIndex}",
                };
                _threads[i] = thread;
                thread.Start();
            }
        }
    }

    private static void EngineLoop(int engineIndex)
    {
        V8ScriptEngine engine = _engines![engineIndex];
        ChannelReader<TsViewTask> reader = _queues![engineIndex].Reader;

        while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            while (reader.TryRead(out TsViewTask? task))
            {
                try
                {
                    task.Completion.SetResult(task.Work(engine));
                }
                catch (Exception ex)
                {
                    task.Completion.SetException(ex);
                }
            }
        }
    }

    private static T Submit<T>(Func<V8ScriptEngine, T> work)
    {
        Initialize();
        int target = unchecked((int)(uint)Interlocked.Increment(ref _roundRobin)) % EngineCount;
        TaskCompletionSource<object> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queues![target].Writer.TryWrite(new TsViewTask(e => work(e)!, tcs)))
            throw new InvalidOperationException("TsCompilerHost 引擎队列已关闭。");
        return (T)tcs.Task.GetAwaiter().GetResult();
    }

    internal static string ScanClassMembers(string sourceText, string className, string? entryClassName)
    {
        return Submit(engine =>
        {
            engine.Script.___sourceText = sourceText;
            engine.Script.___className = className;
            engine.Script.___entryClassName = entryClassName;
            return (string)engine.Evaluate("__scanClassMembers(___sourceText, ___className, ___entryClassName)");
        });
    }

    internal static string ParseMethodBody(string bodyText, string contextJson)
    {
        return Submit(engine =>
        {
            engine.Script.___bodyText = bodyText;
            engine.Script.___contextJson = contextJson;
            return (string)engine.Evaluate("__parseMethodBody(___bodyText, ___contextJson)");
        });
    }

    internal static string ParseParameterList(string parameterListText)
    {
        return Submit(engine =>
        {
            engine.Script.___parameterListText = parameterListText;
            return (string)engine.Evaluate("__parseParameterList(___parameterListText)");
        });
    }

    internal static bool IsValidIdentifier(string identifier)
    {
        if (IdentifierCache.TryGetValue(identifier, out bool cached))
            return cached;
        bool result = Submit(engine =>
        {
            engine.Script.___identifier = identifier;
            return (bool)engine.Evaluate("__isValidIdentifier(___identifier)");
        });
        if (IdentifierCache.Count < PureFunctionCacheLimit)
            IdentifierCache[identifier] = result;
        return result;
    }

    internal static string ParseBeEnumTypeArgument(string typeText)
    {
        if (BeEnumTypeCache.TryGetValue(typeText, out string? cached))
            return cached;
        string result = Submit(engine =>
        {
            engine.Script.___typeText = typeText;
            return (string)engine.Evaluate("__parseBeEnumTypeArgument(___typeText)");
        });
        if (BeEnumTypeCache.Count < PureFunctionCacheLimit)
            BeEnumTypeCache[typeText] = result;
        return result;
    }

    internal static string FormatMethodBody(string bodyText)
    {
        return Submit(engine =>
        {
            engine.Script.___formatBodyText = bodyText;
            return (string)engine.Evaluate("__formatMethodBody(___formatBodyText)");
        });
    }

    internal static string BuildCodeRepoView(string inputJson)
    {
        return Submit(engine =>
        {
            engine.Script.___codeRepoInputJson = inputJson;
            return (string)engine.Evaluate("__buildCodeRepoView(___codeRepoInputJson)");
        });
    }

    internal static string BuildCustomLogicDts(string inputJson)
    {
        return Submit(engine =>
        {
            engine.Script.___customLogicDtsInputJson = inputJson;
            return (string)engine.Evaluate("__buildCustomLogicDts(___customLogicDtsInputJson)");
        });
    }

    internal static string BuildDeviceView(string inputJson)
    {
        return Submit(engine =>
        {
            engine.Script.___deviceViewInputJson = inputJson;
            return (string)engine.Evaluate("__buildDeviceView(___deviceViewInputJson)");
        });
    }

    /// <summary>
    /// 仅用于测试：全量 AST 参考路径（直接全文件官方 printer，不经增量序列化）。
    /// 仅在注入了 <see cref="OverrideTraversalScript"/>（测试 superset bundle）后可用，
    /// 用于与 <see cref="BuildDeviceView"/>（增量路径）做字节级等价性断言。
    /// </summary>
    internal static string BuildDeviceViewBaseline(string inputJson)
    {
        return Submit(engine =>
        {
            engine.Script.___deviceViewInputJson = inputJson;
            return (string)engine.Evaluate("__buildDeviceViewBaseline(___deviceViewInputJson)");
        });
    }

    /// <summary>
    /// 仅用于测试：重置引擎池与纯函数缓存。
    /// </summary>
    internal static void Reset()
    {
        lock (InitLock)
        {
            if (_queues is not null)
            {
                foreach (Channel<TsViewTask>? q in _queues)
                    q?.Writer.TryComplete();
            }

            if (_threads is not null)
            {
                foreach (Thread? t in _threads)
                    t?.Join();
            }

            if (_engines is not null)
            {
                foreach (V8ScriptEngine? e in _engines)
                    e?.Dispose();
            }

            _engines = null;
            _threads = null;
            _queues = null;
            IdentifierCache.Clear();
            BeEnumTypeCache.Clear();
        }

        Initialize();
    }
}
