using System.Diagnostics.CodeAnalysis;
using Toolkit.Mcp;
using Toolkit.Core.Infra;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Mcp;

/// <summary>
/// MCP 测试共享 Fixture。
/// 整个测试集合只构建一次 services + registry，所有不触发视图工作区的测试复用。
/// </summary>
public sealed class McpTestFixture : IDisposable
{
    public static readonly string TestProjectRoot = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");

    private readonly BrowserRenderService? _browser;
    public ToolkitServices? Services { get; }
    public McpToolRegistry? Registry { get; }

    /// <summary>
    /// 测试工程是否存在。不存在时测试应跳过。
    /// </summary>
    [MemberNotNullWhen(true, nameof(Services), nameof(Registry))]
    public bool IsAvailable { get; }

    public McpTestFixture()
    {
        if (!SystemIOWrapper.DirectoryExists(TestProjectRoot))
        {
            IsAvailable = false;
            return;
        }

        IsAvailable = true;
        _browser = new BrowserRenderService(1, Array.Empty<int>());
        ToolkitServices.ShutdownSingleton();
        Services = TestWorkspace.InitializeSingletonWithRecordingPersistence(TestProjectRoot, _browser);
        Registry = new McpToolRegistry(Services, TestProjectRoot, null, registerHandlers: true);
    }

    public void Dispose()
    {
        ToolkitServices.ShutdownSingleton();
        _browser?.Dispose();
    }
}

[Xunit.CollectionDefinition("McpTests", DisableParallelization = true)]
public sealed class McpTestCollection : Xunit.ICollectionFixture<McpTestFixture>
{
}
