using System.Text;

namespace Toolkit.Core.Sdk;

/// <summary>
/// EventSignature 的扩展方法，提供格式化输出功能
/// </summary>
public static class EventSignatureExtensions
{
    /// <summary>
    /// 生成完整名称，如 "CoreDevice.beHit"
    /// </summary>
    public static string GetFullName(this EventSignature sig)
    {
        return $"{sig.DeviceType}.{sig.EventName}";
    }

    /// <summary>
    /// 生成显示用签名，如 "CoreDevice.beHit($attacker: Device, ...)"
    /// </summary>
    public static string ToDisplayString(this EventSignature sig)
    {
        StringBuilder sb = new();
        sb.Append(sig.GetFullName());
        sb.Append('(');

        for (int i = 0; i < sig.Parameters.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            EventParameter p = sig.Parameters[i];
            sb.Append(p.EffectiveName);
            sb.Append(": ");
            sb.Append(p.DisplayTypeName);
        }

        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// 生成脚本头部的 params 行文本（仅用于展示，不作为真实脚本声明）
    /// </summary>
    public static string ToParamsLineString(this EventSignature sig)
    {
        if (sig.Parameters.Count == 0)
        {
            return "params:(无)";
        }

        StringBuilder sb = new();
        sb.Append("params:");
        for (int i = 0; i < sig.Parameters.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            EventParameter p = sig.Parameters[i];
            sb.Append(p.DisplayTypeName);
            sb.Append(' ');
            sb.Append(p.EffectiveName);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 获取事件文档文本，包含摘要、备注和参数说明
    /// </summary>
    public static string GetDocumentation(this EventSignature sig)
    {
        StringBuilder sb = new();

        SignatureDocumentation.AppendSummary(sb, sig.Summary);
        SignatureDocumentation.AppendParameters(sb, sig.Parameters, static p => p.EffectiveName, static p => p.DisplayTypeName, static p => p.Description);
        SignatureDocumentation.AppendRemarks(sb, sig.Remarks);

        return sb.ToString();
    }
}
