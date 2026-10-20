using System.Text.Json;
using ModelContextProtocol.Protocol;
using Toolkit.Mcp;
using Xunit;

namespace Toolkit.Core.Tests.Mcp;

[Collection("McpTests")]
public sealed class RefactorSchemaDefinitionTests
{
    private readonly McpTestFixture _fixture;

    public RefactorSchemaDefinitionTests(McpTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static JsonElement GetToolSchema(McpToolRegistry registry, string toolName)
    {
        Tool? tool = registry.GetAllDefinitions().FirstOrDefault(t => t.Name == toolName);
        Assert.NotNull(tool);
        return tool.InputSchema;
    }

    [Fact]
    public void RefactorSchemas_Should_Declare_AdditionalProperties_False()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        McpToolRegistry registry = _fixture.Registry;
        JsonElement schema = GetToolSchema(registry, "refactor_rename_script");

        Assert.True(schema.TryGetProperty("additionalProperties", out JsonElement additionalProperties));
        Assert.Equal(JsonValueKind.False, additionalProperties.ValueKind);
    }

    [Fact]
    public void RefactorRenameScript_ScriptRelativePath_Should_Be_NonEmpty_String_In_Schema()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        McpToolRegistry registry = _fixture.Registry;
        JsonElement schema = GetToolSchema(registry, "refactor_rename_script");
        JsonElement property = schema.GetProperty("properties").GetProperty("script_relative_path");

        Assert.Equal("string", property.GetProperty("type").GetString());
        Assert.Equal(1, property.GetProperty("minLength").GetInt32());
        // ScriptCodePathProperty: 必须以 .code 结尾，且禁止反斜杠
        Assert.Equal("^[^\\\\]+\\.code$", property.GetProperty("pattern").GetString());
    }

    [Fact]
    public void RefactorCanvasExtract_Line_Should_Be_Positive_Integer_In_Schema()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        McpToolRegistry registry = _fixture.Registry;
        JsonElement schema = GetToolSchema(registry, "refactor_canvas_extract");
        JsonElement property = schema.GetProperty("properties").GetProperty("line");

        Assert.Equal("integer", property.GetProperty("type").GetString());
        Assert.Equal(1, property.GetProperty("minimum").GetInt32());
    }

    [Fact]
    public void RefactorScriptToRepository_RequiredScriptGroup_Should_Be_NonEmpty_String_In_Schema()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        McpToolRegistry registry = _fixture.Registry;
        JsonElement schema = GetToolSchema(registry, "refactor_script_to_repository");
        JsonElement property = schema.GetProperty("properties").GetProperty("script_group");

        Assert.Equal("string", property.GetProperty("type").GetString());
        Assert.Equal(1, property.GetProperty("minLength").GetInt32());
        Assert.Equal("\\S", property.GetProperty("pattern").GetString());
        Assert.Contains(schema.GetProperty("required").EnumerateArray().Select(static x => x.GetString()), static x => x == "script_group");
    }

    [Fact]
    public void RefactorScriptToRepository_DeviceRelativePath_Should_Match_Device_Path_Pattern()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        McpToolRegistry registry = _fixture.Registry;
        JsonElement schema = GetToolSchema(registry, "refactor_script_to_repository");
        JsonElement property = schema.GetProperty("properties").GetProperty("device_relative_path");

        Assert.Equal("string", property.GetProperty("type").GetString());
        Assert.Equal(1, property.GetProperty("minLength").GetInt32());
        // 必须匹配 xxx.map_/数字 或 xxx.mech_/数字 格式
        Assert.Equal("^(?!\\.?\\/)(?!.*\\.\\.)[^\\\\]+\\.(?:map_|mech_)/[0-9]+$", property.GetProperty("pattern").GetString());
    }

    [Fact]
    public void RefactorScriptLinkRepository_DeviceRelativePath_Should_Match_Device_Path_Pattern()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        McpToolRegistry registry = _fixture.Registry;
        JsonElement schema = GetToolSchema(registry, "refactor_script_link_repository");
        JsonElement property = schema.GetProperty("properties").GetProperty("device_relative_path");

        Assert.Equal("string", property.GetProperty("type").GetString());
        Assert.Equal(1, property.GetProperty("minLength").GetInt32());
        // 必须匹配 xxx.map_/数字 或 xxx.mech_/数字 格式
        Assert.Equal("^(?!\\.?\\/)(?!.*\\.\\.)[^\\\\]+\\.(?:map_|mech_)/[0-9]+$", property.GetProperty("pattern").GetString());
    }
}
