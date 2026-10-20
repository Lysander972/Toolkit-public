namespace Toolkit.Core.Canvas;

/// <summary>
/// Canvas 四向转换统一入口
///
/// 架构原则：
/// - 以 CanvasNode 为中心，四个方向均可无损互转
/// - 独立文件和内联版本的 Node 生成完全相同（文件头是适配层的事）
/// - 明确区分 4 个方向，无"贴心"特判
/// - 转换层绝对纯净：只做格式互转，不做业务校验、不做 fallback
///
/// 【硬约束：边界与职责】
/// - CanvasConverter/Codec 属于内部转换层，必须保持无条件转换
/// - 校验只允许放在外部边界操作：写操作入口与渲染入口
/// - 禁止把校验、防御、自动修复逻辑堆进转换层
///
/// 四向转换图：
///                     独立文件二进制
///                          ↕
///     内联 Base64  ←→  CanvasNode  ←→  HTML 独立版
///                          ↕
///                     HTML 内联版
///
/// 【唯一入口】所有 Canvas 转换必须通过本类进行！
/// </summary>
public static class CanvasConverter
{
    #region 独立文件二进制 ↔ CanvasNode

    /// <summary>
    /// 独立文件二进制 → CanvasNode
    /// 格式：[0x02 0x76 0x31][canvas名称][ListVar子节点列表]
    /// </summary>
    public static CanvasNodeResult FromBinary(byte[] data)
    {
        return CanvasPreview.ParseBytesStatic(data);
    }

    /// <summary>
    /// CanvasNode → 独立文件二进制
    /// 格式：[0x02 0x76 0x31][canvas名称][ListVar子节点列表]
    /// </summary>
    public static byte[] ToBinary(CanvasNode root, string canvasName)
    {
        return CanvasPreview.ConvertToBinaryStatic(root, canvasName);
    }

    #endregion

    #region 内联 Base64 ↔ CanvasNode

    /// <summary>
    /// 任意内联 Canvas → CanvasNode
    /// 格式：旧版 canvas:名称=Base64(ListVar) 或新版 ==hjson:&lt;name&gt;: 块
    /// </summary>
    public static CanvasNodeResult FromInline(string canvasArg)
    {
        return CanvasPreview.ParseInlineStatic(canvasArg);
    }

    /// <summary>
    /// CanvasNode → 内联 Base64
    /// 格式：canvas:名称=Base64(ListVar子节点列表)
    /// 输出可直接用于 Bescript 中的 canvas: 参数
    /// </summary>
    public static string ToInlineBase64(CanvasNode root, string canvasName)
    {
        byte[] bytes = CanvasPreview.ConvertToInlineBinaryStatic(root);
        return CanvasInlineArg.BuildLegacyBase64RawText(canvasName, bytes);
    }

    /// <summary>
    /// CanvasNode → 新版内联 Node Hjson
    /// 格式：==hjson:&lt;canvasName&gt;:&lt;content&gt;==
    /// </summary>
    public static string ToInlineNodeHjson(CanvasNode root, string canvasName)
    {
        string hjson = CanvasInlineNodeHjsonCodec.Serialize(root);
        return CanvasInlineArg.BuildNodeHjsonRawText(canvasName, hjson);
    }

    #endregion

    #region HTML 独立版 ↔ CanvasNode

    /// <summary>
    /// HTML 独立版 → CanvasNode
    /// 格式：完整 HTML 文档，包含 &lt;BeCanvas&gt; 标签
    /// 注意：仅做转换，不做校验
    /// </summary>
    public static (CanvasNode root, string canvasName) FromHtml(string html)
    {
        return CanvasHtmlViewCodec.HtmlToNode(html);
    }

    /// <summary>
    /// CanvasNode → HTML 独立版
    /// 格式：完整 HTML 文档，包含注释和静态资源引用外壳
    /// </summary>
    public static string ToHtml(CanvasNode root, string canvasName, string? supportPathPrefix = null)
    {
        return CanvasHtmlViewCodec.NodeToHtml(root, canvasName, supportPathPrefix);
    }

    #endregion

    #region HTML 内联版 ↔ CanvasNode

    /// <summary>
    /// HTML 内联版 → CanvasNode
    /// 格式：仅 &lt;BeCanvas&gt; 标签，无文档外壳
    /// 注意：仅做转换，不做校验
    /// </summary>
    public static (CanvasNode root, string canvasName) FromHtmlInline(string html)
    {
        return CanvasHtmlViewCodec.HtmlToNode(html);
    }

    /// <summary>
    /// CanvasNode → HTML 内联版
    /// 格式：仅 &lt;BeCanvas&gt; 标签，无文档外壳
    /// 用于脚本内联或需要简洁输出的场景
    /// </summary>
    public static string ToHtmlInline(CanvasNode root, string canvasName)
    {
        return CanvasHtmlViewCodec.NodeToHtmlInline(root, canvasName);
    }

    #endregion

    #region 便捷方法：跨格式直接转换

    /// <summary>
    /// 独立文件二进制 → HTML 独立版
    /// </summary>
    /// <param name="data">Canvas 二进制数据</param>
    /// <param name="supportPathPrefix">静态资源前缀路径</param>
    /// <param name="canvasRelativePath">Canvas 文件相对路径（用于生成预览图路径注释）</param>
    public static string BinaryToHtml(byte[] data, string? supportPathPrefix = null, string? canvasRelativePath = null)
    {
        CanvasNodeResult result = FromBinary(data);
        if (!result.Ok || result.Root == null)
        {
            throw new InvalidOperationException($"Canvas 解析失败: {result.Diagnostics[0].Message}");
        }
        return CanvasHtmlViewCodec.NodeToHtml(result.Root, result.CanvasName, supportPathPrefix, canvasRelativePath);
    }

    /// <summary>
    /// HTML 独立版 → 独立文件二进制
    /// </summary>
    public static byte[] HtmlToBinary(string html)
    {
        (CanvasNode root, string canvasName) = FromHtml(html);
        return ToBinary(root, canvasName);
    }

    /// <summary>
    /// 内联 Base64 → HTML 内联版
    /// </summary>
    public static string InlineBase64ToHtmlInline(string canvasArg)
    {
        CanvasNodeResult result = FromInline(canvasArg);
        if (!result.Ok)
        {
            throw new InvalidOperationException($"Canvas 解析失败: {result.Diagnostics[0].Message}");
        }
        return ToHtmlInline(result.Root ?? throw new InvalidOperationException("Canvas 解析结果 Root 为 null"), result.CanvasName);
    }

    /// <summary>
    /// HTML 内联版 → 内联 Base64
    /// </summary>
    public static string HtmlInlineToInlineBase64(string html)
    {
        (CanvasNode root, string canvasName) = FromHtmlInline(html);
        return ToInlineBase64(root, canvasName);
    }

    #endregion
}
