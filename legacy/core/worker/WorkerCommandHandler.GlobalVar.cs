using Toolkit.Core.Infra;
using Toolkit.Core.Refactor;

namespace Toolkit.Core.Worker;

/// <summary>
/// Worker 命令处理器 - 全局变量相关命令
/// </summary>
public sealed partial class WorkerCommandHandler
{
    private WorkerResponse HandleGlobalVarListForScript(WorkerRequest req)
    {
        string scriptPath = RequirePath(req.ScriptRelativePath, "scriptPath");
        (string devicePath, string? eventName) = RequireDeviceOwnership(scriptPath);

        // 此处需要特殊处理：即使失败也要返回 devicePath/eventName
        try
        {
            RefactorResult<GlobalVarListData> result = _services.GlobalVarOperation.List(devicePath);
            GlobalVarListData? data = result.Data;
            if (result.Errors is { Count: > 0 } || data is null)
            {
                string msg = "获取全局变量列表失败";
                if (result.Errors is { Count: > 0 })
                {
                    msg = result.Errors[0].Message;
                }
                return new WorkerResponse(Ok: false, Message: msg, ResolvedDeviceRelativePath: devicePath, ResolvedEventName: eventName);
            }

            return new WorkerResponse(Ok: true, GlobalVarList: data.Variables, ResolvedDeviceRelativePath: devicePath, ResolvedEventName: eventName);
        }
        catch (Exception ex)
        {
            return new WorkerResponse(Ok: false, Message: StackTraceFormatter.FormatException(ex), ResolvedDeviceRelativePath: devicePath, ResolvedEventName: eventName);
        }
    }

    private WorkerResponse HandleGlobalVarBatch(WorkerRequest req)
    {
        string devicePath = RequirePath(req.DeviceRelativePath, "devicePath");

#pragma warning disable TK022 // Operations 是 JSON 反序列化输入，边界 null 检查合法
        if (req.Operations == null || req.Operations.Count == 0)
#pragma warning restore TK022
        {
            return new WorkerResponse(Ok: false, Message: "缺少 operations 参数");
        }

        return Execute(() =>
        {
            RefactorResult<GlobalVarBatchData> result = _services.GlobalVarOperation.Batch(devicePath, req.Operations);
            if (ToErrorResponse(result.Data, result.Errors, "批量操作失败") is { } opError)
            {
                return opError;
            }

            return new WorkerResponse(Ok: true, GlobalVarBatch: result.Data);
        });
    }
}
