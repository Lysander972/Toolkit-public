using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Toolkit.Core.TestsShim;

/// <summary>
/// xunit 测试 shim:以进程内 runner(xunit.runner.utility)执行测试程序集。
/// 用于 Bazel csharp_test——rules_dotnet 无 xunit 规则,以本 shim 替代 vstest。
/// （旧版的 Bazel 现已不再使用，已改回纯 pnpm）
/// 过滤由环境变量指定(短类名子串匹配,与 RunTests.Main 的类名分组语义一致):
///   TOOLKIT_TEST_INCLUDE 逗号分隔包含类名(空=全部)
///   TOOLKIT_TEST_EXCLUDE 逗号分隔排除类名
/// 测试程序集路径:TEST_SRCDIR/_main/src/core/tests/Toolkit.Core.Tests/net10.0/Toolkit.Core.Tests.dll
/// </summary>
public static class Program
{
    private static readonly string[] IncludeClasses = SplitEnv("TOOLKIT_TEST_INCLUDE");
    private static readonly string[] ExcludeClasses = SplitEnv("TOOLKIT_TEST_EXCLUDE");

    public static int Main()
    {
        string testDll = ResolveTestAssembly();
        if (testDll == null)
        {
            Console.Error.WriteLine("无法定位测试程序集(检查 TEST_SRCDIR 环境与 runfiles 布局)");
            return 2;
        }

        using XunitFrontController controller = new(AppDomainSupport.Denied, testDll, shadowCopy: false);
        using TestDiscoveryVisitor discovery = new();
        controller.Find(includeSourceInformation: false, messageSink: discovery, discoveryOptions: TestFrameworkOptions.ForDiscovery());
        discovery.Finished.WaitOne();

        List<ITestCase> filtered = discovery.TestCases
            .Where(tc => IsIncluded(tc.TestMethod.TestClass.Class.Name, tc.TestMethod.Method.Name))
            .ToList();

        Console.WriteLine($"发现测试: {discovery.TestCases.Count} 个,过滤后执行: {filtered.Count} 个");

        using ExecutionSink execution = new();
        ITestFrameworkExecutionOptions options = TestFrameworkOptions.ForExecution();
        options.SetDiagnosticMessages(false);
        options.SetDisableParallelization(true);
        options.SetMaxParallelThreads(1);
        controller.RunTests(filtered, execution, options);
        execution.Finished.WaitOne();

        Console.WriteLine();
        Console.WriteLine($"通过: {execution.Passed}, 失败: {execution.Failed}, 跳过: {execution.Skipped}");
        return execution.Failed > 0 ? 1 : 0;
    }

    private static string[] SplitEnv(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
        {
            return Array.Empty<string>();
        }
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsIncluded(string className, string methodName)
    {
        if (ExcludeClasses.Any(c => className.Contains(c, StringComparison.Ordinal) || methodName.Contains(c, StringComparison.Ordinal)))
        {
            return false;
        }
        return IncludeClasses.Length == 0
            || IncludeClasses.Any(c => className.Contains(c, StringComparison.Ordinal) || methodName.Contains(c, StringComparison.Ordinal));
    }

    private static string? ResolveTestAssembly()
    {
        string? sourceRoot = Environment.GetEnvironmentVariable("TEST_SRCDIR");
        if (string.IsNullOrEmpty(sourceRoot))
        {
            return null;
        }
        string candidate = Path.Combine(
            sourceRoot,
            "_main",
            "src",
            "core",
            "tests",
            "Toolkit.Core.Tests",
            "net10.0",
            "Toolkit.Core.Tests.dll");
        return File.Exists(candidate) ? candidate : null;
    }
}

internal sealed class ExecutionSink : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink, IDisposable
{
    internal readonly AutoResetEvent Finished = new(false);
    internal int Passed;
    internal int Failed;
    internal int Skipped;

    public bool OnMessage(IMessageSinkMessage message)
    {
        switch (message)
        {
            case ITestPassed:
                Interlocked.Increment(ref Passed);
                break;
            case ITestFailed failure:
                Interlocked.Increment(ref Failed);
                string detail = string.Join(Environment.NewLine, failure.Messages);
                Console.WriteLine($"[FAIL] {failure.Test.DisplayName}");
                Console.WriteLine(detail);
                break;
            case ITestSkipped:
                Interlocked.Increment(ref Skipped);
                break;
            case ITestAssemblyFinished:
                Finished.Set();
                break;
        }
        return true;
    }

    public void Dispose()
    {
        Finished.Dispose();
    }
}
