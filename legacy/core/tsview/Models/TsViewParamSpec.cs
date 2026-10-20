using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

/// <summary>
/// TsView 参数规格，包含参数名和 TypeScript 类型名。
/// </summary>
internal sealed record TsViewParamSpec(string Name, string TypeName, MethodEnumType? EnumType = null);

/// <summary>
/// TsView 参数规格工具类，从 BEScript 参数构建 TypeScript 参数列表。
/// 【职责边界】将 BescriptParameter 转换为 TsViewParamSpec，处理类型映射。
/// </summary>
internal static class TsViewParamSpecTools
{
    internal static IReadOnlyList<TsViewParamSpec> BuildFromParameters(
        IReadOnlyList<BescriptParameter> parameters,
        List<ToolError>? warnings,
        string? sourcePath)
    {
        if (parameters.Count == 0)
        {
            return Array.Empty<TsViewParamSpec>();
        }

        List<TsViewParamSpec> list = new(parameters.Count);
        foreach (BescriptParameter p in parameters)
        {
            string tsName = TsViewTypeUtils.ToTsIdentifier(p.Name);
            string typeName = TsViewTypeUtils.ConvertToTsTypeName(p.TypeName);
            if (BescriptVarTypeHelper.TryParse(typeName) is not BescriptVarType varType)
            {
                throw new InvalidOperationException($"参数类型无法识别: {p.Name}:{p.TypeName}");
            }

            // [Type-Check] 检查是否为 Any 类型以选择正确的 TS 类型名，非兜底
            string tsType = TsViewTypeUtils.ToTsBeTypeName(varType);
            if (varType == BescriptVarType.Any)
                tsType = "BeAnyParam";

            // 提取枚举类型（如果参数是 Enum 类型）
            // BescriptParameter.TypeName 可能是具体枚举分组名（如 "KeyCode"）
            MethodEnumType? enumType = null;
            if (varType == BescriptVarType.Enum && !string.Equals(p.TypeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal))
            {
                BuiltinEnumTypes.ByTsTypeName.TryGetValue(p.TypeName, out enumType);
            }

            list.Add(new TsViewParamSpec(tsName, tsType, enumType));
        }

        return list;
    }

    internal static string BuildList(IReadOnlyList<TsViewParamSpec> paramSpecs)
    {
        if (paramSpecs.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", paramSpecs.Select(static p => p.Name + ": " + p.TypeName));
    }

    internal static string BuildNameList(IReadOnlyList<TsViewParamSpec> paramSpecs)
    {
        if (paramSpecs.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", paramSpecs.Select(static p => p.Name));
    }
}
