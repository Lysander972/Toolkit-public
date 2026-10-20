using System.Text;

namespace Toolkit.Core.Sdk;

/// <summary>
/// MethodSignature 的扩展方法，提供格式化输出功能
/// </summary>
public static class MethodSignatureExtensions
{
    /// <summary>
    /// 生成 TS SDK 标准路径，必须与 sdk.ts 声明命名一致。
    /// </summary>
    public static string GetTsSdkPath(this MethodSignature sig)
    {
        string tsMethodName = BuiltinMethodMap.Instance.GetTsMethodName(sig);
        return sig switch
        {
            { Category: MethodCategory.GlobalFunc } => BescriptDtsGenerator.BuildTsSdkPath(sig.Category, string.Empty, tsMethodName),
            { Category: MethodCategory.VarFunc, OwnerType: string ownerType } => BescriptDtsGenerator.BuildTsSdkPath(sig.Category, ownerType, tsMethodName),
            { Category: MethodCategory.DeviceAction, OwnerType: string ownerType } => BescriptDtsGenerator.BuildTsSdkPath(sig.Category, ownerType, tsMethodName),
            { Category: MethodCategory.EditorFunc, OwnerType: string ownerType } => BescriptDtsGenerator.BuildTsSdkPath(sig.Category, ownerType, tsMethodName),
            _ => throw new InvalidOperationException($"方法签名缺少 OwnerType: {sig.Category}.{sig.MethodName}")
        };
    }

    /// <summary>
    /// 生成 BES SDK 标准路径。
    /// </summary>
    public static string GetBesSdkPath(this MethodSignature sig)
    {
        string prefix = sig.Category switch
        {
            MethodCategory.GlobalFunc => "fun",
            MethodCategory.VarFunc => "varf",
            MethodCategory.DeviceAction => "act",
            MethodCategory.EditorFunc => "dvf",
            _ => throw new ArgumentOutOfRangeException(nameof(sig.Category), sig.Category, "未知方法分类")
        };
        return sig.OwnerType is null
            ? $"{prefix}.{sig.MethodName}"
            : $"{prefix}.{sig.OwnerType}.{sig.MethodName}";
    }

    /// <summary>
    /// 生成 BES SDK 标准签名。
    /// </summary>
    public static string ToBesSdkDisplayString(this MethodSignature sig)
    {
        return BuildDisplayString(sig, sig.GetBesSdkPath());
    }

    /// <summary>
    /// 生成完整路径，如 "List.add" 或 "createMech"
    /// </summary>
    public static string GetFullPath(this MethodSignature sig)
    {
        return sig.OwnerType is null
            ? sig.MethodName
            : $"{sig.OwnerType}.{sig.MethodName}";
    }

    /// <summary>
    /// 生成显示用签名，如 "List.add(data: Any) -> void"
    /// </summary>
    public static string ToDisplayString(this MethodSignature sig)
    {
        return BuildDisplayString(sig, sig.GetFullPath());
    }

    private static string BuildDisplayString(MethodSignature sig, string methodPath)
    {
        StringBuilder sb = new();
        sb.Append(methodPath);
        sb.Append('(');

        for (int i = 0; i < sig.Parameters.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            MethodParameter p = sig.Parameters[i];
            sb.Append(p.EffectiveName);
            sb.Append(": ");
            sb.Append(p.DisplayTypeName);
        }

        sb.Append(')');

        if (sig.ReturnType is not null)
        {
            sb.Append(" -> ");
            sb.Append(sig.ReturnType);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 生成使用 BES SDK 标准路径的完整文档文本。
    /// </summary>
    public static string GetBesSdkDocumentation(this MethodSignature sig)
    {
        return BuildDocumentation(sig, sig.ToBesSdkDisplayString());
    }

    /// <summary>
    /// 获取完整文档文本，包含摘要、备注和参数说明
    /// </summary>
    public static string GetDocumentation(this MethodSignature sig)
    {
        return BuildDocumentation(sig, sig.ToDisplayString());
    }

    private static string BuildDocumentation(MethodSignature sig, string signatureLine)
    {
        StringBuilder sb = new();

        // 签名
        sb.AppendLine(signatureLine);
        sb.AppendLine();

        SignatureDocumentation.AppendSummary(sb, sig.Summary);
        if (sig.ShouldMarkNotRecommended)
        {
            string label = sig.IsObsolete ? "不推荐(已作废)" : "不推荐";
            sb.Append(label).Append(": ").AppendLine(sig.ResolvedNotRecommendedReason);
            sb.AppendLine();
        }
        SignatureDocumentation.AppendParameters(sb, sig.Parameters, static p => p.EffectiveName, static p => p.DisplayTypeName, BuildParameterDescription);

        // 返回值
        if (sig.ReturnType is not null)
        {
            sb.Append("返回: ");
            sb.AppendLine(sig.ReturnType);
            sb.AppendLine();
        }

        SignatureDocumentation.AppendRemarks(sb, sig.Remarks);

        if (!string.IsNullOrWhiteSpace(sig.Notes))
        {
            sb.AppendLine("注意:");
            sb.AppendLine(sig.Notes);
        }

        return sb.ToString();
    }

    private static string? BuildParameterDescription(MethodParameter param)
    {
        if (param.EnumType is not { } enumType)
        {
            return param.Description;
        }

        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(param.Description))
        {
            parts.Add(param.Description);
        }

        parts.Add(enumType.Summary);
#pragma warning disable TK022 // Options 是 record 可选参数，C# 不允许集合类型作为默认参数值
        if (enumType.Options is { Count: > 0 })
#pragma warning restore TK022
        {
            parts.Add("可选值: " + string.Join(", ", enumType.EffectiveOptions));
        }

        return string.Join("；", parts);
    }
}
