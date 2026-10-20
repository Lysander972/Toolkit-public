using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

public sealed partial class DeviceTsView
{
    private static readonly string AllowedGlobalVarTypesText = BescriptVarTypeHelper.BuildAllowedTypesText(BescriptVarTypeHelper.IsSupportedGlobalVarType);

    internal sealed record WritebackParseResult(
        List<ScriptUpdate> Updates,
        Dictionary<string, List<DesiredScriptListEntry>> DesiredScriptLists,
        List<DesiredGlobalVar> DesiredGlobalVars,
        Dictionary<string, List<int>> ThisMemberUsages,
        ToolError? Error);

    internal static WritebackParseResult ParseDeviceTsViewForWriteback(
        ProjectModel model,
        IReadOnlyList<string> lines,
        string deviceRelativePath,
        List<ToolError> warnings,
        List<ToolError>? collectedErrors = null)
    {
        List<ScriptUpdate> updates = [];
        Dictionary<string, List<DesiredScriptListEntry>> desiredScriptLists = new(StringComparer.Ordinal);
        List<DesiredGlobalVar> desiredGlobalVars = [];
        Dictionary<string, List<int>> thisMemberUsages = new(StringComparer.Ordinal);

        WritebackParseResult fail(ToolError err) => new(updates, desiredScriptLists, desiredGlobalVars, thisMemberUsages, err);

        string deviceTsViewPath = RelativePathUtils.Combine(ScriptPathHelper.ViewsPath, deviceRelativePath + ".ts");

        if (model.Find<DeviceEntity>(deviceRelativePath) is not DeviceEntity deviceEntity)
            return fail(new ToolError(deviceRelativePath, null, null, "零件实体未找到", ToolDiagnosticSeverity.Error));

        DeviceMetadata deviceMeta = BuildDeviceMetadataFromEntity(deviceEntity);
        int deviceTypeId = deviceMeta.Type;
        (Dictionary<string, ScriptFileInfo> codeRepoGuidMap, HashSet<string> _) = GetCodeRepoGuidMap(model, null, warnings);

        DeviceConversionContext ctx = DeviceConversionContext.Build(model, deviceRelativePath, deviceMeta, codeRepoGuidMap, warnings);

        bool isBirthBuilding = deviceTypeId == DeviceTypeIds.BirthBuilding;
        string deviceClassName = BuildDeviceTsViewClassName(deviceEntity);
        string targetClassName = isBirthBuilding ? deviceClassName + VirtualCoreSuffix : deviceClassName;
        string? entryClassName = isBirthBuilding ? deviceClassName : null;

        TsViewAstScanner.ScanClassResult scan = TsViewAstScanner.ScanClass(lines, targetClassName, entryClassName, deviceTsViewPath);
        if (scan.Error != null)
            return fail(scan.Error);

        IReadOnlyList<AllowedEventInfo> allowedEvents = BuildAllowedEventInfos(deviceTypeId, warnings, deviceRelativePath);
        Dictionary<string, AllowedEventInfo> allowedEventMap = allowedEvents.ToDictionary(x => x.EventName, x => x, StringComparer.Ordinal);
        HashSet<string> allowedEventNames = new(allowedEvents.Select(x => x.EventName), StringComparer.Ordinal);

        if (allowedEventNames.Count > 0)
        {
            if (!scan.HasEventDecorator)
                return fail(new ToolError(deviceTsViewPath, null, null, "缺少 @events 声明"));
            if (!new HashSet<string>(scan.EventDecoratorValues, StringComparer.Ordinal).SetEquals(allowedEventNames))
                return fail(new ToolError(deviceTsViewPath, null, null, "事件列表与允许事件不一致"));
        }

        Dictionary<string, BescriptVarType> globalVarTypes = new(StringComparer.Ordinal);
        foreach (ScannedGlobalVar g in scan.Globals)
        {
            desiredGlobalVars.Add(new DesiredGlobalVar(g.RawName, g.Type, g.Comment));
            globalVarTypes[g.Name] = g.Type;
        }

        Dictionary<string, (string RawName, int DeclLine)> methodInfoMap = new(StringComparer.Ordinal);
        foreach (ScannedMethod method in scan.Methods)
        {
            if (!methodInfoMap.TryAdd(method.Name, (method.RawName, method.DeclLine)))
            {
                int existingLine = methodInfoMap[method.Name].DeclLine;
                return fail(new ToolError(deviceTsViewPath, method.DeclLine, null,
                    $"方法名重复: {method.Name}（已存在于第 {existingLine} 行）"));
            }
        }
        Dictionary<string, string> methodRawNameMap = methodInfoMap.ToDictionary(
            kvp => kvp.Key, kvp => kvp.Value.RawName, StringComparer.Ordinal);
        ActTargetMap original = ctx.ActTargetMap;
        ActTargetMap actTargetMap;
        if (methodRawNameMap.Count == 0)
        {
            actTargetMap = original;
        }
        else
        {
            ActTargetInfo self = original.Self;
            Dictionary<string, string> rawToMethod = new(self.ScriptRawNameToMethodName, StringComparer.Ordinal);
            Dictionary<string, string> methodToRaw = new(self.ScriptMethodNameToRawName, StringComparer.Ordinal);
            foreach ((string mn, string rn) in methodRawNameMap)
            {
                if (!methodToRaw.ContainsKey(mn)) methodToRaw[mn] = rn;
                if (!rawToMethod.ContainsKey(rn)) rawToMethod[rn] = mn;
            }
            ActTargetInfo mergedSelf = self with { ScriptRawNameToMethodName = rawToMethod, ScriptMethodNameToRawName = methodToRaw };
            Dictionary<int, ActTargetInfo> byId = new(original.ById);
            Dictionary<string, ActTargetInfo> byName = new(original.ByName, StringComparer.Ordinal);
            Dictionary<string, ActTargetInfo> byClassName = new(original.ByClassName);
            byId[mergedSelf.DeviceId] = mergedSelf;
            if (mergedSelf.LogicName is string logicName && logicName.Length > 0) byName[logicName] = mergedSelf;
            if (mergedSelf.TsTypeName.Length > 0) byClassName[mergedSelf.TsTypeName] = mergedSelf;
            actTargetMap = new ActTargetMap(mergedSelf, byName, byId, byClassName);
        }

        List<ParsedMethod> methods = [];
        HashSet<string> usedSourcePaths = new(StringComparer.Ordinal);
        HashSet<string> seenEvents = new(StringComparer.Ordinal);
        HashSet<string> eventMethodNames = new(StringComparer.Ordinal);
        List<EventMethodBody> eventMethodBodies = [];

        Dictionary<string, string> eventGroupMemberMap = new(StringComparer.Ordinal);
        foreach (ScannedEventGroup eg in scan.EventGroups)
        {
            foreach (ScannedMethod member in eg.Members)
            {
                eventGroupMemberMap[member.Name] = eg.EventName;
            }
        }

        HashSet<string> scannedEventNames = new(StringComparer.Ordinal);
        foreach (ScannedEventGroup eg in scan.EventGroups)
        {
            string eventName = eg.EventName;
            if (isBirthBuilding && BirthBuildingEventMap.ToStorageName.TryGetValue(eg.EventName, out string? orig))
                eventName = orig;
            scannedEventNames.Add(eventName);
        }
        if (!scannedEventNames.SetEquals(allowedEventNames))
        {
            string extra = string.Join(", ", scannedEventNames.Except(allowedEventNames, StringComparer.Ordinal));
            if (extra.Length > 0)
                return fail(new ToolError(deviceRelativePath, null, null, $"事件注册存在未允许的事件: {extra}；不要添加额外事件组。", ToolDiagnosticSeverity.Fatal));

            string missing = string.Join(", ", allowedEventNames.Except(scannedEventNames, StringComparer.Ordinal));
            if (missing.Length > 0)
                return fail(new ToolError(deviceRelativePath, null, null, $"缺少事件注册: {missing}；不要损坏事件组结构。", ToolDiagnosticSeverity.Fatal));
        }

        foreach (ScannedEventGroup eg in scan.EventGroups)
        {
            string originalEventName = eg.EventName;
            if (isBirthBuilding && BirthBuildingEventMap.ToStorageName.TryGetValue(eg.EventName, out string? orig))
                originalEventName = orig;

            seenEvents.Add(originalEventName);
            AllowedEventInfo info = allowedEventMap[originalEventName];
            IReadOnlyList<EventParamSpec> eventParams = BuildEventParamSpecs(info.Signature);

            foreach (ScannedMethod m in eg.Members)
            {
                eventMethodNames.Add(m.Name);

                ToolError? sigError = ValidateEventMethodSignature(m, originalEventName, eventParams, deviceTsViewPath);
                if (sigError != null)
                    return fail(sigError);

                // TS 生成的事件脚本注入 this 参数用于类型声明，校验后统一剥离，下游不再重复处理
                IReadOnlyList<TsViewParamSpec> cleanParams = m.Params.Where(p => p.Name != "this").ToArray();

                ScriptConversionContext scriptCtx = BuildScriptConversionContext(
                    model,
                    globalVarTypes,
                    methodRawNameMap,
                    actTargetMap,
                    true,
                    eventParams,
                    cleanParams,
                    m.RetVarFromDecorator,
                    m.RetVarTypeFromDecorator);

                string scriptName = m.RawName;
                ToolError? scriptNameError = ValidateRawBesIdentifierForWriteback(scriptName, "BeScript.name", deviceTsViewPath, m.DocStartLine);
                if (scriptNameError != null)
                    return fail(scriptNameError);

                (string listPath, ToolError? listError) = ResolveListJsonPath(deviceRelativePath, deviceTsViewPath, true, originalEventName, allowedEventNames, m.DocStartLine);
                if (listError != null) return fail(listError);

                if (GuidText.Validate(m.CodeRepoGuid))
                {
                    if (!codeRepoGuidMap.TryGetValue(m.CodeRepoGuid, out ScriptFileInfo? repoInfo))
                        return fail(new ToolError(deviceTsViewPath, m.DocStartLine, null, $"未匹配到代码仓库脚本: GUID={m.CodeRepoGuid}"));

                    string guidText = m.CodeRepoGuid;
                    ToolError? repoError = BuildScriptUpdates(model, deviceTsViewPath, m.BodyLines, scriptCtx, true, eventMethodBodies, eventParams, eg.EventName, repoInfo.RelativePath, scriptName, new ParsedMethodSignature(m.Name, !m.IsPrivate && !m.IsProtected, m.IsPrivate, m.IsProtected, false, cleanParams), m.RetVarFromDecorator, m.RetVarTypeFromDecorator, m.BeScriptInfo, guidText, eventMethodNames, updates, warnings, collectedErrors, thisMemberUsages);
                    if (repoError != null)
                    {
                        ToolError? fatal = ClassifyBuildError(repoError, collectedErrors);
                        if (fatal != null) return fail(fatal);
                        continue;
                    }

                    methods.Add(new ParsedMethod(m.DocStartLine, listPath, scriptName, null, guidText, true, m.IsProtected));
                }
                else
                {
                    string sourcePath = RelativePathUtils.Combine(RelativePathUtils.Combine(deviceRelativePath, ProjectEntityLayout.EventDirPrefix + originalEventName), scriptName + ProjectEntityLayout.CodeExt);

                    if (!usedSourcePaths.Add(sourcePath))
                        return fail(new ToolError(deviceTsViewPath, m.DocStartLine, null, $"sourcePath 重复: {sourcePath}"));

                    ToolError? scriptBuildError = BuildScriptUpdates(model, deviceTsViewPath, m.BodyLines, scriptCtx, true, eventMethodBodies, eventParams, eg.EventName, sourcePath, scriptName, new ParsedMethodSignature(m.Name, !m.IsPrivate && !m.IsProtected, m.IsPrivate, m.IsProtected, false, cleanParams), m.RetVarFromDecorator, m.RetVarTypeFromDecorator, m.BeScriptInfo, string.Empty, eventMethodNames, updates, warnings, collectedErrors, thisMemberUsages);
                    if (scriptBuildError != null)
                    {
                        ToolError? fatal = ClassifyBuildError(scriptBuildError, collectedErrors);
                        if (fatal != null) return fail(fatal);
                        continue;
                    }

                    methods.Add(new ParsedMethod(m.DocStartLine, listPath, scriptName, sourcePath, string.Empty, false, m.IsProtected));
                }
            }
        }

        foreach (ScannedMethod m in scan.Methods)
        {
            ScriptConversionContext scriptCtx = BuildScriptConversionContext(
                model,
                globalVarTypes,
                methodRawNameMap,
                actTargetMap,
                false,
                Array.Empty<EventParamSpec>(),
                m.Params,
                m.RetVarFromDecorator,
                m.RetVarTypeFromDecorator);

            string scriptName = m.RawName;
            ToolError? scriptNameError = ValidateRawBesIdentifierForWriteback(scriptName, "BeScript.name", deviceTsViewPath, m.DocStartLine);
            if (scriptNameError != null)
                return fail(scriptNameError);

            (string listPath, ToolError? listError) = ResolveListJsonPath(deviceRelativePath, deviceTsViewPath, false, null, allowedEventNames, m.DocStartLine);
            if (listError != null) return fail(listError);

            if (GuidText.Validate(m.CodeRepoGuid))
            {
                if (!codeRepoGuidMap.TryGetValue(m.CodeRepoGuid, out ScriptFileInfo? repoInfo))
                    return fail(new ToolError(deviceTsViewPath, m.DocStartLine, null, $"未匹配到代码仓库脚本: @GUID_{m.CodeRepoGuid}()"));

                string guidText = m.CodeRepoGuid;

                ToolError? repoError = BuildScriptUpdates(model, deviceTsViewPath, m.BodyLines, scriptCtx, false, eventMethodBodies, Array.Empty<EventParamSpec>(), null, repoInfo.RelativePath, scriptName, new ParsedMethodSignature(m.Name, !m.IsPrivate && !m.IsProtected, m.IsPrivate, m.IsProtected, false, m.Params), m.RetVarFromDecorator, m.RetVarTypeFromDecorator, m.BeScriptInfo, guidText, eventMethodNames, updates, warnings, collectedErrors, thisMemberUsages);
                if (repoError != null)
                {
                    ToolError? fatal = ClassifyBuildError(repoError, collectedErrors);
                    if (fatal != null) return fail(fatal);
                    continue;
                }

                methods.Add(new ParsedMethod(m.DocStartLine, listPath, scriptName, null, guidText, true, m.IsProtected));
            }
            else
            {
                string sourcePath = RelativePathUtils.Combine(RelativePathUtils.Combine(deviceRelativePath, ProjectEntityLayout.ScriptsDirName), scriptName + ProjectEntityLayout.CodeExt);

                if (!usedSourcePaths.Add(sourcePath))
                    return fail(new ToolError(deviceTsViewPath, m.DocStartLine, null, $"sourcePath 重复: {sourcePath}"));

                ToolError? scriptBuildError = BuildScriptUpdates(model, deviceTsViewPath, m.BodyLines, scriptCtx, false, eventMethodBodies, Array.Empty<EventParamSpec>(), null, sourcePath, scriptName, new ParsedMethodSignature(m.Name, !m.IsPrivate && !m.IsProtected, m.IsPrivate, m.IsProtected, false, m.Params), m.RetVarFromDecorator, m.RetVarTypeFromDecorator, m.BeScriptInfo, string.Empty, eventMethodNames, updates, warnings, collectedErrors, thisMemberUsages);
                if (scriptBuildError != null)
                {
                    ToolError? fatal = ClassifyBuildError(scriptBuildError, collectedErrors);
                    if (fatal != null) return fail(fatal);
                    continue;
                }

                methods.Add(new ParsedMethod(m.DocStartLine, listPath, scriptName, sourcePath, string.Empty, false, m.IsProtected));
            }
        }

        HashSet<(string ListPath, string ScriptName)> used = new(ScriptListKeyComparer.Instance);
        foreach (ParsedMethod method in methods)
        {
            (string ListPath, string ScriptName) key = (method.ListPath, method.ScriptName);
            if (!used.Add(key))
                return fail(new ToolError(deviceTsViewPath, method.DocStartLine, null, $"脚本名重复: {method.ScriptName}"));
        }

        foreach (ParsedMethod method in methods)
        {
            if (!desiredScriptLists.TryGetValue(method.ListPath, out List<DesiredScriptListEntry>? list))
            {
                list = [];
                desiredScriptLists[method.ListPath] = list;
            }

            list.Add(new DesiredScriptListEntry(method.ScriptName, method.Guid, method.IsCodeRepo, method.IsCodeRepo && method.IsPrivate));
        }

        return new WritebackParseResult(updates, desiredScriptLists, desiredGlobalVars, thisMemberUsages, null);
    }

    private sealed class ScriptListKeyComparer : IEqualityComparer<(string ListPath, string ScriptName)>
    {
        internal static readonly ScriptListKeyComparer Instance = new();
        public bool Equals((string ListPath, string ScriptName) x, (string ListPath, string ScriptName) y) => StringComparer.Ordinal.Equals(x.ListPath, y.ListPath) && StringComparer.Ordinal.Equals(x.ScriptName, y.ScriptName);
        public int GetHashCode((string ListPath, string ScriptName) obj) => HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.ListPath), StringComparer.Ordinal.GetHashCode(obj.ScriptName));
    }

    /// <summary>
    /// 验证事件方法签名是否与事件签名一致。
    /// 虽然这东西不会存盘进code文件,但不一致足以说明模型缺乏上下文而产生了足以严重破坏代码质量的幻觉,此时必须拒绝和干预要求读SDK而获取上下文
    /// </summary>
    private static ToolError? ValidateEventMethodSignature(
        ScannedMethod method,
        string eventName,
        IReadOnlyList<EventParamSpec> expectedParams,
        string tsViewPath)
    {
        // TS 生成的事件脚本注入 this 参数，校验时剥离(但AI新建的可能缺失但这不影响正常工作)
        IReadOnlyList<TsViewParamSpec> actualParams = method.Params.Where(p => p.Name != "this").ToArray();

        if (actualParams.Count != expectedParams.Count)
        {
            return new ToolError(
                tsViewPath,
                method.DeclLine,
                null,
                $"事件方法签名不匹配: {eventName}。参数数量不一致: 期望 {expectedParams.Count} 个，实际 {actualParams.Count} 个。",
                ToolDiagnosticSeverity.Fatal);
        }

        for (int i = 0; i < expectedParams.Count; i++)
        {
            EventParamSpec expectedParam = expectedParams[i];
            TsViewParamSpec actualParam = actualParams[i];
            string expectedTsType = expectedParam.BeTypeName;

            if (!string.Equals(actualParam.TypeName, expectedTsType, StringComparison.Ordinal))
            {
                return new ToolError(
                    tsViewPath,
                    method.DeclLine,
                    null,
                    $"事件方法签名不匹配: {eventName}。参数 {i + 1} 类型不一致: 期望 {expectedTsType}，实际 {actualParam.TypeName}。",
                    ToolDiagnosticSeverity.Fatal);
            }
        }

        return null;
    }

    internal static BescriptVarType? ParseGlobalVarType(string typeToken)
    {
        // 类型标记来自外部 TS 视图解析，前后空白均需清除
#pragma warning disable TK010
        string raw = typeToken.Trim();
#pragma warning restore TK010
        if (raw.StartsWith("Be", StringComparison.Ordinal)) raw = raw[2..];
        if (BescriptVarTypeHelper.TryParse(raw) is not BescriptVarType parsed) return null;
        if (!BescriptVarTypeHelper.IsSupportedGlobalVarType(parsed)) return null;
        return parsed;
    }

    private static ToolError? BuildScriptUpdates(ProjectModel model, string tsViewPath, IReadOnlyList<(int LineNumber, string LineText)> bodyLines, ScriptConversionContext ctx, bool inEvent, List<EventMethodBody> eventMethodBodies, IReadOnlyList<EventParamSpec> expectedEventParams, string? currentEventName, string targetPath, string scriptName, ParsedMethodSignature methodSig, string? expectedRetVar, string? expectedRetVarType, BeScriptDecoratorInfo? beScriptInfo, string guid, IReadOnlySet<string> eventMethodNames, List<ScriptUpdate> updates, List<ToolError> warnings, List<ToolError>? collectedErrors, Dictionary<string, List<int>> thisMemberUsages)
    {
        int? firstBodyLine = null;
        if (bodyLines.Count > 0)
            firstBodyLine = bodyLines[0].LineNumber;

        string? rawParams = beScriptInfo?.RawParams;
        string? rawRetVar = beScriptInfo?.RawRetVar;

        if (!inEvent && (rawParams != null || rawRetVar != null))
            return new ToolError(tsViewPath, firstBodyLine, null, "自定义脚本禁止使用 rawParams/rawRetVar，签名覆写机制仅限事件脚本。") { Source = new SourceLocation { Path = targetPath } };

        if (methodSig.IsStatic) return new ToolError(tsViewPath, firstBodyLine, null, "方法不允许 static") { Source = new SourceLocation { Path = targetPath } };
        if (!methodSig.IsPublic && !methodSig.IsPrivate && !methodSig.IsProtected) return new ToolError(tsViewPath, firstBodyLine, null, "方法必须声明 public、protected 或 private") { Source = new SourceLocation { Path = targetPath } };
        if (!TsViewNaming.IsValidIdentifier(methodSig.Name)) return new ToolError(tsViewPath, firstBodyLine, null, "脚本的签名和所属区段不一致.注意是否放对了区段标记的位置") { Source = new SourceLocation { Path = targetPath } };
        if (inEvent)
        {
            if (!methodSig.IsPrivate || methodSig.IsPublic || methodSig.IsProtected) return new ToolError(tsViewPath, firstBodyLine, null, "事件脚本必须为 private") { Source = new SourceLocation { Path = targetPath } };
        }

        if (!inEvent)
        {
            foreach (TsViewParamSpec param in methodSig.Params)
            {
                (_, ToolError? paramNameError) = ValidateTsVariableIdentifierForWriteback(param.Name, $"方法参数名 {param.Name}", tsViewPath, firstBodyLine);
                if (paramNameError != null)
                    return paramNameError;
            }
        }

        string legacyThisOwnerType = LoadLegacyThisOwnerType(model, targetPath);

        TsViewBodyParser.TsBodyParseResult parseResult;
        try
        {
            parseResult = TsViewBodyParser.ParseTsBodyToAst(bodyLines, ctx, expectedRetVar, warnings, tsViewPath, collectedErrors);
            MergeThisMemberUsages(thisMemberUsages, parseResult.ThisMemberUsages);
            if (!parseResult.IsSuccess)
            {
                if (parseResult.DiagnosticTag is TsViewDiagnosticTag.SubjectTag subjectTag)
                {
                    return new ToolError(tsViewPath, parseResult.ErrorLine, null, parseResult.Error, ToolDiagnosticSeverity.Fatal)
                    {
                        Source = new SourceLocation { Path = targetPath },
                        Code = subjectTag.Code,
                        Subject = subjectTag.Name
                    };
                }

                return new ToolError(tsViewPath, parseResult.ErrorLine, null, parseResult.Error, ToolDiagnosticSeverity.Fatal)
                {
                    Source = new SourceLocation { Path = targetPath }
                };
            }
        }
        catch (Exception ex)
        {
            return new ToolError(tsViewPath, null, null, $"解析异常: {ex.Message}", ToolDiagnosticSeverity.Fatal) { Source = new SourceLocation { Path = targetPath } };
        }

        BescriptAst bodyAst = parseResult.Ast;
        List<MethodCallInfo> methodCalls = parseResult.MethodCalls;
        IReadOnlyList<InlineCanvasLiteral> inlineCanvases = parseResult.InlineCanvases;
        IReadOnlyList<(int LineNumber, string LineText)> processedBodyLines = parseResult.NormalizedLines;

        if (legacyThisOwnerType.Length != 0)
        {
            BescriptAstNode RewriteNode(BescriptAstNode node)
            {
                List<BescriptAstNode> rewrittenChildren = node.Children.Select(RewriteNode).ToList();

                if (node is IHasCall hasCallNode)
                {
                    BescriptCallSignature rewrittenCall = hasCallNode.Call;
                    if (rewrittenCall is ActCallSignature act &&
                        act.OwnerName == "this" &&
                        !act.OwnerNameIsAlias &&
                        !string.Equals(act.OwnerType, legacyThisOwnerType, StringComparison.Ordinal))
                    {
                        rewrittenCall = act with
                        {
                            OwnerType = legacyThisOwnerType,
                            SaveName = $"{legacyThisOwnerType}.this.{act.FunctionName}"
                        };
                    }
                    return node.WithCallAndChildren(rewrittenCall, rewrittenChildren);
                }

                return node.WithChildren(rewrittenChildren);
            }

            bodyAst = bodyAst with { RootNodes = bodyAst.RootNodes.Select(RewriteNode).ToList() };
        }

        DetectPrivateMethodPenetration(methodCalls, ctx, tsViewPath, targetPath, warnings);
        if (collectedErrors != null)
        {
            IReadOnlyList<LintDiagnostic> conditionDiagnostics = LintEngine.ValidateControlConditionConstMustBeVar(bodyAst, targetPath);
            foreach (LintDiagnostic diag in conditionDiagnostics)
            {
                ToolDiagnosticSeverity severity = diag.Severity switch
                {
                    LintSeverity.Error => ToolDiagnosticSeverity.Error,
                    LintSeverity.Fatal => ToolDiagnosticSeverity.Fatal,
                    LintSeverity.Warning => ToolDiagnosticSeverity.Warning,
                    _ => ToolDiagnosticSeverity.Warning
                };
                collectedErrors.Add(new ToolError(tsViewPath, diag.Location.Line, null, diag.Message, severity)
                {
                    Source = new SourceLocation { Path = targetPath }
                });
            }

            TsViewValidationRules.CollectNonCompliantMarkersFromAst(bodyAst, parseResult.AsAssertionLines, tsViewPath, collectedErrors);

            foreach (InlineCanvasLiteral inlineCanvas in inlineCanvases)
            {
                foreach (string message in CanvasViewValidation.CollectHtmlLintErrors(inlineCanvas.RawCanvas, targetPath))
                {
                    collectedErrors.Add(new ToolError(
                        tsViewPath,
                        inlineCanvas.LineNumber,
                        null,
                        $"内联 Canvas Lint 失败: {message}",
                        ToolDiagnosticSeverity.Error)
                    {
                        Source = new SourceLocation { Path = targetPath }
                    });
                }
            }
        }

        if (inEvent)
        {
            foreach (MethodCallInfo call in methodCalls)
            {
                if (call.Kind == MethodCallKind.Act && call.IsSelfTarget && eventMethodNames.Contains(call.MethodName))
                {
                    return new ToolError(tsViewPath, call.Line, null, $"事件脚本禁止调用其他事件脚本: {call.MethodName}。事件方法仅限 private，禁止跨事件直接调用。")
                    {
                        Source = new SourceLocation { Path = targetPath }
                    };
                }
            }
            eventMethodBodies.Add(new EventMethodBody(methodCalls));
        }

        IReadOnlyList<TsViewParamSpec> paramsForWrite;
        string returnTypeForWrite;
        string? returnVarForWrite;
        if (inEvent)
        {
            paramsForWrite = Array.Empty<TsViewParamSpec>();
            if (!string.IsNullOrWhiteSpace(rawParams))
            {
                (IReadOnlyList<TsViewParamSpec> parsedParams, string? ppErr) = TsViewParameterListParser.Parse(rawParams);
                if (ppErr == null)
                {
                    foreach (TsViewParamSpec param in parsedParams)
                    {
                        if (TsViewNaming.TryDetectEscapedIdentifierLiteral(param.Name) is { } decodedRawName)
                        {
                            return new ToolError(
                                tsViewPath,
                                firstBodyLine,
                                null,
                                $"rawParams 参数名禁止使用转义后的标识符文本: {param.Name}。请使用原始名称: {decodedRawName}。{TsViewNaming.EscapedNameAbuseHint}",
                                ToolDiagnosticSeverity.Fatal)
                            {
                                Source = new SourceLocation { Path = targetPath }
                            };
                        }
                    }
                    paramsForWrite = parsedParams;
                }
            }
            returnTypeForWrite = "void";
            returnVarForWrite = null;
            if (!string.IsNullOrWhiteSpace(rawRetVar))
            {
                (IReadOnlyList<TsViewParamSpec> parsedRet, string? retErr) = TsViewParameterListParser.Parse(rawRetVar);
                if (retErr == null && parsedRet.Count == 1)
                {
                    if (TsViewNaming.TryDetectEscapedIdentifierLiteral(parsedRet[0].Name) is { } decodedRawRetVarName)
                    {
                        return new ToolError(
                            tsViewPath,
                            firstBodyLine,
                            null,
                            $"rawRetVar 参数名禁止使用转义后的标识符文本: {parsedRet[0].Name}。请使用原始名称: {decodedRawRetVarName}。{TsViewNaming.EscapedNameAbuseHint}",
                            ToolDiagnosticSeverity.Fatal)
                        {
                            Source = new SourceLocation { Path = targetPath }
                        };
                    }

                    returnTypeForWrite = parsedRet[0].TypeName;
                    returnVarForWrite = parsedRet[0].Name;
                }
            }
        }
        else
        {
            paramsForWrite = methodSig.Params;
            if (!string.IsNullOrWhiteSpace(expectedRetVar))
            {
                if (string.IsNullOrWhiteSpace(expectedRetVarType))
                {
                    return new ToolError(tsViewPath, firstBodyLine, null, $"BeScript 声明了 retVar=\"{expectedRetVar}\" 但缺少 retVarType，无法确定返回类型。请重新生成视图。", ToolDiagnosticSeverity.Fatal)
                    {
                        Source = new SourceLocation { Path = targetPath }
                    };
                }
                returnTypeForWrite = expectedRetVarType;
                returnVarForWrite = expectedRetVar;
            }
            else
            {
                returnTypeForWrite = "void";
                returnVarForWrite = null;
            }
        }

        bool privateFromModifier = methodSig.IsProtected;
        updates.Add(new ScriptUpdate(
            targetPath,
            scriptName,
            methodSig.Name,
            privateFromModifier,
            paramsForWrite,
            returnTypeForWrite,
            returnVarForWrite,
            bodyAst,
            beScriptInfo is not null && beScriptInfo.Debug == true,
            beScriptInfo is not null && beScriptInfo.Skip == true,
            beScriptInfo is not null && beScriptInfo.Private == true,
            beScriptInfo?.Comment,
            beScriptInfo?.Color,
            guid,
            GuidText.Validate(guid)
        )
        {
            InlineCanvases = inlineCanvases
        });
        return null;
    }


    private static ScriptConversionContext BuildScriptConversionContext(ProjectModel model, IReadOnlyDictionary<string, BescriptVarType> globalVarTypes, IReadOnlyDictionary<string, string> methodRawNameMap, ActTargetMap actTargetMap, bool inEvent, IReadOnlyList<EventParamSpec> currentEventParams, IReadOnlyList<TsViewParamSpec> methodParams, string? returnVarName, string? returnVarType)
    {
        ScriptConversionContext ctx = new(BuiltinMethodRegistry.Instance, actTargetMap, globalVarTypes, methodRawNameMap: methodRawNameMap)
        {
            Model = model
        };
        if (!string.IsNullOrWhiteSpace(returnVarName))
        {
            ctx.ExpectedReturnVarName = TsViewIdentifierMap.ToTsIdentifier(returnVarName);
            if (!string.IsNullOrWhiteSpace(returnVarType))
            {
                ctx.ExpectedReturnType = returnVarType;
            }
        }

        if (inEvent && currentEventParams.Count > 0)
        {
            foreach (EventParamSpec p in currentEventParams)
            {
                ctx.RegisterParameter(p.TsName, p.BeTypeName);
            }
        }
        else
        {
            foreach (TsViewParamSpec p in methodParams)
            {
                ctx.RegisterParameter(p.Name, p.TypeName);
            }
        }
        return ctx;
    }

    private static void DetectPrivateMethodPenetration(IReadOnlyList<MethodCallInfo> methodCalls, ScriptConversionContext ctx, string tsViewPath, string targetPath, List<ToolError> warnings)
    {
        if (ctx.ActTargetMap.Self.DeviceTypeId != DeviceTypeIds.CoreDevice) return;
        ActTargetInfo? coreInfo = ctx.ActTargetMap.ById.Values.FirstOrDefault(info => info.DeviceTypeId == DeviceTypeIds.CoreDevice && info != ctx.ActTargetMap.Self);
        if (coreInfo is null) return;
        foreach (MethodCallInfo call in methodCalls)
        {
            if (call.Kind != MethodCallKind.Act) continue;
            if (coreInfo.ScriptMethodNameToRawName.TryGetValue(call.MethodName, out string? rawName) && coreInfo.PrivateScriptRawNames.Contains(rawName))
                warnings.Add(new ToolError(tsViewPath, call.Line, null, $"机械产生器不应访问目标核心的私有方法: {rawName}。虽然技术上可行，但违反封装原则，建议改为公有方法。", ToolDiagnosticSeverity.Warning)
                {
                    Source = new SourceLocation { Path = targetPath }
                });
        }
    }

    private static (string Path, ToolError? Error) ResolveListJsonPath(string deviceRelativePath, string tsViewPath, bool inEvent, string? eventName, IReadOnlySet<string> allowedEventNames, int docStartLine)
    {
        if (!inEvent) return (ScriptPathHelper.BuildScriptsListJsonPath(deviceRelativePath), null);
        if (string.IsNullOrWhiteSpace(eventName)) return (string.Empty, new ToolError(tsViewPath, docStartLine, null, "事件脚本缺少事件信息"));
        if (!allowedEventNames.Contains(eventName))
        {
            string supportedList = string.Join(", ", allowedEventNames.OrderBy(x => x, StringComparer.Ordinal));
            return (string.Empty, new ToolError(tsViewPath, docStartLine, null, $"事件类型不支持: {eventName}。支持的事件: {supportedList}"));
        }
        return (ScriptPathHelper.BuildEventListJsonPath(deviceRelativePath, eventName), null);
    }

    /// <summary>
    /// 验证原始 BeScript 标识符是否合规（写回规则）。
    /// 违规场景：Agent 可能误把转义后的标识符文本或 TS 视图前缀写入原始名称。
    /// </summary>
    private static ToolError? ValidateRawBesIdentifierForWriteback(string rawName, string fieldName, string path, int? line)
    {
        if (TsViewNaming.TryDetectEscapedIdentifierLiteral(rawName) is { } decodedRawName)
            return new ToolError(path, line, null, $"{fieldName} 禁止使用转义后的标识符文本: {rawName}。请使用原始名称: {decodedRawName}。{TsViewNaming.EscapedNameAbuseHint}", ToolDiagnosticSeverity.Fatal);
        return null;
    }

    /// <summary>
    /// 验证 TS 变量标识符是否合规（写回规则）。
    /// </summary>
    private static (string RawName, ToolError? Error) ValidateTsVariableIdentifierForWriteback(string tsName, string fieldName, string path, int? line)
    {
        if (!TsViewNaming.IsValidIdentifier(tsName))
            return (string.Empty, new ToolError(path, line, null, $"{fieldName} 不是合法 TS 标识符: {tsName}", ToolDiagnosticSeverity.Fatal));

        string rawName = TsViewIdentifierMap.FromTsIdentifier(tsName);
        string normalizedTsName = TsViewIdentifierMap.ToTsIdentifier(rawName);
        if (!string.Equals(tsName, normalizedTsName, StringComparison.Ordinal))
            return (string.Empty, new ToolError(path, line, null, $"{fieldName} 必须使用唯一映射的变量转义名：当前 {tsName}，应为 {normalizedTsName}。{TsViewNaming.EscapedNameAbuseHint}", ToolDiagnosticSeverity.Fatal));

        return (rawName, null);
    }

    /// <summary>
    /// 从实体模型中读取原有脚本里 act *.this 的 OwnerType。
    /// </summary>
    /// <param name="model">项目模型，包含已加载的实体</param>
    /// <param name="targetPath">脚本的相对路径（如 devices/xxx/scripts/yyy.code）</param>
    private static string LoadLegacyThisOwnerType(ProjectModel model, string targetPath)
    {
        string text;
        if (model.Find<InlineScriptEntity>(targetPath) is InlineScriptEntity inlineScript)
        {
            text = inlineScript.SourceText;
        }
        else if (model.Find<CodeRepoScriptEntity>(targetPath) is CodeRepoScriptEntity repoScript)
        {
            text = repoScript.SourceText;
        }
        else
        {
            // 新增脚本：实体尚不存在，无 legacy OwnerType
            return string.Empty;
        }

        BescriptScriptInfo scriptInfo = BescriptTextParser.Parse(targetPath, text).Script;
        BescriptAst ast = scriptInfo.Ast;

        string firstThisOwner = string.Empty;
        bool Visit(BescriptAstNode n)
        {
            if (n is IHasCall { Call: ActCallSignature act } && act.OwnerName == "this" && !act.OwnerNameIsAlias)
            {
                firstThisOwner = act.OwnerType;
                return true;
            }
            foreach (BescriptAstNode c in n.Children)
            {
                if (Visit(c))
                    return true;
            }
            return false;
        }
        foreach (BescriptAstNode root in ast.RootNodes)
        {
            if (Visit(root))
                break;
        }
        return firstThisOwner;
    }

    private static ToolError? ClassifyBuildError(ToolError buildError, List<ToolError>? collectedErrors)
    {
        if (buildError.Message.StartsWith("用户代码区段不得为空", StringComparison.Ordinal))
            return buildError;
        if (collectedErrors != null)
        {
            collectedErrors.Add(buildError);
            return null;
        }
        return buildError;
    }

    private static void MergeThisMemberUsages(
        Dictionary<string, List<int>> target,
        IReadOnlyDictionary<string, IReadOnlyList<int>> source)
    {
        foreach ((string name, IReadOnlyList<int> lines) in source)
        {
            if (!target.TryGetValue(name, out List<int>? targetLines))
            {
                targetLines = [];
                target[name] = targetLines;
            }

            foreach (int line in lines)
            {
                if (!targetLines.Contains(line))
                    targetLines.Add(line);
            }
        }
    }

}
