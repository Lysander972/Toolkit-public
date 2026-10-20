using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    private static readonly HashSet<string> ConditionConstMustVarFunMethods = new(StringComparer.Ordinal)
    {
        "create.bool",
        "bool.not",
        "bool.and",
        "bool.equal",
        "bool.xor"
    };

    /// <summary>
    /// 验证零件中所有脚本的内置方法调用签名。
    /// </summary>
    public static IReadOnlyList<LintDiagnostic> ValidateBuiltinMethodCallSignatures(
        DeviceEntityBase device,
        ScriptAnalysisService analysisService)
    {
        List<LintDiagnostic> diagnostics = [];
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;

        // 构建全局变量类型映射
        IReadOnlyDictionary<string, BescriptVarType> globalTypeMap = BuildVarTypeMap(device.Vars.Variables);

        foreach (InlineScriptEntityBase script in device.AllScripts)
        {
            string scriptPath = script.实体_完整相对路径标识;
            BescriptParseData parseData = analysisService.ParseScript(scriptPath)
                ?? throw new InvalidOperationException($"脚本解析失败: {scriptPath}");

            // 合并脚本局部变量到类型映射
            IReadOnlyDictionary<string, BescriptVarType> localTypeMap = parseData.Ast.DeclaredVariables;
            Dictionary<string, BescriptVarType> mergedTypeMap = new(globalTypeMap, StringComparer.Ordinal);
            foreach (KeyValuePair<string, BescriptVarType> kvp in localTypeMap)
                mergedTypeMap[kvp.Key] = kvp.Value;

            diagnostics.AddRange(ValidateControlConditionConstMustBeVar(parseData.Ast, scriptPath));

            foreach (BescriptAstNode node in parseData.Ast.RootNodes)
            {
                CollectMethodCallDiagnostics(node, diagnostics, registry, device, scriptPath, mergedTypeMap);
            }
        }

        return diagnostics;
    }

    /// <summary>
    /// 检查 if/while 条件中指定 Bool 函数调用是否错误使用了 Bool 常量。
    /// </summary>
    internal static IReadOnlyList<LintDiagnostic> ValidateControlConditionConstMustBeVar(BescriptAst ast, string scriptPath)
    {
        List<LintDiagnostic> diagnostics = [];
        foreach (BescriptAstNode node in ast.RootNodes)
        {
            CollectControlConditionConstMustBeVarDiagnostics(node, scriptPath, diagnostics);
        }

        return diagnostics;
    }

    private static void CollectControlConditionConstMustBeVarDiagnostics(
        BescriptAstNode node,
        string scriptPath,
        List<LintDiagnostic> diagnostics)
    {
        if ((node.Kind == BescriptStatementKind.If || node.Kind == BescriptStatementKind.While) &&
            node is IHasCall { Call: { Kind: BescriptCallKind.Fun } call } &&
            ConditionConstMustVarFunMethods.Contains(call.FunctionName))
        {
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                BescriptCallArgument arg = call.Arguments[i];
                if (!IsBoolConstantArgument(arg))
                    continue;

                string actualText = $"{arg.TypeName} {arg.DecodedText}";
                diagnostics.Add(CreateCallConditionConstMustBeVarDiagnostic(
                    call.FunctionName,
                    i,
                    actualText,
                    scriptPath,
                    node.Line));
            }
        }

        foreach (BescriptAstNode child in node.Children)
        {
            CollectControlConditionConstMustBeVarDiagnostics(child, scriptPath, diagnostics);
        }
    }

    private static bool IsBoolConstantArgument(BescriptCallArgument arg)
    {
        if (arg.IsVariable)
            return false;

        if (arg.VarType.HasValue)
            return arg.VarType.Value == BescriptVarType.Bool;

        return BescriptVarTypeHelper.TryParse(arg.TypeName) is { } argType &&
               argType == BescriptVarType.Bool;
    }

    /// <summary>
    /// 递归收集方法调用的诊断信息。
    /// </summary>
    private static void CollectMethodCallDiagnostics(
        BescriptAstNode node,
        List<LintDiagnostic> diagnostics,
        BuiltinMethodRegistry registry,
        DeviceEntityBase device,
        string scriptPath,
        IReadOnlyDictionary<string, BescriptVarType> varTypes)
    {
        // 处理方法调用节点
        if (node.Kind == BescriptStatementKind.Call && node is IHasCall { Call: var call })
        {
            ValidateMethodCall(node, call, diagnostics, registry, device, scriptPath, varTypes);
        }

        // 递归处理子节点
        foreach (BescriptAstNode child in node.Children)
        {
            CollectMethodCallDiagnostics(child, diagnostics, registry, device, scriptPath, varTypes);
        }
    }

    /// <summary>
    /// 验证单个方法调用。
    /// </summary>
    private static void ValidateMethodCall(
        BescriptAstNode node,
        BescriptCallSignature call,
        List<LintDiagnostic> diagnostics,
        BuiltinMethodRegistry registry,
        DeviceEntityBase device,
        string scriptPath,
        IReadOnlyDictionary<string, BescriptVarType> varTypes)
    {
        // 跳过无效调用
        if (call.Kind == BescriptCallKind.None || call.Kind == BescriptCallKind.Toolf)
            return;

        // 构建调用路径
        string callPath = BuildCallPath(call);

        // Act 类型只检查内置方法名
        if (call.Kind == BescriptCallKind.Act && !BuiltinDeviceMethodNames.Contains(call.FunctionName))
            return;

        // 常量参数值形状验证
        ValidateConstArgumentValueShape(
            diagnostics, callPath, device.DeviceId, device.DeviceType, device.CarrierGuid, device.CarrierKind, scriptPath, node.Line, call.Arguments);
        if (callPath.StartsWith("findExcel2_", StringComparison.Ordinal) &&
            call.Arguments.Count > 0 &&
            !call.Arguments[0].IsVariable &&
            string.Equals(call.Arguments[0].DecodedText, "id", StringComparison.Ordinal))
        {
            diagnostics.Add(CreateFindExcel2KeyMustNotBeIdDiagnostic(
                callPath, $"{call.Arguments[0].TypeName} {call.Arguments[0].DecodedText}", scriptPath, node.Line));
        }

        // 解析方法签名
        if (TryResolveBuiltinMethodSignature(registry, call) is not { } resolved)
        {
            diagnostics.Add(CreateLintEngineErrorDiagnostic(
                $"内置方法签名未注册，无法校验调用参数: {callPath}",
                new LintLocation(scriptPath, node.Line)));
            return;
        }

        MethodSignature methodSig = resolved.Sig;
        string methodPath = resolved.Path;

        if (methodSig.ShouldMarkNotRecommended)
        {
            diagnostics.Add(CreateCallNotRecommendedDiagnostic(
                methodPath,
                methodSig,
                scriptPath,
                node.Line));
        }

        // 检查参数数量
        int actualCount = call.Arguments.Count;
        int totalCount = methodSig.Parameters.Count;

        if (actualCount > totalCount)
        {
            diagnostics.Add(CreateCallParamCountMismatchDiagnostic(
                methodPath, actualCount, totalCount, LintSeverity.Error,
                device.DeviceId, device.DeviceType, device.CarrierGuid, device.CarrierKind, scriptPath, node.Line));
            return;
        }

        // 检查每个参数的类型
        bool hasTypeMismatch = false;
        int max = System.Math.Min(actualCount, methodSig.Parameters.Count);

        for (int i = 0; i < max; i++)
        {
            BescriptCallArgument arg = call.Arguments[i];
            if (TryResolveArgumentType(arg, varTypes) is not { Type: var actualType, TypeName: var actualTypeName })
                continue;

            MethodParameter expectedParam = methodSig.Parameters[i];
            if (BescriptVarTypeHelper.TryParse(expectedParam.TypeName) is not BescriptVarType expectedType)
            {
                diagnostics.Add(CreateLintEngineErrorDiagnostic(
                    $"内置方法签名参数类型无法解析: {methodPath} 参数类型={expectedParam.TypeName}",
                    new LintLocation(scriptPath, node.Line)));
                continue;
            }

            // Any 类型参数检查
            if (expectedType == BescriptVarType.Any && !arg.IsVariable)
            {
                if (BescriptVarTypeHelper.TryParse(arg.TypeName) is { } constType && constType == BescriptVarType.Any)
                    continue;

                string actualText = $"{arg.TypeName} {arg.DecodedText}";
                diagnostics.Add(CreateCallParamAnyMustBeVarDiagnostic(
                    methodPath, i, expectedParam.EffectiveName, actualText,
                    device.DeviceId, device.DeviceType, device.CarrierGuid, device.CarrierKind, scriptPath, node.Line));
                continue;
            }

            // 隐式转 String 检查
            if (expectedType == BescriptVarType.String &&
                actualType != BescriptVarType.String &&
                !expectedParam.AllowImplicitToString)
            {
                diagnostics.Add(CreateCallParamImplicitToStringNotAllowedDiagnostic(
                    methodPath, i, expectedParam.EffectiveName, actualTypeName,
                    device.DeviceId, device.DeviceType, device.CarrierGuid, device.CarrierKind, scriptPath, node.Line));
                hasTypeMismatch = true;
                continue;
            }

            // 类型兼容性检查
            if (!IsMethodParamTypeCompatible(actualType, expectedType, expectedParam))
            {
                diagnostics.Add(CreateCallParamTypeMismatchDiagnostic(
                    methodPath, i, expectedParam.EffectiveName, expectedParam.TypeName, actualTypeName,
                    device.DeviceId, device.DeviceType, device.CarrierGuid, device.CarrierKind, scriptPath, node.Line));
                hasTypeMismatch = true;
            }
        }

        // Struct.get* 第二个参数未复制的风险检测
        if (call is VarfCallSignature varf &&
            string.Equals(varf.OwnerType, nameof(BescriptVarType.Struct), StringComparison.Ordinal) &&
            call.FunctionName.StartsWith("get", StringComparison.Ordinal) &&
            call.Arguments.Count >= 2)
        {
            BescriptCallArgument copyArg = call.Arguments[1];
            if (!copyArg.IsVariable && "0".Equals(copyArg.DecodedText, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateStructGetterNoCopyDiagnostic(
                    methodPath, device.DeviceId, device.DeviceType, device.CarrierGuid, device.CarrierKind, scriptPath, node.Line));
            }
        }

        // 参数数量不足警告
        if (actualCount < totalCount)
        {
            LintSeverity severity = LintSeverity.Warning;
            if (hasTypeMismatch)
                severity = LintSeverity.Error;
            diagnostics.Add(CreateCallParamCountMismatchDiagnostic(
                methodPath, actualCount, totalCount,
                severity,
                device.DeviceId, device.DeviceType, device.CarrierGuid, device.CarrierKind, scriptPath, node.Line));
        }
    }

    /// <summary>
    /// 验证常量参数值形状。
    /// </summary>
    private static void ValidateConstArgumentValueShape(
        List<LintDiagnostic> diagnostics,
        string methodPath,
        int deviceId,
        int deviceType,
        string carrierGuid,
        string carrierKind,
        string scriptPath,
        int line,
        IReadOnlyList<BescriptCallArgument> args)
    {
        if (args.Count == 0)
            return;

        for (int i = 0; i < args.Count; i++)
        {
            BescriptCallArgument arg = args[i];
            if (arg.IsVariable)
                continue;

#pragma warning disable TK008 // 参数解码值为空时跳过验证，避免无意义 lint 检查
            if (arg.DecodedText.Length == 0)
#pragma warning restore TK008
                continue;

            string value = arg.DecodedText;

            if (BescriptVarTypeHelper.TryParse(arg.TypeName) is not BescriptVarType type)
                continue;

            // 允许的类型
            if (type is BescriptVarType.Float
                or BescriptVarType.Long
                or BescriptVarType.Color
                or BescriptVarType.Vector3
                or BescriptVarType.String
                or BescriptVarType.Bool
                or BescriptVarType.Enum)
                continue;

            // create.canvas 允许 Canvas 类型
            if (string.Equals(methodPath, "create.canvas", StringComparison.Ordinal) &&
                type == BescriptVarType.Canvas)
                continue;

            string typeName = type.ToString();
            diagnostics.Add(CreateCallConstValueMustBeBlankDiagnostic(
                methodPath, i, typeName, $"{arg.TypeName} {value}",
                deviceId, deviceType, carrierGuid, carrierKind, scriptPath, line));
        }
    }

    private static (MethodSignature Sig, string Path)? TryResolveBuiltinMethodSignature(
        BuiltinMethodRegistry registry,
        BescriptCallSignature call)
    {
        if (call.Kind == BescriptCallKind.Fun)
        {
            if (registry.GetByPath(call.FunctionName, MethodCategory.GlobalFunc) is { } sig)
                return (sig, call.FunctionName);
            return null;
        }
        if (call.Kind == BescriptCallKind.Varf)
            return TryResolveVarFuncSignature(registry, call);
        if (call.Kind == BescriptCallKind.Act)
            return TryResolveDeviceActionSignature(registry, call);
        if (call.Kind == BescriptCallKind.Dvf)
            return TryResolveEditorFuncSignature(registry, call);
        return null;
    }

    private static (MethodSignature Sig, string Path)? TryResolveVarFuncSignature(
        BuiltinMethodRegistry registry,
        BescriptCallSignature call)
        => TryResolveTypedFuncSignature(registry, call, MethodCategory.VarFunc, useInheritance: true);

    private static (MethodSignature Sig, string Path)? TryResolveEditorFuncSignature(
        BuiltinMethodRegistry registry,
        BescriptCallSignature call)
        => TryResolveTypedFuncSignature(registry, call, MethodCategory.EditorFunc, useInheritance: false);

    private static (MethodSignature Sig, string Path)? TryResolveTypedFuncSignature(
        BuiltinMethodRegistry registry,
        BescriptCallSignature call,
        MethodCategory category,
        bool useInheritance)
    {
        string ownerType = call switch
        {
            VarfCallSignature v => v.OwnerType,
            DvfCallSignature d => d.OwnerType,
            _ => throw new InvalidOperationException($"TryResolveTypedFuncSignature 不应处理 {call.Kind}")
        };

        string path = ownerType + "." + call.FunctionName;
        MethodSignature? sig = registry.GetByPath(path, category);
        if (useInheritance)
            sig = registry.GetByPathWithInheritance(path, category);

        if (sig == null)
            return null;

        return (sig, path);
    }

    private static (MethodSignature Sig, string Path)? TryResolveDeviceActionSignature(
        BuiltinMethodRegistry registry,
        BescriptCallSignature call)
    {
        string methodName = call.FunctionName;
        string actOwnerType = ((ActCallSignature)call).OwnerType;
        string? ownerType = null;
        if (int.TryParse(actOwnerType, out int actOwnerTypeId))
            ownerType = DeviceTypeIds.GetActionOwnerTypeName(actOwnerTypeId);

        // 第 1 层：精确类型匹配（有继承支持）
        if (ownerType != null)
        {
            string typedPath = ownerType + "." + methodName;
            MethodSignature? typedSig = registry.GetByPathWithInheritance(typedPath, MethodCategory.DeviceAction);
            if (typedSig != null)
                return (typedSig, typedPath);
        }

        // 第 2 层：Device 基类通配符
        string devicePath = "Device." + methodName;
        MethodSignature? deviceSig = registry.GetByPath(devicePath, MethodCategory.DeviceAction);
        if (deviceSig != null)
            return (deviceSig, devicePath);

        return null;
    }

    private static string BuildCallPath(BescriptCallSignature call)
    {
        string methodName = call.FunctionName;
        return call.Kind switch
        {
            BescriptCallKind.Fun => methodName,
            BescriptCallKind.Varf => $"{((VarfCallSignature)call).OwnerType}.{methodName}",
            BescriptCallKind.Dvf => $"{((DvfCallSignature)call).OwnerType}.{methodName}",
            BescriptCallKind.Act => $"{(int.TryParse(((ActCallSignature)call).OwnerType, out int id) && DeviceTypeIds.GetActionOwnerTypeName(id) is string typeName ? typeName : $"DeviceType({((ActCallSignature)call).OwnerType})")}.{methodName}",
            _ => methodName
        };
    }

    /// <summary>
    /// 从全局变量列表构建类型映射。
    /// </summary>
    private static IReadOnlyDictionary<string, BescriptVarType> BuildVarTypeMap(
        IReadOnlyList<VarInfo>? vars)
    {
        if (vars == null || vars.Count == 0)
            return new Dictionary<string, BescriptVarType>(0, StringComparer.Ordinal);

        Dictionary<string, BescriptVarType> map = new(StringComparer.Ordinal);
        foreach (VarInfo v in vars)
        {
#pragma warning disable TK008 // 外部变量列表可能含空名条目，跳过
            if (v.Name.Length == 0)
#pragma warning restore TK008
                continue;

            string name = v.Name;
            if (TsViewTypeUtils.TryParseBescriptVarType(v.TypeName) is not BescriptVarType type)
            {
                throw new InvalidOperationException($"未知的变量类型: {v.TypeName} (变量: {name})");
            }

            if (!map.TryGetValue(name, out BescriptVarType existing) || existing == BescriptVarType.Void)
                map[name] = type;
        }

        return map;
    }

    /// <summary>
    /// 尝试解析参数类型。
    /// </summary>
    private static (BescriptVarType Type, string TypeName)? TryResolveArgumentType(
        BescriptCallArgument arg,
        IReadOnlyDictionary<string, BescriptVarType> varTypes)
    {
        if (arg.IsVariable)
        {
            string name = arg.DecodedText;

            if (!varTypes.TryGetValue(name, out BescriptVarType varType))
                return null;

            if (varType is BescriptVarType.Void)
                return null;

            return (varType, varType.ToString());
        }

        if (BescriptVarTypeHelper.TryParse(arg.TypeName) is not BescriptVarType type)
            return null;

        if (type is BescriptVarType.Void)
            return null;

        return (type, type.ToString());
    }

    /// <summary>
    /// 检查方法参数类型是否兼容。
    /// </summary>
    private static bool IsMethodParamTypeCompatible(
        BescriptVarType actual,
        BescriptVarType expected,
        MethodParameter expectedParam)
    {
        if (actual == expected)
            return true;

        if (expected == BescriptVarType.Any)
            return true;

        if (expected == BescriptVarType.String)
            return expectedParam.AllowImplicitToString;

        if (expected == BescriptVarType.Number &&
            actual is BescriptVarType.Float or BescriptVarType.Long)
            return true;

        if (IsUiFamilyType(actual) && (expected == BescriptVarType.UI))
            return true;

        return false;
    }

    /// <summary>
    /// 检查是否为 UI 家族类型。
    /// </summary>
    private static bool IsUiFamilyType(BescriptVarType type)
    {
        return type is BescriptVarType.UI
            or BescriptVarType.UICircle
            or BescriptVarType.UIRect
            or BescriptVarType.UIButton
            or BescriptVarType.UILabel
            or BescriptVarType.UIInput
            or BescriptVarType.UILine
            or BescriptVarType.UIAvatar
            or BescriptVarType.UISpine
            or BescriptVarType.UIRawImage;
    }
}
