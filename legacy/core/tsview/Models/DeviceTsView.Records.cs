using Toolkit.Core.Bescript;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

public sealed partial class DeviceTsView
{
    /// <summary>
    /// 机械产生器(1110)虚拟核心类名后缀。
    /// 1110 双类结构：外壳类 Device_XXX（对外）+ 虚拟核心类 Device_XXX_VirtualCore（对内）。
    /// </summary>
    internal const string VirtualCoreSuffix = "_VirtualCore";

    /// <summary>
    /// 零件脚本列表项。IsCodeRepo 表示 CodeRepo 引用。
    /// </summary>
    private sealed record ScriptEntry(
        string Name,
        string Guid,
        bool IsCodeRepo,
        string? SourcePath,
        BescriptScriptInfo ScriptInfo,
        bool IsPrivateFromList = false
    );

    private sealed record AllowedEventInfo(string EventName, EventSignature Signature);

    internal sealed record EventDir(string EventName, string Directory);

    internal readonly record struct ParsedMethodSignature(
        string Name,
        bool IsPublic,
        bool IsPrivate,
        bool IsProtected,
        bool IsStatic,
        IReadOnlyList<TsViewParamSpec> Params);

    /// <summary>
    /// 解析属性赋值形式的脚本声明行。
    /// 格式：modifier name = BeScript/BeScriptAsync({...})((params) => {
    /// 或：modifier name = BeScript/BeScriptAsync({...})(function (params) {
    /// 生成侧和校验侧规则不同: 生成侧按照是否事件脚本明确分层;写回时为解决幻觉导致的格式滥用,故进行合理兼容(此处格式无任何语义差异,仅为TS语法需求)
    /// 全部使用结构化括号匹配，不使用 IndexOf 文本匹配。
    /// </summary>
    private sealed record EventParamSpec(
        string TsName,
        string BeTypeName,
        string RawName,
        string RawTypeName,
        string? Description,
        MethodEnumType? EnumType = null
    );

    internal sealed record InlineCanvasLiteral(
        int LineNumber,
        string RawCanvas
    );

    internal sealed record ScriptUpdate(
        string SourcePath,
        string ScriptName,
        string MethodName,
        bool IsPrivate,
        IReadOnlyList<TsViewParamSpec> Params,
        string ReturnType,
        string? ReturnVarName,
        BescriptAst BodyAst,
        bool Debug,
        bool Skip,
        bool Private,
        string? Comment,
        IReadOnlyList<int>? Color,
        string Guid,
        bool IsCodeRepo
    )
    {
        public IReadOnlyList<InlineCanvasLiteral> InlineCanvases { get; init; } = Array.Empty<InlineCanvasLiteral>();
    }

    private sealed record ParsedMethod(
        int DocStartLine,
        string ListPath,
        string ScriptName,
        string? SourcePath,
        string Guid,
        bool IsCodeRepo,
        bool IsPrivate = false
    );

    internal sealed record DesiredScriptListEntry(string Name, string Guid, bool IsCodeRepo, bool IsPrivate = false);

    internal sealed record DesiredGlobalVar(string Name, BescriptVarType Type, string? Comment = null);

    internal sealed record ScriptRemoval(string ScriptName, string ScriptPath);

    private sealed record EventMethodBody(IReadOnlyList<MethodCallInfo> MethodCalls);

    /// <summary>
    /// 不完整的构建需求实体集合（从官方解析后的写回结果还原）。
    /// 缺失 device.json 中的部分字段，需用只读零件实体补全。
    /// </summary>
    internal sealed record IncompleteBuildRequirements(
        string DeviceRelativePath,
        IReadOnlyList<ScriptUpdate> ScriptUpdates,
        IReadOnlyDictionary<string, List<DesiredScriptListEntry>> DesiredScriptLists,
        IReadOnlyList<DesiredGlobalVar> DesiredGlobalVars,
        IReadOnlyList<ScriptRemoval> Removals,
        IReadOnlyList<ScriptUpdate> BaseScriptUpdates
    );

    /// <summary>
    /// 完整的构建需求实体集合（补全后）。
    /// 包含完整的 WritableDeviceEntity，可统一持久化。
    /// </summary>
    internal sealed record CompleteBuildRequirements(
        WritableDeviceEntity Device,
        IReadOnlyList<ScriptUpdate> ScriptUpdates,
        IReadOnlyDictionary<string, List<DesiredScriptListEntry>> DesiredScriptLists,
        IReadOnlyList<DesiredGlobalVar> DesiredGlobalVars,
        IReadOnlyList<ScriptRemoval> Removals
    );
}
