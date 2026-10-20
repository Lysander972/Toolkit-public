using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Toolkit.Core.Canvas;

/// <summary>
/// Canvas 节点统一校验器
/// - 字段存在性/组合合法性/过时字段
/// - 运行时基础数值合法性
/// - 富文本格式合法性
/// 布局是否真正可渲染由 JS 预渲染校验负责，这里不再维护重复的 C# 布局镜像。
/// </summary>
internal static class CanvasLayoutValidator
{
    private static readonly HashSet<string> TextProForbiddenFields = new(StringComparer.Ordinal)
    {
        "shadowColor",
        "shadewDistance"
    };

#pragma warning disable TK025 // 原版 Unity 富文本 DSL 无可用外部解析库，唯一校验入口
    private static readonly Regex RichTextTagRegex = new(@"<(color|size)=([^>]+)>|</(color|size)>", RegexOptions.Compiled);
    private static readonly Regex RichTextHex6 = new(@"^[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly Regex RichTextHex8 = new(@"^[0-9a-fA-F]{8}$", RegexOptions.Compiled);
    private static readonly Regex JsParseFloatPrefixRegex = new(
        @"^\s*([+-]?(?:Infinity|(?:(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)))",
        RegexOptions.Compiled);
#pragma warning restore TK025

    public static List<string> ValidateTree(CanvasNode root)
    {
        List<string> errors = [];

        ValidateNodeRecursive(root, errors, "root");
        if (errors.Count > 0)
            return errors;

        // 对齐 TS: Canvas 没有任何子组件时直接报错
        if (root.Children.Count == 0)
        {
            errors.Add("root: Canvas 没有任何子组件，无法渲染预览");
            return errors;
        }

        // 阶段2.1：模拟 TS 数值解析规则，校验值合法性
        ValidateRuntimeValuesRecursive(root, errors, "root");
        if (errors.Count > 0)
            return errors;

        // 阶段2.2：模拟 TS 富文本解析规则（color/size 标签）
        ValidateRichTextRecursive(root, errors, "root");
        if (errors.Count > 0)
            return errors;

        return errors;
    }

    internal static List<string> ValidateNode(CanvasNode node)
    {
        string uiType = node.Type;
        List<string> errors = [];
        string[] fieldOrder = node.GetPresentFieldOrder().ToArray();
        HashSet<string> fieldNames = new(fieldOrder, StringComparer.Ordinal);

        if (fieldOrder.Length == 0 || fieldOrder[0] != "type")
            errors.Add("字段顺序错误：type 必须是第一个字段");
        if (fieldNames.Contains("sons") && fieldOrder[^1] != "sons")
            errors.Add("字段顺序错误：sons 必须是最后一个字段");

        foreach (string field in fieldNames)
        {
            if (!CanvasNodeSchema.IsFieldAllowed(uiType, field))
                errors.Add($"字段 '{GetHtmlFieldName(field, uiType)}' 不允许用于 UI 类型 '{uiType}'");
        }

        ValidateEffectFieldPairs(fieldNames, errors);
        ValidateSlicedQuad(fieldNames, errors);
        bool hasFillMethod = fieldNames.Contains("fillMethod");
        bool hasFillAmount = fieldNames.Contains("fillAmount");
        if (hasFillMethod && !hasFillAmount)
            errors.Add("字段组合非法：存在 fillmethod 时必须同时存在 fillamount");
        if (!hasFillMethod && hasFillAmount)
            errors.Add("字段组合非法：存在 fillamount 时必须同时存在 fillmethod");
        ValidateLayoutFieldShape(uiType, fieldNames, errors);
        ValidateRequiredFields(uiType, fieldNames, errors);

        if (uiType == "TextPro")
        {
            foreach (string field in TextProForbiddenFields)
            {
                if (fieldNames.Contains(field))
                    errors.Add($"字段 '{GetHtmlFieldName(field, uiType)}' 不允许用于 UI 类型 '{uiType}'");
            }
        }

        if (fieldNames.Contains("pos"))
            errors.Add("字段 'legacypos' 已过时：请使用 pos，旧字段只能作为既有错误保留，不允许继续写新内容");

        return errors;
    }

    private static void ValidateNodeRecursive(CanvasNode node, List<string> errors, string nodePath)
    {
        if (node is not UICanvasNode)
        {
            List<string> nodeErrors = ValidateNode(node);
            foreach (string nodeError in nodeErrors)
                errors.Add($"{nodePath}: {nodeError}");
        }

        for (int i = 0; i < node.Children.Count; i++)
            ValidateNodeRecursive(node.Children[i], errors, BuildChildNodePath(nodePath, node.Children[i], i));
    }

    private static string GetHtmlFieldName(string canvasField, string uiType)
    {
        if (canvasField == "label" && uiType == "UIButton")
            return "label(HTML视图中显示为text)";
        string? htmlName = CanvasFieldNameMapping.NodeToHtml(canvasField);
        if (htmlName == null)
            throw new InvalidOperationException($"字段未在 CanvasFieldNameMapping 中声明: {canvasField}");
        return htmlName;
    }

    private static void ValidateEffectFieldPairs(HashSet<string> fieldNames, List<string> errors)
    {
        bool hasShadowColor = fieldNames.Contains("shadowColor");
        bool hasShadowDistance = fieldNames.Contains("shadewDistance");
        if (hasShadowColor && !hasShadowDistance)
            errors.Add("字段组合非法：存在 shadowcolor 时必须同时存在 shadowdistance");
        if (!hasShadowColor && hasShadowDistance)
            errors.Add("字段组合非法：存在 shadowdistance 时必须同时存在 shadowcolor");

        bool hasOutlineColor = fieldNames.Contains("outlineColor");
        bool hasOutlineDistance = fieldNames.Contains("outlineDistance");
        if (hasOutlineColor && !hasOutlineDistance)
            errors.Add("字段组合非法：存在 outlinecolor 时必须同时存在 outlinedistance");
        if (!hasOutlineColor && hasOutlineDistance)
            errors.Add("字段组合非法：存在 outlinedistance 时必须同时存在 outlinecolor");
    }

    private static void ValidateSlicedQuad(HashSet<string> fieldNames, List<string> errors)
    {
        // 九宫格五件套共存：slicedPicGuid 与 slicedBorderLB/RT、slicedRectLB/RT
        // 关联字段必须完整，缺少依赖字段的布局无法加载。
        // 逐个提示缺失字段，指明“存在 X 时必须同时存在 Y”。
        (string Canvas, string Html)[] slicedFields =
        {
            ("slicedPicGuid", "slicedpicguid"),
            ("slicedBorderLB", "slicedborderlb"),
            ("slicedBorderRT", "slicedborderrt"),
            ("slicedRectLB", "slicedrectlb"),
            ("slicedRectRT", "slicedrectrt"),
        };

        string? anchorHtml = null;
        List<string> missingHtml = new();
        foreach ((string canvas, string html) in slicedFields)
        {
            if (fieldNames.Contains(canvas))
            {
                anchorHtml ??= html;
            }
            else
            {
                missingHtml.Add(html);
            }
        }

        if (anchorHtml == null || missingHtml.Count == 0)
        {
            return;
        }

        foreach (string missing in missingHtml)
        {
            errors.Add($"字段组合非法：存在 {anchorHtml} 时必须同时存在 {missing}");
        }
    }

    private static void ValidateLayoutFieldShape(string uiType, HashSet<string> fieldNames, List<string> errors)
    {
        if (uiType != "UIRect" && uiType != "ScrollRect")
            return;

        bool hasCellSize = fieldNames.Contains("cellSize");
        bool hasSpacing = fieldNames.Contains("spacing");
        bool hasVertical = fieldNames.Contains("verticle_spacing");
        bool hasHorizontal = fieldNames.Contains("horizontal_spacing");
        bool hasPadding = fieldNames.Contains("padding");
        bool hasExpandWidth = fieldNames.Contains("childExpendWidth");
        bool hasExpandHeight = fieldNames.Contains("childExpendHeight");
        bool hasChildAlignment = fieldNames.Contains("childAlignment");

        int layoutModeCount = 0;
        if (hasCellSize) layoutModeCount++;
        if (hasVertical) layoutModeCount++;
        if (hasHorizontal) layoutModeCount++;
        if (layoutModeCount > 1)
            errors.Add("字段组合非法：cellsize、verticalspacing、horizontalspacing 三者互斥");

        if (hasCellSize && !hasSpacing)
            errors.Add("字段组合非法：Grid 布局存在 cellsize 时必须同时存在 layoutspacing");
        if (!hasCellSize && hasSpacing)
            errors.Add("字段组合非法：存在 layoutspacing 时必须同时存在 cellsize");

        bool hasLayoutMode = hasCellSize || hasVertical || hasHorizontal;
        if (hasLayoutMode && !hasPadding)
            errors.Add("字段组合非法：存在布局模式字段时必须同时存在 layoutpadding");
        if (!hasLayoutMode && hasPadding)
            errors.Add("字段组合非法：存在 layoutpadding 时必须同时存在一种布局模式字段（cellsize/verticalspacing/horizontalspacing）");

        if ((hasExpandWidth || hasExpandHeight) && !(hasVertical || hasHorizontal))
            errors.Add("字段组合非法：childexpandwidth/childexpandheight 仅允许用于 Vertical/Horizontal 布局");
        if (hasChildAlignment && !hasLayoutMode)
            errors.Add("字段组合非法：存在 childalignment 时必须同时存在一种布局模式字段（cellsize/verticalspacing/horizontalspacing）");
    }

    private static void ValidateRequiredFields(string uiType, HashSet<string> fieldNames, List<string> errors)
    {
        if (!fieldNames.Contains("name"))
            errors.Add("字段缺失：name 为必需字段");
        if (!fieldNames.Contains("size"))
            errors.Add("字段缺失：size 为必需字段");
        if (!fieldNames.Contains("color"))
            errors.Add("字段缺失：color 为必需字段");

        bool hasPos = fieldNames.Contains("Pos");
        bool hasLegacyPos = fieldNames.Contains("pos");
        if (!hasPos && !hasLegacyPos)
            errors.Add("字段缺失：pos 为必需字段");

        if (uiType is "UIRect" or "UIButton" or "ScrollRect")
        {
            if (!fieldNames.Contains("BorderWidth"))
                errors.Add("字段缺失：borderwidth 为必需字段");
            if (!fieldNames.Contains("Radius"))
                errors.Add("字段缺失：radius 为必需字段");
        }

        if (uiType == "UIButton" && !fieldNames.Contains("label"))
            errors.Add("字段缺失：label(HTML视图中显示为text)为必需字段。二进制中UIButton使用label，其他文本组件使用text，若混淆会导致此错误");

        if ((uiType is "UILabel" or "TextPro" or "UIInput") && !fieldNames.Contains("text"))
            errors.Add("字段缺失：text 为必需字段");
    }

#pragma warning disable TK006 // 递归方法，不可内联
    private static void ValidateRuntimeValuesRecursive(CanvasNode node, List<string> errors, string nodePath)
#pragma warning restore TK006
    {
        if (node is not UICanvasNode)
        {
            ValidateNodeVarValues(node, errors, nodePath);
        }

        for (int i = 0; i < node.Children.Count; i++)
            ValidateRuntimeValuesRecursive(node.Children[i], errors, BuildChildNodePath(nodePath, node.Children[i], i));
    }

    private static void ValidateNodeVarValues(CanvasNode node, List<string> errors, string nodePath)
    {
        PropertyInfo[] props = node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (PropertyInfo prop in props)
        {
            object? value = prop.GetValue(node);
            if (value == null)
                continue;

            if (value is FloatVar f)
            {
                if (!float.IsFinite(f.v))
                    errors.Add($"{nodePath}: {prop.Name} 非法数值: {f.v}");
                continue;
            }

            if (value is Vector3Var v3)
            {
                if (!float.IsFinite(v3.X) || !float.IsFinite(v3.Y) || !float.IsFinite(v3.Z))
                    errors.Add($"{nodePath}: {prop.Name} 含非法数值: ({v3.X},{v3.Y},{v3.Z})");
                continue;
            }

            if (value is ColorVar color)
            {
                if (!string.IsNullOrEmpty(color.PaletteName))
                    continue;

                if (!float.IsFinite(color.R) || !float.IsFinite(color.G) || !float.IsFinite(color.B) || !float.IsFinite(color.A))
                {
                    errors.Add($"{nodePath}: {prop.Name} 含非法颜色数值");
                    continue;
                }

                // 对齐 TS: RGB >= 0，A ∈ [0,255]（此处为 0..1 归一化）
                if (color.R < 0f || color.G < 0f || color.B < 0f || color.A < 0f || color.A > 1f)
                    errors.Add($"{nodePath}: {prop.Name} 颜色分量范围非法");
            }
        }
    }

    private static void ValidateRichTextRecursive(CanvasNode node, List<string> errors, string nodePath)
    {
        if (node is UIButtonNode button)
        {
            string? text = button.label?.v;
            ValidateRichTextContent(text, $"{nodePath}: UIButton.text", errors);
        }
        else if (node is UILabelNode label)
        {
            ValidateRichTextContent(label.text?.v, $"{nodePath}: UILabel.text", errors);
        }
        else if (node is UITextProNode textPro)
        {
            ValidateRichTextContent(textPro.text?.v, $"{nodePath}: TextPro.text", errors);
        }

        for (int i = 0; i < node.Children.Count; i++)
            ValidateRichTextRecursive(node.Children[i], errors, BuildChildNodePath(nodePath, node.Children[i], i));
    }

#pragma warning disable TK006 // 独立职责：节点路径构建，含空名降级逻辑
    private static string BuildChildNodePath(string parentPath, CanvasNode child, int index)
#pragma warning restore TK006
    {
        string nodeName = child.name.v;
#pragma warning disable TK011 // Canvas 节点可以没有 name，空名时用类型路径定位
        if (nodeName == string.Empty)
#pragma warning restore TK011
            return $"{parentPath}/{child.Type}[{index}]";
        return $"{parentPath}/{child.Type}[{index}]({nodeName})";
    }

    private static void ValidateRichTextContent(string? text, string hint, List<string> errors)
    {
        if (string.IsNullOrEmpty(text))
            return;

        MatchCollection matches = RichTextTagRegex.Matches(text);
        foreach (Match match in matches)
        {
            string openTag = match.Groups[1].Value;
#pragma warning disable TK011 // 正则区分开标签(<color=xxx>)和闭标签(</color>)，闭标签时 group 1 为空
            if (openTag == string.Empty)
#pragma warning restore TK011
                continue;

            string tagValue = match.Groups[2].Value;
            if (openTag == "size")
            {
                if (ParseJsFloatLike(tagValue) == null)
                    errors.Add($"{hint}: 富文本 size 非法: {tagValue}");
                continue;
            }

            if (openTag == "color")
            {
#pragma warning disable TK010 // 富文本颜色值是用户编写的外部数据，在解析边界处需处理可能的空白
                string colorText = tagValue.Trim();
#pragma warning restore TK010
                if (!colorText.StartsWith("#", StringComparison.Ordinal))
                {
                    errors.Add($"{hint}: 富文本颜色格式错误，只支持十六进制 #RRGGBB 或 #RRGGBBAA: {tagValue}");
                    continue;
                }

                string hex = colorText[1..];
                if (!RichTextHex6.IsMatch(hex) && !RichTextHex8.IsMatch(hex))
                    errors.Add($"{hint}: 富文本颜色十六进制格式错误: {tagValue}");
            }
        }
    }

    private static double? ParseJsFloatLike(string text)
    {
        Match m = JsParseFloatPrefixRegex.Match(text);
        if (!m.Success)
            return null;

        if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsNaN(value))
            return null;

        return value;
    }
}
