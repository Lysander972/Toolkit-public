namespace Toolkit.Core.Infra;

/// <summary>
/// 工作区文件被外部进程占用。
/// 仅用于边界层向上递归传递明确的阻塞事实。
/// </summary>
public sealed class WorkspaceFileBlockedException : IOException
{
    public const string DefaultBlockMessage = "当前工作区有文件或目录被外部进程占用，已进入受阻状态，请稍后重试。";

    public string RelativePath { get; }
    public string BlockMessage { get; }

    public WorkspaceFileBlockedException(string relativePath, string message, Exception? innerException = null)
        : this(relativePath, message, DefaultBlockMessage, innerException)
    {
    }

    public WorkspaceFileBlockedException(string relativePath, string message, string blockMessage, Exception? innerException = null)
        : base(message, innerException)
    {
        RelativePath = WorkspacePath.RequireRelativePath(relativePath);
        BlockMessage = blockMessage;
    }
}

public sealed class WorkspaceStateChangedException : InvalidOperationException
{
    public WorkspaceStateChangedException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// 工作区阻塞状态快照。
/// </summary>
public sealed record WorkspaceBlockStateSnapshot(
    bool IsBlocked,
    IReadOnlyList<string> BlockedRelativePaths,
    string Message
);

/// <summary>
/// 工作区阻塞状态变更事件。
/// </summary>
public sealed class WorkspaceBlockStateChangedEventArgs : EventArgs
{
    public WorkspaceBlockStateSnapshot State { get; }

    public WorkspaceBlockStateChangedEventArgs(WorkspaceBlockStateSnapshot state)
    {
        State = state;
    }
}
