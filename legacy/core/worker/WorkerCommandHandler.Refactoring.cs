using Toolkit.Core.Refactor;

namespace Toolkit.Core.Worker;

/// <summary>
/// Worker 命令处理器 - 重构操作相关命令
/// </summary>
public sealed partial class WorkerCommandHandler
{
    private WorkerResponse HandleIdentifierRename(WorkerRequest req)
    {
        string scriptPath = RequirePath(req.ScriptRelativePath, "scriptPath");
        string oldName = RequireParam(req.OldName, "oldName");
        string newName = RequireParam(req.NewName, "newName");

        return Execute(() =>
        {
            RefactorResult<IdentifierRenameData> result = _services.IdentifierRename.Rename(scriptPath, oldName, newName);
            if (ToErrorResponse(result.Data, result.Errors, "重命名失败") is { } opError)
            {
                return opError;
            }

            return new WorkerResponse(Ok: true, IdentifierRename: result.Data);
        });
    }

    private WorkerResponse HandleScriptRename(WorkerRequest req)
    {
        string scriptPath = RequirePath(req.ScriptRelativePath, "scriptPath");
        string newName = RequireParam(req.NewName, "newName");

        return Execute(() =>
        {
            RefactorResult<ScriptRenameData> result = _services.ScriptRename.Rename(scriptPath, newName);
            if (ToErrorResponse(result.Data, result.Errors, "重命名失败") is { } opError)
            {
                return opError;
            }

            return new WorkerResponse(Ok: true, ScriptRename: result.Data);
        });
    }

#pragma warning disable TK006 // 命令分发处理方法，与其他 Handle* 方法对称
    private WorkerResponse HandleResolveDeviceFromScript(WorkerRequest req)
    {
        string scriptPath = RequirePath(req.ScriptRelativePath, "scriptPath");
        (string devicePath, string? eventName) = RequireDeviceOwnership(scriptPath);

        return new WorkerResponse(Ok: true, ResolvedDeviceRelativePath: devicePath, ResolvedEventName: eventName);
    }
#pragma warning restore TK006
}
