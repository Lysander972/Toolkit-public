using System.Diagnostics.CodeAnalysis;

namespace Toolkit.Core.Infra;

/// <summary>
/// GUID 工具方法。
/// </summary>
public static class GuidText
{
    /// <summary>
    /// GUID 十六进制长度。
    /// </summary>
    public const int HexLength = 32;

    /// <summary>
    /// GUID 纯值正则（供 JSON Schema 等场景引用，确保与 <see cref="Validate"/> 同源）。
    /// </summary>
    public const string HexPattern = "^[0-9a-f]{32}$";

    /// <summary>
    /// 生成新的 GUID（32 位小写十六进制）。
    /// </summary>
    public static string NewGuid() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// 校验 GUID 格式（32 位小写十六进制）。GUID 值校验的唯一实现。
    /// </summary>
    public static bool Validate([NotNullWhen(true)] string? guid)
    {
        if (guid == null || guid.Length != HexLength) return false;
        for (int i = 0; i < HexLength; i++)
        {
            char c = guid[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        }
        return true;
    }

    /// <summary>
    /// 格式错误：格式无效。明确禁止混入转义前缀。
    /// </summary>
    public static string FormatErrorMessage(string fieldName, string actualValue)
    {
        return $"{fieldName} 格式无效：期望纯 32 位小写十六进制（格式示例：deadbeefdeadbeefdeadbeefdeadbeef，禁止混入转义前缀如@1），实际长度: {actualValue.Length}";
    }

    /// <summary>
    /// 未找到错误：指定 GUID 的实体不存在。
    /// </summary>
    public static string NotFoundMessage(string entityType, string guid)
        => $"未找到 {entityType}：{guid}";

    /// <summary>
    /// 已引用错误：该行已是 GUID 引用格式。
    /// </summary>
    public static string AlreadyGuidRefMessage()
        => "该行已是 GUID 引用，无需转换";

    /// <summary>
    /// 数据为空错误：GUID 对应的数据为空。
    /// </summary>
    public static string DataEmptyMessage(string entityType, string guid)
        => $"{entityType}数据为空：{guid}";

    /// <summary>
    /// 已绑定错误：脚本已是代码仓库引用。
    /// </summary>
    public static string AlreadyLinkedMessage(string guid)
        => $"脚本已是代码仓库引用（GUID: {guid}），无需导出";
}
