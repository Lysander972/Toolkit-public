using Toolkit.Core.Bescript;

namespace Toolkit.Core.TsView;

internal sealed record BeScriptDecoratorInfo(
    string Name,
    string? RetVar,
    string? RetVarType,
    bool? Debug,
    bool? Skip,
    bool? Private,
    string? Comment,
    IReadOnlyList<int>? Color,
    string? RawParams,
    string? RawRetVar,
    string? Repo = null,
    bool IsAsync = false);

/// <summary>
/// 扫描提取出的 TS 视图方法元数据。
/// </summary>
internal sealed record ScannedMethod(
    string Name,
    string RawName,
    bool IsPrivate,
    bool IsProtected,
    IReadOnlyList<TsViewParamSpec> Params,
    IReadOnlyList<(int LineNumber, string LineText)> BodyLines,
    int DocStartLine,
    int DeclLine,
    int EndLine,
    string? CodeRepoGuid,
    string? RetVarFromDecorator,
    string? RetVarTypeFromDecorator,
    bool HasBeScriptDecorator,
    BeScriptDecoratorInfo? BeScriptInfo
);

/// <summary>
/// 扫描提取出的 TS 视图全局变量元数据。
/// </summary>
internal sealed record ScannedGlobalVar(
    string Name,
    string RawName,
    BescriptVarType Type,
    string? Comment,
    int LineNumber
);

/// <summary>
/// 扫描到的事件组信息。
/// </summary>
internal sealed record ScannedEventGroup(
    string EventName,
    IReadOnlyList<ScannedMethod> Members);
