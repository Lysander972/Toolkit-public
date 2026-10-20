// 实体解析模型 - 第 1 层内部使用
// 这些类型用于 EntityParsers 和 ProjectModel 之间的数据传递

using Toolkit.Core.Bescript;

namespace Toolkit.Core.Persistence;

/// <summary>
/// 零件链接信息 - 代码仓库引用
/// </summary>
public sealed class DeviceLinkInfo
{
    public string Guid { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

// ========== 零件目录解析模型 ==========

/// <summary>
/// 零件目录解析结果
/// </summary>
internal sealed record DeviceDirectoryInfo(
    string Directory,
    DeviceMetadata Metadata,
    IReadOnlyList<DeviceGlobalVar> GlobalVars,
    IReadOnlyList<BescriptSource> UserScripts,
    IReadOnlyList<EventScriptInfo> EventScripts,
    IReadOnlyList<GuidScriptRef> GuidScriptRefs
);

/// <summary>
/// 事件脚本信息
/// </summary>
internal sealed record EventScriptInfo(
    string Directory,
    string EventName,
    IReadOnlyList<BescriptSource> Scripts
);

/// <summary>
/// GUID 脚本引用 - 来自代码仓库的脚本引用
/// </summary>
internal sealed record GuidScriptRef(
    string Guid,
    string Name,
    string SourceDirectory,
    bool IsEventScript,
    string? EventName
);

// ========== 零件变量模型 ==========

/// <summary>
/// 零件全局变量
/// </summary>
internal sealed record DeviceGlobalVar(
    string Name,
    BescriptVarType Type,
    bool IsGlobal,
    string? Comment = null
);

/// <summary>
/// 零件身份信息
/// </summary>
internal sealed record DeviceIdentity(
    string Directory,
    int DeviceId,
    int DeviceType,
    string CarrierGuid,
    string CarrierKind,
    string? LogicName
);
