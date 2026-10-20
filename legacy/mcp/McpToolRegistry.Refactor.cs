using System.Text.Json;
using System.Text.Json.Serialization;
using Toolkit.Core.Infra;
using Toolkit.Core.Refactor;

namespace Toolkit.Mcp;

/// <summary>
/// MCP 工具注册表 — Refactor 工具执行逻辑
/// </summary>
public sealed partial class McpToolRegistry
{
    private sealed record RefactorResult(
        [property: JsonPropertyName("data")] object? Data,
        [property: JsonPropertyName("affected_paths")] IReadOnlyList<string> AffectedPaths
    );

    /// <summary>executor 内部返回的操作结果（不含 AffectedPaths/Applied）</summary>
    private sealed record RefactorOpResult(
        [property: JsonPropertyName("data")] object? Data
    );

    private delegate (RefactorOpResult Result, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings, IReadOnlyList<string> AffectedPaths) RefactorExecutor(
        JsonElement target);

    private (RefactorResult? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) BuildRefactorSingle(
        RefactorExecutor executor,
        JsonElement? args)
    {
        if (args == null)
        {
            return Fail<RefactorResult>("缺少参数");
        }

        if (args.Value.ValueKind != JsonValueKind.Object)
        {
            return Fail<RefactorResult>("参数必须是对象");
        }

        JsonElement root = args.Value;
        JsonElement targetElement = JsonSerializer.SerializeToElement(root, McpJsonOptions.Standard);
        (RefactorOpResult? opResult, IReadOnlyList<ToolError>? errors, IReadOnlyList<ToolError>? warnings, IReadOnlyList<string>? affectedPaths) = ViewWorkspace.WaitUntilSynchronized(
            () => executor(targetElement));

        RefactorResult result = new RefactorResult(opResult.Data, affectedPaths);
        return (result, errors, warnings);
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteRenameVar(JsonElement target)
    {
        const string kind = "rename.var";
        (RenameIdentifierTarget? req, string? parseError) = DeserializeRefactorTarget<RenameIdentifierTarget>(target, ["script_relative_path", "renames"], []);
        if (req == null)
        {
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));
        }

        IdentifierRename tool = _services.IdentifierRename;
        IdentifierRenameItem[] renames = req.Renames.Select(x => new IdentifierRenameItem(x.OldName, x.NewName)).ToArray();
        RefactorResult<IdentifierRenameData> opResult = tool.Rename(req.ScriptRelativePath, renames);
        IdentifierRenameData? data = opResult.Data;
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;
        RenameVarPayload? payload = null;
        if (data != null)
            payload = new RenameVarPayload(data.Changes);
        RefactorOpResult result = new RefactorOpResult(payload);
        return (result, errors, warnings, new[] { req.ScriptRelativePath });
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteRenameGlobalVar(JsonElement target)
    {
        const string kind = "rename.globalVar";
        (RenameGlobalVarTarget? req, string? parseError) = DeserializeRefactorTarget<RenameGlobalVarTarget>(target, ["device_relative_path", "renames"], []);
        if (req == null)
        {
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));
        }

        GlobalVarRenameItem[] renames = req.Renames.Select(x => new GlobalVarRenameItem(x.OldName, x.NewName)).ToArray();
        RefactorResult<GlobalVarBatchData> opResult = _services.GlobalVarOperation.RenameOnlyDeviceScripts(req.DeviceRelativePath, renames);
        GlobalVarBatchData? data = opResult.Data;
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;
        RefactorOpResult result = new RefactorOpResult(data);
        IReadOnlyList<string> affected = [req.DeviceRelativePath];
        if (data != null)
            affected = new[] { req.DeviceRelativePath }.Concat(data.AffectedScripts).ToArray();
        return (result, errors, warnings, affected);
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteRenameScript(JsonElement target)
    {
        const string kind = "rename.script";
        (ScriptRenameTarget? req, string? parseError) = DeserializeRefactorTarget<ScriptRenameTarget>(target, ["script_relative_path", "new_name"], []);
        if (req == null)
        {
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));
        }

        ScriptRenameTool tool = _services.ScriptRename;
        RefactorResult<ScriptRenameData> opResult = tool.Rename(req.ScriptRelativePath, req.NewName);
        ScriptRenameData? data = opResult.Data;
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;
        RefactorOpResult result = new RefactorOpResult(null);
        string[] affected = [];
        if (data != null)
            affected = new[] { data.NewScriptRelativePath }.Concat(data.UpdatedListFiles).ToArray();
        return (result, errors, warnings, affected);
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteRenameFile(JsonElement target)
    {
        const string kind = "rename.file";
        (FileRenameTarget? req, string? parseError) = DeserializeRefactorTarget<FileRenameTarget>(target, ["old_relative_path", "new_relative_path"], []);
        if (req == null)
        {
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));
        }

        RefactorResult<FileRenameData> opResult = _services.FileRename.Rename(req.OldRelativePath, req.NewRelativePath, _services.Model);
        FileRenameData? data = opResult.Data;
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;
        if (data == null)
        {
            if (errors.Count == 0)
            {
                throw new InvalidOperationException("ExecuteRenameFile 内部状态非法：data 为 null 且 errors 为空");
            }

            RefactorOpResult failedResult = new RefactorOpResult(null);
            return (failedResult, errors, warnings, Array.Empty<string>());
        }

        RefactorOpResult result = new RefactorOpResult(null);
        return (result, errors, warnings, data.AffectedRelativePaths);
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteScriptToRepository(JsonElement target)
    {
        const string kind = "script.toRepository";
        (ScriptToRepositoryTarget? req, string? parseError) = DeserializeRefactorTarget<ScriptToRepositoryTarget>(target, ["device_relative_path", "script_name", "script_group"], []);
        if (req == null)
        {
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));
        }

        ScriptRepository tool = _services.ScriptRepository;
        RefactorResult<ScriptToRepositoryData> opResult = tool.ToRepository(req.DeviceRelativePath, req.ScriptName, req.ScriptGroup);
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;
        if (!opResult.IsSuccess)
            return (new RefactorOpResult(null), errors, warnings, Array.Empty<string>());
        ScriptToRepositoryData data = opResult.SuccessData;
        ScriptToRepositoryPayload payload = new(data.ScriptGuid, data.RepositoryRelativePath);
        RefactorOpResult result = new RefactorOpResult(payload);
        string[] affected = [data.RepositoryRelativePath];
        return (result, errors, warnings, affected);
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteScriptLinkRepository(JsonElement target)
    {
        const string kind = "script.linkRepository";
        (ScriptLinkRepositoryTarget? req, string? parseError) = DeserializeRefactorTarget<ScriptLinkRepositoryTarget>(target, ["device_relative_path", "script_name", "script_group", "target_guid"], ["force"]);
        if (req == null)
        {
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));
        }

        ScriptRepository tool = _services.ScriptRepository;
        RefactorResult<ScriptLinkRepositoryData> opResult = tool.LinkRepository(req.DeviceRelativePath, req.ScriptName, req.ScriptGroup, req.TargetGuid, req.Force);
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;
        if (!opResult.IsSuccess)
            return (new RefactorOpResult(null), errors, warnings, Array.Empty<string>());
        ScriptLinkRepositoryData data = opResult.SuccessData;
        ScriptLinkRepositoryPayload payload = new(data.LinkedGuid, data.RepositoryRelativePath);
        RefactorOpResult result = new RefactorOpResult(payload);
        string[] affected = [data.RepositoryRelativePath];
        return (result, errors, warnings, affected);
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteScriptDeleteRepository(JsonElement target)
    {
        const string kind = "script.deleteRepository";
        (ScriptDeleteRepositoryTarget? req, string? parseError) = DeserializeRefactorTarget<ScriptDeleteRepositoryTarget>(target, ["target_guid"], []);
        if (req == null)
        {
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));
        }

        ScriptRepository tool = _services.ScriptRepository;
        RefactorResult<ScriptDeleteRepositoryData> opResult = tool.DeleteRepository(req.TargetGuid);
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;
        if (!opResult.IsSuccess)
            return (new RefactorOpResult(null), errors, warnings, Array.Empty<string>());
        ScriptDeleteRepositoryData data = opResult.SuccessData;
        ScriptDeleteRepositoryPayload payload = new(data.DeletedGuid, data.RepositoryRelativePath);
        RefactorOpResult result = new RefactorOpResult(payload);
        string[] affected = [data.RepositoryRelativePath, data.RepositoryRelativePath + ".guid"];
        return (result, errors, warnings, affected);
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteScriptDelete(JsonElement target)
    {
        const string kind = "script.delete";
        (ScriptDeleteTarget? req, string? parseError) = DeserializeRefactorTarget<ScriptDeleteTarget>(target, ["device_relative_path", "script_name", "script_group"], ["force"]);
        if (req == null)
        {
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));
        }

        ScriptDelete tool = _services.ScriptDelete;
        (ScriptDeleteData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) opResult = tool.Delete(
            req.DeviceRelativePath, req.ScriptName, req.ScriptGroup, req.Force);
        ScriptDeleteData? data = opResult.Data;
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;

        if (errors.Count > 0)
            return (new RefactorOpResult(null), errors, warnings, Array.Empty<string>());
        if (data is not ScriptDeleteData successData)
            throw new InvalidOperationException("ScriptDelete 成功结果缺少数据");
        RefactorOpResult result = new RefactorOpResult(null);
        string[] affected = successData.AffectedRelativePaths.ToArray();
        return (result, errors, warnings, affected);
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteScriptReorder(JsonElement target)
    {
        const string kind = "script.reorder";
        (ScriptReorderTarget? req, string? parseError) = DeserializeRefactorTarget<ScriptReorderTarget>(target, ["device_relative_path"], ["outline"]);
        if (req == null)
        {
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));
        }

        ScriptReorder tool = _services.ScriptReorder;
        IReadOnlyList<ScriptGroupOrder>? orders = req.Outline?.ScriptGroups == null
            ? null
            : req.Outline.ScriptGroups.Select(o => new ScriptGroupOrder(o.GroupName, o.OrderedNames)).ToArray();
        IReadOnlyList<string>? globalVarOrder = req.Outline?.GlobalVariables == null
            ? null
            : req.Outline.GlobalVariables.Select(v => v.Name).ToArray();
        (ScriptReorderData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) opResult = tool.Reorder(req.DeviceRelativePath, orders, globalVarOrder);
        ScriptReorderData? data = opResult.Data;
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;

        if (errors.Count > 0)
            return (new RefactorOpResult(null), errors, warnings, Array.Empty<string>());
        if (data is not ScriptReorderData successData)
            throw new InvalidOperationException("ScriptReorder 成功结果缺少数据");
        RefactorOpResult result = new RefactorOpResult(successData);
        string[] affected = successData.AffectedRelativePaths.ToArray();
        return (result, errors, warnings, affected);
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteCanvasExtract(JsonElement target)
    {
        const string kind = "canvas.extract";
        (CanvasExtractTarget? req, string? parseError) = DeserializeRefactorTarget<CanvasExtractTarget>(target, ["script_relative_path", "line"], []);
        if (req == null)
        {
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));
        }

        CanvasRefactor tool = _services.CanvasRefactor;
        RefactorResult<CanvasExtractData> opResult = tool.Extract(req.ScriptRelativePath, req.Line);
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;
        if (!opResult.IsSuccess)
            return (new RefactorOpResult(null), errors, warnings, Array.Empty<string>());
        CanvasExtractData data = opResult.SuccessData;
        CanvasExtractPayload payload = new(data.ExtractedGuid, data.CanvasRelativePath, data.PatchedScriptRelativePath);
        RefactorOpResult result = new RefactorOpResult(payload);
        return (result, errors, warnings, data.AffectedPaths);
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteCanvasImportByPath(JsonElement target)
    {
        const string kind = "canvas.import_by_path";
        (CanvasImportByPathTarget? req, string? parseError) = DeserializeRefactorTarget<CanvasImportByPathTarget>(target, ["script_relative_path", "line", "source_canvas_relative_path"], []);
        if (req == null)
        {
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));
        }

        CanvasRefactor tool = _services.CanvasRefactor;
        RefactorResult<CanvasImportData> opResult = tool.ImportByPath(req.ScriptRelativePath, req.Line, req.SourceCanvasRelativePath);
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;
        if (!opResult.IsSuccess)
            return (new RefactorOpResult(null), errors, warnings, Array.Empty<string>());
        CanvasImportData data = opResult.SuccessData;
        CanvasImportPayload payload = new(data.LinkedGuid, data.PatchedScriptRelativePath);
        RefactorOpResult result = new RefactorOpResult(payload);
        return (result, errors, warnings, data.AffectedPaths);
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteCanvasImportByGuid(JsonElement target)
    {
        const string kind = "canvas.import_by_guid";
        (CanvasImportByGuidTarget? req, string? parseError) = DeserializeRefactorTarget<CanvasImportByGuidTarget>(target, ["script_relative_path", "line", "target_guid"], []);
        if (req == null)
        {
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));
        }

        CanvasRefactor tool = _services.CanvasRefactor;
        RefactorResult<CanvasImportData> opResult = tool.ImportByGuid(req.ScriptRelativePath, req.Line, req.TargetGuid);
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;
        if (!opResult.IsSuccess)
            return (new RefactorOpResult(null), errors, warnings, Array.Empty<string>());
        CanvasImportData data = opResult.SuccessData;
        CanvasImportPayload payload = new(data.LinkedGuid, data.PatchedScriptRelativePath);
        RefactorOpResult result = new RefactorOpResult(payload);
        return (result, errors, warnings, data.AffectedPaths);
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteCanvasInline(JsonElement target)
    {
        const string kind = "canvas.inline";
        (CanvasInlineTarget? req, string? parseError) = DeserializeRefactorTarget<CanvasInlineTarget>(target, ["script_relative_path", "line"], []);
        if (req == null)
        {
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));
        }

        CanvasRefactor tool = _services.CanvasRefactor;
        RefactorResult<CanvasInlineData> opResult = tool.Inline(req.ScriptRelativePath, req.Line);
        CanvasInlineData? data = opResult.Data;
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;
        if (data == null)
            return (new RefactorOpResult(null), errors, warnings, Array.Empty<string>());
        RefactorOpResult result = new(new CanvasInlinePayload(data.InlinedCanvasName, data.PatchedScriptRelativePath, data.CanvasFileDeleted));
        return (result, errors, warnings, data.AffectedPaths);
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteVarToGlobal(JsonElement target)
    {
        const string kind = "var.toGlobal";
        (VarToGlobalTarget? req, string? parseError) = DeserializeRefactorTarget<VarToGlobalTarget>(target, ["script_group_path", "script_name", "var_name"], ["allow_other_usages"]);
        if (req == null)
        {
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));
        }

        (VarToGlobalData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) opResult = _services.GlobalVarOperation.CreateGlobalVarOnly(
            req.ScriptGroupPath,
            req.ScriptName,
            req.VarName,
            req.AllowOtherUsages);
        VarToGlobalData? data = opResult.Data;
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;
        if (data == null)
            return (new RefactorOpResult(null), errors, warnings, Array.Empty<string>());
        RefactorOpResult result = new(new VarToGlobalPayload(data.GlobalVarName, data.VarsJsonUpdated));
        return (result, errors, warnings, data.AffectedPaths);
    }

    private (RefactorOpResult, IReadOnlyList<ToolError>, IReadOnlyList<ToolError>, IReadOnlyList<string>) ExecuteVarsToLocal(JsonElement target)
    {
        const string kind = "vars.toLocal";
        (VarsToLocalTarget? req, string? parseError) = DeserializeRefactorTarget<VarsToLocalTarget>(target, ["device_relative_path", "var_names"], ["require_no_refs"]);
        if (req == null)
            return FailRefactorOp(kind, parseError ?? throw new InvalidOperationException(
                $"DeserializeRefactorTarget 内部不一致：null 结果但无错误消息（{kind}）"));

        (VarsToLocalData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) opResult = _services.GlobalVarOperation.VarsToLocal(
            req.DeviceRelativePath,
            req.VarNames,
            req.RequireNoRefs);
        VarsToLocalData? data = opResult.Data;
        IReadOnlyList<ToolError> errors = opResult.Errors;
        IReadOnlyList<ToolError> warnings = opResult.Warnings;
        if (data == null)
            return (new RefactorOpResult(null), errors, warnings, Array.Empty<string>());
        RefactorOpResult result = new(new VarsToLocalPayload(data.RemovedVars, data.VarsJsonUpdated));
        return (result, errors, warnings, data.AffectedPaths);
    }

    private static (RefactorOpResult Result, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings, IReadOnlyList<string> AffectedPaths) FailRefactorOp(
        string kind,
        string message)
    {
        ToolError[] errors = [new ToolError(null, null, null, message)];
        return (new RefactorOpResult(null), errors, Array.Empty<ToolError>(), Array.Empty<string>());
    }

    /// <summary>
    /// 支持的 refactor 操作类型及其 target 字段定义
    /// </summary>
    private static (T? Result, string? Error) DeserializeRefactorTarget<T>(JsonElement target, string[] requiredFields, string[] optionalFields) where T : class
    {
        if (target.ValueKind != JsonValueKind.Object)
        {
            return (null, "参数必须是对象");
        }

        HashSet<string> allAllowed = new HashSet<string>(requiredFields.Concat(optionalFields), StringComparer.Ordinal);

        HashSet<string> providedFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty prop in target.EnumerateObject())
        {
            providedFields.Add(prop.Name);
        }

        string[] missingRequired = requiredFields.Where(f => !providedFields.Contains(f)).ToArray();
        if (missingRequired.Length > 0)
        {
            string schemaMsg = $"必需字段: {string.Join(", ", requiredFields)}";
            if (optionalFields.Length > 0)
                schemaMsg += $"；可选字段: {string.Join(", ", optionalFields)}";
            return (null, $"缺少必需字段: {string.Join(", ", missingRequired)}。{schemaMsg}");
        }

        string[] extraFields = providedFields.Where(f => !allAllowed.Contains(f)).ToArray();
        if (extraFields.Length > 0)
        {
            string schemaMsg = $"必需字段: {string.Join(", ", requiredFields)}";
            if (optionalFields.Length > 0)
                schemaMsg += $"；可选字段: {string.Join(", ", optionalFields)}";
            return (null, $"存在未知字段: {string.Join(", ", extraFields)}。{schemaMsg}");
        }

        try
        {
            T? result = JsonSerializer.Deserialize<T>(target.GetRawText(), McpJsonOptions.Standard);
            if (result == null)
            {
                return (null, "参数解析失败");
            }
            return (result, null);
        }
        catch (JsonException ex)
        {
            return (null, $"参数格式错误: {ex.Message}");
        }
    }

    private static (T? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Fail<T>(string message)
        where T : class
    {
        return (null, [new ToolError(null, null, null, message)], Array.Empty<ToolError>());
    }

    private static string? ValidateViewPath(string path)
    {
        if (path.AsSpan().IsWhiteSpace())
            return "path 为空";

        if (path.Contains('\\', StringComparison.Ordinal))
            return "path 必须使用 /，禁止使用 \\";

        if (path.StartsWith("./", StringComparison.Ordinal))
            return "path 禁止以 ./ 开头";

        if (path.StartsWith("views/", StringComparison.Ordinal))
            return "path 禁止以 views/ 开头，应为相对 temp/toolkit/views 的路径";

        if (path.StartsWith("temp/", StringComparison.Ordinal)
            || path.StartsWith("/temp/", StringComparison.Ordinal)
            || path.IndexOf("/temp/", StringComparison.Ordinal) >= 0)
            return "path 禁止包含 temp/ 前缀，应直接传相对 temp/toolkit/views 的路径";

        if (path.StartsWith("/", StringComparison.Ordinal)
            || (path.Length >= 3
                && char.IsLetter(path[0])
                && path[1] == ':'
                && path[2] == '/'))
            return "path 禁止使用绝对路径";

        if (path.Contains("..", StringComparison.Ordinal))
            return "path 禁止包含 ..";

        if (path.Contains("//", StringComparison.Ordinal))
            return "path 禁止包含 //";

        return null;
    }

    private static (bool Ok, string DraftRelativePath, string ViewPath, string Error) TryValidateDraftPath(string path)
    {
        if (path.Length > 0)
        {
            if (path.Contains('\\', StringComparison.Ordinal))
            {
                return (false, string.Empty, string.Empty, "draft_path 必须使用 /，禁止使用 \\");
            }

            if (path.StartsWith("./", StringComparison.Ordinal))
            {
                return (false, string.Empty, string.Empty, "draft_path 禁止以 ./ 开头");
            }

            if (path.StartsWith("/", StringComparison.Ordinal)
                || (path.Length >= 3
                    && char.IsLetter(path[0])
                    && path[1] == ':'
                    && path[2] == '/'))
            {
                return (false, string.Empty, string.Empty, "draft_path 禁止使用绝对路径");
            }

            if (path.Contains("..", StringComparison.Ordinal))
            {
                return (false, string.Empty, string.Empty, "draft_path 禁止包含 ..");
            }

            if (path.Contains("//", StringComparison.Ordinal))
            {
                return (false, string.Empty, string.Empty, "draft_path 禁止包含 //");
            }

            if (path.StartsWith("temp/", StringComparison.Ordinal))
            {
                return (false, string.Empty, string.Empty, "draft_path 应为相对视图根的路径（如 xxx.ts.draft），禁止以 temp/ 开头");
            }

            if (!path.EndsWith(".ts.draft", StringComparison.Ordinal)
                && !path.EndsWith(".html.draft", StringComparison.Ordinal))
            {
                return (false, string.Empty, string.Empty, "draft_path 后缀不正确（仅支持 .ts.draft/.html.draft）");
            }

            string viewPath = path[..^".draft".Length];
            if (viewPath.Equals("CodeRepo.ts", StringComparison.Ordinal))
            {
                return (false, string.Empty, string.Empty, "CodeRepo.ts.draft 禁止 commit_draft");
            }

            return (true, path, viewPath, string.Empty);
        }
        return (false, string.Empty, string.Empty, "draft_path 为空");
    }

    private void RegisterRefactorTool(
        string name,
        string description,
        RefactorExecutor executor,
        InputSchema schema)
    {
        Register(
            name: name,
            description: description,
            schema: schema,
            handler: async (args, cancellationToken) =>
            {
                // 所有调用统一串行。
                await MCPWriteLock.WaitAsync(cancellationToken);
                try
                {
                    (RefactorResult? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) result = BuildRefactorSingle(executor, args);
                    RefactorResult? data = result.Data;
                    IReadOnlyList<ToolError> errors = result.Errors;
                    IReadOnlyList<ToolError> warnings = result.Warnings;
                    return BuildResponse(data, errors, warnings);
                }
                finally
                {
                    MCPWriteLock.Release();
                }
            }
        );
    }
}
