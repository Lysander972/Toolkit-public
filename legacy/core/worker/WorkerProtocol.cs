using System.Text.Json.Serialization;

using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;
using Toolkit.Core.Refactor;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Worker;

/// <summary>
/// WorkerProtocol - JSON-RPC protocol for VSCode Plugin UI communication.
/// This is ONLY used by the VSCode Plugin UI to communicate with the Host process.
/// NOT to be confused with MCP protocol which is used by AI Agents.
///
/// Architecture:
/// - VSCode Plugin UI --(WorkerProtocol)--> VSCodePluginHost (Worker mode) --(direct)--> Toolkit.Core
/// - AI Agent --(MCP protocol)--> VSCodePluginHost (MCP mode) --(direct)--> Toolkit.Core
///
/// Both paths access the same Core layer, but through different protocols.
/// </summary>
public sealed record WorkerRequest(
    string Cmd,
    int? RequestId = null,

[property: JsonPropertyName("root")]
string? LintRootFullPath = null,

    [property: JsonPropertyName("canvasPath")]
    string? CanvasRelativePath = null,

    string? CanvasArg = null,

    // Symbol lookup parameters
    [property: JsonPropertyName("scriptPath")]
    string? ScriptRelativePath = null,

    string? ScriptText = null,

    int? Line = null,

    string? SymbolName = null,

    // Event documentation parameters
    string? EventName = null,

    string? DeviceType = null,

    // Refactoring parameters
    [property: JsonPropertyName("devicePath")]
    string? DeviceRelativePath = null,

    string? OldName = null,

    string? NewName = null,

    string? TargetGuid = null,

    string? Guid = null,

    string? ScriptName = null,

    // Global variable batch operation parameters
    IReadOnlyList<GlobalVarOp>? Operations = null,

    // View read parameters
    [property: JsonPropertyName("path")]
    string? FileRelativePath = null,

    // Entity inspect parameters
    [property: JsonPropertyName("entityPath")]
    string? EntityRelativePath = null,

    // list.json path parameter (for parsing device and event info from list.json)
    [property: JsonPropertyName("listPath")]
    string? ListRelativePath = null,

    // Canvas preview settings (for updateSettings command)
    [property: JsonPropertyName("canvasPreviewSettings")]
    CanvasPreviewSettingsInput? CanvasPreviewSettings = null,

    // Canvas binary preview (base64 encoded .canvas file content)
    [property: JsonPropertyName("canvasBinaryBase64")]
    string? CanvasBinaryBase64 = null
)
{
}

/// <summary>
/// Canvas 预览设置输入（插件推送到 Host）
/// </summary>
public sealed record CanvasPreviewSettingsInput(
    int DefaultWidth,
    int DefaultHeight
);

// =============================================================================
// 工程树节点 - 用于前端展示
// =============================================================================

/// <summary>
/// 工程树节点，用于前端工程视图展示。
/// 从核心库 ProjectModel 实体树转换而来，避免前端重复实现扫描逻辑。
/// </summary>
public sealed record ProjectTreeNode(
    /// <summary>节点唯一标识（相对路径）</summary>
    string Id,
    /// <summary>显示名称</summary>
    string Name,
    /// <summary>实体类型（folder/map/mech/device/script/canvas/codeRepoScript）</summary>
    string Kind,
    /// <summary>工程根相对路径</summary>
    string? Path = null,
    /// <summary>子节点</summary>
    IReadOnlyList<ProjectTreeNode>? Children = null
);

/// <summary>
/// WorkerProtocol response for VSCode Plugin UI.
/// See WorkerRequest documentation for protocol details.
/// </summary>
public sealed record WorkerResponse(
    bool Ok,
    int? RequestId = null,
    string? Message = null,
    IReadOnlyList<LintDiagnostic>? Diagnostics = null,
    IReadOnlyList<ToolError>? ToolDiagnostics = null,
    // Symbol lookup results
    IReadOnlyList<SymbolLocation>? References = null,
    // BEScript scan results
    IReadOnlyList<ActCallLink>? ActCallLinks = null,
    IReadOnlyList<BescriptBuiltinMethodRange>? BuiltinMethodRanges = null,
    IReadOnlyList<BescriptCanvasArgRange>? CanvasArgRanges = null,
    IReadOnlyList<BescriptGlobalVarRange>? GlobalVarRanges = null,
    // Built-in method documentation (from Core)
    BuiltinMethodDoc? BuiltinMethodDoc = null,
    // Event signature/documentation (from Core)
    IReadOnlyList<EventDoc>? EventDocs = null,
    // Refactoring results
    IdentifierRenameData? IdentifierRename = null,
    IReadOnlyList<GlobalVarInfo>? GlobalVarList = null,
    GlobalVarBatchData? GlobalVarBatch = null,
    ScriptToRepositoryData? ScriptToRepo = null,
    ScriptLinkRepositoryData? ScriptLinkRepo = null,
    IReadOnlyList<string>? ScriptNames = null,
    ScriptRenameData? ScriptRename = null,
    CanvasExtractData? CanvasExtract = null,
    CanvasImportData? CanvasImport = null,
    CanvasInlineData? CanvasInline = null,
    // View read results
    CodeRepoTsViewReadData? CodeRepoTsViewRead = null,
    // Script path resolution results
    [property: JsonPropertyName("resolvedDevicePath")]
    string? ResolvedDeviceRelativePath = null,

    string? ResolvedEventName = null,

    [property: JsonPropertyName("resolvedGuidPath")]
    string? ResolvedGuidRelativePath = null,
    // Entity inspect results
    EntityInspectData? EntityInspect = null,
    // 全量 GUID 映射（每次从 ProjectModel 重新抓取）
    System.Collections.Generic.IReadOnlyDictionary<string, string>? AllGuidMappings = null,
    // Canvas binary preview HTML result
    [property: JsonPropertyName("canvasPreviewHtml")]
    string? CanvasPreviewHtml = null,
    // 工程树（从核心库 ProjectModel 获取）
    IReadOnlyList<ProjectTreeNode>? ProjectTree = null
);

public sealed record SymbolLocation(
    string RelativePath,
    int Line,
    int Column,
    string? Name = null,
    string? Text = null
);

public sealed record ActCallLink(
    string ScriptName,
    BescriptTextSpan Range,
    /// <summary>
    /// 唯一目标定义位置（优先使用）
    /// </summary>
    SymbolLocation? Definition,
    /// <summary>
    /// 多候选位置列表（当有多个同名脚本时）
    /// 仅包含 scripts 脚本组内的同名脚本
    /// </summary>
    IReadOnlyList<SymbolLocation>? Candidates
);

public sealed record BuiltinMethodDoc(
    string Name,
    string Signature,
    string Documentation,
    DocRef? HumanDoc = null,
    DocRef? AgentDoc = null
);

public sealed record EventParameterDoc(
    string Name,
    string TypeName,
    string? Description = null
);

public sealed record EventDoc(
    string EventName,
    string DeviceType,
    string Signature,
    string Documentation,
    string ParamsLine,
    IReadOnlyList<EventParameterDoc> Parameters
);
