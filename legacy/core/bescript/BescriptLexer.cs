using Toolkit.Core.Canvas;

namespace Toolkit.Core.Bescript;

/// <summary>
/// BeScript 词法器：唯一文本分析者。
/// 产出带 1-based 列号的 token 流；命令区内跨行内联 Canvas 块按整行原文收集。
/// </summary>
internal static class BescriptLexer
{
    public static BescriptTokenStream Lex(string text)
    {
        List<BescriptLineTokens> lines = [];
        using StringReader reader = new(text);
        string? line;
        int lineNumber = 1;
        bool inCanvasRaw = false;
        while ((line = reader.ReadLine()) is not null)
        {
            (IReadOnlyList<BescriptToken> tokens, inCanvasRaw) = LexLine(line, lineNumber, inCanvasRaw);
            lines.Add(new BescriptLineTokens(lineNumber, line, tokens));
            lineNumber++;
        }

        return new BescriptTokenStream(lines);
    }

    private static (IReadOnlyList<BescriptToken> Tokens, bool InCanvasRaw) LexLine(string line, int lineNumber, bool inCanvasRaw)
    {
        List<BescriptToken> tokens = [];

        if (inCanvasRaw)
        {
            tokens.Add(new BescriptToken(BescriptTokenKind.RawCanvasBlock, line, lineNumber, 1, line.Length));
            if (line.Contains(CanvasInlineArg.NodeHjsonRawEndWithCloseParen, StringComparison.Ordinal))
            {
                inCanvasRaw = false;
            }

            return (tokens, inCanvasRaw);
        }

        int firstContent = 0;
        while (firstContent < line.Length && line[firstContent] == ' ')
        {
            firstContent++;
        }

        ReadOnlySpan<char> rest = line.AsSpan(firstContent);

        // begincode 允许带版本后缀（begincodeV2），token 取到首个空白
        if (rest.StartsWith(BescriptSyntax.BeginCodePrefix, StringComparison.Ordinal))
        {
            int len = 0;
            while (len < rest.Length && !char.IsWhiteSpace(rest[len]))
            {
                len++;
            }

            tokens.Add(new BescriptToken(BescriptTokenKind.BeginCode, line.Substring(firstContent, len), lineNumber, firstContent + 1, len));
            tokens.Add(NewLineToken(line, lineNumber));
            return (tokens, false);
        }

        if (rest.StartsWith(BescriptSyntax.EndCodePrefix, StringComparison.Ordinal) &&
            (rest.Length == BescriptSyntax.EndCodePrefix.Length || char.IsWhiteSpace(rest[BescriptSyntax.EndCodePrefix.Length])))
        {
            tokens.Add(new BescriptToken(BescriptTokenKind.EndCode, BescriptSyntax.EndCodePrefix, lineNumber, firstContent + 1, BescriptSyntax.EndCodePrefix.Length));
            tokens.Add(NewLineToken(line, lineNumber));
            return (tokens, false);
        }

        if (TryLexHeaderLine(line, lineNumber, firstContent, tokens))
        {
            tokens.Add(NewLineToken(line, lineNumber));
            return (tokens, false);
        }

        int commandStart = LexLineNumber(line, lineNumber, firstContent, tokens);
        bool enteredCanvasRaw = LexCommand(line, lineNumber, commandStart, tokens);
        tokens.Add(NewLineToken(line, lineNumber));
        return (tokens, enteredCanvasRaw);
    }

    private static BescriptToken NewLineToken(string line, int lineNumber)
    {
        return new BescriptToken(BescriptTokenKind.NewLine, string.Empty, lineNumber, line.Length + 1, 0);
    }

    private static bool TryLexHeaderLine(string line, int lineNumber, int firstContent, List<BescriptToken> tokens)
    {
        ReadOnlySpan<char> rest = line.AsSpan(firstContent);
        int colonIndex = rest.IndexOf(':');
        if (colonIndex > 0)
        {
            string key = rest[..colonIndex].ToString();
            if (key is "name" or "comment" or "guid" or "color" or "params" or "return" or "lines"
                or "debug" or "skip" or "private")
            {
                tokens.Add(new BescriptToken(BescriptTokenKind.HeaderKey, key, lineNumber, firstContent + 1, key.Length));
                tokens.Add(new BescriptToken(BescriptTokenKind.HeaderValue, rest[(colonIndex + 1)..].ToString(), lineNumber, firstContent + colonIndex + 2, rest.Length - colonIndex - 1));
                return true;
            }
        }

        if (rest.SequenceEqual(BescriptSyntax.DebugFlagPrefix.AsSpan()) ||
            rest.SequenceEqual(BescriptSyntax.SkipFlagPrefix.AsSpan()) ||
            rest.SequenceEqual(BescriptSyntax.PrivateFlagPrefix.AsSpan()))
        {
            tokens.Add(new BescriptToken(BescriptTokenKind.HeaderKey, rest.ToString(), lineNumber, firstContent + 1, rest.Length));
            return true;
        }

        return false;
    }

    /// <summary>
    /// 行号前缀：[空格]*[数字]+:。行号前缀的空白不进入命令文本与缩进。
    /// 返回命令内容起点（0-based）。无行号时返回 0，使前导空格由 LexCommand 收集为缩进。
    /// </summary>
    private static int LexLineNumber(string line, int lineNumber, int firstContent, List<BescriptToken> tokens)
    {
        int index = firstContent;
        while (index < line.Length && char.IsDigit(line[index]))
        {
            index++;
        }

        if (index > firstContent && index < line.Length && line[index] == ':')
        {
            int len = index - firstContent;
            tokens.Add(new BescriptToken(BescriptTokenKind.LineNumber, line.Substring(firstContent, len), lineNumber, firstContent + 1, len));
            return index + 1;
        }

        return 0;
    }

    /// <summary>
    /// 命令区行内容：缩进 → /* → 缩进 → 注释/命令体。
    /// 返回是否进入跨行 Canvas raw 模式。
    /// </summary>
    private static bool LexCommand(string line, int lineNumber, int start, List<BescriptToken> tokens)
    {
        int index = start;
        int indentStart = index;
        while (index < line.Length && line[index] == ' ')
        {
            index++;
        }

        if (index > indentStart)
        {
            tokens.Add(new BescriptToken(BescriptTokenKind.Indent, line[indentStart..index], lineNumber, indentStart + 1, index - indentStart));
        }

        if (index < line.Length && line.AsSpan(index).StartsWith(BescriptSyntax.BesDisabledPrefix, StringComparison.Ordinal))
        {
            tokens.Add(new BescriptToken(BescriptTokenKind.DisabledPrefix, BescriptSyntax.BesDisabledPrefix, lineNumber, index + 1, BescriptSyntax.BesDisabledPrefix.Length));
            index += BescriptSyntax.BesDisabledPrefix.Length;
            int disabledIndentStart = index;
            while (index < line.Length && line[index] == ' ')
            {
                index++;
            }

            if (index > disabledIndentStart)
            {
                tokens.Add(new BescriptToken(BescriptTokenKind.Indent, line[disabledIndentStart..index], lineNumber, disabledIndentStart + 1, index - disabledIndentStart));
            }
        }

        if (index < line.Length && line.AsSpan(index).StartsWith(BescriptSyntax.BesCommentPrefix, StringComparison.Ordinal))
        {
            tokens.Add(new BescriptToken(BescriptTokenKind.CommentPrefix, BescriptSyntax.BesCommentPrefix, lineNumber, index + 1, BescriptSyntax.BesCommentPrefix.Length));
            int bodyStart = index + BescriptSyntax.BesCommentPrefix.Length;
            tokens.Add(new BescriptToken(BescriptTokenKind.RawValue, line[bodyStart..], lineNumber, bodyStart + 1, line.Length - bodyStart));
            return false;
        }

        return LexCommandBody(line, lineNumber, index, tokens);
    }

    private static bool LexCommandBody(string line, int lineNumber, int start, List<BescriptToken> tokens)
    {
        bool inCanvasRaw = false;
        int index = start;
        while (index < line.Length)
        {
            char ch = line[index];
            if (char.IsWhiteSpace(ch))
            {
                index++;
                continue;
            }

            if (line.AsSpan(index).StartsWith(CanvasInlineArg.NodeHjsonRawMarker, StringComparison.Ordinal))
            {
                string raw = line[index..];
                tokens.Add(new BescriptToken(BescriptTokenKind.RawValue, raw, lineNumber, index + 1, raw.Length));
                inCanvasRaw = !raw.EndsWith(CanvasInlineArg.NodeHjsonSuffix, StringComparison.Ordinal);
                index = line.Length;
                continue;
            }

            BescriptTokenKind? oneCharKind = ch switch
            {
                '=' => BescriptTokenKind.Assign,
                '.' => BescriptTokenKind.Dot,
                '(' => BescriptTokenKind.OpenParen,
                ')' => BescriptTokenKind.CloseParen,
                ':' => BescriptTokenKind.ArgumentSeparator,
                '{' => BescriptTokenKind.OpenBrace,
                '}' => BescriptTokenKind.CloseBrace,
                _ => null
            };
            if (oneCharKind is { } kind)
            {
                tokens.Add(new BescriptToken(kind, line[index].ToString(), lineNumber, index + 1, 1));
                index++;
                continue;
            }

            int wordStart = index;
            while (index < line.Length && !char.IsWhiteSpace(line[index]) && line[index] is not '=' and not '.' and not '(' and not ')' and not ':' and not '{' and not '}')
            {
                index++;
            }

            string word = line[wordStart..index];
            if (word.StartsWith(BescriptSyntax.BesCommentPrefix, StringComparison.Ordinal))
            {
                tokens.Add(new BescriptToken(BescriptTokenKind.CommentPrefix, BescriptSyntax.BesCommentPrefix, lineNumber, wordStart + 1, BescriptSyntax.BesCommentPrefix.Length));
                string body = line[(wordStart + BescriptSyntax.BesCommentPrefix.Length)..];
                tokens.Add(new BescriptToken(BescriptTokenKind.RawValue, body, lineNumber, wordStart + BescriptSyntax.BesCommentPrefix.Length + 1, body.Length));
                return false;
            }

            BescriptTokenKind wordKind = word switch
            {
                "if" or "while" or "delay" or "async" or "break" or "continue" => BescriptTokenKind.StatementKeyword,
                "fun" or "varf" or "toolf" or "act" or "dvf" or "var" => BescriptTokenKind.CallKind,
                _ => BescriptVarTypeHelper.TryParse(word) == null ? BescriptTokenKind.Identifier : BescriptTokenKind.TypeName
            };
            tokens.Add(new BescriptToken(wordKind, word, lineNumber, wordStart + 1, word.Length));
        }

        return inCanvasRaw;
    }
}
