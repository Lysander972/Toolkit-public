using System.Reflection;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Canvas;

/// <summary>
/// Canvas 预览解析 - 纯内存解析，禁止文件 IO。
///
/// 【核心原则】
/// 字段直接平铺在节点类上，通过反射读写，与原版二进制格式完全一致
/// 禁止任何多余的组件分组抽象！禁止任何字段名映射！
/// </summary>
public sealed class CanvasPreview
{
    private static readonly Log CanvasLog = Log.For(LogModule.Canvas);

    private static CanvasNodeResult Fail(string message, string? path = null, int? line = null, ToolDiagnosticSeverity severity = ToolDiagnosticSeverity.Error)
    {
        return new CanvasNodeResult
        {
            Ok = false,
            Diagnostics = { new ToolError(path, line, null, message, severity) }
        };
    }

    #region 静态入口

    internal static CanvasNodeResult ParseBytesStatic(byte[] data)
    {
        CanvasPreview preview = new();
        return preview.ParseBytes(data);
    }

    internal static CanvasNodeResult ParseInlineStatic(string canvasArg)
    {
        CanvasPreview preview = new();
        return preview.ParseInline(canvasArg);
    }

    internal static byte[] ConvertToBinaryStatic(CanvasNode root, string canvasName)
    {
        CanvasPreview preview = new();
        return preview.ConvertToBinary(root, canvasName);
    }

    internal static byte[] ConvertToInlineBinaryStatic(CanvasNode root)
    {
        CanvasPreview preview = new();
        return preview.ConvertToInlineBinary(root);
    }

    #endregion

    #region 实例方法

    public CanvasNodeResult ParseBytes(byte[] data) => ParseBytes(data, string.Empty);

    public CanvasNodeResult ParseInline(string canvasArg)
    {
        if (CanvasInlineArg.Parse(canvasArg) is not CanvasInlineArgData inlineArg)
        {
            string msg = CanvasInlineArg.ClassifyError(canvasArg) switch
            {
                CanvasInlineArgParseErrorKind.Empty => "内联 Canvas 参数为空",
                CanvasInlineArgParseErrorKind.NotCanvasPrefix => "内联 Canvas 参数格式不正确（期望 canvas:名称=Base64 或新版 ==hjson:: 块）",
                CanvasInlineArgParseErrorKind.MissingEquals => "内联 Canvas 参数缺少 '=' 分隔符",
                CanvasInlineArgParseErrorKind.EmptyBase64 => "内联 Canvas Base64 为空",
                CanvasInlineArgParseErrorKind.MissingNodeHjsonTerminator => "新版内联 Canvas 缺少 == 结束标记",
                CanvasInlineArgParseErrorKind.EmptyNodeHjson => "新版内联 Canvas Hjson 为空",
                _ => "内联 Canvas 参数格式不正确"
            };
            return Fail(msg);
        }

        if (inlineArg.FormatKind == CanvasInlineArgFormatKind.NodeHjson)
        {
            return CanvasInlineNodeHjsonCodec.Parse(inlineArg.CanvasName, inlineArg.PayloadText);
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(inlineArg.PayloadText);
        }
        catch (FormatException ex)
        {
            return Fail($"Base64 解码失败: {ex.Message}");
        }

        return ParseBytes(bytes, inlineArg.CanvasName);
    }

    #endregion

    private CanvasNodeResult ParseBytes(byte[] data, string canvasName)
    {
        if (data.Length == 0)
            return Fail("文件内容为空");

        try
        {
            ReadOnlySpan<byte> span = data;
            int offset = 0;

            if (span.Length >= 3 && span[0] == 0x02 && span[1] == 0x76 && span[2] == 0x31)
            {
                offset = 3;
                (canvasName, offset) = BinReader.ReadString(span, offset);
            }

            VarType varType = (VarType)BinReader.Read<byte>(span, offset);
            offset += BinReader.SizeOf<byte>();
            CanvasNode? root = null;

            if (varType == VarType.List || varType == VarType.Canvas)
            {
                ushort count = BinReader.Read<ushort>(span, offset);
                offset += BinReader.SizeOf<ushort>();
                root = new UICanvasNode { name = new StringVar { v = canvasName } };
                root.size = new Vector3Var { X = 1, Y = 1, Z = 0 };
                root.Pos = new Vector3Var { X = 0.5f, Y = 0.5f, Z = 0 };
                root.parentAnchor = new LongVar { v = 4 };
                root.anchor = new LongVar { v = 4 };
                root.widthExtend = new LongVar { v = 1 };
                root.heightExtend = new LongVar { v = 1 };

                for (int i = 0; i < count; i++)
                {
                    if (offset == span.Length)
                        throw new InvalidOperationException($"Canvas 根列表提前结束: 期望 {count} 项, 实际 {i} 项");

                    VarType elemType = (VarType)BinReader.Read<byte>(span, offset);
                    offset += BinReader.SizeOf<byte>();
                    if (elemType == VarType.Dict)
                    {
                        DictVar elemDict = new();
                        offset = elemDict.LoadFromBinary(span, offset);
                        root.Children.Add(ConvertDictToNode(elemDict));
                    }
                    else if (IsUIType(elemType))
                    {
                        if (offset == span.Length || (VarType)span[offset] != VarType.Dict)
                            throw new InvalidOperationException($"Canvas 根列表第 {i} 项类型为 {elemType}，但缺少 Dict payload");
                        offset += BinReader.SizeOf<byte>();
                        DictVar elemDict = new();
                        offset = elemDict.LoadFromBinary(span, offset);
                        root.Children.Add(ConvertDictToNode(elemDict));
                    }
                    else
                    {
                        throw new InvalidOperationException($"Canvas 根列表第 {i} 项类型非法: {elemType}");
                    }
                }
            }
            else if (varType == VarType.Dict)
            {
                DictVar rootDict = new();
                offset = rootDict.LoadFromBinary(span, offset);
                root = ConvertDictToNode(rootDict);
            }
            else if (IsUIType(varType))
            {
                if (offset < span.Length && (VarType)span[offset] == VarType.Dict)
                {
                    offset += BinReader.SizeOf<byte>();
                    DictVar rootDict = new();
                    offset = rootDict.LoadFromBinary(span, offset);
                    root = ConvertDictToNode(rootDict);
                }
                else
                {
                    throw new InvalidOperationException($"UI 类型标记后缺少 Dict payload: {varType}");
                }
            }
            else
            {
                throw new InvalidOperationException($"不支持的 Canvas 根类型: {varType}");
            }

            if (offset < span.Length)
                throw new InvalidOperationException($"Canvas 解析后存在未消费字节: {span.Length - offset}");

            return new CanvasNodeResult
            {
                Ok = true,
                CanvasName = canvasName,
                Root = root
            };
        }
        catch (Exception ex)
        {
            CanvasLog.Exception(ex, "CanvasPreview.ParseBytes");
            return Fail($"解析失败: {ex}", null, null, ToolDiagnosticSeverity.Fatal);
        }
    }

    #region 反射字段

    private static PropertyInfo? GetProperty(CanvasNode node, string fieldName)
    {
        return node switch
        {
            UICanvasNode => typeof(UICanvasNode).GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance),
            UIRectNode => typeof(UIRectNode).GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance),
            UIButtonNode => typeof(UIButtonNode).GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance),
            UILabelNode => typeof(UILabelNode).GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance),
            UITextProNode => typeof(UITextProNode).GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance),
            UIInputNode => typeof(UIInputNode).GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance),
            UIRawImageNode => typeof(UIRawImageNode).GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance),
            UISpineNode => typeof(UISpineNode).GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance),
            UIScrollRectNode => typeof(UIScrollRectNode).GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance),
            _ => null
        };
    }

    private static void SetField(CanvasNode node, string fieldName, VarBase? value)
    {
        PropertyInfo? prop = GetProperty(node, fieldName);
        if (prop == null)
            throw new InvalidOperationException($"节点类型 '{node.Type}' 缺少属性 '{fieldName}'，但 Schema 允许该字段");
        if (!prop.CanWrite)
            throw new InvalidOperationException($"节点类型 '{node.Type}' 的属性 '{fieldName}' 不可写");

        Type propType = prop.PropertyType;

        // 调色板颜色：StringVar → ColorVar
        if (propType == typeof(ColorVar) && value is StringVar sv)
        {
            prop.SetValue(node, new ColorVar { PaletteName = sv.v });
            return;
        }

        // 全程 Var 类型，直接赋值
        prop.SetValue(node, value);
    }

    #endregion

    /// <summary>
    /// 递归将 DictVar 转换为 CanvasNode - 忠实按照字段顺序
    /// 原则：完全通用处理，怎么进怎么出，不做任何字段特判
    /// </summary>
    internal CanvasNode ConvertDictToNode(DictVar dict)
    {
        string typeStr = ((StringVar)dict["type"]).v;
        if (!CanvasNodeSchema.IsSupportedType(typeStr))
            throw new InvalidOperationException($"不支持的 UI 节点类型: {typeStr}");

        CanvasNode node = CanvasNodeSchema.CreateNode(typeStr);

        // 忠实按照二进制字段顺序标记 PresentField（使用 GetOrderedEntries）
        foreach ((zchar key, VarBase? value) in dict.GetOrderedEntries())
        {
            string fieldName = key.ToString();
            node.MarkFieldPresent(fieldName);

            // type 和 sons 需要特殊处理（非 VarBase 属性）
            if (fieldName == "type")
                continue;
            if (fieldName == "sons")
            {
                if (value is ListVar sons)
                {
                    foreach (VarBase child in sons.v)
                    {
                        if (child is not DictVar childDict)
                            throw new InvalidOperationException($"UI 节点缺少 name 字段或 sons 包含非法子项类型: {child.type}");
                        node.Children.Add(ConvertDictToNode(childDict));
                    }
                }
                continue;
            }

            // 所有其他字段（包括 name 和 guid）完全通用处理
            SetField(node, fieldName, value);
        }

        if (!node.IsFieldPresent("name"))
            throw new InvalidOperationException("UI 节点缺少 name 字段");

        return node;
    }

    private static bool IsUIType(VarType type) =>
        type is VarType.UI or VarType.UIRect or VarType.UIButton or VarType.UILabel
            or VarType.UIInput or VarType.UIRawImage or VarType.UISpine;

    /// <summary>
    /// 递归将 CanvasNode 转换为 DictVar - 忠实按照 PresentFieldOrder 顺序
    /// 原则：完全通用处理，怎么进怎么出，不做任何字段特判
    /// </summary>
    internal DictVar ConvertNodeToDict(CanvasNode node)
    {
        DictVar dict = new();

        dict.Add(new zchar("type"), new StringVar { v = node.Type });

        foreach (string fieldName in node.GetPresentFieldOrder())
        {
            if (fieldName == "type" || fieldName == "sons")
                continue;

            PropertyInfo? prop = GetProperty(node, fieldName);

            if (prop?.GetValue(node) is not VarBase value)
            {
                continue;
            }

            if (value is ColorVar c && !string.IsNullOrEmpty(c.PaletteName))
                dict.Add(new zchar(fieldName), new StringVar { v = c.PaletteName });
            else
                dict.Add(new zchar(fieldName), value);
        }

        if (node.Children.Count > 0)
        {
            ListVar sons = new();
            foreach (CanvasNode child in node.Children)
                sons.v.Add(ConvertNodeToDict(child));
            dict.Add(new zchar("sons"), sons);
        }

        return dict;
    }

    /// <summary>
    /// 将 CanvasNode 转换为独立 Canvas 文件格式的二进制数据
    /// </summary>
    internal byte[] ConvertToBinary(CanvasNode root, string canvasName)
    {
        ArgumentNullException.ThrowIfNull(canvasName);

        using BinWriter writer = new(4096);

        writer.Write((byte)0x02);
        writer.Write((byte)0x76);
        writer.Write((byte)0x31);
        writer.WriteAsString(canvasName);

        writer.Write((byte)VarType.List);
        writer.Write((ushort)root.Children.Count);
        foreach (CanvasNode child in root.Children)
        {
            DictVar childDict = ConvertNodeToDict(child);
            childDict.SaveToBinary(writer, 0);
        }

        return writer.ToArray();
    }

    /// <summary>
    /// 将 CanvasNode 转换为内联 Canvas 格式的二进制数据
    /// </summary>
    internal byte[] ConvertToInlineBinary(CanvasNode root)
    {
        using BinWriter writer = new(4096);

        writer.Write((byte)VarType.List);
        writer.Write((ushort)root.Children.Count);
        foreach (CanvasNode child in root.Children)
        {
            DictVar childDict = ConvertNodeToDict(child);
            childDict.SaveToBinary(writer, 0);
        }

        return writer.ToArray();
    }
}
