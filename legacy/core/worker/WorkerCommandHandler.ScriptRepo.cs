using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Refactor;

namespace Toolkit.Core.Worker;

/// <summary>
/// Worker 命令处理器 - 脚本仓库相关命令
/// </summary>
public sealed partial class WorkerCommandHandler
{
    private WorkerResponse HandleScriptToRepository(WorkerRequest req)
    {
        string devicePath = RequirePath(req.DeviceRelativePath, "devicePath");
        string scriptName = RequireParam(req.ScriptName, "scriptName");

        string scriptGroup = ProjectEntityLayout.ScriptsDirName;
        if (req.EventName != null)
        {
            scriptGroup = ProjectEntityLayout.EventDirPrefix + req.EventName;
        }

        return Execute(() =>
        {
            RefactorResult<ScriptToRepositoryData> result = _services.ScriptRepository.ToRepository(devicePath, scriptName, scriptGroup);
            if (ToErrorResponse(result.Data, result.Errors, "脚本导出失败") is { } opError)
            {
                return opError;
            }

            return new WorkerResponse(Ok: true, ScriptToRepo: result.Data);
        });
    }

    private WorkerResponse HandleScriptLinkRepository(WorkerRequest req)
    {
        string listPath = RequirePath(req.ListRelativePath, "listPath");

        (string DeviceRelativePath, string? EventName)? parsed = ScriptPathHelper.ParseListJsonPath(listPath);
        if (parsed is null)
        {
            return new WorkerResponse(Ok: false, Message: "无法从 list.json 路径解析零件信息");
        }

        string devicePath = parsed.Value.DeviceRelativePath;
        string? eventName = parsed.Value.EventName;

        string scriptName = RequireParam(req.ScriptName, "scriptName");
        string targetGuid = RequireParam(req.TargetGuid, "targetGuid");

        string scriptGroup = ProjectEntityLayout.ScriptsDirName;
        if (eventName != null)
        {
            scriptGroup = ProjectEntityLayout.EventDirPrefix + eventName;
        }

        return Execute(() =>
        {
            RefactorResult<ScriptLinkRepositoryData> result = _services.ScriptRepository.LinkRepository(devicePath, scriptName, scriptGroup, targetGuid);
            if (ToErrorResponse(result.Data, result.Errors, "脚本关联失败") is { } opError)
            {
                return opError;
            }

            return new WorkerResponse(Ok: true, ScriptLinkRepo: result.Data);
        });
    }

    private WorkerResponse HandleListScriptGroupEntries(WorkerRequest req)
    {
        string listPath = WorkspacePath.RequireRelativePath(RequirePath(req.ListRelativePath, "listPath"));
        if (_services.Model.Find<ScriptGroupEntity>(listPath) is not ScriptGroupEntity scriptGroup || scriptGroup.ListJsonText == null)
            throw new InvalidOperationException($"未找到脚本组 list.json: {listPath}");

        IReadOnlyList<string> scriptNames = HjsonText.ParseListItems(scriptGroup.ListJsonText)
            .Select(static item => item.Name)
            .ToArray();
        return new WorkerResponse(Ok: true, ScriptNames: scriptNames);
    }
}
