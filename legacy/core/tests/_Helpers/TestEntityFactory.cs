using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests;

internal static class TestEntityFactory
{
    public static ProjectRootEntity CreateRoot()
    {
        return new ProjectRootEntity();
    }

    public static MapEntity CreateMap(ProjectRootEntity root, string mapName = "main")
    {
        string guid = NewGuid();

        return CreateMap(root, mapName, guid);
    }

    public static MapEntity CreateMap(ProjectRootEntity root, string mapName, string guid)
    {

        MapEntity map = new MapEntity
        {
            Parent = root,
            Guid = guid
        };
        map.SetIdentifierInternal(mapName + ProjectEntityLayout.MapExt);
        AttachChild(root, map);
        return map;
    }

    public static MechEntity CreateMech(ProjectRootEntity root, string mechName = "mech")
    {
        string guid = NewGuid();

        return CreateMech(root, mechName, guid);
    }

    public static MechEntity CreateMech(ProjectRootEntity root, string mechName, string guid)
    {

        MechEntity mech = new MechEntity
        {
            Parent = root,
            Guid = guid
        };
        mech.SetIdentifierInternal(mechName + ProjectEntityLayout.MechExt);
        AttachChild(root, mech);
        return mech;
    }

    public static DeviceEntity CreateDevice(MapEntityBase parent, int deviceId = 1, int deviceType = 1, string? logicName = null, string? stickToBone = null, string? mechFileGuid = null)
    {
        DeviceEntity device = new DeviceEntity
        {
            Parent = parent,
            CarrierGuid = parent.Guid,
            CarrierKind = "Map"
        };
        device.SetDeviceIdInternal(deviceId);
        device.SetDeviceIdStrInternal(deviceId.ToString());

        // 通过 DeviceJsonRecord 设置 DeviceType / LogicName / StickToBone / MechFileGuid
        string stickToBoneField = stickToBone is null
            ? string.Empty
            : $"\"stickToBone\":\"{stickToBone}\",";
        string mechFileGuidField = mechFileGuid is null
            ? string.Empty
            : $"\"mechFileGUID\":\"{mechFileGuid}\",";
        string jsonText = $"{{\"type\":{deviceType},{stickToBoneField}\"ext\":{{{mechFileGuidField}\"logicName\":\"{logicName}\"}}}}";
        device.SetDeviceJsonInternal(new DeviceJsonRecord(jsonText));

        AttachChild(parent, device);
        return device;
    }

    /// <summary>
    /// 创建挂在机械上的零件实例。机械场景下 CarrierKind == "Mech"，与 CarrierKind == "Map"
    /// 对应不同的载体能力条件。
    /// </summary>
    public static DeviceEntity CreateDeviceOnMech(MechEntity parent, int deviceId = 1, int deviceType = 1, string? logicName = null, string? stickToBone = null)
    {
        DeviceEntity device = new DeviceEntity
        {
            Parent = parent,
            CarrierGuid = parent.Guid,
            CarrierKind = "Mech"
        };
        device.SetDeviceIdInternal(deviceId);
        device.SetDeviceIdStrInternal(deviceId.ToString());

        string stickToBoneField = stickToBone is null
            ? string.Empty
            : $"\"stickToBone\":\"{stickToBone}\",";
        string jsonText = $"{{\"type\":{deviceType},{stickToBoneField}\"ext\":{{\"logicName\":\"{logicName}\"}}}}";
        device.SetDeviceJsonInternal(new DeviceJsonRecord(jsonText));

        AttachChild(parent, device);
        return device;
    }

    public static ScriptGroupEntity CreateScriptGroup(DeviceEntityBase device, string? eventName = null)
    {
        string dirName = ProjectEntityLayout.ScriptsDirName;
        string? normalizedEvent = eventName;
        if (eventName is not null)
        {
            dirName = ProjectEntityLayout.EventDirPrefix + eventName;
        }

        ScriptGroupEntity group = new ScriptGroupEntity
        {
            Parent = device
        };
        group.SetScriptGroupNameInternal(dirName);
        group.SetEventNameInternal(normalizedEvent);
        group.SetScriptEntriesInternal([]);
        group.SetScriptsInternal([]);
        AttachChild(device, group);
        return group;
    }

#pragma warning disable TK009 // 默认参数值必须为编译时常量，string.Empty 不是常量表达式
    public static InlineScriptEntity CreateInlineScript(ScriptGroupEntityBase group, string scriptName, string sourceText = "")
#pragma warning restore TK009
    {
        string fileName = scriptName + ProjectEntityLayout.CodeExt;

        InlineScriptEntity script = new InlineScriptEntity
        {
            Parent = group
        };
        script.SetScriptFileNameInternal(fileName);
        script.SetEventNameInternal(group.EventName);
        script.SetSourceTextInternal(sourceText);
        AttachChild(group, script);
        return script;
    }

#pragma warning disable TK009 // 默认参数值必须为编译时常量，string.Empty 不是常量表达式
    public static CodeRepoScriptEntity CreateCodeRepoScript(ProjectRootEntity root, string relativePath, string guid, string sourceText = "")
#pragma warning restore TK009
    {
        (ProjectEntity parent, string fileName) = EnsureDirectoryForFile(root, relativePath);
        CodeRepoScriptEntity script = new CodeRepoScriptEntity
        {
            Parent = parent,
            Guid = guid
        };
        script.SetIdentifierInternal(fileName);
        script.SetSourceTextInternal(sourceText);
        AttachChild(parent, script);
        return script;
    }

    public static CanvasEntity CreateCanvas(ProjectRootEntity root, string relativePath, string guid, byte[] data)
    {
        (ProjectEntity parent, string fileName) = EnsureDirectoryForFile(root, relativePath);
        CanvasEntity canvas = new CanvasEntity
        {
            Parent = parent,
            Guid = guid
        };
        canvas.SetIdentifierInternal(fileName);
        canvas.SetDataInternal(data);
        AttachChild(parent, canvas);
        return canvas;
    }

    public static ResourceEntity CreateResource(ProjectRootEntity root, string relativePath, string guid, byte[] data)
    {
        (ProjectEntity parent, string fileName) = EnsureDirectoryForFile(root, relativePath);
        ResourceEntity resource = new()
        {
            Parent = parent,
            Guid = guid,
            Data = data
        };
        resource.SetIdentifierInternal(fileName);
        AttachChild(parent, resource);
        return resource;
    }

    public static DirectoryEntity CreateDirectory(ProjectRootEntity root, string relativePath)
    {
        string normalized = WorkspacePath.RequireRelativePath(relativePath);
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        ProjectEntity parent = root;
        DirectoryEntity? current = null;

        foreach (string segment in segments)
        {
            current = new DirectoryEntity(segment) { Parent = parent };
            AttachChild(parent, current);
            parent = current;
        }

        if (current is null)
        {
            throw new InvalidOperationException($"路径无有效段: {normalized}");
        }

        return current;
    }

    private static (ProjectEntity parent, string fileName) EnsureDirectoryForFile(ProjectRootEntity root, string relativePath)
    {
        string normalized = WorkspacePath.RequireRelativePath(relativePath);
        string fileName = RelativePathUtils.GetLastSegment(normalized);
        string parentDir = RelativePathUtils.GetParentDirectory(normalized);
        ProjectEntity parent = root;
        if (parentDir != WorkspacePath.RootDirectory)
        {
            parent = CreateDirectory(root, parentDir);
        }
        return (parent, fileName);
    }

    private static void AttachChild(ProjectEntity parent, ProjectEntity child)
    {
        if (parent is IHasChildren<DeviceEntityBase> deviceChildren && child is DeviceEntityBase device)
        {
            AppendToReadOnlyList(deviceChildren.Children, device);
            return;
        }

        if (parent is IHasChildren<ScriptGroupEntityBase> scriptGroupChildren && child is ScriptGroupEntityBase scriptGroup)
        {
            AppendToReadOnlyList(scriptGroupChildren.Children, scriptGroup);
            return;
        }

        if (parent is IHasChildren<InlineScriptEntityBase> scriptChildren && child is InlineScriptEntityBase script)
        {
            AppendToReadOnlyList(scriptChildren.Children, script);
            return;
        }

        if (parent is IHasChildren<ProjectEntity> entityChildren)
        {
            AppendToReadOnlyList(entityChildren.Children, child);
            return;
        }

        throw new InvalidOperationException($"父实体不支持挂载子实体: {parent.GetType().Name} -> {child.GetType().Name}");
    }

    private static void AppendToReadOnlyList<TChild>(IReadOnlyList<TChild> children, TChild child)
    {
        if (children is List<TChild> list)
        {
            list.Add(child);
            return;
        }

        throw new InvalidOperationException($"子实体列表不是可写 List: {children.GetType().Name}");
    }

    public static string NewGuid() => GuidText.NewGuid();
}
