// 实体检查工具 - Logic 层
// 基于 Persistence 层提供的 ProjectModel 进行实体检查分析

using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

/// <summary>
/// 实体检查参数
/// </summary>
public sealed record EntityInspectArgs(
    string[] EntityGuids,
    string ScopeLevel = "entity",
    EntityInspectInclude? Include = null
);

/// <summary>
/// 实体检查 include 选项
/// </summary>
public sealed record EntityInspectInclude(
    bool Graph,
    bool Events,
    bool DataView,
    bool KeyOrigins
);

/// <summary>
/// 关联文件路径
/// </summary>
public sealed record RelatedFiles(
    string? MainFileRelativePath,
    string? GuidFileRelativePath,
    string[] 派生目录_完整相对路径s
);

/// <summary>
/// 实体图
/// </summary>
public sealed record EntityGraph(
    GraphNode[] Nodes,
    GraphEdge[] Edges
);

/// <summary>
/// 图节点
/// </summary>
public sealed record GraphNode(
    string Id,
    string Kind,
    string Name
);

/// <summary>
/// 图边
/// </summary>
public sealed record GraphEdge(
    string From,
    string To,
    string Kind
);

/// <summary>
/// 实体事件
/// </summary>
public sealed record EntityEvent(
    string Name,
    string SourceId,
    string? TargetGuid
);

/// <summary>
/// Key 来源
/// </summary>
public sealed record KeyOrigin(
    string Key,
    string Kind,
    string OriginId
);

/// <summary>
/// 实体数据视图（零件明细）
/// </summary>
public sealed record EntityInspectDeviceView(
    IReadOnlyList<EntityInspectDeviceInfo> Devices
);

/// <summary>
/// 零件明细
/// </summary>
public sealed record EntityInspectDeviceInfo(
    int DeviceType,
    string? Alias,
    IReadOnlyList<EntityInspectScriptInfo> Scripts,
    IReadOnlyList<EntityInspectVarInfo> GlobalVars
);

/// <summary>
/// 脚本信息
/// </summary>
public sealed record EntityInspectScriptInfo(
    string RelativePath,
    string? EventName
);

/// <summary>
/// 变量信息
/// </summary>
public sealed record EntityInspectVarInfo(
    string Name,
    string? Type
);

/// <summary>
/// 实体视图（包含视图位置）
/// </summary>
public sealed record EntityView(
    string? Guid,
    string Kind,
    string Name,
    RelatedFiles? RelatedFiles,
    string? View,
    string[]? Children,
    EntityGraph? Graph,
    EntityEvent[]? Events,
    object? DataView,
    KeyOrigin[]? KeyOrigins
);

/// <summary>
/// entity.inspect 返回数据
/// </summary>
public sealed record EntityInspectData(
    string ViewLevel,
    IReadOnlyList<EntityView> Entities
);

/// <summary>
/// 实体检查工具 - Logic 层。
/// 基于 Persistence 层数据进行实体检查分析。
/// </summary>
public sealed class EntityInspectTool
{
    private readonly ProjectModel _model;

    public EntityInspectTool(ProjectLogicContext context)
    {
        _model = context.Model;
    }

    /// <summary>
    /// 检查实体，返回带视图位置的完整信息
    /// </summary>
    public (EntityInspectData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Inspect(
        EntityInspectArgs args)
    {
        List<ToolError> errors = [];
        List<ToolError> warnings = [];
        List<EntityView> entities = [];

        if (args.EntityGuids.Length == 0)
        {
            errors.Add(new ToolError(null, null, null, "必须提供 entityGuids"));
            return (null, errors, warnings);
        }

        foreach (string guid in args.EntityGuids)
        {
            if (_model.FindByGuid(guid) is ProjectEntity entity)
            {
                entities.Add(BuildEntityView(entity, args.Include));
            }
            else
            {
                errors.Add(new ToolError(null, null, null, $"实体不存在: {guid}"));
            }
        }

        return (
            new EntityInspectData(
                ViewLevel: args.ScopeLevel,
                Entities: entities
            ),
            errors,
            warnings
        );
    }

    private EntityView BuildEntityView(ProjectEntity entity, EntityInspectInclude? include)
    {
        string? guid = null;
        if (entity is IHasGuid hasGuid)
            guid = hasGuid.Guid;

        string[]? children = BuildChildren(entity);
        EntityGraph? graph = null;
        EntityEvent[]? events = null;
        object? dataView = null;
        KeyOrigin[]? keyOrigins = null;

        if (include is { Graph: true })
        {
            children = null;
            graph = BuildEntityGraph(entity);
        }
        if (include is { Events: true })
            events = BuildEntityEvents(entity);
        if (include is { DataView: true })
            dataView = BuildDataView(entity);
        if (include is { KeyOrigins: true })
            keyOrigins = BuildKeyOrigins(entity);

        return new EntityView(
            Guid: guid,
            Kind: entity.Kind.ToString(),
            Name: entity.实体_完整相对路径标识,
            RelatedFiles: BuildRelatedFiles(entity),
            View: BuildViewLocation(entity),
            Children: children,
            Graph: graph,
            Events: events,
            DataView: dataView,
            KeyOrigins: keyOrigins
        );
    }

#pragma warning disable TK006 // 实体视图路径构建，与 BuildChildren/BuildRelatedFiles 对称
    private string? BuildViewLocation(ProjectEntity entity)
    {
        return entity switch
        {
            DeviceEntityBase device => EntityViewPath.GetDeviceTsViewPath(device.device目录_完整相对路径),
            CanvasEntityBase canvas => EntityViewPath.GetCanvasHtmlViewPath(canvas.实体_完整相对路径标识),
            CodeRepoScriptEntity => EntityViewPath.GetCodeRepoTsViewPath(),
            _ => null
        };
    }
#pragma warning restore TK006

#pragma warning disable TK006 // 实体子节点构建，与 BuildTsViewPath/BuildRelatedFiles 对称
    private string[]? BuildChildren(ProjectEntity entity)
#pragma warning restore TK006
    {
        return entity switch
        {
            MapEntity map => map.Devices.Select(static d => d.DeviceId.ToString()).ToArray(),
            MechEntity mech => mech.Devices.Select(static d => d.DeviceId.ToString()).ToArray(),
            _ => null
        };
    }

    private RelatedFiles? BuildRelatedFiles(ProjectEntity entity)
    {
        return entity switch
        {
            MapEntity map => new RelatedFiles(
                MainFileRelativePath: map.map文件_完整相对路径,
                GuidFileRelativePath: map.GUID文件_完整相对路径,
                派生目录_完整相对路径s: new[] { map.派生目录_完整相对路径 }
            ),
            MechEntity mech => new RelatedFiles(
                MainFileRelativePath: mech.mech文件_完整相对路径,
                GuidFileRelativePath: mech.GUID文件_完整相对路径,
                派生目录_完整相对路径s: new[] { mech.派生目录_完整相对路径 }
            ),
            DeviceEntity device when device.ScriptGroups.Count > 0 => new RelatedFiles(
                MainFileRelativePath: null,
                GuidFileRelativePath: null,
                派生目录_完整相对路径s: device.ScriptGroups.Select(static g => g.脚本组目录_完整相对路径).ToArray()
            ),
            _ => null
        };
    }

    private EntityGraph? BuildEntityGraph(ProjectEntity entity)
    {
        List<GraphNode> nodes = [];
        List<GraphEdge> edges = [];

        if (entity is MapEntity map)
        {
            string mapGuid = map.Guid;
            HashSet<string> importedMechGuids = map.MechImports.Select(i => i.MechGuid).ToHashSet();
            foreach (MechEntity mech in _model.ListMechs().Where(m => importedMechGuids.Contains(m.Guid)))
            {
                nodes.Add(new GraphNode(mech.Guid, "mech", mech.实体_完整相对路径标识));
                edges.Add(new GraphEdge(mapGuid, mech.Guid, "imports"));
            }
            foreach (DeviceEntityBase device in map.Devices)
            {
                string deviceId = device.DeviceId.ToString();
                nodes.Add(new GraphNode(deviceId, "device", device.device目录_完整相对路径));
                edges.Add(new GraphEdge(mapGuid, deviceId, "contains"));
            }
        }
        else if (entity is MechEntity mech)
        {
            string mechGuid = mech.Guid;
            foreach (DeviceEntityBase device in mech.Devices)
            {
                string deviceId = device.DeviceId.ToString();
                nodes.Add(new GraphNode(deviceId, "device", device.device目录_完整相对路径));
                edges.Add(new GraphEdge(mechGuid, deviceId, "contains"));
            }
        }
        else if (entity is DeviceEntity device)
        {
            string devId = device.DeviceId.ToString();
            foreach (DeviceLinkInfo link in device.Links)
            {
                if (GuidText.Validate(link.Guid))
                    edges.Add(new GraphEdge(devId, link.Guid, "links"));
            }
        }

        if (nodes.Count == 0 && edges.Count == 0)
            return null;

        return new EntityGraph(nodes.ToArray(), edges.ToArray());
    }

#pragma warning disable TK006 // 实体事件构建，与 BuildChildren/BuildRelatedFiles 对称
    private EntityEvent[]? BuildEntityEvents(ProjectEntity entity)
    {
        return entity switch
        {
            DeviceEntity device => device.EventScripts
                .SelectMany(kv => kv.Value.Select(s => new EntityEvent(s.实体_完整相对路径标识, device.DeviceId.ToString(), null)))
                .ToArray(),
            _ => null
        };
    }
#pragma warning restore TK006

    private object? BuildDataView(ProjectEntity entity)
    {
        return entity switch
        {
            DeviceEntity device => new EntityInspectDeviceView(
                new[] { new EntityInspectDeviceInfo(
                    device.DeviceType,
                    device.LogicName,
                    device.Scripts.Select(static s => new EntityInspectScriptInfo(s.实体_完整相对路径标识, s.EventName)).ToArray(),
                    device.Vars.Variables.Select(static v => new EntityInspectVarInfo(v.Name, v.TypeName)).ToList()
                ) }.ToList()
            ),
            _ => null
        };
    }

    private KeyOrigin[]? BuildKeyOrigins(ProjectEntity entity)
    {
        return entity switch
        {
            DeviceEntity device => device.Links
                .Where(l => GuidText.Validate(l.Guid))
                .Select(l => new KeyOrigin(l.Guid, "link", device.DeviceId.ToString()))
                .ToArray(),
            _ => null
        };
    }
}
