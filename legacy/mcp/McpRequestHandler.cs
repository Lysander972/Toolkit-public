using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Toolkit.Core.Infra;

using Toolkit.Core.Persistence;

namespace Toolkit.Mcp;

public sealed record McpHostStateSnapshot(
    string Status,
    string Message,
    IReadOnlyList<string> BlockedFiles
);

/// <summary>
/// MCP JSON-RPC 请求处理器
/// 处理 initialize、tools/list、tools/call 等 MCP 协议方法
/// </summary>
public sealed class McpRequestHandler : IDisposable
{
    private static long _toolCallLogSequence;
    private readonly McpToolRegistry _toolRegistry;
    private readonly McpResourceRegistry _resourceRegistry;
    private readonly Func<McpHostStateSnapshot> _getHostState;
    private readonly Action? _waitForReady;
    private bool _disposed;

    public McpToolRegistry ToolRegistry => _toolRegistry;
    public McpResourceRegistry ResourceRegistry => _resourceRegistry;

    /// <summary>
    /// 服务端支持的协议版本列表，按优先级降序排列
    /// </summary>
    private static readonly string[] SupportedVersions = ["2025-06-18", "2025-03-26"];

    private static readonly string LatestVersion = SupportedVersions[0];
    private const string ServerName = "mech-toolkit";
    private const string ServerVersion = "0.1.0";
    private const string ToolCallPrefix = "[TOOL_CALL]";

    /// <summary>
    /// 门禁拦截范围：仅这些方法的数据依赖 <see cref="McpViewWorkspace"/> 的同步状态。
    /// 判据是"请求的数据来源是否依赖 IsSynchronized"，而非方法名黑名单：
    ///   - tools/list、resources/list、resources/templates/list、prompts/list、prompts/get
    ///     返回的是构造期一次性填充的静态元数据（_tools / _resources / 空数组），与视图是否同步无关；
    ///   - initialize / notifications/initialized 是生命周期方法，本身在 Host 就绪前发生；
    ///   这些方法必须无条件立即返回，让它们等初始化完成纯属空转。
    ///   仅 tools/call（handler 会读写视图）与 resources/read（读操作，保守纳入）需要状态就绪。
    /// </summary>
    private static readonly HashSet<string> StateSensitiveMethods = new(StringComparer.Ordinal)
    {
        "tools/call",
        "resources/read"
    };

    public McpRequestHandler(
        McpToolRegistry toolRegistry,
        McpResourceRegistry resourceRegistry,
        Func<McpHostStateSnapshot> getHostState,
        Action? waitForReady = null)
    {
        _toolRegistry = toolRegistry;
        _resourceRegistry = resourceRegistry;
        _getHostState = getHostState;
        _waitForReady = waitForReady;
    }

    public async Task<JsonRpcMessage?> HandleAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        // 创建运行时错误收集器，关联请求ID
        string operationId = request.Id.ToString();
        RuntimeErrorCollector errorCollector = new RuntimeErrorCollector(operationId, $"mcp:{request.Method}");
        using RuntimeErrorScope _ = RuntimeErrorContext.SetCollector(errorCollector);

        try
        {
            // 门禁只拦截状态敏感方法（见 StateSensitiveMethods）。
            // 静态元数据查询的数据在构造期已填充，与 ViewWorkspace 是否同步无关，放行立即返回，
            // 避免 initializing 期间被 _waitForReady 空转阻塞。
            if (StateSensitiveMethods.Contains(request.Method))
            {
                McpHostStateSnapshot hostState = _getHostState();
                if (hostState.Status == "blocked")
                {
                    return CreateErrorResponse(request.Id, McpErrorCode.InternalError, BuildHostUnavailableMessage(hostState));
                }
                if (hostState.Status == "error")
                {
                    return CreateErrorResponse(request.Id, McpErrorCode.InternalError, BuildHostUnavailableMessage(hostState));
                }
                if (hostState.Status == "initializing")
                {
                    _waitForReady?.Invoke();
                    // 等待后重新检查状态（可能已变为 blocked 或其他异常）
                    hostState = _getHostState();
                    if (hostState.Status != "ready")
                    {
                        return CreateErrorResponse(request.Id, McpErrorCode.InternalError, BuildHostUnavailableMessage(hostState));
                    }
                }
            }

            return request.Method switch
            {
                "initialize" => HandleInitialize(request),
                "notifications/initialized" => null, // 通知无需响应
                "tools/list" => HandleToolsList(request),
                "tools/call" => await HandleToolsCallAsync(request, errorCollector, cancellationToken).ConfigureAwait(false),
                "prompts/list" => new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new ListPromptsResult { Prompts = Array.Empty<Prompt>() }, McpJsonOptions.Output)
                        ?? throw new InvalidOperationException("Failed to serialize response")
                },
                "prompts/get" => HandlePromptsGet(request),
                "resources/list" => HandleResourcesList(request),
                "resources/templates/list" => HandleResourceTemplatesList(request),
                "resources/read" => HandleResourceRead(request),
                // JSON-RPC spec: 未知 method 返回 -32601 (MethodNotFound)
                _ => CreateErrorResponse(request.Id, McpErrorCode.MethodNotFound, $"Method not found: {request.Method}")
            };
        }
        catch (Exception ex) when (ex is WorkspaceFileBlockedException or WorkspaceStateChangedException)
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InternalError, BuildHostUnavailableMessage(_getHostState()));
        }
        catch (OperationCanceledException)
        {
            // 客户端取消（RequestAborted）属正常生命周期，非协议错误：重新抛出交 transport，不记 errorCollector。
            throw;
        }
        catch (Exception ex) // [Fallback-Checked] 协议层异常处理，返回 JSON-RPC 错误
        {
            // 收集运行时错误
            if (!errorCollector.HasErrors)
            {
                errorCollector.AddException(ex);
            }

            return CreateErrorResponse(request.Id, McpErrorCode.InternalError, FormatExceptionChain(ex));
        }
    }

    private JsonRpcMessage HandleInitialize(JsonRpcRequest request)
    {
        if (request.Params == null)
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, "Missing params");
        }

        InitializeRequestParams? initParams;
        try
        {
            initParams = JsonSerializer.Deserialize<InitializeRequestParams>(request.Params.ToJsonString(), McpJsonOptions.Standard);
        }
        catch (JsonException ex) // [Fallback-Checked] 初始化参数无法解析时返回协议错误
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, $"Invalid params: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(initParams?.ProtocolVersion))
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, "Missing protocolVersion");
        }

        // MCP spec "Lifecycle > Version negotiation":
        //   支持客户端版本则返回相同版本，否则返回服务端支持的最新版本，由客户端决定是否断开
        //   https://modelcontextprotocol.io/specification/2025-03-26/basic/lifecycle
        string negotiatedVersion = LatestVersion;
        if (SupportedVersions.Contains(initParams.ProtocolVersion))
            negotiatedVersion = initParams.ProtocolVersion;

        InitializeResult result = new InitializeResult
        {
            ProtocolVersion = negotiatedVersion,
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability { ListChanged = false },
                Prompts = new PromptsCapability { ListChanged = false },
                Resources = new ResourcesCapability { ListChanged = false }
            },
            ServerInfo = new Implementation { Name = ServerName, Version = ServerVersion }
        };

        JsonNode resultNode = JsonSerializer.SerializeToNode(result, McpJsonOptions.Output)
            ?? throw new InvalidOperationException("Failed to serialize response");
        return new JsonRpcResponse { Id = request.Id, Result = resultNode };
    }

    private JsonRpcMessage HandlePromptsGet(JsonRpcRequest request)
    {
        if (request.Params == null)
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, "Missing params");
        }

        GetPromptRequestParams? getParams;
        try
        {
            getParams = JsonSerializer.Deserialize<GetPromptRequestParams>(request.Params.ToJsonString(), McpJsonOptions.Standard);
        }
        catch (JsonException ex) // [Fallback-Checked] JSON 解析失败返回协议错误
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, $"Invalid params: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(getParams?.Name))
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, "Missing prompt name");
        }

        return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, $"Prompt not found: {getParams.Name}");
    }

#pragma warning disable TK006 // RPC 分发方法，各自处理一种协议路由
    private JsonRpcMessage HandleToolsList(JsonRpcRequest request)
#pragma warning restore TK006
    {
        IList<Tool> tools = _toolRegistry.GetAllDefinitions();
        ListToolsResult result = new ListToolsResult { Tools = tools };
        JsonNode resultNode = JsonSerializer.SerializeToNode(result, McpJsonOptions.Output)
            ?? throw new InvalidOperationException("Failed to serialize response");
        return new JsonRpcResponse { Id = request.Id, Result = resultNode };
    }

#pragma warning disable TK006 // RPC 分发方法
    private JsonRpcMessage HandleResourcesList(JsonRpcRequest request)
#pragma warning restore TK006
    {
        IList<Resource> resources = _resourceRegistry.ListResources();
        ListResourcesResult result = new ListResourcesResult { Resources = resources };
        JsonNode resultNode = JsonSerializer.SerializeToNode(result, McpJsonOptions.Output)
            ?? throw new InvalidOperationException("Failed to serialize response");
        return new JsonRpcResponse { Id = request.Id, Result = resultNode };
    }

#pragma warning disable TK006 // RPC 分发方法
    private JsonRpcMessage HandleResourceTemplatesList(JsonRpcRequest request)
#pragma warning restore TK006
    {
        IList<ResourceTemplate> resourceTemplates = _resourceRegistry.ListResourceTemplates();
        ListResourceTemplatesResult result = new ListResourceTemplatesResult { ResourceTemplates = resourceTemplates };
        JsonNode resultNode = JsonSerializer.SerializeToNode(result, McpJsonOptions.Output)
            ?? throw new InvalidOperationException("Failed to serialize response");
        return new JsonRpcResponse { Id = request.Id, Result = resultNode };
    }

    private JsonRpcMessage HandleResourceRead(JsonRpcRequest request)
    {
        if (request.Params == null)
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, "Missing params");
        }

        McpResourceReadParams? readParams;
        try
        {
            readParams = JsonSerializer.Deserialize<McpResourceReadParams>(request.Params.ToJsonString(), McpJsonOptions.Standard);
        }
        catch (JsonException ex) // [Fallback-Checked] JSON 解析失败返回协议错误
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, $"Invalid params: {ex.Message}");
        }

        if (readParams is null || readParams.Uri.All(char.IsWhiteSpace))
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, "Missing resource uri");
        }

        ResourceContents? content = _resourceRegistry.TryReadResource(readParams.Uri);
        if (content is null)
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, $"Resource not found: {readParams.Uri}");
        }

        ReadResourceResult result = new ReadResourceResult { Contents = new[] { content } };
        JsonNode resultNode = JsonSerializer.SerializeToNode(result, McpJsonOptions.Output)
            ?? throw new InvalidOperationException("Failed to serialize response");
        return new JsonRpcResponse { Id = request.Id, Result = resultNode };
    }

    private async Task<JsonRpcMessage> HandleToolsCallAsync(JsonRpcRequest request, RuntimeErrorCollector errorCollector, CancellationToken cancellationToken)
    {
        if (request.Params == null)
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, "Missing params");
        }

        McpToolCallParams? callParams;
        try
        {
            callParams = JsonSerializer.Deserialize<McpToolCallParams>(request.Params.ToJsonString(), McpJsonOptions.Standard);
        }
        catch (JsonException ex) // [Fallback-Checked] JSON 解析失败返回协议错误
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, $"Invalid params: {ex.Message}");
        }

        if (callParams is null || callParams.Name.All(char.IsWhiteSpace))
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, "Missing tool name");
        }

        // MCP spec 2025-03-26 "Server > Tools > Error handling":
        //   未知工具应返回 -32602 (InvalidParams)，示例: "Unknown tool: invalid_tool_name"
        //   https://modelcontextprotocol.io/specification/2025-03-26/server/tools
        Func<JsonElement?, CancellationToken, Task<object?>>? handler = _toolRegistry.TryGetHandler(callParams.Name);
        if (handler == null)
        {
            return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, $"Unknown tool: {callParams.Name}");
        }

        // Schema 验证：在调用 handler 前校验参数格式
        InputSchema? schema = _toolRegistry.TryGetSchema(callParams.Name);
        if (schema != null)
        {
            List<string> validationErrors = McpToolRegistry.ValidateArguments(schema, callParams.Arguments);
            if (validationErrors.Count > 0)
            {
                string errorMessage = string.Join("; ", validationErrors);
                return CreateErrorResponse(request.Id, McpErrorCode.InvalidParams, errorMessage);
            }
        }

        Log log = Log.For(LogModule.Mcp);
        string toolName = callParams.Name;
        long toolLogId = Interlocked.Increment(ref _toolCallLogSequence);

        // 序列化请求参数（用于日志）
        string argsJson = JsonSerializer.Serialize(callParams.Arguments, McpJsonOptions.Output);
        try
        {
            log.Info($"调用工具: id={toolLogId} tool={toolName} args={argsJson}");
            object? result = await handler(callParams.Arguments, cancellationToken).ConfigureAwait(false);
            bool patchMatched = GetPatchMatched(result);
            (string payloadText, bool isError) = BuildToolContent(result);
            CallToolResult callResult = new CallToolResult
            {
                Content = new ContentBlock[] { new TextContentBlock { Text = payloadText } },
                IsError = isError
            };

            string resultJson = JsonSerializer.Serialize(callResult, McpJsonOptions.Output);
            string stackTraceText = string.Empty;
            if (isError && result is PatchToolResponse toolResponse)
                stackTraceText = string.Join("\n---\n", toolResponse.Errors.Select(static error => StackTraceFormatter.ExtractFirstFiveFrames(error.StackTrace)));

            string statusText = "ok";
            if (isError)
                statusText = "error";
            WriteToolCallMessage(toolLogId, toolName, statusText, patchMatched, argsJson, resultJson, stackTraceText);

            JsonNode resultNode = JsonSerializer.SerializeToNode(callResult, McpJsonOptions.Output)
                ?? throw new InvalidOperationException("Failed to serialize response");
            return new JsonRpcResponse { Id = request.Id, Result = resultNode };
        }
        catch (Exception ex) when (ex is WorkspaceFileBlockedException or WorkspaceStateChangedException)
        {
            string errorText = BuildHostUnavailableMessage(_getHostState());
            CallToolResult callResult = new CallToolResult
            {
                Content = new ContentBlock[] { new TextContentBlock { Text = errorText } },
                IsError = true
            };
            string resultJson = JsonSerializer.Serialize(callResult, McpJsonOptions.Output);
            WriteToolCallMessage(toolLogId, toolName, "error", false, argsJson, resultJson, string.Empty);
            return CreateErrorResponse(request.Id, McpErrorCode.InternalError, errorText);
        }
        catch (OperationCanceledException)
        {
            // 客户端取消（RequestAborted）属正常生命周期，非工具错误：重新抛出交 transport，不记 errorCollector。
            throw;
        }
        catch (Exception ex)
        {
            string errorText;
            if (ex is AggregateException aggEx && aggEx.InnerExceptions.Count > 0)
            {
                errorText = string.Join("; ", aggEx.InnerExceptions.Select(static inner => $"[{inner.GetType().Name}] {inner.Message}"));
            }
            else
            {
                errorText = FormatExceptionChain(ex);
            }
            CallToolResult callResult = new CallToolResult
            {
                Content = new ContentBlock[] { new TextContentBlock { Text = errorText } },
                IsError = true
            };
            string resultJson = JsonSerializer.Serialize(callResult, McpJsonOptions.Output);

            WriteToolCallMessage(toolLogId, toolName, "error", false, argsJson, resultJson, StackTraceFormatter.ExtractFirstFiveFrames(ex.StackTrace));

            // 收集运行时错误（会自动输出到 Log）
            errorCollector.AddException(ex);

            JsonNode resultNode = JsonSerializer.SerializeToNode(callResult, McpJsonOptions.Output)
                ?? throw new InvalidOperationException("Failed to serialize response");
            return new JsonRpcResponse { Id = request.Id, Result = resultNode };
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _toolRegistry.Dispose();
    }

#pragma warning disable TK006 // 标准 Dispose 模式方法
    private void ThrowIfDisposed()
#pragma warning restore TK006
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(McpRequestHandler));
        }
    }

    private static (string Text, bool IsError) BuildToolContent(object? result)
    {
        if (result == null)
        {
            throw new InvalidOperationException("工具返回值非法：不允许 null，必须返回标准响应对象 {errors, warnings, data?}");
        }

        if (result is PatchToolResponse response)
        {
            bool hasErrors = response.Errors.Count > 0;
            bool hasWarnings = response.Warnings.Count > 0;
            if (!hasErrors)
            {
                Dictionary<string, JsonElement> flattened = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

                if (response.Data is not null)
                {
                    JsonElement dataProp = JsonSerializer.SerializeToElement(response.Data, McpJsonOptions.Output);
                    if (dataProp.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty prop in dataProp.EnumerateObject())
                        {
                            flattened[prop.Name] = prop.Value;
                        }
                    }
                    else if (dataProp.ValueKind != JsonValueKind.Undefined && dataProp.ValueKind != JsonValueKind.Null)
                    {
                        // 非对象数据用 value 承载，避免额外层级 data: {...}
                        flattened["value"] = dataProp;
                    }
                }

                List<string> allMessages = [];
                if (response.Infos.Count > 0)
                {
                    allMessages.AddRange(FlattenToolErrors(response.Infos));
                }
                if (hasWarnings)
                {
                    allMessages.AddRange(FlattenToolErrors(response.Warnings));
                }
                if (allMessages.Count > 0)
                {
                    flattened["messages"] = JsonSerializer.SerializeToElement(allMessages, McpJsonOptions.Output);
                }

                if (flattened.Count == 0)
                {
                    return ("{}", false);
                }

                if (flattened.Count == 1 && flattened.ContainsKey("value") && !hasWarnings)
                {
                    string primitiveText = TextLineEndings.ToLf(JsonSerializer.Serialize(flattened["value"], McpJsonOptions.Output));
                    return (primitiveText, false);
                }

                JsonElement flattenedElement = JsonSerializer.SerializeToElement(flattened, McpJsonOptions.Output);
                string text = TextLineEndings.ToLf(JsonSerializer.Serialize(flattenedElement, McpJsonOptions.Output));
                return (text, false);
            }

            List<string> failedMessages = [];
            failedMessages.AddRange(FlattenToolErrors(response.Errors));
            if (hasWarnings)
            {
                failedMessages.AddRange(FlattenToolErrors(response.Warnings));
            }

            Dictionary<string, object?> errorObj = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["messages"] = failedMessages
            };

            JsonElement errorElement = JsonSerializer.SerializeToElement(errorObj, McpJsonOptions.Output);
            string errorText = TextLineEndings.ToLf(JsonSerializer.Serialize(errorElement, McpJsonOptions.Output));
            return (errorText, true);
        }

        throw new InvalidOperationException($"工具返回值非法：必须为 {nameof(PatchToolResponse)}");
    }

    private static List<string> FlattenToolErrors(IReadOnlyList<ToolError> errors)
    {
        List<string> result = [];
        foreach (ToolError error in errors)
        {
            string severity = error.Severity.ToString();
            SourceLocation loc = new();
            if (error.Path is not null) loc = loc with { Path = error.Path };
            if (error.Line is not null) loc = loc with { Line = error.Line.Value };
            if (error.Column is not null) loc = loc with { Column = error.Column.Value };
            string location = loc.Format();
            string prefix = $"[{severity}]";
            if (location.Length > 0)
                prefix = $"[{severity}] {location}:";
            result.Add($"{prefix} {error.Message}");

#pragma warning disable TK022 // ExtraMessages 是诊断模型可选字段，null 表示无附加消息
            if (error.ExtraMessages is not null)
#pragma warning restore TK022
            {
                foreach (string extra in error.ExtraMessages)
                {
                    result.Add($"[{severity}] {extra}");
                }
            }
        }
        return result;
    }

#pragma warning disable TK006 // 工具调用结果类型判断
    private static bool GetPatchMatched(object? result)
#pragma warning restore TK006
    {
        return result is PatchToolResponse patchResponse && patchResponse.PatchMatched;
    }

    /// <summary>
    /// 格式化异常为 "[Type] Message -> [InnerType] InnerMessage"，展开一层 InnerException。
    /// 兜底 catch 用此格式避免丢失 CDP 等场景下被包装的底层异常信息。
    /// </summary>
    internal static string FormatExceptionChain(Exception ex)
    {
        string text = $"[{ex.GetType().Name}] {ex.Message}";
        if (ex.InnerException != null)
            text += $" -> [{ex.InnerException.GetType().Name}] {ex.InnerException.Message}";
        return text;
    }

    /// <summary>
    /// 以结构化 JSON 格式通过 stderr 输出工具调用结果。
    /// 与 [PROJECT_TREE] 等结构化消息走同一通道，前端 tryParsePushMessage 识别前缀后分发。
    /// 日志文件写入由前端 handleToolCallMessage 负责。
    /// </summary>
    private static void WriteToolCallMessage(long id, string tool, string status, bool patchMatched, string argsJson, string resultJson, string stackTraceText)
    {
        Dictionary<string, object> payload = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["id"] = id.ToString(),
            ["tool"] = tool,
            ["status"] = status,
            ["patchMatched"] = patchMatched,
            ["args"] = argsJson,
            ["output"] = resultJson
        };
        if (stackTraceText.Length > 0)
        {
            payload["stackTrace"] = stackTraceText;
        }
        string json = JsonSerializer.Serialize(payload, McpJsonOptions.Output);
        AsyncConsoleErrorWriter.WriteLine($"{ToolCallPrefix}{json}");
    }

    private static JsonRpcMessage CreateErrorResponse(RequestId id, McpErrorCode code, string message)
    {
        return new JsonRpcError { Id = id, Error = new JsonRpcErrorDetail { Code = (int)code, Message = message } };
    }

    private static string BuildHostUnavailableMessage(McpHostStateSnapshot hostState)
    {
        if (hostState.BlockedFiles.Count == 0)
        {
            return hostState.Message;
        }

        // 懒查占用进程（MCP 边界，不持 FileSystemCache 任何锁）。
        // 工程根可能未初始化（测试场景），未初始化 / 查询失败 / 无占用者均退化为纯受阻路径，保持既有行为。
        string? projectRootFullPath = null;
        FileSystemCache.ExecuteIfInitialized(
            cache => projectRootFullPath = cache.ProjectRootFullPath);

        List<string> lines = new(hostState.BlockedFiles.Count);
        foreach (string blockedRelativePath in hostState.BlockedFiles)
        {
            string lockers = string.IsNullOrEmpty(projectRootFullPath)
                ? string.Empty
                : FileLockProbe.DescribeLockers(string.Concat(projectRootFullPath, "/", blockedRelativePath));
#pragma warning disable TK008 // lockers 空串是 FileLockProbe 明确契约（无占用者），属业务分支非防御兜底
            lines.Add(lockers.Length == 0
                ? $"- {blockedRelativePath}"
                : $"- {blockedRelativePath}  ← 占用: {lockers}");
#pragma warning restore TK008
        }

        return $"{hostState.Message}\n受阻路径:\n{string.Join("\n", lines)}";
    }
}
