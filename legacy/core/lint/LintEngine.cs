using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Lint;

/// <summary>
/// Lint 引擎 - 提供 BeScript 代码静态检查功能
/// </summary>
public static partial class LintEngine
{
    private static readonly Log Log = Toolkit.Core.Infra.Log.For(LogModule.Core);

    /// <summary>
    /// 内置零件方法名：调用这些方法不会触发潜在递归警告，也用于检测命名冲突
    /// </summary>
    private static readonly HashSet<string> BuiltinDeviceMethodNames = BuildBuiltinDeviceMethodNames();
    private static readonly HashSet<string> BuiltinParamNames = BuildBuiltinParamNames();

    /// <summary>
    /// 全工程 Lint 扫描。
    /// 遍历所有零件，检查全局变量、脚本命名、方法调用等。
    /// </summary>
    /// <param name="model">工程模型</param>
    /// <param name="analysisService">脚本分析服务（用于 AST 解析）</param>
    /// <returns>所有诊断信息</returns>
    public static IReadOnlyList<LintDiagnostic> AnalyzeProject(ProjectModel model, ScriptAnalysisService analysisService)
    {
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        List<LintDiagnostic> diagnostics = [.. ValidateDuplicateGuids(model), .. ValidateTableFormats(model), .. ValidateColorPaletteFormat(model), .. ValidateCanvasFormats(model)];
        Log.Info($"Lint 阶段1(结构校验) {sw.Elapsed.TotalSeconds:F1}s {diagnostics.Count}条");

        sw.Restart();
        List<DeviceEntityBase> devices = model.EnumerateAllDevices().ToList();
        int deviceCount = devices.Count;
        int deviceIndex = 0;
        foreach (DeviceEntityBase device in devices)
        {
            sw.Restart();
            IReadOnlyList<LintDiagnostic> deviceDiags = FromDevice(device, analysisService, model);
            deviceIndex++;
            if (sw.ElapsedMilliseconds > 1000)
            {
                Log.Warning($"Lint 零件[{deviceIndex}/{deviceCount}] {device.实体_完整相对路径标识} {sw.Elapsed.TotalSeconds:F1}s {deviceDiags.Count}条");
            }
            diagnostics.AddRange(deviceDiags);
        }
        Log.Info($"Lint 阶段2(零件扫描) 共{deviceCount}个零件");

        sw.Restart();
        diagnostics.AddRange(ValidateActAndDynamicCalls(model, analysisService));
        Log.Info($"Lint 阶段3(Act/动态调用) {sw.Elapsed.TotalSeconds:F1}s");

        sw.Restart();
        diagnostics.AddRange(ValidateUnusedGlobalVars(model, analysisService));
        Log.Info($"Lint 阶段4(未用全局变量) {sw.Elapsed.TotalSeconds:F1}s");

        sw.Restart();
        CallGraphBuilder callGraphBuilder = new(model, analysisService);
        CallGraphReport callGraphReport = callGraphBuilder.Build();
        Log.Info($"Lint 阶段5(调用图构建) {sw.Elapsed.TotalSeconds:F1}s");

        sw.Restart();
        diagnostics.AddRange(ValidateCallGraphRecursion(callGraphReport));
        Log.Info($"Lint 阶段6(递归校验) {sw.Elapsed.TotalSeconds:F1}s");

        return DeduplicateDiagnostics(diagnostics);
    }

    internal static IReadOnlyList<LintDiagnostic> ValidateCallGraphRecursion(CallGraphReport report)
    {
        List<LintDiagnostic> diagnostics = [];

        foreach (UnresolvedActCall unresolved in report.UnresolvedActCalls)
        {
            if (unresolved.MethodName.Length != 0 &&
                BuiltinDeviceMethodNames.Contains(unresolved.MethodName))
                continue;

            if (string.Equals(unresolved.From.DevicePath, unresolved.TargetDevicePath, StringComparison.Ordinal))
                continue;

            diagnostics.Add(new LintDiagnostic(
                LintRuleIds.CallRecursionPotential,
                LintSeverity.Warning,
                $"act 调用'{unresolved.MethodName}'目标无法静态解析，可能存在递归风险",
                new LintLocation(unresolved.From.ScriptPath, unresolved.Line)));
        }

        foreach (CallGraphCycle cycle in report.Cycles)
        {
            string pathStr = string.Join(" -> ", cycle.Path.Select(FormatCycleNode));
            diagnostics.Add(new LintDiagnostic(
                LintRuleIds.CallRecursion,
                LintSeverity.Error,
                $"检测到递归调用: {pathStr}",
                new LintLocation(cycle.ClosingEdge.From.ScriptPath, 0)));
        }

        return diagnostics;
    }

    private static string FormatCycleNode(CallGraphNode node)
    {
        if (node.DeviceLogicName != null)
            return $"{node.DeviceLogicName}.{node.MethodName}";
        return $"{node.DeviceId}:{node.MethodName}";
    }

    public static IReadOnlyList<LintDiagnostic> FromDevice(DeviceEntityBase device, ScriptAnalysisService? analysisService = null, ProjectModel? model = null)
    {
        List<LintDiagnostic> diagnostics = [];

        string carrierGuid = device.CarrierGuid;
        string carrierKind = device.CarrierKind;

        // 检查全局变量
        string varsRelativePath = device.vars配置_完整相对路径;
        IEnumerable<VarInfo> globals = device.Vars.Variables;
        foreach (VarInfo global in globals)
        {
            diagnostics.AddRange(AnalyzeIdentifier(
                global.Name,
                varsRelativePath));

            diagnostics.AddRange(ValidateBescriptTextCharacters(global.Name, varsRelativePath));

            if (global.Comment != null)
                diagnostics.AddRange(ValidateBescriptTextCharacters(global.Comment, varsRelativePath));

            if (BuiltinParamNames.Contains(global.Name))
            {
                diagnostics.Add(CreateGlobalBuiltinParamClashDiagnostic(
                    global.Name,
                    device.DeviceId,
                    device.DeviceType,
                    carrierGuid,
                    carrierKind,
                    varsRelativePath));
            }
        }

        diagnostics.AddRange(ValidateGlobalScriptNameClashes(device, varsRelativePath));

        if (model != null)
            diagnostics.AddRange(ValidateScriptGroupConsistency(device, model));

        if (device.LogicName is string logicName)
            diagnostics.AddRange(ValidateBescriptTextCharacters(logicName, device.device配置_完整相对路径));

        if (analysisService != null)
        {
            HashSet<string> scannedScriptPaths = new(StringComparer.Ordinal);
            void ScanScriptText(string scriptRelativePath)
            {
                if (!scannedScriptPaths.Add(scriptRelativePath))
                    return;

                string sourceText = analysisService.GetScriptSourceText(scriptRelativePath)
                    ?? throw new InvalidOperationException($"脚本文本缺失: {scriptRelativePath}");
                diagnostics.AddRange(ValidateBescriptTextCharacters(sourceText, scriptRelativePath));
            }

            if (model != null) diagnostics.AddRange(ValidateCanvasUiPathReferences(model, device, analysisService));

            if (model != null) diagnostics.AddRange(ValidateFilenameParams(device, analysisService, model));

            // 方法调用签名验证
            diagnostics.AddRange(ValidateBuiltinMethodCallSignatures(device, analysisService));

            IReadOnlyDictionary<string, BescriptVarType> globalTypeMap = BuildVarTypeMap(device.Vars.Variables);

            // 其他脚本级分析（所有内联脚本：含事件脚本组与非事件脚本组）
            foreach (InlineScriptEntityBase script in device.AllScripts)
            {
                string scriptRelativePath = script.实体_完整相对路径标识;
                ScanScriptText(scriptRelativePath);
                BescriptParseData parseData = analysisService.ParseScript(scriptRelativePath)
                    ?? throw new InvalidOperationException($"脚本解析失败: {scriptRelativePath}");
                diagnostics.AddRange(AnalyzeScript(script, parseData, device));
                diagnostics.AddRange(ValidateScriptVariableConflicts(parseData, device, scriptRelativePath, globalTypeMap));
            }

            // 代码仓库脚本的变量冲突检查
            if (model != null)
            {
                foreach (ScriptGroupEntityBase group in device.ScriptGroups)
                {
                    if (group.ListJsonText is string listJsonText)
                        diagnostics.AddRange(ValidateBescriptTextCharacters(listJsonText, group.listJson_完整相对路径));

                    foreach (ScriptEntryInfo entry in group.ScriptEntries)
                    {
                        if (!entry.IsCodeRepo)
                            continue;

                        string? scriptPath = ResolveScriptPath(entry, group, model);
                        if (scriptPath is null)
                        {
                            diagnostics.Add(CreateLintEngineErrorDiagnostic(
                                $"脚本清单与实体不一致：代码仓库脚本'{entry.Name}'未找到，已跳过",
                                new LintLocation(group.listJson_完整相对路径, 1)));
                            continue;
                        }
                        ScanScriptText(scriptPath);

                        BescriptParseData parseData = analysisService.ParseScript(scriptPath)
                            ?? throw new InvalidOperationException($"脚本解析失败: {scriptPath}");

                        diagnostics.AddRange(ValidateScriptVariableConflicts(
                            parseData, device, scriptPath, globalTypeMap));
                    }
                }
            }

            diagnostics.AddRange(ValidateUnusedLocalVars(device, analysisService));
        }

        // 规则2：1110 同名事件脚本组内脚本名与目标机械核心重叠
        if (model != null)
            diagnostics.AddRange(ValidateBirthBuildingEventClashCore(device, model));

        return diagnostics;
    }

    /// <summary>
    /// 分析单个脚本。
    /// </summary>
    private static IEnumerable<LintDiagnostic> AnalyzeScript(
        InlineScriptEntityBase script,
        BescriptParseData parseData,
        DeviceEntityBase device)
    {
        List<LintDiagnostic> diagnostics = [];

        string carrierGuid = device.CarrierGuid;
        string carrierKind = device.CarrierKind;

        string methodName = parseData.Name;

        // 仅自定义脚本（非事件脚本）参与内置 act 名称冲突检查。
        if (string.IsNullOrWhiteSpace(script.EventName) && BuiltinDeviceMethodNames.Contains(methodName))
        {
            diagnostics.Add(CreateBuiltinMethodClashDiagnostic(
                methodName,
                device.DeviceId,
                device.DeviceType,
                carrierGuid,
                carrierKind,
                script.实体_完整相对路径标识));
        }

        // 规则1：自定义脚本名与该零件类型允许的事件名重名
        diagnostics.AddRange(ValidateCustomScriptNameClashEvent(device, parseData, script));

        // 脚本方法名本身也属于标识符，需要参与关键字检查。
        diagnostics.AddRange(AnalyzeIdentifier(
            methodName,
            script.实体_完整相对路径标识));

        return diagnostics;
    }

    private static string? ResolveScriptPath(ScriptEntryInfo entry, ScriptGroupEntityBase group, ProjectModel model)
    {
        if (entry.IsCodeRepo)
        {
            if (!entry.IsCodeRepo || !GuidText.Validate(entry.Guid))
                return null;
            if (model.FindByGuid<CodeRepoScriptEntity>(entry.Guid) is CodeRepoScriptEntity codeRepo)
                return codeRepo.实体_完整相对路径标识;
            return null;
        }

        InlineScriptEntityBase? inline = group.Scripts
            .FirstOrDefault(s => string.Equals(s.ScriptName, entry.Name, StringComparison.Ordinal));
        return inline?.实体_完整相对路径标识;
    }

    private static IReadOnlyList<LintDiagnostic> ValidateGlobalScriptNameClashes(DeviceEntityBase device, string varsRelativePath)
    {
        HashSet<string> scriptNames = new(StringComparer.Ordinal);
        foreach (ScriptGroupEntityBase group in device.ScriptGroups)
        {
            foreach (ScriptEntryInfo entry in group.ScriptEntries)
                scriptNames.Add(entry.Name);
        }

        List<LintDiagnostic> diagnostics = [];
        HashSet<string> reportedGlobals = new(StringComparer.Ordinal);
        foreach (VarInfo global in device.Vars.Variables)
        {
            if (scriptNames.Contains(global.Name) && reportedGlobals.Add(global.Name))
                diagnostics.Add(CreateGlobalScriptNameClashDiagnostic(global.Name, varsRelativePath));
        }

        return diagnostics;
    }
}
