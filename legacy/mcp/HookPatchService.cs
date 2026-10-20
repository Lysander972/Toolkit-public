using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Toolkit.Core.Infra;

namespace Toolkit.Mcp;

public sealed class HookPatchService
{
    private readonly McpRequestHandler _handler;
    private readonly GuardService _guardService;
    private readonly McpViewWorkspace? _viewWorkspace;
    private readonly string _viewsRootFullPath;
    private long _requestId;

    public HookPatchService(McpRequestHandler handler, GuardService guardService, string projectRootFullPath, McpViewWorkspace? viewWorkspace = null)
    {
        _handler = handler;
        _guardService = guardService;
        _viewWorkspace = viewWorkspace;
        _viewsRootFullPath = Path.GetFullPath(Path.Combine(projectRootFullPath, "temp", "toolkit", "views")).Replace('\\', '/');
    }

    public Task<JsonObject> HandleAsync(string agent, string raw, CancellationToken cancellationToken)
    {
        if (agent == "claude")
            return HandleClaudeAsync(raw, cancellationToken);

        GuardResponse guardResponse = _guardService.Check(new GuardRequest(agent, raw));
        JsonObject response = guardResponse.Allow
            ? new JsonObject()
            : Deny(guardResponse.Reason);
        return Task.FromResult(response);
    }

    private async Task<JsonObject> HandleClaudeAsync(string raw, CancellationToken cancellationToken)
    {
        PatchCreateResult patchCreateResult = TryCreateClaudePatchCall(raw);
        if (patchCreateResult.DenyReason != null)
            return Deny(patchCreateResult.DenyReason);

        if (patchCreateResult.PatchCall != null)
        {
            string reason = await ExecutePatchCallAsync(patchCreateResult.PatchCall, cancellationToken).ConfigureAwait(false);
            return Deny(reason);
        }

        GuardResponse guardResponse = _guardService.Check(new GuardRequest("claude", raw));
        if (guardResponse.Allow)
            return new JsonObject();

        return Deny(guardResponse.Reason);
    }

    private PatchCreateResult TryCreateClaudePatchCall(string raw)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            return PatchCreateResult.None;
        }

        if (root == null)
            return PatchCreateResult.None;

        string? toolName = root["tool_name"]?.GetValue<string>();
        JsonNode? toolInput = root["tool_input"];
        if (toolInput == null)
            return PatchCreateResult.None;

        string? filePath = toolInput["file_path"]?.GetValue<string>();
        if (filePath == null)
            return PatchCreateResult.None;

        string? viewPath = TryCreateViewPath(filePath);
        if (viewPath == null)
            return PatchCreateResult.None;

        PatchCall? patchCall = toolName switch
        {
            "Edit" => CreateEditPatchCall(toolInput, viewPath),
            "MultiEdit" => CreateMultiEditPatchCall(toolInput, viewPath),
            _ => null
        };
        if (patchCall != null)
            return new PatchCreateResult(patchCall, null);

        if (toolName == "Write")
            return CreateWritePatchCall(toolInput, viewPath);

        return PatchCreateResult.None;
    }

    private PatchCreateResult CreateWritePatchCall(JsonNode toolInput, string viewPath)
    {
        string? content = toolInput["content"]?.GetValue<string>();
        if (content == null)
            return PatchCreateResult.None;

        if (_viewWorkspace == null)
        {
            return new PatchCreateResult(null, "已阻断 Claude 原生 Write。Host 当前没有视图工作区，无法转发为 MCP 全量覆盖；请改用 patch_mech_view。");
        }

        (string currentText, IReadOnlyList<ToolError> errors) = ReadCurrentViewText(viewPath, forPatch: false);
        bool pathNotFound = errors.Any(static error => error.Message.StartsWith("路径不存在", StringComparison.Ordinal));
        if (pathNotFound)
        {
            string errorText = string.Join("\n", errors.Select(static error => error.Message));

            // 路径不存在时按后缀分流：.html 转发为 patch 新建（old_string 留空触发新建分支），.ts 拒绝并引导用户去编辑器创建零件。
            if (pathNotFound && viewPath.EndsWith(".html", StringComparison.Ordinal))
            {
                JsonObject createArguments = new()
                {
                    ["view_path"] = viewPath,
                    ["old_string"] = string.Empty,
                    ["new_string"] = content,
                    ["replace_all"] = false
                };
                return new PatchCreateResult(new PatchCall("patch_mech_view", createArguments), null);
            }

            if (pathNotFound && viewPath.EndsWith(".ts", StringComparison.Ordinal))
            {
                return new PatchCreateResult(null, $"已阻断 Claude 原生 Write：{viewPath} 不存在；patch_mech_view 不支持新建 TS 文件，请先在编辑器中创建零件。");
            }

            return new PatchCreateResult(null, $"已阻断 Claude 原生 Write。Host 无法读取当前视图文本，未执行全量覆盖：{errorText}。请先读取确认目标视图，再改用 patch_mech_view。");
        }

        (currentText, errors) = ReadCurrentViewText(viewPath, forPatch: true);
        if (errors.Count > 0)
        {
            string errorText = string.Join("\n", errors.Select(static error => error.Message));
            return new PatchCreateResult(null, $"已阻断 Claude 原生 Write。Host 无法读取当前视图文本，未执行全量覆盖：{errorText}。请先读取确认目标视图，再改用 patch_mech_view。");
        }

        JsonObject arguments = new()
        {
            ["view_path"] = viewPath,
            ["old_string"] = currentText,
            ["new_string"] = content,
            ["replace_all"] = false
        };
        return new PatchCreateResult(new PatchCall("patch_mech_view", arguments), null);
    }

    private PatchCall? CreateEditPatchCall(JsonNode toolInput, string viewPath)
    {
        string? oldString = toolInput["old_string"]?.GetValue<string>();
        string? newString = toolInput["new_string"]?.GetValue<string>();
        if (oldString == null || newString == null)
            return null;

        bool replaceAll = false;
        JsonNode? replaceAllNode = toolInput["replace_all"];
        if (replaceAllNode != null)
            replaceAll = replaceAllNode.GetValue<bool>();

        JsonObject arguments = new()
        {
            ["view_path"] = viewPath,
            ["old_string"] = oldString,
            ["new_string"] = newString,
            ["replace_all"] = replaceAll
        };
        return new PatchCall("patch_mech_view", arguments);
    }

    private PatchCall? CreateMultiEditPatchCall(JsonNode toolInput, string viewPath)
    {
        JsonArray? edits = toolInput["edits"]?.AsArray();
        if (edits == null)
            return null;

        JsonArray patchEdits = new();
        foreach (JsonNode? edit in edits)
        {
            if (edit == null)
                return null;

            JsonNode? replaceAllNode = edit["replace_all"];
            if (replaceAllNode != null && replaceAllNode.GetValue<bool>())
                return null;

            string? oldString = edit["old_string"]?.GetValue<string>();
            string? newString = edit["new_string"]?.GetValue<string>();
            if (oldString == null || newString == null)
                return null;

            patchEdits.Add(new JsonObject
            {
                ["old_string"] = oldString,
                ["new_string"] = newString
            });
        }

        JsonObject arguments = new()
        {
            ["view_path"] = viewPath,
            ["edits"] = patchEdits
        };
        return new PatchCall("patch_mech_view_multiedit", arguments);
    }

    private string? TryCreateViewPath(string filePath)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath).Replace('\\', '/');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        string prefix = _viewsRootFullPath + "/";
        if (!fullPath.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        string viewPath = fullPath[prefix.Length..];
        if (!viewPath.EndsWith(".ts", StringComparison.Ordinal) && !viewPath.EndsWith(".html", StringComparison.Ordinal))
            return null;

        return viewPath;
    }

    private (string Text, IReadOnlyList<ToolError> Errors) ReadCurrentViewText(string viewPath, bool forPatch)
    {
        McpViewWorkspace viewWorkspace = _viewWorkspace ?? throw new InvalidOperationException("McpViewWorkspace is required.");
        if (viewPath.EndsWith(".ts", StringComparison.Ordinal))
        {
            string deviceRelativePath = viewPath[..^".ts".Length];
            (string text, IReadOnlyList<ToolError> errors, _) = viewWorkspace.ReadDeviceView(deviceRelativePath, forPatch);
            return (text, errors);
        }

        string canvasRelativePath = viewPath[..^".html".Length] + ".canvas";
        (string html, IReadOnlyList<ToolError> htmlErrors, _) = viewWorkspace.ReadCanvasHtmlView(canvasRelativePath, forPatch);
        return (html, htmlErrors);
    }

    private async Task<string> ExecutePatchCallAsync(PatchCall patchCall, CancellationToken cancellationToken)
    {
        JsonRpcRequest request = new()
        {
            Id = new RequestId(Interlocked.Increment(ref _requestId)),
            Method = "tools/call",
            Params = new JsonObject
            {
                ["name"] = patchCall.ToolName,
                ["arguments"] = patchCall.Arguments
            }
        };

        JsonRpcMessage? response = await _handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
        if (response is JsonRpcError error)
        {
            return $"已阻断 Claude 原生编辑。Host 已尝试转发到 MCP {patchCall.ToolName}，但调用失败：{error.Error.Message}。请改用 {patchCall.ToolName} 正规重试。";
        }

        if (response is not JsonRpcResponse rpcResponse || rpcResponse.Result == null)
        {
            return $"已阻断 Claude 原生编辑。Host 已尝试转发到 MCP {patchCall.ToolName}，但未取得有效返回。请改用 {patchCall.ToolName} 正规重试。";
        }

        (bool isError, string text) = ReadToolResult(rpcResponse);
        if (isError)
        {
            return $"已阻断 Claude 原生编辑。Host 已转发到 MCP {patchCall.ToolName}，但 patch 未通过：{ExtractMessages(text)}。请改用 {patchCall.ToolName} 按反馈重试。";
        }

        string reason = $"已通过 MCP {patchCall.ToolName} 完成本次 Claude 原生编辑请求，已阻断原工具调用以避免重复写入。请读取目标视图确认修改。";
        if (text != "{}")
            reason += $" MCP 返回：{ExtractMessages(text)}";
        return reason;
    }

    private static (bool IsError, string Text) ReadToolResult(JsonRpcResponse response)
    {
        JsonNode resultNode = response.Result ?? throw new InvalidOperationException("MCP response result is null");
        JsonElement root = JsonSerializer.Deserialize<JsonElement>(resultNode.ToJsonString(), McpJsonOptions.Standard);
        bool isError = false;
        if (root.TryGetProperty("isError", out JsonElement isErrorElement) && isErrorElement.ValueKind == JsonValueKind.True)
            isError = true;

        string text = "{}";
        if (root.TryGetProperty("content", out JsonElement contentElement)
            && contentElement.ValueKind == JsonValueKind.Array
            && contentElement.GetArrayLength() > 0)
        {
            JsonElement first = contentElement[0];
            if (first.ValueKind == JsonValueKind.Object
                && first.TryGetProperty("text", out JsonElement textElement)
                && textElement.ValueKind == JsonValueKind.String)
            {
                text = textElement.GetString() ?? throw new InvalidOperationException("GetString returned null for String value");
            }
        }

        return (isError, text);
    }

    private static string ExtractMessages(string text)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return text;
        }

        JsonArray? messages = root?["messages"]?.AsArray();
        if (messages == null)
            return text;

        List<string> lines = new();
        foreach (JsonNode? message in messages)
        {
            if (message != null)
                lines.Add(message.GetValue<string>());
        }
        return string.Join("\n", lines);
    }

    private static JsonObject Deny(string reason)
    {
        return new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PreToolUse",
                ["permissionDecision"] = "deny",
                ["permissionDecisionReason"] = reason
            }
        };
    }

    private sealed record PatchCall(string ToolName, JsonObject Arguments);

    private sealed record PatchCreateResult(PatchCall? PatchCall, string? DenyReason)
    {
        public static readonly PatchCreateResult None = new(null, null);
    }
}
