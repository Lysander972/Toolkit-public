using System.Text;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Sdk;

internal static class BescriptDtsGenerator
{
    private sealed record DtsCallable(
        string Name,
        string? GenericParams,
        string Params,
        string ReturnType,
        string? Doc,
        RecommendLevel Recommend,
        bool IsObsolete,
        string? NotRecommendedReason,
        SdkAsyncMode AsyncMode = SdkAsyncMode.Sync
    );

    private sealed record MemberCompression(
        string MethodName,
        string? GenericBeTypeName
    );

    private sealed record GlobalCompressionResult(
        IReadOnlyList<DtsCallable> Callables,
        Dictionary<string, MemberCompression> CompressionMap,
        Dictionary<string, List<MemberCompressionEntry>> ReverseMap
    );

    private enum ParamSlotRule
    {
        VariableOnly,
        VariableOrConst,
        ConstOnly,
        StringConvertible,
        AnyParam,
        NumberParam,
        CanvasCreateConstOnly,
        IconCreateConstOnly
    }

    /// <summary>
    /// 压缩映射条目，用于 TS 视图调用语义与 BeScript 方法名互转。
    /// <para>
    /// 该结构是双向映射的唯一数据来源：
    /// - Build 阶段：BeScript AST → TS 视图（转义后映射）
    /// - Parse 阶段：TS 视图 → BeScript AST（映射后反转义）
    /// - patch_mech_view 校验：验证 TS 视图方法调用的合法性
    /// </para>
    /// <para>
    /// 映射规则：(泛型类型, 压缩名, 参数数量) → 唯一确定的原始方法名
    /// </para>
    /// </summary>
    internal sealed record MemberCompressionEntry(
        string OriginalName,
        string? GenericBeTypeName,
        int ParameterCount
    );


    private static readonly string[] UiTypes =
    {
        "UILabel",
        "UIButton",
        "UIRect",
        "UIInput",
        "UIRawImage",
        "UISpine",
        "UILine",
        "UICircle",
        "UIAvatar"
    };

    /// <summary>
    /// Be 类型的 JSDoc 描述。
    /// <para>
    /// 复制语义规则：
    /// - 值类型：赋值即复制值
    /// - 引用类型：赋值复制引用
    /// - 容器类型（List/Dict/Struct）：ref()/read*()/get*()返回引用；clone()递归克隆
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> BeTypeDescriptions = new(StringComparer.Ordinal)
    {
        ["Any"] = "@internal 任意类型占位符，仅用于表示空值常量",
        ["Bool"] = "布尔值类型，值类型",
        ["Bytes"] = "字节数组类型，引用类型",
        ["Canvas"] = "UI 布局实例，引用类型",
        ["Color"] = "颜色类型，值类型",
        ["Datetime"] = "日期时间类型，值类型",
        ["Device"] = "零件引用类型",
        ["Dict"] = "字典容器。ref()/read*()返回引用；clone()递归克隆(仅对List/Dict/Struct递归)",
        ["Enum"] = "枚举类型，值类型",
        ["Float"] = "浮点数类型，值类型",
        ["Icon"] = "图标类型，引用类型",
        ["List"] = "列表容器。ref()/read*()返回引用；clone()递归克隆(仅对List/Dict/Struct递归)",
        ["Long"] = "长整数类型，值类型",
        ["Mech"] = "机械引用类型",
        ["Mesh"] = "网格类型，引用类型",
        ["Number"] = "数值类型，值类型",
        ["Player"] = "玩家引用类型",
        ["String"] = "字符串类型，值类型",
        ["Struct"] = "结构体容器。ref()/get*()返回引用；无clone方法",
        ["UI"] = "UI 组件基类，引用类型",
        ["UILabel"] = "UI 文本标签组件，引用类型",
        ["UIButton"] = "UI 按钮组件，引用类型",
        ["UIRect"] = "UI 矩形容器组件，引用类型",
        ["UIInput"] = "UI 输入框组件，引用类型",
        ["UIRawImage"] = "UI 图片组件，引用类型",
        ["UISpine"] = "UI 动画组件，引用类型",
        ["UILine"] = "UI 线条组件，引用类型",
        ["UICircle"] = "UI 圆形组件，引用类型",
        ["UIAvatar"] = "UI 头像组件，引用类型",
        ["Vector3"] = "三维向量类型，值类型"
    };

    private const string DefaultUiGlobalNotRecommendedReason = "该 UI 全局函数不推荐使用";

    private sealed class NamespaceNode
    {
        public readonly SortedDictionary<string, NamespaceNode> Children = new(StringComparer.Ordinal);
        public readonly List<DtsCallable> Functions = [];
    }

    /// <summary>
    /// TS 视图压缩映射表，是 AST 双向转换的唯一映射规则来源。
    /// <para>
    /// 该表在初始化时从 BuiltinMethodRegistry 构建，之后独立使用，不再依赖 Registry 进行映射查询。
    /// </para>
    /// <para>
    /// 使用场景：
    /// - Build 阶段：BeScript AST → TS 视图（ByOriginal 映射）
    /// - Parse 阶段：TS 视图 → BeScript AST（ByCompressed 映射）
    /// - patch_mech_view 校验：验证 TS 视图方法调用的合法性
    /// </para>
    /// </summary>
    private sealed class TsViewCompressionTable
    {
        public readonly Dictionary<string, MemberCompression> GlobalByOriginal = new(StringComparer.Ordinal);
        public readonly Dictionary<string, List<MemberCompressionEntry>> GlobalByCompressed = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Dictionary<string, MemberCompression>> VarFuncByOriginal = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Dictionary<string, List<MemberCompressionEntry>>> VarFuncByCompressed = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Dictionary<string, MemberCompression>> DeviceActionByOriginal = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Dictionary<string, List<MemberCompressionEntry>>> DeviceActionByCompressed = new(StringComparer.Ordinal);
    }

    private static readonly (string Prefix, string Digits)[] DigitEndingTypeTags = Enum.GetNames(typeof(BescriptVarType))
            .Select(SplitTrailingDigits)
            .Where(static x => x.Digits.Length > 0 && x.Prefix.Length > 0)
            .OrderByDescending(static x => x.Digits.Length)
            .ToArray();
    private static readonly Lazy<TsViewCompressionTable> CompressionTable = new(BuildCompressionTable, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly HashSet<BescriptVarType> GlobalVarSupportedTypes = new(Enum
        .GetValues(typeof(BescriptVarType))
        .Cast<BescriptVarType>()
        .Where(BescriptVarTypeHelper.IsSupportedGlobalVarType));

    // 从末尾拆分数字后缀，避免 Vector3 这类类型名被误吞
    private static (bool Success, string BaseName, int Suffix) TrySplitNumericSuffix(string name)
    {
        string baseName = string.Empty;
        int suffix = 0;

        int end = name.Length - 1;
        if (!char.IsDigit(name[end]))
        {
            return (false, baseName, suffix);
        }

        int start = end;
        while (start >= 0 && char.IsDigit(name[start]))
        {
            start--;
        }

        if (start < 0)
        {
            return (false, baseName, suffix);
        }

        baseName = name.Substring(0, start + 1);
        string suffixDigits = name.Substring(start + 1);

        foreach ((string Prefix, string Digits) tag in DigitEndingTypeTags)
        {
            if (!baseName.EndsWith(tag.Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (!suffixDigits.StartsWith(tag.Digits, StringComparison.Ordinal))
            {
                continue;
            }

            if (suffixDigits.Length == tag.Digits.Length)
            {
                return (false, baseName, suffix);
            }

            if (suffixDigits.Length < tag.Digits.Length)
            {
                continue;
            }

            baseName += tag.Digits;
            suffixDigits = suffixDigits.Substring(tag.Digits.Length);
            break;
        }

        bool success = baseName.Length > 0 && int.TryParse(suffixDigits, out suffix);
        return (success, baseName, suffix);
    }

    private static (string Prefix, string Digits) SplitTrailingDigits(string name)
    {
        if (!char.IsDigit(name[^1]))
        {
            return (name, string.Empty);
        }

        int start = name.Length - 1;
        while (start >= 0 && char.IsDigit(name[start]))
        {
            start--;
        }

        if (start < 0 || start == name.Length - 1)
        {
            return (name, string.Empty);
        }

        return (name.Substring(0, start + 1), name.Substring(start + 1));
    }

    // TS/JS 保留字集合（用于标识符转义）
    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal)
    {
        // JS 关键字
        "break", "case", "catch", "class", "const", "continue",
        "debugger", "default", "delete", "do", "else", "enum",
        "export", "extends", "false", "finally", "for", "function",
        "if", "import", "in", "instanceof", "new", "null",
        "return", "super", "switch", "this", "throw", "true",
        "try", "typeof", "var", "void", "while", "with",
        // TS 严格模式保留字
        "as", "implements", "interface", "let", "package",
        "private", "protected", "public", "static", "yield"
    };

    internal static bool IsReservedWord(string name)
    {
        return ReservedWords.Contains(name);
    }

    internal static string Generate()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        // 注意: 生成 TS 视图时，仅包含 Universal 和 InGameOnly 作用域的方法
        // EditorOnly 方法(如 dvf 编辑器函数)在游戏逻辑中不可用，因此跳过
        MethodSignature[] globalFuncs = QueryAllBuiltinMethods(registry, MethodCategory.GlobalFunc)
            .Where(static s => s.Scope != MethodScope.EditorOnly).ToArray();
        MethodSignature[] varFuncs = QueryAllBuiltinMethods(registry, MethodCategory.VarFunc)
            .Where(static s => s.Scope != MethodScope.EditorOnly).ToArray();
        MethodSignature[] deviceActions = QueryAllBuiltinMethods(registry, MethodCategory.DeviceAction)
            .Where(static s => s.Scope != MethodScope.EditorOnly).ToArray();
        HashSet<string> constTypes = CollectConstTypes(globalFuncs, varFuncs, deviceActions);

        string[] allVarTypes = Enum.GetNames(typeof(BescriptVarType))
            .Where(static n => n is not ("Void" or "UI"))
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> allVarTypeSet = new(allVarTypes, StringComparer.Ordinal);

        Dictionary<string, List<MethodSignature>> varfByOwnerAll = new(StringComparer.Ordinal);
        foreach (MethodSignature s in varFuncs)
        {
            if (s.OwnerType is null)
            {
                continue;
            }
            if (!varfByOwnerAll.TryGetValue(s.OwnerType, out List<MethodSignature>? list))
            {
                list = [];
                varfByOwnerAll[s.OwnerType] = list;
            }
            list.Add(s);
        }
        IReadOnlyList<MethodSignature> uiBaseMethods = varfByOwnerAll.TryGetValue(UiMethodExpansion.UiBaseType, out List<MethodSignature>? uiBaseList)
            ? (IReadOnlyList<MethodSignature>)uiBaseList
            : Array.Empty<MethodSignature>();
        HashSet<string> uiCommon = new(uiBaseMethods.Select(static s => s.MethodName), StringComparer.Ordinal);

        IReadOnlyList<int> allDeviceTypeIds = DeviceTypeIds.GetAllTypeIds();

        Dictionary<string, DeclLineRange> methodLineMap = new(StringComparer.Ordinal);
        string text = BuildDts(
            headerHint: "完整 SDK",
            globalFuncs: globalFuncs,
            varFuncs: varFuncs,
            deviceActions: deviceActions,
            varfByOwnerAll: varfByOwnerAll,
            uiCommon: uiCommon,
            uiBaseMethods: uiBaseMethods,
            constTypes: constTypes,
            classVarTypes: allVarTypeSet,
            varFuncAllowedByOwner: null,
            actAllowedByOwner: null,
            allowedGlobalFullNames: null,
            deviceTypeIdsToDeclare: allDeviceTypeIds,
            includeBaseDeclarations: true,
            includeUiBase: true,
            includeDocs: true,
            includeMethod: null,
            emitNotRecommendedDecorators: true,
            methodLineMap: methodLineMap);
        s_methodDeclLineMap = methodLineMap;
        return text;
    }

    internal static (bool Success, string CompressedName, string? GenericBeTypeName) TryGetGlobalCompressedName(string methodName)
    {
        Dictionary<string, MemberCompression> map = CompressionTable.Value.GlobalByOriginal;
        if (!map.TryGetValue(methodName, out MemberCompression? info))
        {
            return (false, methodName, null);
        }

        return (true, info.MethodName, info.GenericBeTypeName);
    }

    internal static (bool Success, string CompressedName, string? GenericBeTypeName) TryGetVarFuncCompressedName(
        string ownerType,
        string methodName)
    {
        Dictionary<string, Dictionary<string, MemberCompression>> map = CompressionTable.Value.VarFuncByOriginal;
        if (!map.TryGetValue(ownerType, out Dictionary<string, MemberCompression>? byName))
        {
            return (false, methodName, null);
        }

        if (!byName.TryGetValue(methodName, out MemberCompression? info))
        {
            return (false, methodName, null);
        }

        return (true, info.MethodName, info.GenericBeTypeName);
    }

    internal static (bool Success, string CompressedName, string? GenericBeTypeName) TryGetDeviceActionCompressedName(
        string ownerType,
        string methodName)
    {
        Dictionary<string, Dictionary<string, MemberCompression>> map = CompressionTable.Value.DeviceActionByOriginal;
        IReadOnlyList<string> chain = DeviceActionInheritance.GetInheritanceChain(ownerType, includeDevice: true);
        for (int i = 0; i < chain.Count; i++)
        {
            if (!map.TryGetValue(chain[i], out Dictionary<string, MemberCompression>? byName))
            {
                continue;
            }

            if (!byName.TryGetValue(methodName, out MemberCompression? info))
            {
                continue;
            }

            return (true, info.MethodName, info.GenericBeTypeName);
        }

        return (false, methodName, null);
    }

    internal static IReadOnlyList<MemberCompressionEntry> GetGlobalOriginalCandidates(string compressedName)
    {
        Dictionary<string, List<MemberCompressionEntry>> map = CompressionTable.Value.GlobalByCompressed;
        if (map.TryGetValue(compressedName, out List<MemberCompressionEntry>? candidates))
            return candidates;

        return Array.Empty<MemberCompressionEntry>();
    }

    internal static IReadOnlyList<MemberCompressionEntry> GetVarFuncOriginalCandidates(string ownerType, string compressedName)
    {
        Dictionary<string, Dictionary<string, List<MemberCompressionEntry>>> map = CompressionTable.Value.VarFuncByCompressed;
        if (!map.TryGetValue(ownerType, out Dictionary<string, List<MemberCompressionEntry>>? byName))
        {
            return GetVarFuncOriginalCandidatesFromBase(ownerType, compressedName);
        }

        if (byName.TryGetValue(compressedName, out List<MemberCompressionEntry>? candidates))
        {
            return candidates;
        }

        return GetVarFuncOriginalCandidatesFromBase(ownerType, compressedName);
    }

    private static IReadOnlyList<MemberCompressionEntry> GetVarFuncOriginalCandidatesFromBase(
        string ownerType,
        string compressedName)
    {
        return VarFuncInheritance.FindInChain(ownerType, type =>
        {
            Dictionary<string, Dictionary<string, List<MemberCompressionEntry>>> map = CompressionTable.Value.VarFuncByCompressed;
            if (map.TryGetValue(type, out Dictionary<string, List<MemberCompressionEntry>>? byName) &&
                byName.TryGetValue(compressedName, out List<MemberCompressionEntry>? candidates))
            {
                return (true, (IReadOnlyList<MemberCompressionEntry>)candidates);
            }
            return (false, null);
        }, startFromParent: true)
#pragma warning disable TK005 // FindInChain 未命中时返回 null，转为空列表
        ?? Array.Empty<MemberCompressionEntry>()
#pragma warning restore TK005
        ;
    }

    private static TsViewCompressionTable BuildCompressionTable()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        // 注意: 压缩表也应跳过 EditorOnly 方法，与 TS 视图生成逻辑保持一致
        MethodSignature[] globalFuncs = QueryAllBuiltinMethods(registry, MethodCategory.GlobalFunc)
            .Where(s => s.Scope != MethodScope.EditorOnly).ToArray();
        MethodSignature[] varFuncs = QueryAllBuiltinMethods(registry, MethodCategory.VarFunc)
            .Where(s => s.Scope != MethodScope.EditorOnly).ToArray();
        MethodSignature[] deviceActions = QueryAllBuiltinMethods(registry, MethodCategory.DeviceAction)
            .Where(s => s.Scope != MethodScope.EditorOnly).ToArray();

        TsViewCompressionTable table = new();
        BuildGlobalCompressionMaps(globalFuncs, table.GlobalByOriginal, table.GlobalByCompressed);
        HashSet<string> varFuncUiSet = new(UiTypes, StringComparer.Ordinal);
        HashSet<string> varFuncUiCommon = new(
            varFuncs.Where(s => string.Equals(s.OwnerType, UiMethodExpansion.UiBaseType, StringComparison.Ordinal))
                    .Select(s => s.MethodName),
            StringComparer.Ordinal);
        BuildOwnerGroupedCompressionMap(varFuncs, MethodCategory.VarFunc, owner => varFuncUiSet.Contains(owner) ? varFuncUiCommon : EmptyStringSet, table.VarFuncByOriginal, table.VarFuncByCompressed);
        BuildOwnerGroupedCompressionMap(deviceActions, MethodCategory.DeviceAction, _ => EmptyStringSet, table.DeviceActionByOriginal, table.DeviceActionByCompressed);
        return table;
    }

    private static void BuildGlobalCompressionMaps(
        IReadOnlyList<MethodSignature> globalFuncs,
        Dictionary<string, MemberCompression> output,
        Dictionary<string, List<MemberCompressionEntry>> reverseOutput)
    {
        GlobalCompressionResult result = BuildGlobalCompressedCallables(globalFuncs, includeDocs: false, includeMethod: null);
        foreach ((string key, MemberCompression value) in result.CompressionMap)
        {
            output[key] = value;
        }
        foreach ((string key, List<MemberCompressionEntry> value) in result.ReverseMap)
        {
            reverseOutput[key] = value;
        }
    }

    private static GlobalCompressionResult BuildGlobalCompressedCallables(
        IReadOnlyList<MethodSignature> globalFuncs,
        bool includeDocs,
        Func<MethodSignature, bool>? includeMethod)
    {
        Dictionary<string, MemberCompression> compressionMap = new(StringComparer.Ordinal);
        Dictionary<string, List<MemberCompressionEntry>> reverseMap = new(StringComparer.Ordinal);

        IReadOnlyList<DtsCallable> callables = BuildCompressedMethods(
            globalFuncs,
            EmptyStringSet,
            includeDocs,
            MethodCategory.GlobalFunc,
            ownerType: string.Empty,
            allowTypeSuffixCompression: true,
            compressionMap,
            reverseMap,
            includeMethod: includeMethod);

        AppendIdentityCompression(globalFuncs, compressionMap, reverseMap);
        AdjustCreateListCompression(compressionMap, reverseMap);

        List<DtsCallable> adjusted = new(callables.Count);
        foreach (DtsCallable callable in callables)
        {
            string name = callable.Name;
            if (callable.Name == "create.list")
                name = "create.listN";
            string? doc = callable.Doc;
            if (includeDocs && name == "create.canvas" && callable.Doc is { } docText)
#pragma warning disable TK010 // SDK 摘要文本去除前后空白
                doc = docText.Trim() + "。createCanvas 对应 create.canvas，会实例化 Canvas 对象，频繁使用有性能问题，不可用于作为默认值赋值；实例创建后显示方式 2D/3D 固定，通常应在创建前显式调用 ui.2dmode，TS 视图为 ui.X_2dmode，设置是否开启 2D 模式。内联 Canvas 模板示例: BeCanvas.fromNodeConst(\"hud\", `<BeCanvas name=\"hud\" width=\"1080\" height=\"2160\"></BeCanvas>`)。";
#pragma warning restore TK010
            RecommendLevel recommend = callable.Recommend;
            if (name.StartsWith("ui.", StringComparison.Ordinal) && !name.Equals("ui.2dmode", StringComparison.Ordinal))
                recommend = RecommendLevel.NotRecommended;
            string? reason = callable.NotRecommendedReason;
            if (recommend == RecommendLevel.NotRecommended && reason is null)
            {
                reason = DefaultUiGlobalNotRecommendedReason;
            }
            adjusted.Add(callable with { Name = name, Doc = doc, Recommend = recommend, NotRecommendedReason = reason });
        }

        return new GlobalCompressionResult(adjusted, compressionMap, reverseMap);
    }

    private static void AdjustCreateListCompression(
        Dictionary<string, MemberCompression> compressionMap,
        Dictionary<string, List<MemberCompressionEntry>> reverseMap)
    {
        const string legacyName = "create.list";
        const string renamed = "create.listN";

        foreach (string key in compressionMap.Keys.ToArray())
        {
            MemberCompression value = compressionMap[key];
            if (string.Equals(value.MethodName, legacyName, StringComparison.Ordinal))
            {
                compressionMap[key] = new MemberCompression(renamed, value.GenericBeTypeName);
            }
        }

        if (reverseMap.TryGetValue(legacyName, out List<MemberCompressionEntry>? list))
        {
            if (!reverseMap.TryGetValue(renamed, out List<MemberCompressionEntry>? target))
            {
                target = [];
                reverseMap[renamed] = target;
            }

            foreach (MemberCompressionEntry entry in list)
            {
                if (!target.Any(x => x.OriginalName == entry.OriginalName &&
                                     x.GenericBeTypeName == entry.GenericBeTypeName))
                {
                    target.Add(entry);
                }
            }

            reverseMap.Remove(legacyName);
        }
    }

    private static void BuildOwnerGroupedCompressionMap(
        IReadOnlyList<MethodSignature> methods,
        MethodCategory category,
        Func<string, IReadOnlySet<string>> getExcludeNames,
        Dictionary<string, Dictionary<string, MemberCompression>> output,
        Dictionary<string, Dictionary<string, List<MemberCompressionEntry>>> reverseOutput)
    {
        Dictionary<string, List<MethodSignature>> byOwner = new(StringComparer.Ordinal);
        foreach (MethodSignature s in methods)
        {
            if (s.OwnerType is null)
            {
                continue;
            }
            if (!byOwner.TryGetValue(s.OwnerType, out List<MethodSignature>? list))
            {
                list = [];
                byOwner[s.OwnerType] = list;
            }
            list.Add(s);
        }

        foreach ((string ownerType, List<MethodSignature> ownerMethods) in byOwner)
        {
            IReadOnlySet<string> excludeNames = getExcludeNames(ownerType);
            Dictionary<string, MemberCompression> map = new(StringComparer.Ordinal);
            Dictionary<string, List<MemberCompressionEntry>> reverseMap = new(StringComparer.Ordinal);
            BuildCompressedMethods(ownerMethods, excludeNames, includeDocs: false, category, ownerType, allowTypeSuffixCompression: true, map, reverseMap);
            AppendIdentityCompression(ownerMethods, map, reverseMap);
            if (map.Count > 0)
            {
                output[ownerType] = map;
            }
            if (reverseMap.Count > 0)
            {
                reverseOutput[ownerType] = reverseMap;
            }
        }
    }

    private static void AppendRepoDecoratorDeclaration(StringBuilder sb)
    {
        sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("// BeScript 方法装饰器");
        sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("/** @internal TS 视图装饰器类型，仅供内部使用 */");
        sb.AppendLine("type TsViewDecorator = (...args: any[]) => any;");
        sb.AppendLine();
        sb.AppendLine("/** CodeRepo 品牌类型，用于 repo 参数的类型约束 */");
        sb.AppendLine("type BrandedRepo<TGuid extends string> = { readonly __repoGuid: TGuid };");
        sb.AppendLine();
        AppendEventSignatureDeclarations(sb);
        sb.AppendLine(@"
/**
 * @internal BeScript 方法元数据
 *
 * 仅供 BeScript/BeScriptAsync 函数内部使用，禁止在用户代码中直接引用
 */
interface BeScriptMeta {
  /** 原始脚本名称（用于写回定位） */
  name: string;
  /** 代码仓库引用 GUID */
  repo?: BrandedRepo<string>;
  /** 编辑器颜色 [R, G, B]，范围 0-255 */
  color?: readonly [number, number, number];
  /** 调试输出 */
  debug?: boolean;
  /** 跳过执行 */
  skip?: boolean;
  /** 私有：不允许跨零件调用 */
  private?: boolean;
  /** 注释说明 */
  comment?: string;
  /** 返回值变量名（用于写回校验，未提供则表示 void） */
  retVar?: string;
  /** 返回值类型名（由视图生成器填入，禁止手写） */
  retVarType?: keyof RetVarTypeMap;
  /** 原始参数列表（事件脚本专用，由视图生成器填入，禁止手写） */
  rawParams?: string;
  /** 原始返回变量（事件脚本专用，由视图生成器填入，禁止手写） */
  rawRetVar?: string;
}
/** BeScriptMeta 有返回值版本（retVar + retVarType 必填） */
type BeScriptMetaWithRet<T extends keyof RetVarTypeMap = keyof RetVarTypeMap> = BeScriptMeta & { retVar: string; retVarType: T };

/**
 * BeScript 同步脚本声明。
 *
 * 将函数表达式包装为零件脚本方法，运行时具有重入保护和局部变量跨调用保持语义。
 *
 * - 有返回值: 函数返回类型必须与 retVarType 一致
 * - 无返回值: 函数返回类型无约束
 */
declare function BeScript<T extends keyof RetVarTypeMap>(meta: BeScriptMeta & { retVar: string; retVarType: T }): <TFn extends (this: any, ...args: any[]) => RetVarTypeMap[T]>(fn: TFn) => (...args: BeScriptExternalArgs<TFn>) => RetVarTypeMap[T];
declare function BeScript(meta: BeScriptMeta): <TFn extends (this: any, ...args: any[]) => any>(fn: TFn) => (...args: BeScriptExternalArgs<TFn>) => ReturnType<TFn>;

/**
 * BeScript 异步脚本声明。
 *
 * 与 BeScript 相同，但方法体内允许使用 await 进行异步操作。
 *
 * - 有返回值: 调用方阻塞等待完成，对外签名 Promise<T>，调用方必须 await，函数返回类型必须与 retVarType 一致
 * - 无返回值: 调用方立即返回，方法体并发执行，对外签名 FireAndForget，禁止 await
 */
declare function BeScriptAsync<T extends keyof RetVarTypeMap>(meta: BeScriptMetaWithRet<T>): <TFn extends (this: any, ...args: any[]) => Promise<RetVarTypeMap[T]>>(fn: TFn) => (...args: BeScriptExternalArgs<TFn>) => Promise<RetVarTypeMap[T]>;
declare function BeScriptAsync(meta: BeScriptMeta): <TFn extends (this: any, ...args: any[]) => Promise<void>>(fn: TFn) => (...args: BeScriptExternalArgs<TFn>) => FireAndForget;
");

        sb.AppendLine("/**");
        sb.AppendLine(" * 声明零件类支持的事件列表");
        sb.AppendLine(" * ");
        sb.AppendLine(" * 用于零件类声明上方，标注该零件类型允许的事件名称。");
        sb.AppendLine(" * 事件名称必须与零件类型定义中的事件列表完全一致。");
        sb.AppendLine(" * ");
        sb.AppendLine(" * @example");
        sb.AppendLine(" * @events(\"Upd\", \"OnCreate\")");
        sb.AppendLine(" * export class MyDevice { ... }");
        sb.AppendLine(" */");
        sb.AppendLine("declare function events<const TEventNames extends readonly TsViewEventName[]>(...eventNames: TEventNames): (target: new (...args: any[]) => any) => void;");
        sb.AppendLine();
        sb.AppendLine("/**");
        sb.AppendLine(" * @internal 全局变量声明选项");
        sb.AppendLine(" * ");
        sb.AppendLine(" * 仅供 @bsVar 装饰器内部使用，禁止在用户代码中直接引用");
        sb.AppendLine(" */");
        sb.AppendLine("type BsVarOptions = { name: string; comment?: string };");
        sb.AppendLine();
        sb.AppendLine("/**");
        sb.AppendLine(" * 声明零件全局变量的原始名称");
        sb.AppendLine(" * ");
        sb.AppendLine(" * 用于全局变量字段声明上方，指定该变量在 BeScript 中的原始名称和注释。");
        sb.AppendLine(" * ");
        sb.AppendLine(" * @param options.name - BeScript 中的变量名");
        sb.AppendLine(" * @param options.comment - 变量的注释说明");
        sb.AppendLine(" * ");
        sb.AppendLine(" * @example");
        sb.AppendLine(" * @bsVar({ name: \"生命值\", comment: \"当前血量\" })");
        sb.AppendLine(" * private HP: BeFloat = BeFloat.fromBeConst(\"0\");");
        sb.AppendLine(" */");
        sb.AppendLine("declare function bsVar(options: BsVarOptions): TsViewDecorator;");
        sb.AppendLine();
        sb.AppendLine("/**");
        sb.AppendLine(" * @internal 标记底层不推荐使用的淘汰逻辑");
        sb.AppendLine(" * ");
        sb.AppendLine(" * 仅用于 d.ts 自身标注不推荐使用的方法，禁止在用户代码中使用");
        sb.AppendLine(" */");
        sb.AppendLine("type BsNotRecommendedOptions = { obsolete: boolean; reason: string };");
        sb.AppendLine("declare function bsNotRecommended(options: BsNotRecommendedOptions): TsViewDecorator;");
        sb.AppendLine();
    }

    private static void AppendEventSignatureDeclarations(StringBuilder sb)
    {
        EventSignatureRegistry registry = EventSignatureRegistry.Instance;
        IReadOnlyList<EventSignature> signatures = registry.GetAll();

        Dictionary<string, HashSet<string>> signatureMap = new(StringComparer.Ordinal);
        foreach (EventSignature signature in signatures)
        {
            if (!signatureMap.TryGetValue(signature.EventName, out HashSet<string>? typeSet))
            {
                typeSet = new HashSet<string>(StringComparer.Ordinal);
                signatureMap[signature.EventName] = typeSet;
            }

            typeSet.Add(BuildTsEventMethodType(signature));
        }

        sb.AppendLine("/**");
        sb.AppendLine(" * @internal 事件签名映射（完整参数签名）");
        sb.AppendLine(" * ");
        sb.AppendLine(" * 键为事件名，值为该事件可用的方法签名；");
        sb.AppendLine(" * 若同名事件在不同零件类型上参数不同，则使用联合类型。");
        sb.AppendLine(" */");
        sb.AppendLine("interface TsViewEventSignatureMap {");

        foreach ((string eventName, HashSet<string> typeSet) in signatureMap.OrderBy(static kv => kv.Key, StringComparer.Ordinal))
        {
            string[] methodTypes = typeSet.OrderBy(static x => x, StringComparer.Ordinal).ToArray();
            string unionType = string.Join(" | ", methodTypes.Select(static x => $"({x})"));
            if (methodTypes.Length == 1)
                unionType = methodTypes[0];
            sb.Append("  ").Append(TsViewTypeUtils.ToTsStringLiteral(eventName)).Append(": ").Append(unionType).AppendLine(";");
        }

        sb.AppendLine("}");
        sb.AppendLine("type TsViewEventName = keyof TsViewEventSignatureMap;");
        sb.AppendLine("type TsViewEventMethod<E extends TsViewEventName> = TsViewEventSignatureMap[E];");
        sb.AppendLine();
        // 不能用 Parameters<TsViewEventMethod<E>>：Parameters 内置约束 (...args: any[]) => any
        // 对 union 函数类型在 strict function types 下会因逆变失败而触发 2344，
        // 改为 conditional type 直接 infer 参数元组，对 union 自然分发且不要求前置约束。
        sb.AppendLine("type TsViewEventExternalArgs<E extends TsViewEventName> = TsViewEventMethod<E> extends (...args: infer P) => any ? BeScriptExternalArgsTuple<P> : never;");
        sb.AppendLine("type TsViewEventReturn = void | Promise<void> | FireAndForget;");
        sb.AppendLine("type TsViewEventMember<E extends TsViewEventName, TValue> = TValue extends (...args: infer TArgs) => infer TReturn");
        sb.AppendLine("  ? TArgs extends TsViewEventExternalArgs<E>");
        sb.AppendLine("    ? TsViewEventExternalArgs<E> extends TArgs");
        sb.AppendLine("      ? TReturn extends TsViewEventReturn ? TValue : never");
        sb.AppendLine("      : never");
        sb.AppendLine("    : never");
        sb.AppendLine("  : never;");
        sb.AppendLine("type TsViewEventRegistrationMembers<E extends TsViewEventName, TMembers extends Record<string, (...args: any[]) => any>> = { [K in keyof TMembers]: TsViewEventMember<E, TMembers[K]> };");
        sb.AppendLine();
    }

    private static string BuildTsEventMethodType(EventSignature signature)
    {
        if (signature.Parameters.Count == 0)
        {
            return "() => void";
        }

        HashSet<string> usedNames = new(StringComparer.Ordinal);
        List<string> paramParts = new(signature.Parameters.Count);
        for (int i = 0; i < signature.Parameters.Count; i++)
        {
            EventParameter parameter = signature.Parameters[i];
            if (parameter.EffectiveName is null)
            {
                throw new InvalidOperationException($"事件参数名为空: {signature.DeviceType}.{signature.EventName} 参数序号 {i + 1}");
            }

            string baseName;
            try
            {
                baseName = TsViewIdentifierMap.ToTsIdentifier(parameter.EffectiveName);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"事件参数名无法映射为 TS 标识符: {signature.DeviceType}.{signature.EventName} 参数名 {parameter.EffectiveName}",
                    ex);
            }

            if (!TsViewIdentifierMap.IsValidIdentifier(baseName))
            {
                throw new InvalidOperationException(
                    $"事件参数名不是合法的 TS 标识符: {signature.DeviceType}.{signature.EventName} 参数名 {parameter.EffectiveName} 映射后 {baseName}");
            }

            string paramName = TsViewIdentifierMap.EnsureUniqueIdentifier(baseName, usedNames);

            string paramType = BuildTsEventParamTypeName(parameter);
            paramParts.Add($"{paramName}: {paramType}");
        }

        return $"({string.Join(", ", paramParts)}) => void";
    }

    internal static string BuildTsEventParamTypeName(EventParameter parameter)
    {
        if (parameter.EnumType is not null)
        {
            return $"BeEnum<{parameter.EnumType.Name}>";
        }

        string rawTypeName = parameter.TypeName;
        if (rawTypeName is null)
        {
            throw new InvalidOperationException("事件参数类型为空");
        }

        string convertedTypeName = ConvertVarTypeName(rawTypeName);

        if (BescriptVarTypeHelper.TryParse(convertedTypeName) is not BescriptVarType varType)
        {
            throw new InvalidOperationException($"事件参数类型无法识别: {rawTypeName}");
        }

        return ToBeTypeName(varType.ToString());
    }

    private static void AppendEnumSubtypeClasses(
        StringBuilder sb,
        IReadOnlyList<MethodSignature> globalFuncs,
        IReadOnlyList<MethodSignature> varFuncs,
        IReadOnlyList<MethodSignature> deviceActions,
        bool includeDocs)
    {
        Dictionary<string, MethodEnumType> enumTypes = new(StringComparer.Ordinal);

        static void CollectFromMethods(IEnumerable<MethodSignature> methods, Dictionary<string, MethodEnumType> output)
        {
            foreach (MethodSignature sig in methods)
            {
                foreach (MethodParameter parameter in sig.Parameters)
                {
                    if (parameter.EnumType is null || ReferenceEquals(parameter.EnumType, MethodEnumType.FollowVarfOwner))
                    {
                        continue;
                    }

                    output[parameter.EnumType.Name] = parameter.EnumType;
                }
            }
        }

        CollectFromMethods(globalFuncs, enumTypes);
        CollectFromMethods(varFuncs, enumTypes);
        CollectFromMethods(deviceActions, enumTypes);

        foreach (EventSignature signature in EventSignatureRegistry.Instance.GetAll())
        {
            foreach (EventParameter parameter in signature.Parameters)
            {
                if (parameter.EnumType is null)
                {
                    continue;
                }

                enumTypes[parameter.EnumType.Name] = parameter.EnumType;
            }
        }

        if (enumTypes.Count == 0)
        {
            return;
        }

        sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("// 具体 TS 枚举类型");
        sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════");
        sb.AppendLine();

        foreach (MethodEnumType enumType in enumTypes.Values.OrderBy(static x => x.Name, StringComparer.Ordinal))
        {
            string header = $"declare enum {enumType.Name}";
            string? specificEnumDoc = null;
            if (includeDocs)
                specificEnumDoc = enumType.Summary;
            AppendDocLine(sb, string.Empty, specificEnumDoc);
            sb.Append(header).AppendLine(" {");
            IReadOnlyList<EnumMemberInfo> members = BuiltinEnumTypes.GetMembers(enumType);
            const int maxPerLine = 10;
            int lineMemberCount = 0;
            for (int mi = 0; mi < members.Count; mi++)
            {
                EnumMemberInfo member = members[mi];
                bool hasComment = false;
                if (lineMemberCount == 0)
                    sb.Append("  ");
                else
                    sb.Append(' ');
                sb.Append(member.TsName)
                    .Append(" = ")
                    .Append(TsViewTypeUtils.ToTsStringLiteral(member.RawValue))
                    .Append(',');
                if (includeDocs && member.Description is { } desc)
                {
                    string lineComment = TextLineEndings.ToLf(desc).Replace("\n", " ");
                    sb.Append(" // ").Append(lineComment);
                    hasComment = true;
                }
                lineMemberCount++;
                if (lineMemberCount >= maxPerLine || mi == members.Count - 1 || hasComment)
                {
                    sb.AppendLine();
                    lineMemberCount = 0;
                }
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // BeEnum 泛型载体类
        sb.AppendLine("type EnumType = " + string.Join(" | ", enumTypes.Values.Select(static e => e.Name).OrderBy(static n => n, StringComparer.Ordinal)) + ";");
        sb.AppendLine();
        const string beEnumHeader = "declare class BeEnum<T extends EnumType>";
        string? enumDoc = null;
        if (includeDocs && BeTypeDescriptions.ContainsKey("Enum"))
            enumDoc = BeTypeDescriptions["Enum"];
        AppendDocLine(sb, string.Empty, enumDoc);
        sb.Append(beEnumHeader).AppendLine(" {");
        AppendTypeNominalBrand(sb, "  ", "Enum");
        sb.AppendLine("  static fromBeConst<T extends EnumType>(value: T): Const<BeEnum<T>>;");
        sb.Append("  equal(other: ")
            .Append(BuildParamSlotType("BeEnum<T>", ParamSlotRule.ConstOnly))
            .AppendLine("): BeBool;");
        sb.AppendLine("}");
        sb.AppendLine();
    }
    private static void AppendDeviceTypeDeclarations(StringBuilder sb, IReadOnlyList<int> typeIds, IReadOnlySet<string> declaredActionOwners)
    {
        EventSignatureRegistry eventRegistry = EventSignatureRegistry.Instance;
        foreach (int typeId in typeIds)
        {
            // DeviceType_<typeId> 仅是按零件 type id 暴露的动作入口壳。
            // 多个 type 通常复用同一 DeviceActions 原型，不按 typeId 重复展开方法；
            // 事件注册方法按 typeId 的允许事件集合声明，不能随 DeviceActions 继承链外溢。
            string ownerTypeName = DeviceTypeIds.GetActionOwnerTypeName(typeId);
            string typeName = BuildDeviceTypeAliasName(typeId);
            string actionClassName = declaredActionOwners.Contains(ownerTypeName)
                ? "DeviceActions" + ownerTypeName
                : ResolveDeviceActionsBaseClassName(ownerTypeName, declaredActionOwners);
            IReadOnlyDictionary<string, EventSignature> selectedEvents = EventSignatureRules.SelectAllowedEventSignatures(typeId, eventRegistry);
            if (selectedEvents.Count == 0)
            {
                sb.Append("declare class ").Append(typeName)
                    .Append(" extends ").Append(actionClassName).AppendLine(" {}");
                continue;
            }

            sb.Append("declare class ").Append(typeName)
                .Append(" extends ").Append(actionClassName).AppendLine(" {");
            IEnumerable<string> eventNames = selectedEvents.Values
                .Select(static signature =>
                    signature.DeviceType.Equals("BirthBuilding", StringComparison.Ordinal)
                        ? BirthBuildingEventMap.ToCoreName[signature.EventName]
                        : signature.EventName)
                .OrderBy(static eventName => eventName, StringComparer.Ordinal);
            AppendEventRegistrationMethods(sb, "  ", eventNames);
            sb.AppendLine("}");
        }
    }

    private static void AppendActTargetDeclarations(StringBuilder sb)
    {
        sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("// Act 目标定位声明");
        sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("/** @internal Act 调用目标类型，仅供类型系统内部使用 */");
        sb.AppendLine("type ActTarget<TDevice> = TDevice;");
        sb.AppendLine();
        sb.AppendLine("/**");
        sb.AppendLine(" * 零件动作调用入口");
        sb.AppendLine(" *");
        sb.AppendLine(" * 用于调用零件的内置动作或公开自定义脚本");
        sb.AppendLine(" */");
        sb.AppendLine("declare namespace Act {");
        sb.AppendLine("  /** 调用当前零件自身的动作 */");
        sb.AppendLine("  function self<TDevice extends { readonly __concreteBrand: true }>(target: TDevice): ActTarget<TDevice>;");
        sb.AppendLine("  /** 按名称查找零件并调用其动作 */");
        sb.AppendLine("  function byName<TDevice extends { readonly __concreteBrand: true }>(name: string): ActTarget<TDevice>;");
        sb.AppendLine("  /** 按 ID 查找零件并调用其动作 */");
        sb.AppendLine("  function byId<TDevice extends { readonly __concreteBrand: true }>(id: number): ActTarget<TDevice>;");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    internal const string DeviceTypeAliasPrefix = "DeviceType_";

    internal static string BuildDeviceTypeAliasName(int typeId)
    {
        return DeviceTypeAliasPrefix + typeId;
    }

    internal static (bool Success, int TypeId) TryParseDeviceTypeAliasName(string? text)
    {
        if (text is null)
        {
            return (false, 0);
        }

        if (!text.StartsWith(DeviceTypeAliasPrefix, StringComparison.Ordinal))
        {
            return (false, 0);
        }

        if (int.TryParse(text[DeviceTypeAliasPrefix.Length..], out int typeId))
            return (true, typeId);
        return (false, 0);
    }

    internal static string BuildDeviceTypeTypeName(int deviceTypeId)
    {
        if (!DeviceTypeIds.IsKnownTypeId(deviceTypeId))
        {
            throw new InvalidOperationException($"零件类型未同步到 SDK,无法生成 DeviceType 声明: {deviceTypeId}");
        }

        return BuildDeviceTypeAliasName(deviceTypeId);
    }

    /// <summary>
    /// 将原版类型名转换为 TS 视图类型名（添加 Be 前缀）
    /// </summary>
    internal static string ToBeTypeName(string typeName)
    {
        return "Be" + typeName;
    }

    internal static string BuildTsSdkPath(MethodCategory category, string ownerType, string tsMethodName)
    {
        return category switch
        {
            MethodCategory.GlobalFunc => $"G.{tsMethodName}",
            MethodCategory.VarFunc => $"{ToBeTypeName(ownerType)}.{tsMethodName}",
            MethodCategory.DeviceAction => $"DeviceActions{ownerType}.{tsMethodName}",
            MethodCategory.EditorFunc => $"{ToBeTypeName(ownerType)}.{tsMethodName}",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "未知方法分类")
        };
    }

    private static string ConvertVarTypeName(string? typeName)
    {
        if (typeName is null)
            return nameof(BescriptVarType.Void);
        if (BescriptVarTypeHelper.TryParse(typeName) is { } type)
            return type.ToString();
        return typeName;
    }

    private static string ConvertCompressionBaseName(string baseName)
    {
        if (baseName.EndsWith("_", StringComparison.Ordinal))
        {
            return baseName[..^1];
        }

        return baseName;
    }

    private static (bool Success, string ConvertedType) TryConvertTypeSuffixTag(string tag)
    {
#pragma warning disable TK011 // 空 tag 无转换结果，返回失败
        if (tag == string.Empty)
#pragma warning restore TK011
        {
            return (false, string.Empty);
        }

        if (tag.Equals("Byte", StringComparison.Ordinal))
        {
            return (true, "Byte");
        }
        if (tag.Equals("Vector", StringComparison.Ordinal))
        {
            return (true, nameof(BescriptVarType.Vector3));
        }

        if (tag.Equals("Fun", StringComparison.Ordinal))
        {
            return (true, nameof(BescriptVarType.List));
        }

        if (tag.EndsWith("Func", StringComparison.Ordinal))
        {
            tag = tag[..^4];
        }

        string convertedType = ConvertVarTypeName(tag);
        return (convertedType.Length > 0, convertedType);
    }

    private static string? BuildGenericConstraint(IReadOnlyList<string> typeNames)
    {
        if (typeNames.Count == 0)
        {
            return null;
        }

        HashSet<string> set = new(StringComparer.Ordinal);
        foreach (string name in typeNames)
        {
            string converted = ConvertVarTypeName(name);
            set.Add(ToBeTypeName(converted));
        }

        if (set.Count == 0)
            return null;
        return string.Join(" | ", set.OrderBy(static x => x, StringComparer.Ordinal));
    }

    private static string[] ResolveParamNames(IReadOnlyList<MethodParameter> parameters)
    {
        if (parameters.Count == 0)
        {
            return Array.Empty<string>();
        }

        Dictionary<string, int>? autoNameCounts = null;
        for (int i = 0; i < parameters.Count; i++)
        {
            string baseName = BuildAutoParamBaseName(parameters[i].TypeName);
            autoNameCounts ??= new Dictionary<string, int>(StringComparer.Ordinal);
            autoNameCounts.TryGetValue(baseName, out int count);
            autoNameCounts[baseName] = count + 1;
        }

        Dictionary<string, int>? autoNameIndices = null;
        HashSet<string> usedNames = new(StringComparer.Ordinal);
        string[] names = new string[parameters.Count];
        for (int i = 0; i < parameters.Count; i++)
        {
            string paramName = parameters[i].EffectiveName;
            if (paramName is null)
            {
                string baseName = BuildAutoParamBaseName(parameters[i].TypeName);
                if (autoNameCounts is not null &&
                    autoNameCounts.TryGetValue(baseName, out int total) &&
                    total > 1)
                {
                    autoNameIndices ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    autoNameIndices.TryGetValue(baseName, out int index);
                    index++;
                    autoNameIndices[baseName] = index;
                    paramName = $"{baseName}_{index}";
                }
                else
                {
                    paramName = baseName;
                }
            }

            string escapedName = TsViewIdentifierMap.ToTsIdentifier(paramName);
            escapedName = TsViewIdentifierMap.EnsureUniqueIdentifier(escapedName, usedNames);
            names[i] = escapedName;
        }

        return names;
    }

    private static string BuildArgParamsWithOverride(
        IReadOnlyList<MethodParameter> parameters,
        int overrideIndex,
        string overrideType)
    {
        if (parameters.Count == 0)
        {
            return string.Empty;
        }

        string[] names = ResolveParamNames(parameters);
        string[] parts = new string[parameters.Count];
        for (int i = 0; i < parameters.Count; i++)
        {
            string typeName = ToBeParamTypeName(parameters[i]);
            if (i == overrideIndex)
                typeName = overrideType;
            parts[i] = names[i] + ": " + typeName;
        }

        return string.Join(", ", parts);
    }

    private static CompressionHint? TryGetCompressionHint(
        IReadOnlyList<MethodSignature> methods,
        CompressionKind kind,
        string baseName)
    {
        foreach (MethodSignature sig in methods)
        {
            CompressionHint? info = sig.CompressionHint;
            if (info is null)
            {
                continue;
            }

            if (info.Kind != kind)
            {
                continue;
            }

            if (!string.Equals(info.BaseName, baseName, StringComparison.Ordinal))
            {
                continue;
            }

            return info;
        }

        return null;
    }

    private static HashSet<string> CollectConstTypes(params IReadOnlyList<MethodSignature>[] sources)
    {
        HashSet<string> set = new(StringComparer.Ordinal);
        foreach (IReadOnlyList<MethodSignature> list in sources)
        {
            foreach (MethodSignature sig in list)
            {
                foreach (MethodParameter param in sig.Parameters)
                {
                    string converted = ConvertVarTypeName(param.TypeName);
                    set.Add(converted);
                }
            }
        }

        return set;
    }

    private static readonly IReadOnlySet<string> EmptyStringSet = new HashSet<string>(StringComparer.Ordinal);


    private static string BuildDts(
        string headerHint,
        IReadOnlyList<MethodSignature> globalFuncs,
        IReadOnlyList<MethodSignature> varFuncs,
        IReadOnlyList<MethodSignature> deviceActions,
        IReadOnlyDictionary<string, List<MethodSignature>> varfByOwnerAll,
        IReadOnlySet<string> uiCommon,
        IReadOnlyList<MethodSignature> uiBaseMethods,
        HashSet<string> constTypes,
        IReadOnlySet<string> classVarTypes,
        IReadOnlyDictionary<string, HashSet<string>>? varFuncAllowedByOwner,
        IReadOnlyDictionary<string, HashSet<string>>? actAllowedByOwner,
        IReadOnlySet<string>? allowedGlobalFullNames,
        IReadOnlyList<int> deviceTypeIdsToDeclare,
        bool includeBaseDeclarations,
        bool includeUiBase,
        bool includeDocs,
        Func<MethodSignature, bool>? includeMethod,
        bool emitNotRecommendedDecorators,
        Dictionary<string, DeclLineRange>? methodLineMap = null)
    {
        StringBuilder sb = new(256 * 1024);

        // 固定注释区段：使用 doc 格式避免与结构性注释混淆
        sb.AppendLine("/**");
        sb.AppendLine(" * Generated by Toolkit.Core. Do not edit this file manually.");
        sb.AppendLine(" * 用于 TS 视图提示/补全（" + headerHint + "）。");
        sb.AppendLine(" */");
        sb.AppendLine();

        if (includeBaseDeclarations)
        {
            AppendTsGlobalTypes(sb);
            AppendRepoDecoratorDeclaration(sb);
            AppendActTargetDeclarations(sb);
            AppendBaseTypes(sb);
        }

        AppendVarTypeClasses(sb, varFuncs, constTypes, classVarTypes, varFuncAllowedByOwner, uiCommon, includeDocs, includeMethod, emitNotRecommendedDecorators, methodLineMap);
        if (includeBaseDeclarations)
        {
            AppendEnumSubtypeClasses(sb, globalFuncs, varFuncs, deviceActions, includeDocs);
        }
        // BeUI 在 BeString 等类型后生成
        if (includeBaseDeclarations && includeUiBase)
        {
            AppendUiBaseClass(sb, uiBaseMethods, constTypes, includeDocs, methodLineMap);
        }
        AppendGlobalNamespaceG(sb, globalFuncs, includeDocs, allowedGlobalFullNames, includeMethod, emitNotRecommendedDecorators, methodLineMap);
        sb.AppendLine();
        sb.AppendLine("/** delay 语句：等待指定时长（0 = 等1帧），参数必须是返回 BeFloat 的调用，不接受常量 */");
        sb.Append("declare function delay(call: ")
            .Append(BuildParamSlotType("BeFloat", ParamSlotRule.VariableOnly))
            .AppendLine(" | Promise<BeFloat>): Promise<void>");
        IReadOnlySet<string> declaredDeviceActionOwners = AppendDeviceActionClasses(sb, deviceActions, actAllowedByOwner, includeDocs, includeBaseDeclarations, includeMethod, emitNotRecommendedDecorators, methodLineMap);
        AppendDeviceTypeDeclarations(sb, deviceTypeIdsToDeclare, declaredDeviceActionOwners);

        return sb.ToString();
    }

    /// <summary>
    /// 添加 TypeScript 在 noLib 模式下必需的最小全局类型声明。
    /// 这些类型是 TS 编译器内部依赖的，不提供会导致编译错误。
    /// </summary>
    private static void AppendTsGlobalTypes(StringBuilder sb)
    {
        sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("// TypeScript 必需的全局类型（noLib 模式）");
        sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════");
        sb.AppendLine();

        // 基础包装类型
        sb.AppendLine("interface Boolean { valueOf(): boolean; }");
        sb.AppendLine("interface String { valueOf(): string; readonly length: number; }");
        sb.AppendLine("interface Number { valueOf(): number; }");
        sb.AppendLine("interface Object { valueOf(): Object; }");
        sb.AppendLine("interface Function { apply(thisArg: any, argArray?: any): any; call(thisArg: any, ...argArray: any[]): any; bind(thisArg: any, ...argArray: any[]): any; }");
        sb.AppendLine("interface CallableFunction extends Function {}");
        sb.AppendLine("interface NewableFunction extends Function {}");
        sb.AppendLine("interface IArguments { [index: number]: any; length: number; }");
        sb.AppendLine("interface RegExp { test(string: string): boolean; }");
        sb.AppendLine("interface Array<T> { length: number; [n: number]: T; }");
        sb.AppendLine("interface Symbol { toString(): string; valueOf(): symbol; }");
        sb.AppendLine();

        // TypedPropertyDescriptor（experimentalDecorators 需要）
        sb.AppendLine("interface TypedPropertyDescriptor<T> { enumerable?: boolean; configurable?: boolean; writable?: boolean; value?: T; get?: () => T; set?: (value: T) => void; }");
        sb.AppendLine();

        // 工具类型
        sb.AppendLine("type Exclude<T, U> = T extends U ? never : T;");
        sb.AppendLine("type Pick<T, K extends keyof T> = { [P in K]: T[P] };");
        sb.AppendLine("type Omit<T, K extends keyof any> = { [P in Exclude<keyof T, K>]: T[P] };");
        sb.AppendLine("type ConstructorParameters<T extends new (...args: any[]) => any> = T extends new (...args: infer P) => any ? P : never;");
        sb.AppendLine("type InstanceType<T extends new (...args: any[]) => any> = T extends new (...args: any[]) => infer R ? R : any;");
        sb.AppendLine("type Record<K extends keyof any, T> = { [P in K]: T };");
        sb.AppendLine("type Parameters<T extends (...args: any[]) => any> = T extends (...args: infer P) => any ? P : never;");
        sb.AppendLine("type ReturnType<T extends (...args: any[]) => any> = T extends (...args: any[]) => infer R ? R : any;");
        sb.AppendLine("interface Promise<T> { then<TResult1 = T, TResult2 = never>(onfulfilled?: ((value: T) => TResult1 | Promise<TResult1>) | null, onrejected?: ((reason: any) => TResult2 | Promise<TResult2>) | null): Promise<TResult1 | TResult2>; catch<TResult = never>(onrejected?: ((reason: any) => TResult | Promise<TResult>) | null): Promise<T | TResult>; }");
        sb.AppendLine("declare var Promise: { new<T>(executor: (resolve: (value: T) => void, reject: (reason?: any) => void) => void): Promise<T>; }");
        sb.AppendLine("type Awaited<T> = T extends Promise<infer U> ? U : T;");
        sb.AppendLine("declare const __fireAndForget: unique symbol;");
        sb.AppendLine("type FireAndForget = { readonly [__fireAndForget]: true };");
        sb.AppendLine();
        sb.AppendLine("/** @internal BeScript 常量：在原始类型基础上注入 const 品牌 */");
        sb.AppendLine("type Const<T> = Omit<T, '__beConst'> & BeConstMarker;");
        sb.AppendLine("/** @internal BeScript 外部调用参数元组：同位置接受变量或 fromBeConst 常量 */");
        sb.AppendLine("type BeScriptExternalArgsTuple<TArgs extends any[]> = [...{ [I in keyof TArgs]: TArgs[I] | Const<TArgs[I]> }];");
        sb.AppendLine("/** @internal BeScript 外部调用参数：同位置接受变量或 fromBeConst 常量 */");
        sb.AppendLine("type BeScriptExternalArgs<TFn extends (...args: any[]) => any> = BeScriptExternalArgsTuple<Parameters<TFn>>;");
        sb.AppendLine();
    }

    private static void AppendBaseTypes(StringBuilder sb)
    {
        sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("// BeScript 基础类型定义");
        sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════");
        sb.AppendLine();

        // BeValue：所有 Be* 类型联合
        string[] beValueNames = Enum.GetNames(typeof(BescriptVarType))
            .Where(static n => n is not ("Void" or "Enum"))
            .OrderBy(static n => n == "Any")
            .ThenBy(static n => n, StringComparer.Ordinal)
            .Select(ToBeTypeName)
            .ToArray();
        sb.AppendLine("/** @internal 所有 BeScript 值类型的联合，仅供类型系统内部使用 */");
        sb.Append("type BeValue = ").Append(string.Join(" | ", beValueNames)).AppendLine(";");
        sb.AppendLine();

        // RetVarTypeMap：retVarType 字符串到实际类型的映射
        sb.AppendLine("/** @internal retVarType 字符串到实际类型的映射，用于 BeScript/BeScriptAsync 返回值约束 */");
        sb.AppendLine("interface RetVarTypeMap {");
        foreach (string name in beValueNames)
        {
            sb.Append("  ").Append(name).Append(": ").Append(name).AppendLine(";");
        }
        sb.AppendLine("}");
        sb.AppendLine();

        // BeConstMarker
        sb.AppendLine("/**");
        sb.AppendLine(" * @internal 内部标记：标识 BeScript 常量（由 fromBeConst 返回）");
        sb.AppendLine(" * ");
        sb.AppendLine(" * 此类型仅供类型系统内部使用，禁止在用户代码中直接引用或构造");
        sb.AppendLine(" */");
        sb.AppendLine("type BeConstMarker = { readonly __beConst: true };");
        sb.AppendLine();

        // 白名单变量类型（基础类型本体天然排斥常量）
        string[] whitelistVarTypes = beValueNames
            .Where(static n => n != "BeAny")
            .ToArray();

        // BeStringConvertible：白名单变量类型 + BeString 常量
        sb.AppendLine("/**");
        sb.AppendLine(" * 底层方法特有的 String 参数类型，支持隐式类型转换");
        sb.AppendLine(" * ");
        sb.AppendLine(" * 允许传入:");
        sb.AppendLine(" * - BeString 常量（如 BeString.fromBeConst(\"hello\")）");
        sb.AppendLine(" * - 任意 Be 类型的变量（会隐式转换为字符串）");
        sb.AppendLine(" * ");
        sb.AppendLine(" * 禁止传入:");
        sb.AppendLine(" * - 非 String 类型的常量（如 BeFloat.fromBeConst(\"1\")）");
        sb.AppendLine(" */");
        IEnumerable<string> stringConvertible = whitelistVarTypes.Concat(new[] { "Const<BeString>" });
        sb.Append("type BeStringConvertible = ").Append(string.Join(" | ", stringConvertible)).AppendLine(";");
        sb.AppendLine();

        // BeAnyParam：白名单变量类型 + BeAny 常量
        sb.AppendLine("/**");
        sb.AppendLine(" * 底层方法特有的 Any 参数类型");
        sb.AppendLine(" * ");
        sb.AppendLine(" * 允许传入:");
        sb.AppendLine(" * - 任意 Be 类型的变量");
        sb.AppendLine(" * - BeAny 常量（用于表示默认空值）");
        sb.AppendLine(" * ");
        sb.AppendLine(" * 禁止传入:");
        sb.AppendLine(" * - 非 Any 类型的常量（如 BeFloat.fromBeConst(\"1\")）");
        sb.AppendLine(" */");
        IEnumerable<string> anyParam = whitelistVarTypes.Concat(new[] { "Const<BeAny>" });
        sb.Append("type BeAnyParam = ").Append(string.Join(" | ", anyParam)).AppendLine(";");
        sb.AppendLine();

        // Canvas 实例化专用常量类型（非空常量）
        // create.canvas 接受 Const<BeCanvas> | BeCanvasCreateConst（空常量 + 非空常量）
        // 其他 Canvas 参数位接受 BeCanvas | Const<BeCanvas>（变量 + 空常量），BeCanvasCreateConst 被 __beCanvasNonCreate 标记排斥
        sb.AppendLine("/**");
        sb.AppendLine(" * @internal Canvas 实例化专用常量类型");
        sb.AppendLine(" * ");
        sb.AppendLine(" * 仅用于 G.create.canvas 参数。");
        sb.AppendLine(" * - 旧版内联：BeCanvas.fromBase64Const(...)");
        sb.AppendLine(" * - 新版内联：BeCanvas.fromNodeConst(...)");
        sb.AppendLine(" * - 旧版 GUID 引用：BeCanvas.fromGuidConstLegacy(...)");
        sb.AppendLine(" * - 新版 GUID 引用：BeCanvas.fromGuidConst(...)");
        sb.AppendLine(" * ");
        sb.AppendLine(" * 此类型仅供类型系统内部使用，禁止在用户代码中直接引用或构造");
        sb.AppendLine(" */");
        sb.AppendLine("type BeCanvasCreateConst = Omit<Const<BeCanvas>, '__beCanvasNonCreate'> & { readonly __beCanvasNonCreate?: never };");
        sb.AppendLine();

        // Icon 实例化专用常量类型（非空常量）
        // create.icon 接受 Const<BeIcon> | BeIconCreateConst（空常量 + 非空常量）
        // 其他 Icon 参数位接受 BeIcon | Const<BeIcon>（变量 + 空常量），BeIconCreateConst 被 __beIconNonCreate 标记排斥
        sb.AppendLine("/**");
        sb.AppendLine(" * @internal Icon 实例化专用常量类型");
        sb.AppendLine(" * ");
        sb.AppendLine(" * 仅用于 G.create.icon 参数。");
        sb.AppendLine(" * ");
        sb.AppendLine(" * 此类型仅供类型系统内部使用，禁止在用户代码中直接引用或构造");
        sb.AppendLine(" */");
        sb.AppendLine("type BeIconCreateConst = Omit<Const<BeIcon>, '__beIconNonCreate'> & { readonly __beIconNonCreate?: never };");
        sb.AppendLine();
    }

    private static void AppendUiBaseClass(
        StringBuilder sb,
        IReadOnlyList<MethodSignature> uiBaseMethods,
        HashSet<string> constTypes,
        bool includeDocs,
        Dictionary<string, DeclLineRange>? methodLineMap)
    {
        bool uiConstSupported = constTypes.Contains("UI");
        if (includeDocs && BeTypeDescriptions.ContainsKey("UI"))
        {
            sb.Append("/** ").Append(BeTypeDescriptions["UI"]).AppendLine(" */");
        }
        const string header = "declare abstract class BeUI";
        sb.Append(header).AppendLine(" {");
        AppendTypeNominalBrand(sb, "  ", "UI");
        if (uiConstSupported)
        {
            AppendFromBeConst(sb, "  ", "UI", includeDocs);
        }
        if (uiBaseMethods.Count > 0)
        {
            Dictionary<string, MemberCompression> compressionMap = new(StringComparer.Ordinal);
            Dictionary<string, List<MemberCompressionEntry>> reverseMap = new(StringComparer.Ordinal);
            IReadOnlyList<DtsCallable> compressed = BuildCompressedMethods(
                uiBaseMethods,
                new HashSet<string>(StringComparer.Ordinal),
                includeDocs,
                MethodCategory.VarFunc,
                "UI",
                allowTypeSuffixCompression: true,
                compressionMap,
                reverseMap);
            foreach (DtsCallable m in compressed.OrderBy(static x => x.Name, StringComparer.Ordinal))
            {
                DeclLineRange range = AppendCallableMember(sb, "  ", m, emitNotRecommendedDecorators: false);
                RecordDecl(methodLineMap, BuildTsSdkPath(MethodCategory.VarFunc, "UI", m.Name), range);
            }
        }
        sb.AppendLine("}");
    }

    private static void AppendVarTypeClasses(
        StringBuilder sb,
        IReadOnlyList<MethodSignature> varFuncs,
        HashSet<string> constTypes,
        IReadOnlySet<string> allowedTypes,
        IReadOnlyDictionary<string, HashSet<string>>? allowedByOwner,
        IReadOnlySet<string> uiCommon,
        bool includeDocs,
        Func<MethodSignature, bool>? includeMethod,
        bool emitNotRecommendedDecorators,
        Dictionary<string, DeclLineRange>? methodLineMap)
    {
        // 按 OwnerType 分组 varFuncs
        Dictionary<string, List<MethodSignature>> varfByOwner = new(StringComparer.Ordinal);
        foreach (MethodSignature s in varFuncs)
        {
            if (s.OwnerType is null)
            {
                continue;
            }
            if (!varfByOwner.TryGetValue(s.OwnerType, out List<MethodSignature>? list))
            {
                list = [];
                varfByOwner[s.OwnerType] = list;
            }
            list.Add(s);
        }

        string[] typeList = allowedTypes
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        HashSet<string> uiSet = new(UiTypes, StringComparer.Ordinal);

        // 输出允许类型的 class
        foreach (string typeName in typeList)
        {
            if (string.Equals(typeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal))
            {
                continue;
            }

            bool isUi = uiSet.Contains(typeName);
            bool hasVarf = varfByOwner.TryGetValue(typeName, out List<MethodSignature>? varfList);

            // 确定继承关系
            string? baseClass = null;
            if (isUi)
            {
                baseClass = "BeUI";
            }

            // 输出类型 JSDoc
            if (includeDocs && BeTypeDescriptions.ContainsKey(typeName))
            {
                sb.Append("/** ").Append(BeTypeDescriptions[typeName]).AppendLine(" */");
            }

            string beTypeName = ToBeTypeName(typeName);
            string header = $"declare class {beTypeName}";
            if (baseClass is not null)
                header += $" extends {baseClass}";
            sb.Append(header).AppendLine(" {");
            AppendTypeNominalBrand(sb, "  ", typeName);

            // Canvas 类型添加"非创建常量"标记，用于排斥 BeCanvasCreateConst
            if (string.Equals(typeName, nameof(BescriptVarType.Canvas), StringComparison.Ordinal))
            {
                sb.AppendLine("  readonly __beCanvasNonCreate: true;");
            }

            // Icon 类型添加"非创建常量"标记，用于排斥 BeIconCreateConst
            if (string.Equals(typeName, nameof(BescriptVarType.Icon), StringComparison.Ordinal))
            {
                sb.AppendLine("  readonly __beIconNonCreate: true;");
            }

            if (constTypes.Contains(typeName))
            {
                AppendFromBeConst(sb, "  ", typeName, includeDocs);
            }

            string converted = ConvertVarTypeName(typeName);
            if (BescriptVarTypeHelper.TryParse(converted) is { } varType && GlobalVarSupportedTypes.Contains(varType))
            {
                string? docLine = null;
                if (includeDocs)
                    docLine = "全局变量初始化（默认值）";
                AppendDocLine(sb, "  ", docLine);
                sb.Append("  ").Append("static GlobalVarInitialize(): ").Append(ToBeTypeName(typeName)).AppendLine(";");
            }

            // 输出 varFuncs 方法
            if (hasVarf && varfList is not null)
            {
                IReadOnlySet<string> excludeNames = new HashSet<string>(StringComparer.Ordinal);
                if (isUi)
                    excludeNames = uiCommon;
                HashSet<string>? allowed = null;
                if (allowedByOwner is not null)
                {
                    allowedByOwner.TryGetValue(typeName, out allowed);
                }

                Dictionary<string, MemberCompression> compressionMap = new(StringComparer.Ordinal);
                Dictionary<string, List<MemberCompressionEntry>> reverseMap = new(StringComparer.Ordinal);
                IReadOnlyList<DtsCallable> compressed = BuildCompressedMethods(
                    varfList,
                    excludeNames,
                    includeDocs,
                    MethodCategory.VarFunc,
                    typeName,
                    allowTypeSuffixCompression: true,
                    compressionMap,
                    reverseMap,
                    includeMethod: includeMethod);
                IEnumerable<DtsCallable> selectedSource = compressed;
                if (allowed is not null)
                    selectedSource = compressed.Where(x => allowed.Contains(x.Name));
                DtsCallable[] selected = selectedSource
                    .OrderBy(x => x.Name, StringComparer.Ordinal)
                    .ToArray();

                foreach (DtsCallable m in selected)
                {
                    DeclLineRange range = AppendCallableMember(sb, "  ", m, emitNotRecommendedDecorators);
                    RecordDecl(methodLineMap, BuildTsSdkPath(MethodCategory.VarFunc, typeName, m.Name), range);
                }
            }

            sb.AppendLine("}");
        }
    }


    private static void AppendFromBeConst(StringBuilder sb, string indent, string typeName, bool includeDocs)
    {
        // Canvas 有独立的逐方法文档，跳过通用 JSDoc
        if (string.Equals(typeName, nameof(BescriptVarType.Canvas), StringComparison.Ordinal))
        {
            if (includeDocs)
            {
                sb.Append(indent).AppendLine("/** 非创建场景的空 Canvas 占位常量（无参，无需填值） */");
            }
            sb.Append(indent).Append("static fromBeConst(): Const<").Append(ToBeTypeName(typeName)).Append(">").AppendLine(";");
            if (includeDocs)
            {
                sb.Append(indent).AppendLine("/** 旧版内联 Canvas 写回入口，参数仍填写 Canvas HTML 视图模板字符串 */");
            }
            sb.Append(indent).Append("static fromBase64Const<T extends string>(value: T extends \"\" ? never : T): BeCanvasCreateConst").AppendLine(";");
            if (includeDocs)
            {
                sb.Append(indent).AppendLine("/** 新版内联 Canvas 写回入口，第二参数必须是 Canvas HTML 视图模板字符串。示例: BeCanvas.fromNodeConst(\"hud\", `<BeCanvas name=\"hud\" width=\"1080\" height=\"2160\"></BeCanvas>`) */");
            }
            sb.Append(indent).Append("static fromNodeConst<T extends string>(name: string, value: T extends \"\" ? never : T): BeCanvasCreateConst").AppendLine(";");
            if (includeDocs)
            {
                sb.Append(indent).AppendLine("/** 旧版 GUID 引用，参数填 32 位十六进制 GUID，不带 guid: 前缀 */");
            }
            sb.Append(indent).Append("static fromGuidConstLegacy<T extends string>(value: T extends \"\" ? never : T): BeCanvasCreateConst").AppendLine(";");
            if (includeDocs)
            {
                sb.Append(indent).AppendLine("/** 新版 GUID 引用，参数填 32 位十六进制 GUID，不带 ==guid: / == 包裹 */");
            }
            sb.Append(indent).Append("static fromGuidConst<T extends string>(value: T extends \"\" ? never : T): BeCanvasCreateConst").AppendLine(";");
            return;
        }

        if (string.Equals(typeName, nameof(BescriptVarType.Icon), StringComparison.Ordinal))
        {
            if (includeDocs)
            {
                sb.Append(indent).AppendLine("/** 非创建场景的空 Icon 占位常量（无参，无需填值） */");
            }
            sb.Append(indent).Append("static fromBeConst(): Const<").Append(ToBeTypeName(typeName)).Append(">").AppendLine(";");
            if (includeDocs)
            {
                sb.Append(indent).AppendLine("/** G.create.icon 专用的非空 Icon 常量入口 */");
            }
            sb.Append(indent).Append("static fromBeConst<T extends string>(value: T extends \"\" ? never : T): BeIconCreateConst").AppendLine(";");
            return;
        }

        if (includeDocs)
        {
            sb.Append(indent).AppendLine("/**");
            sb.Append(indent).AppendLine(" * 表示 BeScript 常量的包装方法。普通 fromBeConst 按类型固定分为两类：Bool/Float/Long/Color/Vector3/String 必须带参数，其余类型固定无参。");
            sb.Append(indent).AppendLine(" * Enum、Canvas、Icon 等消费位常量使用各自专用形式，不属于普通带参 fromBeConst。");
            sb.Append(indent).AppendLine(" * ");
            sb.Append(indent).AppendLine(" * **注意**: 此方法仅用于将常量嵌入到其他方法的参数位置中，不可直接用于赋值。");
            sb.Append(indent).AppendLine(" * ");
            sb.Append(indent).AppendLine(" * 有参示例: `G.someMethod(BeFloat.fromBeConst(\"1\"))` ✓  |  无参示例: `G.someMethod(BeUI.fromBeConst())` ✓");
            sb.Append(indent).AppendLine(" * - `this.field = BeFloat.fromBeConst(\"0\")` (仅全局变量初始化) ✓");
            sb.Append(indent).AppendLine(" * ");
            sb.Append(indent).AppendLine(" * 禁止的用法:");
            sb.Append(indent).AppendLine(" * - `x = BeFloat.fromBeConst(\"1\")` ✗");
            sb.Append(indent).AppendLine(" * - 任何将其赋值给局部变量的行为 ✗");
            sb.Append(indent).AppendLine(" */");
        }

        // 统一引用 AnyPositionNonEmptyConstTypes 权威集合，与生成层、校验层完全对齐。
        // TS 侧按类型固定决定是否带参数，不存在交叉形式。
        if (ConstFillMode.AnyPositionNonEmptyConstTypes.Contains(typeName))
        {
            if (string.Equals(typeName, nameof(BescriptVarType.Bool), StringComparison.Ordinal))
            {
                sb.Append(indent).Append("static fromBeConst(value: \"0\" | \"1\"): Const<").Append(ToBeTypeName(typeName)).Append(">").AppendLine(";");
            }
            else
            {
                sb.Append(indent).Append("static fromBeConst(value: string): Const<").Append(ToBeTypeName(typeName)).Append(">").AppendLine(";");
            }
        }
        else
        {
            sb.Append(indent).Append("static fromBeConst(): Const<").Append(ToBeTypeName(typeName)).Append(">").AppendLine(";");
        }
    }

#pragma warning disable TK006 // DTS 生成：类型品牌标记输出，独立职责
    private static void AppendTypeNominalBrand(StringBuilder sb, string indent, string typeName)
    {
        string brandName = TsViewIdentifierMap.ToTsIdentifier(typeName);
        sb.Append(indent)
            .AppendLine("readonly __beConst?: never;");
        sb.Append(indent)
            .Append("readonly __beTypeBrand_")
            .Append(brandName)
            .AppendLine(": never;");
    }
#pragma warning restore TK006

    private static IReadOnlySet<string> AppendDeviceActionClasses(
        StringBuilder sb,
        IReadOnlyList<MethodSignature> deviceActions,
        IReadOnlyDictionary<string, HashSet<string>>? allowedByOwner,
        bool includeDocs,
        bool forceDeviceBaseClass,
        Func<MethodSignature, bool>? includeMethod,
        bool emitNotRecommendedDecorators,
        Dictionary<string, DeclLineRange>? methodLineMap)
    {
        // 分流：带 RequireCapabilities 的方法不进入 DeviceActions* 类层级。
        // 带能力要求的方法放入独立的 I{Capability} 接口，零件实例按能力选择 extends。
        Dictionary<DeviceCapability, List<MethodSignature>> capabilityActMap = [];
        Dictionary<string, List<MethodSignature>> actByOwner = new(StringComparer.Ordinal);
        foreach (MethodSignature s in deviceActions)
        {
            if (s.OwnerType is null)
            {
                continue;
            }
            if (s.RequireCapabilities.Count > 0)
            {
                foreach (DeviceCapability cap in s.RequireCapabilities)
                {
                    if (!capabilityActMap.TryGetValue(cap, out List<MethodSignature>? capList))
                    {
                        capList = [];
                        capabilityActMap[cap] = capList;
                    }
                    capList.Add(s);
                }
                continue;
            }
            if (!actByOwner.TryGetValue(s.OwnerType, out List<MethodSignature>? list))
            {
                list = [];
                actByOwner[s.OwnerType] = list;
            }
            list.Add(s);
        }
        HashSet<string> ownersWithActions = new(actByOwner.Keys, StringComparer.Ordinal);
        HashSet<string> ownersToDeclare = new(ownersWithActions, StringComparer.Ordinal);
        ownersToDeclare.Add("Device");

        if (forceDeviceBaseClass || ownersToDeclare.Contains("Device"))
        {
            actByOwner.TryGetValue("Device", out List<MethodSignature>? deviceActs);
            const string deviceHeader = "declare class DeviceActionsDevice";
            sb.Append(deviceHeader).AppendLine(" {");
            if (deviceActs is not null)
            {
                HashSet<string>? allowed = null;
                if (allowedByOwner is not null)
                {
                    allowedByOwner.TryGetValue("Device", out allowed);
                }

                Dictionary<string, MemberCompression> compressionMap = new(StringComparer.Ordinal);
                Dictionary<string, List<MemberCompressionEntry>> reverseMap = new(StringComparer.Ordinal);
                IReadOnlyList<DtsCallable> compressed = BuildCompressedMethods(
                    deviceActs,
                    new HashSet<string>(StringComparer.Ordinal),
                    includeDocs,
                    MethodCategory.DeviceAction,
                    "Device",
                    allowTypeSuffixCompression: true,
                    compressionMap,
                    reverseMap,
                    includeMethod: includeMethod);
                IEnumerable<DtsCallable> selected = compressed;
                if (allowed is not null)
                    selected = compressed.Where(x => allowed.Contains(x.Name));
                foreach (DtsCallable m in selected.OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    DeclLineRange range = AppendCallableMember(sb, "  ", m, emitNotRecommendedDecorators);
                    RecordDecl(methodLineMap, BuildTsSdkPath(MethodCategory.DeviceAction, "Device", m.Name), range);
                }
            }
            sb.AppendLine("}");
        }

        foreach (string ownerType in ownersToDeclare
                     .Where(o => !string.Equals(o, "Device", StringComparison.Ordinal))
                     .OrderBy(o => o, StringComparer.Ordinal))
        {
            string className = "DeviceActions" + ownerType;
            string baseClassName = ResolveDeviceActionsBaseClassName(ownerType, ownersToDeclare);

            HashSet<string>? allowed = null;
            if (allowedByOwner is not null)
            {
                allowedByOwner.TryGetValue(ownerType, out allowed);
            }

            string header = $"declare class {className} extends {baseClassName}";
            sb.Append(header).AppendLine(" {");
            if (actByOwner.TryGetValue(ownerType, out List<MethodSignature>? acts))
            {
                Dictionary<string, MemberCompression> compressionMap = new(StringComparer.Ordinal);
                Dictionary<string, List<MemberCompressionEntry>> reverseMap = new(StringComparer.Ordinal);
                IReadOnlyList<DtsCallable> compressed = BuildCompressedMethods(
                    acts,
                    new HashSet<string>(StringComparer.Ordinal),
                    includeDocs,
                    MethodCategory.DeviceAction,
                    ownerType,
                    allowTypeSuffixCompression: true,
                    compressionMap,
                    reverseMap,
                    includeMethod: includeMethod);
                IEnumerable<DtsCallable> selectedItems = compressed;
                if (allowed is not null)
                    selectedItems = compressed.Where(x => allowed.Contains(x.Name));
                DtsCallable[] selected = selectedItems.OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();

                foreach (DtsCallable m in selected)
                {
                    DeclLineRange range = AppendCallableMember(sb, "  ", m, emitNotRecommendedDecorators);
                    RecordDecl(methodLineMap, BuildTsSdkPath(MethodCategory.DeviceAction, ownerType, m.Name), range);
                }
            }
            sb.AppendLine("}");
        }

        AppendCapabilityInterfaces(sb, capabilityActMap, includeDocs, emitNotRecommendedDecorators, methodLineMap);

        return ownersToDeclare;
    }

    /// <summary>
    /// 按 DeviceCapability 分组生成 I{Capability} 接口（如 ICollidable）。
    /// TS 视图将具有能力要求的方法放入独立接口，
    /// 让零件实例 interface 按需 extends。
    /// </summary>
    private static void AppendCapabilityInterfaces(
        StringBuilder sb,
        IReadOnlyDictionary<DeviceCapability, List<MethodSignature>> capabilityActMap,
        bool includeDocs,
        bool emitNotRecommendedDecorators,
        Dictionary<string, DeclLineRange>? methodLineMap)
    {
        foreach ((DeviceCapability capability, List<MethodSignature> methods) in capabilityActMap)
        {
            string interfaceName = "I" + capability;
            // 不能 export：sdk.ts 全文为脚本，加 export 会变成 module，
            // 把 noLib 必需的全局类型（Boolean/Array 等）困在模块作用域。
            sb.Append("interface ").Append(interfaceName).AppendLine(" {");

            Dictionary<string, MemberCompression> compressionMap = new(StringComparer.Ordinal);
            Dictionary<string, List<MemberCompressionEntry>> reverseMap = new(StringComparer.Ordinal);
            IReadOnlyList<DtsCallable> compressed = BuildCompressedMethods(
                methods,
                new HashSet<string>(StringComparer.Ordinal),
                includeDocs,
                MethodCategory.DeviceAction,
                "Device",
                allowTypeSuffixCompression: true,
                compressionMap,
                reverseMap,
                includeMethod: null);

            foreach (DtsCallable m in compressed.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                DeclLineRange range = AppendCallableMember(sb, "  ", m, emitNotRecommendedDecorators);
                RecordDecl(methodLineMap, BuildTsSdkPath(MethodCategory.DeviceAction, interfaceName, m.Name), range);
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }
    }

    private static void AppendEventRegistrationMethods(StringBuilder sb, string indent, IEnumerable<string> eventNames)
    {
        foreach (string eventName in eventNames)
        {
            string methodName = DeviceTsView.BuildEventRegistrationMethodName(eventName);
            sb.Append(indent)
                .Append("protected static ")
                .Append(methodName)
                .Append("<TMembers extends Record<string, (...args: any[]) => any>>(target: new (...args: any[]) => any, members: TsViewEventRegistrationMembers<")
                .Append(TsViewTypeUtils.ToTsStringLiteral(eventName))
                .AppendLine(", TMembers>): void;");
        }
    }

    /// <summary>
    /// 根据 DeviceActionInheritance 定义的继承链，解析 DeviceActions 类的基类名。
    /// 沿继承链向上查找第一个在 ownersWithActions 中存在的类型。
    /// </summary>
    private static string ResolveDeviceActionsBaseClassName(string ownerType, IReadOnlySet<string> ownersWithActions)
    {
        IReadOnlyList<string> chain = DeviceActionInheritance.GetInheritanceChain(ownerType, includeDevice: true);
        for (int i = 1; i < chain.Count; i++)
        {
            if (ownersWithActions.Contains(chain[i]))
            {
                return "DeviceActions" + chain[i];
            }
        }

        return "DeviceActionsDevice";
    }


    private static IReadOnlyList<DtsCallable> BuildCompressedMethods(
        IReadOnlyList<MethodSignature> methods,
        IReadOnlySet<string> excludedNames,
        bool includeDocs,
        MethodCategory category,
        string ownerType,
        bool allowTypeSuffixCompression,
        Dictionary<string, MemberCompression> compressionMap,
        Dictionary<string, List<MemberCompressionEntry>> reverseMap,
        Func<MethodSignature, bool>? includeMethod = null)
    {
        // 基础类型后缀
        string[] baseTypeTags = Enum.GetNames(typeof(BescriptVarType))
            .Where(n => n is not ("Void" or "UI" or "Number"))
            .ToArray();

        // 构建类型后缀标签：包含 TypeName、TypeNameFunc 两种形式
        // 例如：Bool, BoolFunc, Float, FloatFunc, ...
        string[] typeSuffixTags = baseTypeTags
            .Concat(baseTypeTags.Select(t => t + "Func"))
            .Append("Fun")
            .Append("Vector")
            .OrderByDescending(n => n.Length)
            .ToArray();

        Dictionary<string, MethodSignature> byName = methods.ToDictionary(m => m.MethodName, StringComparer.Ordinal);
        HashSet<string> consumed = new(StringComparer.Ordinal);
        List<DtsCallable> output = [];

        bool Included(MethodSignature sig) => includeMethod is null || includeMethod(sig);

        // 复合压缩：类型后缀 + 数字后缀 => 泛型 + 可变参数
        if (allowTypeSuffixCompression)
        {
            foreach (IGrouping<string, (string Name, string BaseName, int Suffix, string TypeName)> group in methods
                         .Where(s => !excludedNames.Contains(s.MethodName))
                         .Select(s =>
                         {
                             (bool Success, string BaseName, int Suffix) split = TrySplitNumericSuffix(s.MethodName);
                             if (!split.Success)
                             {
                                 return default((string Name, string BaseName, int Suffix, string TypeName)?);
                             }
                             foreach (string tag in typeSuffixTags)
                             {
                                 if (split.BaseName.Length <= tag.Length) continue;
                                 if (!split.BaseName.EndsWith(tag, StringComparison.Ordinal)) continue;

                                 string baseName = ConvertCompressionBaseName(split.BaseName.Substring(0, split.BaseName.Length - tag.Length));
                                 return (Name: s.MethodName, BaseName: baseName, Suffix: split.Suffix, TypeName: tag);
                             }

                             return default((string Name, string BaseName, int Suffix, string TypeName)?);
                         })
                     .OfType<(string Name, string BaseName, int Suffix, string TypeName)>()
                     .GroupBy(x => x.BaseName, StringComparer.Ordinal))
            {
                (string Name, string BaseName, int Suffix, string TypeName)[] items = group.ToArray();
                if (items.Length < 2) continue;
                CompressionHint? hint = TryGetCompressionHint(methods, CompressionKind.TypeSuffixWithNumeric, group.Key);
                if (hint is null)
                {
                    continue;
                }

                IReadOnlyList<string> allowedTypes = hint.AllowedTypes;
                HashSet<string> allowedSet = new(allowedTypes.Select(ConvertVarTypeName), StringComparer.Ordinal);
                items = items
                    .Where(x => TryConvertTypeSuffixTag(x.TypeName) is var c && c.Success && allowedSet.Contains(c.ConvertedType))
                    .ToArray();
                if (items.Length < 2) continue;

                string[] fixedParamNames0 = TrimTrailingVarParams(byName[items[0].Name].Parameters.Select(p => p.EffectiveName).ToArray());
                if (items.Any(x =>
                        !TrimTrailingVarParams(byName[x.Name].Parameters.Select(p => p.EffectiveName).ToArray())
                            .SequenceEqual(fixedParamNames0, StringComparer.Ordinal)))
                {
                    continue;
                }

                string[] returnTypes = items
                .Select(x => byName[x.Name].ReturnType)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToArray();
                if (returnTypes.Length < 2) continue;

                string? genericConstraint = BuildGenericConstraint(returnTypes);
                if (genericConstraint is null)
                {
                    continue;
                }

                bool ok = true;
                foreach (IGrouping<string, (string Name, string BaseName, int Suffix, string TypeName)> typeGroup in items.GroupBy(x => x.TypeName, StringComparer.Ordinal))
                {
                    int[] suffixes = typeGroup.Select(x => x.Suffix).Distinct().OrderBy(x => x).ToArray();
                    if (suffixes.Length < 2 || !IsContinuousNumericSuffixes(suffixes))
                    {
                        ok = false;
                        break;
                    }
                }
                if (!ok) continue;

                foreach (IGrouping<RecommendLevel, MethodSignature> recommendGroup in items
                             .Select(x => byName[x.Name])
                             .Where(Included)
                             .GroupBy(x => x.Recommend))
                {
                    MethodSignature[] groupSigs = recommendGroup.ToArray();

                    string[] groupReturnTypes = groupSigs
                    .Select(x => x.ReturnType)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(t => t, StringComparer.Ordinal)
                    .ToArray();
                    if (groupReturnTypes.Length == 0)
                    {
                        continue;
                    }

                    string? groupConstraint = BuildGenericConstraint(groupReturnTypes);
                    if (groupConstraint is null)
                    {
                        continue;
                    }

                    int maxTotalParams = groupSigs.Select(x => x.Parameters.Count).Max();
                    int maxVarCount = Math.Max(0, maxTotalParams - fixedParamNames0.Length);
                    MethodSignature referenceSig = groupSigs[0];
                    MethodParameter[] fixedParams = referenceSig.Parameters.Take(fixedParamNames0.Length).ToArray();
                    string restElementType = ChooseRestElementType(groupSigs, fixedParamNames0.Length);
                    bool restAllowImplicitToString = groupSigs.Any(sig =>
                    sig.Parameters.Skip(fixedParamNames0.Length).Any(p => p.AllowImplicitToString));
                    (bool groupIsObsolete, string? groupReason) = ResolveNotRecommendedMetadata(groupSigs, recommendGroup.Key);

                    string? doc = null;
                    if (includeDocs)
                        doc = BuildVariadicDoc(referenceSig.Summary, $"R: {string.Join('/', groupReturnTypes)}", null, referenceSig.Notes);

                    output.Add(new DtsCallable(
                        Name: group.Key,
                        GenericParams: "<R extends " + groupConstraint + ">",
                        Params: BuildFixedPlusOptionalParams(fixedParams, "v", maxVarCount, restElementType, restAllowImplicitToString),
                        ReturnType: "R",
                        Doc: doc,
                        Recommend: recommendGroup.Key,
                        IsObsolete: groupIsObsolete,
                        NotRecommendedReason: groupReason,
                        AsyncMode: referenceSig.AsyncMode));
                }

                ApplyCompressionMapping(
                    items,
                    group.Key,
                    x => x.Name,
                    x =>
                    {
                        string genericType = ConvertVarTypeName(byName[x.Name].ReturnType);
                        return genericType;
                    },
                    x => byName[x.Name].Parameters.Count,
                    compressionMap,
                    reverseMap,
                    consumed);
            }
        }

        // 类型后缀压缩：同参不同返回 => 泛型返回
        if (allowTypeSuffixCompression)
        {
            foreach (IGrouping<string, (string Name, string BaseName, string TypeName)> group in methods
                     .Where(s => !excludedNames.Contains(s.MethodName) && !consumed.Contains(s.MethodName))
                     .Select(s =>
                     {
                         foreach (string tag in typeSuffixTags)
                         {
                             if (s.MethodName.Length <= tag.Length) continue;
                             if (!s.MethodName.EndsWith(tag, StringComparison.Ordinal)) continue;

                             string baseName = ConvertCompressionBaseName(s.MethodName.Substring(0, s.MethodName.Length - tag.Length));
                             return (Name: s.MethodName, BaseName: baseName, TypeName: tag);
                         }
                         return default((string Name, string BaseName, string TypeName)?);
                     })
                     .OfType<(string Name, string BaseName, string TypeName)>()
                     .GroupBy(x => x.BaseName, StringComparer.Ordinal))
            {
                (string Name, string BaseName, string TypeName)[] items = group.ToArray();
                if (items.Length < 2) continue;
                CompressionHint? hint = TryGetCompressionHint(methods, CompressionKind.TypeSuffix, group.Key);
                if (hint is null)
                {
                    continue;
                }

                IReadOnlyList<string> allowedTypes = hint.AllowedTypes;
                HashSet<string> allowedSet = new(allowedTypes.Select(ConvertVarTypeName), StringComparer.Ordinal);
                items = items
                    .Where(x => TryConvertTypeSuffixTag(x.TypeName) is var c && c.Success && allowedSet.Contains(c.ConvertedType))
                    .ToArray();
                if (items.Length < 2) continue;

                string[] returnTypes = items
                .Select(x => byName[x.Name].ReturnType)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToArray();
                if (returnTypes.Length < 2) continue;

                string? genericConstraint = BuildGenericConstraint(returnTypes);
                if (genericConstraint is null)
                {
                    continue;
                }

                MethodSignature referenceSig = byName[items[0].Name];
                string params0 = BuildArgParams(referenceSig.Parameters);
                if (items.Any(x => BuildArgParams(byName[x.Name].Parameters) != params0))
                {
                    continue;
                }

                foreach (IGrouping<RecommendLevel, MethodSignature> recommendGroup in items
                             .Select(x => byName[x.Name])
                             .Where(Included)
                             .GroupBy(x => x.Recommend))
                {
                    MethodSignature[] groupSigs = recommendGroup.ToArray();

                    string[] groupReturnTypes = groupSigs
                    .Select(x => x.ReturnType)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(t => t, StringComparer.Ordinal)
                    .ToArray();
                    if (groupReturnTypes.Length == 0)
                    {
                        continue;
                    }

                    string? groupConstraint = BuildGenericConstraint(groupReturnTypes);
                    if (groupConstraint is null)
                    {
                        continue;
                    }

                    MethodSignature groupReference = groupSigs[0];
                    (bool groupIsObsolete, string? groupReason) = ResolveNotRecommendedMetadata(groupSigs, recommendGroup.Key);
                    string? doc = null;
                    if (includeDocs)
                        doc = BuildVariadicDoc(groupReference.Summary, $"R: {string.Join('/', groupReturnTypes)}", argsInfo: null, groupReference.Notes);

                    output.Add(new DtsCallable(
                        Name: group.Key,
                        GenericParams: "<R extends " + groupConstraint + ">",
                        Params: params0,
                        ReturnType: "R",
                        Doc: doc,
                        Recommend: recommendGroup.Key,
                        IsObsolete: groupIsObsolete,
                        NotRecommendedReason: groupReason,
                        AsyncMode: groupReference.AsyncMode));
                }

                foreach ((string Name, string BaseName, string TypeName) item in items)
                {
                    MethodSignature sig = byName[item.Name];
                    string genericType = ConvertVarTypeName(sig.ReturnType);
                    compressionMap[item.Name] = new MemberCompression(group.Key, genericType);
                    AddReverseCompression(reverseMap, group.Key, item.Name, genericType, sig.Parameters.Count);
                }

                foreach ((string Name, string BaseName, string TypeName) item in items)
                {
                    consumed.Add(item.Name);
                }
            }
        }

        // 类型后缀压缩：参数类型变化 => 泛型参数
        if (allowTypeSuffixCompression)
        {
            foreach (IGrouping<string, (string Name, string BaseName, string TypeName)> group in methods
                     .Where(s => !excludedNames.Contains(s.MethodName) && !consumed.Contains(s.MethodName))
                     .Select(s =>
                     {
                         foreach (string tag in typeSuffixTags)
                         {
                             if (s.MethodName.Length <= tag.Length) continue;
                             if (!s.MethodName.EndsWith(tag, StringComparison.Ordinal)) continue;

                             string baseName = ConvertCompressionBaseName(s.MethodName.Substring(0, s.MethodName.Length - tag.Length));
                             return (Name: s.MethodName, BaseName: baseName, TypeName: tag);
                         }
                         return default((string Name, string BaseName, string TypeName)?);
                     })
                     .OfType<(string Name, string BaseName, string TypeName)>()
                     .GroupBy(x => x.BaseName, StringComparer.Ordinal))
            {
                (string Name, string BaseName, string TypeName)[] items = group.ToArray();
                if (items.Length < 2) continue;
                CompressionHint? hint = TryGetCompressionHint(methods, CompressionKind.TypeSuffixParam, group.Key);
                if (hint is null)
                {
                    continue;
                }

                IReadOnlyList<string> allowedTypes = hint.AllowedTypes;
                HashSet<string> allowedSet = new(allowedTypes.Select(ConvertVarTypeName), StringComparer.Ordinal);
                items = items
                    .Where(x => TryConvertTypeSuffixTag(x.TypeName) is var c && c.Success && allowedSet.Contains(c.ConvertedType))
                    .ToArray();
                if (items.Length < 2) continue;

                MethodSignature referenceSig = byName[items[0].Name];
                IReadOnlyList<MethodParameter> referenceParams = referenceSig.Parameters;
                if (referenceParams.Count == 0) continue;

                int varyingIndex = -1;
                bool shapeOk = true;
                foreach ((string Name, string BaseName, string TypeName) item in items)
                {
                    MethodSignature sig = byName[item.Name];
                    if (sig.Parameters.Count != referenceParams.Count)
                    {
                        shapeOk = false;
                        break;
                    }

                    for (int i = 0; i < referenceParams.Count; i++)
                    {
                        MethodParameter refParam = referenceParams[i];
                        MethodParameter curParam = sig.Parameters[i];

                        if (!string.Equals(refParam.EffectiveName, curParam.EffectiveName, StringComparison.Ordinal))
                        {
                            shapeOk = false;
                            break;
                        }

                        string refType = ConvertVarTypeName(refParam.TypeName);
                        string curType = ConvertVarTypeName(curParam.TypeName);
                        if (string.Equals(refType, curType, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        (bool Success, string ConvertedType) suffixConvert = TryConvertTypeSuffixTag(item.TypeName);
                        if (!suffixConvert.Success ||
                            !string.Equals(curType, suffixConvert.ConvertedType, StringComparison.Ordinal))
                        {
                            shapeOk = false;
                            break;
                        }

                        if (varyingIndex < 0)
                        {
                            varyingIndex = i;
                        }
                        else if (varyingIndex != i)
                        {
                            shapeOk = false;
                            break;
                        }
                    }

                    if (!shapeOk)
                    {
                        break;
                    }
                }

                if (!shapeOk || varyingIndex < 0)
                {
                    continue;
                }

                string[] typeList = items
                    .Select(x => byName[x.Name].Parameters[varyingIndex].TypeName)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(t => t, StringComparer.Ordinal)
                    .ToArray();
                string? genericConstraint = BuildGenericConstraint(typeList);
                if (genericConstraint is null)
                {
                    continue;
                }

                string paramText = BuildArgParamsWithOverride(
                    referenceParams,
                    varyingIndex,
                    BuildParamSlotType("T", ParamSlotRule.VariableOrConst));
                foreach (IGrouping<RecommendLevel, MethodSignature> recommendGroup in items
                             .Select(x => byName[x.Name])
                             .Where(Included)
                             .GroupBy(x => x.Recommend))
                {
                    MethodSignature[] groupSigs = recommendGroup.ToArray();

                    string[] groupTypeList = groupSigs
                        .Select(x => x.Parameters[varyingIndex].TypeName)
                        .OfType<string>()
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(t => t, StringComparer.Ordinal)
                        .ToArray();
                    if (groupTypeList.Length == 0)
                    {
                        continue;
                    }

                    string? groupConstraint = BuildGenericConstraint(groupTypeList);
                    if (groupConstraint is null)
                    {
                        continue;
                    }

                    MethodSignature groupReference = groupSigs[0];
                    (bool groupIsObsolete, string? groupReason) = ResolveNotRecommendedMetadata(groupSigs, recommendGroup.Key);
                    string? doc = null;
                    if (includeDocs)
                        doc = BuildVariadicDoc(groupReference.Summary, $"T: {string.Join('/', groupTypeList)}", argsInfo: null, groupReference.Notes);

                    output.Add(new DtsCallable(
                        Name: group.Key,
                        GenericParams: "<T extends " + groupConstraint + ">",
                        Params: paramText,
                        ReturnType: BuildExprReturn(groupReference.ReturnType),
                        Doc: doc,
                        Recommend: recommendGroup.Key,
                        IsObsolete: groupIsObsolete,
                        NotRecommendedReason: groupReason,
                        AsyncMode: groupReference.AsyncMode));
                }

                ApplyCompressionMapping(
                    items,
                    group.Key,
                    x => x.Name,
                    x => TryConvertTypeSuffixTag(x.TypeName).ConvertedType,
                    x => byName[x.Name].Parameters.Count,
                    compressionMap,
                    reverseMap,
                    consumed);
            }
        }

        // 数字后缀压缩
        foreach (IGrouping<string, (string Name, string BaseName, int Suffix)> group in methods
                     .Where(s => !excludedNames.Contains(s.MethodName) && !consumed.Contains(s.MethodName))
                     .Select(s =>
                     {
                         (bool Success, string BaseName, int Suffix) split = TrySplitNumericSuffix(s.MethodName);
                         if (!split.Success)
                         {
                             return default((string Name, string BaseName, int Suffix)?);
                         }
                         return (Name: s.MethodName, BaseName: split.BaseName, Suffix: split.Suffix);
                     })
                     .OfType<(string Name, string BaseName, int Suffix)>()
                     .GroupBy(x => x.BaseName, StringComparer.Ordinal))
        {
            (string Name, string BaseName, int Suffix)[] items = group.OrderBy(x => x.Suffix).ToArray();
            if (items.Length < 1) continue;
            if (TryGetCompressionHint(methods, CompressionKind.NumericSuffix, group.Key) is null)
            {
                continue;
            }

            // 检查基础方法（不带数字后缀）
            string baseName = group.Key;
            MethodSignature? baseMethodSig = null;
            if (byName.TryGetValue(baseName, out MethodSignature? baseSig) &&
                !excludedNames.Contains(baseName) &&
                !consumed.Contains(baseName) &&
                baseSig.MapAlias is null) // 有 mapAlias 的方法不参与压缩
            {
                baseMethodSig = baseSig;
            }

            // 至少需要 2 个签名（基础方法 + 变体，或多个变体）
            int totalCount = items.Length;
            if (baseMethodSig is not null)
                totalCount++;
            if (totalCount < 2) continue;

            if (consumed.Contains(items[0].Name)) continue;

            int[] suffixes = items.Select(x => x.Suffix).Distinct().OrderBy(x => x).ToArray();
            if (!IsContinuousNumericSuffixes(suffixes)) continue;

            HashSet<string> returnTypes = new(StringComparer.Ordinal);
            if (baseMethodSig is not null && baseMethodSig.ReturnType is not null)
            {
                returnTypes.Add(baseMethodSig.ReturnType);
            }
            foreach ((string Name, string BaseName, int Suffix) item in items)
            {
                MethodSignature sig = byName[item.Name];
                if (sig.ReturnType is not null)
                    returnTypes.Add(sig.ReturnType);
            }
            if (returnTypes.Count > 1) continue;

            // 收集所有签名（包括基础方法）
            List<MethodSignature> allSigs = items.Select(x => byName[x.Name]).ToList();
            if (baseMethodSig is not null)
            {
                allSigs.Insert(0, baseMethodSig);
            }

            foreach (IGrouping<RecommendLevel, MethodSignature> recommendGroup in allSigs
                         .Where(Included)
                         .GroupBy(x => x.Recommend))
            {
                MethodSignature[] groupSigs = recommendGroup.ToArray();
                if (groupSigs.Length == 0)
                {
                    continue;
                }

                HashSet<string> groupReturnTypes = new(StringComparer.Ordinal);
                foreach (MethodSignature sig in groupSigs)
                {
                    if (sig.ReturnType is not null)
                    {
                        groupReturnTypes.Add(sig.ReturnType);
                    }
                }
                if (groupReturnTypes.Count > 1)
                {
                    continue;
                }

                // 使用最小参数数量的签名作为固定参数参考
                MethodSignature minParamSig = groupSigs.OrderBy(x => x.Parameters.Count).First();
                string[] fixedParamNames = TrimTrailingVarParams(minParamSig.Parameters.Select(p => p.EffectiveName).ToArray());
                MethodParameter[] fixedParams = minParamSig.Parameters.Take(fixedParamNames.Length).ToArray();
                int maxTotalParams = groupSigs.Select(x => x.Parameters.Count).Max();
                int maxVarCount = Math.Max(0, maxTotalParams - fixedParamNames.Length);
                string restElementType = ChooseRestElementType(groupSigs, fixedParamNames.Length);
                bool restAllowImplicitToString = groupSigs.Any(sig =>
                    sig.Parameters.Skip(fixedParamNames.Length).Any(p => p.AllowImplicitToString));
                (bool groupIsObsolete, string? groupReason) = ResolveNotRecommendedMetadata(groupSigs, recommendGroup.Key);
                string? groupReturnType = null;
                if (groupReturnTypes.Count != 0)
                    groupReturnType = groupReturnTypes.Single();

                string? doc = null;
                if (includeDocs)
                    doc = BuildVariadicDoc(minParamSig.Summary, null, null, minParamSig.Notes);

                output.Add(new DtsCallable(
                    Name: baseName,
                    GenericParams: null,
                    Params: BuildFixedPlusOptionalParams(fixedParams, "v", maxVarCount, restElementType, restAllowImplicitToString),
                    ReturnType: BuildExprReturn(groupReturnType),
                    Doc: doc,
                    Recommend: recommendGroup.Key,
                    IsObsolete: groupIsObsolete,
                    NotRecommendedReason: groupReason,
                    AsyncMode: minParamSig.AsyncMode));
            }

            ApplyCompressionMapping(
                items,
                baseName,
                x => x.Name,
                _ => null,
                x => byName[x.Name].Parameters.Count,
                compressionMap,
                reverseMap,
                consumed);

            // 基础方法也加入 consumed
            if (baseMethodSig is not null)
            {
                consumed.Add(baseName);
                compressionMap.TryAdd(baseName, new MemberCompression(baseName, null));
                AddReverseCompression(reverseMap, baseName, baseName, null, baseMethodSig.Parameters.Count);
            }
        }

        // 非压缩方法
        foreach (MethodSignature sig in methods.OrderBy(s => s.MethodName, StringComparer.Ordinal))
        {
            if (excludedNames.Contains(sig.MethodName)) continue;
            if (consumed.Contains(sig.MethodName)) continue;
            if (!Included(sig)) continue;

            // 使用 mapAlias 作为输出名（如果存在）
            string outputName = sig.MethodName;
            if (sig.MapAlias is not null)
                outputName = sig.MapAlias;

            string? doc = null;
            if (includeDocs)
                doc = BuildMethodDoc(sig.Summary, sig.Notes);

            output.Add(new DtsCallable(
                Name: outputName,
                GenericParams: null,
                Params: BuildArgParams(sig.Parameters, sig.GetFullPath()),
                ReturnType: BuildExprReturn(sig.ReturnType),
                Doc: doc,
                Recommend: sig.Recommend,
                IsObsolete: sig.IsObsolete,
                NotRecommendedReason: ResolveNotRecommendedReason(sig),
                AsyncMode: sig.AsyncMode));
        }

        return output;
    }

    private static void AddReverseCompression(
        Dictionary<string, List<MemberCompressionEntry>> reverseMap,
        string compressedName,
        string originalName,
        string? genericBeTypeName,
        int parameterCount)
    {
        if (!reverseMap.TryGetValue(compressedName, out List<MemberCompressionEntry>? list))
        {
            list = [];
            reverseMap[compressedName] = list;
        }

        if (!list.Any(x => x.OriginalName == originalName))
        {
            list.Add(new MemberCompressionEntry(originalName, genericBeTypeName, parameterCount));
        }
    }

    private static void ApplyCompressionMapping<T>(
        IReadOnlyList<T> items,
        string compressedName,
        Func<T, string> getOriginalName,
        Func<T, string?> getGenericBeTypeName,
        Func<T, int> getParameterCount,
        Dictionary<string, MemberCompression> compressionMap,
        Dictionary<string, List<MemberCompressionEntry>> reverseMap,
        HashSet<string> consumed)
    {
        foreach (T item in items)
        {
            string originalName = getOriginalName(item);
            consumed.Add(originalName);

            string? genericBeTypeName = getGenericBeTypeName(item);
            int parameterCount = getParameterCount(item);
            compressionMap[originalName] = new MemberCompression(compressedName, genericBeTypeName);
            AddReverseCompression(reverseMap, compressedName, originalName, genericBeTypeName, parameterCount);
        }
    }

    private static void AppendIdentityCompression(
        IReadOnlyList<MethodSignature> methods,
        Dictionary<string, MemberCompression> output,
        Dictionary<string, List<MemberCompressionEntry>> reverseMap)
    {
        foreach (MethodSignature method in methods)
        {
            if (!output.ContainsKey(method.MethodName))
            {
                output[method.MethodName] = new MemberCompression(method.MethodName, null);
            }
            AddReverseCompression(reverseMap, method.MethodName, method.MethodName, null, method.Parameters.Count);
        }
    }

    private static string? ResolveNotRecommendedReason(MethodSignature sig)
    {
        if (!sig.ShouldMarkNotRecommended)
        {
            return null;
        }

        if (sig.NotRecommendedReason is not null)
        {
            return sig.NotRecommendedReason;
        }

        if (sig.IsObsolete)
            return BuiltinRecommendationRules.DefaultObsoleteReason;
        return RecommendationReasons.DefaultNotRecommendedReason;
    }

    private static (bool IsObsolete, string? Reason) ResolveNotRecommendedMetadata(
        IReadOnlyList<MethodSignature> signatures,
        RecommendLevel recommend)
    {
        bool hasObsolete = signatures.Any(static s => s.IsObsolete);
        if (recommend != RecommendLevel.NotRecommended && !hasObsolete)
        {
            return (false, null);
        }

        string[] reasons = signatures
            .Select(ResolveNotRecommendedReason)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        string reason = string.Join("；", reasons);
        return (hasObsolete, reason);
    }

    private static bool ShouldEmitNotRecommendedDecorator(DtsCallable callable)
    {
        return callable.Recommend == RecommendLevel.NotRecommended || callable.IsObsolete;
    }

    private static string BuildNotRecommendedDecorator(DtsCallable callable)
    {
        bool isObsolete = callable.IsObsolete;
        if (callable.NotRecommendedReason is null)
        {
            throw new InvalidOperationException("缺少不推荐原因");
        }

        string obsoleteText = "false";
        if (isObsolete)
        {
            obsoleteText = "true";
        }

        return $"// @bsNotRecommended({{ obsolete: {obsoleteText}, reason: {TsViewTypeUtils.ToTsStringLiteral(callable.NotRecommendedReason)} }})";
    }

    private static void AppendDocLine(StringBuilder sb, string indent, string? summary)
    {
        if (summary is null)
        {
            return;
        }

        string line = summary.Replace("*/", "*\\/");
        sb.Append(indent).Append("/** ").Append(line).AppendLine(" */");
    }

    private static string WrapReturnTypeIfAsync(string returnType, SdkAsyncMode asyncMode)
    {
        if (asyncMode is SdkAsyncMode.Sync)
            return returnType;
        return $"Promise<{returnType}>";
    }

    private static DeclLineRange AppendCallableMember(
        StringBuilder sb,
        string indent,
        DtsCallable callable,
        bool emitNotRecommendedDecorators)
    {
        if (emitNotRecommendedDecorators && ShouldEmitNotRecommendedDecorator(callable))
        {
            sb.Append(indent).AppendLine(BuildNotRecommendedDecorator(callable));
        }

        AppendDocLine(sb, indent, callable.Doc);
        int startLine = LineCount(sb);
        StringBuilder declaration = new();
        declaration.Append(ToTsMemberName(callable.Name));
        if (callable.GenericParams is not null)
        {
            declaration.Append(callable.GenericParams);
        }
        declaration.Append('(').Append(callable.Params).Append("): ").Append(WrapReturnTypeIfAsync(callable.ReturnType, callable.AsyncMode)).Append(';');
        string header = declaration.ToString();
        sb.Append(indent).AppendLine(header);
        return new DeclLineRange(header, startLine, startLine);
    }

    private static string? BuildMethodDoc(string? summary, string? notes)
    {
        if (summary is null && notes is null)
            return null;

        if (notes is null)
            return summary;

        if (summary is null)
            return "⚠ " + notes;

        return summary + " ⚠ " + notes;
    }

    private static string BuildVariadicDoc(string? summary, string? genericInfo, string? argsInfo, string? notes = null)
    {
        if (summary is not { } s)
            return string.Empty;
#pragma warning disable TK010 // SDK 文档字符串去除前后空白后拼接
        string head = s.Trim();
        List<string> parts = new(capacity: 4) { head };
        if (genericInfo is not null)
        {
            parts.Add(genericInfo.Trim());
        }
        if (argsInfo is not null)
        {
            parts.Add(argsInfo.Trim());
        }
        if (notes is not null)
        {
            parts.Add("⚠ " + notes.Trim());
        }
#pragma warning restore TK010
        return string.Join("；", parts);
    }

    private static string BuildArgParams(IReadOnlyList<MethodParameter> parameters, string? fullName = null)
    {
        if (parameters.Count == 0)
        {
            return string.Empty;
        }

        string[] names = ResolveParamNames(parameters);
        StringBuilder sb = new();
        for (int i = 0; i < parameters.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(names[i]).Append(": ").Append(ToBeParamTypeName(parameters[i], fullName, i));
        }

        return sb.ToString();
    }

    private static string BuildAutoParamBaseName(string? typeName)
    {
        string converted = ConvertVarTypeName(typeName);
        return converted;
    }

    private static ParamSlotRule ResolveParamSlotRule(string? typeName, bool allowImplicitToString, string? fullName, int paramIndex)
    {
        if (string.Equals(fullName, "create.canvas", StringComparison.Ordinal) && paramIndex == 0)
        {
            return ParamSlotRule.CanvasCreateConstOnly;
        }

        if (string.Equals(fullName, "create.icon", StringComparison.Ordinal) && paramIndex == 0)
        {
            return ParamSlotRule.IconCreateConstOnly;
        }

        if (typeName is null)
        {
            return ParamSlotRule.AnyParam;
        }

        string converted = ConvertVarTypeName(typeName);
        if (converted.Equals(nameof(BescriptVarType.String), StringComparison.Ordinal))
        {
            if (allowImplicitToString)
            {
                return ParamSlotRule.StringConvertible;
            }
        }

        if (converted.Equals(nameof(BescriptVarType.Any), StringComparison.Ordinal))
        {
            return ParamSlotRule.AnyParam;
        }

        if (converted.Equals(nameof(BescriptVarType.Number), StringComparison.Ordinal))
        {
            return ParamSlotRule.NumberParam;
        }

        return ParamSlotRule.VariableOrConst;
    }

    private static string BuildParamSlotType(string baseTypeName, ParamSlotRule rule)
    {
        return rule switch
        {
            ParamSlotRule.VariableOnly => baseTypeName,
            ParamSlotRule.VariableOrConst => baseTypeName + " | Const<" + baseTypeName + ">",
            ParamSlotRule.ConstOnly => "Const<" + baseTypeName + ">",
            ParamSlotRule.StringConvertible => "BeStringConvertible",
            ParamSlotRule.AnyParam => "BeAnyParam",
            ParamSlotRule.NumberParam => "Const<BeNumber> | BeFloat | BeLong",
            ParamSlotRule.CanvasCreateConstOnly => "Const<BeCanvas> | BeCanvasCreateConst",
            ParamSlotRule.IconCreateConstOnly => "Const<BeIcon> | BeIconCreateConst",
            _ => throw new InvalidOperationException("未知参数位规则")
        };
    }

    private static string ToBeParamTypeName(MethodParameter parameter, string? fullName = null, int paramIndex = -1)
    {
        string baseTypeName;
        ParamSlotRule rule;
        if (parameter.EnumType is not null)
        {
            baseTypeName = $"BeEnum<{parameter.EnumType.Name}>";
            rule = ParamSlotRule.ConstOnly;
        }
        else
        {
            string converted = ConvertVarTypeName(parameter.TypeName);
            baseTypeName = ToBeTypeName(converted);
            rule = ResolveParamSlotRule(parameter.TypeName, parameter.AllowImplicitToString, fullName, paramIndex);
        }

        return BuildParamSlotType(baseTypeName, rule);
    }

    private static string BuildExprReturn(string? returnType)
    {
        if (returnType is null)
        {
            return "void";
        }

        string t = ConvertVarTypeName(returnType);
        return ToBeTypeName(t);
    }

    /// <summary>
    /// 将原版方法名转换为 TS 视图成员名
    /// </summary>
    internal static string ToTsMemberName(string name)
    {
        string escaped = TsViewIdentifierMap.ToTsIdentifier(name);
        if (TsViewIdentifierMap.IsValidIdentifier(escaped))
        {
            return escaped;
        }

        return TsViewTypeUtils.ToTsStringLiteral(name);
    }

    private static string[] TrimTrailingVarParams(string[] parameterNames)
    {
        int n = parameterNames.Length;
        while (n > 0)
        {
            string name = parameterNames[n - 1];
            if (IsTrailingVarParamName(name))
            {
                n--;
                continue;
            }
            break;
        }

        if (n == parameterNames.Length)
        {
            return parameterNames;
        }

        string[] trimmed = new string[n];
        Array.Copy(parameterNames, trimmed, n);
        return trimmed;
    }

    private static bool IsTrailingVarParamName(string name)
    {
        if (name.Length < 2)
        {
            return false;
        }

        bool hasKnownPrefix = name.StartsWith("param", StringComparison.Ordinal) ||
            name.StartsWith("data", StringComparison.Ordinal);

        if (!hasKnownPrefix)
        {
            return false;
        }

        int prefixLength = "data".Length;
        if (name.StartsWith("param", StringComparison.Ordinal))
        {
            prefixLength = "param".Length;
        }
        for (int i = prefixLength; i < name.Length; i++)
        {
            if (!char.IsDigit(name[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildFixedPlusOptionalParams(
        MethodParameter[] fixedParams,
        string optionalBaseName,
        int optionalCount,
        string optionalElementType,
        bool optionalAllowImplicitToString)
    {
        if (fixedParams.Length == 0 && optionalCount <= 0)
        {
            return string.Empty;
        }

        HashSet<string> usedNames = new(StringComparer.Ordinal);
        List<string> parts = new(fixedParams.Length + Math.Max(0, optionalCount));
        string baseName = optionalBaseName;

        // 固定参数
        for (int i = 0; i < fixedParams.Length; i++)
        {
            MethodParameter p = fixedParams[i];
            string paramName = TsViewIdentifierMap.ToTsIdentifier(baseName + (i + 1));
            paramName = TsViewIdentifierMap.EnsureUniqueIdentifier(paramName, usedNames);
            parts.Add(paramName + ": " + ToBeParamTypeName(p));
        }

        // 可选参数：从固定参数数量+1开始编号
        if (optionalCount > 0)
        {
            int startIndex = fixedParams.Length + 1;
            for (int i = 0; i < optionalCount; i++)
            {
                string optionalName = TsViewIdentifierMap.ToTsIdentifier(baseName + (startIndex + i));
                optionalName = TsViewIdentifierMap.EnsureUniqueIdentifier(optionalName, usedNames);
                string converted = ConvertVarTypeName(optionalElementType);
                string baseTypeName = ToBeTypeName(converted);
                ParamSlotRule rule = ResolveParamSlotRule(optionalElementType, optionalAllowImplicitToString, null, -1);
                parts.Add(optionalName + "?: " + BuildParamSlotType(baseTypeName, rule));
            }
        }

        return string.Join(", ", parts);
    }

    private static string ChooseRestElementType(IEnumerable<MethodSignature> sigs, int fixedParamCount)
    {
        string? elementType = null;

        foreach (MethodSignature sig in sigs)
        {
            for (int i = fixedParamCount; i < sig.Parameters.Count; i++)
            {
                string t = ConvertVarTypeName(sig.Parameters[i].TypeName);

                if (elementType is null)
                {
                    elementType = t;
                    continue;
                }

                if (!string.Equals(elementType, t, StringComparison.Ordinal))
                {
                    return "Any";
                }
            }
        }

        if (elementType is null)
        {
            throw new InvalidOperationException("可变参数签名缺少元素类型");
        }

        return elementType;
    }

    private static MethodSignature[] QueryAllBuiltinMethods(BuiltinMethodRegistry registry, MethodCategory category)
    {
        const int pageSize = 1000;
        List<MethodSignature> results = new(capacity: pageSize);

        int skip = 0;
        while (true)
        {
            MethodQueryResult page = registry.Query(
                namePattern: null,
                category: category,
                ownerType: null,
                skip: skip,
                take: pageSize);

            if (page.Items.Count == 0)
            {
                break;
            }

            results.AddRange(page.Items);
            skip += page.Items.Count;

            if (skip >= page.Total)
            {
                break;
            }
        }

        return results.ToArray();
    }

    // ============================================================================
    // Global functions (namespace G)
    // ============================================================================

    private static void AppendGlobalNamespaceG(
        StringBuilder sb,
        IReadOnlyList<MethodSignature> globalFuncs,
        bool includeDocs,
        IReadOnlySet<string>? allowedFullNames,
        Func<MethodSignature, bool>? includeMethod,
        bool emitNotRecommendedDecorators,
        Dictionary<string, DeclLineRange>? methodLineMap)
    {
        sb.AppendLine("/**");
        sb.AppendLine(" * BeScript 全局函数命名空间");
        sb.AppendLine(" *");
        sb.AppendLine(" * 包含原版 BeScript 中 fun 关键字定义的全部全局函数");
        sb.AppendLine(" */");
        const string header = "declare namespace G";
        sb.Append(header).AppendLine(" {");

        NamespaceNode node = BuildGlobalFuncNamespaceTree(globalFuncs, includeDocs, allowedFullNames, includeMethod);
        WriteNamespaceNode(sb, node, indent: "  ", methodNamePrefix: string.Empty, hasMethodNamePrefix: false, emitNotRecommendedDecorators, methodLineMap);

        sb.AppendLine("}");
    }

    private static NamespaceNode BuildGlobalFuncNamespaceTree(
        IReadOnlyList<MethodSignature> globalFuncs,
        bool includeDocs,
        IReadOnlySet<string>? allowedFullNames,
        Func<MethodSignature, bool>? includeMethod)
    {
        List<(string FullName, DtsCallable Callable)> output = new(capacity: globalFuncs.Count);

        GlobalCompressionResult globalResult = BuildGlobalCompressedCallables(globalFuncs, includeDocs, includeMethod);
        IReadOnlyList<DtsCallable> callables = globalResult.Callables;
        foreach (DtsCallable callable in callables)
        {
            if (callable.Name is "break" or "continue" or "if" or "while")
            {
                continue;
            }

            string fullName = callable.Name;
            output.Add((fullName, new DtsCallable(
                Name: SplitGlobalPath(fullName)[^1],
                GenericParams: callable.GenericParams,
                Params: callable.Params,
                ReturnType: callable.ReturnType,
                Doc: callable.Doc,
                Recommend: callable.Recommend,
                IsObsolete: callable.IsObsolete,
                NotRecommendedReason: callable.NotRecommendedReason,
                AsyncMode: callable.AsyncMode)));
        }

        (string FullName, DtsCallable Callable)[] filtered;
        if (allowedFullNames is null)
            filtered = output.ToArray();
        else
            filtered = output.Where(x => allowedFullNames.Contains(x.FullName)).ToArray();

        NamespaceNode root = new();
        foreach ((string fullName, DtsCallable callable) in filtered.OrderBy(x => x.FullName, StringComparer.Ordinal))
        {
            string[] segments = SplitGlobalPath(fullName);
            if (segments.Length == 0)
            {
                continue;
            }

            NamespaceNode current = root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                string seg = segments[i];
                if (!current.Children.ContainsKey(seg))
                {
                    current.Children[seg] = new NamespaceNode();
                }
                current = current.Children[seg];
            }

            current.Functions.Add(callable);
        }

        return root;
    }

    private static void WriteNamespaceNode(
        StringBuilder sb,
        NamespaceNode node,
        string indent,
        string methodNamePrefix,
        bool hasMethodNamePrefix,
        bool emitNotRecommendedDecorators,
        Dictionary<string, DeclLineRange>? methodLineMap)
    {
        foreach (DtsCallable fn in node.Functions.OrderBy(static x => x.Name, StringComparer.Ordinal))
        {
            if (emitNotRecommendedDecorators && ShouldEmitNotRecommendedDecorator(fn))
            {
                sb.Append(indent).AppendLine(BuildNotRecommendedDecorator(fn));
            }
            AppendDocLine(sb, indent, fn.Doc);
            int startLine = LineCount(sb);
            string methodName = hasMethodNamePrefix ? methodNamePrefix + "." + fn.Name : fn.Name;
            string methodPath = BuildTsSdkPath(MethodCategory.GlobalFunc, string.Empty, methodName);
            StringBuilder declaration = new();
            declaration.Append("function ").Append(TsViewIdentifierMap.ToTsIdentifier(fn.Name));
            if (fn.GenericParams is not null)
            {
                declaration.Append(fn.GenericParams);
            }
            declaration.Append('(').Append(fn.Params).Append("): ").Append(WrapReturnTypeIfAsync(fn.ReturnType, fn.AsyncMode)).Append(';');
            string header = declaration.ToString();
            sb.Append(indent).AppendLine(header);
            RecordDecl(methodLineMap, methodPath, new DeclLineRange(header, startLine, startLine));
        }

        foreach ((string name, NamespaceNode child) in node.Children)
        {
            sb.Append(indent).Append("namespace ").Append(TsViewIdentifierMap.ToTsIdentifier(name)).AppendLine(" {");
            string childMethodNamePrefix = hasMethodNamePrefix ? methodNamePrefix + "." + name : name;
            WriteNamespaceNode(sb, child, indent + "  ", childMethodNamePrefix, hasMethodNamePrefix: true, emitNotRecommendedDecorators, methodLineMap);
            sb.Append(indent).AppendLine("}");
        }
    }

    private static string[] SplitGlobalPath(string fullName)
    {
        // 全局函数名是原版注册的字符串标识；'/' 是名字的一部分，不是命名空间分隔符。
        return fullName.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsContinuousNumericSuffixes(int[] values)
    {
        if (values.Length == 0)
        {
            return false;
        }

        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] != values[i - 1] + 1)
            {
                return false;
            }
        }

        return true;
    }

    internal sealed record DeclLineRange(string Header, int StartLine, int EndLine);

    private static Dictionary<string, DeclLineRange> s_methodDeclLineMap = new();

    internal static DeclLineRange? GetMethodDeclLineRange(string tsPath)
    {
        return s_methodDeclLineMap.GetValueOrDefault(tsPath);
    }

    private static int LineCount(StringBuilder sb)
    {
        int count = 1;
        for (int i = 0; i < sb.Length; i++)
        {
            if (sb[i] == '\n') count++;
        }
        return count;
    }

    private static void RecordDecl(Dictionary<string, DeclLineRange>? methodLineMap, string tsPath, DeclLineRange range)
    {
        if (methodLineMap is not null)
            methodLineMap[tsPath] = range;
    }
}
