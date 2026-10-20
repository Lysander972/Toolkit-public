using System.Reflection;
using System.Text.Json;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Mcp;

public sealed class McpRequestHandlerBuildToolContentTests
{
    private static readonly MethodInfo BuildToolContentMethod = typeof(McpRequestHandler)
        .GetMethod("BuildToolContent", BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingMethodException("McpRequestHandler.BuildToolContent");

    private static (string Text, bool IsError) InvokeBuildToolContent(object payload)
    {
        object? value = BuildToolContentMethod.Invoke(null, [payload]);
        Assert.NotNull(value);
        return ((string Text, bool IsError))value;
    }

    [Fact]
    public void BuildToolContent_AppliedFalseWithoutErrors_ShouldNotBeError()
    {
        object payload = new PatchToolResponse(
            Array.Empty<ToolError>(),
            Array.Empty<ToolError>(),
            Array.Empty<ToolError>(),
            new { affected_paths = Array.Empty<string>() },
            false
        );

        (string text, bool isError) = InvokeBuildToolContent(payload);

        Assert.False(isError);
        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.False(doc.RootElement.TryGetProperty("message", out _));
        Assert.False(doc.RootElement.TryGetProperty("messages", out _));
        Assert.True(doc.RootElement.TryGetProperty("affected_paths", out _));
    }

    [Fact]
    public void BuildToolContent_WithErrors_ShouldMergeToMessages()
    {
        ToolError error = new("foo.ts", 10, null, "boom", ToolDiagnosticSeverity.Error);
        object payload = new PatchToolResponse(
            [error],
            Array.Empty<ToolError>(),
            Array.Empty<ToolError>(),
            new { affected_paths = Array.Empty<string>() },
            false
        );

        (string text, bool isError) = InvokeBuildToolContent(payload);

        Assert.True(isError);
        using JsonDocument doc = JsonDocument.Parse(text);
        JsonElement messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("[Error] foo.ts:10: boom", messages[0].GetString());
    }

    [Fact]
    public void BuildToolContent_WithWarnings_ShouldFlattenToMessages()
    {
        ToolError warning = new(null, null, null, "草稿已失效", ToolDiagnosticSeverity.Warning);
        object payload = new PatchToolResponse(
            Array.Empty<ToolError>(),
            [warning],
            Array.Empty<ToolError>(),
            new { affected_paths = new[] { "foo.code" } },
            false
        );

        (string text, bool isError) = InvokeBuildToolContent(payload);

        Assert.False(isError);
        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.False(doc.RootElement.TryGetProperty("message", out _));
        JsonElement messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("[Warning] 草稿已失效", messages[0].GetString());
    }

    [Fact]
    public void BuildToolContent_ErrorsAndWarningsMergedInOrder()
    {
        ToolError error = new(null, null, null, "err1", ToolDiagnosticSeverity.Error);
        ToolError warning = new(null, null, null, "warn1", ToolDiagnosticSeverity.Warning);
        object payload = new PatchToolResponse(
            [error],
            [warning],
            Array.Empty<ToolError>(),
            new { affected_paths = Array.Empty<string>() },
            false
        );

        (string text, bool isError) = InvokeBuildToolContent(payload);

        Assert.True(isError);
        using JsonDocument doc = JsonDocument.Parse(text);
        JsonElement messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("[Error] err1", messages[0].GetString());
        Assert.Equal("[Warning] warn1", messages[1].GetString());
    }
}
