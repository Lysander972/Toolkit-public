using System.Text.Json;
using Toolkit.Core.Bescript;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

internal static class TsViewParameterListParser
{
    internal static (IReadOnlyList<TsViewParamSpec> Parameters, string? Error) Parse(string parameterListText)
    {
        string json = TsCompilerHost.ParseParameterList(parameterListText);
        ParameterListParseResult? result = JsonSerializer.Deserialize<ParameterListParseResult>(json, BodyWritebackJson.Options);
        if (result is null)
            throw new InvalidOperationException("TS 官方参数解析返回空结果。");
        if (result.Error is not null)
            return ([], result.Error);

        List<TsViewParamSpec> parameters = [];
        foreach (ParsedParameter parameter in result.Parameters)
        {
            if (!TsViewNaming.IsValidIdentifier(parameter.Name))
                return ([], $"参数名不合法: {parameter.Name}");
            if (TsViewTypeUtils.TryParseBescriptVarType(parameter.TypeName) is not BescriptVarType)
                return ([], $"参数类型无效: {parameter.TypeName}");
            MethodEnumType? enumType = parameter.TypeName.StartsWith("BeEnum<", StringComparison.Ordinal)
                ? TsViewTypeUtils.ResolveMethodEnumTypeFromTsTypeName(parameter.TypeName)
                : null;
            parameters.Add(new TsViewParamSpec(parameter.Name, parameter.TypeName, enumType));
        }
        return (parameters, null);
    }

    private sealed record ParameterListParseResult(List<ParsedParameter> Parameters, string? Error);
    private sealed record ParsedParameter(string Name, string TypeName);
}
