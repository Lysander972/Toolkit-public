namespace Toolkit.Core.TsView;

/// <summary>
/// TS 视图调用类型（语义对应 BES fun/varf/act/dvf）。
/// </summary>
internal enum TsViewCallKind
{
    Fun,
    Varf,
    Act,
    Dvf
}

/// <summary>
/// TS 视图参数类型。
/// </summary>
internal enum TsViewArgKind
{
    Identifier,
    ThisMember,
    StringLiteral,
    NumberLiteral,
    BooleanLiteral,
    BeConst,
    Undefined,
    Other
}

/// <summary>
/// TS 视图参数信息。
/// </summary>
internal sealed record TsViewArg(
    TsViewArgKind Kind,
    string Raw,
    string? StringValue = null,
    string? BeTypeName = null,
    string? EnumTypeName = null
);

/// <summary>
/// TS 视图调用表达式（AST 解析后的结构化表示）。
/// </summary>
internal sealed record TsViewCallExpr(
    TsViewCallKind Kind,
    string? MethodPath,
    string? TargetType,
    string? Target,
    string? MethodName,
    string? GenericBeTypeName,
    TsViewArg? ActTarget,
    IReadOnlyList<TsViewArg> Args,
    bool UseThisTarget = false,
    string? ActTargetType = null
);
