using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    internal static IReadOnlyList<LintDiagnostic> ValidateActAndDynamicCalls(ProjectModel model, ScriptAnalysisService analysisService)
    {
        List<LintDiagnostic> diagnostics = [];
        DeviceEntityBase[] devices = model.EnumerateAllDevices().ToArray();

        Dictionary<(string CarrierKind, string CarrierGuid), List<DeviceEntityBase>> devicesByCarrier = [];
        foreach (DeviceEntityBase device in devices)
        {
            (string CarrierKind, string CarrierGuid) carrierKey = (device.CarrierKind, device.CarrierGuid);
            if (!devicesByCarrier.TryGetValue(carrierKey, out List<DeviceEntityBase>? carrierDevices))
            {
                carrierDevices = [];
                devicesByCarrier[carrierKey] = carrierDevices;
            }

            carrierDevices.Add(device);
        }

        foreach (List<DeviceEntityBase> carrierDevices in devicesByCarrier.Values)
        {
            Dictionary<string, List<DeviceEntityBase>> byLogicName = new(StringComparer.Ordinal);
            foreach (DeviceEntityBase d in carrierDevices)
            {
                if (d.LogicName is not string logicName) continue;
                if (!byLogicName.TryGetValue(logicName, out List<DeviceEntityBase>? list))
                {
                    list = [];
                    byLogicName[logicName] = list;
                }
                list.Add(d);
            }

            foreach ((string? logicName, List<DeviceEntityBase>? group) in byLogicName)
            {
                if (group.Count <= 1) continue;
                foreach (DeviceEntityBase dup in group)
                {
                    diagnostics.Add(CreateDeviceLogicNameDuplicateDiagnostic(
                        logicName, dup.device配置_完整相对路径));
                }
            }
        }

        (_, Dictionary<int, HashSet<string>> publicMethodNamesByDeviceType) =
            BuildPublicMethodNameSets(model, analysisService, devices);

        // 全工程方法名集合（含私有），用于动态调用存在性检查
        HashSet<string> allMethodNames = new(StringComparer.Ordinal);
        foreach (DeviceEntityBase device in devices)
        {
            foreach (ScriptGroupEntityBase scriptGroup in device.ScriptGroups)
            {
                if (!string.IsNullOrWhiteSpace(scriptGroup.EventName))
                    continue;
                foreach (ScriptEntryInfo entry in scriptGroup.ScriptEntries)
                {
                    allMethodNames.Add(entry.Name);
                }
            }
        }

        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;

        foreach (DeviceEntityBase callerDevice in devices)
        {
            if (!devicesByCarrier.TryGetValue((callerDevice.CarrierKind, callerDevice.CarrierGuid), out List<DeviceEntityBase>? carrierDevices))
            {
                continue;
            }

            // 1110 内部的脚本作用域是目标机械（虚拟核心+机械零件），而非所在地图
            if (callerDevice.DeviceType == DeviceTypeIds.BirthBuilding &&
                GuidText.Validate(callerDevice.MechFileGuid) &&
                devicesByCarrier.TryGetValue(("Mech", callerDevice.MechFileGuid), out List<DeviceEntityBase>? mechDevices))
            {
                carrierDevices = mechDevices;
            }

            foreach (InlineScriptEntityBase callerScript in callerDevice.Scripts)
            {
                string scriptRelativePath = callerScript.实体_完整相对路径标识;
                BescriptParseData? parseData = analysisService.ParseScript(scriptRelativePath);
                BescriptAst? scriptAst = parseData?.Ast;
                if (scriptAst is null)
                {
                    continue;
                }

                Stack<BescriptAstNode> stack = new(scriptAst.RootNodes.Reverse());
                while (stack.Count > 0)
                {
                    BescriptAstNode node = stack.Pop();

                    if (node.Kind == BescriptStatementKind.Call && node is IHasCall { Call: var call })
                    {

                        // ═══════════════════════════════════════════════════════════════
                        // 静态 Act 目标作用域检查
                        // ═══════════════════════════════════════════════════════════════
                        // 检查目标零件是否在当前作用域内，与方法名是否内置无关。
                        // 作用域规则（carrierDevices 已在外层按规则切换）：
                        //   地图非1110零件 → 地图全部零件（1110零件可见但只有内置Act）
                        //   1110内部       → 目标机械的全部零件（不含1110自身，而是虚拟核心+机械零件）
                        //   机械           → 机械全部零件
                        // ═══════════════════════════════════════════════════════════════
                        if (call is ActCallSignature act)
                        {
                            string ownerName = act.OwnerName;
                            bool isSelfKeyword = !act.OwnerNameIsAlias && string.Equals(ownerName, "this", StringComparison.Ordinal);

                            // 别名匹配列表（非 alias 路径为空）。供"alias 不唯一/未找到"判定、
                            // "必须用 this"判定、能力校验三处共用，避免重复扫描 carrierDevices。
                            List<DeviceEntityBase> aliasMatches = act.OwnerNameIsAlias
                                ? carrierDevices
                                    .Where(d => string.Equals(d.LogicName, ownerName, StringComparison.Ordinal))
                                    .ToList()
                                : [];

                            if (act.OwnerNameIsAlias)
                            {
                                if (aliasMatches.Count > 1)
                                {
                                    diagnostics.Add(CreateActAliasNotUniqueDiagnostic(
                                        ownerName,
                                        aliasMatches.Count,
                                        scriptRelativePath,
                                        node.Line));
                                }
                                else if (aliasMatches.Count == 0)
                                {
                                    diagnostics.Add(CreateActAliasNotFoundDiagnostic(
                                        ownerName,
                                        scriptRelativePath,
                                        node.Line));
                                }
                            }

                            // 唯一匹配的目标零件实例（self / 唯一 alias / 唯一数字 ID），其他路径为 null。
                            DeviceEntityBase? resolvedTarget = ResolveActTarget(act, callerDevice, carrierDevices, isSelfKeyword, aliasMatches);

                            if (!isSelfKeyword && resolvedTarget == callerDevice)
                            {
                                diagnostics.Add(CreateActSelfMustUseThisDiagnostic(ownerName, scriptRelativePath, node.Line));
                            }

                            // ═══════════════════════════════════════════════════════════════
                            // 静态 Act 能力校验
                            // ═══════════════════════════════════════════════════════════════
                            // 通过 DeviceCapabilityResolver 校验方法所需能力，提前报告不满足条件的调用。
                            // 校验前提：目标零件实例可静态解析（resolvedTarget != null）。
                            // ═══════════════════════════════════════════════════════════════
                            if (resolvedTarget != null &&
                                TryResolveDeviceActionSignature(registry, call) is { } actSig &&
                                actSig.Sig.RequireCapabilities.Count > 0)
                            {
                                foreach (DeviceCapability capability in actSig.Sig.RequireCapabilities)
                                {
                                    if (!DeviceCapabilityResolver.HasCapability(resolvedTarget, capability))
                                    {
                                        diagnostics.Add(CreateActCapabilityUnavailableDiagnostic(
                                            actSig.Path, capability.ToString(), resolvedTarget.DeviceType,
                                            scriptRelativePath, node.Line));
                                    }
                                }
                            }
                        }

                        // ═══════════════════════════════════════════════════════════════
                        // 动态调用目标存在性检查
                        // ═══════════════════════════════════════════════════════════════
                        // 所有走 CallUserAction 路径的动态调用都支持两种格式：
                        //   纯方法名     → 在当前零件查找
                        //   别名.方法名  → 按别名在同作用域内查找目标零件，再查方法
                        //
                        // 分三大类：
                        // A. 地图级动态调用（必须 别名.方法名 格式）：
                        //    callClientMapRetXXXFuncN / callMapFuncN → 目标 ClientMapLogicDevice
                        // B. 指定目标零件的方法调度（纯方法名，查目标零件自身）：
                        //    loopFun / DisableFun / fixedLoopAct
                        // C. 通用动态调用（纯方法名 或 别名.方法名）：
                        //    C1. Act/Varf 类：callFuncN / netCallFunN / unReliableNetCallFunN 等
                        //    C2. Varf 回调类：addClickCB / addPointDownCB / addPointUpCB
                        //        / addPointEnterCB / addPointExitCB（UIButton）
                        //    C3. Act 回调类：setAnimEvent（第3个参数是回调方法名）
                        // ═══════════════════════════════════════════════════════════════
                        if (call.Kind is not BescriptCallKind.None and not BescriptCallKind.Toolf &&
                            TryResolveBuiltinMethodSignature(registry, call) is { } resolved)
                        {
                            string methodName = resolved.Sig.MethodName;

                            // A. 地图级动态调用：必须 别名.方法名 格式
                            int mapTargetType = -1;
                            if (methodName.StartsWith("callClientMapRet", StringComparison.Ordinal) &&
                                methodName.Contains("Func", StringComparison.Ordinal))
                            {
                                mapTargetType = DeviceTypeIds.ClientMapLogicDevice;
                            }
                            else if (methodName.StartsWith("callMapFunc", StringComparison.Ordinal))
                            {
                                mapTargetType = DeviceTypeIds.ClientMapLogicDevice;
                            }

                            if (mapTargetType >= 0)
                            {
                                ValidateDynamicMapCall(call, mapTargetType, resolved.Path, publicMethodNamesByDeviceType,
                                    scriptRelativePath, node.Line, diagnostics);
                                continue;
                            }

                            string? dynamicMethodText = ExtractDynamicMethodText(call, resolved.Sig, methodName);
                            if (dynamicMethodText == null)
                                continue;

                            ValidateDynamicMethodText(dynamicMethodText, resolved.Path, carrierDevices, allMethodNames,
                                scriptRelativePath, node.Line, diagnostics);
                        }
                    }

                    for (int index = node.Children.Count - 1; index >= 0; index--)
                    {
                        stack.Push(node.Children[index]);
                    }
                }
            }
        }

        return diagnostics;
    }

    /// <summary>
    /// 解析静态 Act 调用的目标零件实例。复用调用方已计算的 isSelfKeyword 与 aliasMatches，
    /// 避免对 carrierDevices 重复扫描。返回 null 表示目标无法静态确定（别名零匹配/多匹配/未知 ID），
    /// 此时由别名/作用域校验段单独报错，能力校验跳过。
    /// </summary>
    private static DeviceEntityBase? ResolveActTarget(
        ActCallSignature act,
        DeviceEntityBase callerDevice,
        IReadOnlyList<DeviceEntityBase> carrierDevices,
        bool isSelfKeyword,
        List<DeviceEntityBase> aliasMatches)
    {
        if (isSelfKeyword)
            return callerDevice;

        if (act.OwnerNameIsAlias)
            return aliasMatches.Count == 1 ? aliasMatches[0] : null;

        if (int.TryParse(act.OwnerName, out int targetId))
        {
            if (targetId == callerDevice.DeviceId)
                return callerDevice;
            List<DeviceEntityBase> idMatches = carrierDevices
                .Where(d => d.DeviceId == targetId)
                .ToList();
            return idMatches.Count == 1 ? idMatches[0] : null;
        }

        return null;
    }

    /// <summary>
    /// 地图级动态调用校验：必须 别名.方法名 格式，检查格式 + 按 deviceType 过滤的全工程方法存在性。
    /// </summary>
    private static void ValidateDynamicMapCall(
        BescriptCallSignature call,
        int mapTargetType,
        string methodPath,
        Dictionary<int, HashSet<string>> publicMethodNamesByDeviceType,
        string scriptRelativePath,
        int line,
        List<LintDiagnostic> diagnostics)
    {
        if (call.Arguments.Count == 0) return;
        BescriptCallArgument firstArg = call.Arguments[0];
        if (firstArg.IsVariable ||
            BescriptVarTypeHelper.TryParse(firstArg.TypeName) is not BescriptVarType firstArgType ||
            firstArgType != BescriptVarType.String)
            return;

        string methodText = firstArg.DecodedText;
        int firstDot = methodText.IndexOf('.', StringComparison.Ordinal);
        if (firstDot <= 0 || firstDot != methodText.LastIndexOf('.') || firstDot == methodText.Length - 1)
        {
            diagnostics.Add(CreateDynamicMapMethodFormatDiagnostic(methodPath, methodText, scriptRelativePath, line));
        }
        else
        {
            string dynamicMethodName = methodText[(firstDot + 1)..];
            if (!publicMethodNamesByDeviceType.TryGetValue(mapTargetType, out HashSet<string>? targetSet) ||
                !targetSet.Contains(dynamicMethodName))
            {
                diagnostics.Add(CreateDynamicMapMethodNotFoundDiagnostic(methodPath, mapTargetType, dynamicMethodName, scriptRelativePath, line));
            }
        }
    }

    /// <summary>
    /// 从动态调用中提取方法名字符串参数。
    /// 覆盖：
    ///   callXXX / netCallFunXXX / unReliableNetCallFunXXX / loopFun / DisableFun / fixedLoopAct — 第1个参数
    ///   addClickCB / addPointDownCB / addPointUpCB / addPointEnterCB / addPointExitCB — 第1个参数
    ///   setAnimEvent — 第3个参数（callback）
    /// </summary>
    private static string? ExtractDynamicMethodText(
        BescriptCallSignature call,
        MethodSignature methodSignature,
        string methodName)
    {
        // setAnimEvent 的回调在第3个参数
        if (string.Equals(methodName, "setAnimEvent", StringComparison.Ordinal))
        {
            if (call.Arguments.Count >= 3)
            {
                BescriptCallArgument cbArg = call.Arguments[2];
                if (!cbArg.IsVariable &&
                    BescriptVarTypeHelper.TryParse(cbArg.TypeName) is { } cbType &&
                    cbType == BescriptVarType.String)
                    return cbArg.DecodedText;
            }
            return null;
        }

        // 第1个参数是 String 类型的方法名
        if (call.Arguments.Count == 0) return null;
        BescriptCallArgument firstArg = call.Arguments[0];
        if (firstArg.IsVariable ||
            BescriptVarTypeHelper.TryParse(firstArg.TypeName) is not BescriptVarType firstArgType ||
            firstArgType != BescriptVarType.String)
            return null;

        // Act/Varf 类动态调用
        bool isRegularDynamic =
            methodSignature.Parameters.Count > 0 &&
            string.Equals(methodSignature.Parameters[0].TypeName, nameof(BescriptVarType.String), StringComparison.Ordinal) &&
            (methodName.StartsWith("call", StringComparison.Ordinal) ||
             methodName.StartsWith("netCallFun", StringComparison.Ordinal) ||
             methodName.StartsWith("unReliableNetCallFun", StringComparison.Ordinal) ||
             string.Equals(methodName, "loopFun", StringComparison.Ordinal) ||
             string.Equals(methodName, "DisableFun", StringComparison.Ordinal) ||
             string.Equals(methodName, "fixedLoopAct", StringComparison.Ordinal));

        // UIButton 回调类
        bool isCallbackMethod =
            string.Equals(methodName, "addClickCB", StringComparison.Ordinal) ||
            string.Equals(methodName, "addPointDownCB", StringComparison.Ordinal) ||
            string.Equals(methodName, "addPointUpCB", StringComparison.Ordinal) ||
            string.Equals(methodName, "addPointEnterCB", StringComparison.Ordinal) ||
            string.Equals(methodName, "addPointExitCB", StringComparison.Ordinal);

        if (isRegularDynamic || isCallbackMethod)
            return firstArg.DecodedText;

        return null;
    }

    /// <summary>
    /// 校验动态方法名文本（纯方法名 或 别名.方法名）。
    /// 纯方法名 → 全工程方法存在性
    /// 别名.方法名 → 别名在作用域内存在性 + 全工程方法存在性
    /// </summary>
    private static void ValidateDynamicMethodText(
        string methodText,
        string methodPath,
        List<DeviceEntityBase> carrierDevices,
        HashSet<string> allMethodNames,
        string scriptRelativePath,
        int line,
        List<LintDiagnostic> diagnostics)
    {
        int dotIndex = methodText.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex > 0 && dotIndex == methodText.LastIndexOf('.') && dotIndex < methodText.Length - 1)
        {
            // 别名.方法名 格式
            string alias = methodText[..dotIndex];
            string funcName = methodText[(dotIndex + 1)..];

            // 检查别名在当前作用域内是否存在
            int aliasMatchCount = carrierDevices.Count(d => string.Equals(d.LogicName, alias, StringComparison.Ordinal));
            if (aliasMatchCount == 0)
            {
                diagnostics.Add(CreateActAliasNotFoundDiagnostic(alias, scriptRelativePath, line));
            }

            // 检查方法名全工程存在性
            if (!allMethodNames.Contains(funcName))
            {
                diagnostics.Add(CreateDynamicMethodNotFoundDiagnostic(methodPath, funcName, scriptRelativePath, line));
            }
        }
        else
        {
            // 纯方法名 → 全工程存在性
            if (!allMethodNames.Contains(methodText))
            {
                diagnostics.Add(CreateDynamicMethodNotFoundDiagnostic(methodPath, methodText, scriptRelativePath, line));
            }
        }
    }

    private static (HashSet<string> PublicMethodNames, Dictionary<int, HashSet<string>> PublicMethodNamesByDeviceType) BuildPublicMethodNameSets(
        ProjectModel model,
        ScriptAnalysisService analysisService,
        IReadOnlyList<DeviceEntityBase> devices)
    {
        HashSet<string> publicMethodNames = new(StringComparer.Ordinal);
        Dictionary<int, HashSet<string>> publicMethodNamesByDeviceType = [];

        foreach (DeviceEntityBase device in devices)
        {
            foreach (ScriptGroupEntityBase scriptGroup in device.ScriptGroups)
            {
                if (!string.IsNullOrWhiteSpace(scriptGroup.EventName))
                {
                    continue;
                }

                foreach (ScriptEntryInfo entry in scriptGroup.ScriptEntries)
                {
                    if (entry.IsPrivate)
                    {
                        continue;
                    }

                    BescriptParseData? parseData = null;

                    if (!entry.IsCodeRepo)
                    {
                        InlineScriptEntityBase? inlineScript = scriptGroup.Scripts
                            .FirstOrDefault(s => string.Equals(s.ScriptName, entry.Name, StringComparison.Ordinal));
                        if (inlineScript != null)
                        {
                            parseData = analysisService.ParseScript(inlineScript.实体_完整相对路径标识);
                        }
                    }
                    else if (GuidText.Validate(entry.Guid) &&
                             model.FindByGuid<CodeRepoScriptEntity>(entry.Guid) is CodeRepoScriptEntity codeRepoScript)
                    {
                        parseData = analysisService.ParseScript(codeRepoScript.实体_完整相对路径标识);
                    }

                    if (parseData is null || parseData.Private)
                    {
                        continue;
                    }

                    publicMethodNames.Add(entry.Name);
                    if (!publicMethodNamesByDeviceType.TryGetValue(device.DeviceType, out HashSet<string>? typeSet))
                    {
                        typeSet = new HashSet<string>(StringComparer.Ordinal);
                        publicMethodNamesByDeviceType[device.DeviceType] = typeSet;
                    }

                    typeSet.Add(entry.Name);
                }
            }
        }

        return (publicMethodNames, publicMethodNamesByDeviceType);
    }
}
