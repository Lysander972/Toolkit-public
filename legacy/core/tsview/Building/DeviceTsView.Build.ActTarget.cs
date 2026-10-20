using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

public sealed partial class DeviceTsView
{
    // ╔════════════════════════════════════════════════════════════════════════════════╗
    // ║                    机械产生器(1110)作用域隔离规则                                  ║
    // ╠════════════════════════════════════════════════════════════════════════════════╣
    // ║                                                                                ║
    // ║  1110 有两种完全不同的视角，必须严格隔离：                                          ║
    // ║                                                                                ║
    // ║  【外部视角】地图内其他零件看到的 1110                                             ║
    // ║    - 1110 是地图上的一个零件，可被地图内其他零件调用                                ║
    // ║    - 只能调用 1110 类型的固有 Act，不能调用其自定义脚本                             ║
    // ║    - 作用域：地图                                                               ║
    // ║                                                                                ║
    // ║  【内部视角】1110 内部逻辑的执行上下文                                             ║
    // ║    - 1110 内部完全把自己当作目标机械的核心                                         ║
    // ║    - this 指向目标机械核心，可调用目标机械核心的所有方法                            ║
    // ║    - 内部 Act 目标来源只允许目标机械文件，或当前 1110 对应的独立虚拟机械               ║
    // ║    - 绝对禁止使用地图作用域！                                                     ║
    // ║    - 禁止把地图内其他零件假冒成 1110 内部特殊 Act 调用目标来源                     ║
    // ║                                                                                ║
    // ║  本文件处理的是【内部视角】的 ActTargetMap 构建。                                  ║
    // ║  调用方必须保证：对于 1110 类型，birthBuildingMech 和 scopeDir 正确设置。              ║
    // ║                                                                                ║
    // ╚════════════════════════════════════════════════════════════════════════════════╝

    /// <summary>
    /// 使用缓存构建 ActTargetMap（由 DeviceConversionContext.Build 调用）。
    /// </summary>
    /// <remarks>
    /// 对于机械产生器(1110)，birthBuildingMech 为目标机械实体，scopeDir 为作用域路径。
    /// 1110 内部视角的 Act 目标来源只能是目标机械文件或当前 1110 对应的独立虚拟机械，绝对禁止使用地图零件。
    /// </remarks>
    private static ActTargetMap BuildActTargetMapWithCache(
        ProjectModel model,
        string outerDir,
        string deviceRelativePath,
        DeviceEntityBase currentDevice,
        DeviceMetadata currentMeta,
        MechEntity? birthBuildingMech,
        string scopeDir,
        IReadOnlyDictionary<string, ScriptFileInfo> codeRepoGuidMap,
        List<ToolError> warnings)
    {
        // ═══════════════════════════════════════════════════════════════════
        // 作用域确定（严格隔离，禁止混淆）
        // ═══════════════════════════════════════════════════════════════════
        bool isBirthBuilding = currentMeta.Type == DeviceTypeIds.BirthBuilding;

        // 直接构建基础数据
        Dictionary<int, ActTargetInfo> baseById = [];
        Dictionary<string, ActTargetInfo> baseByName = new(StringComparer.Ordinal);

        // 1110 内部 ActTargetMap 来源要求：
        // 1. 有目标机械文件时，只读取目标机械文件内的零件。
        // 2. 无目标机械文件时，只使用当前 1110 对应的独立虚拟机械。
        // 禁止读取 outerDir 地图零件；地图内其他零件绝对不是 1110 内部特殊 Act 调用目标来源。
        if (isBirthBuilding)
        {
            if (birthBuildingMech is not null)
            {
                AppendActTargetsFromMechEntity(model, birthBuildingMech, baseById, baseByName, codeRepoGuidMap, warnings);
            }
            else
            {
                AppendActTargetsFromRoots(model, scopeDir, baseById, baseByName, overwriteExisting: false, codeRepoGuidMap, warnings);
            }
        }
        else
        {
            // 非 1110 → scopeDir 是地图或机械的派生目录
            AppendActTargetsFromRoots(model, scopeDir, baseById, baseByName, overwriteExisting: false, codeRepoGuidMap, warnings);
        }
        ActTargetMapBase baseData = new(baseById, baseByName);

        // ═══════════════════════════════════════════════════════════════════
        // 命名空间前缀处理
        // ═══════════════════════════════════════════════════════════════════
        IReadOnlyDictionary<int, ActTargetInfo> byId;
        IReadOnlyDictionary<string, ActTargetInfo> byName;

        if (isBirthBuilding)
        {
            // 1110 内部视角：目标机械零件的类型名称需要加上目标机械命名空间前缀
            // 因为 1110 自身物理位置在地图，但内部逻辑上下文是目标机械
            // 独立虚拟机械目录不在模型中，无需添加命名空间前缀
            Dictionary<int, ActTargetInfo> byIdCopy = new(baseData.ById);
            Dictionary<string, ActTargetInfo> byNameCopy = new(baseData.ByName, StringComparer.Ordinal);
            if (birthBuildingMech is not null)
            {
                string targetMechNamespace = "Mech_" + birthBuildingMech.Guid;
                QualifyActTargetTypeNames(targetMechNamespace, byIdCopy, byNameCopy);
            }
            byId = byIdCopy;
            byName = byNameCopy;
        }
        else
        {
            // 非 1110 类型：直接使用基础数据
            byId = baseData.ById;
            byName = baseData.ByName;
        }

        // ActTargetMap 的来源必须已经由上面的作用域规则确定；空表不是可恢复状态。
        // 1110 来源只能是目标机械文件或当前 1110 对应的独立虚拟机械；非 1110 来源只能是 scopeDir 对应的地图/机械。
        // 正常情况下地图和机械都不可能是0零件,不会触发此路径
        if (byId.Count == 0)
        {
            throw new InvalidOperationException(
                $"ActTargetMap 构建失败：作用域内完全找不到任何零件。scopeDir={scopeDir}，" +
                $"outerDir={outerDir}，deviceRelativePath={deviceRelativePath}，" +
                $"isBirthBuilding={isBirthBuilding}。");
        }

        // ═══════════════════════════════════════════════════════════════════
        // 1110 内部视角的 Self 处理
        // ═══════════════════════════════════════════════════════════════════
        // 1110 内部 Self 身份来源唯一是 byId[0]：
        // - 有目标机械文件：目标机械核心零件
        // - 无目标机械文件：当前 1110 对应的独立虚拟机械核心零件
        //
        // 当前地图上的 1110 零件只提供：
        // - 虚拟核心类名
        // - 自身脚本成员
        // - 全局变量声明
        //
        // this 指向目标机械核心；可调用方法 = 自身脚本 ∪ 目标机械核心的所有方法。
        // 与目标机械核心在语义上是同一实体(具体规则是创建时1110的全局变量和脚本会注入机械实例)，因此可访问私有方法。
        // 这里不涉及地图作用域，1110 内部看不到地图其他零件。
        // 地图其他零件调用 1110 属于【外部视角】，由其他代码路径处理。
        //
        // 它绝不是 Act 目标身份来源(其内部视角为另一个机械上的虚拟的零件,和地图都不在一个机械对象上根本没法串通!!!!)。绝对禁止把地图内同号、同类型或其他零件混入这里。
        // 非 self 的内部 Act 目标也只来自同一个 byId/byName，来源仍是目标机械文件或当前 1110 对应的独立虚拟机械。
        //
        // 【私有方法穿透规则】
        // 1. 生成（BES -> TS）：完全支持穿透，合并所有方法
        // 2. TS 语法：天然产生 lint 错误，提醒 Agent 不要继续穿透
        // 3. 写回检测：视为错误但不阻断
        // 4. 写回映射（TS -> BES）：完全支持穿透
        // ═══════════════════════════════════════════════════════════════════
        ActTargetInfo selfInfo;
        if (isBirthBuilding)
        {
            // 故意直接索引目标核心：缺少 ID=0 表示目标机械/独立虚拟机械构建错误，不能改读地图零件。
            ActTargetInfo coreInfo = byId[DeviceTypeIds.MapCoreDevice];
            string virtualCoreTypeName = BuildDeviceTsViewClassName(currentDevice) + VirtualCoreSuffix;
            (IReadOnlyDictionary<string, string> rawToMethod,
                IReadOnlyDictionary<string, string> methodToRaw,
                IReadOnlyDictionary<string, string> rawToReturn,
                IReadOnlySet<string> privateRawNames,
                IReadOnlySet<string> asyncRawNames,
                IReadOnlySet<string> asyncWithReturnRawNames) =
                BuildScriptActMethodMaps(model, deviceRelativePath, codeRepoGuidMap, warnings);

            Dictionary<string, string> mergedRawToMethod = new(rawToMethod, StringComparer.Ordinal);
            Dictionary<string, string> mergedMethodToRaw = new(methodToRaw, StringComparer.Ordinal);
            Dictionary<string, string> mergedRawToReturn = new(rawToReturn, StringComparer.Ordinal);
            HashSet<string> mergedPrivateRawNames = new(privateRawNames, StringComparer.Ordinal);

            foreach ((string rawName, string methodName) in coreInfo.ScriptRawNameToMethodName)
            {
                if (!mergedRawToMethod.ContainsKey(rawName))
                {
                    mergedRawToMethod[rawName] = methodName;
                }
            }
            foreach ((string methodName, string rawName) in coreInfo.ScriptMethodNameToRawName)
            {
                if (!mergedMethodToRaw.ContainsKey(methodName))
                {
                    mergedMethodToRaw[methodName] = rawName;
                }
            }
            foreach ((string rawName, string returnType) in coreInfo.ScriptRawNameToReturnType)
            {
                if (!mergedRawToReturn.ContainsKey(rawName))
                {
                    mergedRawToReturn[rawName] = returnType;
                }
            }

            selfInfo = coreInfo with
            {
                TsTypeName = virtualCoreTypeName,
                ActionOwnerTypeName = "CoreDevice",
                ScriptRawNameToMethodName = mergedRawToMethod,
                ScriptMethodNameToRawName = mergedMethodToRaw,
                ScriptRawNameToReturnType = mergedRawToReturn,
                PrivateScriptRawNames = mergedPrivateRawNames,
                AsyncRawNames = asyncRawNames,
                AsyncWithReturnRawNames = asyncWithReturnRawNames
            };
        }
        else
        {
            selfInfo = byId[currentMeta.DeviceId];
        }

        // 构建类名→ActTargetInfo 的索引；ID 来源唯一是文件名/DeviceId。
        Dictionary<string, ActTargetInfo> byClassName = new(StringComparer.Ordinal);
        foreach (ActTargetInfo info in byId.Values)
        {
            byClassName[info.TsTypeName] = info;
        }
        byClassName[selfInfo.TsTypeName] = selfInfo;

        return new ActTargetMap(selfInfo, byName, byId, byClassName);
    }

    private static void AppendActTargetsFromRoots(
        ProjectModel model,
        string scopeDir,
        IDictionary<int, ActTargetInfo> byId,
        IDictionary<string, ActTargetInfo> byName,
        bool overwriteExisting,
        IReadOnlyDictionary<string, ScriptFileInfo> codeRepoGuidMap,
        List<ToolError> warnings)
    {
        // 独立虚拟机械目录不在模型中，创建核心零件（ID=0）供脚本调用
        if (scopeDir.StartsWith(".virtual_birthbuilding/", StringComparison.Ordinal))
        {
            // 创建虚拟的核心零件元数据
            // ID=0 是核心零件/主机方块，与真实机械的核心零件 ID 一致
            DeviceMetadata virtualCoreMeta = new(
                DeviceId: DeviceTypeIds.MapCoreDevice,
                Type: DeviceTypeIds.CoreDevice,
                LogicName: null,
                MechFileGuid: null);

            string virtualDeviceDir = scopeDir + "/0";  // 核心零件目录是 /0
            // 空 GUID 的 1110 独立虚拟机械核心只能使用已声明的机械核心类型别名（DeviceType_1），
            // 绝对不能引用不存在的 "CoreDevice" 类名。
            string virtualTsTypeName = BescriptDtsGenerator.BuildDeviceTypeTypeName(DeviceTypeIds.CoreDevice);

            ActTargetInfo info = BuildActTargetInfo(model, virtualDeviceDir, DeviceTypeIds.MapCoreDevice, virtualCoreMeta, virtualTsTypeName, codeRepoGuidMap, warnings);
            AddActTargetInfo(info, byId, byName, overwriteExisting);
            return;
        }

        // 确定载体零件源：Map 和 Mech 有且仅有一个非空
        IReadOnlyList<DeviceEntityBase> devices;

        // 判断是否为地图目录（直接遍历所有地图，因为派生目录路径与 map 文件路径不同）
        MapEntity? map = model.ListMaps().FirstOrDefault(m => m.派生目录_完整相对路径.Equals(scopeDir, StringComparison.Ordinal));
        if (map is not null)
        {
            devices = map.Devices;
        }
        else
        {
            // 判断是否为机械目录
            MechEntity? mechEntity = model.ListMechs()
                .FirstOrDefault(m => m.派生目录_完整相对路径.Equals(scopeDir, StringComparison.Ordinal));
            if (mechEntity is not null)
            {
                devices = mechEntity.Devices;
            }
            else
            {
                throw new InvalidOperationException($"scopeDir 既不是地图目录也不是机械目录: {scopeDir}");
            }
        }

        // 直接遍历所有零件实体，不依赖文件系统扫描
        // ID=0 是核心零件或主机方块，绝对不可以跳过！
        foreach (DeviceEntityBase device in devices)
        {
            int deviceId = device.DeviceId;
            string deviceDir = RelativePathUtils.Combine(scopeDir, deviceId.ToString());
            DeviceMetadata meta = BuildDeviceMetadataFromEntity(device);
            string tsTypeName = BuildDeviceTsViewClassName(device);
            ActTargetInfo info = BuildActTargetInfo(model, deviceDir, deviceId, meta, tsTypeName, codeRepoGuidMap, warnings);
            AddActTargetInfo(info, byId, byName, overwriteExisting);
        }
    }

    /// <summary>
    /// 从已知的机械实体直接添加零件到 ActTargetMap（无需通过路径查找）。
    /// 专用于 1110 内部视角有真实机械文件的情况。
    /// </summary>
    private static void AppendActTargetsFromMechEntity(
        ProjectModel model,
        MechEntity mechEntity,
        IDictionary<int, ActTargetInfo> byId,
        IDictionary<string, ActTargetInfo> byName,
        IReadOnlyDictionary<string, ScriptFileInfo> codeRepoGuidMap,
        List<ToolError> warnings)
    {
        // 直接使用实体的派生目录路径，不需要外部传入
        string scopeDir = mechEntity.派生目录_完整相对路径;

        // 直接遍历机械实体的所有零件，不依赖路径查找
        // ID=0 是核心零件或主机方块，绝对不可以跳过！
        foreach (DeviceEntityBase device in mechEntity.Devices)
        {
            int deviceId = device.DeviceId;
            string deviceDir = RelativePathUtils.Combine(scopeDir, deviceId.ToString());
            DeviceMetadata meta = BuildDeviceMetadataFromEntity(device);
            string tsTypeName = BuildDeviceTsViewClassName(device);
            ActTargetInfo info = BuildActTargetInfo(model, deviceDir, deviceId, meta, tsTypeName, codeRepoGuidMap, warnings);
            AddActTargetInfo(info, byId, byName, overwriteExisting: false);
        }
    }

    /// <summary>
    /// 从 DeviceEntity 构建 DeviceMetadata（替代 DeviceEntity.ReadMetadata）。
    /// TS视图类名统一由 <see cref="BuildDeviceTsViewClassName"/> 生成，不分裂到实体层。
    /// </summary>
    private static DeviceMetadata BuildDeviceMetadataFromEntity(DeviceEntityBase device)
    {
        return new DeviceMetadata(
            DeviceId: device.DeviceId,
            Type: device.DeviceType,
            LogicName: device.LogicName,
            MechFileGuid: device.MechFileGuid
        );
    }

    private static (
        IReadOnlyDictionary<string, string> RawToMethod,
        IReadOnlyDictionary<string, string> MethodToRaw,
        IReadOnlyDictionary<string, string> RawToReturnType,
        IReadOnlySet<string> PrivateRawNames,
        IReadOnlySet<string> AsyncRawNames,
        IReadOnlySet<string> AsyncWithReturnRawNames)
        BuildScriptActMethodMaps(
            ProjectModel model,
            string deviceDir,
            IReadOnlyDictionary<string, ScriptFileInfo> codeRepoGuidMap,
            List<ToolError> warnings)
    {
        if (model.Find<DeviceEntity>(deviceDir) is not DeviceEntity device)
        {
            // 独立虚拟机械目录的核心零件不在模型中，返回空映射
            if (deviceDir.StartsWith(".virtual_birthbuilding/", StringComparison.Ordinal))
            {
                return (
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal));
            }
            throw new InvalidOperationException($"零件未加载，无法读取脚本列表: {deviceDir}");
        }

        IReadOnlyList<ScriptEntry> scripts = GetScriptEntriesFromDeviceEntity(device, codeRepoGuidMap, warnings);
        ScriptEntry[] allScripts = scripts.ToArray();

        if (allScripts.Length == 0)
        {
            return (
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));
        }

        HashSet<string> usedMethodNames = new(StringComparer.Ordinal);
        Dictionary<string, string> rawToMethod = new(StringComparer.Ordinal);
        Dictionary<string, string> methodToRaw = new(StringComparer.Ordinal);
        Dictionary<string, string> rawToReturnType = new(StringComparer.Ordinal);
        HashSet<string> privateRawNames = new(StringComparer.Ordinal);
        foreach (ScriptEntry? script in allScripts)
        {
            string methodName = BuildUniqueMethodName(script.Name, usedMethodNames);
            rawToMethod[script.Name] = methodName;
            methodToRaw[methodName] = script.Name;

            BescriptHeader header = script.ScriptInfo.Header;
            if (header.Private || script.IsPrivateFromList)
            {
                privateRawNames.Add(script.Name);
            }

            TsViewScriptReturnInfo returnInfo = TsViewReturnInfoBuilder.Build(header.ReturnParameter);
            // 只记录有实际返回值的脚本（void 不算，不能进 asyncWithReturnRawNames）
            if (returnInfo.HasReturn)
            {
                rawToReturnType[script.Name] = returnInfo.ReturnType;
            }
        }

        // async+return 脚本：async 且有实际返回值（rawToReturnType 只含 HasReturn 的脚本）
        // 仅这类调用需要 await，async+void（FireAndForget）不需要
        TsViewScriptAsyncInput[] asyncInputs = allScripts
            .Select(static s => new TsViewScriptAsyncInput(s.Name, s.SourcePath, s.ScriptInfo))
            .ToArray();
        HashSet<string> asyncNames = TsViewScriptAsyncResolver.ResolveAsyncScripts(asyncInputs);
        HashSet<string> asyncWithReturnRawNames = new(StringComparer.Ordinal);
        foreach (string name in asyncNames)
        {
            if (rawToReturnType.ContainsKey(name))
                asyncWithReturnRawNames.Add(name);
        }

        return (rawToMethod, methodToRaw, rawToReturnType, privateRawNames, asyncNames, asyncWithReturnRawNames);
    }

    private static ActTargetInfo BuildActTargetInfo(
        ProjectModel model,
        string deviceDir,
        int deviceId,
        DeviceMetadata meta,
        string tsTypeName,
        IReadOnlyDictionary<string, ScriptFileInfo> codeRepoGuidMap,
        List<ToolError> warnings)
    {
        // 原版游戏行为：零件逻辑别名允许为空。
        // 当别名为空时，device.json 不存储 ext.logicName 字段，读取时值为 null。
        // Device.LogicName 已统一归一化为 null（空串即无逻辑名），此处直接透传。
        string? logicName = meta.LogicName;
        int typeId = meta.Type;
        (IReadOnlyDictionary<string, string> rawToMethod, IReadOnlyDictionary<string, string> methodToRaw, IReadOnlyDictionary<string, string> rawToReturn, IReadOnlySet<string> privateRawNames, IReadOnlySet<string> asyncRawNames, IReadOnlySet<string> asyncWithReturnRawNames) = BuildScriptActMethodMaps(model, deviceDir, codeRepoGuidMap, warnings);

        return new ActTargetInfo(
            DeviceId: deviceId,
            DeviceTypeId: typeId,
            LogicName: logicName,
            TsTypeName: tsTypeName,
            ActionOwnerTypeName: DeviceTypeIds.GetActionOwnerTypeName(typeId),
            ScriptRawNameToMethodName: rawToMethod,
            ScriptMethodNameToRawName: methodToRaw,
            ScriptRawNameToReturnType: rawToReturn,
            PrivateScriptRawNames: privateRawNames,
            AsyncRawNames: asyncRawNames,
            AsyncWithReturnRawNames: asyncWithReturnRawNames);
    }

    private static void AddActTargetInfo(
        ActTargetInfo info,
        IDictionary<int, ActTargetInfo> byId,
        IDictionary<string, ActTargetInfo> byName,
        bool overwriteExisting)
    {
        if (overwriteExisting || !byId.ContainsKey(info.DeviceId))
        {
            byId[info.DeviceId] = info;
        }

        if (string.IsNullOrWhiteSpace(info.LogicName))
        {
            return;
        }

        if (overwriteExisting)
        {
            byName[info.LogicName] = info;
            return;
        }

        if (!byName.TryGetValue(info.LogicName, out ActTargetInfo? existing) || info.DeviceId < existing.DeviceId)
        {
            byName[info.LogicName] = info;
        }
    }

}
