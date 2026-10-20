namespace Toolkit.Core.TsView;

internal sealed class DeviceTsViewBuildContext
{
    public required IReadOnlyList<string> ReferencePaths { get; init; }
    public required IReadOnlyList<string> CommentLines { get; init; }
    public required IReadOnlyList<string> ExtraCommentLines { get; init; }
    public required string OuterNamespace { get; init; }
    public required string DeviceClassName { get; init; }
    public required string BaseTypeName { get; init; }
    public required bool IsBirthBuilding { get; init; }
    public required IReadOnlyList<string> AllowedEvents { get; init; }
    public required IReadOnlyList<TsViewGlobalVarInfo> GlobalVars { get; init; }
    public required IReadOnlyList<ScriptMethodInfo> ScriptMethodInfos { get; init; }
    public required IReadOnlyList<EventMethodGroupInfo> EventMethodInfos { get; init; }
}

internal sealed record TsViewGlobalVarInfo(
    string OriginalName,
    string TsName,
    string BeTypeName,
    string InitValue,
    string? Comment);

internal sealed record ScriptMethodInfo(
    string OriginalName,
    string MethodName,
    string? SourcePath,
    string? Guid,
    bool IsCodeRepo,
    DeviceTsViewBescriptHeader Header,
    bool IsAsync,
    bool IsPrivate,
    IReadOnlyList<ParameterInfo> Parameters,
    string ReturnType,
    DeviceTsViewScriptBody Body,
    IReadOnlyList<ParameterInfo>? RawParameters = null,
    string? RawRetVar = null);

internal sealed record ParameterInfo(string Name, string TypeName);

internal sealed record DeviceTsViewBescriptHeader(
    string Name,
    string? Comment,
    bool Debug,
    bool Skip,
    bool Private,
    string? Guid,
    DeviceTsViewBescriptColor? Color,
    IReadOnlyList<ParameterInfo> Parameters,
    ParameterInfo? ReturnParameter);

internal sealed record DeviceTsViewBescriptColor(int R, int G, int B);

internal sealed record DeviceTsViewScriptBody(
    IReadOnlyList<DeviceTsViewStatement> Statements,
    IReadOnlyList<DeviceTsViewScriptBodyLine> Lines);

internal sealed record DeviceTsViewScriptBodyLine(int LineNumber, string LineText);

internal sealed record DeviceTsViewStatement(
    string Kind,
    bool IsDisabled,
    int? SourceLine,
    DeviceTsViewExpression? Expression = null,
    DeviceTsViewStatement? ThenStatement = null,
    DeviceTsViewStatement? Body = null,
    IReadOnlyList<DeviceTsViewStatement>? Statements = null,
    string? Text = null,
    DeviceTsViewVariableDeclarationList? DeclarationList = null);

internal sealed record DeviceTsViewVariableDeclarationList(
    string VariableKind,
    IReadOnlyList<DeviceTsViewVariableDeclaration> Declarations);

internal sealed record DeviceTsViewVariableDeclaration(
    string Name,
    string? TypeName,
    DeviceTsViewExpression? Initializer);

internal sealed record DeviceTsViewExpression(
    string Kind,
    string? Text = null,
    DeviceTsViewExpression? Expression = null,
    string? Name = null,
    IReadOnlyList<DeviceTsViewExpression>? Arguments = null,
    IReadOnlyList<string>? TypeArguments = null,
    DeviceTsViewExpression? Left = null,
    string? Operator = null,
    DeviceTsViewExpression? Right = null,
    DeviceTsViewExpression? Argument = null,
    string? TypeName = null,
    IReadOnlyList<DeviceTsViewExpression>? Elements = null,
    IReadOnlyList<DeviceTsViewObjectProperty>? Properties = null,
    bool IsMultiLine = false);

internal sealed record DeviceTsViewObjectProperty(
    string Name,
    DeviceTsViewExpression Value);

internal sealed record EventMethodGroupInfo(
    string EventName,
    string RegistrationEventName,
    IReadOnlyList<EventParamInfo> EventParams,
    IReadOnlyList<ScriptMethodInfo> Scripts,
    string Summary);

internal sealed record EventParamInfo(
    string TsName,
    string BeTypeName,
    string? RawTypeName,
    string? Description);
