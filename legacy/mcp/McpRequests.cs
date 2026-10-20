using System.Text.Json.Serialization;

namespace Toolkit.Mcp;

// =============================================================================
// MCP 请求参数定义
// =============================================================================
// 原则：MCP 层只做协议适配，不定义业务模型。
// 所有业务请求/响应模型都来自 Toolkit.Core，此处只做必要的反序列化包装。
// =============================================================================

/// <summary>patch_mech_view_multiedit 的编辑项</summary>
public sealed record ViewEdit(
    [property: JsonPropertyName("old_string")] string OldString,
    [property: JsonPropertyName("new_string")] string NewString,
    [property: JsonPropertyName("replace_all")] bool ReplaceAll = false);

/// <summary>变量重命名项</summary>
public sealed record RenameItem(
    [property: JsonPropertyName("old_name")] string OldName,
    [property: JsonPropertyName("new_name")] string NewName);

/// <summary>patch_mech_view 单编辑请求</summary>
public sealed record ApplyPatchRequest(
    [property: JsonPropertyName("view_path")] string ViewPath,
    [property: JsonPropertyName("old_string")] string OldString,
    [property: JsonPropertyName("new_string")] string NewString,
    [property: JsonPropertyName("replace_all")] bool ReplaceAll = false,
    [property: JsonPropertyName("base_fingerprint")] string? BaseFingerprint = null
);

/// <summary>patch_mech_view_multiedit 多编辑请求</summary>
public sealed record ApplyPatchMultiRequest(
    [property: JsonPropertyName("view_path")] string ViewPath,
    [property: JsonPropertyName("edits")] ViewEdit[] Edits,
    [property: JsonPropertyName("base_fingerprint")] string? BaseFingerprint = null
);

/// <summary>read_player_log 请求</summary>
public sealed record ReadPlayerLogRequest(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("start_line")] int? StartLine,
    [property: JsonPropertyName("end_line")] int? EndLine,
    [property: JsonPropertyName("keyword")] string? Keyword);

// =============================================================================
// 重构操作目标类型（MCP 协议反序列化专用）
// =============================================================================

public sealed record RenameIdentifierTarget(
    [property: JsonPropertyName("script_relative_path")] string ScriptRelativePath,
    [property: JsonPropertyName("renames")] IReadOnlyList<RenameItem> Renames);

public sealed record RenameGlobalVarTarget(
    [property: JsonPropertyName("device_relative_path")] string DeviceRelativePath,
    [property: JsonPropertyName("renames")] IReadOnlyList<RenameItem> Renames);

public sealed record ScriptRenameTarget(
    [property: JsonPropertyName("script_relative_path")] string ScriptRelativePath,
    [property: JsonPropertyName("new_name")] string NewName);

public sealed record FileRenameTarget(
    [property: JsonPropertyName("old_relative_path")] string OldRelativePath,
    [property: JsonPropertyName("new_relative_path")] string NewRelativePath);

public sealed record ScriptToRepositoryTarget(
    [property: JsonPropertyName("device_relative_path")] string DeviceRelativePath,
    [property: JsonPropertyName("script_name")] string ScriptName,
    [property: JsonPropertyName("script_group")] string ScriptGroup);

public sealed record ScriptLinkRepositoryTarget(
    [property: JsonPropertyName("device_relative_path")] string DeviceRelativePath,
    [property: JsonPropertyName("script_name")] string ScriptName,
    [property: JsonPropertyName("script_group")] string ScriptGroup,
    [property: JsonPropertyName("target_guid")] string TargetGuid,
    [property: JsonPropertyName("force")] bool Force = false);

public sealed record ScriptDeleteRepositoryTarget(
    [property: JsonPropertyName("target_guid")] string TargetGuid);

public sealed record ScriptDeleteTarget(
    [property: JsonPropertyName("device_relative_path")] string DeviceRelativePath,
    [property: JsonPropertyName("script_name")] string ScriptName,
    [property: JsonPropertyName("script_group")] string ScriptGroup,
    [property: JsonPropertyName("force")] bool Force = false);

public sealed record ScriptGroupOrderItem(
    [property: JsonPropertyName("group_name")] string GroupName,
    [property: JsonPropertyName("ordered_names")] string[] OrderedNames);

public sealed record OutlineGlobalVarItem(
    [property: JsonPropertyName("name")] string Name);

public sealed record DeviceOutlineData(
    [property: JsonPropertyName("script_groups")] ScriptGroupOrderItem[] ScriptGroups,
    [property: JsonPropertyName("global_variables")] OutlineGlobalVarItem[] GlobalVariables);

public sealed record ScriptReorderTarget(
    [property: JsonPropertyName("device_relative_path")] string DeviceRelativePath,
    [property: JsonPropertyName("outline")] DeviceOutlineData? Outline);

public sealed record CanvasExtractTarget(
    [property: JsonPropertyName("script_relative_path")] string ScriptRelativePath,
    [property: JsonPropertyName("line")] int Line);

public sealed record CanvasImportByPathTarget(
    [property: JsonPropertyName("script_relative_path")] string ScriptRelativePath,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("source_canvas_relative_path")] string SourceCanvasRelativePath);

public sealed record CanvasImportByGuidTarget(
    [property: JsonPropertyName("script_relative_path")] string ScriptRelativePath,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("target_guid")] string TargetGuid);

public sealed record CanvasInlineTarget(
    [property: JsonPropertyName("script_relative_path")] string ScriptRelativePath,
    [property: JsonPropertyName("line")] int Line);

/// <summary>
/// 变量提升为全局变量 - 只创建 vars.json 中的全局变量，不修改任何 .code 文件
/// 类型从指定脚本的首次出现位置自动提取
/// </summary>
public sealed record VarToGlobalTarget(
    [property: JsonPropertyName("script_group_path")] string ScriptGroupPath,
    [property: JsonPropertyName("script_name")] string ScriptName,
    [property: JsonPropertyName("var_name")] string VarName,
    [property: JsonPropertyName("allow_other_usages")] bool AllowOtherUsages = false);

/// <summary>
/// 将全局变量降回局部 - 只删除 vars.json 中的条目，不修改任何 .code 文件
/// </summary>
public sealed record VarsToLocalTarget(
    [property: JsonPropertyName("device_relative_path")] string DeviceRelativePath,
    [property: JsonPropertyName("var_names")] string[] VarNames,
    [property: JsonPropertyName("require_no_refs")] bool RequireNoRefs = true);

// =============================================================================
// 以下请求类型直接使用核心库定义：
// - ReadEntitiesRequest  -> Toolkit.Core.EntitySearchRequest
// - ReadLintRequest      -> Toolkit.LintResultsRequest
// =============================================================================
