using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Bescript;

/// <summary>
/// BeScript 语法解析器：纯 token 消费，直出 AST。
/// 不进行任何原始文本的 Trim/IndexOf/StartsWith 特判；命令文本由 token 位置切片保留原文。
/// </summary>
internal static class BescriptSyntaxParser
{
    public static (BescriptScriptInfo Script, List<ToolError> Errors, List<ToolError> Warnings) ParseScript(string scriptRelativePath, string text)
    {
        List<BescriptLineTokens> rawLines = BescriptLexer.Lex(text).Lines.ToList();
        List<ToolError> errors = [];
        List<ToolError> warnings = [];

        int beginCodeLine = FindBeginCodeLine(rawLines, scriptRelativePath);

        string? name = null;
        string? comment = null;
        bool debug = false;
        bool skip = false;
        bool isPrivate = false;
        string? guid = null;
        BescriptColor? color = null;
        List<BescriptParameter> parameters = [];
        BescriptParameter? returnParam = null;
        bool seenLinesHeader = false;
        bool seenEndcode = false;

        int i = beginCodeLine + 1;
        for (; i < rawLines.Count; i++)
        {
            IReadOnlyList<BescriptToken> tokens = rawLines[i].Tokens;
            if (tokens.Count == 0)
            {
                continue;
            }

            if (tokens[0].Kind == BescriptTokenKind.EndCode)
            {
                seenEndcode = true;
                break;
            }

            if (tokens[0].Kind != BescriptTokenKind.HeaderKey)
            {
                break;
            }

            BescriptToken keyToken = tokens[0];
            string key = keyToken.Text;
            string valueText = tokens.Count > 1 && tokens[1].Kind == BescriptTokenKind.HeaderValue
                ? tokens[1].Text
                : string.Empty;
            int lineNumber = keyToken.Line;
            int valueColumn = keyToken.Column + keyToken.Length + 1;

            switch (key)
            {
                case "name":
                    BescriptTextCodec.EnsureNoEscaped(valueText, lineNumber, valueColumn, "脚本名");
                    name = RecoverInvalid(valueText);
                    break;
                case "comment":
                    comment = RecoverInvalid(valueText);
                    break;
                case "guid":
                    guid = RecoverInvalid(valueText);
                    break;
                case "color":
                    color = ParseColor(valueText);
                    break;
                case "debug":
                    debug = true;
                    break;
                case "skip":
                    skip = true;
                    break;
                case "private":
                    isPrivate = true;
                    break;
                case "params":
                    i = ParseParamsHeader(rawLines, i, scriptRelativePath, parameters, warnings);
                    break;
                case "return":
                    returnParam = ParseReturnHeader(valueText, lineNumber, valueColumn, scriptRelativePath, warnings);
                    break;
                case "lines":
                    seenLinesHeader = true;
                    break;
                default:
                    break;
            }

            if (seenLinesHeader)
            {
                i++;
                break;
            }
        }

        if (!seenLinesHeader)
        {
            throw ParseError(scriptRelativePath, 0, 0, "Missing 'lines:' header.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw ParseError(scriptRelativePath, 0, 0, "Script has no 'name:' header.");
        }

        List<BescriptAstNode> rootNodes = [];
        Dictionary<string, BescriptVarType> declaredVariables = new(StringComparer.Ordinal);
        int commandIndex = 0;
        Stack<(int Depth, List<BescriptAstNode> Children)> stack = new();
        stack.Push((-1, rootNodes));

        for (; i < rawLines.Count; i++)
        {
            BescriptToken[] tokens = rawLines[i].Tokens.Where(static t => t.Kind != BescriptTokenKind.NewLine).ToArray();
            if (tokens.Length == 0)
            {
                continue;
            }

            if (tokens[0].Kind == BescriptTokenKind.EndCode)
            {
                seenEndcode = true;
                break;
            }

            if (tokens[0].Kind == BescriptTokenKind.RawCanvasBlock)
            {
                continue;
            }

            int firstContentTokenIndex = 0;
            while (firstContentTokenIndex < tokens.Length &&
                   tokens[firstContentTokenIndex].Kind is BescriptTokenKind.LineNumber or BescriptTokenKind.Indent or BescriptTokenKind.DisabledPrefix)
            {
                firstContentTokenIndex++;
            }

            if (firstContentTokenIndex < tokens.Length &&
                tokens[firstContentTokenIndex].Kind == BescriptTokenKind.CloseBrace &&
                firstContentTokenIndex == tokens.Length - 1)
            {
                continue;
            }

            if (firstContentTokenIndex == tokens.Length)
            {
                continue;
            }

            int consumedLineIndex = BuildCommandNode(rawLines, i, tokens, commandIndex, declaredVariables, stack);
            if (consumedLineIndex > i)
            {
                i = consumedLineIndex;
            }

            commandIndex++;
        }

        if (!seenEndcode)
        {
            throw ParseError(scriptRelativePath, rawLines.Count, 1, "Missing 'endcode' terminator.");
        }

        BescriptHeader header = new(name, comment, debug, skip, isPrivate, guid, color, parameters, returnParam);
        BescriptAst ast = new(scriptRelativePath, header, rootNodes, declaredVariables);
        return (new BescriptScriptInfo(scriptRelativePath, header, ast, rawLines.Count), errors, warnings);
    }

    private static int FindBeginCodeLine(List<BescriptLineTokens> rawLines, string scriptRelativePath)
    {
        for (int i = 0; i < rawLines.Count; i++)
        {
            IReadOnlyList<BescriptToken> tokens = rawLines[i].Tokens;
            if (tokens.Count == 0)
            {
                continue;
            }

            if (tokens[0].Kind == BescriptTokenKind.BeginCode)
            {
                return i;
            }

            throw ParseError(scriptRelativePath, i + 1, 1, "Missing 'begincode*' header.");
        }

        return -1;
    }

    private static BescriptColor? ParseColor(string valueText)
    {
        string[] parts = valueText.Split(',');
        if (parts.Length == 3 &&
            int.TryParse(parts[0], out int r) &&
            int.TryParse(parts[1], out int g) &&
            int.TryParse(parts[2], out int b))
        {
            return new BescriptColor(r, g, b);
        }

        return null;
    }

    private static int ParseParamsHeader(
        List<BescriptLineTokens> rawLines,
        int index,
        string scriptRelativePath,
        List<BescriptParameter> parameters,
        List<ToolError> warnings)
    {
        List<(string Text, int Line, int Column)> segments = [];
        CollectParamSegments(rawLines[index], segments);

        int consumed = index;
        while ((consumed + 1) < rawLines.Count)
        {
            IReadOnlyList<BescriptToken> tokens = rawLines[consumed + 1].Tokens;
            if (tokens.Count == 0 || tokens[0].Kind == BescriptTokenKind.HeaderKey)
            {
                break;
            }

            CollectParamSegments(rawLines[consumed + 1], segments);
            consumed++;
        }

        foreach ((string segmentText, int segmentLine, int segmentColumn) in segments)
        {
            string[] parts = SplitWords(segmentText);
            if (parts.Length < 2)
            {
                throw ParseError(scriptRelativePath, segmentLine, segmentColumn, "Invalid parameter declaration.");
            }

            string nameToken = parts[^1];
            BescriptTextCodec.EnsureNoEscaped(nameToken, segmentLine, segmentColumn, "参数名");
            string typeToken = string.Join(" ", parts[..^1]);
            string typeName = typeToken;
            if (BescriptVarTypeHelper.TryParse(typeToken) == null)
            {
                typeName = nameof(BescriptVarType.Void);
                warnings.Add(new ToolError(scriptRelativePath, segmentLine, segmentColumn,
                    $"Unknown parameter type '{typeToken}', coerced to Void.", ToolDiagnosticSeverity.Warning));
            }

            parameters.Add(new BescriptParameter(typeName, RecoverInvalid(nameToken)));
        }

        return consumed;
    }

    /// <summary>
    /// 按逗号切分参数段，段内保留原文与位置。段内为「类型 名」对。
    /// </summary>
    private static void CollectParamSegments(BescriptLineTokens line, List<(string Text, int Line, int Column)> segments)
    {
        IReadOnlyList<BescriptToken> tokens = line.Tokens;
        BescriptToken? headerValue = null;
        foreach (BescriptToken token in tokens)
        {
            if (token.Kind == BescriptTokenKind.HeaderValue)
            {
                headerValue = token;
                break;
            }
        }

        if (headerValue is not { } value)
        {
            return;
        }

        string text = value.Text;
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != ',')
            {
                continue;
            }

            string segment = text[start..i];
            int leading = 0;
            while (leading < segment.Length && char.IsWhiteSpace(segment[leading]))
            {
                leading++;
            }

            if (leading < segment.Length)
            {
                segments.Add((segment[leading..], value.Line, value.Column + start + leading));
            }

            start = i + 1;
        }
    }

    private static BescriptParameter? ParseReturnHeader(
        string valueText,
        int lineNumber,
        int valueColumn,
        string scriptRelativePath,
        List<ToolError> warnings)
    {
        string[] parts = SplitWords(valueText);
        if (parts.Length < 2)
        {
            throw ParseError(scriptRelativePath, lineNumber, valueColumn, "Invalid return declaration.");
        }

        string type = string.Join(' ', parts[..^1]);
        if (BescriptVarTypeHelper.TryParse(type) == null)
        {
            type = nameof(BescriptVarType.Void);
            warnings.Add(new ToolError(scriptRelativePath, lineNumber, valueColumn,
                $"Unknown return type '{string.Join(' ', parts[..^1])}', coerced to Void.", ToolDiagnosticSeverity.Warning));
        }

        string returnNameRaw = parts[^1];
        BescriptTextCodec.EnsureNoEscaped(returnNameRaw, lineNumber, valueColumn, "返回变量名");
        return new BescriptParameter(type, RecoverInvalid(returnNameRaw));
    }

    /// <summary>
    /// 按空白切分（连续空白视为单分隔）。用于 params/return 的「类型 名」对。
    /// </summary>
    private static string[] SplitWords(string text)
    {
        List<string> words = [];
        int start = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool atEnd = i == text.Length;
            bool isSeparator = !atEnd && char.IsWhiteSpace(text[i]);
            if (start < 0 && !isSeparator && !atEnd)
            {
                start = i;
            }
            else if (start >= 0 && (isSeparator || atEnd))
            {
                words.Add(text[start..i]);
                start = -1;
            }
        }

        return words.ToArray();
    }

    // ========== 命令节点构建 ==========

    private static int BuildCommandNode(
        List<BescriptLineTokens> rawLines,
        int lineIndex,
        IReadOnlyList<BescriptToken> tokens,
        int commandIndex,
        Dictionary<string, BescriptVarType> declaredVariables,
        Stack<(int Depth, List<BescriptAstNode> Children)> stack)
    {
        BescriptLineTokens lineTokens = rawLines[lineIndex];
        int lineNumber = lineTokens.Line;

        int textStart = ComputeTextStart(tokens);
        bool isDisabled = tokens.Any(static t => t.Kind == BescriptTokenKind.DisabledPrefix);
        int depth = ComputeDepth(tokens);
        int keywordOffset = -1;
        foreach (BescriptToken token in tokens)
        {
            if (token.Kind == BescriptTokenKind.StatementKeyword &&
                token.Text is "if" or "while" or "delay" or "async")
            {
                keywordOffset = token.Column - 1 - textStart;
                break;
            }
        }

        BescriptStatementKind kind = ClassifyStatement(tokens);
        List<BescriptAstNode> nodeChildren = [];
        BescriptAstNode node;
        int consumedLineIndex = lineIndex;

        switch (kind)
        {
            case BescriptStatementKind.Comment:
                {
                    BescriptToken? body = null;
                    foreach (BescriptToken token in tokens)
                    {
                        if (token.Kind == BescriptTokenKind.RawValue)
                        {
                            body = token;
                            break;
                        }
                    }

                    string decodedBody = body is { } bodyToken
                        ? BescriptTextCodec.RecoverInvalidChar(bodyToken.Text)
                        : string.Empty;
                    string text = BescriptSyntax.BesCommentPrefix + decodedBody;
                    node = new CommentAstNode(lineNumber, depth, commandIndex, text, nodeChildren,
                        LineStartColumns: [textStart], IsDisabled: isDisabled, EndLine: lineNumber, KeywordOffset: keywordOffset);
                    break;
                }
            case BescriptStatementKind.Skip:
                throw new InvalidOperationException($"语句类型 {kind} 不应由 BeScript 文本解析产生。");
            case BescriptStatementKind.Break:
            case BescriptStatementKind.Continue:
            case BescriptStatementKind.Call:
            case BescriptStatementKind.If:
            case BescriptStatementKind.While:
            case BescriptStatementKind.Delay:
            case BescriptStatementKind.Async:
                {
                    CallParseResult parseResult = ParseCallTokens(lineTokens, tokens, rawLines, consumedLineIndex);
                    consumedLineIndex = parseResult.ConsumedLineIndex;
                    BescriptCallSignature call = parseResult.Call;
                    string text = parseResult.CommandText;
                    int endLine = parseResult.EndLine;
                    IReadOnlyList<int> lineStartColumns = parseResult.LineStartColumns;

                    node = kind switch
                    {
                        BescriptStatementKind.If => new IfAstNode(lineNumber, depth, commandIndex, text, call, nodeChildren, LineStartColumns: lineStartColumns, IsDisabled: isDisabled, EndLine: endLine, KeywordOffset: keywordOffset),
                        BescriptStatementKind.While => new WhileAstNode(lineNumber, depth, commandIndex, text, call, nodeChildren, LineStartColumns: lineStartColumns, IsDisabled: isDisabled, EndLine: endLine, KeywordOffset: keywordOffset),
                        BescriptStatementKind.Break => new BreakAstNode(lineNumber, depth, commandIndex, text, call, nodeChildren, LineStartColumns: lineStartColumns, IsDisabled: isDisabled, EndLine: endLine, KeywordOffset: keywordOffset),
                        BescriptStatementKind.Continue => new ContinueAstNode(lineNumber, depth, commandIndex, text, call, nodeChildren, LineStartColumns: lineStartColumns, IsDisabled: isDisabled, EndLine: endLine, KeywordOffset: keywordOffset),
                        BescriptStatementKind.Delay => new DelayAstNode(lineNumber, depth, commandIndex, text, call, nodeChildren, LineStartColumns: lineStartColumns, IsDisabled: isDisabled, EndLine: endLine, KeywordOffset: keywordOffset),
                        BescriptStatementKind.Async => new AsyncAstNode(lineNumber, depth, commandIndex, text, call, nodeChildren, LineStartColumns: lineStartColumns, IsDisabled: isDisabled, EndLine: endLine, KeywordOffset: keywordOffset),
                        _ => new CallAstNode(lineNumber, depth, commandIndex, text, call, nodeChildren, LineStartColumns: lineStartColumns, IsDisabled: isDisabled, EndLine: endLine, KeywordOffset: keywordOffset)
                    };

                    if (call.ReturnVar is { } ret &&
                        BescriptVarTypeHelper.TryParse(ret.TypeName) is { } varType &&
                        varType != BescriptVarType.UI &&
                        !declaredVariables.ContainsKey(ret.Name))
                    {
                        declaredVariables[ret.Name] = varType;
                    }

                    break;
                }
            default:
                throw new InvalidOperationException($"未处理的语句类型: {kind}");
        }

        while (stack.Count > 0 && stack.Peek().Depth >= depth)
        {
            stack.Pop();
        }

        stack.Peek().Children.Add(node);

        if (kind is BescriptStatementKind.If or BescriptStatementKind.While)
        {
            stack.Push((depth, nodeChildren));
        }

        return consumedLineIndex;
    }

    /// <summary>
    /// 命令文本起始列：行号前缀（含其前空白）与缩进、/* 前缀的累计长度（0-based，相对原始行）。
    /// </summary>
    private static int ComputeTextStart(IReadOnlyList<BescriptToken> tokens)
    {
        int offset = 0;
        foreach (BescriptToken token in tokens)
        {
            if (token.Kind == BescriptTokenKind.LineNumber)
            {
                offset = token.Column - 1 + token.Length;
            }
            else if (token.Kind is BescriptTokenKind.Indent or BescriptTokenKind.DisabledPrefix)
            {
                offset += token.Length;
            }
            else
            {
                break;
            }
        }

        return offset;
    }

    /// <summary>
    /// 缩进深度：行号前缀后的 Indent 与 /* 后的 Indent 空格数合计 / IndentSize。
    /// </summary>
    private static int ComputeDepth(IReadOnlyList<BescriptToken> tokens)
    {
        int spaces = 0;
        foreach (BescriptToken token in tokens)
        {
            if (token.Kind is BescriptTokenKind.Indent)
            {
                spaces += token.Length;
            }
            else if (token.Kind is BescriptTokenKind.LineNumber or BescriptTokenKind.DisabledPrefix)
            {
                continue;
            }
            else
            {
                break;
            }
        }

        return spaces / BescriptSyntax.IndentSize;
    }

    private static BescriptStatementKind ClassifyStatement(IReadOnlyList<BescriptToken> tokens)
    {
        foreach (BescriptToken token in tokens)
        {
            if (token.Kind is BescriptTokenKind.LineNumber or BescriptTokenKind.Indent or BescriptTokenKind.DisabledPrefix or BescriptTokenKind.Assign)
            {
                continue;
            }

            if (token.Kind == BescriptTokenKind.CommentPrefix)
            {
                return BescriptStatementKind.Comment;
            }

            if (token.Kind == BescriptTokenKind.StatementKeyword)
            {
                return token.Text switch
                {
                    "if" => BescriptStatementKind.If,
                    "while" => BescriptStatementKind.While,
                    "delay" => BescriptStatementKind.Delay,
                    "async" => BescriptStatementKind.Async,
                    _ => BescriptStatementKind.Call
                };
            }

            if (token.Kind == BescriptTokenKind.CallKind)
            {
                if (token.Text == "fun")
                {
                    foreach (BescriptToken inner in tokens)
                    {
                        if ((inner.Kind is BescriptTokenKind.Identifier or BescriptTokenKind.TypeName or BescriptTokenKind.StatementKeyword) &&
                            inner.Column > token.Column)
                        {
                            if (inner.Text == "break")
                            {
                                return BescriptStatementKind.Break;
                            }

                            if (inner.Text == "continue")
                            {
                                return BescriptStatementKind.Continue;
                            }

                            break;
                        }
                    }
                }

                return BescriptStatementKind.Call;
            }

            return BescriptStatementKind.Call;
        }

        return BescriptStatementKind.Call;
    }

    // ========== 调用解析 ==========

    private sealed record CallParseResult(
        BescriptCallSignature Call,
        string CommandText,
        int EndLine,
        int ConsumedLineIndex,
        IReadOnlyList<int> LineStartColumns);

    private static CallParseResult ParseCallTokens(
        BescriptLineTokens lineTokens,
        IReadOnlyList<BescriptToken> tokens,
        List<BescriptLineTokens> rawLines,
        int lineIndex)
    {
        string rawLine = lineTokens.Text;
        int textStart = ComputeTextStart(tokens);
        int tokenTextStart = textStart;
        string firstLineText = textStart <= rawLine.Length ? rawLine[textStart..] : string.Empty;

        ReturnVarDecl? returnVar = null;
        bool discardsReturn = false;
        int callTokenIndex = -1;
        int returnTypeOffset = -1;
        int returnNameOffset = -1;

        for (int i = 0; i < tokens.Count; i++)
        {
            BescriptToken token = tokens[i];
            if (token.Kind is BescriptTokenKind.LineNumber or BescriptTokenKind.Indent or BescriptTokenKind.DisabledPrefix)
            {
                continue;
            }

            if (token.Kind == BescriptTokenKind.Assign)
            {
                string? returnType = null;
                string? returnName = null;
                int wordIndex = i - 1;
                while (wordIndex >= 0 && !IsWordToken(tokens[wordIndex]))
                {
                    wordIndex--;
                }

                if (wordIndex >= 0)
                {
                    returnName = tokens[wordIndex].Text;
                    int typeIndex = wordIndex - 1;
                    while (typeIndex >= 0 && !IsWordToken(tokens[typeIndex]))
                    {
                        typeIndex--;
                    }

                    if (typeIndex >= 0)
                    {
                        returnType = tokens[typeIndex].Text;
                    }
                }

                if (returnType is not null && returnName is not null &&
                    BescriptVarTypeHelper.TryParse(returnType) != null)
                {
                    string retType = returnType;
                    string retName = returnName;
                    BescriptTextCodec.EnsureNoEscaped(retName, "返回变量名");
                    returnVar = new ReturnVarDecl(retType, BescriptTextCodec.RecoverInvalidChar(retName));
                    returnTypeOffset = tokens[wordIndex - 1].Column - 1 - tokenTextStart;
                    returnNameOffset = tokens[wordIndex].Column - 1 - tokenTextStart;
                }
                else
                {
                    discardsReturn = true;
                }

                continue;
            }

            if (token.Kind == BescriptTokenKind.CallKind && token.Text is "fun" or "act" or "varf" or "dvf" or "toolf")
            {
                callTokenIndex = i;
            }

            if (callTokenIndex >= 0)
            {
                break;
            }
        }

        if (callTokenIndex < 0)
        {
            throw new InvalidOperationException($"调用解析失败: 未识别的调用前缀。文本={firstLineText}");
        }

        BescriptToken callToken = tokens[callTokenIndex];
        string kindWord = callToken.Text;
        BescriptCallKind kind = kindWord switch
        {
            "fun" => BescriptCallKind.Fun,
            "toolf" => BescriptCallKind.Toolf,
            "varf" => BescriptCallKind.Varf,
            "act" => BescriptCallKind.Act,
            "dvf" => BescriptCallKind.Dvf,
            _ => BescriptCallKind.None
        };

        int keywordOffset = callToken.Column - 1 - tokenTextStart;

        int openParenIndex = -1;
        for (int i = callTokenIndex + 1; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == BescriptTokenKind.OpenParen)
            {
                openParenIndex = i;
                break;
            }
        }

        if (openParenIndex < 0)
        {
            throw new InvalidOperationException($"调用解析失败: 找不到 '('。文本={firstLineText}");
        }

        int closeParenIndex = -1;
        for (int i = openParenIndex + 1; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == BescriptTokenKind.CloseParen)
            {
                closeParenIndex = i;
                break;
            }
        }

        string commandText = firstLineText;
        int endLine = lineTokens.Line;
        int consumedLineIndex = lineIndex;
        List<int> lineStartColumns = [textStart];
        bool isCanvasMultiLine = false;
        int closeParenOffset;

        if (closeParenIndex >= 0)
        {
            closeParenOffset = tokens[closeParenIndex].Column - 1 - tokenTextStart;
        }
        else
        {
            isCanvasMultiLine = true;
            List<string> rawParts = [firstLineText];
            int j = lineIndex + 1;
            bool closed = false;
            for (; j < rawLines.Count; j++)
            {
                IReadOnlyList<BescriptToken> rawTokens = rawLines[j].Tokens;
                if (rawTokens.Count == 0 || rawTokens[0].Kind != BescriptTokenKind.RawCanvasBlock)
                {
                    break;
                }

                rawParts.Add(rawTokens[0].Text);
                lineStartColumns.Add(0);
                endLine = rawLines[j].Line;
                if (rawTokens[0].Text.Contains(CanvasInlineArg.NodeHjsonRawEndWithCloseParen, StringComparison.Ordinal))
                {
                    closed = true;
                    j++;
                    break;
                }
            }

            if (!closed)
            {
                throw new InvalidOperationException("新版内联 Canvas 缺少 ==) 结束标记。");
            }

            consumedLineIndex = j - 1;
            commandText = string.Join("\n", rawParts);
            closeParenOffset = commandText.Length - 1;
        }

        int openParenOffset = tokens[openParenIndex].Column - 1 - tokenTextStart;

        List<BescriptCallSegmentSpan> segmentSpans = [];
        string rawSignature = string.Empty;
        int signatureStartOffset = -1;
        int signatureEndOffset = -1;
        for (int i = callTokenIndex + 1; i < openParenIndex; i++)
        {
            BescriptToken token = tokens[i];
            if (token.Kind == BescriptTokenKind.Dot || token.Kind == BescriptTokenKind.OpenBrace)
            {
                continue;
            }

            int offset = token.Column - 1 - tokenTextStart;
            if (signatureStartOffset < 0)
            {
                signatureStartOffset = offset;
            }

            segmentSpans.Add(new BescriptCallSegmentSpan(offset, token.Length));
            signatureEndOffset = offset + token.Length;
        }

        if (signatureStartOffset < 0)
        {
            throw new InvalidOperationException($"调用解析失败: 签名缺失。文本={commandText}");
        }

        rawSignature = commandText[signatureStartOffset..signatureEndOffset];

        List<BescriptCallArgument> args = ParseArguments(
            tokens, openParenIndex, closeParenIndex, commandText, tokenTextStart, isCanvasMultiLine);

        BescriptCallSignature call = kind switch
        {
            BescriptCallKind.Fun => BuildFunOrToolf(BescriptCallKind.Fun, rawSignature, segmentSpans, args, returnVar, discardsReturn, commandText,
                keywordOffset, openParenOffset, closeParenOffset, returnTypeOffset, returnNameOffset),
            BescriptCallKind.Toolf => BuildFunOrToolf(BescriptCallKind.Toolf, rawSignature, segmentSpans, args, returnVar, discardsReturn, commandText,
                keywordOffset, openParenOffset, closeParenOffset, returnTypeOffset, returnNameOffset),
            BescriptCallKind.Varf => ParseVarf(rawSignature, segmentSpans, args, returnVar, discardsReturn, commandText,
                keywordOffset, openParenOffset, closeParenOffset, returnTypeOffset, returnNameOffset),
            BescriptCallKind.Act => ParseAct(rawSignature, segmentSpans, args, returnVar, discardsReturn, commandText,
                keywordOffset, openParenOffset, closeParenOffset, returnTypeOffset, returnNameOffset),
            BescriptCallKind.Dvf => ParseDvf(rawSignature, segmentSpans, args, returnVar, discardsReturn, commandText,
                keywordOffset, openParenOffset, closeParenOffset, returnTypeOffset, returnNameOffset),
            _ => throw new InvalidOperationException($"调用解析失败: 未识别的调用前缀。文本={commandText}")
        };

        return new CallParseResult(call, commandText, endLine, consumedLineIndex, lineStartColumns);
    }

    private static bool IsWordToken(BescriptToken token)
    {
        return token.Kind is BescriptTokenKind.TypeName or BescriptTokenKind.Identifier or BescriptTokenKind.StatementKeyword or BescriptTokenKind.CallKind;
    }

    /// <summary>
    /// 公开单行调用解析入口：将命令文本整体词法化后解析。
    /// </summary>
    public static BescriptCallSignature ParseCall(string fullText)
    {
        BescriptTokenStream stream = BescriptLexer.Lex(fullText);
        BescriptLineTokens? line = null;
        foreach (BescriptLineTokens candidate in stream.Lines)
        {
            if (candidate.Tokens.Count > 0)
            {
                line = candidate;
                break;
            }
        }

        if (line is null)
        {
            throw new InvalidOperationException($"调用解析失败: 文本为空。文本={fullText}");
        }

        int dummyLineIndex = 0;
        return ParseCallTokens(line, line.Tokens, [], dummyLineIndex).Call;
    }

    private static BescriptCallSignature BuildFunOrToolf(
        BescriptCallKind kind,
        string rawSignature,
        List<BescriptCallSegmentSpan> segmentSpans,
        List<BescriptCallArgument> args,
        ReturnVarDecl? returnVar,
        bool discardsReturn,
        string fullText,
        int keywordOffset,
        int openParenOffset,
        int closeParenOffset,
        int returnTypeOffset,
        int returnNameOffset)
    {
        BescriptTextCodec.EnsureNoEscaped(rawSignature.AsSpan(), "方法名");
        string name = BescriptTextCodec.RecoverInvalidChar(rawSignature.AsSpan());
        return kind == BescriptCallKind.Fun
            ? new FunCallSignature(name, name, args, SegmentSpans: segmentSpans, ReturnVar: returnVar, DiscardsReturn: discardsReturn,
                KeywordOffset: keywordOffset, OpenParenOffset: openParenOffset, CloseParenOffset: closeParenOffset, ReturnTypeOffset: returnTypeOffset, ReturnNameOffset: returnNameOffset)
            : new ToolfCallSignature(name, name, args, SegmentSpans: segmentSpans, ReturnVar: returnVar, DiscardsReturn: discardsReturn,
                KeywordOffset: keywordOffset, OpenParenOffset: openParenOffset, CloseParenOffset: closeParenOffset, ReturnTypeOffset: returnTypeOffset, ReturnNameOffset: returnNameOffset);
    }

    private static List<BescriptCallArgument> ParseArguments(
        IReadOnlyList<BescriptToken> tokens,
        int openParenIndex,
        int closeParenIndex,
        string commandText,
        int tokenTextStart,
        bool isCanvasMultiLine)
    {
        List<BescriptCallArgument> args = [];
        int i = openParenIndex + 1;
        while (i < closeParenIndex || (isCanvasMultiLine && i < tokens.Count))
        {
            BescriptToken headToken = tokens[i];
            if (headToken.Kind == BescriptTokenKind.ArgumentSeparator)
            {
                i++;
                continue;
            }

            int typeStart = headToken.Column - 1 - tokenTextStart;
            string head = headToken.Text;
            bool isVar = string.Equals(head, "var", StringComparison.Ordinal);

            int sepIndex = -1;
            for (int k = i + 1; k <= closeParenIndex; k++)
            {
                BescriptToken token = tokens[k];
                if (token.Kind is BescriptTokenKind.ArgumentSeparator or BescriptTokenKind.CloseParen)
                {
                    sepIndex = k;
                    break;
                }
            }

            int typeEnd = typeStart + head.Length;
            int valueEnd;
            if (isCanvasMultiLine && sepIndex < 0)
            {
                valueEnd = commandText.Length - 1;
            }
            else if (sepIndex >= 0)
            {
                valueEnd = tokens[sepIndex].Column - 1 - tokenTextStart;
            }
            else
            {
                throw new InvalidOperationException($"调用解析失败: 参数缺少空格分隔。文本={commandText}");
            }

            if (valueEnd == typeEnd)
            {
                throw new InvalidOperationException($"调用解析失败: 参数缺少空格分隔。文本={commandText}");
            }

            string rawValue = commandText[typeEnd..valueEnd];
            int valueStartIndex = typeEnd;
            if (rawValue.Length > 0 && rawValue[0] == ' ')
            {
                rawValue = rawValue[1..];
                valueStartIndex++;
            }

            if (isVar)
            {
                BescriptTextCodec.EnsureNoEscaped(rawValue.AsSpan(), "变量名");
            }

            string decoded = BescriptTextCodec.RecoverInvalidChar(rawValue.AsSpan());
            BescriptVarType? parsedType = null;
            if (!isVar && BescriptVarTypeHelper.TryParse(head) is { } t)
            {
                parsedType = t;
            }

            args.Add(new BescriptCallArgument(isVar, head, rawValue, decoded, parsedType, typeStart, valueStartIndex));
            if (sepIndex >= 0)
            {
                i = sepIndex + 1;
            }
            else
            {
                break;
            }
        }

        return args;
    }

    private static VarfCallSignature ParseVarf(
        string rawSignature,
        List<BescriptCallSegmentSpan> segmentSpans,
        List<BescriptCallArgument> args,
        ReturnVarDecl? returnVar,
        bool discardsReturn,
        string fullText,
        int keywordOffset,
        int openParenOffset,
        int closeParenOffset,
        int returnTypeOffset,
        int returnNameOffset)
    {
        string[] parts = SplitSignature(rawSignature, fullText);
        string ownerType = parts[0];
        if (BescriptVarTypeHelper.TryParse(ownerType) == null)
        {
            throw new InvalidOperationException($"调用解析失败: varf ownerType 非法。文本={fullText}");
        }

        BescriptTextCodec.EnsureNoEscaped(parts[1].AsSpan(), "对象名");
        string ownerName = BescriptTextCodec.RecoverInvalidChar(parts[1].AsSpan());
        BescriptTextCodec.EnsureNoEscaped(parts[2].AsSpan(), "方法名");
        string funcName = BescriptTextCodec.RecoverInvalidChar(parts[2].AsSpan());

        return new VarfCallSignature(ownerType, ownerName, false, funcName, rawSignature, args, SegmentSpans: segmentSpans, ReturnVar: returnVar, DiscardsReturn: discardsReturn,
            KeywordOffset: keywordOffset, OpenParenOffset: openParenOffset, CloseParenOffset: closeParenOffset, ReturnTypeOffset: returnTypeOffset, ReturnNameOffset: returnNameOffset);
    }

    private static ActCallSignature ParseAct(
        string rawSignature,
        List<BescriptCallSegmentSpan> segmentSpans,
        List<BescriptCallArgument> args,
        ReturnVarDecl? returnVar,
        bool discardsReturn,
        string fullText,
        int keywordOffset,
        int openParenOffset,
        int closeParenOffset,
        int returnTypeOffset,
        int returnNameOffset)
    {
        string[] parts = SplitSignature(rawSignature, fullText);
        string ownerType = parts[0];
        if (!int.TryParse(ownerType, out _))
        {
            throw new InvalidOperationException($"调用解析失败: act ownerType 非数字。文本={fullText}");
        }

        string ownerName;
        bool ownerNameIsAlias;
        if (parts[1].StartsWith('/'))
        {
            string rawOwner = parts[1][1..];
            BescriptTextCodec.EnsureNoEscaped(rawOwner.AsSpan(), "对象名");
            ownerName = rawOwner;
            ownerNameIsAlias = true;
        }
        else if (string.Equals(parts[1], "this", StringComparison.Ordinal))
        {
            BescriptTextCodec.EnsureNoEscaped(parts[1].AsSpan(), "对象名");
            ownerName = parts[1];
            ownerNameIsAlias = false;
        }
        else if (!int.TryParse(parts[1], out _))
        {
            BescriptTextCodec.EnsureNoEscaped(parts[1].AsSpan(), "对象名");
            ownerName = parts[1];
            ownerNameIsAlias = true;
        }
        else
        {
            ownerName = parts[1];
            ownerNameIsAlias = false;
        }

        BescriptTextCodec.EnsureNoEscaped(parts[2].AsSpan(), "方法名");
        string funcName = BescriptTextCodec.RecoverInvalidChar(parts[2].AsSpan());

        return new ActCallSignature(ownerType, ownerName, ownerNameIsAlias, funcName, rawSignature, args, SegmentSpans: segmentSpans, ReturnVar: returnVar, DiscardsReturn: discardsReturn,
            KeywordOffset: keywordOffset, OpenParenOffset: openParenOffset, CloseParenOffset: closeParenOffset, ReturnTypeOffset: returnTypeOffset, ReturnNameOffset: returnNameOffset);
    }

    private static DvfCallSignature ParseDvf(
        string rawSignature,
        List<BescriptCallSegmentSpan> segmentSpans,
        List<BescriptCallArgument> args,
        ReturnVarDecl? returnVar,
        bool discardsReturn,
        string fullText,
        int keywordOffset,
        int openParenOffset,
        int closeParenOffset,
        int returnTypeOffset,
        int returnNameOffset)
    {
        string[] parts = SplitSignature(rawSignature, fullText);
        BescriptTextCodec.EnsureNoEscaped(parts[0].AsSpan(), "对象名");
        string ownerName = BescriptTextCodec.RecoverInvalidChar(parts[0].AsSpan());
        string ownerType = parts[1];
        BescriptTextCodec.EnsureNoEscaped(parts[2].AsSpan(), "方法名");
        string funcName = BescriptTextCodec.RecoverInvalidChar(parts[2].AsSpan());

        return new DvfCallSignature(ownerType, ownerName, false, funcName, rawSignature, args, SegmentSpans: segmentSpans, ReturnVar: returnVar, DiscardsReturn: discardsReturn,
            KeywordOffset: keywordOffset, OpenParenOffset: openParenOffset, CloseParenOffset: closeParenOffset, ReturnTypeOffset: returnTypeOffset, ReturnNameOffset: returnNameOffset);
    }

    private static string[] SplitSignature(string rawSignature, string fullText)
    {
        string[] parts = rawSignature.Split('.', 3, StringSplitOptions.None);
        if (parts.Length != 3)
        {
            throw new InvalidOperationException($"调用解析失败: 签名不合法。文本={fullText}");
        }

        return parts;
    }

    private static string RecoverInvalid(string text)
    {
        return BescriptTextCodec.RecoverInvalidChar(text.AsSpan());
    }

    private static InvalidOperationException ParseError(string scriptRelativePath, int line, int column, string message)
    {
        return new InvalidOperationException($"BEScript 解析失败: {scriptRelativePath}:{line}:{column} {message}");
    }
}
