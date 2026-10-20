namespace Toolkit.Core.Bescript;

internal enum BescriptTokenKind
{
    BeginCode,
    EndCode,
    HeaderKey,
    HeaderValue,
    LineNumber,
    Indent,
    DisabledPrefix,
    CommentPrefix,
    StatementKeyword,
    CallKind,
    Identifier,
    TypeName,
    Assign,
    Dot,
    OpenParen,
    CloseParen,
    OpenBrace,
    CloseBrace,
    ArgumentSeparator,
    RawValue,
    RawCanvasBlock,
    NewLine
}

/// <summary>
/// 单个词法 token。Column 为 1-based（相对原始行，含行号前缀与缩进），Length 为字符数。
/// </summary>
internal readonly record struct BescriptToken(
    BescriptTokenKind Kind,
    string Text,
    int Line,
    int Column,
    int Length);

internal sealed record BescriptLineTokens(
    int Line,
    string Text,
    IReadOnlyList<BescriptToken> Tokens);

internal sealed record BescriptTokenStream(IReadOnlyList<BescriptLineTokens> Lines);
