namespace Toolkit.Core.Canvas;

/// <summary>
/// Canvas 节点类型与字段约束
///
/// Canvas 持久化字段组合规则：
/// - 所有类型共有字段：guid, Pos/pos, parentAnchor, anchor, width/heightExtend,
///   width/heightEdge, size, rot, color, shadowColor/shadewDistance, outlineColor/outlineDistance
/// - UIRect: 视觉样式(BorderWidth/Radius) + 填充(Fill) + 遮罩(Mask) + 九宫格(ImageSlice) + 布局(Layout)
/// - UIButton: 视觉样式 + 九宫格 + 按钮事件 + 文本基础字段
/// - UILabel: 文本基础字段 + outlineWidth
/// - TextPro: UILabel所有字段 + 特效字段(Underlay/Glow/Bevel/Gradient等)
/// - UIInput: 输入框字段(text/FontSize/Alignment/multiLine/contentType/labelColor)
/// - UIRawImage: 原始图片字段(fileGuid/softMaskGuid)
/// - UISpine: Spine 资源字段(atlasGuid/skelGuid/jsonGuid/pngGuids/animIndex)
/// - ScrollRect: UIRect所有字段 + 滚动字段(scrollHorizontal/scrollVertical)
///
/// 注意：UILine/UICircle/UIAvatar 是独立UI变量类型，不是Canvas布局节点类型
///
/// TextPro 的 shadowColor/shadewDistance 禁用、布局必填、字段组合等语义约束不在此处声明，
/// 统一由 Lint 负责。
/// </summary>
public static class CanvasNodeSchema
{
    private static readonly VarType[] LongType = { VarType.Long };
    private static readonly VarType[] FloatType = { VarType.Float };
    private static readonly VarType[] BoolType = { VarType.Bool };
    private static readonly VarType[] StringType = { VarType.String };
    private static readonly VarType[] ListType = { VarType.List };
    private static readonly VarType[] Vector3Type = { VarType.Vector3 };
    private static readonly VarType[] ColorType = { VarType.Color, VarType.String };

    private static readonly Dictionary<string, VarType[]> CommonProps = new(StringComparer.Ordinal)
    {
        ["guid"] = LongType,
        ["Pos"] = Vector3Type,
        // 旧字段：只用于读取现有旧内容。
        // 语义层会统一报 Lint Error，禁止继续写新内容。
        ["pos"] = Vector3Type,
        ["parentAnchor"] = LongType,
        ["anchor"] = LongType,
        ["widthExtend"] = LongType,
        ["heightExtend"] = LongType,
        ["size"] = Vector3Type,
        ["rot"] = FloatType,
        ["color"] = ColorType,
        // 所有 UI 类型共有的视觉效果字段
        ["shadowColor"] = ColorType,
        ["shadewDistance"] = Vector3Type,
        ["outlineColor"] = ColorType,
        ["outlineDistance"] = Vector3Type
    };

    // 排版属性（text/label 由各组件单独声明）
    private static readonly Dictionary<string, VarType[]> TextFormatProps = new(StringComparer.Ordinal)
    {
        ["FontSize"] = FloatType,
        ["Alignment"] = LongType,
        ["lineSpacing"] = FloatType,
        ["lightFont"] = BoolType,
        ["characterSpacing"] = FloatType,
        ["fontStyle"] = LongType
    };

    // UILabel/TextPro/UIInput 文本字段
    private static readonly Dictionary<string, VarType[]> TextCommonProps = Merge(
        new Dictionary<string, VarType[]>(StringComparer.Ordinal) { ["text"] = StringType },
        TextFormatProps
    );

    // UIButton 文本字段：原版用 label，不用 text
    private static readonly Dictionary<string, VarType[]> ButtonTextProps = Merge(
        new Dictionary<string, VarType[]>(StringComparer.Ordinal) { ["label"] = StringType },
        TextFormatProps
    );

    private static readonly Dictionary<string, VarType[]> LabelEffectProps = new(StringComparer.Ordinal)
    {
        // UILabel 特有的文本描边宽度（原版导出时若 outlineWidth>0 会同时写入 outlineDistance 和 outlineColor）
        ["outlineWidth"] = FloatType,
        ["boolBestFit"] = BoolType
    };

    private static readonly Dictionary<string, VarType[]> TextProEffectProps = new(StringComparer.Ordinal)
    {
        ["bUNDERLAY"] = BoolType,
        ["UnderlayColor"] = ColorType,
        ["UnderlayOffset"] = Vector3Type,
        ["UnderlaySoftness"] = FloatType,
        ["bGLOW"] = BoolType,
        ["GlowColor"] = ColorType,
        ["GlowOffset"] = FloatType,
        ["GlowInner"] = FloatType,
        ["GlowOuter"] = FloatType,
        ["GlowPower"] = FloatType,
        ["enableVertexGradient"] = BoolType,
        ["topLeft"] = ColorType,
        ["topRight"] = ColorType,
        ["bottomLeft"] = ColorType,
        ["bottomRight"] = ColorType,
        ["FaceDilate"] = FloatType,
        ["bBEVEL"] = BoolType,
        ["SpecularColor"] = ColorType,
        ["LightAngle"] = FloatType,
        ["SpecularPower"] = FloatType,
        ["Reflectivity"] = FloatType,
        ["Diffuse"] = FloatType,
        ["Ambient"] = FloatType,
        ["Bevel"] = FloatType,
        ["BevelOffset"] = FloatType,
        ["BevelWidth"] = FloatType,
        ["BevelRoundness"] = FloatType,
        ["BevelClamp"] = FloatType
    };

    private static readonly Dictionary<string, VarType[]> ImageSliceProps = new(StringComparer.Ordinal)
    {
        ["slicedPicGuid"] = StringType,
        ["slicedBorderLB"] = Vector3Type,
        ["slicedBorderRT"] = Vector3Type,
        ["slicedRectLB"] = Vector3Type,
        ["slicedRectRT"] = Vector3Type
    };

    private static readonly Dictionary<string, VarType[]> LayoutProps = new(StringComparer.Ordinal)
    {
        ["cellSize"] = Vector3Type,
        ["spacing"] = Vector3Type,
        ["verticle_spacing"] = FloatType,
        ["horizontal_spacing"] = FloatType,
        ["childExpendWidth"] = BoolType,
        ["childExpendHeight"] = BoolType,
        ["childAlignment"] = LongType,
        ["padding"] = Vector3Type
    };

    private static readonly Dictionary<string, VarType[]> RectVisualProps = new(StringComparer.Ordinal)
    {
        ["BorderWidth"] = FloatType,
        ["Radius"] = FloatType
    };

    private static readonly Dictionary<string, VarType[]> MaskProps = new(StringComparer.Ordinal)
    {
        ["mask"] = BoolType
    };

    private static readonly Dictionary<string, VarType[]> FillProps = new(StringComparer.Ordinal)
    {
        ["fillAmount"] = FloatType,
        ["fillMethod"] = LongType
    };

    private static readonly Dictionary<string, VarType[]> ButtonEventProps = new(StringComparer.Ordinal)
    {
        ["clickCBName"] = StringType,
        ["pressCBName"] = StringType,
        ["releaseCBName"] = StringType
    };

    private static readonly Dictionary<string, VarType[]> InputProps = new(StringComparer.Ordinal)
    {
        ["multiLine"] = BoolType,
        ["contentType"] = LongType,
        ["labelColor"] = ColorType
    };

    private static readonly Dictionary<string, VarType[]> InputTextProps = new(StringComparer.Ordinal)
    {
        ["text"] = StringType,
        ["FontSize"] = FloatType,
        ["Alignment"] = LongType
    };

    private static readonly Dictionary<string, VarType[]> RawImageProps = new(StringComparer.Ordinal)
    {
        ["fileGuid"] = StringType,
        ["softMaskGuid"] = StringType
    };

    private static readonly Dictionary<string, VarType[]> SpineProps = new(StringComparer.Ordinal)
    {
        ["atlasGuid"] = StringType,
        ["skelGuid"] = StringType,
        ["jsonGuid"] = StringType,
        ["pngGuids"] = ListType,
        ["animIndex"] = LongType
    };

    private static readonly Dictionary<string, VarType[]> ScrollProps = new(StringComparer.Ordinal)
    {
        ["scrollHorizontal"] = BoolType,
        ["scrollVertical"] = BoolType
    };

    private static readonly Dictionary<string, VarType[]> TypeProps_UIRect = Merge(RectVisualProps, MaskProps, FillProps, ImageSliceProps, LayoutProps);
    private static readonly Dictionary<string, VarType[]> TypeProps_UIButton = Merge(RectVisualProps, ImageSliceProps, ButtonEventProps, ButtonTextProps);
    private static readonly Dictionary<string, VarType[]> TypeProps_UILabel = Merge(TextCommonProps, LabelEffectProps);
    private static readonly Dictionary<string, VarType[]> TypeProps_TextPro = Merge(TextCommonProps, LabelEffectProps, TextProEffectProps);
    private static readonly Dictionary<string, VarType[]> TypeProps_UIInput = Merge(InputProps, InputTextProps);
    private static readonly Dictionary<string, VarType[]> TypeProps_UIRawImage = Merge(RawImageProps);
    private static readonly Dictionary<string, VarType[]> TypeProps_UISpine = Merge(SpineProps);
    // ScrollRect 与 UIRect 共用矩形、布局和九宫格字段，不包含填充字段
    private static readonly Dictionary<string, VarType[]> TypeProps_ScrollRect = Merge(RectVisualProps, MaskProps, ImageSliceProps, LayoutProps, ScrollProps);

    public static CanvasNode CreateNode(string type) =>
        type switch
        {
            "Canvas" => new UICanvasNode(),
            "UIRect" => new UIRectNode(),
            "UIButton" => new UIButtonNode(),
            "UILabel" => new UILabelNode(),
            "UIInput" => new UIInputNode(),
            "UIRawImage" => new UIRawImageNode(),
            "UISpine" => new UISpineNode(),
            "TextPro" => new UITextProNode(),
            "ScrollRect" => new UIScrollRectNode(),
            _ => throw new InvalidOperationException($"不支持的 UI 节点类型: {type}")
        };

    public static bool IsSupportedType(string? type) =>
        type is "Canvas" or "UIRect" or "UIButton" or "UILabel" or "UIInput" or "UIRawImage"
            or "UISpine" or "TextPro" or "ScrollRect";

    public static VarType[]? GetAllowedTypes(string uiType, string key)
    {
        if (string.Equals(key, "type", StringComparison.Ordinal) || string.Equals(key, "sons", StringComparison.Ordinal))
            return null;

        if (string.Equals(key, "name", StringComparison.Ordinal))
            return StringType;

        Dictionary<string, VarType[]>? typeProps = GetTypeProps(uiType);
        if (typeProps != null && typeProps.ContainsKey(key))
            return typeProps[key];

        if (CommonProps.ContainsKey(key))
            return CommonProps[key];

        return null;
    }

    internal static bool IsFieldAllowed(string uiType, string field)
    {
        if (field is "type" or "name" or "sons")
            return true;

        Dictionary<string, VarType[]>? typeProps = GetTypeProps(uiType);
        if (typeProps != null && typeProps.ContainsKey(field))
            return true;

        return CommonProps.ContainsKey(field);
    }

    private static Dictionary<string, VarType[]>? GetTypeProps(string uiType) =>
        uiType switch
        {
            "UIRect" => TypeProps_UIRect,
            "UIButton" => TypeProps_UIButton,
            "UILabel" => TypeProps_UILabel,
            "TextPro" => TypeProps_TextPro,
            "UIInput" => TypeProps_UIInput,
            "UIRawImage" => TypeProps_UIRawImage,
            "UISpine" => TypeProps_UISpine,
            "ScrollRect" => TypeProps_ScrollRect,
            _ => null
        };

    private static Dictionary<string, VarType[]> Merge(params Dictionary<string, VarType[]>[] sources)
    {
        Dictionary<string, VarType[]> result = new(StringComparer.Ordinal);
        foreach (Dictionary<string, VarType[]> source in sources)
        {
            foreach ((string? key, VarType[]? value) in source)
            {
                result.Add(key, value);
            }
        }

        return result;
    }
}
