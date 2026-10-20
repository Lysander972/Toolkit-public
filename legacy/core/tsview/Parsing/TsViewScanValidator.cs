using System.Text.Json;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;

namespace Toolkit.Core.TsView;

/// <summary>
/// TS 视图扫描结果的业务校验。
/// 从 TsViewClassScanner 提取的校验逻辑集中于此。
/// </summary>
internal static class TsViewScanValidator
{
    /// <summary>
    /// 校验区段标记的完整性和顺序。
    /// </summary>
    internal static ToolError? ValidateSections(JsonElement sectionInfo, string filePath)
    {
        bool seenScript = sectionInfo.TryGetProperty("seenScript", out JsonElement ssElem) && ssElem.GetBoolean();
        int scriptEndLine = -1;
        if (sectionInfo.TryGetProperty("scriptEndLine", out JsonElement seElem))
            scriptEndLine = seElem.GetInt32();

        if (!seenScript)
        {
            return new ToolError(filePath, null, null, "缺少脚本区段 BEGIN 标记。", ToolDiagnosticSeverity.Fatal);
        }

        if (scriptEndLine < 0)
        {
            return new ToolError(filePath, null, null, "脚本区段未闭合（缺少 END）。", ToolDiagnosticSeverity.Fatal);
        }

        bool seenGlobals = sectionInfo.TryGetProperty("seenGlobals", out JsonElement sgElem) && sgElem.GetBoolean();
        int globalsEndLine = -1;
        if (sectionInfo.TryGetProperty("globalsEndLine", out JsonElement geElem))
            globalsEndLine = geElem.GetInt32();

        if (seenGlobals && globalsEndLine < 0)
        {
            return new ToolError(filePath, null, null, "Globals 区段未闭合（缺少 END）。", ToolDiagnosticSeverity.Fatal);
        }

        return null;
    }

    /// <summary>
    /// 校验全局变量：转义名检测、名称映射一致性、类型解析。
    /// </summary>
    internal static (ScannedGlobalVar? Var, ToolError? Error) ValidateGlobalVar(
        string rawName,
        string fieldName,
        string typeName,
        string? comment,
        string filePath,
        int lineNumber)
    {
        // 转义名检测
        if (TsViewNaming.TryDetectEscapedIdentifierLiteral(rawName) is { } decodedRawVarName)
        {
            return (null, new ToolError(filePath, lineNumber, null,
                $"@bsVar.name 禁止使用转义后的标识符文本: {rawName}。请使用原始名称: {decodedRawVarName}。{TsViewNaming.EscapedNameAbuseHint}",
                ToolDiagnosticSeverity.Fatal));
        }

        // 名称映射一致性
        string expectedTsVarName = TsViewIdentifierMap.ToTsIdentifier(rawName);
        if (!string.Equals(expectedTsVarName, fieldName, StringComparison.Ordinal))
        {
            return (null, new ToolError(filePath, lineNumber, null,
                $"@bsVar.name 与字段名不一致：name={rawName} 应映射为 {expectedTsVarName}，当前字段名={fieldName}",
                ToolDiagnosticSeverity.Fatal));
        }

        // 类型解析
        if (DeviceTsView.ParseGlobalVarType(typeName) is not BescriptVarType varType)
        {
            return (null, new ToolError(filePath, lineNumber, null,
                $"全局变量类型无效: {typeName}",
                ToolDiagnosticSeverity.Fatal));
        }

        return (new ScannedGlobalVar(fieldName, rawName, varType, comment, lineNumber), null);
    }

    /// <summary>
    /// 校验方法一致性：BeScript.name 转义名检测、retVar 转义名检测、
    /// 参数名规范化、参数类型白名单、方法名一致性。
    /// </summary>
    internal static ToolError? ValidateMethodConsistency(
        IReadOnlyList<ScannedMethod> methods,
        string filePath)
    {
        foreach (ScannedMethod m in methods)
        {
            // BeScript.name 转义名检测
            if (TsViewNaming.TryDetectEscapedIdentifierLiteral(m.RawName) is { } decodedScriptName)
            {
                return new ToolError(filePath, m.DocStartLine, null,
                    $"BeScript.name 禁止使用转义后的标识符文本: {m.RawName}。请使用原始名称: {decodedScriptName}。{TsViewNaming.EscapedNameAbuseHint}",
                    ToolDiagnosticSeverity.Fatal);
            }

            // retVar 转义名检测
            if (m.RetVarFromDecorator != null &&
                TsViewNaming.TryDetectEscapedIdentifierLiteral(m.RetVarFromDecorator) is { } decodedRetVarName)
            {
                return new ToolError(filePath, m.DocStartLine, null,
                    $"BeScript.retVar 禁止使用转义后的标识符文本: {m.RetVarFromDecorator}。请使用原始名称: {decodedRetVarName}。{TsViewNaming.EscapedNameAbuseHint}",
                    ToolDiagnosticSeverity.Fatal);
            }

            // 参数名规范化校验
            foreach (TsViewParamSpec p in m.Params)
            {
                string rawParamName = TsViewIdentifierMap.FromTsIdentifier(p.Name);
                string normalizedTsParamName = TsViewIdentifierMap.ToTsIdentifier(rawParamName);
                if (!string.Equals(p.Name, normalizedTsParamName, StringComparison.Ordinal))
                {
                    return new ToolError(filePath, m.DeclLine, null,
                        $"方法参数名必须使用唯一映射的变量转义名：当前 {p.Name}，应为 {normalizedTsParamName}。{TsViewNaming.EscapedNameAbuseHint}",
                        ToolDiagnosticSeverity.Fatal);
                }
            }

            // 参数类型白名单
            foreach (TsViewParamSpec p in m.Params)
            {
                if (IsDisallowedCustomMethodParamType(p.TypeName))
                {
                    return new ToolError(filePath, m.DeclLine, null,
                        $"自定义方法参数类型 {p.TypeName} 不允许，仅允许具体类型。",
                        ToolDiagnosticSeverity.Fatal);
                }
            }

            // 返回类型白名单已移除（箭头函数无返回类型注解，类型由 retVar 字段决定）

            // 方法名一致性
            string normalizedExpected = TsViewNaming.BuildMethodIdentifier(m.RawName);
            if (!string.Equals(m.Name, normalizedExpected, StringComparison.Ordinal))
            {
                string hint = string.Empty;
                if (m.RawName.Contains('\''))
                    hint = $" BeScript 中的 \"'\"（如 {m.RawName}）会编码为 \"_x27_\"，对应 TS 方法名 {normalizedExpected}。";
                return new ToolError(filePath, m.DeclLine, null,
                    $"方法名不匹配：预期 {normalizedExpected}，当前 {m.Name}。{hint}",
                    ToolDiagnosticSeverity.Fatal);
            }
        }

        return null;
    }

#pragma warning disable TK006 // 类型白名单判定，集中禁用类型集合
    private static bool IsDisallowedCustomMethodParamType(string typeName)
    {
        return typeName is "BeUI" or "BeAny" or "BeVoid" or "BeNumber" or "BeStringConvertible";
    }
#pragma warning restore TK006
}
