using Toolkit.Core.Bescript;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

/// <summary>
/// TS 视图调用 → BEScript 调用映射器（仅负责调用层，不处理语句结构）。
/// </summary>
/// <remarks>
/// <para><b>Act 调用映射规则</b></para>
/// <list type="bullet">
///   <item>类名 ↔ ID 双射：ByClassName 是唯一查找入口，ID 来源唯一是文件名</item>
///   <item>DeviceTypeId 直接来自 device.json，不做任何转换或兜底</item>
///   <item>ActionOwnerTypeName 用于方法签名查找，与 DeviceTypeId 独立</item>
/// </list>
/// <para><b>机械产生器规则</b></para>
/// <list type="bullet">
///   <item>机械产生器(1110)是地图级零件，不属于任何机械内部</item>
///   <item>1110 内部：this 指向目标机械核心(类型1)，不可能出现 1110.this</item>
///   <item>1110 内部：不能调用自身，也不能调用其他 1110 零件</item>
///   <item>其他零件：可以调用 1110./XXX.XXX，但只能调用固有 Act，不能调用自定义脚本</item>
/// </list>
/// </remarks>
internal static class TsViewCallMapper
{
    internal const string MissingActTargetErrorMessage = "Act 目标未解析：找不到目标零件，请确保引用的零件 ID 或别名正确存在。";

    private readonly record struct ArgBuildResult(List<BescriptCallArgument> Args, string? Error, TsViewDiagnosticTag DiagnosticTag);

    internal static ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> MapToBescript(
        TsViewCallExpr call,
        ScriptConversionContext context)
    {
        switch (call.Kind)
        {
            case TsViewCallKind.Fun:
                {
                    ParseResult<BescriptCallSignature> result = MapFunCall(call, context);
                    return result.Success
                        ? new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.OkResult((result.Value, null))
                        : new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.FailResult(result.Error, result.DiagnosticTag);
                }
            case TsViewCallKind.Varf:
                {
                    ParseResult<BescriptCallSignature> result = MapVarfCall(call, context);
                    return result.Success
                        ? new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.OkResult((result.Value, null))
                        : new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.FailResult(result.Error, result.DiagnosticTag);
                }
            case TsViewCallKind.Act:
                return MapActCall(call, context);
            case TsViewCallKind.Dvf:
                {
                    ParseResult<BescriptCallSignature> result = MapDvfCall(call, context);
                    return result.Success
                        ? new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.OkResult((result.Value, null))
                        : new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.FailResult(result.Error, result.DiagnosticTag);
                }
            default:
                return new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.FailResult("不支持的调用类型。");
        }
    }

    internal static bool ShouldPrefixReturn(
        TsViewCallExpr call,
        BescriptCallSignature sig,
        ScriptConversionContext context,
        ActTargetInfo? resolvedActTarget)
    {
        string? returnType = ResolveReturnType(call, sig, context, resolvedActTarget);
        return returnType is not null &&
               !returnType.Equals("void", StringComparison.Ordinal);
    }

    private static ParseResult<BescriptCallSignature> MapFunCall(
        TsViewCallExpr call,
        ScriptConversionContext context)
    {
        if (call.MethodPath is not string methodPath)
            return new ParseResult<BescriptCallSignature>.FailResult("全局调用缺少方法路径。");

        string? normalizedGeneric = null;
        if (call.GenericBeTypeName is not null)
            normalizedGeneric = TsViewTypeUtils.ConvertToTsTypeName(call.GenericBeTypeName);
        CorrectionResolve resolved = ResolveGlobalWithCorrection(methodPath, normalizedGeneric, call.Args.Count);
        if (!resolved.Result.Found)
            return new ParseResult<BescriptCallSignature>.FailResult($"{resolved.Result.Error}；BeScript 的 fun/varf 关键字方法完全不可自定义，禁止伪造方法，全部的底层方法都可以到 sdk.ts 上查找。");
        if (resolved.Corrected)
            context.ExpansionWarnings.Add(BuildCorrectionWarning(context, resolved));

        string besMethodName = resolved.Result.BesMethodName;

        if (context.MethodRegistry.GetByPath(besMethodName, MethodCategory.GlobalFunc) is not MethodSignature methodSig)
            throw new InvalidOperationException($"全局方法映射缺少 SDK 签名: {besMethodName}");
        ArgBuildResult argsResult = BuildBescriptArgs(call, context, methodSig);
        List<BescriptCallArgument> args = argsResult.Args;
        if (argsResult.Error is not null)
            return new ParseResult<BescriptCallSignature>.FailResult(argsResult.Error, argsResult.DiagnosticTag);

        string? enumError = ValidateEnumConstArgs(call, methodSig, context);
        if (enumError is not null)
            return new ParseResult<BescriptCallSignature>.FailResult(enumError);

        return new ParseResult<BescriptCallSignature>.OkResult(new FunCallSignature(
            besMethodName,
            besMethodName,
            args,
            SegmentSpans: [],
            null));
    }

    private static ParseResult<BescriptCallSignature> MapVarfCall(
        TsViewCallExpr call,
        ScriptConversionContext context)
    {
        if (call.Target is not string target)
            return new ParseResult<BescriptCallSignature>.FailResult("varf 调用缺少 target。");

        ParseResult<string> ownerTypeResult = ResolveVarOwnerType(call, context);
        if (!ownerTypeResult.Success)
            return new ParseResult<BescriptCallSignature>.FailResult(ownerTypeResult.Error, ownerTypeResult.DiagnosticTag);
        string ownerType = ownerTypeResult.Value;

        if (call.MethodName is not string tsMethodName)
            return new ParseResult<BescriptCallSignature>.FailResult("varf 调用缺少方法名。");

        string? normalizedGeneric = null;
        if (call.GenericBeTypeName is not null)
            normalizedGeneric = TsViewTypeUtils.ConvertToTsTypeName(call.GenericBeTypeName);
        CorrectionResolve resolved = ResolveVarFuncWithCorrection(ownerType, tsMethodName, normalizedGeneric, call.Args.Count);
        if (!resolved.Result.Found)
            return new ParseResult<BescriptCallSignature>.FailResult($"{resolved.Result.Error}；BeScript 的 fun/varf 关键字方法完全不可自定义，禁止伪造方法，全部的底层方法都可以到 sdk.ts 上查找。");
        if (resolved.Corrected)
            context.ExpansionWarnings.Add(BuildCorrectionWarning(context, resolved));

        string besMethodName = resolved.Result.BesMethodName;

        if (context.MethodRegistry.GetByPathWithInheritance(
            ownerType + "." + besMethodName,
            MethodCategory.VarFunc) is not MethodSignature methodSig)
            throw new InvalidOperationException($"变量方法映射缺少 SDK 签名: {ownerType}.{besMethodName}");
        ArgBuildResult argsResult = BuildBescriptArgs(call, context, methodSig);
        List<BescriptCallArgument> args = argsResult.Args;
        if (argsResult.Error is not null)
            return new ParseResult<BescriptCallSignature>.FailResult(argsResult.Error, argsResult.DiagnosticTag);

        string? enumError = ValidateEnumConstArgs(call, methodSig, context);
        if (enumError is not null)
            return new ParseResult<BescriptCallSignature>.FailResult(enumError);

        string decodedTarget = TsViewIdentifierMap.FromTsIdentifier(target);
        string saveName = $"{ownerType}.{decodedTarget}.{besMethodName}";
        return new ParseResult<BescriptCallSignature>.OkResult(new VarfCallSignature(
            ownerType,
            decodedTarget,
            false,
            besMethodName,
            saveName,
            args,
            SegmentSpans: [],
            null));
    }

    private static ParseResult<BescriptCallSignature> MapDvfCall(
        TsViewCallExpr call,
        ScriptConversionContext context)
    {
        if (call.Target is null ||
            call.TargetType is not string targetType)
        {
            return new ParseResult<BescriptCallSignature>.FailResult("dvf 调用缺少 target/targetType。");
        }

        if (call.MethodName is not string tsMethodName)
            return new ParseResult<BescriptCallSignature>.FailResult("dvf 调用缺少方法名。");

        string? normalizedGeneric = null;
        if (call.GenericBeTypeName is not null)
            normalizedGeneric = TsViewTypeUtils.ConvertToTsTypeName(call.GenericBeTypeName);
        CorrectionResolve resolved = ResolveVarFuncWithCorrection(targetType, tsMethodName, normalizedGeneric, call.Args.Count);
        if (!resolved.Result.Found)
            return new ParseResult<BescriptCallSignature>.FailResult($"{resolved.Result.Error}；BeScript 的 fun/varf 关键字方法完全不可自定义，禁止伪造方法，全部的底层方法都可以到 sdk.ts 上查找。");
        if (resolved.Corrected)
            context.ExpansionWarnings.Add(BuildCorrectionWarning(context, resolved));

        string besMethodName = resolved.Result.BesMethodName;

        if (context.MethodRegistry.GetByPathWithInheritance(
            targetType + "." + besMethodName,
            MethodCategory.VarFunc) is not MethodSignature methodSig)
            throw new InvalidOperationException($"编辑器方法映射缺少 SDK 签名: {targetType}.{besMethodName}");
        ArgBuildResult argsResult = BuildBescriptArgs(call, context, methodSig);
        List<BescriptCallArgument> args = argsResult.Args;
        if (argsResult.Error is not null)
            return new ParseResult<BescriptCallSignature>.FailResult(argsResult.Error, argsResult.DiagnosticTag);

        string? enumError = ValidateEnumConstArgs(call, methodSig, context);
        if (enumError is not null)
            return new ParseResult<BescriptCallSignature>.FailResult(enumError);

        string decodedTarget = TsViewIdentifierMap.FromTsIdentifier(call.Target);
        string saveName = $"{decodedTarget}.{targetType}.{besMethodName}";
        return new ParseResult<BescriptCallSignature>.OkResult(new DvfCallSignature(
            targetType,
            decodedTarget,
            false,
            besMethodName,
            saveName,
            args,
            SegmentSpans: [],
            null));
    }

    private static ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> MapActCall(
        TsViewCallExpr call,
        ScriptConversionContext context)
    {
        if (call.ActTarget is null || call.MethodName is not string methodName)
            return new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.FailResult("act 调用缺少目标或方法名。");

        // ═══════════════════════════════════════════════════════════════════
        // Act 目标查找：统一用 ByClassName（类名 ↔ ID 双射，ID 来源唯一是文件名）
        // 降级情况：ActTargetType 是 DeviceType_XXX 格式（未找到具体零件类）
        // ═══════════════════════════════════════════════════════════════════
        if (call.ActTargetType is not string actTargetType)
            return new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.FailResult("Act 调用缺少类型参数。");

        if (!context.ActTargetMap.ByClassName.TryGetValue(actTargetType, out ActTargetInfo? targetInfo))
        {
            // 降级路径：检查是否为 DeviceType_XXX 格式（缺失零件占位符）
            if (call.ActTargetType is string unresolvedType
                && unresolvedType.StartsWith("DeviceType_", StringComparison.Ordinal)
                && call.ActTarget is TsViewArg unresolvedTarget
                && (unresolvedTarget.Kind == TsViewArgKind.NumberLiteral || unresolvedTarget.Kind == TsViewArgKind.StringLiteral))
            {
                ArgBuildResult unresolvedArgsResult = BuildBescriptArgs(call.Args, context);
                List<BescriptCallArgument> unresolvedArgs = unresolvedArgsResult.Args;
                if (unresolvedArgsResult.Error is not null)
                    return new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.FailResult(unresolvedArgsResult.Error, unresolvedArgsResult.DiagnosticTag);

                ParseResult<BescriptCallSignature> unresolvedResult = MapUnresolvedActCall(call, unresolvedArgs, unresolvedType, unresolvedTarget, methodName);
                return unresolvedResult.Success
                    ? new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.OkResult((unresolvedResult.Value, null))
                    : new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.FailResult(unresolvedResult.Error, unresolvedResult.DiagnosticTag);
            }

            return new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.FailResult($"Act 目标类名未找到: {actTargetType}");
        }

        if (IsActSelfTarget(call.ActTarget))
        {
            targetInfo = context.ActTargetMap.Self;
        }

        // ═══════════════════════════════════════════════════════════════════
        // DeviceTypeId 直接来自 ActTargetInfo（源自 device.json）
        // 对于 1110 类型零件，输出的就是 "1110"，不做任何转换
        // ActionOwnerTypeName 用于方法签名查找，与 DeviceTypeId 独立
        // ═══════════════════════════════════════════════════════════════════
        string ownerTypeIdText = targetInfo.DeviceTypeId.ToString();
        string actionOwnerTypeName = targetInfo.ActionOwnerTypeName;

        // ownerName 从 ActTarget 参数确定调用形式
        string ownerName;
        bool ownerNameIsAlias;
        if (IsActSelfTarget(call.ActTarget))
        {
            ownerName = "this";
            ownerNameIsAlias = false;
        }
        else if (targetInfo.LogicName is string logicName)
        {
            ownerName = logicName;
            ownerNameIsAlias = true;
        }
        else
        {
            ownerName = targetInfo.DeviceId.ToString();
            ownerNameIsAlias = false;
        }

        string? besMethodName;
        string tsMethodName = methodName;

        // 先尝试自定义脚本解析
        besMethodName = targetInfo.ScriptMethodNameToRawName.TryGetValue(tsMethodName, out string? raw)
            ? raw
            : null;
        if (besMethodName is not null)
        {
            // 自定义脚本调用
            ArgBuildResult customArgsResult = BuildBescriptArgs(call.Args, context);
            List<BescriptCallArgument> customArgs = customArgsResult.Args;
            if (customArgsResult.Error is not null)
                return new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.FailResult(customArgsResult.Error, customArgsResult.DiagnosticTag);

            string customSaveName = $"{ownerTypeIdText}.{ownerName}.{besMethodName}";
            return new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.OkResult((new ActCallSignature(
                ownerTypeIdText,
                ownerName,
                ownerNameIsAlias,
                besMethodName,
                customSaveName,
                customArgs,
                SegmentSpans: [],
                null), targetInfo));
        }
        else
        {
            // 内置方法解析
            string? normalizedGeneric = null;
            if (call.GenericBeTypeName is not null)
                normalizedGeneric = TsViewTypeUtils.ConvertToTsTypeName(call.GenericBeTypeName);
            CorrectionResolve resolved = ResolveDeviceActionWithCorrection(actionOwnerTypeName, tsMethodName, normalizedGeneric, call.Args.Count);
            if (!resolved.Result.Found)
                return new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.FailResult(resolved.Result.Error);
            if (resolved.Corrected)
                context.ExpansionWarnings.Add(BuildCorrectionWarning(context, resolved));

            besMethodName = TsViewIdentifierMap.FromTsIdentifier(resolved.Result.BesMethodName);
        }

        if (context.MethodRegistry.GetByPathWithInheritance(
            actionOwnerTypeName + "." + besMethodName,
            MethodCategory.DeviceAction) is not MethodSignature methodSig)
            throw new InvalidOperationException($"Act 方法映射缺少 SDK 签名: {actionOwnerTypeName}.{besMethodName}");
        ArgBuildResult argsResult = BuildBescriptArgs(call, context, methodSig);
        List<BescriptCallArgument> args = argsResult.Args;
        if (argsResult.Error is not null)
            return new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.FailResult(argsResult.Error, argsResult.DiagnosticTag);

        string? enumError = ValidateEnumConstArgs(call, methodSig, context);
        if (enumError is not null)
            return new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.FailResult(enumError);

        string saveName = $"{ownerTypeIdText}.{ownerName}.{besMethodName}";
        return new ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)>.OkResult((new ActCallSignature(
            ownerTypeIdText,
            ownerName,
            ownerNameIsAlias,
            besMethodName,
            saveName,
            args,
            SegmentSpans: [],
            null), targetInfo));
    }

    /// <summary>
    /// 校验 BeConst 参数与期望参数类型的兼容性，以及 Enum 参数位的数值合法性。
    /// </summary>
    /// <remarks>
    /// <para><b>Enum 校验设计思路：按数值判定，不按声明的类型名判定</b></para>
    /// <para>Agent 可能写错泛型位置（BeEnum&lt;T&gt;.fromBeConst 而非标准 BeEnum.fromBeConst&lt;T&gt;）、
    /// 漏写泛型（BeEnum.fromBeConst）、或声明了错误的枚举类型（BeEnum&lt;WrongType&gt;.fromBeConst(RightType.Member)）。
    /// 这些情况下，只要参数是已知枚举成员，TryParseConstValue 就能提取出有效数值。
    /// 此处只需判定该数值是否是目标枚举的合法取值：是则接受（格式不规范时发警告纠正），否则拒绝。</para>
    /// </remarks>
    private static string? ValidateEnumConstArgs(
        TsViewCallExpr call,
        MethodSignature methodSig,
        ScriptConversionContext context)
    {
        int count = Math.Min(call.Args.Count, methodSig.Parameters.Count);
        for (int i = 0; i < count; i++)
        {
            TsViewArg arg = call.Args[i];
            MethodParameter expectedParam = methodSig.Parameters[i];

            // ─── 跨类型兼容：仅对 BeConst 做，字面量由 BuildBescriptArgs 转换后自然处理 ───
            if (arg.Kind == TsViewArgKind.BeConst && arg.BeTypeName is string argBeTypeName)
            {
                string? compatError = ValidateArgParamTypeCompat(i, argBeTypeName, expectedParam);
                if (compatError is not null)
                    return compatError;
            }

            // ─── Enum 参数位：校验数值合法性（BeConst<Enum> 和将被自动转换的字面量） ───
            if (!string.Equals(expectedParam.TypeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal))
                continue;

            bool isEnumBeConst = arg.Kind == TsViewArgKind.BeConst &&
                                 arg.BeTypeName is string bt && string.Equals(bt, nameof(BescriptVarType.Enum), StringComparison.Ordinal);
            bool isAutoConvertLiteral = arg.Kind is TsViewArgKind.NumberLiteral or TsViewArgKind.StringLiteral;

            if (!isEnumBeConst && !isAutoConvertLiteral)
                continue;

            MethodEnumType? expectedEnumType = expectedParam.EnumType;
            if (expectedEnumType is null)
                return $"内置方法签名缺少枚举组: {methodSig.GetFullPath()} 参数 {expectedParam.EffectiveName}";

            if (ReferenceEquals(expectedEnumType, MethodEnumType.FollowVarfOwner))
            {
                if (call.Target is not string enumTarget ||
                    !context.VariableEnumTypes.TryGetValue(enumTarget, out MethodEnumType? ownerEnumType))
                    return $"参数 {i + 1}: 变量 {call.Target} 的枚举类型未声明。正确写法应使用 var 声明时指定具体枚举类型: var {call.Target}: BeEnum<具体枚举类型> = ...。";

                expectedEnumType = ownerEnumType;
            }

            // 校验的是数值而非类型名——只要数值在目标枚举中存在就合法
            if (BuiltinEnumTypes.GetMemberByRawValue(expectedEnumType, arg.Raw) is null)
                return $"参数 {i + 1}: 数值 {arg.Raw} 不是枚举 {expectedEnumType.Name} 的合法取值。正确写法: BeEnum<{expectedEnumType.Name}>.fromBeConst({expectedEnumType.Name}.具体成员)。";

            // 格式不规范（声明的枚举类型名与参数要求的不一致）→ 警告纠正
            if (isEnumBeConst && !string.Equals(arg.EnumTypeName, expectedEnumType.Name, StringComparison.Ordinal))
            {
                context.ExpansionWarnings.Add(new ToolError(
                    context.ExpansionTargetPath, null, null,
                    $"参数 {i + 1}: 枚举格式不规范（声明的枚举类型 {arg.EnumTypeName} 与参数要求的 {expectedEnumType.Name} 不一致），数值已按 {arg.Raw} 接受。正确写法: BeEnum<{expectedEnumType.Name}>.fromBeConst({expectedEnumType.Name}.具体成员)。",
                    ToolDiagnosticSeverity.Warning));
            }
        }

        return null;
    }

    /// <summary>
    /// 校验 BeConst 参数类型与期望参数类型基本兼容。
    /// 禁止 Long/Float/String 等非 Enum 类型塞给 Enum 参数，反之亦然。
    /// </summary>
    private static string? ValidateArgParamTypeCompat(int paramIndex, string argBeTypeName, MethodParameter expectedParam)
    {
        string expectedTypeName = expectedParam.TypeName;

        bool argIsEnum = string.Equals(argBeTypeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal);
        bool paramIsEnum = string.Equals(expectedTypeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal);

        if (argIsEnum && !paramIsEnum)
            return $"参数 {paramIndex + 1}: 类型不匹配，传入枚举常量但参数期望类型为 {expectedTypeName}。";

        if (!argIsEnum && paramIsEnum)
            return $"参数 {paramIndex + 1}: 类型不匹配，传入 {argBeTypeName} 常量但参数期望类型为 Enum。正确写法: BeEnum<具体类型>.fromBeConst(具体类型.成员)。";

        return null;
    }

    private static string? ResolveReturnType(
        TsViewCallExpr call,
        BescriptCallSignature sig,
        ScriptConversionContext context,
        ActTargetInfo? targetInfo)
    {
        switch (call.Kind)
        {
            case TsViewCallKind.Fun:
                return context.MethodRegistry.GetByPath(sig.FunctionName, MethodCategory.GlobalFunc)?.ReturnType;

            case TsViewCallKind.Varf:
                return context.MethodRegistry.GetByPathWithInheritance(
                    ((VarfCallSignature)sig).OwnerType + "." + sig.FunctionName,
                    MethodCategory.VarFunc)?.ReturnType;
            case TsViewCallKind.Dvf:
                return context.MethodRegistry.GetByPathWithInheritance(
                    ((DvfCallSignature)sig).OwnerType + "." + sig.FunctionName,
                    MethodCategory.VarFunc)?.ReturnType;

            case TsViewCallKind.Act:
                if (targetInfo == null) throw new InvalidOperationException("Act 调用缺少 targetInfo");
                if (targetInfo.ScriptRawNameToReturnType.TryGetValue(sig.FunctionName, out string? customRet))
                {
                    return customRet;
                }

                string ownerTypeName = targetInfo.ActionOwnerTypeName;
                return context.MethodRegistry.GetByPathWithInheritance(
                    ownerTypeName + "." + sig.FunctionName,
                    MethodCategory.DeviceAction)?.ReturnType;

            default:
                return null;
        }
    }

    /// <summary>
    /// 解析 varf 调用的变量类型。
    /// </summary>
    /// <remarks>
    /// <para><b>varf 类型解析规则</b></para>
    /// <list type="bullet">
    ///   <item>优先使用 as 断言中的类型（TargetType）</item>
    ///   <item>其次从上下文推断（DeclaredVariables / GlobalVariables）</item>
    ///   <item>悬空变量必须有 as 断言，否则报错</item>
    /// </list>
    /// <para><b>与 BES → TS 对应关系</b></para>
    /// <list type="bullet">
    ///   <item>类型一致：xxx.method() — 从上下文推断</item>
    ///   <item>类型不一致或悬空：(xxx as BeType).method() — 从 as 断言获取</item>
    /// </list>
    /// </remarks>
    private static ParseResult<string> ResolveVarOwnerType(TsViewCallExpr call, ScriptConversionContext context)
    {
        // 优先使用 as 断言中的类型
        if (call.TargetType is not null)
            return new ParseResult<string>.OkResult(call.TargetType);

        if (call.Target is null)
            return new ParseResult<string>.FailResult("varf 目标缺失。");

        string target = call.Target;

        // 从上下文推断类型
        if (context.DeclaredVariables.TryGetValue(target, out BescriptVarType declared))
            return new ParseResult<string>.OkResult(declared.ToString());

        if (context.GlobalVariables.TryGetValue(target, out BescriptVarType globalType))
        {
            if (!call.UseThisTarget)
                return new ParseResult<string>.FailResult($"全局变量必须显式使用 this.: {target}");

            return new ParseResult<string>.OkResult(globalType.ToString());
        }

        if (call.UseThisTarget)
            return new ParseResult<string>.FailResult(
                $"this. 仅允许用于全局变量: {target},如果确实要使用全局变量那么应该检查拼写是否准确",
                TsViewDiagnosticTag.ForSubject(TsViewDiagnosticCodes.InvalidThisGlobalReference, target));

        // 悬空变量且无 as 断言
        return new ParseResult<string>.FailResult($"未知变量: {target}。局部变量必须使用 var 声明；全局变量必须使用 this.<变量名>。");
    }

    /// <summary>
    /// 构建 BES 调用参数列表。
    /// </summary>
    /// <remarks>
    /// <para><b>实参规则</b></para>
    /// <list type="bullet">
    ///   <item>标识符参数：直接输出为 var 参数</item>
    ///   <item>this.xxx 参数：全局变量，输出为 var 参数</item>
    ///   <item>BeConst 参数：常量，输出为类型化常量</item>
    ///   <item>悬空变量（未声明的标识符）：原版允许，直接输出为 var 参数</item>
    /// </list>
    /// </remarks>
    /// <summary>
    /// 构建参数列表，根据方法签名期望类型将字面量自动转为标准 BeConst 形式。
    /// </summary>
    /// <remarks>
    /// <para><b>设计思路：自动纠正仅负责转换，不重复校验</b></para>
    /// <para>字面量 → BeConst 的类型转换在此完成。Enum 数值合法性由 ValidateEnumConstArgs 统一校验，
    /// 跨类型兼容（Enum 常量塞非 Enum 位 / 反之）由 ValidateArgParamTypeCompat 负责。</para>
    /// <para><b>自动纠正规则（能纠正就纠正+警告，不能纠正就是标准错误）</b></para>
    /// <list type="bullet">
    ///   <item>数字字面量 42 → Float/Long/Bool/Enum 按签名转型为 BeXxx.fromBeConst("42")</item>
    ///   <item>字符串字面量 "42" → Float/Long/Bool/Vector3/Color/Enum 按签名转型</item>
    ///   <item>布尔字面量 true/false → Bool 转型为 BeBool.fromBeConst("1")/("0")</item>
    ///   <item>标识符 True/False → Bool 转型为 BeBool.fromBeConst("1")/("0")</item>
    ///   <item>数组字面量 [1,2,3] → 在 TryParseArg 中转为逗号拼接的 StringLiteral，此处按 Vector3/Color 转型</item>
    ///   <item>Color hex #RRGGBBAA → 自动解码为 r,g,b,a 格式</item>
    ///   <item>Color 3段值 r,g,b → 自动补 alpha=255</item>
    ///   <item>未匹配期望类型的字面量 → 拒绝（标准错误，不是"无法纠正"）</item>
    ///   <item>Enum 参数位变量 → 拒绝（Enum 必须常量）</item>
    ///   <item>Enum 变量 → 拒绝（Enum 必须常量）</item>
    /// </list>
    /// <para>跨类型兼容（Enum 常量塞非 Enum 位 / 非 Enum 常量塞 Enum 位）由后续 ValidateArgParamTypeCompat 负责。</para>
    /// </remarks>
    private static ArgBuildResult BuildBescriptArgs(
        TsViewCallExpr call,
        ScriptConversionContext context,
        MethodSignature methodSig)
    {
        List<TsViewArg> args = new(call.Args.Count);
        for (int i = 0; i < call.Args.Count; i++)
        {
            TsViewArg arg = call.Args[i];
            MethodParameter expectedParam = methodSig.Parameters[i];
            string expectedTypeName = expectedParam.TypeName;

            // Enum 参数位：禁止变量，必须为枚举常量
            if (string.Equals(expectedTypeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal))
            {
                if (arg.Kind is TsViewArgKind.Identifier or TsViewArgKind.ThisMember)
                    return new ArgBuildResult([], $"参数 {i + 1}: Enum 参数位禁止使用变量 '{arg.Raw}'。正确写法: BeEnum<{expectedParam.DisplayTypeName}>.fromBeConst({expectedParam.DisplayTypeName}.具体成员)。", TsViewDiagnosticTag.None);
            }

            TsViewArg fixedArg = arg.Kind switch
            {
                TsViewArgKind.NumberLiteral when string.Equals(expectedTypeName, nameof(BescriptVarType.Float), StringComparison.Ordinal)
                    => new TsViewArg(TsViewArgKind.BeConst, arg.Raw, arg.Raw, nameof(BescriptVarType.Float)),
                TsViewArgKind.NumberLiteral when string.Equals(expectedTypeName, nameof(BescriptVarType.Long), StringComparison.Ordinal)
                    => new TsViewArg(TsViewArgKind.BeConst, arg.Raw, arg.Raw, nameof(BescriptVarType.Long)),
                TsViewArgKind.NumberLiteral when string.Equals(expectedTypeName, nameof(BescriptVarType.Bool), StringComparison.Ordinal)
                    => new TsViewArg(TsViewArgKind.BeConst, arg.Raw, arg.Raw, nameof(BescriptVarType.Bool)),
                TsViewArgKind.NumberLiteral when string.Equals(expectedTypeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal)
                    => new TsViewArg(TsViewArgKind.BeConst, arg.Raw, arg.Raw, nameof(BescriptVarType.Enum), expectedParam.DisplayTypeName),
                TsViewArgKind.StringLiteral when string.Equals(expectedTypeName, nameof(BescriptVarType.Float), StringComparison.Ordinal)
                    => new TsViewArg(TsViewArgKind.BeConst, arg.Raw, arg.Raw, nameof(BescriptVarType.Float)),
                TsViewArgKind.StringLiteral when string.Equals(expectedTypeName, nameof(BescriptVarType.Long), StringComparison.Ordinal)
                    => new TsViewArg(TsViewArgKind.BeConst, arg.Raw, arg.Raw, nameof(BescriptVarType.Long)),
                TsViewArgKind.StringLiteral when string.Equals(expectedTypeName, nameof(BescriptVarType.Bool), StringComparison.Ordinal)
                    => new TsViewArg(TsViewArgKind.BeConst, arg.Raw, arg.Raw, nameof(BescriptVarType.Bool)),
                TsViewArgKind.StringLiteral when string.Equals(expectedTypeName, nameof(BescriptVarType.Vector3), StringComparison.Ordinal)
                    => new TsViewArg(TsViewArgKind.BeConst, arg.Raw, arg.Raw, nameof(BescriptVarType.Vector3)),
                TsViewArgKind.StringLiteral when string.Equals(expectedTypeName, nameof(BescriptVarType.Color), StringComparison.Ordinal)
                    => new TsViewArg(TsViewArgKind.BeConst, arg.Raw, arg.Raw, nameof(BescriptVarType.Color)),
                TsViewArgKind.StringLiteral when string.Equals(expectedTypeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal)
                    => new TsViewArg(TsViewArgKind.BeConst, arg.Raw, arg.Raw, nameof(BescriptVarType.Enum), expectedParam.DisplayTypeName),
                TsViewArgKind.BooleanLiteral when string.Equals(expectedTypeName, nameof(BescriptVarType.Bool), StringComparison.Ordinal)
                    => new TsViewArg(TsViewArgKind.BeConst, arg.Raw, arg.Raw, nameof(BescriptVarType.Bool)),
                TsViewArgKind.Identifier when string.Equals(expectedTypeName, nameof(BescriptVarType.Bool), StringComparison.Ordinal) &&
                                              (string.Equals(arg.Raw, "True", StringComparison.Ordinal) ||
                                               string.Equals(arg.Raw, "False", StringComparison.Ordinal))
                    => new TsViewArg(TsViewArgKind.BeConst, arg.Raw, arg.Raw, nameof(BescriptVarType.Bool)),
                _ => arg
            };
            // 字面量未匹配任何期望类型的转换规则 → 拒绝
            if (fixedArg.Kind == TsViewArgKind.NumberLiteral)
                return new ArgBuildResult([], $"参数 {i + 1}: 这是错误写法，禁止使用数字字面量 '{arg.Raw}'。正确写法应使用对应类型的 BeType.fromBeConst(\"...\") 常量。", TsViewDiagnosticTag.None);
            if (fixedArg.Kind == TsViewArgKind.BooleanLiteral)
                return new ArgBuildResult([], $"参数 {i + 1}: 这是错误写法，禁止使用布尔字面量 '{arg.Raw}'。正确写法应使用 BeBool.fromBeConst(\"...\") 常量。", TsViewDiagnosticTag.None);
            // Color 规范化：hex → r,g,b,a；3段补 alpha=255
            if (fixedArg.Kind == TsViewArgKind.BeConst &&
                string.Equals(fixedArg.BeTypeName, nameof(BescriptVarType.Color), StringComparison.Ordinal))
            {
                string colorText = fixedArg.Raw;
                if (colorText.StartsWith('#'))
                    colorText = colorText[1..];
                bool hexParsed = false;
                if (colorText.Length is 6 or 8)
                {
                    bool isHex = true;
                    foreach (char ch in colorText)
                    {
                        if (!((ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'F') || (ch >= 'a' && ch <= 'f')))
                        { isHex = false; break; }
                    }
                    if (isHex)
                    {
                        int r = Convert.ToInt32(colorText[..2], 16);
                        int g = Convert.ToInt32(colorText[2..4], 16);
                        int b = Convert.ToInt32(colorText[4..6], 16);
                        int a = colorText.Length == 8 ? Convert.ToInt32(colorText[6..8], 16) : 255;
                        fixedArg = fixedArg with { Raw = string.Join(",", r, g, b, a), StringValue = string.Join(",", r, g, b, a) };
                        hexParsed = true;
                    }
                }
                if (!hexParsed && fixedArg.Raw.Split(',').Length == 3)
                    fixedArg = fixedArg with { Raw = fixedArg.Raw + ",255", StringValue = fixedArg.Raw + ",255" };
            }

            if (fixedArg.Kind == TsViewArgKind.BeConst &&
                fixedArg.BeTypeName is string beTypeName &&
                fixedArg.StringValue is string constValue)
            {
                string consumerMethodName = methodSig.GetFullPath();
                string? scopedConstError = ConstFillMode.ValidateConsumerScopedFreeValueArg(beTypeName, constValue, consumerMethodName, i + 1);
                if (scopedConstError is not null)
                {
                    return new ArgBuildResult([], scopedConstError, TsViewDiagnosticTag.None);
                }
            }
            args.Add(fixedArg);
        }

        return BuildBescriptArgs(args, context);
    }

    private static ArgBuildResult BuildBescriptArgs(
        IReadOnlyList<TsViewArg> args,
        ScriptConversionContext context)
    {
        List<BescriptCallArgument> result = [];

        for (int i = 0; i < args.Count; i++)
        {
            TsViewArg arg = args[i];
            switch (arg.Kind)
            {
                case TsViewArgKind.Identifier:
                    // 全局变量必须用 this. 显式引用
                    if (!context.DeclaredVariables.ContainsKey(arg.Raw) &&
                        context.GlobalVariables.ContainsKey(arg.Raw))
                    {
                        return new ArgBuildResult(result, $"全局变量必须显式使用 this.: {arg.Raw}", TsViewDiagnosticTag.None);
                    }
                    // 悬空变量（未声明）原版允许，直接输出
                    string decodedIdent = TsViewIdentifierMap.FromTsIdentifier(arg.Raw);
                    result.Add(new BescriptCallArgument(
                        true, "var",
                        decodedIdent,
                        decodedIdent, null));
                    break;

                case TsViewArgKind.ThisMember:
                    if (!context.GlobalVariables.ContainsKey(arg.Raw))
                    {
                        return new ArgBuildResult(
                            result,
                            $"this. 仅允许用于全局变量: {arg.Raw},如果确实要使用全局变量那么应该检查拼写是否准确",
                            TsViewDiagnosticTag.ForSubject(TsViewDiagnosticCodes.InvalidThisGlobalReference, arg.Raw));
                    }
                    string decodedMember = TsViewIdentifierMap.FromTsIdentifier(arg.Raw);
                    result.Add(new BescriptCallArgument(
                        true, "var",
                        decodedMember,
                        decodedMember, null));
                    break;

                case TsViewArgKind.BeConst:
                    if (arg.BeTypeName is not string rawBeTypeName)
                        return new ArgBuildResult(result, $"参数 {i + 1}: BeConst 缺少类型信息", TsViewDiagnosticTag.None);

                    string beTypeName = TsViewTypeUtils.ConvertToTsTypeName(rawBeTypeName);
                    string? beBody = arg.StringValue;
                    if (beBody is null)
                        return new ArgBuildResult(result, $"参数 {i + 1}: BeConst 缺少常量值", TsViewDiagnosticTag.None);

                    // Canvas 类型：禁止 HTML 格式直接写入 BeScript
                    if (string.Equals(beTypeName, "Canvas", StringComparison.Ordinal) &&
                        beBody.StartsWith("<", StringComparison.Ordinal))
                    {
                        return new ArgBuildResult(result, $"参数 {i + 1}: Canvas 参数禁止直接使用裸 HTML，必须通过 BeCanvas.fromBase64Const / fromNodeConst / fromGuidConstLegacy / fromGuidConst 生成。", TsViewDiagnosticTag.None);
                    }

                    if (string.Equals(beTypeName, "Canvas", StringComparison.Ordinal) &&
                        beBody.Length > 0 &&
                        !TsViewTypeUtils.IsCanvasGuid(beBody) &&
                        CanvasInlineArg.Parse(beBody) is null)
                    {
                        return new ArgBuildResult(result, $"参数 {i + 1}: Canvas 格式无效，必须为旧版 canvas:名称=base64、新版 ==hjson:: 块，或旧版 guid:xxx / 新版 ==guid:xxx==。", TsViewDiagnosticTag.None);
                    }

                    // GUID 格式校验：guid:xxx（旧版）或 ==guid:xxx==（新版）
                    if (string.Equals(beTypeName, "Canvas", StringComparison.Ordinal))
                    {
                        string? guidText = BescriptSyntax.TryExtractGuid(beBody);

                        if (guidText is not null)
                        {
                            ProjectModel? model = context.Model;
                            if (model is null)
                                return new ArgBuildResult(result, $"参数 {i + 1}: Canvas GUID 校验失败（Model 未就绪）", TsViewDiagnosticTag.None);

                            if (model.FindByGuid(guidText) is not ProjectEntity)
                                return new ArgBuildResult(result, $"参数 {i + 1}: Canvas GUID 未找到: {guidText}", TsViewDiagnosticTag.None);
                        }
                    }
                    if (string.Equals(beTypeName, "Bool", StringComparison.Ordinal))
                    {
                        beBody = ConvertBoolToBescript(beBody);
                        if (!beBody.Equals("0", StringComparison.Ordinal) && !beBody.Equals("1", StringComparison.Ordinal))
                            return new ArgBuildResult(result, $"参数 {i + 1}: Bool 只允许 0/1", TsViewDiagnosticTag.None);
                    }
                    BescriptVarType beVarType = BescriptVarType.Any;
                    if (BescriptVarTypeHelper.TryParse(beTypeName) is BescriptVarType parsedBeVarType)
                        beVarType = parsedBeVarType;
                    result.Add(new BescriptCallArgument(
                        false, beTypeName, beBody, beBody, beVarType));
                    break;

                case TsViewArgKind.Undefined:
                    // undefined 占位符：正向映射生成的缺失参数，剔除不输出到 BeScript
                    break;

                case TsViewArgKind.StringLiteral:
                    {
                        string? strValue = arg.StringValue;
                        if (strValue is null)
                            return new ArgBuildResult(result, $"参数 {i + 1}: 字符串参数缺少字面量", TsViewDiagnosticTag.None);

                        result.Add(new BescriptCallArgument(
                            false, "String", strValue, strValue, BescriptVarType.String));
                        break;
                    }

                case TsViewArgKind.NumberLiteral:
                    return new ArgBuildResult(result, $"参数 {i + 1}: 这是错误写法，禁止使用数字字面量 '{arg.Raw}'。正确写法应使用对应类型的 BeType.fromBeConst(\"...\") 常量。", TsViewDiagnosticTag.None);

                case TsViewArgKind.BooleanLiteral:
                    return new ArgBuildResult(result, $"参数 {i + 1}: 这是错误写法，禁止使用布尔字面量 '{arg.Raw}'。正确写法应使用 BeBool.fromBeConst(\"...\") 常量。", TsViewDiagnosticTag.None);

                case TsViewArgKind.Other:
                    return new ArgBuildResult(result, $"参数 {i + 1}: 不支持的参数类型 {arg.Kind}（常量请使用 BeType.fromBeConst() 或 BeType.fromBeConst(\"值\")，枚举常量必须写成 BeEnum.fromBeConst(具体枚举成员)）", TsViewDiagnosticTag.None);

                default:
                    return new ArgBuildResult(result, $"参数 {i + 1}: 不支持的参数类型 {arg.Kind}（常量请使用 BeType.fromBeConst() 或 BeType.fromBeConst(\"值\")，枚举常量必须写成 BeEnum.fromBeConst(具体枚举成员)）", TsViewDiagnosticTag.None);
            }
        }

        return new ArgBuildResult(result, null, TsViewDiagnosticTag.None);
    }

#pragma warning disable TK006 // Bool 值转换是独立语义，不宜内联到调用处
    private static string ConvertBoolToBescript(string raw)
#pragma warning restore TK006
    {
        if (raw.Equals("true", StringComparison.Ordinal)) return "1";
        if (raw.Equals("True", StringComparison.Ordinal)) return "1";
        if (raw.Equals("false", StringComparison.Ordinal)) return "0";
        if (raw.Equals("False", StringComparison.Ordinal)) return "0";
        return raw;
    }

    private static bool IsActSelfTarget(TsViewArg arg)
    {
        return arg.Kind == TsViewArgKind.Identifier &&
               arg.Raw.Equals("this", StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 缺失零件占位符（Unresolved Act Target）反向映射支持
    // ═══════════════════════════════════════════════════════════════════

    internal static bool IsMissingActTargetPlaceholder(TsViewCallExpr call) =>
        call.Kind == TsViewCallKind.Act
        && call.ActTargetType is string type
        && type.StartsWith("DeviceType_", StringComparison.Ordinal)
        && call.ActTarget is TsViewArg target
        && (target.Kind == TsViewArgKind.NumberLiteral || target.Kind == TsViewArgKind.StringLiteral);

    private static ParseResult<BescriptCallSignature> MapUnresolvedActCall(
        TsViewCallExpr call,
        IReadOnlyList<BescriptCallArgument> args,
        string actTargetType,
        TsViewArg actTarget,
        string methodName)
    {
        // 从 DeviceType_XXX 提取首段数字
        if (!actTargetType.StartsWith("DeviceType_", StringComparison.Ordinal) ||
            !int.TryParse(actTargetType["DeviceType_".Length..], out int ownerTypeId))
        {
            return new ParseResult<BescriptCallSignature>.FailResult($"无法解析降级类型名: {actTargetType}");
        }

        if (!DeviceTypeIds.IsKnownTypeId(ownerTypeId))
        {
            return new ParseResult<BescriptCallSignature>.FailResult($"未知类型.不要编造不存在的零件类型.若确实存在那么可能是Act 目标零件类型未同步到 SDK: {ownerTypeId}");
        }

        // 根据 ActTarget 确定 ownerName 和 ownerNameIsAlias
        // self 永远指向自身，不可能缺失，此处只处理 byId/byName
        string ownerName;
        bool ownerNameIsAlias;

        if (actTarget.Kind == TsViewArgKind.NumberLiteral &&
            int.TryParse(actTarget.Raw, out int deviceId))
        {
            ownerName = deviceId.ToString();
            ownerNameIsAlias = false;
        }
        else if (actTarget.Kind == TsViewArgKind.StringLiteral)
        {
            ownerName = actTarget.Raw;
            ownerNameIsAlias = true;
        }
        else
        {
            return new ParseResult<BescriptCallSignature>.FailResult($"不支持的 Act 目标参数类型: {actTarget.Kind}");
        }

        string besMethodName = TsViewIdentifierMap.FromTsIdentifier(methodName);

        string ownerTypeIdText = ownerTypeId.ToString();
        string saveName = $"{ownerTypeIdText}.{ownerName}.{besMethodName}";
        return new ParseResult<BescriptCallSignature>.OkResult(new ActCallSignature(
            ownerTypeIdText,
            ownerName,
            ownerNameIsAlias,
            besMethodName,
            saveName,
            args,
            SegmentSpans: [],
            null));
    }

    // ═══════════════════════════════════════════════════════════════════
    // 未压缩原始 BeScript 方法名 → 压缩名 自动纠偏
    // ──────────────────────────────────────────────────────────────────
    // Agent 在 TS 视图里可能误用未压缩的原始 bes 方法名（带参数/类型后缀的
    // add1 等），而 TS 视图合法的是压缩名单一条目（add）。此处在校验失败边界
    // 做一道精准重试：把输入名当作原始 bes 名，经压缩表映射回压缩名再查反向表。
    // 精准性由 BuiltinMethodMap 的严格双射保证（反向键含 argCount），且只有
    // 输入名精确命中真实 bes 原始名才触发，零猜测、零模糊匹配。
    // ═══════════════════════════════════════════════════════════════════

    internal readonly record struct CorrectionResolve(
        BuiltinMethodMap.ReverseResult Result,
        bool Corrected,
        string OriginalInputName,
        string CorrectedTsName);

    private static CorrectionResolve ResolveWithCorrectionCore(
        string inputName,
        string? agentGeneric,
        int argCount,
        Func<string, string?, int, BuiltinMethodMap.ReverseResult> getReverse,
        Func<string, (bool Success, string CompressedName, string? GenericBeTypeName)> tryGetCompressed)
    {
        // 1) 先按原样查一次（覆盖正常情况）
        BuiltinMethodMap.ReverseResult first = getReverse(inputName, agentGeneric, argCount);
        if (first.Found)
            return new CorrectionResolve(first, false, inputName, inputName);

        // 2) 精准纠偏重试：raw → FromTsIdentifier → TryGetCompressed → ToTsIdentifier → GetReverse
        string candidateRaw = TsViewIdentifierMap.FromTsIdentifier(inputName);
        (bool success, string compressedName, string? genericBeTypeName) = tryGetCompressed(candidateRaw);
        if (!success)
            return new CorrectionResolve(first, false, inputName, inputName);
        string retryTsName = TsViewIdentifierMap.ToTsIdentifier(compressedName);
        // 重试泛型一律用压缩推断值：构建期反向键的泛型就取自这里（BuiltinMethodMap.Build），
        // 逐字段一致才能命中；Get*Reverse 内部会再做一次 ConvertToTsTypeName（幂等）。
        BuiltinMethodMap.ReverseResult retry = getReverse(retryTsName, genericBeTypeName, argCount);
        if (!retry.Found)
            return new CorrectionResolve(first, false, inputName, inputName);
        return new CorrectionResolve(retry, true, inputName, retryTsName);
    }

    internal static CorrectionResolve ResolveGlobalWithCorrection(string methodPath, string? agentGeneric, int argCount)
        => ResolveWithCorrectionCore(methodPath, agentGeneric, argCount,
            (n, g, c) => BuiltinMethodMap.Instance.GetGlobalReverse(n, g, c),
            BescriptDtsGenerator.TryGetGlobalCompressedName);

    internal static CorrectionResolve ResolveVarFuncWithCorrection(string ownerType, string methodName, string? agentGeneric, int argCount)
        => ResolveWithCorrectionCore(methodName, agentGeneric, argCount,
            (n, g, c) => BuiltinMethodMap.Instance.GetVarFuncReverse(ownerType, n, g, c),
            raw => TryGetVarFuncCompressedWithBase(ownerType, raw));

    internal static CorrectionResolve ResolveDeviceActionWithCorrection(string ownerType, string methodName, string? agentGeneric, int argCount)
        => ResolveWithCorrectionCore(methodName, agentGeneric, argCount,
            (n, g, c) => BuiltinMethodMap.Instance.GetDeviceActionReverse(ownerType, n, g, c),
            raw => BescriptDtsGenerator.TryGetDeviceActionCompressedName(ownerType, raw));

    private static (bool Success, string CompressedName, string? GenericBeTypeName) TryGetVarFuncCompressedWithBase(
        string ownerType, string raw)
    {
        (bool success, string compressedName, string? genericBeTypeName) =
            BescriptDtsGenerator.TryGetVarFuncCompressedName(ownerType, raw);
        if (success)
            return (true, compressedName, genericBeTypeName);

        // varf 压缩表不遍历继承链，镜像 GetVarFuncReverse 的 UI 基类回退
        string? baseType = VarFuncInheritance.GetBaseOwnerType(ownerType);
        return baseType is null
            ? (false, raw, null)
            : BescriptDtsGenerator.TryGetVarFuncCompressedName(baseType, raw);
    }

    private static ToolError BuildCorrectionWarning(ScriptConversionContext context, CorrectionResolve resolved)
    {
        string msg = $"`{resolved.OriginalInputName}` 是错误写法（误用了未压缩的原始 BeScript 方法名）。" +
                     $"已自动改写为压缩后的 TS 视图方法名 `{resolved.CorrectedTsName}` 并按精准双射接受。" +
                     $"正确写法：TS 视图中应使用压缩名，原始名仅存在于底层 BeScript；完整合法方法名请到 sdk.ts 查阅。";
        return new ToolError(context.ExpansionTargetPath, null, null, msg, ToolDiagnosticSeverity.Warning);
    }

}
