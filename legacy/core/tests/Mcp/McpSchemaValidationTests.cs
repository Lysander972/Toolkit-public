using System.Text.Json;
using Toolkit.Mcp;
using Xunit;

namespace Toolkit.Core.Tests.Mcp;

public sealed class McpSchemaValidationTests
{
    /// <summary>
    /// 辅助方法：从 JSON 创建 InputSchema（使用 Toolkit.Mcp.InputSchema，而非 ModelContextProtocol.Protocol.InputSchema）
    /// </summary>
    private static InputSchema CreateSchema(string json)
    {
        return JsonSerializer.Deserialize<InputSchema>(json, McpJsonOptions.Standard) ?? throw new JsonException($"Failed to deserialize InputSchema: {json}");
    }

    /// <summary>
    /// 辅助方法：从 JSON 创建参数
    /// </summary>
    private static JsonElement? CreateArguments(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json, McpJsonOptions.Standard);
    }

    [Fact]
    public void ValidateArguments_Should_Return_Empty_When_Valid()
    {
        InputSchema schema = CreateSchema("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string", "minLength": 1 }
                },
                "required": ["name"]
            }
            """);

        JsonElement? args = CreateArguments("""{"name": "test"}""");

        List<string> errors = McpToolRegistry.ValidateArguments(schema, args);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateArguments_Should_Detect_Missing_Required()
    {
        InputSchema schema = CreateSchema("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string" }
                },
                "required": ["name"]
            }
            """);

        JsonElement? args = CreateArguments("""{}""");

        List<string> errors = McpToolRegistry.ValidateArguments(schema, args);

        Assert.Single(errors);
        Assert.Contains("缺少必需参数: name", errors);
    }

    [Fact]
    public void ValidateArguments_Should_Detect_MinLength_Violation()
    {
        InputSchema schema = CreateSchema("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string", "minLength": 5 }
                }
            }
            """);

        JsonElement? args = CreateArguments("""{"name": "abc"}""");

        List<string> errors = McpToolRegistry.ValidateArguments(schema, args);

        Assert.Single(errors);
        Assert.Contains("长度不足", errors[0]);
    }

    [Fact]
    public void ValidateArguments_Should_Detect_Pattern_Violation()
    {
        InputSchema schema = CreateSchema("""
            {
                "type": "object",
                "properties": {
                    "guid": { "type": "string", "pattern": "^[0-9a-f]{32}$" }
                }
            }
            """);

        // 33 字符（带 @1 前缀）
        JsonElement? args = CreateArguments("""{"guid": "@1deadbeefdeadbeefdeadbeefdeadbeef"}""");

        List<string> errors = McpToolRegistry.ValidateArguments(schema, args);

        Assert.Single(errors);
        Assert.Contains("格式无效", errors[0]);
        Assert.Contains("pattern", errors[0].ToLower());
    }

    [Fact]
    public void ValidateArguments_Should_Detect_Enum_Violation()
    {
        InputSchema schema = CreateSchema("""
            {
                "type": "object",
                "properties": {
                    "mode": { "type": "string", "enum": ["read", "write"] }
                }
            }
            """);

        JsonElement? args = CreateArguments("""{"mode": "delete"}""");

        List<string> errors = McpToolRegistry.ValidateArguments(schema, args);

        Assert.Single(errors);
        Assert.Contains("值无效", errors[0]);
    }

    [Fact]
    public void ValidateArguments_Should_Detect_Minimum_Violation()
    {
        InputSchema schema = CreateSchema("""
            {
                "type": "object",
                "properties": {
                    "count": { "type": "integer", "minimum": 1 }
                }
            }
            """);

        JsonElement? args = CreateArguments("""{"count": 0}""");

        List<string> errors = McpToolRegistry.ValidateArguments(schema, args);

        Assert.Single(errors);
        Assert.Contains("值过小", errors[0]);
    }

    [Fact]
    public void ValidateArguments_Should_Detect_Array_MinItems_Violation()
    {
        InputSchema schema = CreateSchema("""
            {
                "type": "object",
                "properties": {
                    "items": { "type": "array", "minItems": 1 }
                }
            }
            """);

        JsonElement? args = CreateArguments("""{"items": []}""");

        List<string> errors = McpToolRegistry.ValidateArguments(schema, args);

        Assert.Single(errors);
        Assert.Contains("元素不足", errors[0]);
    }

    [Fact]
    public void ValidateArguments_Should_Detect_Array_Item_Pattern_Violation()
    {
        InputSchema schema = CreateSchema("""
            {
                "type": "object",
                "properties": {
                    "guids": {
                        "type": "array",
                        "items": { "type": "string", "pattern": "^[0-9a-f]{32}$" }
                    }
                }
            }
            """);

        JsonElement? args = CreateArguments("""{"guids": ["deadbeefdeadbeefdeadbeefdeadbeef", "invalid-guid"]}""");

        List<string> errors = McpToolRegistry.ValidateArguments(schema, args);

        Assert.Single(errors);
        Assert.Contains("guids[1]", errors[0]);
        Assert.Contains("格式无效", errors[0]);
    }

    [Fact]
    public void ValidateArguments_Should_Detect_Array_Object_Item_Missing_Required()
    {
        InputSchema schema = CreateSchema("""
            {
                "type": "object",
                "properties": {
                    "edits": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "old_string": { "type": "string" },
                                "new_string": { "type": "string" }
                            },
                            "required": ["old_string", "new_string"]
                        }
                    }
                }
            }
            """);

        JsonElement? args = CreateArguments("""{"edits": [{"oldString": "a", "newString": "b"}]}""");

        List<string> errors = McpToolRegistry.ValidateArguments(schema, args);

        Assert.Equal(2, errors.Count);
        Assert.Contains("缺少必需参数: edits[0].old_string", errors);
        Assert.Contains("缺少必需参数: edits[0].new_string", errors);
    }

    [Fact]
    public void ValidateArguments_Should_Detect_Type_Mismatch_String()
    {
        InputSchema schema = CreateSchema("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string" }
                }
            }
            """);

        JsonElement? args = CreateArguments("""{"name": 123}""");

        List<string> errors = McpToolRegistry.ValidateArguments(schema, args);

        Assert.Single(errors);
        Assert.Contains("类型错误", errors[0]);
    }

    [Fact]
    public void ValidateArguments_Should_Return_Empty_When_Arguments_Null()
    {
        InputSchema schema = CreateSchema("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string" }
                }
            }
            """);

        List<string> errors = McpToolRegistry.ValidateArguments(schema, null);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateArguments_Should_Validate_Real_Guid_Format()
    {
        InputSchema schema = CreateSchema("""
            {
                "type": "object",
                "properties": {
                    "targetGuid": { "type": "string", "pattern": "^[0-9a-f]{32}$" }
                }
            }
            """);

        // 正确的 32 位小写十六进制
        JsonElement? validArgs = CreateArguments("""{"targetGuid": "deadbeefdeadbeefdeadbeefdeadbeef"}""");
        List<string> validErrors = McpToolRegistry.ValidateArguments(schema, validArgs);
        Assert.Empty(validErrors);

        // 错误：33 位（带 @1 前缀）
        JsonElement? invalidArgs1 = CreateArguments("""{"targetGuid": "@1deadbeefdeadbeefdeadbeefdeadbeef"}""");
        List<string> invalidErrors1 = McpToolRegistry.ValidateArguments(schema, invalidArgs1);
        Assert.Single(invalidErrors1);

        // 错误：包含大写字母
        JsonElement? invalidArgs2 = CreateArguments("""{"targetGuid": "DEADBEEFDEADBEEFDEADBEEFDEADBEEF"}""");
        List<string> invalidErrors2 = McpToolRegistry.ValidateArguments(schema, invalidArgs2);
        Assert.Single(invalidErrors2);

        // 错误：31 位（少一位）
        JsonElement? invalidArgs3 = CreateArguments("""{"targetGuid": "deadbeefdeadbeefdeadbeefdeadbee"}""");
        List<string> invalidErrors3 = McpToolRegistry.ValidateArguments(schema, invalidArgs3);
        Assert.Single(invalidErrors3);
    }
}
