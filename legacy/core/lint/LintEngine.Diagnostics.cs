using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    // 通用诊断创建方法
    private static LintDiagnostic Diag(string ruleId, LintSeverity severity, string message, LintLocation location)
        => new(ruleId, severity, message, location);

    private static LintDiagnostic DiagError(string ruleId, string message, LintLocation location)
        => new(ruleId, LintSeverity.Error, message, location);

    internal static LintDiagnostic CreateLintEngineErrorDiagnostic(string message, LintLocation location)
        => DiagError(LintRuleIds.LintEngineError, message, location);

    // 全局变量与参数冲突诊断
    private static LintDiagnostic CreateParamGlobalClashDiagnostic(
        string name,
        int deviceId,
        int deviceType,
        string carrierGuid,
        string carrierKind,
        string sourceRelativePath)
        => DiagError(LintRuleIds.ParamClashGlobal, $"自定义方法参数与全局变量同名: {name}",
            new LintLocation(sourceRelativePath, 1));

    private static LintDiagnostic CreateGlobalBuiltinParamClashDiagnostic(
        string name,
        int deviceId,
        int deviceType,
        string carrierGuid,
        string carrierKind,
        string sourceRelativePath)
        => DiagError(LintRuleIds.GlobalClashBuiltinParam, $"全局变量名称与底层方法参数冲突: {name}",
            new LintLocation(sourceRelativePath, 1));

    private static LintDiagnostic CreateGlobalScriptNameClashDiagnostic(
        string name,
        string sourceRelativePath)
        => Diag(LintRuleIds.GlobalClashScriptName, LintSeverity.Warning,
            $"全局变量与脚本同名: {name}。BEScript 支持该重名，但 TS 视图不推荐，会产生大量 TS 语法错误干扰条目",
            new LintLocation(sourceRelativePath, 1));

    private static LintDiagnostic CreateGlobalReturnClashDiagnostic(
        string name,
        int deviceId,
        int deviceType,
        string carrierGuid,
        string carrierKind,
        string sourceRelativePath)
        => DiagError(LintRuleIds.GlobalClashReturn, $"全局变量名称与返回名冲突: {name}",
            new LintLocation(sourceRelativePath, 1));

    private static LintDiagnostic CreateBuiltinMethodClashDiagnostic(string name, int deviceId, int deviceType, string carrierGuid, string carrierKind, string scriptPath)
        => DiagError(LintRuleIds.NamingClashBuiltinMethod, $"自定义脚本方法名与内置零件动作重名（act 写法一致）: {name}",
            new LintLocation(scriptPath, 1));

    private static LintDiagnostic CreateGlobalTypeMismatchDiagnostic(string name, int deviceId, int deviceType, string carrierGuid, string carrierKind, string scriptPath, int line)
        => DiagError(LintRuleIds.VarGlobalTypeMismatch, $"与全局变量同名的变量使用了不同类型: {name}",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateEventNameClashDiagnostic(string scriptName, string scriptPath)
        => DiagError(LintRuleIds.NamingClashEventName,
            $"自定义脚本名'{scriptName}'与该零件类型的事件名重名",
            new LintLocation(scriptPath, 1));

    private static LintDiagnostic CreateBirthBuildingEventClashCoreDiagnostic(
        string storageEventName, string coreEventName, string clashScriptName, string devicePath)
        => Diag(LintRuleIds.BirthBuildingEventNameClashCore, LintSeverity.Warning,
            $"机械产生器事件脚本组'{storageEventName}'(注入到目标核心的'{coreEventName}')与目标机械核心同名事件脚本组中存在同名脚本'{clashScriptName}'，注入时会产生覆盖冲突",
            new LintLocation(devicePath, 1));

    private static LintDiagnostic CreateUnusedVarDiagnostic(string varName, string sourcePath)
        => Diag(LintRuleIds.VarUnused, LintSeverity.Warning,
            $"变量未被使用: {varName}",
            new LintLocation(sourcePath, 1));

    private static LintDiagnostic CreateCallParamCountMismatchDiagnostic(
        string methodPath, int actualCount, int totalCount, LintSeverity severity,
        int deviceId, int deviceType, string carrierGuid, string carrierKind, string scriptPath, int line)
        => Diag(LintRuleIds.CallParamSignatureMismatch, severity,
            $"方法'{methodPath}'参数数量与底层签名不一致：脚本={actualCount}，签名={totalCount}",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateCallNotRecommendedDiagnostic(
        string methodPath,
        MethodSignature methodSig,
        string scriptPath,
        int line)
    {
        string state = "不推荐";
        if (methodSig.IsObsolete)
            state = "已作废";
        return Diag(
            LintRuleIds.CallNotRecommended,
            LintSeverity.Warning,
            $"方法'{methodPath}'为{state}方法：{methodSig.ResolvedNotRecommendedReason}",
            new LintLocation(scriptPath, line));
    }

    private static string ParamLabel(int index, string paramName)
        => $"第 {index + 1} 个参数 {paramName}";

    private static LintDiagnostic CreateCallParamTypeMismatchDiagnostic(
        string methodPath, int index, string paramName, string expectedType, string actualType,
        int deviceId, int deviceType, string carrierGuid, string carrierKind, string scriptPath, int line)
        => DiagError(LintRuleIds.CallParamSignatureMismatch,
            $"方法'{methodPath}'参数类型不一致：{ParamLabel(index, paramName)} 脚本={actualType}，签名={expectedType}",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateCallParamImplicitToStringNotAllowedDiagnostic(
        string methodPath, int index, string paramName, string actualType,
        int deviceId, int deviceType, string carrierGuid, string carrierKind, string scriptPath, int line)
        => DiagError(LintRuleIds.CallParamImplicitToStringNotAllowed,
            $"方法'{methodPath}'不允许把 {actualType} 隐式转为文字：{ParamLabel(index, paramName)} 只允许 String（或改用支持隐式转换的 Text 参数位置）",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateFindExcel2KeyMustNotBeIdDiagnostic(string methodPath, string actualText, string scriptPath, int line)
        => DiagError(LintRuleIds.FindExcel2KeyMustNotBeId,
            $"方法'{methodPath}'禁止用'id'作为查询列名（当前={actualText}）。findExcel2 返回的就是行 ID，用一条数据查询其自身毫无意义，而且会导致报错",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateCallParamAnyMustBeVarDiagnostic(
        string methodPath, int index, string paramName, string actualText,
        int deviceId, int deviceType, string carrierGuid, string carrierKind, string scriptPath, int line)
        => DiagError(LintRuleIds.CallParamAnyMustBeVar,
            $"方法'{methodPath}'Any 参数只允许变量或 Any 常量：{ParamLabel(index, paramName)} 不能写其他类型常量（当前={actualText}）",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateCallConditionConstMustBeVarDiagnostic(
        string methodPath,
        int index,
        string actualText,
        string scriptPath,
        int line)
        => DiagError(
            LintRuleIds.CallConditionConstMustBeVar,
            $"if/while 条件调用'{methodPath}'的第 {index + 1} 个参数禁止写 Bool 常量，必须传变量（当前={actualText}）",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateCallConstValueMustBeBlankDiagnostic(
        string methodPath, int index, string typeName, string actualText,
        int deviceId, int deviceType, string carrierGuid, string carrierKind, string scriptPath, int line)
        => DiagError(LintRuleIds.CallConstValueMustBeBlank,
            $"方法'{methodPath}'常量写法不支持：类型 {typeName} 的常量必须留空（当前={actualText}）",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateCallFilenameNotFoundDiagnostic(
        string methodPath, int paramIndex, string paramName, string fileName, string scriptPath, int line)
        => Diag(LintRuleIds.CallFilenameNotFound, LintSeverity.Error,
            $"方法'{methodPath}'的{ParamLabel(paramIndex, paramName)}传入的文件名'{fileName}'在工程中找不到对应资源；文件参数只填包含扩展名的文件本身名称，不填 GUID 或带路径的名称",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateStructGetterNoCopyDiagnostic(
        string methodPath,
        int deviceId,
        int deviceType,
        string carrierGuid,
        string carrierKind,
        string scriptPath,
        int line)
        => Diag(
            LintRuleIds.StructGetterNoCopy,
            LintSeverity.Warning,
            $"Struct 读取未复制：{methodPath} 的第二个 Bool 参数为 0，会把返回值的标识符直接绑定到Struct的字段上，行为难以预测，请改为 1（复制副本）",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateActAliasNotUniqueDiagnostic(
        string ownerName,
        int matchCount,
        string scriptPath,
        int line)
        => Diag(
            LintRuleIds.CallActAliasNotUnique,
            LintSeverity.Warning,
            $"act 目标别名'{ownerName}'在当前地图/机械内命中 {matchCount} 个零件，目标不唯一；建议消除重名逻辑别名",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateActAliasNotFoundDiagnostic(
        string ownerName,
        string scriptPath,
        int line)
        => DiagError(
            LintRuleIds.CallActAliasNotFound,
            $"act 目标别名'{ownerName}'在当前作用域内找不到任何匹配零件",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateActSelfMustUseThisDiagnostic(
        string ownerName,
        string scriptPath,
        int line)
        => DiagError(
            LintRuleIds.CallActSelfMustUseThis,
            $"act 调用解析到自身，禁止使用'{ownerName}'写法；请改为 this（TS 视图为 Act.self）",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateActCapabilityUnavailableDiagnostic(
        string methodPath,
        string capabilityName,
        int deviceType,
        string scriptPath,
        int line)
        => DiagError(
            LintRuleIds.CallActCapabilityUnavailable,
            $"act 调用'{methodPath}'需要零件具备能力'{capabilityName}'，但目标零件类型 {deviceType} 的当前实例不具备该能力",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateDeviceLogicNameDuplicateDiagnostic(
        string logicName,
        string deviceJsonPath)
        => Diag(
            LintRuleIds.StructDeviceLogicNameDuplicate,
            LintSeverity.Warning,
            $"零件逻辑别名重复:'{logicName}'（同一地图/机械内必须唯一）",
            new LintLocation(deviceJsonPath, 1));

    private static LintDiagnostic CreateDynamicMethodNotFoundDiagnostic(
        string methodPath,
        string targetMethodName,
        string scriptPath,
        int line)
        => DiagError(
            LintRuleIds.CallDynamicMethodNotFound,
            $"动态调用'{methodPath}'的方法名常量'{targetMethodName}'在全工程找不到同名自定义脚本",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateDynamicMapMethodFormatDiagnostic(
        string methodPath,
        string actualText,
        string scriptPath,
        int line)
        => DiagError(
            LintRuleIds.CallDynamicMapMethodFormat,
            $"动态调用'{methodPath}'的方法字符串格式错误:'{actualText}'，必须是'零件名.方法名'",
            new LintLocation(scriptPath, line));

    private static LintDiagnostic CreateDynamicMapMethodNotFoundDiagnostic(
        string methodPath,
        int targetDeviceType,
        string methodName,
        string scriptPath,
        int line)
        => DiagError(
            LintRuleIds.CallDynamicMapMethodNotFound,
            $"动态调用'{methodPath}'的方法'{methodName}'在目标零件类型 {targetDeviceType} 中找不到同名非私有自定义脚本",
            new LintLocation(scriptPath, line));

    #region 诊断结果后处理

    /// <summary>
    /// 规范化诊断信息中的脚本路径（统一斜杠方向）
    /// </summary>
    public static LintDiagnostic NormalizeDiagnostic(LintDiagnostic diag)
    {
        string normalizedPath = WorkspacePath.RequireRelativePathOrEmpty(diag.Location.Path);
        if (string.Equals(normalizedPath, diag.Location.Path, StringComparison.Ordinal))
        {
            return diag;
        }
        return diag with { Location = diag.Location with { Path = normalizedPath } };
    }

    /// <summary>
    /// 去重诊断信息（按 RuleId + Path + Line + Message 去重）
    /// </summary>
    public static LintDiagnostic[] DeduplicateDiagnostics(IEnumerable<LintDiagnostic> diagnostics)
    {
        List<LintDiagnostic> result = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (LintDiagnostic diag in diagnostics)
        {
            string key = string.Join("|",
                diag.RuleId,
                diag.Location.Path,
                diag.Location.Line.ToString(),
                diag.Message);
            if (seen.Add(key))
            {
                result.Add(diag);
            }
        }

        return result.ToArray();
    }

    #endregion
}
