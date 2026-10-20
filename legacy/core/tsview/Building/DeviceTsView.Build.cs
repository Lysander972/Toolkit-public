using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

// ┌────────────────────────────────────────────────────────────────────────────┐
// │ 翻译规范 (Translation Convention)                                          │
// │ • Mech → 机械 (禁止译为"机甲")                                              │
// │ • Device → 零件 (禁止译为"设备"，除非指物理设备如手机)                       │
// └────────────────────────────────────────────────────────────────────────────┘

public sealed partial class DeviceTsView
{
    private static readonly JsonSerializerOptions DeviceViewJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// 零件转换上下文：Build/Parse 共用的唯一上下文机制。
    /// </summary>
    /// <remarks>
    /// <para><b>作用域规则（严格隔离，禁止混淆）</b></para>
    /// <list type="bullet">
    ///   <item>地图零件 → 作用域是地图本身（OuterDir）</item>
    ///   <item>机械零件 → 作用域是机械本身（OuterDir）</item>
    ///   <item>机械生成器 (1110) → 内部 Act 目标来源只能是目标机械文件或当前 1110 对应的独立虚拟机械，绝对禁止使用地图零件</item>
    /// </list>
    /// <para><b>1110 的两种视角</b></para>
    /// <list type="bullet">
    ///   <item>【外部视角】地图内其他零件看到的 1110：作用域是地图，只能调用 1110 固有 Act</item>
    ///   <item>【内部视角】1110 内部逻辑：作用域是目标机械，1110 把自己当作目标机械核心</item>
    /// </list>
    /// <para>本类处理的是【内部视角】。</para>
    /// </remarks>
    private sealed record DeviceConversionContext(
        DeviceMetadata Meta,
        string OuterDir,
        MechEntity? BirthBuildingMech,
        string ScopeDir,
        ActTargetMap ActTargetMap)
    {

        /// <summary>
        /// 构建零件转换上下文（唯一入口）。
        /// </summary>
        public static DeviceConversionContext Build(
            ProjectModel model,
            string deviceRelativePath,
            DeviceMetadata meta,
            IReadOnlyDictionary<string, ScriptFileInfo> codeRepoGuidMap,
            List<ToolError> warnings)
        {
            // 从模型获取零件实体
            if (model.Find<DeviceEntity>(deviceRelativePath) is not DeviceEntity deviceEntity)
            {
                throw new InvalidOperationException($"零件实体未找到: {deviceRelativePath}");
            }

            (string _, string _, string outerDir) = ResolveOuterNamespace(deviceEntity);

            // ═══════════════════════════════════════════════════════════════════
            // 1110 内部视角：Act 目标来源只允许目标机械文件或当前 1110 对应的独立虚拟机械
            // 绝对禁止把地图内其他零件混入 1110 内部特殊 Act 调用来源
            // ═══════════════════════════════════════════════════════════════════
            MechEntity? birthBuildingMech = null;
            string scopeDir;
            bool isBirthBuilding = meta.Type == DeviceTypeIds.BirthBuilding;

            if (isBirthBuilding)
            {
                // 内外视角彻底隔离：地图看到的 1110 与内部逻辑不是一个区域，禁止互相借用来源
                string? failReason;
                (birthBuildingMech, failReason) = TryResolveBirthBuildingMech(model, meta);

                // 1110 内部视角的作用域是目标机械文件；没有目标机械文件时使用当前 1110 对应的独立虚拟机械。
                if (birthBuildingMech is not null)
                {
                    scopeDir = birthBuildingMech.派生目录_完整相对路径;
                }
                else
                {
                    scopeDir = BuildVirtualBirthBuildingDir(deviceRelativePath);
                    warnings.Add(new ToolError(deviceRelativePath, null, null,
                        $"机械产生器目标机械解析失败: {failReason}; 已使用独立虚拟机械。", ToolDiagnosticSeverity.Warning));
                }
            }
            else
            {
                // 非 1110 类型：使用 outerDir（地图或机械）
                scopeDir = outerDir;
            }

            ActTargetMap actTargetMap = BuildActTargetMapWithCache(
                model, outerDir, deviceRelativePath, deviceEntity, meta, birthBuildingMech, scopeDir, codeRepoGuidMap, warnings);

            return new DeviceConversionContext(meta, outerDir, birthBuildingMech, scopeDir, actTargetMap);
        }
    }

#pragma warning disable TK006 // 前置条件校验：确保 typeName 存在，失败即停止
    private static string EnsureDeviceTypeName(string? deviceTypeName)
    {
        if (string.IsNullOrWhiteSpace(deviceTypeName))
        {
            throw new InvalidOperationException("零件缺少 type id，无法生成 DeviceType_<typeId> 继承。");
        }

        return deviceTypeName;
    }
#pragma warning restore TK006

    private static string QualifyTsTypeName(string outerNamespace, string tsTypeName)
    {
        return outerNamespace + "." + tsTypeName;
    }

    internal static string BuildEventRegistrationMethodName(string eventName) => "registerEvent_" + eventName;

    private static void QualifyActTargetTypeNames(
        string outerNamespace,
        IDictionary<int, ActTargetInfo> byId,
        IDictionary<string, ActTargetInfo> byName)
    {
        int[] ids = byId.Keys.ToArray();
        foreach (int id in ids)
        {
            ActTargetInfo info = byId[id];
            byId[id] = info with { TsTypeName = QualifyTsTypeName(outerNamespace, info.TsTypeName) };
        }

        string[] names = byName.Keys.ToArray();
        foreach (string name in names)
        {
            ActTargetInfo info = byName[name];
            byName[name] = info with { TsTypeName = QualifyTsTypeName(outerNamespace, info.TsTypeName) };
        }
    }

    internal static DeviceTsViewBuildResult BuildDeviceTsView(
        ProjectModel model,
        string deviceRelativePath,
        List<ToolError> warnings,
        IReadOnlyDictionary<string, string>? codeContentOverrides = null,
        ScriptAstCache? cache = null)
    {
        // 通过模型获取 DeviceEntity，禁止直接读取文件系统
        DeviceEntity? deviceEntity = model.Find<DeviceEntity>(deviceRelativePath);
        if (deviceEntity is null)
        {
            warnings.Add(new ToolError(deviceRelativePath, null, null, "零件实体未找到", ToolDiagnosticSeverity.Error));
            return new DeviceTsViewBuildResult(string.Empty, new TsViewEditAllowance(Array.Empty<TsViewEditRange>(), 0), Array.Empty<DeviceTsViewScriptIndex>(), Array.Empty<string>());
        }

        // 从 DeviceEntity 构建元数据（替代 DeviceEntity.ReadMetadata）
        DeviceMetadata meta = BuildDeviceMetadataFromEntity(deviceEntity);

        (Dictionary<string, ScriptFileInfo> codeRepoGuidMap, HashSet<string> codeRepoAsyncNames) = GetCodeRepoGuidMap(model, cache, warnings);

        // 应用 code 内容 override
        if (codeContentOverrides is { Count: > 0 })
        {
            codeRepoGuidMap = ApplyCodeContentOverrides(model, codeRepoGuidMap, codeContentOverrides);
        }

        DeviceConversionContext ctx = DeviceConversionContext.Build(model, deviceRelativePath, meta, codeRepoGuidMap, warnings);
        (string outerNs, string outerKind, string _) = ResolveOuterNamespace(deviceEntity);
        string deviceNs = BuildDeviceTsViewClassName(deviceEntity);

        BirthBuildingBaseInfo? birthBuildingBaseInfo = null;
        if (meta.Type == DeviceTypeIds.BirthBuilding)
        {
            birthBuildingBaseInfo = ResolveBirthBuildingBaseInfo(model, meta, deviceRelativePath, outerNs, warnings);
        }

        List<string> referencedDevicePaths = [];
        if (birthBuildingBaseInfo is not null)
            referencedDevicePaths.Add(birthBuildingBaseInfo.ReferencedDevicePath);

        // 从 DeviceEntity 获取全局变量（替代 DeviceEntity.ReadGlobalVars）
        IReadOnlyList<DeviceGlobalVar> globalVars = BuildGlobalVarsFromEntity(deviceEntity);
        IReadOnlyDictionary<string, BescriptVarType> globalVarTypes = BuildGlobalVarTypeMap(globalVars);
        IReadOnlyList<AllowedEventInfo> allowedEvents = BuildAllowedEventInfos(meta.Type, warnings, deviceRelativePath);

        List<string> headerExtraLines =
        [
            $"- devicePath: {deviceRelativePath}",
            $"- outer: {outerKind} {outerNs}",
            "此文件必须通过 MCP 修改才有效，绝对禁止 MCP 之外的任何修改途径",
            "此段为固定结构，不得随意修改。",
        ];
        List<string> references =
        [
            "../sdk.ts",
            "../custom-logic.d.ts",
            "../CodeRepo.ts"
        ];
        if (!string.IsNullOrWhiteSpace(birthBuildingBaseInfo?.ReferencePath))
        {
            references.Add(birthBuildingBaseInfo.ReferencePath);
        }

        // 从 DeviceEntity 获取脚本列表（替代 ReadScriptList 的文件读取）
        IReadOnlyList<ScriptEntry> scripts = GetScriptEntriesFromDeviceEntity(deviceEntity, codeRepoGuidMap, warnings, cache);
        Dictionary<string, IReadOnlyList<ScriptEntry>> eventScriptsByEvent = new(StringComparer.Ordinal);
        Dictionary<string, EventDir> eventDirMap = new(StringComparer.Ordinal);
        foreach (AllowedEventInfo ev in allowedEvents)
        {
            IReadOnlyList<ScriptEntry> eventScripts = GetEventScriptEntriesFromDeviceEntity(deviceEntity, ev.EventName, codeRepoGuidMap, warnings, cache);
            if (eventScripts.Count > 0)
            {
                eventScriptsByEvent[ev.EventName] = eventScripts;
                // 构造 EventDir（ResolveEventSignature 实际上不使用 Directory，传入零件目录即可）
                eventDirMap[ev.EventName] = new EventDir(ev.EventName, deviceRelativePath);
            }
        }

        List<TsViewScriptAsyncInput> asyncInputs = [];
        foreach (ScriptEntry script in scripts)
        {
            asyncInputs.Add(new TsViewScriptAsyncInput(script.Name, script.SourcePath, script.ScriptInfo));
        }
        foreach (IReadOnlyList<ScriptEntry> eventScripts in eventScriptsByEvent.Values)
        {
            foreach (ScriptEntry script in eventScripts)
            {
                asyncInputs.Add(new TsViewScriptAsyncInput(script.Name, script.SourcePath, script.ScriptInfo));
            }
        }

        HashSet<string> seedAsyncNames = new(StringComparer.Ordinal);
        HashSet<string> seedAsyncNamesWithReturn = new(StringComparer.Ordinal);
        foreach (ScriptEntry script in scripts)
        {
            if (GuidText.Validate(script.Guid) &&
                !string.IsNullOrWhiteSpace(script.SourcePath))
            {
                // script.Name 本身就是脚本名（不含扩展名），无需从路径截取
                if (codeRepoAsyncNames.Contains(script.Name))
                {
                    seedAsyncNames.Add(script.Name);
                    if (codeRepoGuidMap.TryGetValue(script.Guid, out ScriptFileInfo? info) &&
                        info.ScriptInfo.Header.ReturnParameter is { Name: { Length: > 0 } })
                    {
                        seedAsyncNamesWithReturn.Add(script.Name);
                    }
                }
            }
        }
        foreach (IReadOnlyList<ScriptEntry> eventScripts in eventScriptsByEvent.Values)
        {
            foreach (ScriptEntry script in eventScripts)
            {
                if (GuidText.Validate(script.Guid) &&
                    !string.IsNullOrWhiteSpace(script.SourcePath))
                {
                    // script.Name 本身就是脚本名（不含扩展名），无需从路径截取
                    if (codeRepoAsyncNames.Contains(script.Name))
                    {
                        seedAsyncNames.Add(script.Name);
                        if (codeRepoGuidMap.TryGetValue(script.Guid, out ScriptFileInfo? info) &&
                            info.ScriptInfo.Header.ReturnParameter is { Name: { Length: > 0 } })
                        {
                            seedAsyncNamesWithReturn.Add(script.Name);
                        }
                    }
                }
            }
        }

        // 从已构建的 ActTargetMap 收集跨零件 AsyncWithReturnRawNames（BuildScriptActMethodMaps 已算好）
        HashSet<string> externalAsyncWithReturnRawNames = new(StringComparer.Ordinal);
        foreach (ActTargetInfo target in ctx.ActTargetMap.ById.Values)
            foreach (string name in target.AsyncWithReturnRawNames)
                externalAsyncWithReturnRawNames.Add(name);

        HashSet<string> asyncNames = TsViewScriptAsyncResolver.ResolveAsyncScripts(asyncInputs, seedAsyncNames, seedAsyncNamesWithReturn, externalAsyncWithReturnRawNames);

        // 用完整传播结果回填 Self 的 AsyncRawNames / AsyncWithReturnRawNames
        // BuildScriptActMethodMaps 中计算时缺少跨零件 external 信息，此处是唯一权威来源
        HashSet<string> asyncWithReturnRawNames = new(StringComparer.Ordinal);
        foreach (ScriptEntry script in scripts.Concat(eventScriptsByEvent.Values.SelectMany(static v => v)))
        {
            if (asyncNames.Contains(script.Name) &&
                script.ScriptInfo.Header.ReturnParameter is { Name: { Length: > 0 } })
            {
                asyncWithReturnRawNames.Add(script.Name);
            }
        }
        ctx = ctx with { ActTargetMap = ctx.ActTargetMap with { Self = ctx.ActTargetMap.Self with { AsyncRawNames = asyncNames, AsyncWithReturnRawNames = asyncWithReturnRawNames } } };

        string deviceTypeName = BescriptDtsGenerator.BuildDeviceTypeTypeName(meta.Type);
        HashSet<string> usedMethodNames = new(StringComparer.Ordinal);
        List<(ScriptEntry Script, string MethodName)> scriptMethodMetas = new(scripts.Count);
        Dictionary<string, List<(ScriptEntry Script, string MethodName)>> eventMethodMetas = new(StringComparer.Ordinal);
        Dictionary<string, string> methodDisplayNameMap = new(StringComparer.Ordinal);
        HashSet<string> methodDisplayNameConflicts = new(StringComparer.Ordinal);
        foreach (ScriptEntry script in scripts)
        {
            string methodName = BuildUniqueMethodName(script.Name, usedMethodNames);
            scriptMethodMetas.Add((script, methodName));
            TrackMethodDisplayName(methodDisplayNameMap, methodDisplayNameConflicts, script.Name, methodName);
        }
        foreach (AllowedEventInfo ev in allowedEvents)
        {
            if (!eventScriptsByEvent.TryGetValue(ev.EventName, out IReadOnlyList<ScriptEntry>? eventScripts))
            {
                continue;
            }

            List<(ScriptEntry Script, string MethodName)> methodInfos = new(eventScripts.Count);
            HashSet<string> usedEventMethodNames = new(StringComparer.Ordinal);
            foreach (ScriptEntry script in eventScripts)
            {
                string methodName = TsViewNaming.BuildMethodIdentifier(script.Name);
                if (!usedEventMethodNames.Add(methodName))
                {
                    throw new InvalidOperationException($"事件脚本方法名冲突: {methodName}（事件 {ev.EventName} 内）");
                }
                methodInfos.Add((script, methodName));
            }
            eventMethodMetas[ev.EventName] = methodInfos;
        }
        foreach (string conflicted in methodDisplayNameConflicts)
        {
            methodDisplayNameMap.Remove(conflicted);
        }
        string baseTypeName = EnsureDeviceTypeName(deviceTypeName);
        if (meta.Type == DeviceTypeIds.BirthBuilding && birthBuildingBaseInfo is not null)
        {
            // 1110 机械产生器：有目标机械时继承其核心零件类型 {mechNs}.{deviceClassName}。
            // birthBuildingBaseInfo 为 null 的两种合法场景：
            //   1) 零件未配置 mechFileGUID（ext 中无该字段）
            //   2) GUID 指向的机械文件不存在
            // 以上两种情况保留默认的 DeviceType_N 基类，绝对不能引用不存在的 "CoreDevice" 类名。
            baseTypeName = birthBuildingBaseInfo.BaseTypeName;
        }
        TsViewGlobalVarInfo[] globalVarInfos = globalVars
            .Select(static gv => new TsViewGlobalVarInfo(
                gv.Name,
                TsViewIdentifierMap.ToTsIdentifier(gv.Name),
                ToTsGlobalVarBeTypeName(gv.Type),
                BuildGlobalVarInitValue(gv.Type),
                gv.Comment))
            .ToArray();

        // 阶段3: 脚本体生成（并行化）
        List<ScriptMethodInfo> scriptMethodInfos = BuildScriptMethodInfosParallel(
            scriptMethodMetas,
            asyncNames,
            globalVarTypes,
            ctx.ActTargetMap,
            methodDisplayNameMap,
            warnings);

        List<EventMethodGroupInfo> eventMethodInfos = BuildEventMethodInfosParallel(
            allowedEvents,
            eventDirMap,
            eventScriptsByEvent,
            eventMethodMetas,
            meta.Type,
            asyncNames,
            globalVarTypes,
            ctx.ActTargetMap,
            methodDisplayNameMap,
            deviceRelativePath,
            warnings);

        DeviceTsViewBuildContext buildContext = new()
        {
            ReferencePaths = references,
            CommentLines = new[] { "零件 TS 聚合视图（临时）" },
            ExtraCommentLines = headerExtraLines,
            OuterNamespace = outerNs,
            DeviceClassName = deviceNs,
            BaseTypeName = baseTypeName,
            IsBirthBuilding = meta.Type == DeviceTypeIds.BirthBuilding,
            AllowedEvents = allowedEvents.Select(static e => e.EventName).ToArray(),
            GlobalVars = globalVarInfos,
            ScriptMethodInfos = scriptMethodInfos,
            EventMethodInfos = eventMethodInfos
        };

        return EmitDeviceTsViewWithMetadata(buildContext, referencedDevicePaths, ctx.ActTargetMap);
    }

    private static DeviceTsViewBuildResult EmitDeviceTsViewWithMetadata(
        DeviceTsViewBuildContext buildContext,
        IReadOnlyList<string> referencedDevicePaths,
        ActTargetMap? actTargetMap)
    {
        string inputJson = JsonSerializer.Serialize(buildContext, DeviceViewJsonOptions);
        string outputJson = TsCompilerHost.BuildDeviceView(inputJson);
        OfficialDeviceViewOutput? output = JsonSerializer.Deserialize<OfficialDeviceViewOutput>(outputJson, DeviceViewJsonOptions);
        if (output is null)
            throw new InvalidOperationException("TS 官方 Device 视图生成返回空结果。");
        if (output.Error is not null)
            throw new InvalidOperationException(output.Error);

        string tsView = output.TsView;
        IReadOnlyList<string> lines = TextLines.SplitLines(tsView);
        int totalLines = lines.Count;
        List<TsViewEditRange> outerRanges = BuildOuterRangesFromGeneratedText(lines);
        List<DeviceTsViewScriptIndex> scriptIndexes = BuildGeneratedScriptIndexes(output.Scripts, buildContext);
        TsViewEditAllowance editAllowance = BuildEditAllowance(totalLines, outerRanges);
        return new DeviceTsViewBuildResult(tsView, editAllowance, scriptIndexes, referencedDevicePaths) { ActTargetMap = actTargetMap };
    }

    private sealed record OfficialDeviceViewOutput(
        string TsView,
        DeviceViewScriptIndexJson[]? Scripts,
        string? Error);

    private sealed record DeviceViewScriptIndexJson(
        string MethodName,
        int DocStartLine,
        int EndLine,
        DeviceViewBodyLineJson[] BodyLines);

    private sealed record DeviceViewBodyLineJson(int LineNumber, string LineText);

    private static List<TsViewEditRange> BuildOuterRangesFromGeneratedText(IReadOnlyList<string> lines)
    {
        List<TsViewEditRange> outerRanges = [];
        OuterSectionState? currentSection = null;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            int contentStart = 0;
            while (contentStart < line.Length && char.IsWhiteSpace(line[contentStart]))
                contentStart++;

            ReadOnlySpan<char> content = line.AsSpan(contentStart);
            if (!content.StartsWith("//", StringComparison.Ordinal))
                continue;

            string markerText = content[2..].ToString();
            int lineNumber = i + 1;
            currentSection = HandleSectionMarker(
                markerText,
                lineNumber,
                currentSection,
                outerRanges);
        }

        return outerRanges;
    }

    private static List<DeviceTsViewScriptIndex> BuildGeneratedScriptIndexes(
        DeviceViewScriptIndexJson[]? scripts,
        DeviceTsViewBuildContext buildContext)
    {
        if (buildContext.ScriptMethodInfos.Count == 0 || scripts is null || scripts.Length == 0)
            return [];

        Dictionary<string, DeviceViewScriptIndexJson> byMethodName = new(StringComparer.Ordinal);
        foreach (DeviceViewScriptIndexJson s in scripts)
            byMethodName[s.MethodName] = s;

        List<DeviceTsViewScriptIndex> result = new(buildContext.ScriptMethodInfos.Count);
        foreach (ScriptMethodInfo script in buildContext.ScriptMethodInfos)
        {
            if (!byMethodName.TryGetValue(script.MethodName, out DeviceViewScriptIndexJson? indexed))
                throw new InvalidOperationException($"生成后的 TS 视图未找到脚本方法: {script.MethodName}");

            result.Add(new DeviceTsViewScriptIndex(
                script.SourcePath,
                script.Guid,
                script.OriginalName,
                indexed.DocStartLine,
                indexed.EndLine,
                BuildGeneratedLineMap(script.Body, indexed.BodyLines)));
        }

        return result;
    }

    private static IReadOnlyList<TsViewLineMapping>? BuildGeneratedLineMap(
        DeviceTsViewScriptBody body,
        IReadOnlyList<DeviceViewBodyLineJson> bodyLines)
    {
        if (body.Statements.Count == 0 || bodyLines.Count == 0)
            return null;

        List<TsViewLineMapping> mappings = [];
        int bodyLineIndex = 0;
        foreach (DeviceTsViewStatement statement in body.Statements)
        {
            if (bodyLineIndex >= bodyLines.Count)
                break;

            if (statement.SourceLine.HasValue)
                mappings.Add(new TsViewLineMapping(statement.SourceLine.Value, bodyLines[bodyLineIndex].LineNumber));
            bodyLineIndex++;
        }

        if (mappings.Count == 0)
            return null;
        return mappings;
    }

    private static OuterSectionState? HandleSectionMarker(
        string text,
        int lineNumber,
        OuterSectionState? currentSection,
        List<TsViewEditRange> outerRanges)
    {
        if (string.Equals(text, " [Globals] BEGIN", StringComparison.Ordinal))
        {
            return new OuterSectionState("globals", lineNumber);
        }

        if (string.Equals(text, " [Globals] END", StringComparison.Ordinal))
        {
            if (currentSection?.Kind == "globals")
            {
                outerRanges.Add(BuildSectionMarkerSpan(currentSection, lineNumber));
            }
            return null;
        }

        if (string.Equals(text, " [Script] BEGIN", StringComparison.Ordinal))
        {
            return new OuterSectionState("script", lineNumber);
        }

        if (string.Equals(text, " [Script] END", StringComparison.Ordinal))
        {
            if (currentSection?.Kind == "script")
            {
                outerRanges.Add(BuildSectionMarkerSpan(currentSection, lineNumber));
            }
            return null;
        }

        if (string.Equals(text, " [Events] BEGIN", StringComparison.Ordinal))
        {
            return new OuterSectionState("events", lineNumber);
        }

        if (string.Equals(text, " [Events] END", StringComparison.Ordinal))
        {
            if (currentSection?.Kind == "events")
            {
                outerRanges.Add(BuildSectionMarkerSpan(currentSection, lineNumber));
            }
            return null;
        }

        return currentSection;
    }

    private static TsViewEditRange BuildSectionMarkerSpan(OuterSectionState section, int endLine) =>
        new(section.StartLine, endLine);

    /// <summary>
    /// 三区段（[Globals]/[Script]/[Events]）内容为可编辑区；其余行为生成时固定的视图骨架，补丁不得改动其文本。
    /// 方法体内的局部变量前置声明等约定由解析阶段单独报「方法体结构约定」类错误，不做行级禁区拼图。
    /// </summary>
    private static TsViewEditAllowance BuildEditAllowance(
        int totalLines,
        IReadOnlyList<TsViewEditRange> outerRanges)
    {
        if (totalLines <= 0)
        {
            return new TsViewEditAllowance(Array.Empty<TsViewEditRange>(), 0);
        }

        bool[] allowed = new bool[totalLines + 1]; // 1-based，0 未用

        foreach (TsViewEditRange outer in outerRanges)
        {
            int start = Math.Max(1, outer.StartLine + 1);
            int end = Math.Min(outer.EndLine - 1, totalLines);
            if (start > end || allowed.Length == 0)
            {
                continue;
            }

            for (int i = start; i <= end; i++)
            {
                allowed[i] = true;
            }
        }

        IReadOnlyList<TsViewEditRange> forbidden = CompressForbiddenRanges(allowed, totalLines);
        return new TsViewEditAllowance(forbidden, totalLines);
    }

    private static IReadOnlyList<TsViewEditRange> CompressForbiddenRanges(bool[] allowed, int totalLines)
    {
        List<TsViewEditRange> result = [];
        if (totalLines <= 0)
        {
            return result;
        }

        int? forbidStart = null;
        for (int line = 1; line <= totalLines; line++)
        {
            bool isAllowed = line < allowed.Length && allowed[line];
            if (!isAllowed)
            {
                forbidStart ??= line;
            }
            else if (forbidStart.HasValue)
            {
                result.Add(new TsViewEditRange(forbidStart.Value, line - 1));
                forbidStart = null;
            }
        }

        if (forbidStart.HasValue)
        {
            result.Add(new TsViewEditRange(forbidStart.Value, totalLines));
        }

        return result;
    }

    private sealed record OuterSectionState(string Kind, int StartLine);

    private static ScriptMethodInfo BuildDeviceScriptFileInfo(
        ScriptEntry script,
        string methodName,
        IReadOnlySet<string> asyncNames,
        IReadOnlyDictionary<string, BescriptVarType> globalVarTypes,
        ActTargetMap actTargetMap,
        IReadOnlyDictionary<string, string> methodDisplayNameMap,
        List<ToolError> warnings)
    {
        BescriptHeader header = script.ScriptInfo.Header;

        IReadOnlyList<BescriptParameter> parameters = header.Parameters;
        IReadOnlyList<TsViewParamSpec> paramSpecs = TsViewParamSpecTools.BuildFromParameters(parameters, warnings, script.SourcePath);
        TsViewScriptReturnInfo returnInfo = TsViewReturnInfoBuilder.Build(header.ReturnParameter);

        // CodeRepo 脚本仅用于定位 GUID，不在 TS 视图上暴露 sourcePath，避免写回校验误判
        string? docSourcePath = script.SourcePath;
        if (script.IsCodeRepo)
            docSourcePath = null;
        DeviceTsViewScriptBody body = BuildScriptBody(
            script.SourcePath,
            script.ScriptInfo,
            paramSpecs,
            globalVarTypes,
            actTargetMap,
            returnInfo,
            methodDisplayNameMap,
            warnings,
            script.Guid);

        return new ScriptMethodInfo(
            script.Name,
            methodName,
            docSourcePath,
            script.Guid,
            script.IsCodeRepo,
            BuildDeviceViewBescriptHeader(header),
            asyncNames.Contains(script.Name),
            header.Private || script.IsPrivateFromList,
            paramSpecs.Select(static p => new ParameterInfo(p.Name, p.TypeName)).ToArray(),
            returnInfo.ReturnType,
            body);
    }

    private static ScriptMethodInfo BuildEventScriptFileInfo(
        ScriptEntry script,
        string methodName,
        string eventName,
        IReadOnlyList<EventParamSpec> eventParams,
        IReadOnlySet<string> asyncNames,
        IReadOnlyDictionary<string, BescriptVarType> globalVarTypes,
        ActTargetMap actTargetMap,
        IReadOnlyDictionary<string, string> methodDisplayNameMap,
        List<ToolError> warnings)
    {
        BescriptHeader header = script.ScriptInfo.Header;

        TsViewParamSpec[] eventParamSpecs = eventParams.Select(static p => new TsViewParamSpec(p.TsName, p.BeTypeName)).ToArray();

        // CodeRepo 脚本仅用于定位 GUID，不在 TS 视图上暴露 sourcePath，避免写回校验误判
        string? docSourcePath = script.SourcePath;
        if (script.IsCodeRepo)
            docSourcePath = null;
        DeviceTsViewScriptBody body = BuildScriptBody(
            script.SourcePath,
            script.ScriptInfo,
            eventParamSpecs,
            globalVarTypes,
            actTargetMap,
            new TsViewScriptReturnInfo.NoReturn(),
            methodDisplayNameMap,
            warnings,
            script.Guid);

        // 原始签名：仅当链接 code 且有参数/返回值时保留，用于写回
        IReadOnlyList<BescriptParameter> parameters = header.Parameters;
        BescriptParameter? returnParameter = header.ReturnParameter;
        IReadOnlyList<ParameterInfo>? rawParameters = null;
        string? rawRetVar = null;
        if (script.IsCodeRepo && (parameters.Count > 0 || returnParameter is not null))
        {
            IReadOnlyList<TsViewParamSpec> rawParamSpecs = TsViewParamSpecTools.BuildFromParameters(parameters, warnings: null, sourcePath: null);
            rawParameters = rawParamSpecs.Select(static p => new ParameterInfo(p.Name, p.TypeName)).ToArray();
            // rawRetVar 格式: "varName: BeTypeName"（与 rawParams 格式一致）
            if (returnParameter is { } retParam && BescriptVarTypeHelper.TryParse(TsViewTypeUtils.ConvertToTsTypeName(retParam.TypeName)) is { } retVarType)
            {
                string tsRetType = TsViewTypeUtils.ToTsBeTypeName(retVarType);
                rawRetVar = retParam.Name + ": " + tsRetType;
            }
        }

        return new ScriptMethodInfo(
            script.Name,
            methodName,
            docSourcePath,
            script.Guid,
            script.IsCodeRepo,
            BuildDeviceViewBescriptHeader(header),
            asyncNames.Contains(script.Name),
            IsPrivate: true,
            eventParamSpecs.Select(static p => new ParameterInfo(p.Name, p.TypeName)).ToArray(),
            ReturnType: "void",
            body,
            RawParameters: rawParameters,
            RawRetVar: rawRetVar);
    }

    private static DeviceTsViewBescriptHeader BuildDeviceViewBescriptHeader(BescriptHeader header)
    {
        DeviceTsViewBescriptColor? color = header.Color is null
            ? null
            : new DeviceTsViewBescriptColor(header.Color.R, header.Color.G, header.Color.B);
        List<ParameterInfo> parameters = new(header.Parameters.Count);
        foreach (BescriptParameter parameter in header.Parameters)
        {
            string typeName = TsViewTypeUtils.ConvertToTsTypeName(parameter.TypeName);
            if (BescriptVarTypeHelper.TryParse(typeName) is not BescriptVarType varType)
            {
                throw new InvalidOperationException($"参数类型无法识别: {parameter.Name}:{parameter.TypeName}");
            }

            parameters.Add(new ParameterInfo(parameter.Name, TsViewTypeUtils.ToTsBeTypeName(varType)));
        }

        ParameterInfo? returnParameter = null;
        if (header.ReturnParameter is { } ret)
        {
            string typeName = TsViewTypeUtils.ConvertToTsTypeName(ret.TypeName);
            if (BescriptVarTypeHelper.TryParse(typeName) is not BescriptVarType varType)
            {
                throw new InvalidOperationException($"返回类型无法识别: {ret.Name}:{ret.TypeName}");
            }

            returnParameter = new ParameterInfo(ret.Name, TsViewTypeUtils.ToTsBeTypeName(varType));
        }

        return new DeviceTsViewBescriptHeader(
            header.Name,
            header.Comment,
            header.Debug,
            header.Skip,
            header.Private,
            header.Guid,
            color,
            parameters,
            returnParameter);
    }

    private static DeviceTsViewScriptBody BuildScriptBody(
        string? sourcePath,
        BescriptScriptInfo scriptInfo,
        IReadOnlyList<TsViewParamSpec> paramSpecs,
        IReadOnlyDictionary<string, BescriptVarType> globalVarTypes,
        ActTargetMap actTargetMap,
        TsViewScriptReturnInfo returnInfo,
        IReadOnlyDictionary<string, string> methodDisplayNameMap,
        List<ToolError> warnings,
        string? guid)
    {
        BescriptAst besAst = scriptInfo.Ast;
        Dictionary<int, string> commandLines = [];
        foreach (BescriptAstNode node in besAst.RootNodes.FlattenNodes())
        {
            int endLine = node.EndLine > 0 ? node.EndLine : node.Line;
            commandLines[node.Line] = node.Text;
            for (int lineNumber = node.Line + 1; lineNumber <= endLine; lineNumber++)
                commandLines[lineNumber] = node.Text;
        }
        List<DeviceTsViewScriptBodyLine> lines = new(scriptInfo.LineCount);
        for (int lineNumber = 1; lineNumber <= scriptInfo.LineCount; lineNumber++)
        {
            lines.Add(new DeviceTsViewScriptBodyLine(lineNumber, commandLines.TryGetValue(lineNumber, out string? lineText) ? lineText : string.Empty));
        }

        if (besAst.RootNodes.Count == 0)
        {
            return new DeviceTsViewScriptBody([], lines);
        }

        ScriptConversionContext ctx = new(
            BuiltinMethodRegistry.Instance,
            actTargetMap,
            globalVarTypes,
            methodDisplayNameMap: methodDisplayNameMap);
        if (returnInfo is TsViewScriptReturnInfo.WithReturn returnValueInfo)
            ctx.ExpectedReturnVarName = TsViewIdentifierMap.ToTsIdentifier(returnValueInfo.ReturnVarName);
        ctx.ExpectedReturnType = returnInfo.ReturnType;
        IReadOnlyDictionary<string, BescriptVarType>? declaredLocals = FilterLocalDeclaredVariables(besAst.DeclaredVariables, globalVarTypes);

        string tsReturnVarName;
        if (returnInfo is TsViewScriptReturnInfo.WithReturn valueReturnInfo)
        {
            tsReturnVarName = TsViewIdentifierMap.ToTsIdentifier(valueReturnInfo.ReturnVarName);
            if (TsViewTypeUtils.TryParseBescriptVarType(returnInfo.ReturnType) is not BescriptVarType returnVarType)
            {
                throw new InvalidOperationException($"返回类型无法识别: {returnInfo.ReturnType}");
            }
            if (!ctx.DeclaredVariables.ContainsKey(tsReturnVarName))
            {
                ctx.DeclaredVariables[tsReturnVarName] = returnVarType;
            }
        }

        foreach (TsViewParamSpec param in paramSpecs)
        {
            ctx.RegisterParameter(param.Name, param.TypeName);
        }

        if (declaredLocals is not null)
        {
            foreach ((string? key, BescriptVarType value) in declaredLocals)
            {
                string tsName = TsViewIdentifierMap.ToTsIdentifier(key);
                if (!ctx.DeclaredVariables.ContainsKey(tsName))
                {
                    ctx.DeclaredVariables[tsName] = value;
                }
            }
        }

        // TS 视图侧重算：按 SDK 注册重算 async 状态
        besAst = besAst with { RootNodes = TsViewAsyncValidation.RewriteAsyncForTsView(besAst.RootNodes) };

        try
        {
            if (returnInfo is TsViewScriptReturnInfo.WithReturn finalReturnInfo)
                return BescriptToDeviceTsViewMapper.Map(besAst, ctx, 0, TsViewIdentifierMap.ToTsIdentifier(finalReturnInfo.ReturnVarName), lines);

            return BescriptToDeviceTsViewMapper.Map(besAst, ctx, 0, lines);
        }
        catch (InvalidOperationException ex)
        {
            string displaySourcePath = "<memory>";
            if (sourcePath is not null)
                displaySourcePath = sourcePath;
            throw new InvalidOperationException($"脚本转换失败: {displaySourcePath}: {ex.Message}", ex);
        }
    }

    private static IReadOnlyDictionary<string, BescriptVarType>? FilterLocalDeclaredVariables(
        IReadOnlyDictionary<string, BescriptVarType>? declaredVariables,
        IReadOnlyDictionary<string, BescriptVarType> globalVarTypes)
    {
        if (declaredVariables is null || declaredVariables.Count == 0)
        {
            return declaredVariables;
        }

        Dictionary<string, BescriptVarType> locals = new(StringComparer.Ordinal);
        foreach ((string? rawName, BescriptVarType varType) in declaredVariables)
        {
            string tsName = TsViewIdentifierMap.ToTsIdentifier(rawName);
            if (globalVarTypes.ContainsKey(tsName))
            {
                continue;
            }

            if (!locals.ContainsKey(tsName))
            {
                locals[tsName] = varType;
            }
        }

        return locals;
    }

    private static string BuildGlobalVarInitValue(BescriptVarType type)
    {
        return type switch
        {
            BescriptVarType.Bool => "0",
            BescriptVarType.Float => "0",
            BescriptVarType.Long => "0",
            BescriptVarType.Enum => "0",
            BescriptVarType.Datetime => "0",
            BescriptVarType.Vector3 => "0,0,0",
            BescriptVarType.Color => "0,0,0,0",
            _ => string.Empty
        };
    }

    private static string BuildUniqueMethodName(
        string rawName,
        ISet<string> usedNames)
    {
        string methodName = TsViewNaming.BuildMethodIdentifier(rawName);

        if (usedNames.Contains(methodName))
        {
            throw new InvalidOperationException($"方法名重复: {methodName}。请检查脚本名称是否存在归一化后的冲突");
        }

        usedNames.Add(methodName);
        return methodName;
    }

    private static void TrackMethodDisplayName(
        IDictionary<string, string> map,
        ISet<string> conflicts,
        string rawName,
        string displayName)
    {
        if (map.TryGetValue(rawName, out string? existing) && !existing.Equals(displayName, StringComparison.Ordinal))
        {
            conflicts.Add(rawName);
            return;
        }

        if (!conflicts.Contains(rawName))
        {
            map[rawName] = displayName;
        }
    }

    /// <summary>
    /// 枚举零件的事件脚本目录。通过模型读取，禁止直接扫描文件系统。
    /// </summary>
    internal static IReadOnlyList<EventDir> EnumerateEventDirectories(ProjectModel model, string deviceDir)
    {
        if (model.Find<DeviceEntity>(deviceDir) is not DeviceEntity device)
        {
            throw new InvalidOperationException($"零件未加载: {deviceDir}");
        }

        return device.ScriptGroups
            .SelectMany(static g =>
                g.EventName is string eventName
                    ? new[] { new EventDir(eventName, g.脚本组目录_完整相对路径) }
                    : Array.Empty<EventDir>())
            .OrderBy(static x => x.EventName, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 解析零件所在的外层命名空间（地图或机械）。
    /// 直接从零件实体获取载体信息，零件的 Parent 指向载体实体。
    /// </summary>
    private static (string OuterNamespace, string Kind, string OuterDirectory) ResolveOuterNamespace(DeviceEntity device)
    {
        string kind = device.CarrierKind;
        string guid = device.CarrierGuid;
        string path = device.Parent switch
        {
            MapEntity map => map.派生目录_完整相对路径,
            MechEntity mech => mech.派生目录_完整相对路径,
            _ => throw new InvalidOperationException($"零件 {device.device目录_完整相对路径} 的父级不是有效的载体实体")
        };
        return (kind + "_" + guid, kind, path);
    }

    /// <summary>
    /// 获取代码仓库 GUID 映射和异步名称集合。
    /// cache 非空时走代际缓存（同轮内同内容只解析一次）；为 null 时实时扫描（override/测试路径）。
    /// </summary>
    private static (Dictionary<string, ScriptFileInfo> Map, HashSet<string> AsyncNames) GetCodeRepoGuidMap(ProjectModel model, ScriptAstCache? cache, List<ToolError> warnings)
    {
        List<ScriptFileInfo> scripts = cache is null
            ? CodeRepoScripts.Scan(model, warnings)
            : CodeRepoScripts.ScanOrCache(model, cache, warnings);
        TsViewScriptAsyncInput[] asyncInputs = scripts.Select(static s => new TsViewScriptAsyncInput(s.FileStem, s.RelativePath, s.ScriptInfo)).ToArray();
        HashSet<string> asyncNames = TsViewScriptAsyncResolver.ResolveAsyncScripts(asyncInputs);
        Dictionary<string, ScriptFileInfo> map = CodeRepoScripts.BuildGuidMap(scripts, static script => script);
        return (map, asyncNames);
    }

    /// <summary>
    /// 应用 code 内容 override，返回新的 codeRepoGuidMap。
    /// </summary>
    private static Dictionary<string, ScriptFileInfo> ApplyCodeContentOverrides(
        ProjectModel model,
        Dictionary<string, ScriptFileInfo> original,
        IReadOnlyDictionary<string, string> overrides)
    {
        Dictionary<string, ScriptFileInfo> result = new(original, StringComparer.Ordinal);

        foreach ((string? relativePath, string? newContent) in overrides)
        {
            // 只处理 CodeRepo 脚本（通过实体查询判断）
            if (model.Find<CodeRepoScriptEntity>(relativePath) is not CodeRepoScriptEntity scriptEntity)
            {
                continue;
            }

            string guid = scriptEntity.Guid;

            // 解析新内容
            BescriptScriptInfo scriptInfo = BescriptTextParser.Parse(relativePath, newContent).Script;
            TsViewCodeScriptParseResult parsed = TsViewCodeScriptReader.ParseFromResult(scriptInfo, newContent);

            string relForNamespace = CodeRepoPath.NormalizeForNamespace(relativePath);
            string[] parts = relForNamespace.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            string namespaceFileStem = parts[^1];
            string[] namespaceDirSegs = Array.Empty<string>();
            if (parts.Length > 1)
                namespaceDirSegs = parts[..^1];
            string fileStem = scriptEntity.ScriptName;

            ScriptFileInfo newInfo = new(
                relativePath,
                namespaceDirSegs,
                namespaceFileStem,
                fileStem,
                guid,
                newContent,
                scriptInfo);

            result[guid] = newInfo;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, BescriptVarType> BuildGlobalVarTypeMap(IReadOnlyList<DeviceGlobalVar> globals)
    {
        Dictionary<string, BescriptVarType> map = new(StringComparer.Ordinal);
        foreach (DeviceGlobalVar gv in globals)
        {
            string tsName = TsViewIdentifierMap.ToTsIdentifier(gv.Name);
            if (!map.ContainsKey(tsName))
            {
                map[tsName] = gv.Type;
            }
        }

        return map;
    }

    private static IReadOnlyList<AllowedEventInfo> BuildAllowedEventInfos(int deviceTypeId, List<ToolError> warnings, string devicePath)
    {
        EventSignatureRegistry registry = EventSignatureRegistry.Instance;
        IReadOnlyDictionary<string, EventSignature> selected = EventSignatureRules.SelectAllowedEventSignatures(deviceTypeId, registry);
        if (selected.Count == 0)
        {
            return Array.Empty<AllowedEventInfo>();
        }

        return selected
            .OrderBy(static kv => kv.Key, StringComparer.Ordinal)
            .Select(static kv => new AllowedEventInfo(kv.Key, kv.Value))
            .ToArray();
    }

    private static IReadOnlyList<ScriptEntry> BuildScriptEntriesFromScriptGroup(
        DeviceEntityBase device,
        ScriptGroupEntityBase scriptGroup,
        IReadOnlyDictionary<string, ScriptFileInfo> codeRepoGuidMap,
        List<ToolError> warnings,
        string? eventNameForWarning,
        bool skipBlankInlineSource,
        ScriptAstCache? cache = null)
    {
        List<ScriptEntry> result = new(scriptGroup.ScriptEntries.Count);
        Dictionary<string, InlineScriptEntityBase> inlineScriptsByName = new(StringComparer.Ordinal);

        foreach (InlineScriptEntityBase script in scriptGroup.Scripts)
        {
#pragma warning disable TK011 // 意图行为：由参数 skipBlankInlineSource 控制是否跳过空白内联脚本
            if (skipBlankInlineSource && script.SourceText == string.Empty)
#pragma warning restore TK011
            {
                continue;
            }

            if (!inlineScriptsByName.ContainsKey(script.ScriptName))
            {
                inlineScriptsByName[script.ScriptName] = script;
            }
        }

        foreach (ScriptEntryInfo entry in scriptGroup.ScriptEntries)
        {
            if (!entry.IsCodeRepo)
            {
                if (!inlineScriptsByName.TryGetValue(entry.Name, out InlineScriptEntityBase? inlineScript))
                {
                    continue;
                }

                ArgumentNullException.ThrowIfNull(inlineScript.SourceText, $"脚本 {inlineScript.实体_完整相对路径标识} 的 SourceText 未加载");
                string scriptPath = inlineScript.实体_完整相对路径标识;
                BescriptScriptInfo scriptInfo = cache is null
                    ? BescriptTextParser.Parse(scriptPath, inlineScript.SourceText).Script
                    : cache.GetOrParse(scriptPath, _ => (inlineScript.SourceText, ScriptAstCache.ComputeTextHash(inlineScript.SourceText))).ScriptInfo;
                result.Add(new ScriptEntry(inlineScript.ScriptName, string.Empty, false, scriptPath, scriptInfo, entry.IsPrivate));
                continue;
            }

            if (!GuidText.Validate(entry.Guid))
            {
                continue;
            }

            string guidText = entry.Guid;
            if (!codeRepoGuidMap.TryGetValue(guidText, out ScriptFileInfo? repoInfo) || repoInfo.ScriptInfo is null)
            {
                string warningMessage = $"代码仓库脚本 GUID 未找到或无效: {entry.Name}";
                if (!string.IsNullOrWhiteSpace(eventNameForWarning))
                    warningMessage = $"事件 {eventNameForWarning} 代码仓库脚本 GUID 未找到或无效: {entry.Name}";

                warnings.Add(new ToolError(
                    device.device目录_完整相对路径,
                    null,
                    null,
                    warningMessage,
                    ToolDiagnosticSeverity.Warning));
                continue;
            }

            result.Add(new ScriptEntry(entry.Name, guidText, true, repoInfo.RelativePath, repoInfo.ScriptInfo, entry.IsPrivate));
        }

        return result;
    }

    private static (EventSignature? Signature, string DeviceType) ResolveEventSignature(int deviceTypeId, string eventDir, string eventName)
    {
        string deviceType = EventSignatureRules.ResolveEventOwnerType(deviceTypeId);
#pragma warning disable TK011 // deviceType 来自 ResolveEventOwnerType 边界映射；eventName 为空相当于无事件
        if (deviceType == string.Empty || eventName == string.Empty)
        {
            return (null, deviceType);
        }
#pragma warning restore TK011

        IReadOnlyList<string> chain = EventSignatureRules.BuildEventOwnerTypeChain(deviceType);
        EventSignatureRegistry registry = EventSignatureRegistry.Instance;
        EventSignature? signature = chain
            .Select(owner => registry.Get(owner, eventName))
            .FirstOrDefault(sig => sig is not null);
        return (signature, deviceType);
    }

    private static IReadOnlyList<EventParamSpec> BuildEventParamSpecs(EventSignature signature)
    {
        if (signature.Parameters.Count == 0)
        {
            return Array.Empty<EventParamSpec>();
        }

        List<EventParamSpec> list = new(signature.Parameters.Count);
        foreach (EventParameter p in signature.Parameters)
        {
            string tsName = TsViewTypeUtils.ToTsIdentifier(p.EffectiveName);
            string beTypeName = string.Empty;
#pragma warning disable TK011 // TypeName 为空时不构造 TS 类型名，属正常条件分支
            if (p.TypeName != string.Empty)
                beTypeName = BescriptDtsGenerator.BuildTsEventParamTypeName(p);
#pragma warning restore TK011
            list.Add(new EventParamSpec(tsName, beTypeName, p.EffectiveName, p.DisplayTypeName, p.Description, p.EnumType));
        }

        return list;
    }

    private static readonly Lazy<HashSet<string>> DeviceActionOwnerTypes = new(static () =>
    {
        HashSet<string> set = new(StringComparer.Ordinal);
        foreach (MethodSignature sig in DeviceActionDefinitions.GetAll())
        {
            if (!string.IsNullOrWhiteSpace(sig.OwnerType))
            {
                set.Add(sig.OwnerType);
            }
        }
        return set;
    });

    private static BirthBuildingBaseInfo? ResolveBirthBuildingBaseInfo(
        ProjectModel model,
        DeviceMetadata meta,
        string devicePath,
        string currentOuterNamespace,
        List<ToolError> warnings)
    {
        string? guid = meta.MechFileGuid;
        if (!GuidText.Validate(guid))
        {
            // 机械产生器缺失机械guid：无目标机械文件可引用，BuildActTargetMap 只能使用当前 1110 对应的独立虚拟机械。
            return null;
        }

        // 直接通过 GUID 获取机械实体
        if (model.FindByGuid(guid) is not ProjectEntity entity)
        {
            warnings.Add(new ToolError(devicePath, null, null,
                $"机械产生器引用的机械 GUID 未找到: {guid}", ToolDiagnosticSeverity.Warning));
            return null;
        }

        if (entity is not MechEntity mechEntity)
        {
            warnings.Add(new ToolError(devicePath, null, null,
                $"GUID {guid} 对应的实体不是机械: {entity.实体_完整相对路径标识}", ToolDiagnosticSeverity.Warning));
            return null;
        }

        string mechDir = mechEntity.派生目录_完整相对路径;

        // 机械产生器的基类是目标机械的核心零件（device id = 0）
        string referencedDevicePath = RelativePathUtils.Combine(mechDir, DeviceTypeIds.MapCoreDevice.ToString());

        // 构建引用路径：从当前 device 到目标机械核心零件 TS 视图
        string referencePath = BuildDeviceViewReferencePath(devicePath, referencedDevicePath);

        // 获取目标机械的核心零件实体，构建完整的基类名 {mechNamespace}.{deviceClassName}
        string baseTypeName;
        if (model.Find<DeviceEntity>(referencedDevicePath) is DeviceEntity coreDevice)
        {
            (string targetMechNs, string _, string _) = ResolveOuterNamespace(coreDevice);
            string coreDeviceClassName = BuildDeviceTsViewClassName(coreDevice);
            baseTypeName = $"{targetMechNs}.{coreDeviceClassName}";
        }
        else
        {
            warnings.Add(new ToolError(devicePath, null, null,
                $"无法获取目标机械的核心零件: {referencedDevicePath}", ToolDiagnosticSeverity.Warning));
            return null;
        }

        return new BirthBuildingBaseInfo(baseTypeName, referencePath, referencedDevicePath);
    }

    private static string BuildDeviceViewReferencePath(string fromDevicePath, string toDevicePath)
    {
        string from = WorkspacePath.RequireRelativePath(fromDevicePath);
        string to = WorkspacePath.RequireRelativePath(toDevicePath);

        string fromFile = RelativePathUtils.Combine(ScriptPathHelper.ViewsPath, $"{from}.ts");
        string fromDir = RelativePathUtils.GetParentDirectory(fromFile);
        string fromDirRelativePath = WorkspacePath.RequireRelativePath(fromDir);

        string toFile = RelativePathUtils.Combine(ScriptPathHelper.ViewsPath, $"{to}.ts");

        string[] fromSegments = fromDirRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] toSegments = toFile.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int common = 0;
        while (common < fromSegments.Length && common < toSegments.Length &&
               string.Equals(fromSegments[common], toSegments[common], StringComparison.Ordinal))
        {
            common++;
        }

        List<string> parts = [];
        for (int i = common; i < fromSegments.Length; i++)
        {
            parts.Add("..");
        }
        for (int i = common; i < toSegments.Length; i++)
        {
            parts.Add(toSegments[i]);
        }

        return string.Join("/", parts);
    }

    /// <summary>
    /// 为机械产生器构造独立虚拟机械作用域路径。
    /// 每个 1110 使用自身路径派生出的虚拟机械；该虚拟机械只包含核心零件，禁止混入地图内其他零件。
    /// </summary>
#pragma warning disable TK006 // 虚拟产生器路径构建，1110 无目标机械文件时专用
    private static string BuildVirtualBirthBuildingDir(string deviceDir)
    {
        string relDevice = WorkspacePath.RequireRelativePath(deviceDir);
        string safeName = relDevice.Replace('/', '_');
        return $".virtual_birthbuilding/{safeName}";
    }
#pragma warning restore TK006

    private static (MechEntity? Mech, string? FailureReason) TryResolveBirthBuildingMech(
        ProjectModel model,
        DeviceMetadata meta)
    {
        string? guid = meta.MechFileGuid;
        if (!GuidText.Validate(guid))
        {
            return (null, "mechFileGUID 为空");
        }

        // 直接通过 GUID 获取机械实体
        if (model.FindByGuid(guid) is not ProjectEntity entity)
        {
            return (null, $"未找到 GUID 对应的机械: {guid}");
        }

        if (entity is not MechEntity mechEntity)
        {
            return (null, $"GUID {guid} 对应的实体不是机械: {entity.实体_完整相对路径标识}");
        }

        return (mechEntity, null);
    }

    private sealed record BirthBuildingBaseInfo(string BaseTypeName, string ReferencePath, string ReferencedDevicePath);

    // ActTarget 相关方法已提取到 DeviceTsView.Build.ActTarget.cs

    // ─────────────────────────────────────────────────────────────────────────────
    // 并行化脚本体生成
    // ─────────────────────────────────────────────────────────────────────────────

    private static List<ScriptMethodInfo> BuildScriptMethodInfosParallel(
        List<(ScriptEntry Script, string MethodName)> scriptMethodMetas,
        IReadOnlySet<string> asyncNames,
        IReadOnlyDictionary<string, BescriptVarType> globalVarTypes,
        ActTargetMap actTargetMap,
        IReadOnlyDictionary<string, string> methodDisplayNameMap,
        List<ToolError> warnings)
    {
        if (scriptMethodMetas.Count == 0)
        {
            return [];
        }

        // 脚本数量少时串行执行，避免并行开销
        if (scriptMethodMetas.Count <= 2)
        {
            List<ScriptMethodInfo> result = new(scriptMethodMetas.Count);
            foreach ((ScriptEntry? script, string? methodName) in scriptMethodMetas)
            {
                result.Add(BuildDeviceScriptFileInfo(
                    script,
                    methodName,
                    asyncNames,
                    globalVarTypes,
                    actTargetMap,
                    methodDisplayNameMap,
                    warnings));
            }
            return result;
        }

        ScriptMethodInfo[] results = new ScriptMethodInfo[scriptMethodMetas.Count];
        ConcurrentBag<ToolError> localWarnings = [];

        Parallel.For(0, scriptMethodMetas.Count, i =>
        {
            (ScriptEntry script, string methodName) = scriptMethodMetas[i];
            List<ToolError> threadLocalWarnings = [];
            results[i] = BuildDeviceScriptFileInfo(
                script,
                methodName,
                asyncNames,
                globalVarTypes,
                actTargetMap,
                methodDisplayNameMap,
                threadLocalWarnings);
            foreach (ToolError w in threadLocalWarnings)
            {
                localWarnings.Add(w);
            }
        });

        warnings.AddRange(localWarnings);
        return results.ToList();
    }

    private static List<EventMethodGroupInfo> BuildEventMethodInfosParallel(
        IReadOnlyList<AllowedEventInfo> allowedEvents,
        Dictionary<string, EventDir> eventDirMap,
        Dictionary<string, IReadOnlyList<ScriptEntry>> eventScriptsByEvent,
        Dictionary<string, List<(ScriptEntry Script, string MethodName)>> eventMethodMetas,
        int deviceTypeId,
        IReadOnlySet<string> asyncNames,
        IReadOnlyDictionary<string, BescriptVarType> globalVarTypes,
        ActTargetMap actTargetMap,
        IReadOnlyDictionary<string, string> methodDisplayNameMap,
        string devicePath,
        List<ToolError> warnings)
    {
        if (allowedEvents.Count == 0)
        {
            return [];
        }

        // 事件数量少时串行执行
        if (allowedEvents.Count <= 2)
        {
            return BuildEventMethodInfosSerial(
                allowedEvents, eventDirMap, eventScriptsByEvent, eventMethodMetas,
                deviceTypeId, asyncNames, globalVarTypes, actTargetMap,
                methodDisplayNameMap, devicePath, warnings);
        }

        EventMethodGroupInfo[] results = new EventMethodGroupInfo[allowedEvents.Count];
        ConcurrentBag<ToolError> localWarnings = [];

        Parallel.For(0, allowedEvents.Count, i =>
        {
            AllowedEventInfo ev = allowedEvents[i];
            List<ToolError> threadLocalWarnings = [];
            results[i] = BuildSingleEventMethodGroup(
                ev, eventDirMap, eventScriptsByEvent, eventMethodMetas,
                deviceTypeId, asyncNames, globalVarTypes, actTargetMap,
                methodDisplayNameMap, devicePath, threadLocalWarnings);
            foreach (ToolError w in threadLocalWarnings)
            {
                localWarnings.Add(w);
            }
        });

        warnings.AddRange(localWarnings);
        return results.ToList();
    }

    private static List<EventMethodGroupInfo> BuildEventMethodInfosSerial(
        IReadOnlyList<AllowedEventInfo> allowedEvents,
        Dictionary<string, EventDir> eventDirMap,
        Dictionary<string, IReadOnlyList<ScriptEntry>> eventScriptsByEvent,
        Dictionary<string, List<(ScriptEntry Script, string MethodName)>> eventMethodMetas,
        int deviceTypeId,
        IReadOnlySet<string> asyncNames,
        IReadOnlyDictionary<string, BescriptVarType> globalVarTypes,
        ActTargetMap actTargetMap,
        IReadOnlyDictionary<string, string> methodDisplayNameMap,
        string devicePath,
        List<ToolError> warnings)
    {
        List<EventMethodGroupInfo> result = new(allowedEvents.Count);
        foreach (AllowedEventInfo ev in allowedEvents)
        {
            result.Add(BuildSingleEventMethodGroup(
                ev, eventDirMap, eventScriptsByEvent, eventMethodMetas,
                deviceTypeId, asyncNames, globalVarTypes, actTargetMap,
                methodDisplayNameMap, devicePath, warnings));
        }
        return result;
    }

    private static EventMethodGroupInfo BuildSingleEventMethodGroup(
        AllowedEventInfo ev,
        Dictionary<string, EventDir> eventDirMap,
        Dictionary<string, IReadOnlyList<ScriptEntry>> eventScriptsByEvent,
        Dictionary<string, List<(ScriptEntry Script, string MethodName)>> eventMethodMetas,
        int deviceTypeId,
        IReadOnlySet<string> asyncNames,
        IReadOnlyDictionary<string, BescriptVarType> globalVarTypes,
        ActTargetMap actTargetMap,
        IReadOnlyDictionary<string, string> methodDisplayNameMap,
        string devicePath,
        List<ToolError> warnings)
    {
        EventParamSpec[] eventParams = Array.Empty<EventParamSpec>();
        if (eventDirMap.TryGetValue(ev.EventName, out EventDir? eventDir))
        {
            (EventSignature? eventSignature, string ownerType) = ResolveEventSignature(deviceTypeId, eventDir.Directory, eventDir.EventName);
            if (eventSignature is null)
            {
                throw new InvalidOperationException(
                    $"事件签名解析失败: {devicePath} {eventDir.EventName} (ownerType: {ownerType})");
            }
            eventParams = BuildEventParamSpecs(eventSignature).ToArray();
        }

        EventParamInfo[] eventParamInfos = eventParams
            .Select(p => new EventParamInfo(p.TsName, p.BeTypeName, p.RawTypeName, p.Description))
            .ToArray();

        IReadOnlyList<ScriptEntry>? scriptsForEvent = null;
        if (eventScriptsByEvent.TryGetValue(ev.EventName, out IReadOnlyList<ScriptEntry>? es))
            scriptsForEvent = es;
        List<(ScriptEntry Script, string MethodName)> methodInfos = [];
        if (eventMethodMetas.TryGetValue(ev.EventName, out List<(ScriptEntry Script, string MethodName)>? metas))
            methodInfos = metas;

        List<ScriptMethodInfo> eventScripts = [];
        if (scriptsForEvent is not null && scriptsForEvent.Count > 0 && methodInfos.Count > 0)
        {
            // 事件脚本数量较多时并行化
            if (scriptsForEvent.Count > 2)
            {
                ScriptMethodInfo[] scriptResults = new ScriptMethodInfo[Math.Min(scriptsForEvent.Count, methodInfos.Count)];
                ConcurrentBag<ToolError> localWarnings = [];

                Parallel.For(0, scriptResults.Length, j =>
                {
                    ScriptEntry script = scriptsForEvent[j];
                    string methodName = methodInfos[j].MethodName;
                    List<ToolError> threadLocalWarnings = [];
                    scriptResults[j] = BuildEventScriptFileInfo(
                        script,
                        methodName,
                        ev.EventName,
                        eventParams,
                        asyncNames,
                        globalVarTypes,
                        actTargetMap,
                        methodDisplayNameMap,
                        threadLocalWarnings);
                    foreach (ToolError w in threadLocalWarnings)
                    {
                        localWarnings.Add(w);
                    }
                });

                foreach (ToolError w in localWarnings)
                {
                    warnings.Add(w);
                }
                eventScripts.AddRange(scriptResults);
            }
            else
            {
                for (int j = 0; j < scriptsForEvent.Count && j < methodInfos.Count; j++)
                {
                    ScriptEntry script = scriptsForEvent[j];
                    string methodName = methodInfos[j].MethodName;
                    eventScripts.Add(BuildEventScriptFileInfo(
                        script,
                        methodName,
                        ev.EventName,
                        eventParams,
                        asyncNames,
                        globalVarTypes,
                        actTargetMap,
                        methodDisplayNameMap,
                        warnings));
                }
            }
        }

        // BirthBuilding：注册键使用注入目标核心事件名，写回时再映射回机械产生器存储事件名。
        if (deviceTypeId == DeviceTypeIds.BirthBuilding &&
            BirthBuildingEventMap.ToCoreName.TryGetValue(ev.EventName, out string? coreName))
        {
            return new EventMethodGroupInfo(coreName, coreName, eventParamInfos, eventScripts, ev.Signature.Summary);
        }

        return new EventMethodGroupInfo(ev.EventName, ev.EventName, eventParamInfos, eventScripts, ev.Signature.Summary);
    }

    /// <summary>
    /// 从 DeviceEntity 获取脚本列表（替代 ReadScriptList 的文件读取）。
    /// </summary>
    private static IReadOnlyList<ScriptEntry> GetScriptEntriesFromDeviceEntity(
        DeviceEntityBase device,
        IReadOnlyDictionary<string, ScriptFileInfo> codeRepoGuidMap,
        List<ToolError> warnings,
        ScriptAstCache? cache = null)
    {
        List<ScriptEntry> result = [];

        foreach (ScriptGroupEntityBase scriptGroup in device.ScriptGroups)
        {
            if (!string.IsNullOrEmpty(scriptGroup.EventName))
            {
                continue;
            }

            result.AddRange(BuildScriptEntriesFromScriptGroup(
                device,
                scriptGroup,
                codeRepoGuidMap,
                warnings,
                eventNameForWarning: null,
                skipBlankInlineSource: true,
                cache: cache));
        }

        return result;
    }

    private static IReadOnlyList<ScriptEntry> GetEventScriptEntriesFromDeviceEntity(
        DeviceEntityBase device,
        string eventName,
        IReadOnlyDictionary<string, ScriptFileInfo> codeRepoGuidMap,
        List<ToolError> warnings,
        ScriptAstCache? cache = null)
    {
        List<ScriptEntry> result = [];

        foreach (ScriptGroupEntityBase scriptGroup in device.ScriptGroups)
        {
            if (!string.Equals(scriptGroup.EventName, eventName, StringComparison.Ordinal))
            {
                continue;
            }

            result.AddRange(BuildScriptEntriesFromScriptGroup(
                device,
                scriptGroup,
                codeRepoGuidMap,
                warnings,
                eventNameForWarning: eventName,
                skipBlankInlineSource: true,
                cache: cache));
        }

        return result;
    }

    private static IReadOnlyList<DeviceGlobalVar> BuildGlobalVarsFromEntity(DeviceEntity device)
    {
        if (device.Vars.Variables.Count == 0)
        {
            return Array.Empty<DeviceGlobalVar>();
        }

        List<DeviceGlobalVar> result = [];
        foreach (VarInfo v in device.Vars.Variables)
        {
            BescriptVarType varType = ParseVarType(v.TypeName);
            result.Add(new DeviceGlobalVar(v.Name, varType, IsGlobal: true, v.Comment));
        }

        return result;
    }

    /// <summary>
    /// 解析变量类型字符串为 BescriptVarType。
    /// </summary>
    private static BescriptVarType ParseVarType(string typeName)
    {
        // 使用 BescriptVarTypeHelper.TryParse 尝试匹配标准类型名
        if (BescriptVarTypeHelper.TryParse(typeName) is { } type)
        {
            return type;
        }

        // 未知类型：抛出错误，禁止 fallback
        throw new InvalidOperationException($"未知的变量类型: {typeName}");
    }
}
