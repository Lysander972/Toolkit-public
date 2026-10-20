using System.Text.Json;
using ModelContextProtocol.Protocol;
using Toolkit.Mcp;
using Xunit;

namespace Toolkit.Core.Tests.Mcp;

[Collection("McpTests")]
public sealed class McpSchemaDefinitionTests
{
    private readonly McpTestFixture _fixture;

    public McpSchemaDefinitionTests(McpTestFixture fixture)
    {
        _fixture = fixture;
    }

#pragma warning disable TK006 // 测试辅助：按名称提取工具 schema
    private static JsonElement GetToolSchema(McpToolRegistry registry, string toolName)
    {
        Tool tool = registry.GetAllDefinitions().First(t => t.Name == toolName);
        return tool.InputSchema;
    }
#pragma warning restore TK006

    [Fact]
    public void PatchMechView_Should_Use_ViewPath_And_NonEmpty_Constraints()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        McpToolRegistry registry = _fixture.Registry;
        JsonElement schema = GetToolSchema(registry, "patch_mech_view");
        JsonElement properties = schema.GetProperty("properties");

        Assert.True(properties.TryGetProperty("view_path", out JsonElement viewPath));
        Assert.False(properties.TryGetProperty("viewRelativePath", out _));
        Assert.Equal("string", viewPath.GetProperty("type").GetString());
        Assert.Equal(1, viewPath.GetProperty("minLength").GetInt32());
        Assert.True(viewPath.TryGetProperty("pattern", out _));

        // 单编辑平铺参数
        Assert.True(properties.TryGetProperty("old_string", out JsonElement oldString));
        Assert.Equal("string", oldString.GetProperty("type").GetString());
        // old_string 允许空串（新建 .html canvas 模式传 ""），故无 minLength/pattern 约束
        Assert.False(oldString.TryGetProperty("minLength", out _));
        Assert.False(oldString.TryGetProperty("pattern", out _));

        Assert.True(properties.TryGetProperty("new_string", out _));
        Assert.True(properties.TryGetProperty("replace_all", out _));
        Assert.False(properties.TryGetProperty("edits", out _));
    }

    [Fact]
    public void PatchMechView_OldString_Empty_Should_Pass_Schema_Validation()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        McpToolRegistry registry = _fixture.Registry;
        InputSchema? schema = registry.TryGetSchema("patch_mech_view");
        Assert.NotNull(schema);

        // 新建 canvas 场景：old_string 传空串必须能通过 schema 校验（不得被 minLength/pattern 拦截）。
        using JsonDocument doc = JsonDocument.Parse("""{"view_path":"new_canvas.html","old_string":"","new_string":"<a/>"}""");
        List<string> errors = McpToolRegistry.ValidateArguments(schema, doc.RootElement.Clone());
        Assert.Empty(errors);
    }

    [Fact]
    public void PatchMechViewMultiedit_Should_Use_Edits_Array_With_SnakeCase()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        McpToolRegistry registry = _fixture.Registry;
        JsonElement schema = GetToolSchema(registry, "patch_mech_view_multiedit");
        JsonElement properties = schema.GetProperty("properties");

        Assert.True(properties.TryGetProperty("view_path", out _));
        Assert.True(properties.TryGetProperty("edits", out JsonElement edits));
        Assert.False(properties.TryGetProperty("replace_all", out _));

        JsonElement oldString = edits.GetProperty("items").GetProperty("properties").GetProperty("old_string");
        Assert.Equal("array", edits.GetProperty("type").GetString());
        Assert.Equal(1, edits.GetProperty("minItems").GetInt32());
        Assert.Equal("string", oldString.GetProperty("type").GetString());
        Assert.Equal(1, oldString.GetProperty("minLength").GetInt32());
        Assert.Equal("\\S", oldString.GetProperty("pattern").GetString());
    }

    [Fact]
    public void CommitDraft_Should_Use_DraftPath_Constraints()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        McpToolRegistry registry = _fixture.Registry;
        JsonElement schema = GetToolSchema(registry, "commit_draft");
        JsonElement properties = schema.GetProperty("properties");

        Assert.True(properties.TryGetProperty("draft_path", out JsonElement draftPath));
        Assert.False(properties.TryGetProperty("view_path", out _));
        Assert.Equal("string", draftPath.GetProperty("type").GetString());
        Assert.Equal(1, draftPath.GetProperty("minLength").GetInt32());
        Assert.True(draftPath.TryGetProperty("pattern", out JsonElement pattern));
        Assert.Contains(".draft", pattern.GetString());
    }

    [Fact]
    public void CleanupDrafts_Should_Use_NonEmpty_DraftPaths_Array()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        McpToolRegistry registry = _fixture.Registry;
        JsonElement schema = GetToolSchema(registry, "cleanup_drafts");
        JsonElement properties = schema.GetProperty("properties");

        Assert.True(properties.TryGetProperty("draft_paths", out JsonElement draftPaths));
        Assert.False(properties.TryGetProperty("view_path", out _));
        Assert.Equal("array", draftPaths.GetProperty("type").GetString());
        Assert.Equal(1, draftPaths.GetProperty("minItems").GetInt32());

        JsonElement draftPathItem = draftPaths.GetProperty("items");
        Assert.Equal("string", draftPathItem.GetProperty("type").GetString());
        Assert.Equal(1, draftPathItem.GetProperty("minLength").GetInt32());
        Assert.True(draftPathItem.TryGetProperty("pattern", out JsonElement itemPattern));
        Assert.Contains(".draft", itemPattern.GetString());
    }

    [Fact]
    public void ReadEntities_Should_Use_NonEmpty_String_And_Array_Constraints()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        McpToolRegistry registry = _fixture.Registry;
        // read_entities_by_name 检查 namePattern 和 limit
        JsonElement schemaByName = GetToolSchema(registry, "read_entities_by_name");
        JsonElement propertiesByName = schemaByName.GetProperty("properties");
        JsonElement namePattern = propertiesByName.GetProperty("name_pattern");
        JsonElement limit = propertiesByName.GetProperty("limit");

        Assert.Equal(1, namePattern.GetProperty("minLength").GetInt32());
        Assert.Equal("\\S", namePattern.GetProperty("pattern").GetString());
        Assert.Equal(1, limit.GetProperty("minimum").GetInt32());

        // read_entities_by_guid 检查 entityGuids
        JsonElement schemaByGuid = GetToolSchema(registry, "read_entities_by_guid");
        JsonElement propertiesByGuid = schemaByGuid.GetProperty("properties");
        JsonElement entityGuids = propertiesByGuid.GetProperty("entity_guids");
        JsonElement entityGuidItem = entityGuids.GetProperty("items");

        // entityGuids 是 Required 参数，不需要 minItems
        Assert.Equal("array", entityGuids.GetProperty("type").GetString());
        Assert.Equal("string", entityGuidItem.GetProperty("type").GetString());
        Assert.Equal("^[0-9a-f]{32}$", entityGuidItem.GetProperty("pattern").GetString());
    }

    [Fact]
    public void ReadLint_And_PlayerLog_Should_Use_Positive_Integer_Constraints()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        McpToolRegistry registry = _fixture.Registry;
        JsonElement lintSchema = GetToolSchema(registry, "read_lint");
        JsonElement lintProperties = lintSchema.GetProperty("properties");

        Assert.Equal(1, lintProperties.GetProperty("start").GetProperty("minimum").GetInt32());
        Assert.Equal(1, lintProperties.GetProperty("limit").GetProperty("minimum").GetInt32());

        // filter.paths 使用 StringArrayProperty，允许空数组，元素必须有 pattern
        JsonElement filter = lintProperties.GetProperty("filter").GetProperty("properties");
        JsonElement paths = filter.GetProperty("paths");
        Assert.Equal("array", paths.GetProperty("type").GetString());
        Assert.True(paths.TryGetProperty("items", out JsonElement pathItems));
        Assert.Equal(1, pathItems.GetProperty("minLength").GetInt32());
        Assert.Equal("\\S", pathItems.GetProperty("pattern").GetString());

        JsonElement playerLogSchema = GetToolSchema(registry, "read_player_log");
        JsonElement playerLogProperties = playerLogSchema.GetProperty("properties");
        Assert.Equal(1, playerLogProperties.GetProperty("start_line").GetProperty("minimum").GetInt32());
        Assert.Equal(1, playerLogProperties.GetProperty("end_line").GetProperty("minimum").GetInt32());
    }

    [Fact]
    public void ReadSdkDocs_Should_Use_NonEmpty_String_Constraints_For_Optional_Strings()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        McpToolRegistry registry = _fixture.Registry;
        JsonElement schema = GetToolSchema(registry, "read_sdk_docs");
        JsonElement properties = schema.GetProperty("properties");
        JsonElement pattern = properties.GetProperty("pattern");
        JsonElement path = properties.GetProperty("path");
        JsonElement area = properties.GetProperty("area");

        // pattern 和 path 使用 NonEmptyStringProperty
        Assert.Equal(1, pattern.GetProperty("minLength").GetInt32());
        Assert.Equal("\\S", pattern.GetProperty("pattern").GetString());
        Assert.Equal(1, path.GetProperty("minLength").GetInt32());
        Assert.Equal("\\S", path.GetProperty("pattern").GetString());

        // area 允许空字符串（没有 minLength 和 pattern）
        Assert.Equal("string", area.GetProperty("type").GetString());
        Assert.False(area.TryGetProperty("minLength", out _));
        Assert.False(area.TryGetProperty("pattern", out _));
    }
}
