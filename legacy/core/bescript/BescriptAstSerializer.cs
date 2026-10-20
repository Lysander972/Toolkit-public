using System.Text;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Bescript;

/// <summary>
/// 将 BescriptAst 序列化为 BeScript 文本。
/// 与 BescriptAstBuilder（文本→AST）互为逆操作。
/// </summary>
public static class BescriptAstSerializer
{
    /// <summary>
    /// BeScript 文本格式统一使用 LF 换行符。
    /// </summary>
    private const char LF = '\n';

    /// <summary>
    /// 序列化完整的脚本文本（包括头部和 endcode）。
    /// </summary>
    public static string SerializeFull(BescriptAst ast)
    {
        StringBuilder sb = new();

        // 头部（按原版固定顺序：debug → skip → private → guid → color → name → comment → params → return → lines）
        sb.Append(BescriptSyntax.BeginCodeV2).Append(LF);

        if (ast.Header.Debug)
            sb.Append(BescriptSyntax.DebugFlagPrefix).Append(":1").Append(LF);

        if (ast.Header.Skip)
            sb.Append(BescriptSyntax.SkipFlagPrefix).Append(":1").Append(LF);

        if (ast.Header.Private)
            sb.Append(BescriptSyntax.PrivateFlagPrefix).Append(":1").Append(LF);

        if (GuidText.Validate(ast.Header.Guid))
            sb.Append(BescriptSyntax.GuidPrefix).Append(BescriptTextCodec.ReplaceInvalidChar(ast.Header.Guid.AsSpan())).Append(LF);

        if (ast.Header.Color is { } c)
            sb.Append(BescriptSyntax.ColorPrefix).Append(c.R).Append(',').Append(c.G).Append(',').Append(c.B).Append(LF);

        if (ast.Header.Name.Length > 0)
            sb.Append(BescriptSyntax.NamePrefix).Append(BescriptTextCodec.ReplaceInvalidChar(ast.Header.Name.AsSpan())).Append(LF);

        if (!string.IsNullOrEmpty(ast.Header.Comment))
            sb.Append(BescriptSyntax.CommentPrefix).Append(BescriptTextCodec.ReplaceInvalidChar(ast.Header.Comment.AsSpan())).Append(LF);

        if (ast.Header.Parameters.Count > 0)
        {
            sb.Append(BescriptSyntax.ParamsPrefix);
            for (int i = 0; i < ast.Header.Parameters.Count; i++)
            {
                if (i > 0) sb.Append(',');
                BescriptParameter p = ast.Header.Parameters[i];
                sb.Append(p.TypeName).Append(' ').Append(BescriptTextCodec.ReplaceInvalidChar(p.Name.AsSpan()));
            }
            sb.Append(LF);
        }

        if (ast.Header.ReturnParameter is { } ret)
            sb.Append(BescriptSyntax.ReturnPrefix).Append(ret.TypeName).Append(' ').Append(BescriptTextCodec.ReplaceInvalidChar(ret.Name.AsSpan())).Append(LF);

        sb.Append(BescriptSyntax.LinesPrefix).Append(LF);

        // 命令
        foreach (string line in SerializeCommands(ast.RootNodes))
            sb.Append(line).Append(LF);

        sb.Append(BescriptSyntax.EndCodePrefix);
        return sb.ToString();
    }

    /// <summary>
    /// 仅序列化命令部分（不包括头部和 endcode）。
    /// </summary>
    public static List<string> Serialize(BescriptAst ast)
    {
        return SerializeCommands(ast.RootNodes);
    }

#pragma warning disable TK006 // 序列化入口，隔离调用方与内部递归实现
    private static List<string> SerializeCommands(IReadOnlyList<BescriptAstNode> nodes)
    {
        List<string> lines = [];
        SerializeNodes(nodes, lines);
        return lines;
    }
#pragma warning restore TK006

    private static void SerializeNodes(IReadOnlyList<BescriptAstNode> nodes, List<string> lines)
    {
        foreach (BescriptAstNode node in nodes)
        {
            string indent = string.Empty;
            if (node.Depth > 0)
                indent = new string(' ', node.Depth * BescriptSyntax.IndentSize);
            string prefix = string.Empty;
            if (node.IsDisabled)
                prefix = BescriptSyntax.BesDisabledPrefix;

            switch (node.Kind)
            {
                case BescriptStatementKind.If:
                    lines.AddRange(TextLines.SplitLines(prefix + indent + "if{" + SerializeConditionCall(((IHasCall)node).Call)));
                    SerializeNodes(node.Children, lines);
                    lines.Add(indent + "}");
                    break;

                case BescriptStatementKind.While:
                    lines.AddRange(TextLines.SplitLines(prefix + indent + "while{" + SerializeConditionCall(((IHasCall)node).Call)));
                    SerializeNodes(node.Children, lines);
                    lines.Add(indent + "}");
                    break;

                case BescriptStatementKind.Delay:
                    lines.AddRange(TextLines.SplitLines(prefix + indent + "delay " + SerializeCall(((IHasCall)node).Call)));
                    break;

                case BescriptStatementKind.Async:
                    lines.AddRange(TextLines.SplitLines(prefix + indent + "async " + SerializeCall(((IHasCall)node).Call)));
                    break;

                case BescriptStatementKind.Break:
                    lines.Add(prefix + indent + "fun break()");
                    break;

                case BescriptStatementKind.Continue:
                    lines.Add(prefix + indent + "fun continue()");
                    break;

                case BescriptStatementKind.Call:
                    lines.AddRange(TextLines.SplitLines(prefix + indent + SerializeCall(((IHasCall)node).Call)));
                    break;

                case BescriptStatementKind.Comment:
                case BescriptStatementKind.Skip:
                    // Comment/Skip 类型一定是注释格式，直接编码
                    lines.Add(prefix + indent + "//" + BescriptTextCodec.ReplaceInvalidChar(node.Text[2..].AsSpan()));
                    break;
                default:
                    throw new InvalidOperationException($"不支持的语句类型: {node.Kind}");
            }
        }
    }

    private static string SerializeCall(BescriptCallSignature sig)
    {
        StringBuilder sb = new();

        // 返回变量前缀
        if (sig.ReturnVar is { } ret)
        {
            sb.Append(ret.TypeName);
            sb.Append(' ');
            sb.Append(BescriptTextCodec.ReplaceInvalidChar(ret.Name.AsSpan()));
            sb.Append('=');
        }
        else if (sig.DiscardsReturn)
        {
            sb.Append('=');
        }

        // 调用类型和签名
        switch (sig.Kind)
        {
            case BescriptCallKind.Fun:
                sb.Append("fun ");
                sb.Append(BescriptTextCodec.ReplaceInvalidChar(sig.FunctionName.AsSpan()));
                break;

            case BescriptCallKind.Toolf:
                sb.Append("toolf ");
                sb.Append(BescriptTextCodec.ReplaceInvalidChar(sig.FunctionName.AsSpan()));
                break;

            case BescriptCallKind.Varf:
                {
                    VarfCallSignature v = (VarfCallSignature)sig;
                    sb.Append("varf ");
                    sb.Append(v.OwnerType);
                    sb.Append('.');
                    sb.Append(BescriptTextCodec.ReplaceInvalidChar(v.OwnerName.AsSpan()));
                    sb.Append('.');
                    sb.Append(BescriptTextCodec.ReplaceInvalidChar(v.FunctionName.AsSpan()));
                    break;
                }

            case BescriptCallKind.Act:
                {
                    ActCallSignature a = (ActCallSignature)sig;
                    sb.Append("act ");
                    sb.Append(a.OwnerType);
                    sb.Append('.');
                    if (a.OwnerNameIsAlias)
                    {
                        sb.Append('/');
                    }
                    sb.Append(BescriptTextCodec.ReplaceInvalidChar(a.OwnerName.AsSpan()));
                    sb.Append('.');
                    sb.Append(BescriptTextCodec.ReplaceInvalidChar(a.FunctionName.AsSpan()));
                    break;
                }

            case BescriptCallKind.Dvf:
                {
                    DvfCallSignature d = (DvfCallSignature)sig;
                    sb.Append("dvf ");
                    sb.Append(BescriptTextCodec.ReplaceInvalidChar(d.OwnerName.AsSpan()));
                    sb.Append('.');
                    sb.Append(d.OwnerType);
                    sb.Append('.');
                    sb.Append(BescriptTextCodec.ReplaceInvalidChar(d.FunctionName.AsSpan()));
                    break;
                }

            case BescriptCallKind.None:
                throw new InvalidOperationException("调用签名未指定类型");

            default:
                throw new InvalidOperationException($"不支持的调用类型: {sig.Kind}");
        }

        // 参数
        sb.Append('(');
        for (int i = 0; i < sig.Arguments.Count; i++)
        {
            if (i > 0) sb.Append(':');
            BescriptCallArgument arg = sig.Arguments[i];
            sb.Append(arg.TypeName);
            sb.Append(' ');
            sb.Append(FormatArgRaw(arg));
        }
        sb.Append(')');

        return sb.ToString();
    }

    private static string SerializeConditionCall(BescriptCallSignature sig)
    {
        // 条件上下文(if/while)中不允许带返回前缀，仅序列化调用本身
        return SerializeCall(sig with { ReturnVar = null, DiscardsReturn = false });
    }

    /// <summary>保证 Bool 常量输出为 0/1，禁止 true/false 污染。</summary>
    private static string FormatArgRaw(BescriptCallArgument arg)
    {
        if (!arg.IsVariable &&
            string.Equals(arg.TypeName, nameof(BescriptVarType.Canvas), StringComparison.Ordinal) &&
            CanvasInlineArg.Parse(arg.DecodedText) is CanvasInlineArgData { FormatKind: CanvasInlineArgFormatKind.NodeHjson } inlineArg)
        {
            // TryParse 提取的 PayloadText 包含格式边界 \n（如 "\nline1\n"），
            // 而 EncodeNodeHjsonForBescript 会添加自己的 \n 边界，必须先剥离避免双重换行。
            string payload = inlineArg.PayloadText.StartsWith('\n')
                ? inlineArg.PayloadText[1..]
                : inlineArg.PayloadText;
            return CanvasInlineArg.EncodeNodeHjsonForBescript(inlineArg.CanvasName, payload);
        }

        string raw = BescriptTextCodec.ReplaceInvalidChar(arg.DecodedText.AsSpan());
        if (arg.IsVariable)
        {
            return raw;
        }

        if (!string.Equals(arg.TypeName, nameof(BescriptVarType.Bool), StringComparison.Ordinal))
        {
            return raw;
        }

        string t = arg.DecodedText;
        if (t.Equals("1", StringComparison.Ordinal)) return "1";
        if (t.Equals("0", StringComparison.Ordinal)) return "0";

        throw new InvalidOperationException($"Bool 参数只允许 0/1，当前为: {arg.DecodedText}");
    }
}
