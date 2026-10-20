using System.Text.Json.Serialization;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Refactor;
using Toolkit.Core.TsView;

namespace Toolkit.Mcp;

// ============================================================================
// Refactor Payload 类型定义
// ============================================================================

/// <summary>重命名标识符的 payload</summary>
internal sealed record RenameVarPayload(
    [property: JsonPropertyName("changes")] IReadOnlyList<IdentifierChange> Changes);

/// <summary>脚本移入仓库的 payload</summary>
internal sealed record ScriptToRepositoryPayload(
    [property: JsonPropertyName("script_guid")] string ScriptGuid,
    [property: JsonPropertyName("repository_path")] string RepositoryPath);

/// <summary>脚本链接仓库的 payload</summary>
internal sealed record ScriptLinkRepositoryPayload(
    [property: JsonPropertyName("linked_guid")] string LinkedGuid,
    [property: JsonPropertyName("repository_path")] string RepositoryPath);

/// <summary>脚本删除仓库的 payload</summary>
internal sealed record ScriptDeleteRepositoryPayload(
    [property: JsonPropertyName("deleted_guid")] string DeletedGuid,
    [property: JsonPropertyName("repository_path")] string RepositoryPath);

/// <summary>Canvas 提取的 payload</summary>
internal sealed record CanvasExtractPayload(
    [property: JsonPropertyName("extracted_guid")] string ExtractedGuid,
    [property: JsonPropertyName("canvas_path")] string CanvasPath,
    [property: JsonPropertyName("patched_script_path")] string PatchedScriptPath);

/// <summary>Canvas 导入的 payload</summary>
internal sealed record CanvasImportPayload(
    [property: JsonPropertyName("linked_guid")] string LinkedGuid,
    [property: JsonPropertyName("patched_script_path")] string PatchedScriptPath);

/// <summary>Canvas 内联的 payload</summary>
internal sealed record CanvasInlinePayload(
    [property: JsonPropertyName("inlined_canvas_name")] string InlinedCanvasName,
    [property: JsonPropertyName("patched_script_path")] string PatchedScriptPath,
    [property: JsonPropertyName("canvas_file_deleted")] bool CanvasFileDeleted);

/// <summary>局部变量提升为全局变量的 payload</summary>
internal sealed record VarToGlobalPayload(
    [property: JsonPropertyName("global_var_name")] string GlobalVarName,
    [property: JsonPropertyName("vars_json_updated")] bool VarsJsonUpdated);

/// <summary>全局变量降级/删除的 payload</summary>
internal sealed record VarsToLocalPayload(
    [property: JsonPropertyName("removed_vars")] IReadOnlyList<string> RemovedVars,
    [property: JsonPropertyName("vars_json_updated")] bool VarsJsonUpdated);

/// <summary>TSC 状态结果的 payload（error/skipped）</summary>
internal sealed record TscStatusPayload(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string? Reason = null);

/// <summary>TSC 不可用的 payload</summary>
internal sealed record TscUnavailablePayload(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("hint")] string Hint);

/// <summary>TSC 成功结果的 payload</summary>
internal sealed record TscOkPayload(
    [property: JsonPropertyName("truncated")] bool? Truncated,
    [property: JsonPropertyName("items")] IReadOnlyList<McpToolRegistry.TscDiagnosticItem> Items);

/// <summary>搜索结果的 payload</summary>
internal sealed record SearchPayload(
    [property: JsonPropertyName("entities")] IReadOnlyList<EntitySearchResultItem> Entities);

internal sealed record PatchToolResponse(
    [property: JsonPropertyName("errors")] IReadOnlyList<ToolError> Errors,
    [property: JsonPropertyName("warnings")] IReadOnlyList<ToolError> Warnings,
    [property: JsonPropertyName("infos")] IReadOnlyList<ToolError> Infos,
    [property: JsonPropertyName("data")] object? Data,
    [property: JsonIgnore] bool PatchMatched);

/// <summary>
/// MCP 工具注册表 — 数据类型定义
/// </summary>
public sealed partial class McpToolRegistry
{
    private sealed record PatchData(
        [property: JsonPropertyName("affected_paths")] IReadOnlyList<string>? AffectedPaths,
        [property: JsonPropertyName("preview_png_paths")] IReadOnlyList<string>? PreviewPngPaths,
        [property: JsonPropertyName("lint_results")] object? LintResults,
        /// <summary>新建 Canvas 分配的 GUID（修改时为 null）；Agent 可直接取用免再读</summary>
        [property: JsonPropertyName("created_canvas_guid")] string? CreatedCanvasGuid = null
    );

    private sealed record LintMergedData(
        [property: JsonPropertyName("items")] IReadOnlyList<TsViewLintResultItem> Items,
        [property: JsonPropertyName("truncated")] bool Truncated,
        [property: JsonPropertyName("ts_diagnostics")] object? TsDiagnostics
    );

    /// <summary>
    /// 代码仓库写入结果
    /// </summary>
    private sealed record RepoWriteResult(
        [property: JsonPropertyName("affected_paths")] List<string> AffectedPaths
    );

    private sealed record CleanupDraftsResponse(
        [property: JsonPropertyName("removed_count")] int RemovedCount
    );

    private sealed record PlayerLogMetaData(
        [property: JsonPropertyName("file_size_bytes")] long FileSizeBytes,
        [property: JsonPropertyName("line_count")] int LineCount
    );

    private sealed record PlayerLogReadData(
        [property: JsonPropertyName("lines")] IReadOnlyList<string> Lines
    );

    private sealed record PlayerLogSearchMatch(
        [property: JsonPropertyName("line_number")] int LineNumber,
        [property: JsonPropertyName("line")] string Line
    );

    private sealed record PlayerLogSearchData(
        [property: JsonPropertyName("matches")] IReadOnlyList<PlayerLogSearchMatch> Matches
    );
}
