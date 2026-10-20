using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Worker;

/// <summary>
/// Worker 命令处理器 - 实体相关命令
/// </summary>
public sealed partial class WorkerCommandHandler
{
    private WorkerResponse HandleEntityInspect(WorkerRequest req)
    {
        string entityPath = RequirePath(req.EntityRelativePath, "entityPath");

        return Execute(() =>
        {
            string relativePath = WorkspacePath.RequireRelativePath(entityPath);

            // 通过 ProjectModel 统一模型获取实体数据
            ProjectModel model = _services.Model;

            // 尝试获取实体（地图、机械、零件、Canvas、代码仓库脚本等）
            string mainFile = relativePath;
            ProjectEntity? entity = null;

            // 按类型尝试获取实体
            if (model.Find<MapEntity>(mainFile) is MapEntity mapEntity)
            {
                entity = mapEntity;
            }
            else if (model.Find<MechEntity>(mainFile) is MechEntity mechEntity)
            {
                entity = mechEntity;
            }
            else if (model.Find<DeviceEntity>(mainFile) is DeviceEntity deviceEntity)
            {
                entity = deviceEntity;
            }
            else if (model.Find<CanvasEntity>(mainFile) is CanvasEntity canvasEntity)
            {
                entity = canvasEntity;
            }
            else if (model.Find<CodeRepoScriptEntity>(mainFile) is CodeRepoScriptEntity codeRepoEntity)
            {
                entity = codeRepoEntity;
            }

            if (entity == null)
            {
                return new WorkerResponse(Ok: false, Message: $"未找到实体: {mainFile}");
            }

            if (entity is not IHasGuid hasGuid)
            {
                return new WorkerResponse(Ok: false, Message: "实体没有 GUID");
            }

            string guid = hasGuid.Guid;

            EntityInspectArgs args = new(
                EntityGuids: new[] { guid },
                ScopeLevel: "entity",
                Include: new EntityInspectInclude(Graph: false, Events: false, DataView: true, KeyOrigins: false));

            (EntityInspectData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) result = _services.EntityInspect.Inspect(args);
            if (ToErrorResponse(result.Data, result.Errors, "实体查看失败") is { } opError)
            {
                return opError;
            }

            return new WorkerResponse(Ok: true, EntityInspect: result.Data);
        });
    }
}
