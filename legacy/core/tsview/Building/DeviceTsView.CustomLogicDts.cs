using System.Text.Json;

using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

public sealed partial class DeviceTsView
{
    private static readonly JsonSerializerOptions CustomLogicDtsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static string BuildCustomLogicDts(ProjectModel model, ScriptAstCache? cache = null)
    {
        List<ToolError> warnings = [];
        (Dictionary<string, ScriptFileInfo> codeRepoGuidMap, HashSet<string> codeRepoAsyncNames) = GetCodeRepoGuidMap(model, cache, warnings);
        List<CustomLogicDeviceEntry> entries = [];
        HashSet<int> knownTypeIds = new(DeviceTypeIds.GetAllTypeIds());

        // 通过模型统一查询所有零件，禁止直接文件扫描
        // Offensive: 如果 ProjectModel 未正确加载，直接崩溃而不是生成空文件
        int mapCount = model.ListMaps().Count;
        int mechCount = model.ListMechs().Count;
        if (mapCount == 0 && mechCount == 0)
        {
            throw new InvalidOperationException(
                "ProjectModel 未加载：ListMaps() 和 ListMechs() 都返回空。" +
                "确保 Host 在传入 Model 前已调用 FullScan() 或 LoadMaps()。");
        }

        // 预收集全工程 AsyncWithReturnRawNames（用于跨零件 Act 调用的 async 传染）
        HashSet<string> globalExternalAsyncWithReturn = new(StringComparer.Ordinal);
        foreach (DeviceEntityBase pre in model.EnumerateAllDevices())
        {
            int preTypeId = pre.DeviceType;
            if (!knownTypeIds.Contains(preTypeId) || preTypeId == DeviceTypeIds.BirthBuilding)
                continue;
            IReadOnlyList<ScriptEntry> preScripts = GetScriptEntriesFromDeviceEntity(pre, codeRepoGuidMap, warnings, cache);
            TsViewScriptAsyncInput[] preInputs = preScripts
                .Select(s => new TsViewScriptAsyncInput(s.Name, s.SourcePath, s.ScriptInfo))
                .ToArray();
            HashSet<string> preAsync = TsViewScriptAsyncResolver.ResolveAsyncScripts(preInputs);
            foreach (ScriptEntry ps in preScripts)
                if (preAsync.Contains(ps.Name) && ps.ScriptInfo.Header.ReturnParameter is { Name: { Length: > 0 } })
                    globalExternalAsyncWithReturn.Add(ps.Name);
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (DeviceEntityBase device in model.EnumerateAllDevices())
        {
            string deviceNs = BuildDeviceTsViewClassName(device);

            // 从 device 实体获取类型ID
            int typeId = device.DeviceType;

            if (!knownTypeIds.Contains(typeId))
            {
                // Offensive: 记录无效零件，帮助诊断问题
                warnings.Add(new ToolError(
                    device.device目录_完整相对路径,
                    null,
                    null,
                    $"零件({device.DeviceId}/{device.LogicName})被跳过: 未知的 type ID: {typeId}",
                    ToolDiagnosticSeverity.Error));
                continue;
            }

            // 零件实体本身包含载体信息
            string outerNs = device.CarrierKind + "_" + device.CarrierGuid;

            // 机械产生器(1110)：仅生成外壳声明
            if (typeId == DeviceTypeIds.BirthBuilding)
            {
                string birthKey = outerNs + "/" + deviceNs;
                if (!seen.Add(birthKey))
                {
                    continue;
                }
                string birthBuildingTypeName = BescriptDtsGenerator.BuildDeviceTypeTypeName(DeviceTypeIds.BirthBuilding);
                entries.Add(new CustomLogicDeviceEntry(outerNs, deviceNs, birthBuildingTypeName, Array.Empty<ScriptEntry>(), new HashSet<string>(StringComparer.Ordinal), ResolveCapabilityNames(device)));
                continue;
            }

            string deviceTypeName = BescriptDtsGenerator.BuildDeviceTypeTypeName(typeId);

            // 通过零件实体获取脚本列表
            IReadOnlyList<ScriptEntry> scripts = GetScriptEntriesFromDeviceEntity(device, codeRepoGuidMap, warnings, cache);

            // asyncNames 计算需要完整脚本集合（含事件脚本）才能正确传播，但最终只展示普通脚本
            List<TsViewScriptAsyncInput> asyncInputs = [];
            foreach (ScriptEntry s in scripts)
                asyncInputs.Add(new TsViewScriptAsyncInput(s.Name, s.SourcePath, s.ScriptInfo));
            foreach (ScriptGroupEntityBase sg in device.ScriptGroups)
            {
                if (sg.EventName == null) continue;
                foreach (ScriptEntry s in BuildScriptEntriesFromScriptGroup(device, sg, codeRepoGuidMap, warnings, sg.EventName, skipBlankInlineSource: true, cache: cache))
                    asyncInputs.Add(new TsViewScriptAsyncInput(s.Name, s.SourcePath, s.ScriptInfo));
            }
            HashSet<string> seedAsyncNames = scripts
                .Where(s => s.IsCodeRepo && codeRepoAsyncNames.Contains(s.Name))
                .Select(s => s.Name)
                .ToHashSet(StringComparer.Ordinal);
            HashSet<string> seedAsyncNamesWithReturn = new(StringComparer.Ordinal);
            foreach (ScriptEntry s in scripts)
            {
                if (s.IsCodeRepo && codeRepoAsyncNames.Contains(s.Name) &&
                    codeRepoGuidMap.TryGetValue(s.Guid, out ScriptFileInfo? info) &&
                    info.ScriptInfo.Header.ReturnParameter is { Name: { Length: > 0 } })
                {
                    seedAsyncNamesWithReturn.Add(s.Name);
                }
            }
            HashSet<string> deviceAsyncNames = TsViewScriptAsyncResolver.ResolveAsyncScripts(asyncInputs, seedAsyncNames, seedAsyncNamesWithReturn, globalExternalAsyncWithReturn);

            ScriptEntry[] publicScripts = scripts
                .Where(s => !s.ScriptInfo.Header.Private)
                .ToArray();

            string key = outerNs + "/" + deviceNs;
            if (!seen.Add(key))
            {
                continue;
            }

            entries.Add(new CustomLogicDeviceEntry(outerNs, deviceNs, deviceTypeName, publicScripts, deviceAsyncNames, ResolveCapabilityNames(device)));
        }

        // Offensive: 如果遍历完所有零件后 entries 为空，说明有问题
        if (entries.Count == 0)
        {
            // 收集所有错误：包括生成过程中的警告和模型加载错误
            List<string> allErrors = [.. warnings.Select(w => $"  - [{w.Severity}] {w.Path}: {w.Message}")];

            // 包含模型加载阶段的错误（关键！）
            foreach (ToolError loadError in model.LoadErrors.Errors)
            {
                allErrors.Add($"  - [{loadError.Severity}] {loadError.Path}: {loadError.Message}");
            }

            string errorDetails = "没有收集到具体错误（检查零件目录结构或 device.json 是否存在）";
            if (allErrors.Count > 0)
                errorDetails = string.Join("\n", allErrors);

            throw new InvalidOperationException(
                $"custom-logic.d.ts 生成失败：遍历了 {mapCount} 个地图和 {mechCount} 个机械，但没有找到任何有效零件。\n" +
                $"错误详情:\n{errorDetails}");
        }

        List<CustomLogicDtsNamespace> namespaces = [];
        IGrouping<string, CustomLogicDeviceEntry>[] nsGroups = entries
            .GroupBy(e => e.OuterNamespace, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToArray();
        foreach (IGrouping<string, CustomLogicDeviceEntry> nsGroup in nsGroups)
        {
            List<CustomLogicDtsDevice> devices = [];

            foreach (CustomLogicDeviceEntry device in nsGroup.OrderBy(e => e.DeviceClassName, StringComparer.Ordinal))
            {
                List<CustomLogicDtsMethod> methods = [];
                HashSet<string> usedMethodNames = new(StringComparer.Ordinal);

                foreach (ScriptEntry script in device.PublicScripts)
                {
                    CustomLogicDtsMethod method = BuildCustomLogicMethodSignature(script, device.AsyncNames, warnings, usedMethodNames);
                    methods.Add(method);
                }

                devices.Add(new CustomLogicDtsDevice(
                    device.DeviceClassName,
                    EnsureDeviceTypeName(device.DeviceTypeName),
                    methods,
                    device.Capabilities));
            }

            namespaces.Add(new CustomLogicDtsNamespace(nsGroup.Key, devices));
        }

        CustomLogicDtsInput input = new(namespaces);
        string inputJson = JsonSerializer.Serialize(input, CustomLogicDtsJsonOptions);
        string outputJson = TsCompilerHost.BuildCustomLogicDts(inputJson);
        CustomLogicDtsOutput? output = JsonSerializer.Deserialize<CustomLogicDtsOutput>(outputJson, CustomLogicDtsJsonOptions);
        if (output is null)
        {
            throw new InvalidOperationException("custom-logic.d.ts 生成返回空结果。");
        }
        if (output.Error is not null)
        {
            throw new InvalidOperationException(output.Error);
        }

        return output.DtsText;
    }

    /// <summary>
    /// 统一零件TS视图类名生成规则（TS视图层唯一入口）。
    /// 数据来源：device.LogicName（别名）和 device.DeviceId（目录名）。
    /// 格式：Device_{logicName}_{id}，logicName为空时使用 "anonymous"。
    /// </summary>
    /// <remarks>
    /// 原版游戏行为：零件逻辑别名允许为空字符串。
    /// 当别名为空时，device.json 不存储 ext.logicName 字段，读取时值为 null。
    /// TS视图中使用 "anonymous" 作为回退，生成 Device_anonymous_{id} 格式的类名。
    /// </remarks>
    internal static string BuildDeviceTsViewClassName(DeviceEntityBase device)
    {
        string namePart = "anonymous";
        if (device.LogicName != null)
            namePart = TsViewIdentifierMap.ToTsIdentifier(device.LogicName);
        return "Device_" + namePart + "_" + device.DeviceId;
    }

    /// <summary>
    /// 解析零件实例在当前条件下具备的能力名列表，用于 TS 视图接口化。
    /// 与 DeviceCapabilityResolver 共用能力规则，零件实例 interface 按能力 extends 对应的 I{Capability}。
    /// </summary>
    private static IReadOnlyList<string> ResolveCapabilityNames(DeviceEntityBase device)
    {
        List<string> result = [];
        foreach (DeviceCapability capability in Enum.GetValues<DeviceCapability>())
        {
            if (DeviceCapabilityResolver.HasCapability(device, capability))
                result.Add(capability.ToString());
        }
        return result;
    }

    private static CustomLogicDtsMethod BuildCustomLogicMethodSignature(
        ScriptEntry script,
        IReadOnlySet<string> asyncNames,
        List<ToolError> warnings,
        HashSet<string> usedMethodNames)
    {
        BescriptHeader header = script.ScriptInfo.Header;

        string methodName = BuildUniqueMethodName(script.Name, usedMethodNames);
        IReadOnlyList<TsViewParamSpec> paramSpecs = TsViewParamSpecTools.BuildFromParameters(header.Parameters, warnings, script.SourcePath);
        TsViewScriptReturnInfo returnInfo = TsViewReturnInfoBuilder.Build(header.ReturnParameter);

        CustomLogicDtsParameter[] parameters = paramSpecs
            .Select(static p => new CustomLogicDtsParameter(p.Name, p.TypeName))
            .ToArray();

        bool isAsync = asyncNames.Contains(script.Name);
        return new CustomLogicDtsMethod(methodName, parameters, returnInfo.ReturnType, isAsync, returnInfo.HasReturn);
    }

    private sealed record CustomLogicDeviceEntry(
        string OuterNamespace,
        string DeviceClassName,
        string DeviceTypeName,
        IReadOnlyList<ScriptEntry> PublicScripts,
        IReadOnlySet<string> AsyncNames,
        IReadOnlyList<string> Capabilities
    );

    private sealed record CustomLogicDtsInput(IReadOnlyList<CustomLogicDtsNamespace> Namespaces);

    private sealed record CustomLogicDtsNamespace(
        string Name,
        IReadOnlyList<CustomLogicDtsDevice> Devices);

    private sealed record CustomLogicDtsDevice(
        string Name,
        string BaseTypeName,
        IReadOnlyList<CustomLogicDtsMethod> Methods,
        IReadOnlyList<string> Capabilities);

    private sealed record CustomLogicDtsMethod(
        string Name,
        IReadOnlyList<CustomLogicDtsParameter> Parameters,
        string ReturnTypeName,
        bool IsAsync,
        bool HasReturn);

    private sealed record CustomLogicDtsParameter(
        string Name,
        string TypeName);

    private sealed record CustomLogicDtsOutput(
        string DtsText,
        string? Error);
}
