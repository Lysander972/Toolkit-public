// Canvas 节点类型定义
//
// 【核心原则】
// 字段直接使用 Vars.cs 中的类型（Vector3Var、ColorVar 等），无中间转换
// 字段直接平铺在节点类上，与原版二进制格式完全一致
// 字段平铺存储，不按特性分组嵌套。
// 禁止任何多余的组件分组抽象！
//
// 【字段名规范】
// 字段名必须与 Canvas 持久化字段名完全一致

#nullable enable

using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Canvas.Render;
using Toolkit.Core.Infra;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;
using Toolkit.Core.Refactor;
using Toolkit.Core.Sdk;
using Toolkit.Core.Search;
using Toolkit.Core.TsView;
using Toolkit.Core.Worker;

namespace Toolkit.Core.Canvas;

#region 基础类型

public sealed class CanvasNodeResult
{
    public bool Ok { get; set; }
    public string CanvasName { get; set; } = string.Empty;
    public CanvasNode? Root { get; set; }
    public List<ToolError> Diagnostics { get; } = new();
}

public interface ICanvasNode
{
    string Type { get; }
    StringVar name { get; set; }
    LongVar? guid { get; set; }
    List<CanvasNode> Children { get; }
}

/// <summary>
/// Canvas 节点基类
/// 字段直接定义在各类上，与原版平铺格式一致
/// </summary>
public abstract class CanvasNode : ICanvasNode
{
    public abstract string Type { get; }
    public StringVar name { get; set; } = new() { v = string.Empty };
    public LongVar? guid { get; set; }
    public List<CanvasNode> Children { get; } = new();

    // ========== 所有节点共有字段 ==========
    // 布局基础（所有节点都有）
    public Vector3Var? Pos { get; set; }              // 行 402
    public Vector3Var? pos { get; set; }              // 历史格式
    public Vector3Var? size { get; set; }             // 行 445
    public FloatVar? rot { get; set; }                   // 行 448
    public LongVar? parentAnchor { get; set; }           // 行 405
    public LongVar? anchor { get; set; }                 // 行 410
    public LongVar? widthExtend { get; set; }            // 行 427
    public LongVar? heightExtend { get; set; }           // 行 442

    // 存在的字段名集合
    private HashSet<string> PresentFields { get; } = new(StringComparer.Ordinal);
    private List<string> PresentFieldOrder { get; } = new();

    internal void MarkFieldPresent(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName)) return;
        if (PresentFields.Add(fieldName))
            PresentFieldOrder.Add(fieldName);
    }

    internal bool IsFieldPresent(string fieldName) => PresentFields.Contains(fieldName);
    internal IReadOnlyList<string> GetPresentFieldOrder() => PresentFieldOrder;
    internal IReadOnlySet<string> GetPresentFields() => new HashSet<string>(PresentFields, StringComparer.Ordinal);
}

#endregion

#region 节点类型定义

/// <summary>
/// UIRect 节点
/// </summary>
public sealed class UIRectNode : CanvasNode
{
    public override string Type => "UIRect";

    // 通用阴影描边（BEUI 基类，行 455-463）
    public ColorVar? shadowColor { get; set; }
    public Vector3Var? shadewDistance { get; set; }   // 原版拼写
    public ColorVar? outlineColor { get; set; }
    public Vector3Var? outlineDistance { get; set; }

    // 视觉（行 451, 473-474, 561）
    public ColorVar? color { get; set; }
    public FloatVar? BorderWidth { get; set; }
    public FloatVar? Radius { get; set; }
    public BoolVar? mask { get; set; }

    // 填充（行 477-478，仅 UIRect）
    public LongVar? fillMethod { get; set; }
    public FloatVar? fillAmount { get; set; }

    // 排版（行 490-542）
    public Vector3Var? cellSize { get; set; }
    public Vector3Var? spacing { get; set; }
    public FloatVar? verticle_spacing { get; set; }     // 原版拼写
    public FloatVar? horizontal_spacing { get; set; }
    public BoolVar? childExpendWidth { get; set; }
    public BoolVar? childExpendHeight { get; set; }
    public LongVar? childAlignment { get; set; }
    public Vector3Var? padding { get; set; }

    // 九宫格图片（行 738-743）
    public StringVar? slicedPicGuid { get; set; }
    public Vector3Var? slicedBorderLB { get; set; }
    public Vector3Var? slicedBorderRT { get; set; }
    public Vector3Var? slicedRectLB { get; set; }
    public Vector3Var? slicedRectRT { get; set; }
}

/// <summary>
/// UIButton 节点
/// </summary>
public sealed class UIButtonNode : CanvasNode
{
    public override string Type => "UIButton";

    // 通用阴影描边
    public ColorVar? shadowColor { get; set; }
    public Vector3Var? shadewDistance { get; set; }
    public ColorVar? outlineColor { get; set; }
    public Vector3Var? outlineDistance { get; set; }

    // 视觉
    public ColorVar? color { get; set; }
    public FloatVar? BorderWidth { get; set; }
    public FloatVar? Radius { get; set; }

    // 文本（UIButton 用 label，行 570）
    public StringVar? label { get; set; }
    public FloatVar? FontSize { get; set; }
    public LongVar? Alignment { get; set; }
    public FloatVar? lineSpacing { get; set; }
    public BoolVar? lightFont { get; set; }
    public FloatVar? characterSpacing { get; set; }
    public LongVar? fontStyle { get; set; }

    // 回调（行 574-584）
    public StringVar? clickCBName { get; set; }
    public StringVar? pressCBName { get; set; }
    public StringVar? releaseCBName { get; set; }

    // 九宫格图片
    public StringVar? slicedPicGuid { get; set; }
    public Vector3Var? slicedBorderLB { get; set; }
    public Vector3Var? slicedBorderRT { get; set; }
    public Vector3Var? slicedRectLB { get; set; }
    public Vector3Var? slicedRectRT { get; set; }
}

/// <summary>
/// UILabel 节点（含 TextPro）
/// </summary>
public sealed class UILabelNode : CanvasNode
{
    public override string Type => "UILabel";

    // 通用阴影描边（UILabel 通过 Effects，行 455-463）
    public ColorVar? shadowColor { get; set; }
    public Vector3Var? shadewDistance { get; set; }
    public ColorVar? outlineColor { get; set; }
    public Vector3Var? outlineDistance { get; set; }
    public FloatVar? outlineWidth { get; set; }         // 行 635 (TextPro)

    // 视觉
    public ColorVar? color { get; set; }

    // 文本（行 591, 592, 595, 602, 612, 617, 687）
    public StringVar? text { get; set; }
    public FloatVar? FontSize { get; set; }
    public LongVar? Alignment { get; set; }
    public FloatVar? lineSpacing { get; set; }
    public FloatVar? characterSpacing { get; set; }
    public LongVar? fontStyle { get; set; }
    public BoolVar? lightFont { get; set; }
    public BoolVar? boolBestFit { get; set; }

    // TextPro 特效（行 607-654）
    public FloatVar? FaceDilate { get; set; }
    public BoolVar? bUNDERLAY { get; set; }
    public ColorVar? UnderlayColor { get; set; }
    public Vector3Var? UnderlayOffset { get; set; }
    public FloatVar? UnderlaySoftness { get; set; }
    public BoolVar? bGLOW { get; set; }
    public ColorVar? GlowColor { get; set; }
    public FloatVar? GlowOffset { get; set; }
    public FloatVar? GlowInner { get; set; }
    public FloatVar? GlowOuter { get; set; }
    public FloatVar? GlowPower { get; set; }
    public BoolVar? enableVertexGradient { get; set; }
    public ColorVar? topLeft { get; set; }
    public ColorVar? topRight { get; set; }
    public ColorVar? bottomLeft { get; set; }
    public ColorVar? bottomRight { get; set; }
    public BoolVar? bBEVEL { get; set; }
    public ColorVar? SpecularColor { get; set; }
    public FloatVar? LightAngle { get; set; }
    public FloatVar? SpecularPower { get; set; }
    public FloatVar? Reflectivity { get; set; }
    public FloatVar? Diffuse { get; set; }
    public FloatVar? Ambient { get; set; }
    public FloatVar? Bevel { get; set; }
    public FloatVar? BevelOffset { get; set; }
    public FloatVar? BevelWidth { get; set; }
    public FloatVar? BevelRoundness { get; set; }
    public FloatVar? BevelClamp { get; set; }
}

/// <summary>
/// TextPro 节点（继承 UILabel 的所有文本特效字段）
/// </summary>
public sealed class UITextProNode : CanvasNode
{
    public override string Type => "TextPro";

    // 通用阴影描边
    public ColorVar? shadowColor { get; set; }
    public Vector3Var? shadewDistance { get; set; }
    public ColorVar? outlineColor { get; set; }
    public Vector3Var? outlineDistance { get; set; }
    public FloatVar? outlineWidth { get; set; }

    // 视觉
    public ColorVar? color { get; set; }

    // 文本
    public StringVar? text { get; set; }
    public FloatVar? FontSize { get; set; }
    public LongVar? Alignment { get; set; }
    public FloatVar? lineSpacing { get; set; }
    public FloatVar? characterSpacing { get; set; }
    public LongVar? fontStyle { get; set; }
    public BoolVar? lightFont { get; set; }
    public BoolVar? boolBestFit { get; set; }

    // TextPro 特效
    public FloatVar? FaceDilate { get; set; }
    public BoolVar? bUNDERLAY { get; set; }
    public ColorVar? UnderlayColor { get; set; }
    public Vector3Var? UnderlayOffset { get; set; }
    public FloatVar? UnderlaySoftness { get; set; }
    public BoolVar? bGLOW { get; set; }
    public ColorVar? GlowColor { get; set; }
    public FloatVar? GlowOffset { get; set; }
    public FloatVar? GlowInner { get; set; }
    public FloatVar? GlowOuter { get; set; }
    public FloatVar? GlowPower { get; set; }
    public BoolVar? enableVertexGradient { get; set; }
    public ColorVar? topLeft { get; set; }
    public ColorVar? topRight { get; set; }
    public ColorVar? bottomLeft { get; set; }
    public ColorVar? bottomRight { get; set; }
    public BoolVar? bBEVEL { get; set; }
    public ColorVar? SpecularColor { get; set; }
    public FloatVar? LightAngle { get; set; }
    public FloatVar? SpecularPower { get; set; }
    public FloatVar? Reflectivity { get; set; }
    public FloatVar? Diffuse { get; set; }
    public FloatVar? Ambient { get; set; }
    public FloatVar? Bevel { get; set; }
    public FloatVar? BevelOffset { get; set; }
    public FloatVar? BevelWidth { get; set; }
    public FloatVar? BevelRoundness { get; set; }
    public FloatVar? BevelClamp { get; set; }
}

/// <summary>
/// UIInput 节点
/// </summary>
public sealed class UIInputNode : CanvasNode
{
    public override string Type => "UIInput";

    // 通用阴影描边
    public ColorVar? shadowColor { get; set; }
    public Vector3Var? shadewDistance { get; set; }
    public ColorVar? outlineColor { get; set; }
    public Vector3Var? outlineDistance { get; set; }

    // 视觉
    public ColorVar? color { get; set; }
    public FloatVar? BorderWidth { get; set; }
    public FloatVar? Radius { get; set; }

    // 文本（行 695-702）
    public StringVar? text { get; set; }
    public StringVar? label { get; set; }
    public FloatVar? FontSize { get; set; }
    public LongVar? Alignment { get; set; }
    public FloatVar? lineSpacing { get; set; }
    public BoolVar? lightFont { get; set; }
    public FloatVar? characterSpacing { get; set; }
    public LongVar? fontStyle { get; set; }
    public ColorVar? labelColor { get; set; }        // 行 702

    // 输入选项（行 705-710）
    public BoolVar? multiLine { get; set; }
    public LongVar? contentType { get; set; }
}

/// <summary>
/// UIRawImage 节点
/// </summary>
public sealed class UIRawImageNode : CanvasNode
{
    public override string Type => "UIRawImage";

    // 通用阴影描边
    public ColorVar? shadowColor { get; set; }
    public Vector3Var? shadewDistance { get; set; }
    public ColorVar? outlineColor { get; set; }
    public Vector3Var? outlineDistance { get; set; }

    // 视觉
    public ColorVar? color { get; set; }

    // 图片（行 719-724）
    public StringVar? fileGuid { get; set; }
    public StringVar? softMaskGuid { get; set; }
}

/// <summary>
/// UISpine 节点
/// </summary>
public sealed class UISpineNode : CanvasNode
{
    public override string Type => "UISpine";

    // 通用阴影描边
    public ColorVar? shadowColor { get; set; }
    public Vector3Var? shadewDistance { get; set; }
    public ColorVar? outlineColor { get; set; }
    public Vector3Var? outlineDistance { get; set; }

    // 视觉
    public ColorVar? color { get; set; }

    // Spine 资源
    public StringVar? atlasGuid { get; set; }
    public StringVar? skelGuid { get; set; }
    public StringVar? jsonGuid { get; set; }
    public ListVar? pngGuids { get; set; }
    public LongVar? animIndex { get; set; }
}

/// <summary>
/// ScrollRect 节点
/// </summary>
public sealed class UIScrollRectNode : CanvasNode
{
    public override string Type => "ScrollRect";

    // 通用阴影描边
    public ColorVar? shadowColor { get; set; }
    public Vector3Var? shadewDistance { get; set; }
    public ColorVar? outlineColor { get; set; }
    public Vector3Var? outlineDistance { get; set; }

    // 视觉
    public ColorVar? color { get; set; }
    public FloatVar? BorderWidth { get; set; }
    public FloatVar? Radius { get; set; }
    public BoolVar? mask { get; set; }

    // 滚动（行 550-555）
    public BoolVar? scrollHorizontal { get; set; }
    public BoolVar? scrollVertical { get; set; }

    // 排版
    public Vector3Var? cellSize { get; set; }
    public Vector3Var? spacing { get; set; }
    public FloatVar? verticle_spacing { get; set; }
    public FloatVar? horizontal_spacing { get; set; }
    public BoolVar? childExpendWidth { get; set; }
    public BoolVar? childExpendHeight { get; set; }
    public LongVar? childAlignment { get; set; }
    public Vector3Var? padding { get; set; }

    // 九宫格图片
    public StringVar? slicedPicGuid { get; set; }
    public Vector3Var? slicedBorderLB { get; set; }
    public Vector3Var? slicedBorderRT { get; set; }
    public Vector3Var? slicedRectLB { get; set; }
    public Vector3Var? slicedRectRT { get; set; }
}

/// <summary>
/// Canvas 根节点
/// </summary>
public sealed class UICanvasNode : CanvasNode
{
    public override string Type => "Canvas";
}

#endregion
