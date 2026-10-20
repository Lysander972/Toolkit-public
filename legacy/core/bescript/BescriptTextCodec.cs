using System.Text;

namespace Toolkit.Core.Bescript;

/// <summary>
/// 按 BEScript 的转义规则处理脚本文本中的 name/comment/字符串常量等。
/// </summary>
public static class BescriptTextCodec
{
    public static string RecoverInvalidChar(ReadOnlySpan<char> word)
    {
        if (word.IsEmpty)
        {
            return string.Empty;
        }

        // 与原版一致：如果没有转义字符，直接返回原字符串（零分配）
        if (word.IndexOf('@') < 0)
        {
            return word.ToString();
        }

        // 有转义字符时才进行处理
        StringBuilder vsb = new(word.Length);
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            if (c == '@' && i < word.Length - 1)
            {
                i++;
                char c2 = word[i];
                switch (c2)
                {
                    case '1':
                        vsb.Append(':');
                        break;
                    case '2':
                        vsb.Append(';');
                        break;
                    case '3':
                        vsb.Append('=');
                        break;
                    case '4':
                        vsb.Append('(');
                        break;
                    case '5':
                        vsb.Append(')');
                        break;
                    case '6':
                        vsb.Append('/');
                        break;
                    case '7':
                        vsb.Append('\\');
                        break;
                    case '8':
                        vsb.Append(' ');
                        break;
                    case '9':
                        vsb.Append('.');
                        break;
                    case '@':
                        vsb.Append('@');
                        break;
                    default:
                        // 不是转义标记，原样输出
                        vsb.Append('@');
                        vsb.Append(c2);
                        break;
                }
            }
            else
            {
                vsb.Append(c);
            }
        }

        return vsb.ToString();
    }

    public static string ReplaceInvalidChar(ReadOnlySpan<char> word)
    {
        if (word.IsEmpty)
        {
            return string.Empty;
        }

        // 特殊处理 Canvas GUID 格式：==guid:xxx==
        // 格式：==guid: + 32位十六进制 + ==
        // 只转义中间的 : 为 @1，== 前后缀保持不变
        string text = word.ToString();
        if (BescriptSyntax.IsNewRef(text))
        {
            string guid = text[BescriptSyntax.NewRefPrefix.Length..^BescriptSyntax.NewRefSuffix.Length];
            // GUID 本身是纯十六进制，不需要额外转义
            return $"==guid@1{guid}==";
        }

        StringBuilder vsb = new(word.Length * 2);
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            switch (c)
            {
                case '@':
                    vsb.Append("@@");
                    break;
                case ':':
                    vsb.Append("@1");
                    break;
                case '=':
                    vsb.Append("@3");
                    break;
                case '(':
                    vsb.Append("@4");
                    break;
                case ')':
                    vsb.Append("@5");
                    break;
                case '\r':
                case '\n':
                    break;
                default:
                    vsb.Append(c);
                    break;
            }
        }

        return vsb.ToString();
    }

    public static void EnsureNoEscaped(ReadOnlySpan<char> raw, string label)
    {
        if (ReplaceInvalidChar(raw).IndexOf('@') >= 0)
        {
            throw new InvalidOperationException($"{label}不允许包含需转义字符");
        }
    }

    public static void EnsureNoEscaped(string text, int line, int column, string label)
    {
        if (ReplaceInvalidChar(text.AsSpan()).IndexOf('@') >= 0)
        {
            throw new InvalidOperationException($"BEScript 解析失败: {line}:{column} {label}不允许包含需转义字符");
        }
    }
}
