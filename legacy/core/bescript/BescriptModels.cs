namespace Toolkit.Core.Bescript;

// ========== 调用签名相关 ==========

public enum BescriptCallKind
{
    None,
    Fun,
    Toolf,
    Varf,
    Act,
    Dvf
}

/// <summary>
/// 签名（点分隔段）在命令文本中的偏移。段偏移相对节点 Text 的字符位置。
/// </summary>
public sealed record BescriptCallSegmentSpan(int Offset, int Length);

public sealed record BescriptCallArgument(
    bool IsVariable,
    string TypeName,
    string RawText,
    string DecodedText,
    BescriptVarType? VarType,
    int TypeStartIndex = -1,
    int ValueStartIndex = -1
);

public sealed record ReturnVarDecl(string TypeName, string Name);

public abstract record BescriptCallSignature(
    string FunctionName,
    string SaveName,
    IReadOnlyList<BescriptCallArgument> Arguments,
    ReturnVarDecl? ReturnVar,
    IReadOnlyList<BescriptCallSegmentSpan> SegmentSpans,
    bool DiscardsReturn = false,
    int KeywordOffset = -1,
    int OpenParenOffset = -1,
    int CloseParenOffset = -1,
    int ReturnTypeOffset = -1,
    int ReturnNameOffset = -1)
{
    public abstract BescriptCallKind Kind { get; }
}

public sealed record FunCallSignature(
    string FunctionName,
    string SaveName,
    IReadOnlyList<BescriptCallArgument> Arguments,
    IReadOnlyList<BescriptCallSegmentSpan> SegmentSpans,
    ReturnVarDecl? ReturnVar = null,
    bool DiscardsReturn = false,
    int KeywordOffset = -1,
    int OpenParenOffset = -1,
    int CloseParenOffset = -1,
    int ReturnTypeOffset = -1,
    int ReturnNameOffset = -1)
    : BescriptCallSignature(FunctionName, SaveName, Arguments, ReturnVar, SegmentSpans, DiscardsReturn, KeywordOffset, OpenParenOffset, CloseParenOffset, ReturnTypeOffset, ReturnNameOffset)
{
    public override BescriptCallKind Kind => BescriptCallKind.Fun;
}

public sealed record ToolfCallSignature(
    string FunctionName,
    string SaveName,
    IReadOnlyList<BescriptCallArgument> Arguments,
    IReadOnlyList<BescriptCallSegmentSpan> SegmentSpans,
    ReturnVarDecl? ReturnVar = null,
    bool DiscardsReturn = false,
    int KeywordOffset = -1,
    int OpenParenOffset = -1,
    int CloseParenOffset = -1,
    int ReturnTypeOffset = -1,
    int ReturnNameOffset = -1)
    : BescriptCallSignature(FunctionName, SaveName, Arguments, ReturnVar, SegmentSpans, DiscardsReturn, KeywordOffset, OpenParenOffset, CloseParenOffset, ReturnTypeOffset, ReturnNameOffset)
{
    public override BescriptCallKind Kind => BescriptCallKind.Toolf;
}

public sealed record VarfCallSignature(
    string OwnerType,
    string OwnerName,
    bool OwnerNameIsAlias,
    string FunctionName,
    string SaveName,
    IReadOnlyList<BescriptCallArgument> Arguments,
    IReadOnlyList<BescriptCallSegmentSpan> SegmentSpans,
    ReturnVarDecl? ReturnVar = null,
    bool DiscardsReturn = false,
    int KeywordOffset = -1,
    int OpenParenOffset = -1,
    int CloseParenOffset = -1,
    int ReturnTypeOffset = -1,
    int ReturnNameOffset = -1)
    : BescriptCallSignature(FunctionName, SaveName, Arguments, ReturnVar, SegmentSpans, DiscardsReturn, KeywordOffset, OpenParenOffset, CloseParenOffset, ReturnTypeOffset, ReturnNameOffset)
{
    public override BescriptCallKind Kind => BescriptCallKind.Varf;
}

public sealed record ActCallSignature(
    string OwnerType,
    string OwnerName,
    bool OwnerNameIsAlias,
    string FunctionName,
    string SaveName,
    IReadOnlyList<BescriptCallArgument> Arguments,
    IReadOnlyList<BescriptCallSegmentSpan> SegmentSpans,
    ReturnVarDecl? ReturnVar = null,
    bool DiscardsReturn = false,
    int KeywordOffset = -1,
    int OpenParenOffset = -1,
    int CloseParenOffset = -1,
    int ReturnTypeOffset = -1,
    int ReturnNameOffset = -1)
    : BescriptCallSignature(FunctionName, SaveName, Arguments, ReturnVar, SegmentSpans, DiscardsReturn, KeywordOffset, OpenParenOffset, CloseParenOffset, ReturnTypeOffset, ReturnNameOffset)
{
    public override BescriptCallKind Kind => BescriptCallKind.Act;
}

public sealed record DvfCallSignature(
    string OwnerType,
    string OwnerName,
    bool OwnerNameIsAlias,
    string FunctionName,
    string SaveName,
    IReadOnlyList<BescriptCallArgument> Arguments,
    IReadOnlyList<BescriptCallSegmentSpan> SegmentSpans,
    ReturnVarDecl? ReturnVar = null,
    bool DiscardsReturn = false,
    int KeywordOffset = -1,
    int OpenParenOffset = -1,
    int CloseParenOffset = -1,
    int ReturnTypeOffset = -1,
    int ReturnNameOffset = -1)
    : BescriptCallSignature(FunctionName, SaveName, Arguments, ReturnVar, SegmentSpans, DiscardsReturn, KeywordOffset, OpenParenOffset, CloseParenOffset, ReturnTypeOffset, ReturnNameOffset)
{
    public override BescriptCallKind Kind => BescriptCallKind.Dvf;
}

// ========== 脚本头部相关 ==========

public sealed record BescriptParameter(string TypeName, string Name);

public sealed record BescriptColor(int R, int G, int B);

public sealed record BescriptHeader(
    string Name,
    string? Comment,
    bool Debug,
    bool Skip,
    bool Private,
    string? Guid,
    BescriptColor? Color,
    IReadOnlyList<BescriptParameter> Parameters,
    BescriptParameter? ReturnParameter
)
{
    public static BescriptHeader Empty { get; } = new(string.Empty, null, false, false, false, null, null, Array.Empty<BescriptParameter>(), null);
}

// ========== 脚本信息 ==========

/// <summary>
/// 解析产物：头部 + AST + 总行数。
/// </summary>
public sealed record BescriptScriptInfo(
    string? ScriptRelativePath,
    BescriptHeader Header,
    BescriptAst Ast,
    int LineCount
);

// ========== AST 相关 ==========

public enum BescriptStatementKind
{
    If,
    While,
    Delay,
    Async,
    Comment,
    Skip,
    Break,
    Continue,
    Call
}

public interface IHasCall
{
    BescriptCallSignature Call { get; }
}

/// <summary>
/// AST 节点。
/// Text 为命令文本：剥离行号前缀、前导空格与 /* 前缀后的原文（注释行为 "//" + 解码后正文）。
/// LineStartColumns 记录 Text 每行在原始行中的 0-based 起始列（首元素即命令起始列）。
/// </summary>
public abstract record BescriptAstNode(
    int Line,
    int Depth,
    int Index,
    BescriptStatementKind Kind,
    string Text,
    IReadOnlyList<BescriptAstNode> Children,
    IReadOnlyList<int> LineStartColumns,
    bool IsDisabled = false,
    int EndLine = 0,
    int KeywordOffset = -1);

public sealed record CallAstNode(
    int Line, int Depth, int Index, string Text,
    BescriptCallSignature Call,
    IReadOnlyList<BescriptAstNode> Children,
    IReadOnlyList<int> LineStartColumns,
    bool IsDisabled = false,
    int EndLine = 0,
    int KeywordOffset = -1)
    : BescriptAstNode(Line, Depth, Index, BescriptStatementKind.Call, Text, Children, LineStartColumns, IsDisabled, EndLine, KeywordOffset), IHasCall;

public sealed record IfAstNode(
    int Line, int Depth, int Index, string Text,
    BescriptCallSignature Call,
    IReadOnlyList<BescriptAstNode> Children,
    IReadOnlyList<int> LineStartColumns,
    bool IsDisabled = false,
    int EndLine = 0,
    int KeywordOffset = -1)
    : BescriptAstNode(Line, Depth, Index, BescriptStatementKind.If, Text, Children, LineStartColumns, IsDisabled, EndLine, KeywordOffset), IHasCall;

public sealed record WhileAstNode(
    int Line, int Depth, int Index, string Text,
    BescriptCallSignature Call,
    IReadOnlyList<BescriptAstNode> Children,
    IReadOnlyList<int> LineStartColumns,
    bool IsDisabled = false,
    int EndLine = 0,
    int KeywordOffset = -1)
    : BescriptAstNode(Line, Depth, Index, BescriptStatementKind.While, Text, Children, LineStartColumns, IsDisabled, EndLine, KeywordOffset), IHasCall;

public sealed record DelayAstNode(
    int Line, int Depth, int Index, string Text,
    BescriptCallSignature Call,
    IReadOnlyList<BescriptAstNode> Children,
    IReadOnlyList<int> LineStartColumns,
    bool IsDisabled = false,
    int EndLine = 0,
    int KeywordOffset = -1)
    : BescriptAstNode(Line, Depth, Index, BescriptStatementKind.Delay, Text, Children, LineStartColumns, IsDisabled, EndLine, KeywordOffset), IHasCall;

public sealed record AsyncAstNode(
    int Line, int Depth, int Index, string Text,
    BescriptCallSignature Call,
    IReadOnlyList<BescriptAstNode> Children,
    IReadOnlyList<int> LineStartColumns,
    bool IsDisabled = false,
    int EndLine = 0,
    int KeywordOffset = -1)
    : BescriptAstNode(Line, Depth, Index, BescriptStatementKind.Async, Text, Children, LineStartColumns, IsDisabled, EndLine, KeywordOffset), IHasCall;

public sealed record BreakAstNode(
    int Line, int Depth, int Index, string Text,
    BescriptCallSignature Call,
    IReadOnlyList<BescriptAstNode> Children,
    IReadOnlyList<int> LineStartColumns,
    bool IsDisabled = false,
    int EndLine = 0,
    int KeywordOffset = -1)
    : BescriptAstNode(Line, Depth, Index, BescriptStatementKind.Break, Text, Children, LineStartColumns, IsDisabled, EndLine, KeywordOffset), IHasCall;

public sealed record ContinueAstNode(
    int Line, int Depth, int Index, string Text,
    BescriptCallSignature Call,
    IReadOnlyList<BescriptAstNode> Children,
    IReadOnlyList<int> LineStartColumns,
    bool IsDisabled = false,
    int EndLine = 0,
    int KeywordOffset = -1)
    : BescriptAstNode(Line, Depth, Index, BescriptStatementKind.Continue, Text, Children, LineStartColumns, IsDisabled, EndLine, KeywordOffset), IHasCall;

public sealed record CommentAstNode(
    int Line, int Depth, int Index, string Text,
    IReadOnlyList<BescriptAstNode> Children,
    IReadOnlyList<int> LineStartColumns,
    bool IsDisabled = false,
    int EndLine = 0,
    int KeywordOffset = -1)
    : BescriptAstNode(Line, Depth, Index, BescriptStatementKind.Comment, Text, Children, LineStartColumns, IsDisabled, EndLine, KeywordOffset);

public sealed record SkipAstNode(
    int Line, int Depth, int Index, string Text,
    IReadOnlyList<BescriptAstNode> Children,
    IReadOnlyList<int> LineStartColumns,
    bool IsDisabled = false,
    int EndLine = 0,
    int KeywordOffset = -1)
    : BescriptAstNode(Line, Depth, Index, BescriptStatementKind.Skip, Text, Children, LineStartColumns, IsDisabled, EndLine, KeywordOffset);

public sealed record BescriptAst(
    string? ScriptRelativePath,
    BescriptHeader Header,
    IReadOnlyList<BescriptAstNode> RootNodes,
    IReadOnlyDictionary<string, BescriptVarType> DeclaredVariables)
{
    /// <summary>
    /// 无脚本级局部变量推断时使用的共享空表。禁止用 null 表示「无变量」。
    /// </summary>
    public static IReadOnlyDictionary<string, BescriptVarType> EmptyDeclaredVariables { get; } =
        new Dictionary<string, BescriptVarType>(StringComparer.Ordinal);
}

public static class BescriptAstExtensions
{
    public static IEnumerable<BescriptAstNode> FlattenNodes(this IReadOnlyList<BescriptAstNode> roots)
    {
        Stack<BescriptAstNode> stack = new(roots.Count);
        for (int i = roots.Count - 1; i >= 0; i--)
            stack.Push(roots[i]);

        while (stack.Count > 0)
        {
            BescriptAstNode node = stack.Pop();
            yield return node;
            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }
    }

    public static BescriptAstNode WithChildren(this BescriptAstNode node, IReadOnlyList<BescriptAstNode> newChildren)
    {
        return node switch
        {
            CallAstNode n => n with { Children = newChildren },
            IfAstNode n => n with { Children = newChildren },
            WhileAstNode n => n with { Children = newChildren },
            DelayAstNode n => n with { Children = newChildren },
            AsyncAstNode n => n with { Children = newChildren },
            BreakAstNode n => n with { Children = newChildren },
            ContinueAstNode n => n with { Children = newChildren },
            CommentAstNode n => n with { Children = newChildren },
            SkipAstNode n => n with { Children = newChildren },
            _ => throw new InvalidOperationException($"Unknown AST node type: {node.GetType().Name}")
        };
    }

    public static BescriptAstNode WithCallAndChildren(this BescriptAstNode node, BescriptCallSignature newCall, IReadOnlyList<BescriptAstNode> newChildren)
    {
        return node switch
        {
            CallAstNode n => n with { Call = newCall, Children = newChildren },
            IfAstNode n => n with { Call = newCall, Children = newChildren },
            WhileAstNode n => n with { Call = newCall, Children = newChildren },
            DelayAstNode n => n with { Call = newCall, Children = newChildren },
            AsyncAstNode n => n with { Call = newCall, Children = newChildren },
            BreakAstNode n => n with { Call = newCall, Children = newChildren },
            ContinueAstNode n => n with { Call = newCall, Children = newChildren },
            _ => node.WithChildren(newChildren)
        };
    }
}
