using System.Collections.ObjectModel;

namespace Toolkit.Core.Canvas;

/// <summary>
/// Canvas HTML 视图标签与字段映射（受控子集）
///
/// 【映射规则】
/// - HTML 属性名通过 CanvasFieldNameMapping 从 Node 字段名获取
/// - CanvasFieldNameMapping 是唯一的权威映射来源
/// - 本文件只定义每个标签支持哪些 Node 字段，不重复定义映射
///
/// 【HTML 属性命名规范】
/// - HTML 属性名必须是全小写
/// - 来源：docs/Canvas_Fields_Spec.md 第六节 "Node 字段与 HTML 属性映射表"
///
/// 约束：
/// - 标签名统一使用 Be 前缀的 DSL 标签（BeCanvas/BeUIRect/...），与 TS 视图运行时类型对齐；
///   CanvasType 保持引擎类型名（Canvas/UIRect/...），二者解耦
/// - 仅包含 CanvasNodeSchema 已允许的字段，禁止私自扩展
/// </summary>
public enum CanvasHtmlAttributeKind
{
    Name,
    CanvasField,
    Meta
}

public sealed record CanvasHtmlAttribute(
    string HtmlName,
    string CanvasField,
    CanvasHtmlAttributeKind Kind,
    IReadOnlyList<VarType> AllowedTypes,
    bool Required);

public sealed record CanvasHtmlTag(
    string TagName,
    string CanvasType,
    IReadOnlyList<CanvasHtmlAttribute> Attributes);

public static class CanvasHtmlTagSchema
{
    private static readonly ReadOnlyDictionary<string, CanvasHtmlTag> Tags = BuildTags();
    private static readonly ReadOnlyDictionary<string, CanvasHtmlTag> TagsByCanvasType = new ReadOnlyDictionary<string, CanvasHtmlTag>(
        Tags.Values.ToDictionary(static t => t.CanvasType, StringComparer.Ordinal));

    public static CanvasHtmlTag? FindTag(string tagName) =>
        Tags.ContainsKey(tagName) ? Tags[tagName] : null;

    public static CanvasHtmlTag? FindTagByCanvasType(string canvasType) =>
        TagsByCanvasType.ContainsKey(canvasType) ? TagsByCanvasType[canvasType] : null;

    public static CanvasHtmlAttribute? FindAttribute(string tagName, string htmlName)
    {
        CanvasHtmlTag? tag = FindTag(tagName);
        if (tag == null)
            return null;

        foreach (CanvasHtmlAttribute a in tag.Attributes)
        {
            if (string.Equals(a.HtmlName, htmlName, StringComparison.Ordinal))
                return a;
        }

        return null;
    }

    private static ReadOnlyDictionary<string, CanvasHtmlTag> BuildTags()
    {
        CanvasHtmlTag[] tags = new[]
        {
            BuildCanvasTag(),
            BuildRectTag(),
            BuildButtonTag(),
            BuildLabelTag(),
            BuildTextProTag(),
            BuildInputTag(),
            BuildRawImageTag(),
            BuildSpineTag(),
            BuildScrollRectTag()
        };

        return new ReadOnlyDictionary<string, CanvasHtmlTag>(
            tags.ToDictionary(static t => t.TagName, StringComparer.Ordinal));
    }

    private static CanvasHtmlTag BuildCanvasTag()
    {
        return new CanvasHtmlTag(
            "BeCanvas",
            "Canvas",
            new List<CanvasHtmlAttribute>
            {
                Name(required: false),
                Meta("width", required: true),
                Meta("height", required: true)
            });
    }

    private static CanvasHtmlTag BuildRectTag()
    {
        return new CanvasHtmlTag(
            "BeUIRect",
            "UIRect",
            Append(LayoutCommon("UIRect"),
                new[]
                {
                    Field("UIRect", "BorderWidth"),
                    Field("UIRect", "Radius"),
                    Field("UIRect", "fillMethod"),
                    Field("UIRect", "fillAmount"),
                    Field("UIRect", "mask"),
                    Field("UIRect", "slicedPicGuid"),
                    Field("UIRect", "slicedBorderLB"),
                    Field("UIRect", "slicedBorderRT"),
                    Field("UIRect", "slicedRectLB"),
                    Field("UIRect", "slicedRectRT"),
                    Field("UIRect", "cellSize"),
                    Field("UIRect", "spacing"),
                    Field("UIRect", "verticle_spacing"),
                    Field("UIRect", "horizontal_spacing"),
                    Field("UIRect", "childExpendWidth"),
                    Field("UIRect", "childExpendHeight"),
                    Field("UIRect", "childAlignment"),
                    Field("UIRect", "padding")
                }));
    }

    private static CanvasHtmlTag BuildButtonTag()
    {
        List<CanvasHtmlAttribute> attrs = LayoutCommon("UIButton");
        Append(attrs,
            new[]
            {
                Field("UIButton", "BorderWidth"),
                Field("UIButton", "Radius"),
                Field("UIButton", "slicedPicGuid"),
                Field("UIButton", "slicedBorderLB"),
                Field("UIButton", "slicedBorderRT"),
                Field("UIButton", "slicedRectLB"),
                Field("UIButton", "slicedRectRT")
            });
        Append(attrs, TextCommon("UIButton"));
        Append(attrs,
            new[]
            {
                Field("UIButton", "clickCBName"),
                Field("UIButton", "pressCBName"),
                Field("UIButton", "releaseCBName")
            });

        return new CanvasHtmlTag(
            "BeUIButton",
            "UIButton",
            attrs);
    }

    private static CanvasHtmlTag BuildLabelTag()
    {
        return new CanvasHtmlTag(
            "BeUILabel",
            "UILabel",
            Append(Append(LayoutCommon("UILabel"), TextCommon("UILabel")),
                new[]
                {
                    Field("UILabel", "outlineWidth"),
                    Field("UILabel", "boolBestFit")
                }));
    }

    private static CanvasHtmlTag BuildTextProTag()
    {
        return new CanvasHtmlTag(
            "BeUILabelTextPro",
            "TextPro",
            Append(Append(LayoutCommon("TextPro"), TextCommon("TextPro")),
                new[]
                {
                    Field("TextPro", "boolBestFit"),
                    Field("TextPro", "outlineWidth"),
                    Field("TextPro", "FaceDilate"),
                    Field("TextPro", "bUNDERLAY"),
                    Field("TextPro", "UnderlayColor"),
                    Field("TextPro", "UnderlayOffset"),
                    Field("TextPro", "UnderlaySoftness"),
                    Field("TextPro", "bGLOW"),
                    Field("TextPro", "GlowColor"),
                    Field("TextPro", "GlowOffset"),
                    Field("TextPro", "GlowInner"),
                    Field("TextPro", "GlowOuter"),
                    Field("TextPro", "GlowPower"),
                    Field("TextPro", "enableVertexGradient"),
                    Field("TextPro", "bBEVEL"),
                    Field("TextPro", "SpecularColor"),
                    Field("TextPro", "LightAngle"),
                    Field("TextPro", "SpecularPower"),
                    Field("TextPro", "Reflectivity"),
                    Field("TextPro", "Diffuse"),
                    Field("TextPro", "Ambient"),
                    Field("TextPro", "Bevel"),
                    Field("TextPro", "BevelOffset"),
                    Field("TextPro", "BevelWidth"),
                    Field("TextPro", "BevelRoundness"),
                    Field("TextPro", "BevelClamp"),
                    Field("TextPro", "topLeft"),
                    Field("TextPro", "topRight"),
                    Field("TextPro", "bottomLeft"),
                    Field("TextPro", "bottomRight")
                }));
    }

    private static CanvasHtmlTag BuildInputTag()
    {
        List<CanvasHtmlAttribute> attrs = LayoutCommon("UIInput");
        Append(attrs,
            new[]
            {
                Field("UIInput", "text"),
                Field("UIInput", "FontSize"),
                Field("UIInput", "Alignment")
            });
        Append(attrs,
            new[]
            {
                Field("UIInput", "contentType"),
                Field("UIInput", "multiLine"),
                Field("UIInput", "labelColor")
            });

        return new CanvasHtmlTag(
            "BeUIInput",
            "UIInput",
            attrs);
    }

    private static CanvasHtmlTag BuildRawImageTag()
    {
        return new CanvasHtmlTag(
            "BeUIRawImage",
            "UIRawImage",
            Append(LayoutCommon("UIRawImage"),
                new[]
                {
                    Field("UIRawImage", "fileGuid"),
                    Field("UIRawImage", "softMaskGuid")
                }));
    }

    private static CanvasHtmlTag BuildSpineTag()
    {
        return new CanvasHtmlTag(
            "BeUISpine",
            "UISpine",
            Append(LayoutCommon("UISpine"),
                new[]
                {
                    Field("UISpine", "atlasGuid"),
                    Field("UISpine", "skelGuid"),
                    Field("UISpine", "jsonGuid"),
                    Field("UISpine", "pngGuids"),
                    Field("UISpine", "animIndex")
                }));
    }

    private static CanvasHtmlTag BuildScrollRectTag()
    {
        return new CanvasHtmlTag(
            "BeUIRectScrollRect",
            "ScrollRect",
            Append(LayoutCommon("ScrollRect"),
                new[]
                {
                    Field("ScrollRect", "scrollHorizontal"),
                    Field("ScrollRect", "scrollVertical"),
                    Field("ScrollRect", "BorderWidth"),
                    Field("ScrollRect", "Radius"),
                    // fillMethod/fillAmount 仅 UIRect 支持，ScrollRect 不支持
                    Field("ScrollRect", "mask"),
                    Field("ScrollRect", "slicedPicGuid"),
                    Field("ScrollRect", "slicedBorderLB"),
                    Field("ScrollRect", "slicedBorderRT"),
                    Field("ScrollRect", "slicedRectLB"),
                    Field("ScrollRect", "slicedRectRT"),
                    Field("ScrollRect", "cellSize"),
                    Field("ScrollRect", "spacing"),
                    Field("ScrollRect", "verticle_spacing"),
                    Field("ScrollRect", "horizontal_spacing"),
                    Field("ScrollRect", "childExpendWidth"),
                    Field("ScrollRect", "childExpendHeight"),
                    Field("ScrollRect", "childAlignment"),
                    Field("ScrollRect", "padding")
                }));
    }

    private static List<CanvasHtmlAttribute> LayoutCommon(string canvasType)
    {
        List<CanvasHtmlAttribute> list =
        [
            Field(canvasType, "name"),
            Field(canvasType, "guid"),
            Field(canvasType, "Pos"),
            // 历史兼容：旧版 pos 字段
            Field(canvasType, "pos"),
            Field(canvasType, "size"),
            Field(canvasType, "parentAnchor"),
            Field(canvasType, "anchor"),
            Field(canvasType, "widthExtend"),
            Field(canvasType, "heightExtend"),
            Field(canvasType, "rot"),
            Field(canvasType, "color")
        ];

        TryAdd(list, canvasType, "shadowColor");
        TryAdd(list, canvasType, "shadewDistance");
        TryAdd(list, canvasType, "outlineColor");
        TryAdd(list, canvasType, "outlineDistance");

        return list;
    }

    /// <summary>
    /// 文本通用属性
    /// </summary>
    private static IReadOnlyList<CanvasHtmlAttribute> TextCommon(string canvasType)
    {
        return new List<CanvasHtmlAttribute>
        {
            canvasType switch
            {
                "UIButton" => new CanvasHtmlAttribute("text", "label", CanvasHtmlAttributeKind.CanvasField,
                    CanvasNodeSchema.GetAllowedTypes(canvasType, "label")
                    ?? throw new InvalidOperationException($"字段未在 CanvasNodeSchema 中声明: {canvasType}.label"), false),
                "UILabel" or "TextPro" or "UIInput" => Field(canvasType, "text"),
                _ => throw new InvalidOperationException($"文本字段映射仅支持 UIButton/UILabel/TextPro/UIInput: {canvasType}")
            },
            Field(canvasType, "FontSize"),
            Field(canvasType, "Alignment"),
            Field(canvasType, "lineSpacing"),
            Field(canvasType, "lightFont"),
            Field(canvasType, "characterSpacing"),
            Field(canvasType, "fontStyle")
        };
    }

#pragma warning disable TK006 // Schema 构建辅助：合并属性列表，支持链式调用
    private static List<CanvasHtmlAttribute> Append(List<CanvasHtmlAttribute> source, IEnumerable<CanvasHtmlAttribute> extras)
    {
        source.AddRange(extras);
        return source;
    }
#pragma warning restore TK006

    private static CanvasHtmlAttribute Name(bool required = false) =>
        new("name", "name", CanvasHtmlAttributeKind.Name, new[] { VarType.String }, required);

    private static CanvasHtmlAttribute Meta(string htmlName, bool required = false) =>
        new(htmlName, string.Empty, CanvasHtmlAttributeKind.Meta, Array.Empty<VarType>(), required);

    /// <summary>
    /// 创建字段属性 - HTML 属性名从 CanvasFieldNameMapping 自动获取
    /// </summary>
    private static CanvasHtmlAttribute Field(string canvasType, string canvasField, bool required = false)
    {
        VarType[]? types = CanvasNodeSchema.GetAllowedTypes(canvasType, canvasField);
        if (types == null || types.Length == 0)
        {
            throw new InvalidOperationException($"字段未在 CanvasNodeSchema 中声明: {canvasType}.{canvasField}");
        }

        // 通过 CanvasFieldNameMapping 获取 HTML 属性名
        string? htmlName = CanvasFieldNameMapping.NodeToHtml(canvasField);
        if (htmlName == null)
        {
            throw new InvalidOperationException($"字段未在 CanvasFieldNameMapping 中声明: {canvasField}");
        }

        return new CanvasHtmlAttribute(htmlName, canvasField, CanvasHtmlAttributeKind.CanvasField, types, required);
    }

    /// <summary>
    /// 尝试添加字段属性（如果字段在该类型中允许）
    /// </summary>
    private static void TryAdd(ICollection<CanvasHtmlAttribute> list, string canvasType, string canvasField, bool required = false)
    {
        VarType[]? types = CanvasNodeSchema.GetAllowedTypes(canvasType, canvasField);
        if (types == null || types.Length == 0)
            return;

        string? htmlName = CanvasFieldNameMapping.NodeToHtml(canvasField);
        if (htmlName == null)
            return;

        list.Add(new CanvasHtmlAttribute(htmlName, canvasField, CanvasHtmlAttributeKind.CanvasField, types, required));
    }
}
