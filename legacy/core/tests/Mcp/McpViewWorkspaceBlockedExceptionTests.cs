using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Mcp;

/// <summary>
/// ExtractBlocked 是视图刷新 loop 把"被并行任务包装的受阻异常"从普通失败中分流出来的纯逻辑。
/// 它决定了同一份"外部进程锁文件"事实在串行/并行路径下得到一致处置（受阻=可恢复，不进失败计数、不 FailFast）。
/// </summary>
public sealed class McpViewWorkspaceBlockedExceptionTests
{
    private static WorkspaceFileBlockedException MakeBlocked()
        => new("res/piece/body/piece_12_3.png", "占用", "受阻");

    [Fact]
    public void ExtractBlocked_Returns_Bare_Blocked_Exception()
    {
        WorkspaceFileBlockedException blocked = MakeBlocked();
        Assert.Same(blocked, McpViewWorkspace.ExtractBlocked(blocked));
    }

    [Fact]
    public void ExtractBlocked_Unwraps_Single_Aggregate_To_Blocked()
    {
        WorkspaceFileBlockedException blocked = MakeBlocked();
        Assert.Same(blocked, McpViewWorkspace.ExtractBlocked(new AggregateException(blocked)));
    }

    [Fact]
    public void ExtractBlocked_Unwraps_Nested_Aggregate()
    {
        WorkspaceFileBlockedException blocked = MakeBlocked();
        Assert.Same(blocked, McpViewWorkspace.ExtractBlocked(new AggregateException(new AggregateException(blocked))));
    }

    [Fact]
    public void ExtractBlocked_Picks_Blocked_From_Mixed_Inner_Exceptions()
    {
        WorkspaceFileBlockedException blocked = MakeBlocked();
        AggregateException agg = new(new InvalidOperationException("x"), blocked);
        Assert.Same(blocked, McpViewWorkspace.ExtractBlocked(agg));
    }

    [Fact]
    public void ExtractBlocked_Returns_Null_When_No_Blocked_Anywhere()
    {
        Assert.Null(McpViewWorkspace.ExtractBlocked(new InvalidOperationException("boom")));
        Assert.Null(McpViewWorkspace.ExtractBlocked(
            new AggregateException(new InvalidOperationException("a"), new FormatException("b"))));
    }
}
