using System.Text.Json;

#nullable disable warnings

using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

/// <summary>
/// 基于 V8 + 官方 TypeScript API 的类结构扫描器。
/// 替代 TsViewClassScanner 的文本特判。
/// </summary>
internal static class TsViewAstScanner
{
    public sealed record ScanClassResult(
        List<ScannedGlobalVar> Globals,
        List<ScannedMethod> Methods,
        List<ScannedEventGroup> EventGroups,
        bool HasEventDecorator,
        IReadOnlyList<string> EventDecoratorValues,
        ToolError? Error);

    public static ScanClassResult ScanClass(
        IReadOnlyList<string> lines,
        string className,
        string? entryClassName,
        string filePath)
    {
        string sourceText = string.Join("\n", lines);
        string json;
        try
        {
            json = TsCompilerHost.ScanClassMembers(sourceText, className, entryClassName);
        }
        catch (Exception ex)
        {
            return new ScanClassResult([], [], [], false, [], new ToolError(filePath, null, null, $"V8 扫描异常: {ex.Message}", ToolDiagnosticSeverity.Fatal));
        }

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // 检查 JS 端错误
        if (root.TryGetProperty("error", out JsonElement errElem))
        {
            int? errLine = null;
            if (root.TryGetProperty("errorLine", out JsonElement lineElem))
                errLine = lineElem.GetInt32();
            string errorMessage = errElem.GetString();
            return new ScanClassResult([], [], [], false, [], new ToolError(filePath, errLine, null, errorMessage, ToolDiagnosticSeverity.Fatal));
        }

        List<ScannedGlobalVar> globals = [];
        List<ScannedMethod> methods = [];
        List<ScannedEventGroup> eventGroups = [];

        // 区段校验
        if (root.TryGetProperty("sectionInfo", out JsonElement sectionElem))
        {
            ToolError? sectionError = TsViewScanValidator.ValidateSections(sectionElem, filePath);
            if (sectionError != null)
                return new ScanClassResult([], [], [], false, [], sectionError);
        }

        // 反序列化全局变量
        if (root.TryGetProperty("globals", out JsonElement globalsElem))
        {
            foreach (JsonElement g in globalsElem.EnumerateArray())
            {
                string name = g.GetProperty("name").GetString();
                string rawName = g.GetProperty("rawName").GetString();
                string typeName = g.GetProperty("typeName").GetString();
                string? comment = null;
                if (g.TryGetProperty("comment", out JsonElement cElem))
                    comment = cElem.GetString();
                int lineNumber = g.GetProperty("lineNumber").GetInt32();

                (ScannedGlobalVar? scannedVar, ToolError? varError) = TsViewScanValidator.ValidateGlobalVar(rawName, name, typeName, comment, filePath, lineNumber);
                if (varError != null)
                    return new ScanClassResult([], [], [], false, [], varError);
                globals.Add(scannedVar);
            }
        }

        // 反序列化方法
        if (root.TryGetProperty("methods", out JsonElement methodsElem))
        {
            foreach (JsonElement m in methodsElem.EnumerateArray())
                methods.Add(DeserializeMethod(m));
        }

        // 反序列化事件组
        if (root.TryGetProperty("eventGroups", out JsonElement egElem))
        {
            foreach (JsonElement eg in egElem.EnumerateArray())
            {
                string eventName = eg.GetProperty("eventName").GetString();
                List<ScannedMethod> memberMethods = [];
                if (eg.TryGetProperty("members", out JsonElement membersElem))
                {
                    foreach (JsonElement mm in membersElem.EnumerateArray())
                        memberMethods.Add(DeserializeMethod(mm));
                }
                eventGroups.Add(new ScannedEventGroup(eventName, memberMethods));
            }
        }

        // 后置校验
        bool hasEventDecorator = root.GetProperty("hasEventDecorator").GetBoolean();
        IReadOnlyList<string> eventDecoratorValues = root.GetProperty("eventDecoratorValues").EnumerateArray().Select(value => value.GetString()).ToArray();

        ToolError? consistencyError = TsViewScanValidator.ValidateMethodConsistency(methods, filePath);
        if (consistencyError != null)
            return new ScanClassResult([], [], [], false, [], consistencyError);

        return new ScanClassResult(globals, methods, eventGroups, hasEventDecorator, eventDecoratorValues, null);
    }

    private static ScannedMethod DeserializeMethod(JsonElement m)
    {
        string name = m.GetProperty("name").GetString();
        string rawName = m.GetProperty("rawName").GetString();
        bool isPrivate = m.GetProperty("isPrivate").GetBoolean();
        bool isProtected = m.GetProperty("isProtected").GetBoolean();
        string returnType = m.GetProperty("returnType").GetString();
        int docStartLine = m.GetProperty("docStartLine").GetInt32();
        int declLine = m.GetProperty("declLine").GetInt32();
        int endLine = m.GetProperty("endLine").GetInt32();

        string? codeRepoGuid = null;
        if (m.TryGetProperty("codeRepoGuid", out JsonElement crElem))
            codeRepoGuid = crElem.GetString();

        string? retVar = null;
        if (m.TryGetProperty("retVarFromDecorator", out JsonElement rvElem))
            retVar = rvElem.GetString();

        string? retVarType = null;
        if (m.TryGetProperty("retVarTypeFromDecorator", out JsonElement rvtElem))
            retVarType = rvtElem.GetString();
        bool hasBeScript = m.TryGetProperty("hasBeScriptDecorator", out JsonElement hbElem) && hbElem.GetBoolean();

        List<TsViewParamSpec> paramSpecs = [];
        if (m.TryGetProperty("params", out JsonElement paramsElem))
        {
            foreach (JsonElement p in paramsElem.EnumerateArray())
            {
                string pName = p.GetProperty("name").GetString();
                string pType = p.GetProperty("typeName").GetString();
                MethodEnumType? enumType = null;
                if (pType.StartsWith("BeEnum<", StringComparison.Ordinal))
                    enumType = TsViewTypeUtils.ResolveMethodEnumTypeFromTsTypeName(pType);
                paramSpecs.Add(new TsViewParamSpec(pName, pType, enumType));
            }
        }

        List<(int LineNumber, string LineText)> bodyLines = [];
        if (m.TryGetProperty("bodyLines", out JsonElement blElem))
        {
            foreach (JsonElement bl in blElem.EnumerateArray())
            {
                int ln = bl.GetProperty("lineNumber").GetInt32();
                string lt = bl.GetProperty("lineText").GetString();
                bodyLines.Add((ln, lt));
            }
        }

        BeScriptDecoratorInfo? bsInfo = null;
        if (m.TryGetProperty("beScriptInfo", out JsonElement bsElem))
            bsInfo = DeserializeBeScriptInfo(bsElem);

        return new ScannedMethod(
            name, rawName, isPrivate, isProtected,
            paramSpecs, bodyLines, docStartLine, declLine, endLine,
            codeRepoGuid, retVar, retVarType, hasBeScript, bsInfo);
    }

    private static BeScriptDecoratorInfo DeserializeBeScriptInfo(JsonElement elem)
    {
        string name = elem.GetProperty("name").GetString();
        string? retVar = GetNullableString(elem, "retVar");
        string? retVarType = GetNullableString(elem, "retVarType");
        bool? debug = GetNullableBool(elem, "debug");
        bool? skip = GetNullableBool(elem, "skip");
        bool? isPrivate = GetNullableBool(elem, "isPrivate");
        string? comment = GetNullableString(elem, "comment");
        string? rawParams = GetNullableString(elem, "rawParams");
        string? rawRetVar = GetNullableString(elem, "rawRetVar");
        string? repo = GetNullableString(elem, "repo");
        bool isAsync = elem.TryGetProperty("isAsync", out JsonElement asyncElem) && asyncElem.GetBoolean();

        IReadOnlyList<int>? color = null;
        if (elem.TryGetProperty("color", out JsonElement colorElem) && colorElem.ValueKind == JsonValueKind.Array)
        {
            List<int> colorList = [];
            foreach (JsonElement c in colorElem.EnumerateArray())
                colorList.Add(c.GetInt32());
            color = colorList;
        }

        return new BeScriptDecoratorInfo(name, retVar, retVarType, debug, skip, isPrivate, comment, color, rawParams, rawRetVar, repo, isAsync);
    }

    private static string? GetNullableString(JsonElement elem, string prop)
    {
        if (!elem.TryGetProperty(prop, out JsonElement val)) return null;
        return val.GetString();
    }

#pragma warning disable TK006 // JSON 属性解析，与 GetNullableString 对称
    private static bool? GetNullableBool(JsonElement elem, string prop)
    {
        if (!elem.TryGetProperty(prop, out JsonElement val)) return null;
        if (val.ValueKind == JsonValueKind.Null) return null;
        return val.GetBoolean();
    }
#pragma warning restore TK006
}
