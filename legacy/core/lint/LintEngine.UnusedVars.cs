using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    // 可内联的 create 系列函数
    private static readonly HashSet<string> InlinableCreateFuncs = new(StringComparer.Ordinal)
    {
        "create.float",
        "create.long",
        "create.bool",
        "create.color",
        "create.string",
        "creatVariable.Vector3"
    };

    public static IReadOnlyList<LintDiagnostic> ValidateUnusedLocalVars(
        DeviceEntityBase device, ScriptAnalysisService analysisService)
    {
        List<LintDiagnostic> diagnostics = [];

        HashSet<string> globalNames = new(
            device.Vars.Variables.Select(g => g.Name),
            StringComparer.Ordinal);

        foreach (ScriptGroupEntityBase group in device.ScriptGroups)
        {
            foreach (ScriptEntryInfo entry in group.ScriptEntries)
            {
                if (entry.IsCodeRepo)
                    continue;

                InlineScriptEntityBase? inline = group.Scripts
                    .FirstOrDefault(s => string.Equals(s.ScriptName, entry.Name, StringComparison.Ordinal));
                if (inline is null)
                {
                    diagnostics.Add(CreateLintEngineErrorDiagnostic(
                        $"脚本清单与实体不一致：'{entry.Name}' 在脚本组中无对应实体，已跳过",
                        new LintLocation(group.listJson_完整相对路径, 1)));
                    continue;
                }

                BescriptParseData parseData = analysisService.ParseScript(inline.实体_完整相对路径标识)
                    ?? throw new InvalidOperationException($"脚本解析失败: {inline.实体_完整相对路径标识}");

                string scriptPath = inline.实体_完整相对路径标识;
                BescriptAst ast = parseData.Ast;
                ScriptVarInfo info = CollectScriptVarInfo(ast);

                foreach (BescriptParameter param in parseData.Parameters)
                {
                    if (!info.ReadNames.Contains(param.Name))
                        diagnostics.Add(CreateUnusedVarDiagnostic(param.Name, scriptPath));
                }

                string? returnParamName = parseData.ReturnParameter?.Name;
                foreach (string localVar in ast.DeclaredVariables.Keys)
                {
                    if (globalNames.Contains(localVar))
                        continue;
                    if (!info.ReadNames.Contains(localVar) &&
                        !string.Equals(localVar, returnParamName, StringComparison.Ordinal))
                        diagnostics.Add(CreateUnusedVarDiagnostic(localVar, scriptPath));
                }

                HashSet<string> paramNames = new(
                    parseData.Parameters.Select(p => p.Name),
                    StringComparer.Ordinal);

                diagnostics.AddRange(CollectInlinableVarDiagnostics(info, scriptPath, returnParamName, globalNames, paramNames));
            }
        }

        return diagnostics;
    }

    public static IReadOnlyList<LintDiagnostic> ValidateUnusedGlobalVars(
        ProjectModel model, ScriptAnalysisService analysisService)
    {
        List<LintDiagnostic> diagnostics = [];

        foreach (DeviceEntity device in model.List<DeviceEntity>())
        {
            IReadOnlyList<VarInfo> globals = device.Vars.Variables;
            if (globals.Count == 0)
                continue;

            string varsPath = device.vars配置_完整相对路径;
            HashSet<string> globalNames = new(globals.Select(g => g.Name), StringComparer.Ordinal);
            HashSet<string> allGlobalUsages = new(StringComparer.Ordinal);

            foreach (ScriptGroupEntityBase group in device.ScriptGroups)
            {
                foreach (ScriptEntryInfo entry in group.ScriptEntries)
                {
                    string? scriptPath = ResolveScriptPath(entry, group, model);
                    if (scriptPath is null)
                    {
                        diagnostics.Add(CreateLintEngineErrorDiagnostic(
                            $"脚本清单与实体不一致：'{entry.Name}' 在脚本组中无对应实体，已跳过",
                            new LintLocation(group.listJson_完整相对路径, 1)));
                        continue;
                    }

                    BescriptParseData parseData = analysisService.ParseScript(scriptPath)
                        ?? throw new InvalidOperationException($"脚本解析失败: {scriptPath}");

                    BescriptAst ast = parseData.Ast;
                    HashSet<string> readVars = CollectReadVariables(ast);

                    foreach (string used in readVars)
                    {
                        bool isLocal = ast.DeclaredVariables.ContainsKey(used) && !globalNames.Contains(used);
                        bool isParam = parseData.Parameters.Any(p =>
                            string.Equals(p.Name, used, StringComparison.Ordinal));

                        if (!isLocal && !isParam)
                            allGlobalUsages.Add(used);
                    }
                }
            }

            foreach (VarInfo global in globals)
            {
                if (!allGlobalUsages.Contains(global.Name))
                    diagnostics.Add(CreateUnusedVarDiagnostic(global.Name, varsPath));
            }
        }

        return diagnostics;
    }

    internal static IEnumerable<LintDiagnostic> ValidateScriptVariableConflicts(
        BescriptParseData parseData,
        DeviceEntityBase device,
        string scriptPath,
        IReadOnlyDictionary<string, BescriptVarType> globalTypeMap)
    {
        string carrierGuid = device.CarrierGuid;
        string carrierKind = device.CarrierKind;

        foreach (BescriptParameter param in parseData.Parameters)
        {
            if (globalTypeMap.ContainsKey(param.Name))
            {
                yield return CreateParamGlobalClashDiagnostic(
                    param.Name,
                    device.DeviceId,
                    device.DeviceType,
                    carrierGuid,
                    carrierKind,
                    scriptPath);
            }
        }

        if (parseData.ReturnParameter != null &&
            globalTypeMap.ContainsKey(parseData.ReturnParameter.Name))
        {
            yield return CreateGlobalReturnClashDiagnostic(
                parseData.ReturnParameter.Name,
                device.DeviceId,
                device.DeviceType,
                carrierGuid,
                carrierKind,
                scriptPath);
        }

        foreach ((string localName, BescriptVarType localType) in parseData.Ast.DeclaredVariables)
        {
            if (globalTypeMap.TryGetValue(localName, out BescriptVarType globalType) &&
                globalType != localType)
            {
                yield return CreateGlobalTypeMismatchDiagnostic(
                    localName,
                    device.DeviceId,
                    device.DeviceType,
                    carrierGuid,
                    carrierKind,
                    scriptPath,
                    1);
            }
        }
    }

    // ── 共享数据结构 ──

    private sealed record ScriptVarInfo(
        HashSet<string> ReadNames,
        Dictionary<string, List<(BescriptCallSignature Call, int ArgIndex, BescriptAstNode Node)>> ReadPositions,
        Dictionary<string, int> WriteCounts,
        Dictionary<string, List<BescriptAstNode>> WriteNodes,
        Dictionary<string, (BescriptCallSignature Call, BescriptAstNode Node)> CreateCandidates,
        Dictionary<string, BescriptVarType> CreateVarTypes
    );

    // ── 单次遍历收集全部变量信息（unused + inlinable 共用）──

    private static ScriptVarInfo CollectScriptVarInfo(BescriptAst ast)
    {
        HashSet<string> readNames = new(StringComparer.Ordinal);
        Dictionary<string, List<(BescriptCallSignature, int, BescriptAstNode)>> readPositions = new(StringComparer.Ordinal);
        Dictionary<string, int> writeCounts = new(StringComparer.Ordinal);
        Dictionary<string, List<BescriptAstNode>> writeNodes = new(StringComparer.Ordinal);
        Dictionary<string, (BescriptCallSignature, BescriptAstNode)> createCandidates = new(StringComparer.Ordinal);
        Dictionary<string, BescriptVarType> createVarTypes = new(StringComparer.Ordinal);

        foreach (BescriptAstNode node in ast.RootNodes.FlattenNodes())
        {
            if (node is not IHasCall { Call: var call })
                continue;

            if (call.ReturnVar is { } ret)
            {
                writeCounts.TryGetValue(ret.Name, out int wc);
                writeCounts[ret.Name] = wc + 1;

                if (!writeNodes.TryGetValue(ret.Name, out List<BescriptAstNode>? wnList))
                {
                    wnList = [];
                    writeNodes[ret.Name] = wnList;
                }
                wnList.Add(node);

                if (call.Kind == BescriptCallKind.Fun &&
                    InlinableCreateFuncs.Contains(call.FunctionName) &&
                    call.Arguments.Count > 0 &&
                    !call.Arguments.Any(static a => a.IsVariable))
                {
                    createCandidates[ret.Name] = (call, node);
                    if (BescriptVarTypeHelper.TryParse(ret.TypeName) is { } vt)
                        createVarTypes[ret.Name] = vt;
                }
                else if (call.Kind == BescriptCallKind.Fun &&
                         !InlinableCreateFuncs.Contains(call.FunctionName))
                {
                    createCandidates[ret.Name] = (call, node);
                    if (BescriptVarTypeHelper.TryParse(ret.TypeName) is { } vt)
                        createVarTypes[ret.Name] = vt;
                }
            }

            for (int i = 0; i < call.Arguments.Count; i++)
            {
                BescriptCallArgument arg = call.Arguments[i];
                if (!arg.IsVariable)
                    continue;

                readNames.Add(arg.DecodedText);
                if (!readPositions.TryGetValue(arg.DecodedText, out List<(BescriptCallSignature, int, BescriptAstNode)>? list))
                {
                    list = [];
                    readPositions[arg.DecodedText] = list;
                }
                list.Add((call, i, node));
            }

            string? ownerName = call switch
            {
                VarfCallSignature v => v.OwnerNameIsAlias ? null : v.OwnerName,
                ActCallSignature a => a.OwnerNameIsAlias ? null : a.OwnerName,
                DvfCallSignature d => d.OwnerName,
                _ => null
            };

            if (ownerName != null &&
                (call is VarfCallSignature ||
                 !string.Equals(ownerName, "this", StringComparison.Ordinal)) &&
                !int.TryParse(ownerName, out _))
            {
                readNames.Add(ownerName);
                if (!readPositions.TryGetValue(ownerName, out List<(BescriptCallSignature, int, BescriptAstNode)>? list))
                {
                    list = [];
                    readPositions[ownerName] = list;
                }
                list.Add((call, -1, node));
            }
        }

        return new ScriptVarInfo(readNames, readPositions, writeCounts, writeNodes, createCandidates, createVarTypes);
    }

    /// <summary>
    /// 仅收集变量名（供 GlobalVars 用）。
    /// </summary>
    private static HashSet<string> CollectReadVariables(BescriptAst ast)
    {
        HashSet<string> used = new(StringComparer.Ordinal);
        foreach (BescriptAstNode node in ast.RootNodes.FlattenNodes())
        {
            if (node is not IHasCall)
                continue;

            BescriptCallSignature call = ((IHasCall)node).Call;

            foreach (BescriptCallArgument arg in call.Arguments)
            {
                if (arg.IsVariable)
                    used.Add(arg.DecodedText);
            }

            string? ownerName = call switch
            {
                VarfCallSignature v => v.OwnerNameIsAlias ? null : v.OwnerName,
                ActCallSignature a => a.OwnerNameIsAlias ? null : a.OwnerName,
                DvfCallSignature d => d.OwnerName,
                _ => null
            };

            if (ownerName != null &&
                (call is VarfCallSignature ||
                 !string.Equals(ownerName, "this", StringComparison.Ordinal)) &&
                !int.TryParse(ownerName, out _))
            {
                used.Add(ownerName);
            }
        }
        return used;
    }

    // ── 可内联局部变量 ──

    private static IReadOnlyList<LintDiagnostic> CollectInlinableVarDiagnostics(
        ScriptVarInfo info,
        string scriptPath,
        string? returnParamName,
        HashSet<string> globalNames,
        HashSet<string> paramNames)
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        List<LintDiagnostic> results = [];

        foreach ((string varName, (BescriptCallSignature? createCall, BescriptAstNode? declNode)) in info.CreateCandidates)
        {
            if (!info.WriteCounts.TryGetValue(varName, out int wc) || wc < 1)
                continue;

            if (!info.ReadPositions.ContainsKey(varName) || info.ReadPositions[varName].Count == 0)
                continue;

            if (!info.CreateVarTypes.TryGetValue(varName, out BescriptVarType varType))
                continue;

            if (globalNames.Contains(varName))
                continue;

            if (paramNames.Contains(varName))
                continue;

            if (string.Equals(varName, returnParamName, StringComparison.Ordinal))
                continue;

            bool isConstCreate = InlinableCreateFuncs.Contains(createCall.FunctionName) &&
                                 createCall.Arguments.Count > 0 &&
                                 !createCall.Arguments.Any(static a => a.IsVariable);

            if (isConstCreate)
            {
                // ── 常量内联（原有逻辑）──
                if (wc != 1)
                    continue;

                List<(BescriptCallSignature Call, int ArgIndex, BescriptAstNode Node)> readList = info.ReadPositions[varName];
                bool canInline = true;
                bool hasCreateStringFold = false; // 可折叠警告标志位
                foreach ((BescriptCallSignature call, int argIndex, BescriptAstNode node) in readList)
                {
                    if (argIndex < 0)
                    {
                        canInline = false;
                        break;
                    }

                    if (TryResolveBuiltinMethodSignature(registry, call) is not { } resolved)
                    {
                        canInline = false;
                        break;
                    }

                    MethodSignature sig = resolved.Sig;
                    if (argIndex >= sig.Parameters.Count)
                    {
                        canInline = false;
                        break;
                    }

                    MethodParameter param = sig.Parameters[argIndex];
                    if (BescriptVarTypeHelper.TryParse(param.TypeName) is BescriptVarType.Any)
                    {
                        canInline = false;
                        break;
                    }

                    if ((node.Kind is BescriptStatementKind.If or BescriptStatementKind.While) &&
                        call.Kind == BescriptCallKind.Fun &&
                        ConditionConstMustVarFunMethods.Contains(call.FunctionName))
                    {
                        canInline = false;
                        break;
                    }

                    if (BescriptVarTypeHelper.TryParse(param.TypeName) is not { } expectedType)
                    {
                        canInline = false;
                        break;
                    }

                    bool sameTypeConst = varType == expectedType;
                    bool createStringFold = readList.Count == 1 &&
                                            call.Kind == BescriptCallKind.Fun &&
                                            string.Equals(call.FunctionName, "create.string", StringComparison.Ordinal) &&
                                            expectedType == BescriptVarType.String &&
                                            param.AllowImplicitToString &&
                                            ConstFillMode.AnyPositionNonEmptyConstTypes.Contains(varType.ToString());

                    if (!sameTypeConst && !createStringFold)
                    {
                        canInline = false;
                        break;
                    }
                    if (createStringFold)
                        hasCreateStringFold = true;
                }

                if (canInline)
                {
                    string constText = BuildConstText(createCall, varType);
                    string message = $"局部变量可内联为常量: {varName} = {constText}";
                    if (hasCreateStringFold)
                    {
                        string stringConstText = createCall.Arguments.Count == 1
                            ? createCall.Arguments[0].DecodedText
                            : string.Join(",", createCall.Arguments.Select(static a => a.DecodedText));
                        message = $"局部变量可内联为 String 常量: {varName} = String {stringConstText}；仅 G.create.string 消费位适用：Bool/Float/Long/Vector3/Color/String 可隐式转 String，推荐直接写 BeString.fromBeConst(\"{stringConstText}\")。";
                    }
                    results.Add(Diag(
                        LintRuleIds.VarInlinableConst,
                        LintSeverity.Warning,
                        message,
                        new LintLocation(scriptPath, declNode.Line)));
                }
            }
            else
            {
                // ── 表达式内联：Bool 变量 + 消费方式 create.bool + 仅 if 条件 ──
                if (varType != BescriptVarType.Bool)
                    continue;

                List<(BescriptCallSignature Call, int ArgIndex, BescriptAstNode Node)> readList = info.ReadPositions[varName];
                if (readList.Count != 1)
                    continue;

                (BescriptCallSignature call, int argIndex, BescriptAstNode node) = readList[0];

                if (argIndex < 0)
                    continue;

                if (call.FunctionName != "create.bool")
                    continue;

                if (node.Kind != BescriptStatementKind.If)
                    continue;

                if (wc != 1)
                    continue;

                results.Add(Diag(
                    LintRuleIds.VarInlinableExpr,
                    LintSeverity.Warning,
                    $"if 条件支持直接使用 Bool 返回值表达式，无需单独变量，create.bool 包装可移除: {varName}",
                    new LintLocation(scriptPath, declNode.Line)));
            }
        }

        return results;
    }

    private static string BuildConstText(BescriptCallSignature createCall, BescriptVarType varType)
    {
        if (createCall.Arguments.Count == 1)
        {
            BescriptCallArgument arg = createCall.Arguments[0];
            return $"{arg.TypeName} {arg.DecodedText}";
        }

        string values = string.Join(", ", createCall.Arguments.Select(static a => a.DecodedText));
        return $"{varType} ({values})";
    }
}
