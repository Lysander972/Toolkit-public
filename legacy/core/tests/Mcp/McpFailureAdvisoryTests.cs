using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Toolkit.Mcp;
using Xunit;

namespace Toolkit.Core.Tests.Mcp;

public sealed class McpFailureAdvisoryTests
{
    [Fact]
    public void ProcessFailureAdvisories_ToolResultError_ShouldAppendWarningIntoMessages()
    {
        const string sessionId = "session-1";
        ConcurrentDictionary<string, int> failureCounts = new(StringComparer.Ordinal);
        failureCounts[sessionId] = 0;
        ConcurrentDictionary<string, McpHostRunner.SessionMonitor> sessionMonitors = CreateSessionMonitors(sessionId);
        JsonRpcRequest request = CreateToolRequest("patch_mech_view");

        InvokeFailureAdvisories(CreateToolResponse("[Error] old_string 未唯一匹配", true), request, failureCounts, sessionMonitors, sessionId);

        JsonRpcResponse response = CreateToolResponse("[Error] old_string 未唯一匹配", true);
        JsonRpcResponse result = Assert.IsType<JsonRpcResponse>(
            InvokeFailureAdvisories(response, request, failureCounts, sessionMonitors, sessionId));

        string[] messages = ReadMessages(result);
        Assert.Equal(2, messages.Length);
        Assert.Equal("[Error] old_string 未唯一匹配", messages[0]);
        Assert.Contains("警告: 你已经连续失败2次", messages[1]);
        Assert.Contains("工具警告[patch_mech_view]: 已连续失败2次", messages[1]);

        JsonObject resultObj = Assert.IsType<JsonObject>(result.Result);
        Assert.False(resultObj.ContainsKey("tips"));
        JsonArray content = Assert.IsType<JsonArray>(resultObj["content"]);
        Assert.Single(content);
    }

    [Fact]
    public void ProcessFailureAdvisories_SuccessToolResult_ShouldResetFailureCounts()
    {
        const string sessionId = "session-1";
        ConcurrentDictionary<string, int> failureCounts = new(StringComparer.Ordinal);
        failureCounts[sessionId] = 0;
        ConcurrentDictionary<string, McpHostRunner.SessionMonitor> sessionMonitors = CreateSessionMonitors(sessionId);
        JsonRpcRequest request = CreateToolRequest("patch_mech_view");

        InvokeFailureAdvisories(CreateToolResponse("[Error] 第一次失败", true), request, failureCounts, sessionMonitors, sessionId);
        InvokeFailureAdvisories(CreateToolResponse("[Error] 第二次失败", true), request, failureCounts, sessionMonitors, sessionId);
        InvokeFailureAdvisories(CreateToolResponse("{}", false), request, failureCounts, sessionMonitors, sessionId);

        Assert.Equal(0, failureCounts[sessionId]);

        JsonRpcResponse response = CreateToolResponse("[Error] 重置后第一次失败", true);
        JsonRpcResponse result = Assert.IsType<JsonRpcResponse>(
            InvokeFailureAdvisories(response, request, failureCounts, sessionMonitors, sessionId));

        string[] messages = ReadMessages(result);
        Assert.Single(messages);
        Assert.Equal("[Error] 重置后第一次失败", messages[0]);
    }

    [Fact]
    public void ProcessFailureAdvisories_BlockedJsonRpcError_ShouldNotSplitAdviceIntoErrorData()
    {
        const string sessionId = "session-1";
        ConcurrentDictionary<string, int> failureCounts = new(StringComparer.Ordinal);
        failureCounts[sessionId] = 0;
        ConcurrentDictionary<string, McpHostRunner.SessionMonitor> sessionMonitors = CreateSessionMonitors(sessionId);
        JsonRpcRequest request = CreateToolRequest("patch_mech_view");
        JsonRpcError blockedResponse = new()
        {
            Id = new RequestId(1L),
            Error = new JsonRpcErrorDetail
            {
                Code = -32000,
                Message = "存在至少一个文件正在被外部进程占用，Host 已阻塞。"
            }
        };

        JsonRpcMessage result = InvokeFailureAdvisories(blockedResponse, request, failureCounts, sessionMonitors, sessionId);

        JsonRpcError returned = Assert.IsType<JsonRpcError>(result);
        Assert.Same(blockedResponse, returned);
        Assert.Equal(0, failureCounts[sessionId]);
        Assert.False(returned.Error.Data is JsonObject dataObj && dataObj.ContainsKey("tips"));
    }

    private static JsonRpcMessage InvokeFailureAdvisories(
        JsonRpcMessage response,
        JsonRpcRequest request,
        ConcurrentDictionary<string, int> failureCounts,
        ConcurrentDictionary<string, McpHostRunner.SessionMonitor> sessionMonitors,
        string sessionId)
    {
        return McpHostRunner.ProcessFailureAdvisories(response, request, failureCounts, sessionMonitors, sessionId);
    }

    private static ConcurrentDictionary<string, McpHostRunner.SessionMonitor> CreateSessionMonitors(string sessionId)
    {
        ConcurrentDictionary<string, McpHostRunner.SessionMonitor> sessionMonitors = new(StringComparer.Ordinal);
        sessionMonitors[sessionId] = new McpHostRunner.SessionMonitor(sessionId, "http");
        return sessionMonitors;
    }

    private static JsonRpcRequest CreateToolRequest(string toolName)
    {
        return new JsonRpcRequest
        {
            Id = new RequestId(1L),
            Method = "tools/call",
            Params = new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = new JsonObject()
            }
        };
    }

    private static JsonRpcResponse CreateToolResponse(string message, bool isError)
    {
        JsonObject payload = new();
        if (isError)
        {
            JsonArray messages = new();
            messages.Add(message);
            payload["messages"] = messages;
        }

        return new JsonRpcResponse
        {
            Id = new RequestId(1L),
            Result = new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = payload.ToJsonString(McpJsonOptions.Output)
                    }
                },
                ["isError"] = isError
            }
        };
    }

    private static string[] ReadMessages(JsonRpcResponse response)
    {
        ToolCallResultPayload resultPayload =
            JsonSerializer.Deserialize<ToolCallResultPayload>(response.Result, McpJsonOptions.Output)
            ?? throw new InvalidOperationException("Tool result payload was not JSON");
        ToolTextPayload textPayload =
            JsonSerializer.Deserialize<ToolTextPayload>(resultPayload.Content[0].Text, McpJsonOptions.Output)
            ?? throw new InvalidOperationException("Tool text payload was not JSON");
        return textPayload.Messages.ToArray();
    }

    private sealed record ToolCallResultPayload(ToolTextContentPayload[] Content, bool IsError);

    private sealed record ToolTextContentPayload(string Type, string Text);

    private sealed record ToolTextPayload(List<string> Messages);
}
