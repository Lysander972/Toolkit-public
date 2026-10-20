// BeScript 语法扫描 - Logic 层
// 职责：基于 AST 与词法 token 的位置信息识别语法元素（Act 调用、内置方法、Canvas 参数、全局变量引用、语义 token）
// 位置一律来自解析产物（节点 LineStartColumns + 签名 span + token 列），禁止文本反推。

using Toolkit.Core.Bescript;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Analysis;

public sealed record BescriptTextSpan(int Line, int Column, int Length);

public sealed record BescriptActCallRange(string ScriptName, string? OwnerName, bool OwnerNameIsAlias, BescriptTextSpan Range);

public sealed record BescriptBuiltinMethodRange(string MethodName, BescriptTextSpan Range);

public abstract record BescriptCanvasArgRange(string RawArg, BescriptTextSpan Range);

public sealed record BescriptGuidCanvasArgRange(string RawArg, string Guid, BescriptTextSpan Range)
    : BescriptCanvasArgRange(RawArg, Range);

public sealed record BescriptInlineCanvasArgRange(string RawArg, string CanvasName, BescriptTextSpan Range)
    : BescriptCanvasArgRange(RawArg, Range);

public sealed record BescriptGlobalVarRange(string Name, BescriptTextSpan Range);

// === 语义 Token 扫描 ===

public sealed record BescriptSemanticToken(int Line, int Column, int Length, int Type, int Modifiers);

// Token 类型索引（与 VSCode legend 声明顺序一致）
// function=0, variable=1, parameter=2, string=3, keyword=4, number=5, class=6, type=7
// modifier: declaration=1, defaultLibrary=2, readonly=4

public sealed class BescriptScanner
{
    private static readonly Lazy<HashSet<string>> BuiltinMethodLookup = new(BuildBuiltinMethodLookup);

    public IReadOnlyList<BescriptActCallRange> ScanActCalls(string text)
    {
        (BescriptAst ast, string[] lines) = ParseAst(text);
        List<BescriptActCallRange> results = [];
        foreach (BescriptAstNode node in ast.RootNodes)
            TraverseNodes(node, results, static (n, r) => CollectActCallFromNode(n, r));

        return results;
    }

    public IReadOnlyList<BescriptBuiltinMethodRange> ScanBuiltinMethodCalls(string text)
    {
        (BescriptAst ast, string[] lines) = ParseAst(text);
        List<BescriptBuiltinMethodRange> results = [];
        foreach (BescriptAstNode node in ast.RootNodes)
            TraverseNodes(node, results, static (n, r) => CollectBuiltinMethodFromNode(n, r));

        return results;
    }

    public IReadOnlyList<BescriptCanvasArgRange> ScanCanvasArgs(string text)
    {
        (BescriptAst ast, string[] lines) = ParseAst(text);
        List<BescriptCanvasArgRange> results = [];
        foreach (BescriptAstNode node in ast.RootNodes)
            TraverseNodes(node, results, static (n, r) => CollectCanvasArgFromNode(n, r));

        return results;
    }

    public IReadOnlyList<BescriptGlobalVarRange> ScanGlobalVarRefs(string text, IReadOnlyList<string> globalVarNames)
    {
        if (globalVarNames.Count == 0)
        {
            return Array.Empty<BescriptGlobalVarRange>();
        }

        (BescriptAst ast, string[] lines) = ParseAst(text);
        HashSet<string> nameSet = new(globalVarNames, StringComparer.Ordinal);
        List<BescriptGlobalVarRange> results = [];
        foreach (BescriptAstNode node in ast.RootNodes)
            TraverseNodes(node, results, (n, r) => CollectIdentifierRefFromNode(n, nameSet, r));

        return results;
    }

    private static (BescriptAst Ast, string[] Lines) ParseAst(string text)
    {
        string[] lines = TextLines.SplitLines(text);
        BescriptScriptInfo info = BescriptTextParser.Parse(string.Empty, text).Script;
        return (info.Ast, lines);
    }

    // 签名内"最后一段"（方法名/脚本名）在命令文本中的起始偏移。
    private static int LastSegmentOffset(BescriptCallSignature sig)
    {
        IReadOnlyList<BescriptCallSegmentSpan> spans = sig.SegmentSpans;
        if (spans.Count == 0)
        {
            return 0;
        }

        int lastDot = sig.SaveName.LastIndexOf('.');
        return spans[0].Offset + (lastDot >= 0 ? lastDot + 1 : 0);
    }

    private static void CollectActCallFromNode(BescriptAstNode node, List<BescriptActCallRange> results)
    {
        if (node is not IHasCall { Call: ActCallSignature act })
        {
            return;
        }

        string scriptName = act.FunctionName;
        if (IsBuiltinMethod(scriptName))
        {
            return;
        }

        int nameOffset = LastSegmentOffset(act);
        results.Add(new BescriptActCallRange(scriptName, act.OwnerName, act.OwnerNameIsAlias,
            NodeSpan(node, nameOffset, act.SaveName.Length - LastDotInSaveName(act) - 1)));
    }

    private static int LastDotInSaveName(BescriptCallSignature sig)
    {
        int lastDot = sig.SaveName.LastIndexOf('.');
        return lastDot;
    }

    private static void CollectBuiltinMethodFromNode(BescriptAstNode node, List<BescriptBuiltinMethodRange> results)
    {
        if (node is not IHasCall hasCall)
        {
            return;
        }

        BescriptCallSignature sig = hasCall.Call;
        switch (sig.Kind)
        {
            case BescriptCallKind.Fun:
            case BescriptCallKind.Toolf:
                if (IsBuiltinMethod(sig.FunctionName))
                {
                    results.Add(new BescriptBuiltinMethodRange(sig.FunctionName,
                        NodeSpan(node, LastSegmentOffset(sig), sig.SaveName.Length - LastDotInSaveName(sig) - 1)));
                }

                break;
            case BescriptCallKind.Varf:
            case BescriptCallKind.Dvf:
                {
                    string ownerType = ((VarfCallSignature)sig).OwnerType;
                    string fullMethodName = ownerType + "." + sig.FunctionName;
                    if (IsBuiltinMethod(fullMethodName))
                    {
                        results.Add(new BescriptBuiltinMethodRange(fullMethodName,
                            NodeSpan(node, LastSegmentOffset(sig), sig.FunctionName.Length)));
                    }

                    break;
                }
            case BescriptCallKind.Act:
                if (IsBuiltinMethod(sig.FunctionName))
                {
                    results.Add(new BescriptBuiltinMethodRange(sig.FunctionName,
                        NodeSpan(node, LastSegmentOffset(sig), sig.FunctionName.Length)));
                }

                break;
            case BescriptCallKind.None:
                throw new InvalidOperationException("调用签名未指定类型");
            default:
                throw new InvalidOperationException($"不支持的调用类型: {sig.Kind}");
        }
    }

    private static void CollectCanvasArgFromNode(BescriptAstNode node, List<BescriptCanvasArgRange> results)
    {
        if (node is not IHasCall hasCall)
        {
            return;
        }

        BescriptCallSignature sig = hasCall.Call;
        if (sig.Kind != BescriptCallKind.Fun ||
            !string.Equals(sig.FunctionName, "create.canvas", StringComparison.Ordinal))
        {
            return;
        }

        foreach (BescriptCallArgument arg in sig.Arguments)
        {
            if (!string.Equals(arg.TypeName, "Canvas", StringComparison.Ordinal))
            {
                continue;
            }

            string decodedArg = arg.DecodedText;
            BescriptTextSpan span = NodeSpan(node, arg.ValueStartIndex, arg.RawText.Length);

            if (CanvasInlineArg.Parse(decodedArg) is CanvasInlineArgData inlineArg)
            {
                results.Add(new BescriptInlineCanvasArgRange(decodedArg, inlineArg.CanvasName, span));
            }
            else if (BescriptSyntax.TryExtractGuid(decodedArg) is string candidate)
            {
                results.Add(new BescriptGuidCanvasArgRange(decodedArg, candidate, span));
            }

            break;
        }
    }

    private static void TraverseNodes<T>(BescriptAstNode node, List<T> results, Action<BescriptAstNode, List<T>> nodeHandler)
    {
        if (node.Kind != BescriptStatementKind.Skip)
        {
            nodeHandler(node, results);
        }

        foreach (BescriptAstNode child in node.Children)
        {
            TraverseNodes(child, results, nodeHandler);
        }
    }

    private static void CollectIdentifierRefFromNode(BescriptAstNode node, HashSet<string> names, List<BescriptGlobalVarRange> results)
    {
        if (node is not IHasCall hasCall)
        {
            return;
        }

        BescriptCallSignature sig = hasCall.Call;

        // 1. 赋值目标（等号左边的变量名）
        if (sig.ReturnVar is { } ret && names.Contains(ret.Name))
        {
            results.Add(new BescriptGlobalVarRange(ret.Name, NodeSpan(node, sig.ReturnNameOffset, ret.Name.Length)));
        }

        // 2. varf/dvf 目标变量名（FunctionName 对应被访问的全局变量）
        if ((sig.Kind == BescriptCallKind.Varf || sig.Kind == BescriptCallKind.Dvf)
            && names.Contains(sig.FunctionName))
        {
            results.Add(new BescriptGlobalVarRange(sig.FunctionName,
                NodeSpan(node, LastSegmentOffset(sig), sig.FunctionName.Length)));
        }

        // 3. Owner 名（varf/act/dvf 中作为零件引用的变量）
        string? ownerName = sig switch
        {
            VarfCallSignature v => v.OwnerName,
            ActCallSignature a => a.OwnerName,
            DvfCallSignature d => d.OwnerName,
            _ => null
        };
        if (ownerName is not null && names.Contains(ownerName))
        {
            int ownerOffset = OwnerNameOffset(sig);
            results.Add(new BescriptGlobalVarRange(ownerName, NodeSpan(node, ownerOffset, ownerName.Length)));
        }

        // 4. 变量类型实参（IsVariable 标记的参数）
        foreach (BescriptCallArgument arg in sig.Arguments)
        {
            if (!arg.IsVariable || !names.Contains(arg.DecodedText))
            {
                continue;
            }

            results.Add(new BescriptGlobalVarRange(arg.DecodedText, NodeSpan(node, arg.ValueStartIndex, arg.RawText.Length)));
        }
    }

    /// <summary>
    /// 调用签名 Owner 段在命令文本中的起始偏移（别名形式跳过 '/'）。
    /// </summary>
    private static int OwnerNameOffset(BescriptCallSignature sig)
    {
        IReadOnlyList<BescriptCallSegmentSpan> spans = sig.SegmentSpans;
        if (spans.Count < 2)
        {
            return 0;
        }

        bool alias = sig switch
        {
            ActCallSignature a => a.OwnerNameIsAlias,
            _ => false
        };
        return spans[1].Offset + (alias ? 1 : 0);
    }

    /// <summary>
    /// 命令文本内偏移 → 原始行 (Line, Column)。多行文本按行内偏移换算，每行列偏移来自节点 LineStartColumns。
    /// </summary>
    private static (int Line, int Column) NodeOffsetToPosition(BescriptAstNode node, int offset)
    {
        IReadOnlyList<int> lineStarts = node.LineStartColumns;
        int lineIndex = 0;
        int lineStart = 0;
        int bound = Math.Min(offset, node.Text.Length);
        for (int i = 0; i < bound; i++)
        {
            if (node.Text[i] == '\n')
            {
                lineIndex++;
                lineStart = i + 1;
            }
        }

        int startColumn = lineIndex < lineStarts.Count ? lineStarts[lineIndex] : 0;
        return (node.Line + lineIndex, startColumn + (bound - lineStart));
    }

    private static BescriptTextSpan NodeSpan(BescriptAstNode node, int offset, int length)
    {
        (int line, int column) = NodeOffsetToPosition(node, offset);
        return new BescriptTextSpan(line, column, length);
    }

    // ========== 语义 Token 扫描 ==========

    private const int TK_FUNC = 0;
    private const int TK_VAR = 1;
    private const int TK_PARAM = 2;
    private const int TK_STR = 3;
    private const int TK_KEYWORD = 4;
    private const int TK_NUMBER = 5;
    private const int TK_CLASS = 6;
    private const int TK_TYPE = 7;
    private const int TK_COMMENT = 8;
    private const int TK_DISABLED = 9;
    private const int MOD_DECL = 1;
    private const int MOD_BUILTIN = 2;
    private const int MOD_READONLY = 4;

    public IReadOnlyList<BescriptSemanticToken> ScanSemanticTokens(string text, IReadOnlyList<string> globalVarNames)
    {
        string[] lines = TextLines.SplitLines(text);
        BescriptTokenStream tokenStream = BescriptLexer.Lex(text);
        BescriptScriptInfo info = BescriptTextParser.Parse(string.Empty, text).Script;
        BescriptAst ast = info.Ast;
        HashSet<string> globals = new(globalVarNames, StringComparer.Ordinal);

        List<BescriptSemanticToken> results = [];
        CollectHeaderTokens(tokenStream, info.Header, results);

        void VisitAll(BescriptAstNode n)
        {
            if (n.Kind is BescriptStatementKind.Comment)
            {
                EmitLineRangeToken(tokenStream, n.Line, BescriptTokenKind.CommentPrefix, TK_COMMENT, results);
            }
            else if (n.IsDisabled)
            {
                EmitLineRangeToken(tokenStream, n.Line, BescriptTokenKind.DisabledPrefix, TK_DISABLED, results);
            }
            else
            {
                EmitStatementKeyword(n, results);
                if (n is IHasCall hasCallNode && hasCallNode.Call is { } call)
                {
                    CollectCallTokens(n, call, globals, results);
                }
            }

            foreach (BescriptAstNode child in n.Children)
            {
                VisitAll(child);
            }
        }

        foreach (BescriptAstNode node in ast.RootNodes)
        {
            VisitAll(node);
        }

        return results;
    }

    /// <summary>
    /// 注释/禁用行高亮：从标记 token 起到行尾。
    /// </summary>
    private static void EmitLineRangeToken(BescriptTokenStream tokenStream, int line, BescriptTokenKind markerKind, int type, List<BescriptSemanticToken> results)
    {
        BescriptLineTokens? lineTokens = line >= 1 && line <= tokenStream.Lines.Count ? tokenStream.Lines[line - 1] : null;
        if (lineTokens is null)
        {
            return;
        }

        foreach (BescriptToken token in lineTokens.Tokens)
        {
            if (token.Kind == markerKind)
            {
                results.Add(new BescriptSemanticToken(line, token.Column - 1, lineTokens.Text.Length - (token.Column - 1), type, 0));
                return;
            }
        }
    }

    // --- Headers ---

    private static void CollectHeaderTokens(BescriptTokenStream tokenStream, BescriptHeader header, List<BescriptSemanticToken> results)
    {
        foreach (BescriptLineTokens lineTokens in tokenStream.Lines)
        {
            IReadOnlyList<BescriptToken> tokens = lineTokens.Tokens;
            if (tokens.Count == 0)
            {
                continue;
            }

            BescriptToken first = tokens[0];
            if (first.Kind == BescriptTokenKind.BeginCode || first.Kind == BescriptTokenKind.EndCode)
            {
                results.Add(new BescriptSemanticToken(first.Line, first.Column - 1, first.Length, TK_KEYWORD, 0));
            }
            else if (first.Kind == BescriptTokenKind.HeaderKey)
            {
                EmitHeaderKeyTokens(lineTokens, header, results);
            }
        }
    }

    private static void EmitHeaderKeyTokens(BescriptLineTokens lineTokens, BescriptHeader header, List<BescriptSemanticToken> results)
    {
        BescriptToken keyToken = lineTokens.Tokens[0];
        string key = keyToken.Text;
        int line = keyToken.Line;
        results.Add(new BescriptSemanticToken(line, keyToken.Column - 1, keyToken.Length, TK_KEYWORD, 0));

        BescriptToken? valueToken = null;
        foreach (BescriptToken token in lineTokens.Tokens)
        {
            if (token.Kind == BescriptTokenKind.HeaderValue)
            {
                valueToken = token;
                break;
            }
        }

        switch (key)
        {
            case "name":
                if (valueToken is { } nameValue && header.Name.Length > 0)
                {
                    results.Add(new BescriptSemanticToken(line, nameValue.Column - 1, nameValue.Length, TK_FUNC, MOD_DECL));
                }

                break;
            case "params":
                if (valueToken is { } paramsValue)
                {
                    EmitParamsTokens(paramsValue, header.Parameters, results);
                }

                break;
            case "return":
                if (valueToken is { } returnValue && header.ReturnParameter is { } ret)
                {
                    EmitTypeNamePairTokens(returnValue, ret.TypeName, ret.Name, line, TK_VAR, results);
                }

                break;
            default:
                break;
        }
    }

    /// <summary>
    /// 类型+名 对（params/return 值）在值文本内的位置：按空白与逗号切分。
    /// </summary>
    private static void EmitTypeNamePairTokens(
        BescriptToken valueToken,
        string typeName,
        string name,
        int line,
        int nameType,
        List<BescriptSemanticToken> results)
    {
        string text = valueToken.Text;
        int baseColumn = valueToken.Column - 1;
        int typeOffset = -1;
        int nameOffset = -1;
        int wordStart = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool atEnd = i == text.Length;
            bool isSeparator = !atEnd && (char.IsWhiteSpace(text[i]) || text[i] == ',');
            if (wordStart < 0 && !isSeparator && !atEnd)
            {
                wordStart = i;
            }
            else if (wordStart >= 0 && (isSeparator || atEnd))
            {
                if (typeOffset < 0)
                {
                    typeOffset = wordStart;
                }

                nameOffset = wordStart;
                wordStart = -1;
            }
        }

        if (typeOffset >= 0)
        {
            results.Add(new BescriptSemanticToken(line, baseColumn + typeOffset, typeName.Length, TK_TYPE, 0));
        }

        if (nameOffset >= 0)
        {
            results.Add(new BescriptSemanticToken(line, baseColumn + nameOffset, name.Length, nameType, MOD_DECL));
        }
    }

    private static void EmitParamsTokens(BescriptToken valueToken, IReadOnlyList<BescriptParameter> parameters, List<BescriptSemanticToken> results)
    {
        string text = valueToken.Text;
        int baseColumn = valueToken.Column - 1;
        int line = valueToken.Line;
        int parameterIndex = 0;
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != ',')
            {
                continue;
            }

            if (parameterIndex < parameters.Count && parameters[parameterIndex] is { } parameter)
            {
                EmitTypeNamePairInSegment(text[start..i], baseColumn + start, line, parameter.TypeName, parameter.Name, TK_PARAM, results);
            }

            parameterIndex++;
            start = i + 1;
        }
    }

    private static void EmitTypeNamePairInSegment(
        string segment,
        int baseColumn,
        int line,
        string typeName,
        string name,
        int nameType,
        List<BescriptSemanticToken> results)
    {
        int typeOffset = -1;
        int nameOffset = -1;
        int wordStart = -1;
        for (int i = 0; i <= segment.Length; i++)
        {
            bool atEnd = i == segment.Length;
            bool isSeparator = !atEnd && char.IsWhiteSpace(segment[i]);
            if (wordStart < 0 && !isSeparator && !atEnd)
            {
                wordStart = i;
            }
            else if (wordStart >= 0 && (isSeparator || atEnd))
            {
                if (typeOffset < 0)
                {
                    typeOffset = wordStart;
                }

                nameOffset = wordStart;
                wordStart = -1;
            }
        }

        if (typeOffset >= 0)
        {
            results.Add(new BescriptSemanticToken(line, baseColumn + typeOffset, typeName.Length, TK_TYPE, 0));
        }

        if (nameOffset >= 0)
        {
            results.Add(new BescriptSemanticToken(line, baseColumn + nameOffset, name.Length, nameType, MOD_DECL));
        }
    }

    // --- Code body (AST walk) ---

    private static void EmitStatementKeyword(BescriptAstNode node, List<BescriptSemanticToken> results)
    {
        if (node.KeywordOffset < 0)
        {
            return;
        }

        string keyword = node.Kind switch
        {
            BescriptStatementKind.If => "if",
            BescriptStatementKind.While => "while",
            BescriptStatementKind.Delay => "delay",
            BescriptStatementKind.Async => "async",
            _ => string.Empty
        };
        if (keyword.Length > 0)
        {
            (int line, int column) = NodeOffsetToPosition(node, node.KeywordOffset);
            results.Add(new BescriptSemanticToken(line, column, keyword.Length, TK_KEYWORD, 0));
        }
    }

    private static void CollectCallTokens(BescriptAstNode node, BescriptCallSignature sig, HashSet<string> globals, List<BescriptSemanticToken> results)
    {
        // 返回变量
        if (sig.ReturnVar is { } ret)
        {
            if (sig.ReturnTypeOffset >= 0)
            {
                (int typeLine, int typeColumn) = NodeOffsetToPosition(node, sig.ReturnTypeOffset);
                results.Add(new BescriptSemanticToken(typeLine, typeColumn, ret.TypeName.Length, TK_TYPE, 0));
            }

            if (sig.ReturnNameOffset >= 0)
            {
                (int nameLine, int nameColumn) = NodeOffsetToPosition(node, sig.ReturnNameOffset);
                results.Add(new BescriptSemanticToken(nameLine, nameColumn, ret.Name.Length, TK_VAR,
                    MOD_DECL | (globals.Contains(ret.Name) ? MOD_READONLY : 0)));
            }
        }

        // 调用关键字
        if (sig.KeywordOffset >= 0)
        {
            string? callKeyword = sig.Kind switch
            {
                BescriptCallKind.Fun => "fun",
                BescriptCallKind.Toolf => "toolf",
                BescriptCallKind.Act => "act",
                BescriptCallKind.Varf => "varf",
                BescriptCallKind.Dvf => "dvf",
                _ => null
            };
            if (callKeyword is not null)
            {
                (int kwLine, int kwColumn) = NodeOffsetToPosition(node, sig.KeywordOffset);
                results.Add(new BescriptSemanticToken(kwLine, kwColumn, callKeyword.Length, TK_KEYWORD, 0));
            }
        }

        switch (sig.Kind)
        {
            case BescriptCallKind.Varf:
                {
                    VarfCallSignature vf = (VarfCallSignature)sig;
                    EmitSegment(node, vf.SegmentSpans, 0, vf.OwnerType.Length, TK_TYPE, 0, results);
                    EmitSegment(node, vf.SegmentSpans, 1, vf.OwnerName.Length, TK_VAR, globals.Contains(vf.OwnerName) ? MOD_READONLY : 0, results);
                    if (vf.FunctionName.Length > 0)
                    {
                        bool isBuiltin = IsBuiltinMethod(vf.OwnerType + "." + vf.FunctionName);
                        EmitLastSegment(node, sig, vf.FunctionName.Length, TK_FUNC, isBuiltin ? MOD_BUILTIN : 0, results);
                    }

                    break;
                }
            case BescriptCallKind.Dvf:
                {
                    DvfCallSignature dv = (DvfCallSignature)sig;
                    EmitSegment(node, dv.SegmentSpans, 0, dv.OwnerName.Length, TK_VAR, globals.Contains(dv.OwnerName) ? MOD_READONLY : 0, results);
                    EmitSegment(node, dv.SegmentSpans, 1, dv.OwnerType.Length, TK_TYPE, 0, results);
                    if (dv.FunctionName.Length > 0)
                    {
                        bool isBuiltin = IsBuiltinMethod(dv.OwnerType + "." + dv.FunctionName);
                        EmitLastSegment(node, sig, dv.FunctionName.Length, TK_FUNC, isBuiltin ? MOD_BUILTIN : 0, results);
                    }

                    break;
                }
            case BescriptCallKind.Fun:
            case BescriptCallKind.Toolf:
                {
                    if (node.Kind == BescriptStatementKind.Break || node.Kind == BescriptStatementKind.Continue)
                    {
                        EmitLastSegment(node, sig, sig.FunctionName.Length, TK_KEYWORD, 0, results);
                        break;
                    }

                    if (sig.FunctionName.Length > 0)
                    {
                        IReadOnlyList<BescriptCallSegmentSpan> spans = sig.SegmentSpans;
                        if (spans.Count > 1)
                        {
                            for (int i = 0; i < spans.Count; i++)
                            {
                                bool isLast = i == spans.Count - 1;
                                int type = isLast ? TK_FUNC : TK_CLASS;
                                int mod = isLast && IsBuiltinMethod(sig.FunctionName) ? MOD_BUILTIN : 0;
                                EmitSegment(node, spans, i, spans[i].Length, type, mod, results);
                            }
                        }
                        else
                        {
                            bool isBuiltin = IsBuiltinMethod(sig.FunctionName);
                            EmitLastSegment(node, sig, sig.FunctionName.Length, TK_FUNC, isBuiltin ? MOD_BUILTIN : 0, results);
                        }
                    }

                    break;
                }
            case BescriptCallKind.Act:
                {
                    ActCallSignature ac = (ActCallSignature)sig;
                    EmitSegment(node, ac.SegmentSpans, 0, ac.OwnerType.Length, TK_CLASS, 0, results);

                    if (ac.OwnerName.Length > 0)
                    {
                        int ownerOffset = OwnerNameOffset(sig);
                        if (ac.OwnerNameIsAlias)
                        {
                            EmitAtOffset(node, ownerOffset, ac.OwnerName.Length, TK_CLASS, 0, results);
                        }
                        else if (string.Equals(ac.OwnerName, "this", StringComparison.Ordinal))
                        {
                            EmitAtOffset(node, ownerOffset, ac.OwnerName.Length, TK_KEYWORD, 0, results);
                        }
                        else if (int.TryParse(ac.OwnerName, out _))
                        {
                            EmitAtOffset(node, ownerOffset, ac.OwnerName.Length, TK_NUMBER, 0, results);
                        }
                    }

                    EmitLastSegment(node, sig, sig.SaveName.Length - LastDotInSaveName(sig) - 1, TK_FUNC, 0, results);
                    break;
                }
            case BescriptCallKind.None:
                throw new InvalidOperationException("调用签名未指定类型");
            default:
                throw new InvalidOperationException($"不支持的调用类型: {sig.Kind}");
        }

        // 参数
        foreach (BescriptCallArgument arg in sig.Arguments)
        {
            if (arg.IsVariable)
            {
                if (arg.TypeStartIndex >= 0)
                {
                    (int kwLine, int kwColumn) = NodeOffsetToPosition(node, arg.TypeStartIndex);
                    results.Add(new BescriptSemanticToken(kwLine, kwColumn, "var".Length, TK_KEYWORD, 0));
                }

                if (arg.ValueStartIndex >= 0)
                {
                    (int varLine, int varColumn) = NodeOffsetToPosition(node, arg.ValueStartIndex);
                    results.Add(new BescriptSemanticToken(varLine, varColumn, arg.DecodedText.Length, TK_VAR,
                        globals.Contains(arg.DecodedText) ? MOD_READONLY : 0));
                }
            }
            else
            {
                if (arg.TypeStartIndex >= 0 && BescriptVarTypeHelper.TryParse(arg.TypeName) != null)
                {
                    (int typeLine, int typeColumn) = NodeOffsetToPosition(node, arg.TypeStartIndex);
                    results.Add(new BescriptSemanticToken(typeLine, typeColumn, arg.TypeName.Length, TK_TYPE, 0));
                }

                if (arg.ValueStartIndex >= 0)
                {
                    int tokenType = arg.TypeName == "String" || arg.TypeName == "Canvas" ||
                        (arg.TypeName == "Color" && arg.DecodedText.StartsWith("颜色", StringComparison.Ordinal))
                        ? TK_STR
                        : string.Equals(arg.DecodedText, "true", StringComparison.Ordinal) ||
                          string.Equals(arg.DecodedText, "false", StringComparison.Ordinal)
                        ? TK_KEYWORD
                        : TK_NUMBER;
                    EmitAtOffset(node, arg.ValueStartIndex, arg.RawText.Length, tokenType, 0, results);
                }
            }
        }
    }

    private static void EmitSegment(
        BescriptAstNode node,
        IReadOnlyList<BescriptCallSegmentSpan>? spans,
        int index,
        int length,
        int type,
        int mod,
        List<BescriptSemanticToken> results)
    {
        if (spans is null || index >= spans.Count)
        {
            return;
        }

        EmitAtOffset(node, spans[index].Offset, length, type, mod, results);
    }

    private static void EmitLastSegment(
        BescriptAstNode node,
        BescriptCallSignature sig,
        int length,
        int type,
        int mod,
        List<BescriptSemanticToken> results)
    {
        EmitAtOffset(node, LastSegmentOffset(sig), length, type, mod, results);
    }

    private static void EmitAtOffset(
        BescriptAstNode node,
        int offset,
        int length,
        int type,
        int mod,
        List<BescriptSemanticToken> results)
    {
        // 跨行值（内联 Canvas）按行拆段，token 不跨行
        int end = offset + length;
        int lineIndex = 0;
        int lineStart = 0;
        int segmentStart = offset;
        IReadOnlyList<int> lineStarts = node.LineStartColumns;
        while (segmentStart < end)
        {
            int lineEnd = node.Text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = node.Text.Length;
            }

            if (segmentStart < lineEnd)
            {
                int segmentLength = Math.Min(end, lineEnd) - segmentStart;
                int columnOffset = lineIndex < lineStarts.Count ? lineStarts[lineIndex] : 0;
                results.Add(new BescriptSemanticToken(node.Line + lineIndex, columnOffset + (segmentStart - lineStart), segmentLength, type, mod));
            }

            if (lineEnd >= end || lineEnd >= node.Text.Length)
            {
                break;
            }

            segmentStart = lineEnd + 1;
            lineIndex++;
            lineStart = segmentStart;
        }
    }

    private static bool IsBuiltinMethod(string name) => BuiltinMethodLookup.Value.Contains(name);

    private static HashSet<string> BuildBuiltinMethodLookup()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        HashSet<string> results = new HashSet<string>(StringComparer.Ordinal);

        const int pageSize = 1000;
        int skip = 0;

        while (true)
        {
            MethodQueryResult page = registry.Query(
                namePattern: null,
                category: null,
                ownerType: null,
                skip: skip,
                take: pageSize);

            foreach (MethodSignature sig in page.Items)
            {
                results.Add(sig.GetFullPath());
                if (UiMethodExpansion.IsUiBaseMethod(sig))
                {
                    foreach (string derived in UiMethodExpansion.UiDerivedTypes)
                    {
                        results.Add($"{derived}.{sig.MethodName}");
                    }
                }

                if (sig.Category == MethodCategory.DeviceAction)
                {
                    results.Add(sig.MethodName);
                }
            }

            if (page.Items.Count == 0 || skip + page.Items.Count >= page.Total)
            {
                break;
            }

            skip += page.Items.Count;
        }

        return results;
    }
}
