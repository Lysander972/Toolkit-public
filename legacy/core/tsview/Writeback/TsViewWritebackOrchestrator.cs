using DiffPlex;
using DiffPlex.Model;

using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.TsView;

/// <summary>
/// TS 视图写回协调器。
/// 负责协调写回流程：
/// - 官方 TypeScript API：TS 文本解析、扫描与打印
/// - Layer 1: 官方解析结果 ↔ BescriptAst
/// - Layer 2: BescriptAst ↔ 构建需求实体集合
/// </summary>
internal sealed class TsViewWritebackOrchestrator
{
    private readonly ProjectModel _model;
    private readonly ProjectLogicContext _logic;

    public TsViewWritebackOrchestrator(ProjectModel model, ProjectLogicContext logic)
    {
        _model = model;
        _logic = logic;
    }

    /// <summary>
    /// 执行完整的写回流程。
    /// </summary>
    public (DeviceTsViewPatchData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Execute(
        string deviceRelativePath,
        TsViewPatch[] patches,
        string? baseTsView,
        string? baseTsViewFileRelativePath,
        bool forceRemoval = false,
        bool persistChanges = true)
    {
        List<ToolError> warnings = [];

        if (patches.Length == 0)
        {
            return FailPatch("缺少 patches");
        }

        (string resolvedDevicePath, string? pathError) = WorkspacePath.TryRequireRelativePath(deviceRelativePath);
        if (pathError != null)
        {
            return FailPatch(pathError);
        }
        deviceRelativePath = resolvedDevicePath;

        if (_model.Find<DeviceEntity>(deviceRelativePath) is not DeviceEntity readOnlyDevice)
        {
            return FailPatch($"零件实体未找到: {deviceRelativePath}");
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Phase 1: 获取 TS 视图和可编辑范围
        // ═══════════════════════════════════════════════════════════════════════════════
        string? currentTsView = baseTsView;
        TsViewEditAllowance? editAllowance = null;
        ActTargetMap? actTargetMap = null;
        string? resolvedBaseTsViewFileRelativePath = null;

        if (baseTsView is string { Length: > 0 } && baseTsViewFileRelativePath is not null)
        {
            (resolvedBaseTsViewFileRelativePath, string? baseTsViewFilePathError) = WorkspacePath.TryRequireRelativePath(baseTsViewFileRelativePath);
            if (baseTsViewFilePathError != null)
            {
                return FailPatch(baseTsViewFilePathError);
            }
        }

        if (baseTsView is string { Length: > 0 } providedTsView)
        {
            DeviceTsViewBuildResult fresh = DeviceTsView.BuildDeviceTsView(_model, deviceRelativePath, warnings, cache: _logic.ScriptAstCache);
            editAllowance = fresh.EditAllowance;
            actTargetMap = fresh.ActTargetMap;
            string freshTsViewForCompare = BuildTsViewForCompare(fresh.TsView, resolvedBaseTsViewFileRelativePath);

            if (!string.Equals(providedTsView, freshTsViewForCompare, StringComparison.Ordinal))
            {
                return FailPatch("TS 视图基线已变化，请先重新读取最新视图后再应用补丁。");
            }
        }
        else
        {
            DeviceTsViewBuildResult buildResult = DeviceTsView.BuildDeviceTsView(_model, deviceRelativePath, warnings, cache: _logic.ScriptAstCache);
            currentTsView = buildResult.TsView;
            editAllowance = buildResult.EditAllowance;
            actTargetMap = buildResult.ActTargetMap;
        }

        if (editAllowance is null)
        {
            return FailPatch("缺少 TS 视图编辑区段信息");
        }

        if (currentTsView is not { } tsView)
        {
            return FailPatch("TS 视图生成失败");
        }

        List<string> currentLines = TextLines.SplitLines(tsView).ToList();

        // ═══════════════════════════════════════════════════════════════════════════════
        // Phase 2: 应用补丁 + 视图固定骨架行校验（三区段以外、生成时锁定的行不得改文本）
        // ═══════════════════════════════════════════════════════════════════════════════
        (List<string> patchedLines, string? patchError) = ApplyLinePatches(currentLines.ToList(), patches);
        if (patchError != null)
        {
            return FailPatch(patchError);
        }

        Dictionary<int, int> lineMapping = BuildLineMapping(currentLines, patchedLines);
        string? structureError = ValidateFixedStructureLines(currentLines, patchedLines, editAllowance.ForbiddenRanges, lineMapping);
        if (structureError != null)
        {
            return FailPatch(structureError);
        }

        // 【禁止】禁止以 patchedLines == currentLines 为由短路跳过 Phase 3+ 管线。
        // 回环无损测试依赖完整管线执行来验证转换一致性，短路 = 掩盖潜在有损问题。

        // ═══════════════════════════════════════════════════════════════════════════════
        // Phase 3: 官方解析 + 两层业务转换 → IncompleteBuildRequirements
        // ═══════════════════════════════════════════════════════════════════════════════
        (DeviceTsView.IncompleteBuildRequirements? incomplete, IReadOnlyList<ToolError> parseErrors) parseResult =
            ParseForWriteback(patchedLines, currentLines, deviceRelativePath, warnings);

        if (parseResult.incomplete is null)
        {
            return (null, parseResult.parseErrors, warnings);
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Phase 3.5: 异步一致性校验
        // ═══════════════════════════════════════════════════════════════════════════════
        (IReadOnlyList<ToolError> asyncErrors, IReadOnlyList<ToolError> asyncWarnings) =
            TsViewAsyncValidation.Validate(parseResult.incomplete.ScriptUpdates, deviceRelativePath, actTargetMap);

        if (asyncErrors.Count > 0)
        {
            return (null, asyncErrors, warnings);
        }

        foreach (ToolError w in asyncWarnings)
        {
            warnings.Add(w);
        }

        // .code 侧重算：按 SDK 注册重算 async 状态
        IReadOnlyList<DeviceTsView.ScriptUpdate> bescriptUpdates =
            TsViewAsyncValidation.RewriteAsyncForBescriptUpdates(parseResult.incomplete.ScriptUpdates);
        if (!ReferenceEquals(bescriptUpdates, parseResult.incomplete.ScriptUpdates))
        {
            parseResult = (parseResult.incomplete with { ScriptUpdates = bescriptUpdates }, parseResult.parseErrors);
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Phase 3.6: 内联 Canvas 统一校验
        // ═══════════════════════════════════════════════════════════════════════════════
        (IReadOnlyList<ToolError> canvasErrors, IReadOnlyList<ToolError> canvasWarnings) =
            ValidateInlineCanvasChanges(parseResult.incomplete.ScriptUpdates, parseResult.incomplete.BaseScriptUpdates);

        if (canvasErrors.Count > 0)
        {
            return (null, canvasErrors, warnings);
        }

        foreach (ToolError w in canvasWarnings)
        {
            warnings.Add(w);
        }

        IReadOnlyList<ToolError> textCharacterErrors =
            ValidateBescriptTextCharacterChanges(parseResult.incomplete.ScriptUpdates, parseResult.incomplete.BaseScriptUpdates);

        if (textCharacterErrors.Count > 0)
        {
            return (null, textCharacterErrors, warnings);
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Phase 4: 冲突检测
        // ═══════════════════════════════════════════════════════════════════════════════
        ToolError? conflictError = DetectCodeRepoConflict(parseResult.incomplete.ScriptUpdates, deviceRelativePath);
        if (conflictError != null)
        {
            return (null, new[] { conflictError }, warnings);
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Phase 5: Layer 3 补全 → CompleteBuildRequirements
        // ═══════════════════════════════════════════════════════════════════════════════
        DeviceTsView.CompleteBuildRequirements complete = CompleteRequirements(parseResult.incomplete, readOnlyDevice);

        // ═══════════════════════════════════════════════════════════════════════════════
        // Phase 6: 验证删除操作
        // ═══════════════════════════════════════════════════════════════════════════════
        if (complete.Removals.Count > 0 && !forceRemoval)
        {
            IReadOnlyList<ToolError> removalErrors = DeviceTsView.ValidateRemovalsForApplyPatch(_logic, complete.Removals, complete.ScriptUpdates);
            if (removalErrors.Count > 0)
            {
                return (null, removalErrors, warnings);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Phase 7: 级联诊断检查（CodeRepo 脚本修改影响其他零件）
        // ═══════════════════════════════════════════════════════════════════════════════
        if (complete.ScriptUpdates.Count > 0)
        {
            (bool cascadeOk, IReadOnlyList<ToolError> cascadeErrors) = ValidateCascadeChanges(
                deviceRelativePath, complete.ScriptUpdates, warnings);
            if (!cascadeOk)
            {
                return (null, cascadeErrors, warnings);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Phase 8: 统一持久化
        // ═══════════════════════════════════════════════════════════════════════════════
        if (persistChanges)
        {
            IReadOnlyList<string> affectedPaths = EntityWritebackDispatcher.Persist(_model, complete, warnings);
            if (affectedPaths.Count == 0)
            {
                warnings.Add(new ToolError(
                    deviceRelativePath,
                    null,
                    null,
                    "本次补丁没有产生任何脚本或列表或变量的实际变动。可修改内容仅限：全局变量、事件脚本、自定义脚本。其他内容（如文件头、类声明、BeScript/BeScriptAsync 这类固定结构、区段标记等）不会被写回。若你试图只改这里来控制是否异步,这是无效的: 真正决定因素是方法是否带返回值,以及方法体内是否存在需要等待的调用。",
                    ToolDiagnosticSeverity.Warning));
            }

            return (new DeviceTsViewPatchData(
                    DeviceRelativePath: deviceRelativePath,
                    Applied: true,
                    PatchedTsView: TextLines.BuildLines(patchedLines.ToArray()),
                    AffectedRelativePaths: affectedPaths),
                Array.Empty<ToolError>(),
                warnings);
        }

        return (new DeviceTsViewPatchData(
                DeviceRelativePath: deviceRelativePath,
                Applied: true,
                PatchedTsView: TextLines.BuildLines(patchedLines.ToArray()),
                AffectedRelativePaths: Array.Empty<string>()),
            Array.Empty<ToolError>(),
            warnings);
    }

    private static string BuildTsViewForCompare(string tsView, string? baseTsViewFileRelativePath)
    {
        if (baseTsViewFileRelativePath is null)
        {
            return tsView;
        }

        return TsViewSupportReferenceRewriter.Rewrite(
            tsView,
            baseTsViewFileRelativePath,
            ScriptPathHelper.ViewsPath,
            ScriptPathHelper.BescriptReferenceRootPath);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 官方解析 + 两层业务转换: TS 文本 → 写回业务结果 → BescriptAst → bes 上下文
    // ═══════════════════════════════════════════════════════════════════════════════

    private (DeviceTsView.IncompleteBuildRequirements? incomplete, IReadOnlyList<ToolError> errors) ParseForWriteback(
        IReadOnlyList<string> patchedLines,
        IReadOnlyList<string> currentLines,
        string deviceRelativePath,
        List<ToolError> warnings)
    {
        List<ToolError> patchedErrors = [];

        // 基线解析用于脚本删除检测、Error diff，以及判断全局变量声明是否被删除。
        List<ToolError> baselineErrors = [];
        DeviceTsView.WritebackParseResult baselineResult = DeviceTsView.ParseDeviceTsViewForWriteback(
            _model,
            currentLines,
            deviceRelativePath,
            warnings,
            collectedErrors: baselineErrors);

        // 解析补丁后 TS 视图
        DeviceTsView.WritebackParseResult patchedResult = DeviceTsView.ParseDeviceTsViewForWriteback(
            _model,
            patchedLines,
            deviceRelativePath,
            warnings,
            collectedErrors: patchedErrors);

        // 致命错误直接阻断
        if (patchedResult.Error != null)
        {
            return (null, AggregateDeletedGlobalUsageErrors(baselineResult, patchedResult, [patchedResult.Error]));
        }

        List<ToolError> fatalErrors = patchedErrors.Where(e => e.Severity == ToolDiagnosticSeverity.Fatal).ToList();
        if (fatalErrors.Count > 0)
        {
            return (null, AggregateDeletedGlobalUsageErrors(baselineResult, patchedResult, fatalErrors));
        }

        // Error diff：新增 Error 整体拒绝
        List<ToolError> patchedNonFatal = patchedErrors.Where(e => e.Severity != ToolDiagnosticSeverity.Fatal).ToList();
        if (patchedNonFatal.Count > 0)
        {
            HashSet<string> baselineSignatures = new(
                baselineErrors.Select(e => $"{e.Path}|{e.Message}"),
                StringComparer.Ordinal);

            List<ToolError> newErrors = patchedNonFatal
                .Where(e => !baselineSignatures.Contains($"{e.Path}|{e.Message}"))
                .ToList();

            if (newErrors.Count > 0)
            {
                return (null, AggregateDeletedGlobalUsageErrors(baselineResult, patchedResult, newErrors));
            }
        }

        (IReadOnlyList<ToolError> canvasErrors, _) =
            ValidateInlineCanvasChanges(patchedResult.Updates, baselineResult.Updates);
        if (canvasErrors.Count > 0)
        {
            return (null, canvasErrors);
        }

        // 一致性校验：desiredScriptLists 中的每个非 CodeRepo 条目必须在 scriptUpdates 中有对应
        // 不一致 = 全量重建时必然丢文件，直接拒绝
        HashSet<string> updatePaths = new(
            patchedResult.Updates.Select(u => WorkspacePath.RequireRelativePath(u.SourcePath)),
            StringComparer.Ordinal);
        List<string> missingScripts = [];
        foreach ((string listPath, List<DeviceTsView.DesiredScriptListEntry> entries) in patchedResult.DesiredScriptLists)
        {
            string scriptDir = RelativePathUtils.GetParentDirectory(listPath);
            foreach (DeviceTsView.DesiredScriptListEntry entry in entries)
            {
                if (GuidText.Validate(entry.Guid)) continue; // CodeRepo 由外部管理
                string expectedPath = RelativePathUtils.Combine(scriptDir, entry.Name + ProjectEntityLayout.CodeExt);
                if (!updatePaths.Contains(expectedPath))
                    missingScripts.Add(expectedPath);
            }
        }
        if (missingScripts.Count > 0)
        {
            string detail = string.Join(", ", missingScripts);
            string errDetail = string.Empty;
            if (patchedErrors.Count > 0)
                errDetail = "\n解析错误: " + string.Join("; ", patchedErrors.Select(e => $"[{e.Path}:{e.Line}] {e.Message}"));
            return (null, new[] { new ToolError(
                deviceRelativePath, null, null,
                $"写回一致性校验失败：{missingScripts.Count} 个脚本在列表中声明但解析失败，全量重建会导致丢失: {detail}{errDetail}",
                ToolDiagnosticSeverity.Fatal) });
        }

        // 构建删除列表
        List<DeviceTsView.ScriptRemoval> removals = DeviceTsView.BuildScriptRemovals(
            baselineResult.DesiredScriptLists, patchedResult.DesiredScriptLists, deviceRelativePath);

        DeviceTsView.IncompleteBuildRequirements incomplete = new(
            deviceRelativePath,
            patchedResult.Updates,
            patchedResult.DesiredScriptLists,
            patchedResult.DesiredGlobalVars,
            removals,
            baselineResult.Updates);

        return (incomplete, Array.Empty<ToolError>());
    }

    private static IReadOnlyList<ToolError> AggregateDeletedGlobalUsageErrors(
        DeviceTsView.WritebackParseResult baselineResult,
        DeviceTsView.WritebackParseResult patchedResult,
        IReadOnlyList<ToolError> errors)
    {
        HashSet<string> patchedGlobals = new(
            patchedResult.DesiredGlobalVars.Select(v => v.Name),
            StringComparer.Ordinal);
        HashSet<string> deletedGlobals = new(
            baselineResult.DesiredGlobalVars
                .Select(v => v.Name)
                .Where(name => !patchedGlobals.Contains(name)),
            StringComparer.Ordinal);

        if (deletedGlobals.Count == 0)
            return errors;

        Dictionary<string, List<ToolError>> deletedUsageErrors = new(StringComparer.Ordinal);
        List<ToolError> remaining = [];

        foreach (ToolError error in errors)
        {
            bool isGlobalUsageError =
                string.Equals(error.Code, TsViewDiagnosticCodes.MissingGlobalVariable, StringComparison.Ordinal) ||
                string.Equals(error.Code, TsViewDiagnosticCodes.InvalidThisGlobalReference, StringComparison.Ordinal);

            if (isGlobalUsageError &&
                error.Subject is string subject &&
                deletedGlobals.Contains(subject))
            {
                if (!deletedUsageErrors.TryGetValue(subject, out List<ToolError>? subjectErrors))
                {
                    subjectErrors = [];
                    deletedUsageErrors[subject] = subjectErrors;
                }
                subjectErrors.Add(error);
                continue;
            }

            remaining.Add(error);
        }

        foreach (string deletedGlobal in deletedGlobals)
        {
            if (deletedUsageErrors.ContainsKey(deletedGlobal) ||
                !patchedResult.ThisMemberUsages.ContainsKey(deletedGlobal))
            {
                continue;
            }

            List<ToolError> subjectErrors = errors
                .Where(error => string.Equals(error.Subject, deletedGlobal, StringComparison.Ordinal))
                .ToList();
            if (subjectErrors.Count == 0)
            {
                subjectErrors.AddRange(errors.Where(error =>
                    error.Message.StartsWith(TsViewBodyParser.BodyStructureConventionPrefix, StringComparison.Ordinal) &&
                    (error.Message.Contains($"无法推断变量类型: {deletedGlobal}", StringComparison.Ordinal) ||
                        error.Message.Contains($"未知全局变量: {deletedGlobal}", StringComparison.Ordinal) ||
                        error.Message.Contains($"this. 仅允许用于全局变量: {deletedGlobal}", StringComparison.Ordinal))));
            }

            if (subjectErrors.Count == 0)
            {
                continue;
            }

            if (!subjectErrors.Any(error => string.Equals(error.Subject, deletedGlobal, StringComparison.Ordinal)))
            {
                ToolError firstError = subjectErrors
                    .OrderBy(static error => error.Line.HasValue ? error.Line.Value : int.MaxValue)
                    .First();
                int firstIndex = subjectErrors.IndexOf(firstError);
                subjectErrors[firstIndex] = firstError with
                {
                    Code = TsViewDiagnosticCodes.InvalidThisGlobalReference,
                    Subject = deletedGlobal
                };
            }

            deletedUsageErrors[deletedGlobal] = subjectErrors;
            remaining.RemoveAll(error => subjectErrors.Contains(error));
        }

        if (deletedUsageErrors.Count == 0)
            return errors;

        foreach (string deletedGlobal in deletedUsageErrors.Keys)
        {
            remaining.RemoveAll(error =>
                error.Message.StartsWith(TsViewBodyParser.BodyStructureConventionPrefix, StringComparison.Ordinal) &&
                (error.Message.Contains($"无法推断变量类型: {deletedGlobal}", StringComparison.Ordinal) ||
                    error.Message.Contains($"未知全局变量: {deletedGlobal}", StringComparison.Ordinal) ||
                    error.Message.Contains($"this. 仅允许用于全局变量: {deletedGlobal}", StringComparison.Ordinal)));
        }

        List<string> details = [];
        foreach ((string name, _) in deletedUsageErrors.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            List<int> usageLines = patchedResult.ThisMemberUsages[name];
            string[] locations = usageLines
                .Distinct()
                .OrderBy(line => line)
                .Take(20)
                .Select(line => line.ToString())
                .ToArray();
            string suffix = usageLines.Distinct().Count() > locations.Length
                ? " 等"
                : string.Empty;
            details.Add(locations.Length == 0 ? name : $"{name}（行 {string.Join(", ", locations)}{suffix}）");
        }

        string message =
            $"将被删除的全局变量仍在 TS 视图中被使用: {string.Join("; ", details)}。删除全局变量声明会让这些 this.<变量名> 引用失去全局变量语义，写回后会导致语义发生变化并伴随巨大副作用。推荐使用 refactor_vars_to_local 删除或降级全局变量，让工具先检查引用；如果使用次数不多，也可以用 patch_mech_view / patch_mech_view_multiedit 逐处调整这些引用后再删除声明。";

        ToolError first = deletedUsageErrors.Values.SelectMany(static x => x).OrderBy(static error => error.Line.HasValue ? error.Line.Value : int.MaxValue).First();
        remaining.Insert(0, new ToolError(first.Path, first.Line, null, message, ToolDiagnosticSeverity.Fatal)
        {
            Source = first.Source,
            Code = TsViewDiagnosticCodes.DeletedGlobalVariableStillUsed,
            Subject = string.Join(",", deletedUsageErrors.Keys.OrderBy(key => key, StringComparer.Ordinal))
        });

        return remaining;
    }

    private static (IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) ValidateInlineCanvasChanges(
        IReadOnlyList<DeviceTsView.ScriptUpdate> patchedUpdates,
        IReadOnlyList<DeviceTsView.ScriptUpdate> baseUpdates)
    {
        Dictionary<string, DeviceTsView.ScriptUpdate> baseUpdateMap = new(StringComparer.Ordinal);
        foreach (DeviceTsView.ScriptUpdate baseUpdate in baseUpdates)
        {
            baseUpdateMap[$"{baseUpdate.SourcePath}|{baseUpdate.MethodName}"] = baseUpdate;
        }

        List<ToolError> errors = [];
        foreach (DeviceTsView.ScriptUpdate patchedUpdate in patchedUpdates)
        {
            if (patchedUpdate.InlineCanvases.Count == 0)
                continue;

            baseUpdateMap.TryGetValue($"{patchedUpdate.SourcePath}|{patchedUpdate.MethodName}", out DeviceTsView.ScriptUpdate? baseUpdate);

            for (int i = 0; i < patchedUpdate.InlineCanvases.Count; i++)
            {
                DeviceTsView.InlineCanvasLiteral patchedInline = patchedUpdate.InlineCanvases[i];
                IReadOnlyList<string> baselineErrors = Array.Empty<string>();
                if (baseUpdate != null && i < baseUpdate.InlineCanvases.Count)
                {
                    DeviceTsView.InlineCanvasLiteral baseInline = baseUpdate.InlineCanvases[i];
                    baselineErrors = CanvasViewValidation.CollectHtmlLintErrors(baseInline.RawCanvas, patchedUpdate.SourcePath);
                }

                IReadOnlyList<string> patchedErrors = CanvasViewValidation.CollectHtmlLintErrors(
                    patchedInline.RawCanvas,
                    patchedUpdate.SourcePath);
                List<string> newErrors = CanvasViewValidation.ComputeNewErrors(baselineErrors, patchedErrors);
                foreach (string error in newErrors)
                {
                    errors.Add(new ToolError(
                        patchedUpdate.SourcePath,
                        patchedInline.LineNumber,
                        null,
                        $"内联 Canvas 校验失败: {error}",
                        ToolDiagnosticSeverity.Error));
                }
            }
        }

        return (errors, Array.Empty<ToolError>());
    }

    private static IReadOnlyList<ToolError> ValidateBescriptTextCharacterChanges(
        IReadOnlyList<DeviceTsView.ScriptUpdate> patchedUpdates,
        IReadOnlyList<DeviceTsView.ScriptUpdate> baseUpdates)
    {
        Dictionary<string, DeviceTsView.ScriptUpdate> baseUpdateMap = new(StringComparer.Ordinal);
        foreach (DeviceTsView.ScriptUpdate baseUpdate in baseUpdates)
            baseUpdateMap[$"{baseUpdate.SourcePath}|{baseUpdate.MethodName}"] = baseUpdate;

        List<ToolError> errors = [];
        foreach (DeviceTsView.ScriptUpdate patchedUpdate in patchedUpdates)
        {
            string patchedScriptText = EntityWritebackDispatcher.BuildScriptText(patchedUpdate);
            List<ToolError> baselineErrors = [];
            Dictionary<int, int> lineMapping = [];
            if (baseUpdateMap.TryGetValue($"{patchedUpdate.SourcePath}|{patchedUpdate.MethodName}", out DeviceTsView.ScriptUpdate? baseUpdate))
            {
                string baseScriptText = EntityWritebackDispatcher.BuildScriptText(baseUpdate);
                foreach (LintDiagnostic diagnostic in LintEngine.ValidateBescriptTextCharacters(baseScriptText, baseUpdate.SourcePath))
                    baselineErrors.Add(new ToolError(diagnostic.Location.Path, diagnostic.Location.Line, null, diagnostic.Message, ToolDiagnosticSeverity.Error));
                lineMapping = BuildLineMapping(
                    TextLines.SplitLines(baseScriptText).ToList(),
                    TextLines.SplitLines(patchedScriptText).ToList());
            }

            List<ToolError> patchedErrors = [];
            foreach (LintDiagnostic diagnostic in LintEngine.ValidateBescriptTextCharacters(patchedScriptText, patchedUpdate.SourcePath))
                patchedErrors.Add(new ToolError(diagnostic.Location.Path, diagnostic.Location.Line, null, diagnostic.Message, ToolDiagnosticSeverity.Error));
            errors.AddRange(ComputeNewErrors(baselineErrors, patchedErrors, lineMapping));
        }

        return errors;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Layer 3: bes 上下文 → 完整构建需求实体集合
    // ═══════════════════════════════════════════════════════════════════════════════

    private static DeviceTsView.CompleteBuildRequirements CompleteRequirements(
        DeviceTsView.IncompleteBuildRequirements incomplete,
        DeviceEntity readOnlyDevice)
    {
        // 用只读零件实体补全：ToWritable() 保留了 device.json 中 TS 视图无法还原的字段
        WritableDeviceEntity writableDevice = readOnlyDevice.ToWritable();

        return new DeviceTsView.CompleteBuildRequirements(
            writableDevice,
            incomplete.ScriptUpdates,
            incomplete.DesiredScriptLists,
            incomplete.DesiredGlobalVars,
            incomplete.Removals);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 补丁应用和验证
    // ═══════════════════════════════════════════════════════════════════════════════

    private static (List<string> Lines, string? Error) ApplyLinePatches(List<string> lines, TsViewPatch[] patches)
    {
        List<string> original = new(lines);

        TsViewPatch[] sorted = patches.OrderByDescending(p => p.StartLine).ToArray();
        foreach (TsViewPatch patch in sorted)
        {
            NormalizePatchIndentation(patch);

            if (patch.StartLine < 1 || patch.EndLine < patch.StartLine || patch.EndLine > lines.Count)
            {
                return (original, $"补丁行号越界: {patch.StartLine}-{patch.EndLine}");
            }

            int removeCount = patch.EndLine - patch.StartLine + 1;
            lines.RemoveRange(patch.StartLine - 1, removeCount);

            if (patch.NewLines.Length > 0)
            {
                lines.InsertRange(patch.StartLine - 1, patch.NewLines);
            }
        }

        return (lines, null);
    }

    private static void NormalizePatchIndentation(TsViewPatch patch)
    {
        if (patch.NewLines.Length == 0)
        {
            return;
        }

        for (int i = 0; i < patch.NewLines.Length; i++)
        {
            if (patch.NewLines[i].Contains('\t'))
            {
                patch.NewLines[i] = patch.NewLines[i].Replace("\t", "  ");
            }
        }
    }

    private static string? ValidateFixedStructureLines(
        IReadOnlyList<string> baseLines,
        IReadOnlyList<string> patchedLines,
        IReadOnlyList<TsViewEditRange> fixedLineRanges,
        IReadOnlyDictionary<int, int> lineMapping)
    {
        if (fixedLineRanges.Count == 0)
        {
            return null;
        }

        foreach (TsViewEditRange range in fixedLineRanges)
        {
            for (int line = range.StartLine; line <= range.EndLine; line++)
            {
                if (line < 1 || line > baseLines.Count)
                {
                    return BuildFixedStructureTamperMessage(fixedLineRanges, line);
                }

                if (!lineMapping.TryGetValue(line, out int mappedLine)
                    || mappedLine < 1
                    || mappedLine > patchedLines.Count)
                {
                    return BuildFixedStructureTamperMessage(fixedLineRanges, line);
                }

                if (!string.Equals(baseLines[line - 1], patchedLines[mappedLine - 1], StringComparison.Ordinal))
                {
                    return BuildFixedStructureTamperMessage(fixedLineRanges, line);
                }
            }
        }

        return null;
    }

    private static string BuildFixedStructureTamperMessage(
        IReadOnlyList<TsViewEditRange> fixedLineRanges,
        int changedLine)
    {
        string segments = "(无)";
        if (fixedLineRanges.Count != 0)
        {
            segments = string.Join("；", fixedLineRanges.Select(r =>
            {
                if (r.StartLine == r.EndLine)
                    return $"{r.StartLine}";
                return $"{r.StartLine}-{r.EndLine}";
            }));
        }
        return $"TS视图固定骨架被改动（基线第 {changedLine} 行）。仅 [Globals]/[Script]/[Events] 区段内部可改；import、namespace、class 壳、@events 与三区 BEGIN/END 边界行以及区段外内容须与基线逐行一致。固定骨架行号区间: {segments}";
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 行号映射
    // ═══════════════════════════════════════════════════════════════════════════════

    private static Dictionary<int, int> BuildLineMapping(
        IReadOnlyList<string> baseLines,
        IReadOnlyList<string> patchedLines)
    {
        Dictionary<int, int> mapping = [];
        if (baseLines.Count == 0)
        {
            return mapping;
        }

        Differ differ = new();
        DiffResult result = differ.CreateDiffs(
            string.Join("\n", baseLines),
            string.Join("\n", patchedLines),
            false,
            false,
            new DiffPlex.Chunkers.LineChunker());

        List<DiffBlock> blocks = result.DiffBlocks.OrderBy(b => b.DeleteStartA).ToList();

        int offset = 0;
        int blockIndex = 0;

        for (int baseLine = 1; baseLine <= baseLines.Count; baseLine++)
        {
            int baseIndex = baseLine - 1;

            while (blockIndex < blocks.Count && blocks[blockIndex].DeleteStartA + blocks[blockIndex].DeleteCountA <= baseIndex)
            {
                DiffBlock block = blocks[blockIndex];
                offset += block.InsertCountB - block.DeleteCountA;
                blockIndex++;
            }

            if (blockIndex < blocks.Count)
            {
                DiffBlock block = blocks[blockIndex];
                if (baseIndex >= block.DeleteStartA && baseIndex < block.DeleteStartA + block.DeleteCountA)
                {
                    int mappedLine = block.InsertStartB + 1 + (baseIndex - block.DeleteStartA);
                    if (mappedLine > block.InsertStartB + block.InsertCountB)
                    {
                        mappedLine = block.InsertStartB + Math.Max(block.InsertCountB, 1);
                    }
                    mapping[baseLine] = mappedLine;
                    continue;
                }
            }

            mapping[baseLine] = baseLine + offset;
        }

        return mapping;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 冲突检测
    // ═══════════════════════════════════════════════════════════════════════════════

    private static ToolError? DetectCodeRepoConflict(IReadOnlyList<DeviceTsView.ScriptUpdate> updates, string deviceRelativePath)
    {
        Dictionary<string, List<DeviceTsView.ScriptUpdate>> pathToUpdates = new(StringComparer.Ordinal);
        foreach (DeviceTsView.ScriptUpdate update in updates)
        {
            if (!pathToUpdates.TryGetValue(update.SourcePath, out List<DeviceTsView.ScriptUpdate>? list))
            {
                list = [];
                pathToUpdates[update.SourcePath] = list;
            }
            list.Add(update);
        }

        foreach ((string path, List<DeviceTsView.ScriptUpdate> list) in pathToUpdates)
        {
            if (list.Count <= 1)
            {
                continue;
            }

            DeviceTsView.ScriptUpdate first = list[0];
            List<string> firstCommands = BescriptAstSerializer.Serialize(first.BodyAst);
            bool hasConflict = false;
            for (int i = 1; i < list.Count; i++)
            {
                DeviceTsView.ScriptUpdate other = list[i];
                List<string> otherCommands = BescriptAstSerializer.Serialize(other.BodyAst);
                if (!firstCommands.SequenceEqual(otherCommands))
                {
                    hasConflict = true;
                    break;
                }
            }

            if (hasConflict)
            {
                string methodNames = string.Join(", ", list.Select(u => u.MethodName));
                return new ToolError(
                    deviceRelativePath,
                    null,
                    null,
                    $"CodeRepo 脚本冲突：{path} 被多处修改且内容不一致（{methodNames}）",
                    ToolDiagnosticSeverity.Fatal);
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 删除验证
    // ═══════════════════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════════════════
    // 级联诊断检查（CodeRepo 脚本修改影响其他零件）
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 对 CodeRepo 脚本修改执行级联诊断检查。
    /// CodeRepo 脚本被多个零件引用时，修改会影响所有引用它的零件。
    /// </summary>
    private (bool Ok, IReadOnlyList<ToolError> NewErrors) ValidateCascadeChanges(
        string currentDevicePath,
        IReadOnlyList<DeviceTsView.ScriptUpdate> scriptUpdates,
        List<ToolError> warnings)
    {
        // 构建补丁内容映射：路径 → 新内容
        Dictionary<string, string> patchedContentMap = BuildPatchedContentMap(scriptUpdates);

        // 计算受影响的零件闭包
        HashSet<string> affectedDevices = ComputeAffectedDeviceClosure(currentDevicePath, scriptUpdates);

        // 如果只有当前零件，无需级联检查
        if (affectedDevices.Count <= 1)
        {
            return (true, Array.Empty<ToolError>());
        }

        // 对受影响的零件执行级联诊断
        return ValidateAffectedDevicesCascade(affectedDevices, patchedContentMap, warnings);
    }

    /// <summary>
    /// 从脚本更新列表构建补丁内容映射。
    /// </summary>
    private static Dictionary<string, string> BuildPatchedContentMap(IReadOnlyList<DeviceTsView.ScriptUpdate> scriptUpdates)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (DeviceTsView.ScriptUpdate update in scriptUpdates)
        {
            string sourceRelativePath = WorkspacePath.RequireRelativePath(update.SourcePath);
            string scriptText = EntityWritebackDispatcher.BuildScriptText(update);
            map[sourceRelativePath] = scriptText;
        }
        return map;
    }

    /// <summary>
    /// 从脚本更新列表中提取受影响的零件路径闭包。
    /// 对于 CodeRepo 脚本，需要找到所有链接该 GUID 的零件。
    /// </summary>
    private HashSet<string> ComputeAffectedDeviceClosure(
        string currentDevicePath,
        IReadOnlyList<DeviceTsView.ScriptUpdate> scriptUpdates)
    {
        HashSet<string> closure = new(StringComparer.Ordinal) { currentDevicePath };

        foreach (DeviceTsView.ScriptUpdate update in scriptUpdates)
        {
            // 只有 CodeRepo 脚本才需要级联检查
            if (!update.IsCodeRepo)
            {
                continue;
            }

            // 查找链接到该脚本的所有零件（从反向索引）
            IReadOnlyList<string> linkedDevices = Array.Empty<string>();
            if (_logic.LinkReverseIndex.ContainsKey(update.Guid))
                linkedDevices = _logic.LinkReverseIndex[update.Guid].ToArray();
            foreach (string devicePath in linkedDevices)
            {
                closure.Add(devicePath);
            }
        }

        return closure;
    }

    /// <summary>
    /// 对受影响零件闭包执行级联诊断检查。
    /// 对每个零件：生成 baseline/patched TS 视图，解析并执行 Lint 规则，计算错误 diff。
    /// </summary>
    private (bool Ok, IReadOnlyList<ToolError> NewErrors) ValidateAffectedDevicesCascade(
        HashSet<string> affectedDevices,
        Dictionary<string, string> patchedContentMap,
        List<ToolError> warnings)
    {
        List<ToolError> allNewErrors = [];
        ScriptAnalysisService baselineAnalysisService = new(_model);
        ScriptAnalysisService patchedAnalysisService = new(_model, patchedContentMap);

        foreach (string devicePath in affectedDevices)
        {
            (string deviceRelativePath, string? resolveError) = WorkspacePath.TryRequireRelativePath(devicePath);
            if (resolveError != null)
            {
                return (false, new[] { new ToolError(devicePath, 0, null, resolveError, ToolDiagnosticSeverity.Fatal) });
            }

            if (_model.Find<DeviceEntityBase>(deviceRelativePath) is not DeviceEntityBase device)
            {
                return (false, new[] { new ToolError(devicePath, 0, null, $"零件实体未加载: {deviceRelativePath}", ToolDiagnosticSeverity.Fatal) });
            }

            // 生成 baseline TS 视图（当前磁盘状态）
            DeviceTsViewBuildResult baselineBuild = DeviceTsView.BuildDeviceTsView(_model, deviceRelativePath, warnings, cache: _logic.ScriptAstCache);
            string baselineTsView = baselineBuild.TsView;

            // 生成 patched TS 视图（模拟补丁后状态）
            DeviceTsViewBuildResult patchedBuild = DeviceTsView.BuildDeviceTsView(_model, deviceRelativePath, warnings, patchedContentMap, cache: _logic.ScriptAstCache);
            string patchedTsView = patchedBuild.TsView;

            List<string> baselineLines = TextLines.SplitLines(baselineTsView).ToList();
            List<string> patchedLines = TextLines.SplitLines(patchedTsView).ToList();

            // 计算行号映射
            Dictionary<int, int> lineMapping = BuildLineMapping(baselineLines, patchedLines);

            // 收集 baseline 错误（解析 + Lint）
            List<ToolError> baselineErrors = [];
            _ = DeviceTsView.ParseDeviceTsViewForWriteback(
                _model, baselineLines, deviceRelativePath, warnings,
                collectedErrors: baselineErrors);
            CollectLintErrorsFromDevice(device, baselineAnalysisService, baselineErrors, _model);

            // 收集 patched 错误（解析 + Lint）
            List<ToolError> patchedErrors = [];
            DeviceTsView.WritebackParseResult patchedResult = DeviceTsView.ParseDeviceTsViewForWriteback(
                _model, patchedLines, deviceRelativePath, warnings,
                collectedErrors: patchedErrors);
            CollectLintErrorsFromDevice(device, patchedAnalysisService, patchedErrors, _model);

            // Fatal 错误直接阻断
            if (patchedResult.Error != null)
            {
                return (false, new[] { patchedResult.Error });
            }

            List<ToolError> fatalErrors = patchedErrors.Where(e => e.Severity == ToolDiagnosticSeverity.Fatal).ToList();
            if (fatalErrors.Count > 0)
            {
                return (false, fatalErrors);
            }

            // 计算新增错误
            List<ToolError> newErrors = ComputeNewErrors(baselineErrors, patchedErrors, lineMapping);
            allNewErrors.AddRange(newErrors);
        }

        // 全局调用关系检查（Act 别名/动态调用等）的 baseline vs patched diff
        List<ToolError> baselineGlobalErrors = CollectGlobalCallTargetErrors(baselineAnalysisService, _model);
        List<ToolError> patchedGlobalErrors = CollectGlobalCallTargetErrors(patchedAnalysisService, _model);
        allNewErrors.AddRange(ComputeNewErrors(baselineGlobalErrors, patchedGlobalErrors, []));

        return (allNewErrors.Count == 0, allNewErrors);
    }

    /// <summary>
    /// 从零件收集完整 Lint 错误（Error/Fatal）。
    /// </summary>
    private static void CollectLintErrorsFromDevice(
        DeviceEntityBase device,
        ScriptAnalysisService analysisService,
        List<ToolError> errors,
        ProjectModel model)
    {
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.FromDevice(device, analysisService, model);
        foreach (LintDiagnostic diag in diagnostics)
        {
            ToolDiagnosticSeverity? severity = diag.Severity switch
            {
                LintSeverity.Error => ToolDiagnosticSeverity.Error,
                LintSeverity.Fatal => ToolDiagnosticSeverity.Fatal,
                _ => null
            };

            if (!severity.HasValue)
            {
                continue;
            }

            errors.Add(new ToolError(
                diag.Location.Path,
                diag.Location.Line,
                null,
                diag.Message,
                severity.Value));
        }
    }

    /// <summary>
    /// 收集全局调用关系检查的 Error/Fatal 诊断。
    /// </summary>
    private static List<ToolError> CollectGlobalCallTargetErrors(ScriptAnalysisService analysisService, ProjectModel model)
    {
        List<ToolError> errors = [];
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateActAndDynamicCalls(model, analysisService);
        foreach (LintDiagnostic diag in diagnostics)
        {
            ToolDiagnosticSeverity? severity = diag.Severity switch
            {
                LintSeverity.Error => ToolDiagnosticSeverity.Error,
                LintSeverity.Fatal => ToolDiagnosticSeverity.Fatal,
                _ => null
            };

            if (!severity.HasValue)
            {
                continue;
            }

            errors.Add(new ToolError(
                diag.Location.Path,
                diag.Location.Line,
                null,
                diag.Message,
                severity.Value));
        }

        return errors;
    }

    /// <summary>
    /// 计算 patched 相对于 baseline 的新增错误。
    /// 使用行号映射将 baseline 错误映射到 patched 行号，然后找出新增的错误。
    /// </summary>
    private static List<ToolError> ComputeNewErrors(
        List<ToolError> baselineErrors,
        List<ToolError> patchedErrors,
        Dictionary<int, int> lineMapping)
    {
        // 构建已存在的错误签名集合（基于映射后的行号）
        HashSet<string> existingSignatures = new(StringComparer.Ordinal);
        foreach (ToolError error in baselineErrors)
        {
            int? mappedLine = error.Line;
            if (error.Line.HasValue && lineMapping.TryGetValue(error.Line.Value, out int mapped))
                mappedLine = mapped;
            string signature = $"{error.Path}|{mappedLine}|{error.Message}";
            existingSignatures.Add(signature);
        }

        // 找出新增的错误
        List<ToolError> newErrors = [];
        foreach (ToolError error in patchedErrors)
        {
            string signature = $"{error.Path}|{error.Line}|{error.Message}";
            if (!existingSignatures.Contains(signature))
            {
                newErrors.Add(error);
            }
        }

        return newErrors;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════════════════════════════════════════════

    private static (DeviceTsViewPatchData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) FailPatch(string message)
    {
        return (null, [new ToolError(null, null, null, message)], Array.Empty<ToolError>());
    }
}
