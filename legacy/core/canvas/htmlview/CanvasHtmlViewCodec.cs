using System.Globalization;
using System.Reflection;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Canvas;

internal static class CanvasHtmlViewConstants
{
    public const string RootTag = "BeCanvas";
    public const string RootAttrName = "name";
    public const string RootAttrWidth = "width";
    public const string RootAttrHeight = "height";
    public const int DefaultRootWidth = 1080;
    public const int DefaultRootHeight = 2160;
    public const string HtmlLang = "zh-CN";

    // 统一缩进规范：每级 2 空格
    public const int SpacesPerIndent = 2;

    // 文档结构缩进层级
    public const int IndentHtml = 0;       // <html> 标签层级
    public const int IndentHeadBody = 1;   // <head>/<body> 标签层级
    public const int IndentHeadContent = 2; // <head> 内容层级

    // Canvas 内容缩进层级（body 内）
    public const int IndentRoot = 1;       // BeCanvas 标签层级
    public const int IndentChild = 2;      // BeCanvas 直接子节点层级
}

/// <summary>
/// 枚举名称映射表
/// 架构：HTML 视图使用枚举名称而非数字，增强可读性
/// </summary>
internal static class CanvasEnumMapping
{
    // TextAnchor: parentAnchor, anchor, childAlignment, Alignment
    private static readonly Dictionary<long, string> TextAnchorNames = new()
    {
        [0] = "UPPER_LEFT",
        [1] = "UPPER_CENTER",
        [2] = "UPPER_RIGHT",
        [3] = "MIDDLE_LEFT",
        [4] = "MIDDLE_CENTER",
        [5] = "MIDDLE_RIGHT",
        [6] = "LOWER_LEFT",
        [7] = "LOWER_CENTER",
        [8] = "LOWER_RIGHT"
    };

    private static readonly Dictionary<string, long> TextAnchorValues = BuildReverseMap(TextAnchorNames);

    // ExtentType: widthExtend, heightExtend
    private static readonly Dictionary<long, string> ExtentTypeNames = new()
    {
        [0] = "ABSOLUTE",
        [1] = "PERCENT",
        [2] = "TO_EDGE",
        [3] = "ASPECT"
    };

    private static readonly Dictionary<string, long> ExtentTypeValues = BuildReverseMap(ExtentTypeNames);

    // FillMethodType: fillMethod
    private static readonly Dictionary<long, string> FillMethodTypeNames = new()
    {
        [-1] = "NONE",
        [0] = "HORIZONTAL",
        [1] = "VERTICAL",
        [2] = "RADIAL_90",
        [3] = "RADIAL_180",
        [4] = "RADIAL_360"
    };

    private static readonly Dictionary<string, long> FillMethodTypeValues = BuildReverseMap(FillMethodTypeNames);

    // UIInputContentType: contentType
    private static readonly Dictionary<long, string> ContentTypeNames = new()
    {
        [0] = "STANDARD",
        [1] = "INTEGER_NUMBER",
        [2] = "DECIMAL_NUMBER",
        [3] = "PASSWORD"
    };

    private static readonly Dictionary<string, long> ContentTypeValues = BuildReverseMap(ContentTypeNames);

    /// <summary>
    /// 构建反向映射（名称 → 数值）
    /// 【硬约束】禁止大小写不敏感！必须精确匹配。
    /// </summary>
    private static Dictionary<string, long> BuildReverseMap(Dictionary<long, string> forward)
    {
        // 【硬约束】使用 StringComparer.Ordinal 而非 OrdinalIgnoreCase
        // 枚举名称必须精确匹配（如 UPPER_LEFT），禁止任何形式的大小写容忍
        Dictionary<string, long> reverse = new(StringComparer.Ordinal);
        foreach ((long k, string? v) in forward)
        {
            reverse[v] = k;
        }
        return reverse;
    }

    /// <summary>
    /// 将枚举数值转换为名称（用于 ToHtml）
    /// </summary>
    public static string? ToName(string field, long value)
    {
        return field switch
        {
            "parentAnchor" or "anchor" or "childAlignment" or "Alignment" =>
                TextAnchorNames.GetValueOrDefault(value),
            "widthExtend" or "heightExtend" =>
                ExtentTypeNames.GetValueOrDefault(value),
            "fillMethod" =>
                FillMethodTypeNames.GetValueOrDefault(value),
            "contentType" =>
                ContentTypeNames.GetValueOrDefault(value),
            _ => null
        };
    }

    /// <summary>
    /// 将枚举名称转换为数值（用于 FromHtml）
    /// </summary>
    public static long? ToValue(string field, string name)
    {
        Dictionary<string, long>? map = field switch
        {
            "parentAnchor" or "anchor" or "childAlignment" or "Alignment" => TextAnchorValues,
            "widthExtend" or "heightExtend" => ExtentTypeValues,
            "fillMethod" => FillMethodTypeValues,
            "contentType" => ContentTypeValues,
            _ => null
        };

        if (map == null)
            return null;
        if (!map.TryGetValue(name, out long value))
            return null;
        return value;
    }

    /// <summary>
    /// 判断字段是否为枚举类型
    /// </summary>
    public static bool IsEnumField(string field)
    {
        return field is "parentAnchor" or "anchor" or "childAlignment" or "Alignment"
            or "widthExtend" or "heightExtend"
            or "fillMethod"
            or "contentType";
    }
}

/// <summary>
/// Canvas HTML 视图编解码器
///
/// 架构（明确对称）：
/// - 二进制 ←→ VarBase ←→ CanvasNode(类似AST) ←→ HTML(树形结构)
///
/// 核心转换：CanvasNode ↔ VarBase（由 CanvasPreview 负责，正反对称）
/// 视图转换：CanvasNode ↔ HTML（由本类负责）
/// 职责边界：本类只做转换，不承载写操作业务校验
///
/// 【Canvas 持久化字段处理原则】
/// 1. 原版导出哪些字段，HTML 就输出哪些字段；原版不导出的，HTML 也不输出
/// 2. 原版读取时期望哪些字段必须存在，HTML 解析时就必须要求这些字段
/// 3. 二进制写回时仅固定 type 在最前、sons 在最后；其余字段顺序保持节点现有顺序
/// 4. 禁止任何"贴心"的默认值补丁！忠实还原原版行为！
/// 5. null 就是 null，空串就是空串，禁止混淆和自动转换！
/// </summary>
public static class CanvasHtmlViewCodec
{
    #region 二进制 → HTML（内部方法，供 CanvasConverter 使用）

    /// <summary>
    /// 二进制 → HTML 独立版
    /// 内部方法，请使用 CanvasConverter.BinaryToHtml
    /// </summary>
    internal static string ToHtml(byte[] canvasBytes) => ToHtml(canvasBytes, null);

    internal static string ToHtml(byte[] canvasBytes, string? supportPathPrefix)
    {
        // 二进制 → VarBase → CanvasNode → HTML
        CanvasNodeResult result = CanvasPreview.ParseBytesStatic(canvasBytes);

        if (!result.Ok || result.Root == null)
        {
            throw new InvalidOperationException($"Canvas 解析失败: {result.Diagnostics[0].Message}");
        }

        return ToHtmlFromNodeResult(result.Root, result.CanvasName, supportPathPrefix);
    }

    /// <summary>
    /// 从 CanvasNode 生成 HTML
    /// 架构：CanvasNode(类似AST) → HTML(树形结构)
    /// </summary>
    private static string ToHtmlFromNodeResult(CanvasNode root, string canvasName, string? supportPathPrefix, string? canvasRelativePath = null)
    {
        StringBuilder sb = new();
        AppendDocumentHead(sb, supportPathPrefix, canvasRelativePath);

        AppendIndent(sb, CanvasHtmlViewConstants.IndentRoot);
        sb.Append('<').Append(CanvasHtmlViewConstants.RootTag);

        // name 属性（始终输出，与二进制解析一致）
        sb.Append(' ').Append(CanvasHtmlViewConstants.RootAttrName).Append("=\"").Append(Escape(canvasName)).Append('"');

        // width/height 固定默认值（不从内容推导）
        sb.Append(' ').Append(CanvasHtmlViewConstants.RootAttrWidth).Append("=\"").Append(CanvasHtmlViewConstants.DefaultRootWidth).Append('"');
        sb.Append(' ').Append(CanvasHtmlViewConstants.RootAttrHeight).Append("=\"").Append(CanvasHtmlViewConstants.DefaultRootHeight).Append('"');

        // 处理子节点（自定义标签禁止自闭合，必须显式闭合，避免 HTML 解析器错乱层级）
        if (root.Children.Count == 0)
        {
            sb.Append("></").Append(CanvasHtmlViewConstants.RootTag).Append(">\n");
        }
        else
        {
            sb.Append(">\n");
            for (int childIndex = 0; childIndex < root.Children.Count; childIndex++)
            {
                CanvasNode child = root.Children[childIndex];
                WriteHtmlNode(sb, child, CanvasHtmlViewConstants.IndentChild);
            }
            AppendIndent(sb, CanvasHtmlViewConstants.IndentRoot);
            sb.Append("</").Append(CanvasHtmlViewConstants.RootTag).Append(">\n");
        }

        AppendDocumentTail(sb);
        return sb.ToString();
    }

    /// <summary>
    /// 写入单个节点 HTML（从 CanvasNode）
    /// 架构：CanvasNode → HTML
    /// 原则：只输出 PresentFields 中存在的字段，保持 roundtrip 字节级一致性
    /// </summary>
    private static void WriteHtmlNode(StringBuilder sb, CanvasNode node, int indent)
    {
        string uiType = node.Type;
        CanvasHtmlTag tag = CanvasHtmlTagSchema.FindTagByCanvasType(uiType)
            ?? throw new InvalidOperationException($"HTML视图不支持UI类型: {uiType}");

        if (string.Equals(tag.TagName, CanvasHtmlViewConstants.RootTag, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HTML视图禁止嵌套Canvas");
        }

        AppendIndent(sb, indent);
        sb.Append('<').Append(tag.TagName);

        // 完全按照 PresentFieldOrder 顺序输出属性，保证 roundtrip 字节级一致性
        Dictionary<string, CanvasHtmlAttribute> attrByField = new(StringComparer.Ordinal);
        foreach (CanvasHtmlAttribute attr in tag.Attributes)
        {
            attrByField[attr.CanvasField] = attr;
        }

        foreach (string canvasField in node.GetPresentFieldOrder())
        {
            if (!attrByField.TryGetValue(canvasField, out CanvasHtmlAttribute? attr))
                continue;

            if (attr.Kind == CanvasHtmlAttributeKind.Meta)
                continue;

            string? value = GetNodeFieldValue(node, canvasField);
            if (value == null)
                continue;

            sb.Append(' ').Append(attr.HtmlName).Append("=\"").Append(Escape(value)).Append('"');
        }

        // 处理子节点（自定义标签禁止自闭合，必须显式闭合，避免 HTML 解析器错乱层级）
        if (node.Children.Count == 0)
        {
            sb.Append("></").Append(tag.TagName).Append(">\n");
            return;
        }

        sb.Append(">\n");
        for (int childIndex = 0; childIndex < node.Children.Count; childIndex++)
        {
            CanvasNode child = node.Children[childIndex];
            WriteHtmlNode(sb, child, indent + 1);
        }
        AppendIndent(sb, indent);
        sb.Append("</").Append(tag.TagName).Append(">\n");
    }

    /// <summary>
    /// 从 CanvasNode 获取字段值（转换为 HTML 属性值）
    /// 架构：CanvasNode → HTML 字符串
    /// 原则：枚举转为名称增强可读性，其他原样透传
    /// </summary>
    private static string? GetNodeFieldValue(CanvasNode node, string canvasField)
    {
        PropertyInfo prop = node.GetType().GetProperty(canvasField, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"节点类型 '{node.Type}' 缺少属性 '{canvasField}'");
        VarBase value = prop.GetValue(node) switch
        {
            VarBase varValue => varValue,
            _ => throw new InvalidOperationException($"节点类型 '{node.Type}' 属性 '{canvasField}' 不是 Canvas Var 类型")
        };

        // 枚举字段转为名称
        if (value is LongVar lv && CanvasEnumMapping.IsEnumField(canvasField))
        {
            string? enumName = CanvasEnumMapping.ToName(canvasField, lv.v);
            if (enumName != null)
                return enumName;
        }

        // 其他类型转换
        return value switch
        {
            StringVar sv => sv.v,
            LongVar l => l.v.ToString(CultureInfo.InvariantCulture),
            FloatVar f => FloatToMinimalString(f.v),
            BoolVar b when b.v => "1",
            BoolVar => "0",
            Vector3Var vec => $"{FloatToMinimalString(vec.X)},{FloatToMinimalString(vec.Y)},{FloatToMinimalString(vec.Z)}",
            ColorVar color => ColorToString(color),
            ListVar list => StringListToString(list),
            _ => throw new InvalidOperationException($"不支持的 Canvas Var 类型: {value.GetType().Name}")
        };
    }

    /// <summary>
    /// 将 HTML 字符串转换为 Var 类型（通过反射获取目标属性类型）
    /// </summary>
    private static VarBase ConvertHtmlValueToVar(CanvasNode node, string fieldName, string value)
    {
        PropertyInfo prop = node.GetType().GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"节点类型 '{node.Type}' 缺少属性 '{fieldName}'");

        Type targetType = prop.PropertyType;

        // 枚举字段
        if (CanvasEnumMapping.IsEnumField(fieldName))
        {
            long? enumValue = CanvasEnumMapping.ToValue(fieldName, value);
            if (enumValue.HasValue)
                return new LongVar { v = enumValue.Value };
            throw new InvalidOperationException($"字段 '{fieldName}' 需要有效的枚举名称: {value}");
        }

        // 根据目标类型转换
        if (targetType == typeof(Vector3Var))
            return ParseVector3(value, fieldName);
        if (targetType == typeof(ColorVar))
            return ParseColor(value, fieldName);
        if (targetType == typeof(BoolVar))
        {
            if (value == "1") return new BoolVar { v = true };
            if (value == "0") return new BoolVar { v = false };
            throw new InvalidOperationException($"字段 {fieldName} 必须为 0/1: {value}");
        }
        if (targetType == typeof(LongVar))
            return new LongVar { v = long.Parse(value, CultureInfo.InvariantCulture) };
        if (targetType == typeof(FloatVar))
            return new FloatVar { v = float.Parse(value, CultureInfo.InvariantCulture) };
        if (targetType == typeof(StringVar))
            return new StringVar { v = value };
        if (targetType == typeof(ListVar))
            return ParseStringList(value);

        throw new InvalidOperationException($"不支持的目标类型: {targetType.Name}");
    }

    /// <summary>
    /// 颜色转 HTML 属性值
    ///
    /// 【颜色格式规范 - 唯二格式，严格区分】
    /// 1. rgba(r,g,b,a) - RGB 颜色值，范围 0-255（支持小数）
    /// 2. palette(name) - 调色板名称，引用 ColorPalette 中的预定义颜色
    ///
    /// 禁止其他格式！禁止其他格式！禁止其他格式！
    /// </summary>
    private static string ColorToString(ColorVar color)
    {
        // 调色板名称：输出 palette(name) 格式
        if (!string.IsNullOrEmpty(color.PaletteName))
        {
            return $"palette({color.PaletteName})";
        }

        // RGB 颜色：每通道尝试整数表示（最短无损），失败回退 TruncateDoubleMantissa + G17
        string r = ColorChannelToMinimalString(color.R);
        string g = ColorChannelToMinimalString(color.G);
        string b = ColorChannelToMinimalString(color.B);
        string a = ColorChannelToMinimalString(color.A);
        return $"rgba({r},{g},{b},{a})";
    }

    /// <summary>
    /// Float/Vector3 分量：double 冗余精度 + 最短无损表示。
    /// 算法结构：TruncateDoubleMantissa → 尝试最短表示 → float 层按位验证 → 回退全精度。
    /// 注：float→double 是 IEEE 754 精确提升，TruncateDoubleMantissa 对纯 float 值为恒等操作，
    /// 但保持与 ColorChannelToMinimalString 相同的三步结构。
    /// </summary>
    private static string FloatToMinimalString(float value)
    {
        // 步骤1: double 冗余精度
        double A = TruncateDoubleMantissa((double)value, keepFractionBits: 24);

        // 步骤2: 尝试最短无损表示（net10 Ryu 格式化器）
        string C = value.ToString("R", CultureInfo.InvariantCulture);

        // 步骤3: float 层按位验证
        float B = float.Parse(C, CultureInfo.InvariantCulture);
        if (BitConverter.SingleToInt32Bits(B) == BitConverter.SingleToInt32Bits(value))
            return C;

        // 回退: G9 保底
        return value.ToString("G9", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Color RGBA 通道：double 冗余精度 + 最短无损表示。
    /// 算法结构：TruncateDoubleMantissa → 尝试整数 → float 层按位验证 → 回退全精度。
    /// 验证模拟 ParseColor 路径：(float)(double.Parse(C) / 255.0)。
    /// </summary>
    private static string ColorChannelToMinimalString(float channelValue)
    {
        // 步骤1: double 冗余精度（*255 域变换引入 double 噪声，截断清理）
        double A = TruncateDoubleMantissa((double)channelValue * 255.0, keepFractionBits: 24);

        // 步骤2: 尝试整数表示（原版 ColorPanel: 0-255 整数）
        int intVal = (int)Math.Round((double)channelValue * 255.0);
        intVal = Math.Clamp(intVal, 0, 255);

        // 步骤3: float 层按位验证（模拟 ParseColor 路径）
        float B = (float)((double)intVal / 255.0);
        if (BitConverter.SingleToInt32Bits(B) == BitConverter.SingleToInt32Bits(channelValue))
            return intVal.ToString(CultureInfo.InvariantCulture);

        // 回退: A 的全精度表示
        return A.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static double TruncateDoubleMantissa(double value, int keepFractionBits)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value == 0d)
            return value;

        if (keepFractionBits >= 52)
            return value;
        if (keepFractionBits < 0)
            throw new ArgumentOutOfRangeException(nameof(keepFractionBits), "keepFractionBits 必须在 0..52 范围");

        ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
        const ulong signAndExpMask = 0xFFF0000000000000UL;
        const ulong mantissaMask = 0x000FFFFFFFFFFFFFUL;

        ulong signAndExp = bits & signAndExpMask;
        ulong mantissa = bits & mantissaMask;

        int dropBits = 52 - keepFractionBits;
        ulong clearMask = ~((1UL << dropBits) - 1UL);
        ulong truncatedMantissa = mantissa & clearMask;

        ulong truncatedBits = signAndExp | truncatedMantissa;
        return BitConverter.Int64BitsToDouble((long)truncatedBits);
    }

    private static string? StringListToString(ListVar? list)
    {
        if (list == null || list.v.Count == 0)
            return null;

        StringBuilder sb = new();
        for (int i = 0; i < list.v.Count; i++)
        {
            if (list.v[i] is not StringVar item)
                throw new InvalidOperationException("ListVar 仅支持 StringVar 用于 HTML 属性输出");

            if (i > 0)
                sb.Append(',');
            sb.Append(item.v);
        }

        return sb.ToString();
    }

    private static ListVar ParseStringList(string value)
    {
        ListVar list = new();
        foreach (string rawPart in value.Split(','))
        {
#pragma warning disable TK010 // 系统边界：CSV 拆分后各元素可能带空格，Trim 是标准解析行为
            string part = rawPart.Trim();
#pragma warning restore TK010
            list.v.Add(new StringVar { v = part });
        }

        return list;
    }

    #endregion

    #region Node ↔ HTML（核心方法，以 CanvasNode 为中心）

    /// <summary>
    /// HTML → CanvasNode（解析 HTML 为 Node 树，不进行二进制转换）
    /// 架构：HTML → CanvasNode
    /// </summary>
    public static (CanvasNode root, string canvasName) HtmlToNode(string html)
    {
        XElement htmlRoot = ExtractCanvasRoot(html);
        return ConvertHtmlToNodeTree(htmlRoot);
    }

    /// <summary>
    /// CanvasNode → HTML 独立版（完整文档，带外壳）
    /// 架构：CanvasNode → HTML
    /// </summary>
    public static string NodeToHtml(CanvasNode root, string canvasName, string? supportPathPrefix = null, string? canvasRelativePath = null)
    {
        return ToHtmlFromNodeResult(root, canvasName, supportPathPrefix, canvasRelativePath);
    }

    /// <summary>
    /// CanvasNode → HTML 内联版（仅 BeCanvas 标签，无文档外壳）
    /// 架构：CanvasNode → HTML（与独立版 BeCanvas 内容字节级完全一致）
    /// 原则：内联版 = 独立版中 &lt;body&gt; 内的 BeCanvas 部分，完全一样
    /// </summary>
    public static string NodeToHtmlInline(CanvasNode root, string canvasName)
    {
        StringBuilder sb = new();

        // 内联版：完全复用独立版的 BeCanvas 输出逻辑
        // 格式与独立版中 <body> 内的 BeCanvas 字节级完全一致
        AppendIndent(sb, CanvasHtmlViewConstants.IndentRoot);
        sb.Append('<').Append(CanvasHtmlViewConstants.RootTag);

        // name 属性（始终输出，与二进制解析一致）
        sb.Append(' ').Append(CanvasHtmlViewConstants.RootAttrName).Append("=\"").Append(Escape(canvasName)).Append('"');

        // width/height 固定默认值
        sb.Append(' ').Append(CanvasHtmlViewConstants.RootAttrWidth).Append("=\"").Append(CanvasHtmlViewConstants.DefaultRootWidth).Append('"');
        sb.Append(' ').Append(CanvasHtmlViewConstants.RootAttrHeight).Append("=\"").Append(CanvasHtmlViewConstants.DefaultRootHeight).Append('"');

        // 处理子节点（复用独立版的 WriteHtmlNode，缩进级别完全一致）
        if (root.Children.Count == 0)
        {
            sb.Append("></").Append(CanvasHtmlViewConstants.RootTag).Append(">\n");
        }
        else
        {
            sb.Append(">\n");
            foreach (CanvasNode child in root.Children)
            {
                WriteHtmlNode(sb, child, CanvasHtmlViewConstants.IndentChild);
            }
            AppendIndent(sb, CanvasHtmlViewConstants.IndentRoot);
            sb.Append("</").Append(CanvasHtmlViewConstants.RootTag).Append(">\n");
        }

        return sb.ToString();
    }

    #endregion

    #region HTML → 二进制（内部方法，供 CanvasConverter 使用）

    /// <summary>
    /// 独立Canvas文件序列化（输出完整的Canvas文件格式）
    /// 内部方法，请使用 CanvasConverter.HtmlToBinary
    /// </summary>
    internal static byte[] FromHtml(string html) => FromHtmlWithName(html).bytes;

    /// <summary>
    /// 独立Canvas文件序列化（输出完整的Canvas文件格式，返回canvas名称）
    /// 格式：[0x02 0x76 0x31] [canvas名称 7-bit编码] [ListVar子节点列表]
    /// 内部方法，请使用 CanvasConverter.FromHtml + CanvasConverter.ToBinary
    /// </summary>
    internal static (byte[] bytes, string canvasName) FromHtmlWithName(string html)
    {
        // HTML → CanvasNode → VarBase → 二进制
        // 架构：对称转换，原样传递，无"自动修复"
        XElement htmlRoot = ExtractCanvasRoot(html);
        (CanvasNode rootNode, string canvasName) = ConvertHtmlToNodeTree(htmlRoot);

        // CanvasNode → 完整Canvas二进制格式
        byte[] bytes = CanvasPreview.ConvertToBinaryStatic(rootNode, canvasName);

        return (bytes, canvasName);
    }

    /// <summary>
    /// 内联Canvas序列化（用于脚本中的create.canvas调用）
    /// 内部方法，请使用 CanvasConverter.FromHtmlInline + CanvasConverter.ToInlineBase64
    ///
    /// 格式区别：
    /// - 独立文件：[0x02 0x76 0x31] [canvas名称] [ListVar子节点列表]
    /// - 内联模式：直接 [ListVar子节点列表]（无文件头，无canvas名称）
    /// </summary>
    internal static (byte[] bytes, string canvasName) FromHtmlInline(string html)
    {
        XElement htmlRoot = ExtractCanvasRoot(html);
        (CanvasNode rootNode, string canvasName) = ConvertHtmlToNodeTree(htmlRoot);

        byte[] bytes = CanvasPreview.ConvertToInlineBinaryStatic(rootNode);

        return (bytes, canvasName);
    }

    /// <summary>
    /// HTML 元素 → CanvasNode 树
    /// 架构：HTML → CanvasNode
    /// </summary>
    private static (CanvasNode root, string canvasName) ConvertHtmlToNodeTree(XElement htmlRoot)
    {
        string tagName = htmlRoot.Name.LocalName;
        CanvasHtmlTag tag = CanvasHtmlTagSchema.FindTag(tagName)
            ?? throw new InvalidOperationException($"HTML视图不支持标签: {tagName}");

        if (!string.Equals(tag.TagName, CanvasHtmlViewConstants.RootTag, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HTML根元素必须为Canvas");
        }

        // 使用有序列表保持 HTML 属性顺序，保证 roundtrip 字节级一致性
        List<(string field, string value)> fieldValues = [];
        HashSet<string> presentHtmlNames = new(StringComparer.Ordinal);
        HashSet<string> seenFields = new(StringComparer.Ordinal);
        string canvasName = string.Empty;
        string? widthValue = null;
        string? heightValue = null;

        foreach (XAttribute attr in htmlRoot.Attributes())
        {
            CanvasHtmlAttribute schemaAttr = CanvasHtmlTagSchema.FindAttribute(tagName, attr.Name.LocalName)
                ?? throw new InvalidOperationException($"HTML视图存在未知属性: {tagName}.{attr.Name.LocalName}。{NodeSchemaHint}");

            presentHtmlNames.Add(schemaAttr.HtmlName);

            if (schemaAttr.Kind == CanvasHtmlAttributeKind.Meta)
            {
                if (string.Equals(schemaAttr.HtmlName, CanvasHtmlViewConstants.RootAttrWidth, StringComparison.Ordinal))
                {
                    widthValue = attr.Value;
                }
                else if (string.Equals(schemaAttr.HtmlName, CanvasHtmlViewConstants.RootAttrHeight, StringComparison.Ordinal))
                {
                    heightValue = attr.Value;
                }
                continue;
            }

            if (schemaAttr.Kind == CanvasHtmlAttributeKind.Name)
            {
                canvasName = DecodeChineseQuoteEscapes(attr.Value);
                // name 属性不作为 PresentField（与二进制解析一致）
                continue;
            }

            string decoded = DecodeChineseQuoteEscapes(attr.Value);
            if (seenFields.Add(schemaAttr.CanvasField))
            {
                fieldValues.Add((schemaAttr.CanvasField, decoded));
            }
        }

        ValidateRequiredAttributes(tag, presentHtmlNames);
        if (widthValue != null)
            ParseRootDimension(widthValue, "width");
        if (heightValue != null)
            ParseRootDimension(heightValue, "height");

        UICanvasNode root = new();
        // 【Canvas 根节点特殊处理】
        // Canvas 根节点的 name 来自文件头，不在子节点的 DictVar 中
        // Canvas 名称保存在文件头
        root.name = new StringVar { v = canvasName };

        // 【Canvas 根节点默认值】
        // 这些值是运行时默认值，不参与序列化（原版 Canvas 根节点不序列化这些字段）
        // 见 CanvasPreview.ParseBytes line 131-136 的对称处理
        // 注意：这些字段不标记为 PresentField，因为它们不出现在二进制中
        root.size = new Vector3Var { X = 1, Y = 1, Z = 0 };
        root.Pos = new Vector3Var { X = 0.5f, Y = 0.5f, Z = 0 };
        root.parentAnchor = new LongVar { v = 4 };
        root.anchor = new LongVar { v = 4 };
        root.widthExtend = new LongVar { v = 1 };
        root.heightExtend = new LongVar { v = 1 };

        // width/height 是 Meta 属性，仅用于 HTML 文档显示，不影响实际布局

        // 从 HTML 字段值填充到 CanvasNode（原样传递）
        ApplyFieldsFromHtml(root, fieldValues);

        foreach (XElement childElem in htmlRoot.Elements())
        {
            CanvasNode childNode = ConvertHtmlElementToNode(childElem);
            root.Children.Add(childNode);
        }

        return (root, canvasName);
    }

    /// <summary>
    /// HTML 元素 → CanvasNode
    /// 架构：HTML → CanvasNode
    /// </summary>
    private static CanvasNode ConvertHtmlElementToNode(XElement elem)
    {
        string tagName = elem.Name.LocalName;
        CanvasHtmlTag tag = CanvasHtmlTagSchema.FindTag(tagName)
            ?? throw new InvalidOperationException($"HTML视图不支持标签: {tagName}");

        if (string.Equals(tag.TagName, CanvasHtmlViewConstants.RootTag, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HTML视图禁止嵌套Canvas");
        }

        // 使用有序列表保持 HTML 属性顺序，保证 roundtrip 字节级一致性
        List<(string field, string value)> fieldValues = [];
        HashSet<string> presentHtmlNames = new(StringComparer.Ordinal);
        HashSet<string> seenFields = new(StringComparer.Ordinal);

        foreach (XAttribute attr in elem.Attributes())
        {
            CanvasHtmlAttribute schemaAttr = CanvasHtmlTagSchema.FindAttribute(tag.TagName, attr.Name.LocalName)
                ?? throw new InvalidOperationException($"HTML视图存在未知属性: {tag.TagName}.{attr.Name.LocalName}。{NodeSchemaHint}");

            if (schemaAttr.Kind == CanvasHtmlAttributeKind.Meta)
            {
                throw new InvalidOperationException($"HTML视图不允许元属性出现在子节点: {tag.TagName}.{schemaAttr.HtmlName}");
            }

            presentHtmlNames.Add(schemaAttr.HtmlName);

            // 避免重复字段
            if (seenFields.Add(schemaAttr.CanvasField))
            {
                fieldValues.Add((schemaAttr.CanvasField, DecodeChineseQuoteEscapes(attr.Value)));
            }
        }

        ValidateRequiredAttributes(tag, presentHtmlNames);

        CanvasNode node = CanvasNodeSchema.CreateNode(tag.CanvasType);

        // 标记 type 为 PresentField（原版始终导出 type）
        node.MarkFieldPresent("type");

        // 从 HTML 字段值填充到 CanvasNode（保持原始顺序）
        ApplyFieldsFromHtml(node, fieldValues);

        foreach (XElement child in elem.Elements())
        {
            CanvasNode childNode = ConvertHtmlElementToNode(child);
            node.Children.Add(childNode);
        }

        // 如果有子节点，标记 sons 为 PresentField（与二进制解析一致）
        if (node.Children.Count > 0)
        {
            node.MarkFieldPresent("sons");
        }

        return node;
    }

    /// <summary>
    /// 从 HTML 字段值填充到 CanvasNode
    /// 架构：HTML → CanvasNode（反序列化，原样传递）
    /// 原则：只做纯净转换，不做验证，验证统一在 Node 层面完成
    /// </summary>
    private static void ApplyFieldsFromHtml(CanvasNode node, List<(string field, string value)> fieldValues)
    {
        // 只做字段转换和设置，验证统一在 CanvasLayoutValidator / Lint 中完成
        foreach ((string field, string value) in fieldValues)
        {
            VarBase converted = ConvertHtmlValueToVar(node, field, value);
            node.MarkFieldPresent(field);
            PropertyInfo prop = node.GetType().GetProperty(field, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"节点类型 '{node.Type}' 缺少属性 '{field}'");
            prop.SetValue(node, converted);
        }
    }

    private static void ValidateRequiredAttributes(CanvasHtmlTag tag, HashSet<string> presentHtmlNames)
    {
        // 注：字段组合与必填约束统一在 CanvasLayoutValidator / Lint 中完成
        // 此处只检查 Meta 属性（如 BeCanvas 的 width/height）
        foreach (CanvasHtmlAttribute attr in tag.Attributes)
        {
            if (!attr.Required)
                continue;

            // 只检查 Meta 属性，Canvas 字段的验证统一在 Node 层面完成
            if (attr.Kind == CanvasHtmlAttributeKind.Meta)
            {
                if (!presentHtmlNames.Contains(attr.HtmlName))
                {
                    throw new InvalidOperationException($"HTML视图缺少必要属性: {tag.TagName}.{attr.HtmlName}。{NodeSchemaHint}");
                }
            }
        }
    }

    private static double ParseRootDimension(string rawValue, string fieldName)
    {
        if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            throw new InvalidOperationException($"Canvas.{fieldName} 必须为数值： {rawValue}");
        return value;
    }

    private static float ParseFloat(string value, string fieldName)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
        {
            throw new InvalidOperationException($"字段 {fieldName} 需要数值: {value}");
        }
        if (!float.IsFinite(result))
        {
            throw new InvalidOperationException($"字段 {fieldName} 必须为有限数值: {value}");
        }

        return result;
    }

    private static Vector3Var ParseVector3(string value, string fieldName)
    {
        string[] parts = value.Split(',');
        if (parts.Length != 3)
        {
            throw new InvalidOperationException($"字段 {fieldName} 需要向量格式 x,y,z: {value}");
        }

        return new Vector3Var
        {
            X = ParseFloat(parts[0], $"{fieldName}.x"),
            Y = ParseFloat(parts[1], $"{fieldName}.y"),
            Z = ParseFloat(parts[2], $"{fieldName}.z")
        };
    }

    /// <summary>
    /// 解析 HTML 颜色属性值
    ///
    /// 【颜色格式规范 - 唯二格式，严格区分】
    /// 1. rgba(r,g,b,a) - RGB 颜色值，范围 0-255（支持小数），使用 double 精度保证字节级无损
    /// 2. palette(name) - 调色板名称，引用 ColorPalette 中的预定义颜色
    ///
    /// 禁止其他格式！禁止其他格式！禁止其他格式！
    /// </summary>
    private static ColorVar ParseColor(string value, string fieldName)
    {
        // palette(name) 格式 - 调色板名称
        if (value.StartsWith("palette(", StringComparison.Ordinal) && value.EndsWith(")"))
        {
#pragma warning disable TK010 // 系统边界：从 HTML palette(name) 中提取名称时去除可能的前后空格
            string paletteName = value.Substring(8, value.Length - 9).Trim();
#pragma warning restore TK010
            return new ColorVar { PaletteName = paletteName };
        }

        // rgba(r,g,b,a) 格式 - 0-255 范围，double 精度保证字节级无损
        if (value.StartsWith("rgba(", StringComparison.Ordinal) && value.EndsWith(")"))
        {
            string inner = value.Substring(5, value.Length - 6);
            string[] parts = inner.Split(',');
            if (parts.Length == 4)
            {
                double r = double.Parse(parts[0], CultureInfo.InvariantCulture);
                double g = double.Parse(parts[1], CultureInfo.InvariantCulture);
                double b = double.Parse(parts[2], CultureInfo.InvariantCulture);
                double a = double.Parse(parts[3], CultureInfo.InvariantCulture);

                // 对齐 TS: RGB>=0，A=0..255
                if (r < 0 || g < 0 || b < 0 || a < 0 || a > 255)
                {
                    throw new InvalidOperationException($"字段 {fieldName} 的 rgba 分量范围必须是 RGB>=0 且 A=0..255: {value}");
                }

                return new ColorVar
                {
                    R = (float)(r / 255.0),
                    G = (float)(g / 255.0),
                    B = (float)(b / 255.0),
                    A = (float)(a / 255.0)
                };
            }
            throw new InvalidOperationException($"字段 {fieldName} 的 rgba 格式需要 4 个分量: {value}");
        }

        // 非法格式
        throw new InvalidOperationException($"字段 {fieldName} 颜色格式错误，仅支持 rgba(r,g,b,a) 或 palette(name): {value}");
    }

    #endregion

    #region HTML解析辅助

    public static string ExtractCanvasRootFragment(string html)
    {
        return ExtractCanvasRoot(html).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement ExtractCanvasRoot(string html)
    {
        // Canvas "HTML" 是受控 XML DSL；浏览器只消费生成的页面，不能把 HTML 容错与大小写折叠带入读写语义。
        try
        {
            using StringReader stringReader = new(html);
            using XmlReader reader = XmlReader.Create(stringReader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            XElement documentRoot = document.Root ?? throw new InvalidOperationException("Canvas XML缺少根元素");
            List<XElement> roots = documentRoot.DescendantsAndSelf(CanvasHtmlViewConstants.RootTag).ToList();
            if (roots.Count != 1)
                throw new InvalidOperationException(roots.Count == 0 ? "HTML缺少Canvas" : "HTML视图只允许包含1个Canvas");

            return roots[0];
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"Canvas XML解析失败: {ex.Message}", ex);
        }
    }

    #endregion

    #region 工具方法

    private const string NodeSchemaFileName = "CanvasTypesNodes.ts";
    private const string NodeSchemaHint = "请查看 HTML 视图头部的节点Schema定义位置（temp/toolkit/canvas_render/CanvasTypesNodes.ts）。";

    private static readonly IReadOnlyList<(string NodeType, string Marker)> NodeSchemaMarkers =
        new List<(string, string)>
        {
            // 标记不含尾随空格：CanvasTypesNodes.ts 经 prettier 统一格式后类声明行为尾（无尾空格），
            // 带尾空格会精确匹配失败导致 cctor 抛异常（e010cbe1 引入的回归，此处恢复）
            ("CanvasNodeModel", "export abstract class CanvasNodeModel"),
            ("UIRect", "export class UIRectNodeClass"),
            ("UIButton", "export class UIButtonNodeClass"),
            ("UILabel", "export class UILabelNodeClass"),
            ("TextPro", "export class TextProNodeClass"),
            ("UIInput", "export class UIInputNodeClass"),
            ("UIRawImage", "export class UIRawImageNodeClass"),
            ("UISpine", "export class UISpineNodeClass"),
            ("ScrollRect", "export class ScrollRectNodeClass")
        };

    /// <summary>
    /// 节点Schema在 canvas_render TS support 中的定义位置。
    /// 行号直接从实际 support 文件内容解析，禁止手写漂移表。
    /// </summary>
    private static readonly IReadOnlyList<(string NodeType, int LineNumber)> NodeSchemaLocations;

    static CanvasHtmlViewCodec()
    {
        string resourceName = "Toolkit.Core.canvas.render." + NodeSchemaFileName;
        Assembly assembly = typeof(CanvasHtmlViewCodec).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Canvas render TS support资源不存在: {resourceName}");
        }

        using StreamReader reader = new(stream);
        string sourceText = reader.ReadToEnd();

        List<(string NodeType, int LineNumber)> locations = new(NodeSchemaMarkers.Count);
        foreach ((string nodeType, string marker) in NodeSchemaMarkers)
        {
            using StringReader lineReader = new(sourceText);
            int lineNumber = 1;
            bool found = false;
            while (lineReader.ReadLine() is string line)
            {
                if (line.Contains(marker, StringComparison.Ordinal))
                {
                    locations.Add((nodeType, lineNumber));
                    found = true;
                    break;
                }

                lineNumber++;
            }

            if (!found)
            {
                throw new InvalidOperationException($"Canvas render TS support中未找到标记: {marker}");
            }
        }

        NodeSchemaLocations = locations.AsReadOnly();
    }

    // 【注意】脚本放在 body 底部，确保 DOM 就绪后再执行，避免异步等待
    private static string _cachedSupportPathPrefix = string.Empty;

    private static void AppendDocumentHead(StringBuilder sb, string? supportPathPrefix, string? canvasRelativePath = null)
    {
        string prefix = string.Empty;
        string nodeSchemaPath;
        if (supportPathPrefix != null)
        {
            if (supportPathPrefix.EndsWith("/", StringComparison.Ordinal))
                prefix = supportPathPrefix;
            else
                prefix = supportPathPrefix + "/";
            nodeSchemaPath = prefix + NodeSchemaFileName;
        }
        else
        {
            nodeSchemaPath = RelativePathUtils.Combine(ScriptPathHelper.CanvasRenderRootPath, NodeSchemaFileName);
        }
        _cachedSupportPathPrefix = prefix;

        // 生成节点Schema定义位置注释
        sb.Append("<!--\n");
        sb.Append("  节点Schema定义位置 (canvas_render):\n");
        foreach ((string nodeType, int lineNumber) in NodeSchemaLocations)
        {
            sb.Append($"    - {nodeType}: {nodeSchemaPath}:{lineNumber}\n");
        }

        // 添加 PNG 预览图路径注释
        if (!string.IsNullOrWhiteSpace(canvasRelativePath))
        {
            string withoutExt = canvasRelativePath[..^ProjectEntityLayout.CanvasExt.Length];
            string pngPath = $"{ScriptPathHelper.ViewsPath}/{withoutExt}.png";
            sb.Append("\n  本HTML视图的预览图为:\n");
            sb.Append($"    {pngPath}\n");
        }

        sb.Append("-->\n");

        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"").Append(CanvasHtmlViewConstants.HtmlLang).Append("\">\n");
        AppendIndent(sb, CanvasHtmlViewConstants.IndentHeadBody);
        sb.Append("<head>\n");
        AppendIndent(sb, CanvasHtmlViewConstants.IndentHeadContent);
        sb.Append("<meta charset=\"UTF-8\" />\n");
        AppendIndent(sb, CanvasHtmlViewConstants.IndentHeadContent);
        sb.Append("<link rel=\"stylesheet\" href=\"").Append(prefix).Append(CanvasHtmlViewAssets.CssFileName).Append("\" />\n");
        AppendIndent(sb, CanvasHtmlViewConstants.IndentHeadBody);
        sb.Append("</head>\n");
        AppendIndent(sb, CanvasHtmlViewConstants.IndentHeadBody);
        sb.Append("<body>\n");
    }

    private static void AppendDocumentTail(StringBuilder sb)
    {
        string prefix = _cachedSupportPathPrefix;
        // 脚本放在 body 底部，确保 DOM 就绪后再执行。
        // 顺序要求：索引脚本（palette/guid-map）先完成，再执行引擎脚本（必须最后）。
        AppendIndent(sb, CanvasHtmlViewConstants.IndentHeadContent);
        sb.Append("<script src=\"").Append(prefix).Append(CanvasHtmlViewAssets.PaletteFileName).Append("\"></script>\n");
        AppendIndent(sb, CanvasHtmlViewConstants.IndentHeadContent);
        sb.Append("<script src=\"").Append(prefix).Append(CanvasHtmlViewAssets.StandaloneGuidMapFileName).Append("\"></script>\n");
        AppendIndent(sb, CanvasHtmlViewConstants.IndentHeadContent);
        sb.Append("<script src=\"").Append(prefix).Append(CanvasHtmlViewAssets.ScriptFileName).Append("\"></script>\n");
        AppendIndent(sb, CanvasHtmlViewConstants.IndentHeadBody);
        sb.Append("</body>\n");
        sb.Append("</html>\n");
    }

    private static void AppendIndent(StringBuilder sb, int indent)
    {
        for (int i = 0; i < indent * CanvasHtmlViewConstants.SpacesPerIndent; i++)
        {
            sb.Append(' ');
        }
    }

    private static string Escape(string value)
    {
        return SecurityElement.Escape(value
            .Replace("“", "\\u201c", StringComparison.Ordinal)
            .Replace("”", "\\u201d", StringComparison.Ordinal));
    }

    private static string DecodeChineseQuoteEscapes(string value)
    {
        return value
            .Replace("\\u201c", "“", StringComparison.Ordinal)
            .Replace("\\u201d", "”", StringComparison.Ordinal)
            .Replace("\\u201C", "“", StringComparison.Ordinal)
            .Replace("\\u201D", "”", StringComparison.Ordinal);
    }

    #endregion
}
