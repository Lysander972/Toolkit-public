using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toolkit.Mcp;

// 本文件仅保留业务层所需的请求参数类型和内部 Schema 构建类型
// JSON-RPC 协议类型和 MCP 协议类型均使用官方 SDK: ModelContextProtocol.Protocol

/// <summary>
/// tools/call 参数
/// </summary>
public sealed record McpToolCallParams(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] JsonElement? Arguments
);

/// <summary>
/// resources/read 参数
/// </summary>
public sealed record McpResourceReadParams(
    [property: JsonPropertyName("uri")] string Uri
);

// 以下为内部使用的 JSON Schema 构建类型，用于序列化为 Tool.InputSchema

/// <summary>
/// JSON Schema 形式的输入参数定义
/// </summary>
public sealed record InputSchema
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, PropertySchema>? Properties { get; init; }

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Required { get; init; }

    [JsonPropertyName("additionalProperties")]
    public bool AdditionalProperties { get; init; } = false;
}

/// <summary>
/// 属性定义
/// </summary>
public sealed record PropertySchema
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "string";

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, PropertySchema>? Properties { get; init; }

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Required { get; init; }

    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PropertySchema? Items { get; init; }

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Enum { get; init; }

    [JsonPropertyName("minLength")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinLength { get; init; }

    [JsonPropertyName("pattern")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Pattern { get; init; }

    [JsonPropertyName("minimum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Minimum { get; init; }

    [JsonPropertyName("minItems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinItems { get; init; }

    [JsonPropertyName("default")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Default { get; init; }
}
