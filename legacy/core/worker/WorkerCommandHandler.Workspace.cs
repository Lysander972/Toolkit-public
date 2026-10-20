using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Worker;

/// <summary>
/// Worker 命令处理器 - 工作区检查相关命令
/// </summary>
public sealed partial class WorkerCommandHandler
{
    private WorkerResponse HandleLintWorkspace(WorkerRequest req)
    {
        // root 由插件端传入；Lint 规则口径统一以核心规则集为准，不做插件侧特化过滤。
        string rawRoot = RequireParam(req.LintRootFullPath, "root");
        string root = WorkspacePath.RequireRelativePath(rawRoot);

        return Execute(() =>
        {
            // 使用 ScriptAnalysisService 解析脚本 AST
            ScriptAnalysisService analysisService = new(_services.Model);

            // 执行全工程 Lint 扫描
            IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(_services.Model, analysisService);

            // 处理诊断路径并去重
            LintDiagnostic[] processedDiagnostics = diagnostics.Select(LintEngine.NormalizeDiagnostic).ToArray();
            LintDiagnostic[] deduplicated = LintEngine.DeduplicateDiagnostics(processedDiagnostics);

            return new WorkerResponse(Ok: true, Diagnostics: deduplicated);
        });
    }

    /// <summary>
    /// 获取工程树 - 从核心库 ProjectModel 实体树转换为前端可用的树结构
    /// </summary>
#pragma warning disable TK006 // 命令分发模式：每个 WorkerCommandKind 对应独立 handler，非一次性 Helper
    private WorkerResponse HandleGetProjectTree()
#pragma warning restore TK006
    {
        return Execute(() =>
        {
            ProjectRootEntity root = _services.Model.Root;

            List<ProjectTreeNode> tree = BuildProjectTreeNodes(root);
            return new WorkerResponse(Ok: true, ProjectTree: tree);
        });
    }

    /// <summary>
    /// 从实体树构建前端可用的树节点列表
    /// </summary>
    private List<ProjectTreeNode> BuildProjectTreeNodes(ProjectRootEntity root)
    {
        List<ProjectTreeNode> result = [];

        foreach (ProjectEntity child in root.Children)
        {
            ProjectTreeNode? node = TryBuildTreeNode(child);
            if (node != null)
            {
                result.Add(node);
            }
        }

        // 排序：文件夹优先，然后按名称
        result.Sort((a, b) =>
        {
            bool aIsFolder = a.Kind == "folder" || a.Kind == "device";
            bool bIsFolder = b.Kind == "folder" || b.Kind == "device";
            if (aIsFolder && !bIsFolder) return -1;
            if (!aIsFolder && bIsFolder) return 1;
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        return result;
    }

    /// <summary>
    /// 从实体构建树节点
    /// </summary>
    private ProjectTreeNode? TryBuildTreeNode(ProjectEntity entity)
    {
        // 获取展示用的 kind
        string? displayKind = GetDisplayKind(entity);
        if (displayKind == null)
        {
            return null;
        }

        string id = entity.实体_完整相对路径标识;
        string name = entity is DeviceEntityBase { LogicName: string logicName }
            ? logicName
            : entity.实体_无相对路径标识符;

        // 构建子节点（协变接口，无 switch）
        List<ProjectTreeNode>? children = null;
        if (entity is IHasChildren<ProjectEntity> hasChildren && hasChildren.Children.Count > 0)
        {
            children = [];
            foreach (ProjectEntity child in hasChildren.Children)
            {
                ProjectTreeNode? childNode = TryBuildTreeNode(child);
                if (childNode != null)
                {
                    children.Add(childNode);
                }
            }

            if (children.Count == 0)
            {
                children = null;
            }
            else
            {
                // 排序子节点
                children.Sort((a, b) =>
                {
                    bool aIsFolder = a.Kind == "folder" || a.Kind == "device";
                    bool bIsFolder = b.Kind == "folder" || b.Kind == "device";
                    if (aIsFolder && !bIsFolder) return -1;
                    if (!aIsFolder && bIsFolder) return 1;
                    return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
                });
            }
        }

        return new ProjectTreeNode(
            Id: id,
            Name: name,
            Kind: displayKind,
            Path: id,
            Children: children
        );
    }

    /// <summary>
    /// 获取实体的展示类型
    /// </summary>
    private static string? GetDisplayKind(ProjectEntity entity)
    {
        return entity.Kind switch
        {
            ProjectEntityKind.Root => null, // 根节点不展示
            ProjectEntityKind.Map => "map",
            ProjectEntityKind.Mech => "mech",
            ProjectEntityKind.Device => "device",
            ProjectEntityKind.ScriptGroup => null, // 脚本组不单独展示
            ProjectEntityKind.InlineScript => "inlineScript",
            ProjectEntityKind.CodeRepoScript => "codeRepoScript",
            ProjectEntityKind.Canvas => "canvas",
            ProjectEntityKind.Resource => "resource",
            ProjectEntityKind.Directory => "folder",
            ProjectEntityKind.Spine => "spine",
            ProjectEntityKind.TempRoot => null, // 临时目录不展示
            ProjectEntityKind.ModDirectory => null, // Mod 目录不展示
            ProjectEntityKind.ToolkitRoot => null, // Toolkit 目录不展示
            ProjectEntityKind.ToolkitInternalDirectory => null,
            ProjectEntityKind.ToolkitFile => null,
            ProjectEntityKind.OtherTempFile => null,
            _ => null
        };
    }

    private WorkerResponse HandleResolveGuidAsset(WorkerRequest req)
    {
        string guid = RequireParam(req.Guid, "guid");

        return Execute(() =>
        {
            // 直接通过 GUID 获取实体
            if (_services.Model.FindByGuid(guid) is not ProjectEntity entity)
            {
                return new WorkerResponse(Ok: false, Message: $"未找到 GUID 对应的资源: {guid}");
            }

            // 返回实体的完整路径
            string relativePath = entity.实体_完整相对路径标识;
            return new WorkerResponse(Ok: true, ResolvedGuidRelativePath: relativePath);
        });
    }

    /// <summary>
    /// 获取全量 GUID → Path 映射（每次从 ProjectModel 重新抓取）
    /// </summary>
    private WorkerResponse HandleGetAllGuidMappings(WorkerRequest req)
    {
        return Execute(() =>
        {
            Dictionary<string, string> mappings = [];
            foreach (KeyValuePair<string, ProjectEntity> kvp in _services.Model.EntitiesByGuid)
            {
                mappings[kvp.Key] = kvp.Value.实体_完整相对路径标识;
            }
            return new WorkerResponse(Ok: true, AllGuidMappings: mappings);
        });
    }

}
