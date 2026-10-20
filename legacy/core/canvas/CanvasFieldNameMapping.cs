// Canvas 字段名双向映射
//
// 【权威映射规则】
// - Node 字段名：与 Canvas 二进制格式完全一致
// - HTML 属性名：全小写，修正历史拼写错误（如 shadew → shadow）
//
// 【双向映射】
// - NodeToHtml(nodeFieldName) → htmlAttrName
// - HtmlToNode(htmlAttrName) → nodeFieldName
//
// 【数据来源】
// docs/Canvas_Fields_Spec.md 第六节 "Node 字段与 HTML 属性映射表"

namespace Toolkit.Core.Canvas;

/// <summary>
/// Canvas 字段名双向映射（Node ↔ HTML）
/// 权威映射规则，禁止在其他地方散落映射逻辑
/// </summary>
public static class CanvasFieldNameMapping
{
    // Node 字段名 → HTML 属性名（全小写）
    private static readonly Dictionary<string, string> s_nodeToHtml = new(StringComparer.Ordinal)
    {
        // 基础属性
        ["name"] = "name",
        ["guid"] = "guid",
        ["Pos"] = "pos",
        ["pos"] = "legacypos",        // 历史原版SDK兼容,旧字段映射，仅用于读取旧内容,在Lint当中故意地制造了错误促使清理此值避免其出现
        ["size"] = "size",
        ["rot"] = "rot",
        ["parentAnchor"] = "parentanchor",
        ["anchor"] = "anchor",
        ["widthExtend"] = "widthextend",
        ["heightExtend"] = "heightextend",

        // 视觉
        ["color"] = "color",
        ["BorderWidth"] = "borderwidth",
        ["Radius"] = "radius",
        ["mask"] = "mask",

        // 阴影描边
        ["shadowColor"] = "shadowcolor",
        ["shadewDistance"] = "shadowdistance",  // 原版拼写错误保留
        ["outlineColor"] = "outlinecolor",
        ["outlineDistance"] = "outlinedistance",
        ["outlineWidth"] = "outlinewidth",

        // 文本
        ["text"] = "text",
        ["label"] = "label",          // UIButton 专用
        ["labelColor"] = "textcolor",
        ["FontSize"] = "fontsize",
        ["Alignment"] = "alignment",
        ["lineSpacing"] = "linespacing",
        ["lightFont"] = "lightfont",
        ["boolBestFit"] = "boolbestfit",
        ["characterSpacing"] = "characterspacing",
        ["fontStyle"] = "fontstyle",

        // 填充
        ["fillMethod"] = "fillmethod",
        ["fillAmount"] = "fillamount",

        // 排版
        ["cellSize"] = "cellsize",
        ["spacing"] = "layoutspacing",
        ["verticle_spacing"] = "verticalspacing",  // 原版拼写错误保留
        ["horizontal_spacing"] = "horizontalspacing",
        ["padding"] = "layoutpadding",
        ["childAlignment"] = "childalignment",
        ["childExpendWidth"] = "childexpandwidth",
        ["childExpendHeight"] = "childexpandheight",

        // 图片
        ["slicedPicGuid"] = "slicedpicguid",
        ["slicedBorderLB"] = "slicedborderlb",
        ["slicedBorderRT"] = "slicedborderrt",
        ["slicedRectLB"] = "slicedrectlb",
        ["slicedRectRT"] = "slicedrectrt",
        ["fileGuid"] = "fileguid",
        ["softMaskGuid"] = "softmaskguid",
        ["atlasGuid"] = "atlasguid",
        ["skelGuid"] = "skelguid",
        ["jsonGuid"] = "jsonguid",
        ["pngGuids"] = "pngguids",
        ["animIndex"] = "animindex",

        // 按钮回调
        ["clickCBName"] = "clickcbname",
        ["pressCBName"] = "presscbname",
        ["releaseCBName"] = "releasecbname",

        // 输入
        ["multiLine"] = "multiline",
        ["contentType"] = "contenttype",

        // 滚动
        ["scrollHorizontal"] = "scrollhorizontaldisabled",
        ["scrollVertical"] = "scrollverticaldisabled",

        // TextPro 特效
        ["FaceDilate"] = "facedilate",
        ["bUNDERLAY"] = "underlayenabled",
        ["UnderlayColor"] = "underlaycolor",
        ["UnderlayOffset"] = "underlayoffset",
        ["UnderlaySoftness"] = "underlaysoftness",
        ["bGLOW"] = "glowenabled",
        ["GlowColor"] = "glowcolor",
        ["GlowOffset"] = "glowoffset",
        ["GlowInner"] = "glowinner",
        ["GlowOuter"] = "glowouter",
        ["GlowPower"] = "glowpower",
        ["enableVertexGradient"] = "vertexgradientenabled",
        ["topLeft"] = "gradienttopleft",
        ["topRight"] = "gradienttopright",
        ["bottomLeft"] = "gradientbottomleft",
        ["bottomRight"] = "gradientbottomright",
        ["bBEVEL"] = "bevelenabled",
        ["SpecularColor"] = "specularcolor",
        ["LightAngle"] = "lightangle",
        ["SpecularPower"] = "specularpower",
        ["Reflectivity"] = "reflectivity",
        ["Diffuse"] = "diffuse",
        ["Ambient"] = "ambient",
        ["Bevel"] = "bevel",
        ["BevelOffset"] = "beveloffset",
        ["BevelWidth"] = "bevelwidth",
        ["BevelRoundness"] = "bevelroundness",
        ["BevelClamp"] = "bevelclamp",
    };

    // HTML 属性名 → Node 字段名（反向映射，构建时生成）
    private static readonly Dictionary<string, string> s_htmlToNode = new(StringComparer.Ordinal);

    static CanvasFieldNameMapping()
    {
        // 构建反向映射
        foreach ((string? nodeField, string? htmlAttr) in s_nodeToHtml)
        {
            s_htmlToNode[htmlAttr] = nodeField;
        }
    }

    /// <summary>
    /// Node 字段名 → HTML 属性名
    /// </summary>
    /// <returns>HTML 属性名（全小写），如果字段无映射则返回 null</returns>
    public static string? NodeToHtml(string nodeFieldName)
    {
        return s_nodeToHtml.ContainsKey(nodeFieldName) ? s_nodeToHtml[nodeFieldName] : null;
    }

    /// <summary>
    /// HTML 属性名 → Node 字段名
    /// </summary>
    /// <returns>Node 字段名，如果属性无映射则返回 null</returns>
    public static string? HtmlToNode(string htmlAttrName)
    {
        return s_htmlToNode.ContainsKey(htmlAttrName) ? s_htmlToNode[htmlAttrName] : null;
    }
}
