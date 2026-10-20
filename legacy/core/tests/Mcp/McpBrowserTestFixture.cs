using Toolkit.Mcp;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Mcp;

/// <summary>
/// 需要真实视图工作区和跨测试复用浏览器的 MCP 测试 Fixture。
/// </summary>
public sealed class McpBrowserTestFixture : IDisposable
{
    public Exception? InitializationError { get; }
    public static readonly string TestProjectRoot = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");

    private readonly ToolkitServices? _services;
    private readonly McpViewWorkspace? _workspace;
    private readonly McpToolRegistry? _registry;

    public ToolkitServices Services => _services ?? throw new InvalidOperationException("MCP 浏览器测试 Fixture 不可用");
    public McpViewWorkspace Workspace => _workspace ?? throw new InvalidOperationException("MCP 浏览器测试 Fixture 不可用");
    public McpToolRegistry Registry => _registry ?? throw new InvalidOperationException("MCP 浏览器测试 Fixture 不可用");
    public bool IsAvailable { get; }

    public McpBrowserTestFixture()
    {
        if (!SystemIOWrapper.DirectoryExists(TestProjectRoot))
        {
            IsAvailable = false;
            return;
        }

        try
        {
            IsAvailable = true;
            ToolkitServices.ShutdownSingleton();
            _services = TestWorkspace.InitializeSingletonWithRecordingPersistence(TestProjectRoot, SharedBrowser.Instance);
            _workspace = new McpViewWorkspace(_services);
            _registry = new McpToolRegistry(_services, TestProjectRoot, _workspace, registerHandlers: true);
        }
        catch (Exception ex)
        {
            ToolkitServices.ShutdownSingleton();
            IsAvailable = false;
            InitializationError = ex;
        }
    }

    public void Dispose()
    {
        if (!IsAvailable)
        {
            ToolkitServices.ShutdownSingleton();
            return;
        }

        _registry?.Dispose();
        _workspace?.Dispose();
        ToolkitServices.ShutdownSingleton();
    }
}

[Xunit.CollectionDefinition("McpBrowserTests", DisableParallelization = true)]
public sealed class McpBrowserTestCollection : Xunit.ICollectionFixture<McpBrowserTestFixture>
{
}
