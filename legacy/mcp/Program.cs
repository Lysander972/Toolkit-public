using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;
using Toolkit.Core.Worker;

namespace Toolkit.Mcp;

public static class McpHostRunner
{
    private const string SsePath = "/sse";
    private const string MessagePathPrefix = "/sse/message/";
    private const string McpPath = "/mcp";
    private const string HostNegotiatePath = "/host/negotiate";
    private const string McpSessionIdHeader = "Mcp-Session-Id";
    private const string SessionPushPrefix = "[MCP_SESSIONS]";
    private const string ToolsPushPrefix = "[MCP_TOOLS]";
    private const string HostStatePushPrefix = "[MCP_HOST_STATE]";
    private const string SessionTransportSse = "sse";
    private const string SessionTransportHttp = "http";
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HttpResponseStallLogDelay = TimeSpan.FromSeconds(30);
    private static void DisposeHandler(McpRequestHandler handler)
    {
        try { handler.Dispose(); }
        catch
        {
            // 释放失败不应影响退出流程
        }
    }

    // 日志统一走控制台输出，由前端接收处理，无需显式关闭

    /// <summary>
    /// 启动 MCP 服务。
    /// 注意：依赖由外部传入，确保与 Host 共用同一套 Model 和 Logic 实例。
    /// </summary>
    /// <param name="args">命令行参数</param>
    /// <param name="services">核心服务门面（进程内唯一实例）</param>
    /// <param name="projectRootFullPath">工程根目录绝对路径</param>
    public static async Task RunAsync(string[] args, Core.ToolkitServices services, string projectRootFullPath)
    {
        Log.For(LogModule.Mcp).Info("MCP 服务启动中...");
        // 内容版本未变（无影响刷新）时工程树必然相同，跳过数 MB 级序列化与推送。
        // 门控状态用闭包局部量：同一进程可存在多个 MCP 实例（测试），禁止静态共享。
        // 初始值取当前版本：启动推送（RunSseAsync）无条件执行，覆盖此前的全部状态。
        long lastPushedTreeVersion = services.Model.RefreshVersion;
        void PushProjectTreeIfChanged()
        {
            if (services.Model.RefreshVersion == lastPushedTreeVersion)
            {
                return;
            }
            PushProjectTree(services);
            lastPushedTreeVersion = services.Model.RefreshVersion;
        }
        services.Model.ModelRefreshed += (sender, e) => PushProjectTreeIfChanged();
        McpViewWorkspace viewWorkspace = new McpViewWorkspace(services);
        services.Model.WorkspaceBlockStateChanged += (sender, e) => PushHostState(BuildHostState(services.Model, viewWorkspace));
        viewWorkspace.SynchronizationStateChanged += (sender, e) => PushHostState(BuildHostState(services.Model, viewWorkspace));

        Log.For(LogModule.Mcp).Info("构建 MCP 请求处理器...");
        ResourceGenScheduler? resourceScheduler = BuildResourceGenScheduler(args);
        McpRequestHandler mcpHandler = new McpRequestHandler(
            new McpToolRegistry(services, projectRootFullPath, viewWorkspace, resourceScheduler: resourceScheduler),
            new McpResourceRegistry(),
            () => BuildHostState(services.Model, viewWorkspace),
            () => viewWorkspace.WaitUntilSynchronized(() => { }));
        int port = ResolvePort(args);
        string listenPrefix = $"http://127.0.0.1:{port}";
        Log.For(LogModule.Mcp).Info($"MCP 监听地址: {listenPrefix}");

        await RunSseAsync(mcpHandler, listenPrefix, services, projectRootFullPath, viewWorkspace, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task RunSseAsync(
        McpRequestHandler handler,
        string listenPrefix,
        Core.ToolkitServices services,
        string projectRootFullPath,
        McpViewWorkspace viewWorkspace,
        CancellationToken cancellationToken)
    {
        ConcurrentDictionary<string, SseClient> clients = new(StringComparer.Ordinal);
        ConcurrentDictionary<string, int> httpSessions = new(StringComparer.Ordinal);
        ConcurrentDictionary<string, int> sseFailureCounts = new(StringComparer.Ordinal);
        ConcurrentDictionary<string, SessionMonitor> sessionMonitors = new(StringComparer.Ordinal);

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(listenPrefix);
        // 默认不启用 ASP.NET Core 控制台日志：避免混入错误流/刷屏；必要信息由本模块自行输出。
        builder.Logging.ClearProviders();

        WebApplication app = builder.Build();
        app.Lifetime.ApplicationStopping.Register(() => DisposeHandler(handler));

        app.MapGet(SsePath, async context =>
        {
            await HandleSseAsync(context, clients, sseFailureCounts, sessionMonitors, cancellationToken).ConfigureAwait(false);
        });

        app.MapPost($"{MessagePathPrefix}{{sessionId}}", async context =>
        {
            if (context.Request.RouteValues["sessionId"] is not string sessionId)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await HandleMessageAsync(context, sessionId, handler, clients, sseFailureCounts, sessionMonitors, cancellationToken).ConfigureAwait(false);
        });

        app.MapPost(McpPath, async context =>
        {
            await HandleMcpStreamableHttpAsync(context, handler, httpSessions, sessionMonitors, cancellationToken).ConfigureAwait(false);
        });

        app.MapGet(McpPath, HandleMcpGetStreamableHttpAsync);

        app.MapDelete(McpPath, context => HandleMcpDeleteSessionAsync(context, httpSessions, sessionMonitors));

        LspRequestHandler lspHandler = new LspRequestHandler(services);
        app.MapPost("/lsp", async context =>
        {
            await HandleLspRequestAsync(context, lspHandler).ConfigureAwait(false);
        });

        SdkContextService sdkContextService = new(services, projectRootFullPath);
        app.MapPost("/sdk-context", async context =>
        {
            SdkContextRequest? req = await context.Request.ReadFromJsonAsync<SdkContextRequest>().ConfigureAwait(false);
            if (req == null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            SdkContextResponse resp = sdkContextService.GetContext(req);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(resp).ConfigureAwait(false);
        });

        app.MapPost("/sdk-context/hook/{agent}", async context =>
        {
            string raw;
            using (StreamReader reader = new(context.Request.Body, TextEncoding.Utf8, false, leaveOpen: true))
                raw = await reader.ReadToEndAsync().ConfigureAwait(false);

            string? agent = context.Request.RouteValues["agent"]?.ToString();
            if (agent is null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            SdkContextHookResponse response = sdkContextService.BuildHookResponse(agent, raw);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = response.ContentType;
            await context.Response.WriteAsync(response.Body).ConfigureAwait(false);
        });

        GuardService guardService = new();
        HookPatchService hookPatchService = new(handler, guardService, projectRootFullPath, viewWorkspace);
        app.MapPost("/guard", async context =>
        {
            GuardRequest? req = await context.Request.ReadFromJsonAsync<GuardRequest>().ConfigureAwait(false);
            if (req == null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            GuardResponse resp = guardService.Check(req);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(resp).ConfigureAwait(false);
        });

        app.MapPost("/guard/claude-hook", async context =>
        {
            string raw;
            using (StreamReader reader = new(context.Request.Body, TextEncoding.Utf8, false, leaveOpen: true))
                raw = await reader.ReadToEndAsync().ConfigureAwait(false);
            JsonObject response = await hookPatchService.HandleAsync("claude", raw, cancellationToken).ConfigureAwait(false);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(response).ConfigureAwait(false);
        });

        app.MapPost("/guard/claude", async context =>
        {
            string raw;
            using (StreamReader reader = new(context.Request.Body, TextEncoding.Utf8, false, leaveOpen: true))
                raw = await reader.ReadToEndAsync().ConfigureAwait(false);
            GuardResponse resp = guardService.Check(new GuardRequest("claude", raw));
            if (resp.Allow)
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync("ALLOW").ConfigureAwait(false);
                return;
            }
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync(resp.Reason).ConfigureAwait(false);
        });

        app.MapPost("/guard/cline", async context =>
        {
            string raw;
            using (StreamReader reader = new(context.Request.Body, TextEncoding.Utf8, false, leaveOpen: true))
                raw = await reader.ReadToEndAsync().ConfigureAwait(false);
            GuardResponse resp = guardService.Check(new GuardRequest("cline", raw));
            if (resp.Allow)
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync("ALLOW").ConfigureAwait(false);
                return;
            }
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync(resp.Reason).ConfigureAwait(false);
        });

        app.MapPost("/guard/crush", async context =>
        {
            string raw;
            using (StreamReader reader = new(context.Request.Body, TextEncoding.Utf8, false, leaveOpen: true))
                raw = await reader.ReadToEndAsync().ConfigureAwait(false);
            GuardResponse resp = guardService.Check(new GuardRequest("crush", raw));
            if (resp.Allow)
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync("ALLOW").ConfigureAwait(false);
                return;
            }
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync(resp.Reason).ConfigureAwait(false);
        });

        // /host/negotiate:无副作用身份查询,供新旧形态协商复用同一 Host 实例。
        // 占用者工作区(来自进程 cwd 解析的 projectRootFullPath)与请求方一致才复用。
        app.MapGet(HostNegotiatePath, () => Results.Json(new HostNegotiationResponse(
            projectRootFullPath,
            HostIdentity.InstanceId,
            HostIdentity.Version,
            Environment.ProcessId,
            AcceptReuse: true)));

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        Log.For(LogModule.Mcp).Info("服务器已启动");
        PushMcpTools(handler);
        PushProjectTree(services);
        PushHostState(BuildHostState(services.Model, viewWorkspace));
        PushMcpSessions(sessionMonitors);
        await app.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
        Log.For(LogModule.Mcp).Info("服务器已关闭");

        DisposeHandler(handler);
    }

    private static async Task HandleSseAsync(
        HttpContext context,
        ConcurrentDictionary<string, SseClient> clients,
        ConcurrentDictionary<string, int> failureCounts,
        ConcurrentDictionary<string, SessionMonitor> sessionMonitors,
        CancellationToken cancellationToken)
    {
        HttpResponse response = context.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.RequestAborted);
        await response.StartAsync(linked.Token).ConfigureAwait(false);

        StreamWriter writer = new(response.Body, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true)
        {
            AutoFlush = false,
            NewLine = "\n"
        };

        SseClient client = new(writer);
        clients[client.Id] = client;
        failureCounts[client.Id] = 0;
        sessionMonitors[client.Id] = new SessionMonitor(client.Id, SessionTransportSse);
        PushMcpSessions(sessionMonitors);

        string host = context.Request.Host.Value ?? throw new InvalidOperationException("Request Host not available");
        string endpoint = $"{context.Request.Scheme}://{host}{MessagePathPrefix}{client.Id}";
        if (!await client.SendEventAsync("endpoint", endpoint, linked.Token).ConfigureAwait(false))
        {
            clients.TryRemove(client.Id, out _);
            failureCounts.TryRemove(client.Id, out _);
            sessionMonitors.TryRemove(client.Id, out _);
            PushMcpSessions(sessionMonitors);
            return;
        }

        await KeepAliveAsync(client, linked.Token).ConfigureAwait(false);
        clients.TryRemove(client.Id, out _);
        failureCounts.TryRemove(client.Id, out _);
        sessionMonitors.TryRemove(client.Id, out _);
        PushMcpSessions(sessionMonitors);
    }

    private static async Task KeepAliveAsync(SseClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await Task.Delay(HeartbeatInterval, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) // [Fallback-Checked] 取消等待，正常退出循环
            { break; }

            if (!await client.SendCommentAsync("keep-alive", cancellationToken).ConfigureAwait(false))
                break;
        }
    }

    private static async Task HandleMessageAsync(
        HttpContext context,
        string sessionId,
        McpRequestHandler handler,
        ConcurrentDictionary<string, SseClient> clients,
        ConcurrentDictionary<string, int> failureCounts,
        ConcurrentDictionary<string, SessionMonitor> sessionMonitors,
        CancellationToken cancellationToken)
    {
        if (!clients.TryGetValue(sessionId, out SseClient? client))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        string payload;
        using (StreamReader reader = new(context.Request.Body, TextEncoding.Utf8, false, leaveOpen: true))
            payload = await reader.ReadToEndAsync().ConfigureAwait(false);

#pragma warning disable TK008 // MCP 边界：HTTP 请求体允许为空，需要返回协议错误
        if (payload.Length == 0)
#pragma warning restore TK008
        {
            await SendRpcResponseAsync(client,
                CreateJsonRpcError(null, McpErrorCode.InvalidRequest, "Empty request body"),
                cancellationToken).ConfigureAwait(false);
            context.Response.StatusCode = StatusCodes.Status200OK;
            return;
        }

        JsonRpcRequest? request;
        try { request = JsonSerializer.Deserialize<JsonRpcRequest>(payload, McpJsonOptions.Standard); }
        catch (JsonException ex)
        {
            await SendRpcResponseAsync(client,
                CreateJsonRpcError(null, McpErrorCode.ParseError, $"Parse error: {ex.Message}"),
                cancellationToken).ConfigureAwait(false);
            context.Response.StatusCode = StatusCodes.Status200OK;
            return;
        }

        if (request == null)
        {
            await SendRpcResponseAsync(client,
                CreateJsonRpcError(request?.Id, McpErrorCode.InvalidRequest, "Invalid request"),
                cancellationToken).ConfigureAwait(false);
            context.Response.StatusCode = StatusCodes.Status200OK;
            return;
        }

        JsonRpcMessage? response = await handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
        if (response != null)
            response = ProcessFailureAdvisories(response, request, failureCounts, sessionMonitors, sessionId);

        if (request.Id.Id != null && response != null)
        {
            await SendRpcResponseAsync(client, response, cancellationToken).ConfigureAwait(false);
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
    }

    private static async Task HandleMcpStreamableHttpAsync(
        HttpContext context,
        McpRequestHandler handler,
        ConcurrentDictionary<string, int> httpSessions,
        ConcurrentDictionary<string, SessionMonitor> sessionMonitors,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.RequestAborted);
        string? sessionId = TryGetSessionIdFromHeaders(context.Request);

        string payload;
        using (StreamReader reader = new(context.Request.Body, TextEncoding.Utf8, false, leaveOpen: true))
            payload = await reader.ReadToEndAsync().ConfigureAwait(false);

#pragma warning disable TK008 // MCP 边界：HTTP 请求体允许为空，需要返回协议错误
        if (payload.Length == 0)
#pragma warning restore TK008
        {
            await WriteHttpJsonAsync(context,
                CreateJsonRpcError(null, McpErrorCode.InvalidRequest, "Empty request body"),
                linked.Token).ConfigureAwait(false);
            return;
        }

        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(payload, McpJsonOptions.Standard); }
        catch (JsonException ex) // [Fallback-Checked] JSON 解析失败返回协议错误
        {
            await WriteHttpJsonAsync(context,
                CreateJsonRpcError(null, McpErrorCode.ParseError, $"Parse error: {ex.Message}"),
                linked.Token).ConfigureAwait(false);
            return;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            await HandleBatchRequestAsync(context, root, handler, httpSessions, sessionMonitors, sessionId, linked.Token).ConfigureAwait(false);
            return;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            await WriteHttpJsonAsync(context,
                CreateJsonRpcError(null, McpErrorCode.InvalidRequest, "Invalid request"),
                linked.Token).ConfigureAwait(false);
            return;
        }

        await HandleSingleRequestAsync(context, root, handler, httpSessions, sessionMonitors, sessionId, linked.Token).ConfigureAwait(false);
    }

    private static Task HandleMcpGetStreamableHttpAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        context.Response.Headers.Append("Allow", "POST, DELETE");
        return Task.CompletedTask;
    }

    private static async Task HandleBatchRequestAsync(
        HttpContext context,
        JsonElement root,
        McpRequestHandler handler,
        ConcurrentDictionary<string, int> httpSessions,
        ConcurrentDictionary<string, SessionMonitor> sessionMonitors,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        List<JsonRpcMessage> responses = [];

        foreach (JsonElement item in root.EnumerateArray())
        {
            JsonRpcRequest? request;
            try { request = JsonSerializer.Deserialize<JsonRpcRequest>(item.GetRawText(), McpJsonOptions.Standard); }
            catch (JsonException ex) // [Fallback-Checked] JSON 解析失败返回协议错误
            {
                JsonRpcMessage invalidRequest = CreateJsonRpcError(null, McpErrorCode.InvalidRequest, $"Invalid request: {ex.Message}");
                responses.Add(invalidRequest);
                continue;
            }

            if (request == null)
            {
                JsonRpcMessage invalidRequest = CreateJsonRpcError(null, McpErrorCode.InvalidRequest, "Invalid request");
                responses.Add(invalidRequest);
                continue;
            }

            // 前置拦截：解析确定性的 sessionId
            if (!string.Equals(request.Method, "initialize", StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(sessionId))
            {
                responses.Add(CreateJsonRpcError(request.Id, McpErrorCode.InvalidRequest, $"Missing {McpSessionIdHeader}"));
                continue;
            }

            string resolvedSessionId;
            if (sessionId != null)
                resolvedSessionId = sessionId;
            else
                resolvedSessionId = Guid.NewGuid().ToString("N");
            RegisterHttpSession(context, httpSessions, sessionMonitors, resolvedSessionId);

            JsonRpcMessage? response = await handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
            if (response != null)
                response = ProcessFailureAdvisories(response, request, httpSessions, sessionMonitors, resolvedSessionId);

            if (request.Id.Id != null && response != null)
                responses.Add(response);
        }

        if (responses.Count == 0)
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return;
        }

        string batchPayload = JsonSerializer.Serialize(responses, McpJsonOptions.Output);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.StartAsync(cancellationToken).ConfigureAwait(false);
        await context.Response.WriteAsync(batchPayload, cancellationToken).ConfigureAwait(false);
    }

    private static async Task HandleSingleRequestAsync(
        HttpContext context,
        JsonElement root,
        McpRequestHandler handler,
        ConcurrentDictionary<string, int> httpSessions,
        ConcurrentDictionary<string, SessionMonitor> sessionMonitors,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        JsonRpcRequest? request;
        try { request = JsonSerializer.Deserialize<JsonRpcRequest>(root.GetRawText(), McpJsonOptions.Standard); }
        catch (JsonException ex) // [Fallback-Checked] JSON 解析失败返回协议错误
        {
            await WriteHttpJsonAsync(context,
                CreateJsonRpcError(null, McpErrorCode.InvalidRequest, $"Invalid request: {ex.Message}"),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request == null)
        {
            await WriteHttpJsonAsync(context,
                CreateJsonRpcError(null, McpErrorCode.InvalidRequest, "Invalid request"),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        // 前置拦截：解析确定性的 sessionId
        if (!string.Equals(request.Method, "initialize", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(sessionId))
        {
            await WriteHttpJsonAsync(context,
                CreateJsonRpcError(request.Id, McpErrorCode.InvalidRequest, $"Missing {McpSessionIdHeader}"),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        string resolvedSessionId;
        if (sessionId != null)
            resolvedSessionId = sessionId;
        else
            resolvedSessionId = Guid.NewGuid().ToString("N");
        RegisterHttpSession(context, httpSessions, sessionMonitors, resolvedSessionId);

        JsonRpcMessage? response = await handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
        if (response != null)
            response = ProcessFailureAdvisories(response, request, httpSessions, sessionMonitors, resolvedSessionId);

        if (request.Id.Id == null || response == null)
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return;
        }

        await WriteHttpJsonAsync(context, response, cancellationToken).ConfigureAwait(false);
    }

    private static Task HandleMcpDeleteSessionAsync(
        HttpContext context,
        ConcurrentDictionary<string, int> httpSessions,
        ConcurrentDictionary<string, SessionMonitor> sessionMonitors)
    {
        string? rawSessionId = TryGetSessionIdFromHeaders(context.Request);
        if (string.IsNullOrWhiteSpace(rawSessionId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        }

        string sessionId = rawSessionId;
        if (!httpSessions.ContainsKey(sessionId))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        httpSessions.TryRemove(sessionId, out _);
        sessionMonitors.TryRemove(sessionId, out _);
        PushMcpSessions(sessionMonitors);
        context.Response.StatusCode = StatusCodes.Status200OK;
        return Task.CompletedTask;
    }

    private static void RegisterHttpSession(
        HttpContext context,
        ConcurrentDictionary<string, int> httpSessions,
        ConcurrentDictionary<string, SessionMonitor> sessionMonitors,
        string sessionId)
    {
        bool isNew = !httpSessions.ContainsKey(sessionId);
        if (isNew)
        {
            httpSessions.TryAdd(sessionId, 0);
            sessionMonitors.TryAdd(sessionId, new SessionMonitor(sessionId, SessionTransportHttp));
            PushMcpSessions(sessionMonitors);
        }

        context.Response.Headers[McpSessionIdHeader] = sessionId;
    }

    private static string? TryGetSessionIdFromHeaders(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(McpSessionIdHeader, out Microsoft.Extensions.Primitives.StringValues values))
            return null;
        return values[0];
    }

    private static void PushMcpSessions(
        ConcurrentDictionary<string, SessionMonitor> sessionMonitors)
    {
        List<SessionPushItem> items = [];

        foreach (KeyValuePair<string, SessionMonitor> entry in sessionMonitors.ToArray())
        {
            SessionMonitor monitor = entry.Value;
            items.Add(monitor.ToPushItem());
        }

        items.Sort(static (a, b) => string.Compare(a.SessionId, b.SessionId, StringComparison.Ordinal));
        string json = JsonSerializer.Serialize(items, McpJsonOptions.Output);
        AsyncConsoleErrorWriter.WriteLine($"{SessionPushPrefix}{json}");
    }

    internal static JsonRpcMessage ProcessFailureAdvisories(
        JsonRpcMessage response,
        JsonRpcRequest request,
        ConcurrentDictionary<string, int> failureCounts,
        ConcurrentDictionary<string, SessionMonitor> sessionMonitors,
        string sessionId)
    {
        // 非工具调用不计入失败计数
        string? toolName = TryGetToolName(request);
        if (toolName == null)
            return response;

        if (response is not JsonRpcResponse rpcResponse)
            return response;

        ToolCallResultPayload resultPayload =
            JsonSerializer.Deserialize<ToolCallResultPayload>(rpcResponse.Result, McpJsonOptions.Output)
            ?? throw new InvalidOperationException("tools/call response payload is invalid");
        if (!resultPayload.IsError)
        {
            failureCounts[sessionId] = 0;
            sessionMonitors[sessionId].RecordToolCall(toolName, false);
            PushMcpSessions(sessionMonitors);
            return response;
        }

        int failureCount = failureCounts.AddOrUpdate(sessionId, 1, static (_, c) => c + 1);

        List<string> advisories = [];
        if (failureCount > 1)
            advisories.Add(BuildGenericFailureAdvice(failureCount));

        int toolFailureCount = sessionMonitors[sessionId].RecordToolCall(toolName, true);
        if (toolFailureCount > 1)
            advisories.Add(BuildToolFailureAdvice(toolName, toolFailureCount));
        PushMcpSessions(sessionMonitors);

        if (advisories.Count == 0)
            return response;

        ToolTextPayload textPayload =
            JsonSerializer.Deserialize<ToolTextPayload>(resultPayload.Content[0].Text, McpJsonOptions.Output)
            ?? throw new InvalidOperationException("tools/call text payload is invalid");
        textPayload.Messages.Add(string.Join("\n", advisories));

        resultPayload.Content[0] = resultPayload.Content[0] with
        {
            Text = JsonSerializer.Serialize(textPayload, McpJsonOptions.Output)
        };
        rpcResponse.Result = JsonSerializer.SerializeToNode(resultPayload, McpJsonOptions.Output)
            ?? throw new InvalidOperationException("tools/call advisory response serialization failed");
        return response;
    }

    private sealed record ToolCallResultPayload(ToolTextContentPayload[] Content, bool IsError);

    private sealed record ToolTextContentPayload(string Type, string Text);

    private sealed record ToolTextPayload(List<string> Messages);

    private static string BuildGenericFailureAdvice(int failureCount)
        => failureCount switch
        {
            <= 3 => $"警告: 你已经连续失败{failureCount}次。先读这次返回的错误和参数名，不要只改几个值继续乱试。注意使用MCP仍是必要的,禁止尝试滥用sed和Python等绕过限制破坏一致性.应该在遵守规范的前提下继续任务,而不是降低标准.",
            <= 6 => $"警告: 你已经连续失败{failureCount}次。继续重复同一路径通常无效。应先核对比如语法、schema、方法名、字段名、路径、GUID、行号这些关键信息。注意使用MCP仍是必要的,禁止尝试滥用sed和Python等绕过限制破坏一致性.应该在遵守规范的前提下继续任务,而不是降低标准.",
            _ => $"警告: 你已经连续失败{failureCount}次。停下来!!!不要在错误的操作上死循环!!!注意使用MCP仍是必要的,禁止尝试滥用sed和Python等绕过限制破坏一致性.停止歪曲和滥用行为!"
        };

    private static string BuildToolFailureAdvice(string toolName, int failureCount)
    {
        string body = toolName switch
        {
            "patch_mech_view" when failureCount <= 3 => "若错误涉及\"新建 canvas\",按新建模式传 old_string: \"\"(空串),不要在空与非空之间反复改。要注意转义问题,若持续失败有可能是工具(而不是MCP)本身的强转导致的失败.这种场景下应该向用户报告和核实原文内容,定位到底是此前发错了请求参数还是工具转了某些字符导致无法匹配。",
            "patch_mech_view" => "不要继续改写 oldString 和 newString 乱试。若错误涉及\"新建 canvas\",按新建模式传 old_string: \"\"(空串),不要在空与非空之间反复改。要注意转义问题,若持续失败有可能是工具(而不是MCP)本身的强转导致的失败.这种场景下应该向用户报告和核实原文内容,定位到底是此前发错了请求参数还是工具转了某些字符导致无法匹配。",
            _ when toolName.StartsWith("refactor_", StringComparison.Ordinal) => "这类工具依赖精确定位。若持续失败，通常是相对路径、行号、GUID、scriptGroup、scriptName 这些目标信息不对。应先重新读取确认，不要直接乱试。",
            "read_sdk_docs" => "先核对 mode 与 pattern 或 path 是否对应，再按返回结果缩小范围，不要混着换 mode 乱试。",
            "read_lint" => "先看返回里的 errors 和 warnings，再核对 severity、start、limit、filter 字段形状，不要跳过诊断直接乱改。",
            "read_entities_by_name" or "read_entities_by_guid" => "先核对 name_pattern、entity_guids、kinds、detail 是否真实对应目标。不要臆造实体名、GUID 或路径；若拿不准，应先向用户核实。",
            "read_player_log" => "先核对 mode。read 模式下 startLine 和 endLine 必须准确；search 模式下 keyword 必须准确，不要靠反复扩大范围乱试。",
            _ => "先对照这次返回的错误和参数字段，不要只换几个值继续撞。"
        };
        return $"工具警告[{toolName}]: 已连续失败{failureCount}次。{body}";
    }

    private static string? TryGetToolName(JsonRpcRequest request)
    {
        if (!string.Equals(request.Method, "tools/call", StringComparison.Ordinal) || request.Params == null)
            return null;
        JsonElement element = JsonSerializer.SerializeToElement(request.Params, McpJsonOptions.Output);
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("name", out JsonElement nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
            return null;
        return nameElement.GetString();
    }

    private static async Task WriteHttpJsonAsync(HttpContext context, JsonRpcMessage response, CancellationToken cancellationToken)
    {
        string payload = JsonSerializer.Serialize(response, McpJsonOptions.Output);
        string responseId = response switch
        {
            JsonRpcResponse jsonResponse => jsonResponse.Id.ToString(),
            JsonRpcError jsonError => jsonError.Id.ToString(),
            _ => "unknown"
        };
        string stage = "StartAsync";
        using CancellationTokenSource stallLogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task stallLogTask = WatchHttpResponseStallAsync(
            HttpResponseStallLogDelay,
            stallLogCts.Token,
            () => stage,
            responseId,
            payload.Length,
            () => context.RequestAborted.IsCancellationRequested,
            message => Log.For(LogModule.Mcp).Error(message));
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        try
        {
            await context.Response.StartAsync(cancellationToken).ConfigureAwait(false);
            stage = "WriteAsync";
            await context.Response.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            stage = "FlushAsync";
            await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            stallLogCts.Cancel();
            try { await stallLogTask.ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                // watchdog 已取消，结束等待属于正常路径。
            }
        }
    }

    internal static async Task WatchHttpResponseStallAsync(
        TimeSpan delay,
        CancellationToken cancellationToken,
        Func<string> getStage,
        string responseId,
        int payloadLength,
        Func<bool> isRequestAborted,
        Action<string> logError)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        logError($"MCP HTTP 响应发送超过 {delay.TotalSeconds:F0}s，可能阻塞: id={responseId}, stage={getStage()}, payloadLength={payloadLength}, requestAborted={isRequestAborted()}");
    }

    private static async Task<bool> SendRpcResponseAsync(SseClient client, JsonRpcMessage response, CancellationToken cancellationToken)
    {
        string payload = JsonSerializer.Serialize(response, McpJsonOptions.Output);
        return await client.SendEventAsync("message", payload, cancellationToken).ConfigureAwait(false);
    }

    private static JsonRpcError CreateJsonRpcError(RequestId? id, McpErrorCode code, string message)
        => new() { Id = id.GetValueOrDefault(), Error = new JsonRpcErrorDetail { Code = (int)code, Message = message } };

    /// <summary>MCP 端口唯一来源：统一配置文件（规范 docs/Common_ToolkitConfig_Spec.md §3）。禁止 CLI 第二来源。</summary>
    private static int ResolvePort(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--mcp-port", StringComparison.Ordinal) && i + 1 < args.Length
                && int.TryParse(args[i + 1], out int port) && port > 0)
                return port;
            if (args[i].StartsWith("--mcp-port=", StringComparison.Ordinal)
                && int.TryParse(args[i]["--mcp-port=".Length..], out int inlinePort) && inlinePort > 0)
                return inlinePort;
        }
        return new ToolkitConfig(ToolkitUserPaths.ConfigFileFullPath).GetInt("mcp.port");
    }

    /// <summary>构造资源生成调度器（引擎进程生命周期 = Host 生命周期；无 --engine-python 时不启用该域）。</summary>
    private static ResourceGenScheduler? BuildResourceGenScheduler(string[] args)
    {
        string? enginePythonFullPath = GetEnginePython(args);
        if (string.IsNullOrEmpty(enginePythonFullPath))
        {
            Log.For(LogModule.Mcp).Info("资源生成域未启用（未注入 --engine-python）");
            return null;
        }
        ComfyUIEngineProcessManager engine = new(enginePythonFullPath);
        engine.Start();
        string cacheDirFullPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Toolkit", "ResourceGen");
        return new ResourceGenScheduler(engine, cacheDirFullPath);
    }

    private static string? GetEnginePython(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--engine-python", StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static void PushMcpTools(McpRequestHandler handler)
    {
        IList<Tool> tools = handler.ToolRegistry.GetAllDefinitions();
        IList<Resource> resources = handler.ResourceRegistry.ListResources();
        IList<ResourceTemplate> templates = handler.ResourceRegistry.ListResourceTemplates();
        var payload = new
        {
            tools = tools.Select(t => new { t.Name, t.Description, t.InputSchema }).ToArray(),
            resourcesCount = resources.Count,
            resourceTemplatesCount = templates.Count
        };
        string json = JsonSerializer.Serialize(payload, McpJsonOptions.Output);
        AsyncConsoleErrorWriter.WriteLine($"{ToolsPushPrefix}{json}");
    }

    /// <summary>
    /// 主动推送工程树数据到前端。
    /// 使用特殊格式输出到 stderr，由前端 workerProcessHooks 解析。
    /// </summary>
    private static void PushProjectTree(Core.ToolkitServices services)
    {
        ProjectRootEntity root = services.Model.Root;

        List<ProjectTreeNode> tree = BuildProjectTreeNodes(root);
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        string json = JsonSerializer.Serialize(tree, options);
        // 使用特殊前缀标记，前端可识别并解析；异步写入，禁止在模型写锁内阻塞
        AsyncConsoleErrorWriter.WriteLine($"[PROJECT_TREE]{json}");
    }

    private static McpHostStateSnapshot BuildHostState(ProjectModel model, McpViewWorkspace viewWorkspace)
    {
        WorkspaceBlockStateSnapshot blockState = model.GetWorkspaceBlockState();
        if (blockState.IsBlocked)
        {
            return new McpHostStateSnapshot("blocked", blockState.Message, blockState.BlockedRelativePaths);
        }

        if (!viewWorkspace.IsSynchronized)
        {
            if (viewWorkspace.HasPermanentRebuildFailure && viewWorkspace.LastRebuildFailureMessage is { } fatalMessage)
                return new McpHostStateSnapshot("error", $"工作区视图刷新连续失败：{fatalMessage}", Array.Empty<string>());

            if (viewWorkspace.LastRebuildFailureMessage is { } failureMessage)
                return new McpHostStateSnapshot("initializing", $"工作区视图刷新失败，正在重试：{failureMessage}", Array.Empty<string>());

            return new McpHostStateSnapshot("initializing", "工作区模型与视图尚未同步完成，Host 暂不可用。", Array.Empty<string>());
        }

        return new McpHostStateSnapshot("ready", "Host 已就绪", Array.Empty<string>());
    }

    private static void PushHostState(McpHostStateSnapshot state)
    {
        var payload = new
        {
            status = state.Status,
            message = state.Message,
            blockedFiles = state.BlockedFiles
        };
        string json = JsonSerializer.Serialize(payload, McpJsonOptions.Output);
        AsyncConsoleErrorWriter.WriteLine($"{HostStatePushPrefix}{json}");
    }

#pragma warning disable TK006 // 树构建方法，与 TryBuildTreeNode 配套
    private static List<ProjectTreeNode> BuildProjectTreeNodes(ProjectRootEntity root)
#pragma warning restore TK006
    {
        List<ProjectTreeNode> result = [];
        foreach (ProjectEntity child in root.Children)
            result.Add(TryBuildTreeNode(child));

        result.Sort(SortNodes);
        return result;
    }

    /// <summary>
    /// 从实体构建树节点
    /// </summary>
    private static ProjectTreeNode TryBuildTreeNode(ProjectEntity entity)
    {
        string displayKind = GetDisplayKind(entity);
        string id = entity.实体_完整相对路径标识;
        string name = GetDisplayName(entity);

        List<ProjectTreeNode>? children = null;
        if (entity is IHasChildren<ProjectEntity> hasChildren && hasChildren.Children.Count > 0)
        {
            children = [];
            foreach (ProjectEntity child in hasChildren.Children)
                children.Add(TryBuildTreeNode(child));

            if (children.Count > 0)
                children.Sort(SortNodes);
            else
                children = null;
        }

        return new ProjectTreeNode(Id: id, Name: name, Kind: displayKind, Path: id, Children: children);
    }

    private static int SortNodes(ProjectTreeNode a, ProjectTreeNode b)
    {
        bool aIsFolder = a.Kind == "folder" || a.Kind == "device" || a.Kind == "spine";
        bool bIsFolder = b.Kind == "folder" || b.Kind == "device" || b.Kind == "spine";
        if (aIsFolder && !bIsFolder) return -1;
        if (!aIsFolder && bIsFolder) return 1;
        return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// 获取实体的展示类型 - 返回所有实体类型，由前端负责筛选
    /// </summary>
    private static string GetDisplayKind(ProjectEntity entity)
        => entity.Kind switch
        {
            ProjectEntityKind.Root => "root",
            ProjectEntityKind.Map => "map",
            ProjectEntityKind.Mech => "mech",
            ProjectEntityKind.Device => "device",
            ProjectEntityKind.ScriptGroup => "scriptGroup",
            ProjectEntityKind.InlineScript => "inlineScript",
            ProjectEntityKind.CodeRepoScript => "codeRepoScript",
            ProjectEntityKind.Canvas => "canvas",
            ProjectEntityKind.Resource => "resource",
            ProjectEntityKind.Directory => "folder",
            ProjectEntityKind.Spine => "spine",
            ProjectEntityKind.TempRoot => "tempRoot",
            ProjectEntityKind.ModDirectory => "modDirectory",
            ProjectEntityKind.ToolkitRoot => "toolkitRoot",
            ProjectEntityKind.ToolkitInternalDirectory => "toolkitInternal",
            ProjectEntityKind.ToolkitFile => "toolkitFile",
            ProjectEntityKind.OtherTempFile => "otherTempFile",
            _ => "unknown"
        };

    /// <summary>
    /// 获取实体的展示名称
    /// </summary>
#pragma warning disable TK006 // 实体展示名称提取
    private static string GetDisplayName(ProjectEntity entity)
#pragma warning restore TK006
    {
        if (entity is DeviceEntityBase device && !string.IsNullOrEmpty(device.LogicName))
            return device.LogicName;
        return entity.实体_无相对路径标识符;
    }

    private sealed class SseClient
    {
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public SseClient(StreamWriter writer)
        {
            _writer = writer;
            Id = Guid.NewGuid().ToString("N");
        }

        public string Id { get; }

        public async Task<bool> SendEventAsync(string eventName, string data, CancellationToken cancellationToken)
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _writer.WriteAsync($"event: {eventName}\n").ConfigureAwait(false);
                foreach (string line in data.Split('\n'))
                    await _writer.WriteAsync($"data: {line}\n").ConfigureAwait(false);
                await _writer.WriteAsync("\n").ConfigureAwait(false);
                await _writer.FlushAsync().ConfigureAwait(false);
                return true;
            }
            catch (IOException) // [Fallback-Checked] SSE 连接断开，返回 false
            { return false; }
            catch (ObjectDisposedException) // [Fallback-Checked] SSE 连接断开，返回 false
            { return false; }
            finally { _writeLock.Release(); }
        }

        public async Task<bool> SendCommentAsync(string comment, CancellationToken cancellationToken)
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _writer.WriteAsync($": {comment}\n\n").ConfigureAwait(false);
                await _writer.FlushAsync().ConfigureAwait(false);
                return true;
            }
            catch (IOException) // [Fallback-Checked] SSE 连接断开，返回 false
            { return false; }
            catch (ObjectDisposedException) // [Fallback-Checked] SSE 连接断开，返回 false
            { return false; }
            finally { _writeLock.Release(); }
        }
    }

    internal sealed class SessionMonitor
    {
        private readonly object _sync = new();
        private DateTimeOffset? _lastToolCallUtc;
        private string? _lastFailedToolName;
        private int _consecutiveToolCallErrorCount;

        public SessionMonitor(string sessionId, string transport)
        {
            SessionId = sessionId;
            Transport = transport;
            CreatedAtUtc = DateTimeOffset.UtcNow;
        }

        public string SessionId { get; }

        public string Transport { get; }

        public DateTimeOffset CreatedAtUtc { get; }

        public int RecordToolCall(string toolName, bool isFailure)
        {
            lock (_sync)
            {
                _lastToolCallUtc = DateTimeOffset.UtcNow;
                if (!isFailure)
                {
                    _lastFailedToolName = null;
                    _consecutiveToolCallErrorCount = 0;
                    return 0;
                }
                if (string.Equals(_lastFailedToolName, toolName, StringComparison.Ordinal))
                    _consecutiveToolCallErrorCount++;
                else
                    _consecutiveToolCallErrorCount = 1;
                _lastFailedToolName = toolName;
                return _consecutiveToolCallErrorCount;
            }
        }

        public DateTimeOffset GetLastActivityUtc()
        {
            lock (_sync)
            {
                return _lastToolCallUtc.GetValueOrDefault(CreatedAtUtc);
            }
        }

        internal SessionPushItem ToPushItem()
        {
            lock (_sync)
            {
                return new SessionPushItem(
                    SessionId,
                    Transport,
                    CreatedAtUtc.ToString("O"),
                    _lastToolCallUtc?.ToString("O"),
                    _consecutiveToolCallErrorCount);
            }
        }
    }

    internal sealed record SessionPushItem(
        string SessionId,
        string Transport,
        string CreatedAtUtc,
        string? LastToolCallUtc,
        int ConsecutiveToolCallErrorCount);

    private static readonly JsonSerializerOptions LspJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static async Task HandleLspRequestAsync(HttpContext context, LspRequestHandler handler)
    {
        string payload;
        using (StreamReader reader = new(context.Request.Body, TextEncoding.Utf8, false, leaveOpen: true))
            payload = await reader.ReadToEndAsync().ConfigureAwait(false);

#pragma warning disable TK008 // MCP 边界：HTTP 请求体允许为空，需要返回协议错误
        if (payload.Length == 0)
#pragma warning restore TK008
        {
            await WriteLspErrorAsync(context, null, -32700, "Empty request body").ConfigureAwait(false);
            return;
        }

        JsonObject request;
        try
        {
            JsonNode? parsed = JsonNode.Parse(payload);
            if (parsed is not JsonObject reqObj)
            {
                await WriteLspErrorAsync(context, null, -32600, "Invalid request").ConfigureAwait(false);
                return;
            }
            request = reqObj;
        }
        catch (JsonException)
        {
            await WriteLspErrorAsync(context, null, -32700, "Parse error").ConfigureAwait(false);
            return;
        }

        JsonObject response = handler.Handle(request);
        string responseJson = response.ToJsonString();

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(responseJson).ConfigureAwait(false);
    }

    private static async Task WriteLspErrorAsync(HttpContext context, JsonNode? id, int code, string message)
    {
        JsonObject error = new()
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };
        if (id != null) error["id"] = id;
        string json = error.ToJsonString();
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(json).ConfigureAwait(false);
    }
}
