using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Worker;

/// <summary>
/// Worker 命令处理器 - TS 视图相关命令
/// </summary>
public sealed partial class WorkerCommandHandler
{
    private WorkerResponse HandleCodeRepoTsViewRead()
    {
        return Execute(() =>
        {
            (CodeRepoTsViewReadData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) result = _services.CodeRepoTsView.Read(_services.Model);
            if (ToErrorResponse(result.Data, result.Errors, "读取 CodeRepo TS 视图失败") is { } opError)
            {
                return opError;
            }

            return new WorkerResponse(Ok: true, CodeRepoTsViewRead: result.Data);
        });
    }
}
