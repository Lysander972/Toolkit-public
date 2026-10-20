using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toolkit.Mcp;

/// <summary>
/// MCP 模块统一的 JSON 序列化配置
/// </summary>
internal static class McpJsonOptions
{
    /// <summary>
    /// 标准配置：用于反序列化入参
    /// </summary>
    public static readonly JsonSerializerOptions Standard = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 输出配置：用于序列化响应和 Schema，排除 StackTrace 字段
    /// </summary>
    public static readonly JsonSerializerOptions Output = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
