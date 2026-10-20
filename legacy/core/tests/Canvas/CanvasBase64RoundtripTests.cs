using Xunit;
using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Canvas;

/// <summary>
/// Canvas Base64（经过 bes 反转义后）到 Node 的双向转换测试
///
/// 测试架构：
/// - Base64 解码 → CanvasNode（CanvasPreview.ParseInline）
/// - CanvasNode → Base64 编码（CanvasInlineArg.BuildRawText + 内联序列化）
///
/// 注意：
/// - 内联格式与独立文件格式的区别：内联格式无文件头，直接是 ListVar
/// - BeScript 中的转义（bes:）由脚本解析层处理，此测试聚焦于 canvas:格式
/// </summary>
public sealed class CanvasBase64RoundtripTests
{
    /// <summary>
    /// 构建内联格式的 Canvas 二进制数据（无文件头，直接是 ListVar）
    /// </summary>
    private static byte[] BuildInlineCanvasBytes()
    {
        DictVar label = new DictVar();
        label[new zchar("type")] = new StringVar { v = "UILabel" };
        label[new zchar("name")] = new StringVar { v = "inlineLabel" };
        label[new zchar("parentAnchor")] = new LongVar { v = 4 };
        label[new zchar("anchor")] = new LongVar { v = 4 };
        label[new zchar("Pos")] = new Vector3Var { X = 0, Y = 0, Z = 0 };
        label[new zchar("size")] = new Vector3Var { X = 200, Y = 50, Z = 1 };
        label[new zchar("text")] = new StringVar { v = "Inline Canvas" };
        label[new zchar("FontSize")] = new FloatVar { v = 20f };

        ListVar sons = new ListVar();
        sons.v.Add(label);

        DictVar root = new DictVar();
        root[new zchar("type")] = new StringVar { v = "UIRect" };
        root[new zchar("name")] = new StringVar { v = "inlineRoot" };
        root[new zchar("parentAnchor")] = new LongVar { v = 4 };
        root[new zchar("anchor")] = new LongVar { v = 4 };
        root[new zchar("size")] = new Vector3Var { X = 500, Y = 400, Z = 1 };
        root[new zchar("sons")] = sons;

        // 内联格式：直接 ListVar（无文件头）
        using BinWriter writer = new BinWriter(256);
        writer.Write((byte)VarType.List);
        writer.Write((ushort)1);
        root.SaveToBinary(writer, 0);

        return writer.ToArray();
    }

    /// <summary>
    /// 将 CanvasNode 树序列化为内联格式并编码为 Base64
    /// </summary>
    private static string SerializeToInlineBase64(CanvasNode root, string canvasName)
    {
        CanvasPreview preview = new CanvasPreview();

        // 内联格式：直接 ListVar
        using BinWriter writer = new BinWriter(4096);
        ListVar listVar = new ListVar();
        foreach (CanvasNode child in root.Children)
        {
            listVar.v.Add(preview.ConvertNodeToDict(child));
        }
        listVar.SaveToBinary(writer, 0);

        return CanvasInlineArg.BuildLegacyBase64RawText(canvasName, writer.ToArray());
    }

    [Fact]
    public void Roundtrip_SimpleInlineCanvas_ShouldBe_Lossless()
    {
        byte[] bytes = BuildInlineCanvasBytes();
        string originalInline = CanvasInlineArg.BuildLegacyBase64RawText("simple_inline", bytes);

        // Base64 解码 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseInline(originalInline);

        Assert.True(result.Ok);
        Assert.NotNull(result.Root);
        Assert.Equal("simple_inline", result.CanvasName);

        // 验证解析结果
        Assert.Single(result.Root.Children);
        CanvasNode root = result.Root.Children[0];
        Assert.Equal("UIRect", root.Type);
        Assert.Equal("inlineRoot", root.name.v);
        Assert.Single(root.Children);

        CanvasNode label = root.Children[0];
        Assert.Equal("UILabel", label.Type);
        Assert.Equal("inlineLabel", label.name.v);

        // CanvasNode → Base64
        string roundtripInline = SerializeToInlineBase64(result.Root, result.CanvasName);

        // 验证字符串级一致
        Assert.Equal(originalInline, roundtripInline);
    }

    [Fact]
    public void Roundtrip_EmptyInlineCanvas_ShouldBe_Lossless()
    {
        // 构建空内联 Canvas
        using BinWriter writer = new BinWriter(32);
        writer.Write((byte)VarType.List);
        writer.Write((ushort)0);

        byte[] bytes = writer.ToArray();
        string originalInline = CanvasInlineArg.BuildLegacyBase64RawText("empty_inline", bytes);

        // Base64 解码 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseInline(originalInline);

        Assert.True(result.Ok);
        Assert.NotNull(result.Root);
        Assert.Equal("empty_inline", result.CanvasName);
        Assert.Empty(result.Root.Children);

        // CanvasNode → Base64
        string roundtripInline = SerializeToInlineBase64(result.Root, result.CanvasName);

        // 验证字符串级一致
        Assert.Equal(originalInline, roundtripInline);
    }

    [Fact]
    public void Roundtrip_MultipleTopLevelInlineCanvas_ShouldBe_Lossless()
    {
        // 构建多个顶层节点的内联 Canvas
        DictVar node1 = new DictVar();
        node1[new zchar("type")] = new StringVar { v = "UIRect" };
        node1[new zchar("name")] = new StringVar { v = "top1" };
        node1[new zchar("parentAnchor")] = new LongVar { v = 4 };
        node1[new zchar("anchor")] = new LongVar { v = 4 };
        node1[new zchar("size")] = new Vector3Var { X = 100, Y = 100, Z = 1 };

        DictVar node2 = new DictVar();
        node2[new zchar("type")] = new StringVar { v = "UILabel" };
        node2[new zchar("name")] = new StringVar { v = "top2" };
        node2[new zchar("parentAnchor")] = new LongVar { v = 4 };
        node2[new zchar("anchor")] = new LongVar { v = 4 };
        node2[new zchar("size")] = new Vector3Var { X = 200, Y = 50, Z = 1 };
        node2[new zchar("text")] = new StringVar { v = "Label" };

        using BinWriter writer = new BinWriter(128);
        writer.Write((byte)VarType.List);
        writer.Write((ushort)2);
        node1.SaveToBinary(writer, 0);
        node2.SaveToBinary(writer, 0);

        string originalInline = CanvasInlineArg.BuildLegacyBase64RawText("multi_top_inline", writer.ToArray());

        // Base64 解码 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseInline(originalInline);

        Assert.True(result.Ok);
        Assert.NotNull(result.Root);
        Assert.Equal(2, result.Root.Children.Count);

        // CanvasNode → Base64
        string roundtripInline = SerializeToInlineBase64(result.Root, result.CanvasName);

        // 验证字符串级一致
        Assert.Equal(originalInline, roundtripInline);
    }

    [Fact]
    public void Roundtrip_ComplexInlineCanvas_ShouldBe_Lossless()
    {
        // 构建包含多种类型的复杂内联 Canvas
        ListVar sons = new ListVar();

        // UIButton
        DictVar button = new DictVar();
        button[new zchar("type")] = new StringVar { v = "UIButton" };
        button[new zchar("name")] = new StringVar { v = "btn" };
        button[new zchar("parentAnchor")] = new LongVar { v = 4 };
        button[new zchar("anchor")] = new LongVar { v = 4 };
        button[new zchar("size")] = new Vector3Var { X = 150, Y = 50, Z = 1 };
        button[new zchar("label")] = new StringVar { v = "Button" };
        button[new zchar("clickCBName")] = new StringVar { v = "onBtnClick" };
        sons.v.Add(button);

        // TextPro
        DictVar textPro = new DictVar();
        textPro[new zchar("type")] = new StringVar { v = "TextPro" };
        textPro[new zchar("name")] = new StringVar { v = "textPro" };
        textPro[new zchar("parentAnchor")] = new LongVar { v = 4 };
        textPro[new zchar("anchor")] = new LongVar { v = 4 };
        textPro[new zchar("size")] = new Vector3Var { X = 300, Y = 80, Z = 1 };
        textPro[new zchar("text")] = new StringVar { v = "TextPro Inline" };
        textPro[new zchar("bGLOW")] = new BoolVar { v = true };
        textPro[new zchar("GlowColor")] = new ColorVar { R = 1f, G = 0f, B = 0f, A = 1f };
        sons.v.Add(textPro);

        // 根节点
        DictVar root = new DictVar();
        root[new zchar("type")] = new StringVar { v = "UIRect" };
        root[new zchar("name")] = new StringVar { v = "complexRoot" };
        root[new zchar("parentAnchor")] = new LongVar { v = 4 };
        root[new zchar("anchor")] = new LongVar { v = 4 };
        root[new zchar("size")] = new Vector3Var { X = 800, Y = 600, Z = 1 };
        root[new zchar("sons")] = sons;

        using BinWriter writer = new BinWriter(512);
        writer.Write((byte)VarType.List);
        writer.Write((ushort)1);
        root.SaveToBinary(writer, 0);

        string originalInline = CanvasInlineArg.BuildLegacyBase64RawText("complex_inline", writer.ToArray());

        // Base64 解码 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseInline(originalInline);

        Assert.True(result.Ok);
        Assert.NotNull(result.Root);

        // 验证解析结果
        Assert.Single(result.Root.Children);
        CanvasNode rootParsed = result.Root.Children[0];
        Assert.Equal(2, rootParsed.Children.Count);
        Assert.Equal("UIButton", rootParsed.Children[0].Type);
        Assert.Equal("TextPro", rootParsed.Children[1].Type);

        // CanvasNode → Base64
        string roundtripInline = SerializeToInlineBase64(result.Root, result.CanvasName);

        // 验证字符串级一致
        Assert.Equal(originalInline, roundtripInline);
    }

    [Fact]
    public void Roundtrip_UnicodeCanvasName_ShouldBe_Lossless()
    {
        // 使用 Unicode 名称
        DictVar label = new DictVar();
        label[new zchar("type")] = new StringVar { v = "UILabel" };
        label[new zchar("name")] = new StringVar { v = "label" };
        label[new zchar("parentAnchor")] = new LongVar { v = 4 };
        label[new zchar("size")] = new Vector3Var { X = 100, Y = 30, Z = 1 };

        using BinWriter writer = new BinWriter(64);
        writer.Write((byte)VarType.List);
        writer.Write((ushort)1);
        label.SaveToBinary(writer, 0);

        string originalInline = CanvasInlineArg.BuildLegacyBase64RawText("中文画布名称", writer.ToArray());

        // Base64 解码 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseInline(originalInline);

        Assert.True(result.Ok);
        Assert.NotNull(result.Root);
        Assert.Equal("中文画布名称", result.CanvasName);

        // CanvasNode → Base64
        string roundtripInline = SerializeToInlineBase64(result.Root, result.CanvasName);

        // 验证字符串级一致
        Assert.Equal(originalInline, roundtripInline);
    }

    [Fact]
    public void Roundtrip_SpecialCharactersInText_ShouldBe_Lossless()
    {
        // 包含特殊字符的文本
        DictVar label = new DictVar();
        label[new zchar("type")] = new StringVar { v = "UILabel" };
        label[new zchar("name")] = new StringVar { v = "specialLabel" };
        label[new zchar("parentAnchor")] = new LongVar { v = 4 };
        label[new zchar("size")] = new Vector3Var { X = 200, Y = 50, Z = 1 };
        label[new zchar("text")] = new StringVar { v = "Line1\nLine2\tTabbed" };
        label[new zchar("FontSize")] = new FloatVar { v = 16f };

        using BinWriter writer = new BinWriter(128);
        writer.Write((byte)VarType.List);
        writer.Write((ushort)1);
        label.SaveToBinary(writer, 0);

        string originalInline = CanvasInlineArg.BuildLegacyBase64RawText("special_text", writer.ToArray());

        // Base64 解码 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseInline(originalInline);

        Assert.True(result.Ok);
        CanvasNode root = Assert.IsType<UICanvasNode>(result.Root);

        // 验证特殊字符保留
        UILabelNode parsedLabel = Assert.IsType<UILabelNode>(root.Children[0]);
        StringVar parsedText = Assert.IsType<StringVar>(parsedLabel.text);
        Assert.Equal("Line1\nLine2\tTabbed", parsedText.v);

        // CanvasNode → Base64
        string roundtripInline = SerializeToInlineBase64(result.Root, result.CanvasName);

        // 验证字符串级一致
        Assert.Equal(originalInline, roundtripInline);
    }

    [Fact]
    public void ParseInline_InvalidFormat_ShouldFail()
    {
        CanvasPreview preview = new CanvasPreview();

        // 缺少 canvas: 前缀
        CanvasNodeResult result1 = preview.ParseInline("name=AAAA");
        Assert.False(result1.Ok);

        // 缺少 = 分隔符
        CanvasNodeResult result2 = preview.ParseInline("canvas:name");
        Assert.False(result2.Ok);

        // 空 Base64
        CanvasNodeResult result3 = preview.ParseInline("canvas:name=");
        Assert.False(result3.Ok);

        // 无效 Base64
        CanvasNodeResult result4 = preview.ParseInline("canvas:name=!!!invalid!!!");
        Assert.False(result4.Ok);
    }

    [Fact]
    public void Roundtrip_AllFieldTypes_ShouldBe_Lossless()
    {
        // 测试所有字段类型：Long, Float, Bool, String, Vector3, Color
        DictVar rect = new DictVar();
        rect[new zchar("type")] = new StringVar { v = "UIRect" };
        rect[new zchar("name")] = new StringVar { v = "allFields" };

        // Long 字段
        rect[new zchar("guid")] = new LongVar { v = 12345 };
        rect[new zchar("parentAnchor")] = new LongVar { v = 4 };
        rect[new zchar("anchor")] = new LongVar { v = 4 };
        rect[new zchar("widthExtend")] = new LongVar { v = 1 };
        rect[new zchar("heightExtend")] = new LongVar { v = 1 };

        // Float 字段
        rect[new zchar("rot")] = new FloatVar { v = 45f };
        rect[new zchar("Radius")] = new FloatVar { v = 15f };
        rect[new zchar("BorderWidth")] = new FloatVar { v = 2f };

        // Vector3 字段
        rect[new zchar("Pos")] = new Vector3Var { X = 10, Y = 20, Z = 0 };
        rect[new zchar("size")] = new Vector3Var { X = 300, Y = 200, Z = 1 };

        // Color 字段
        rect[new zchar("color")] = new ColorVar { R = 0.5f, G = 0.6f, B = 0.7f, A = 0.8f };

        // Bool 字段
        rect[new zchar("mask")] = new BoolVar { v = true };

        using BinWriter writer = new BinWriter(256);
        writer.Write((byte)VarType.List);
        writer.Write((ushort)1);
        rect.SaveToBinary(writer, 0);

        string originalInline = CanvasInlineArg.BuildLegacyBase64RawText("all_fields", writer.ToArray());

        // Base64 解码 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseInline(originalInline);

        Assert.True(result.Ok);

        // 验证所有字段
        Assert.NotNull(result.Root);
        UIRectNode parsed = Assert.IsType<UIRectNode>(result.Root.Children[0]);
        Assert.NotNull(parsed.guid);
        Assert.Equal(12345, parsed.guid.v);
        Assert.NotNull(parsed.parentAnchor);
        Assert.Equal(4, parsed.parentAnchor.v);
        Assert.NotNull(parsed.anchor);
        Assert.Equal(4, parsed.anchor.v);
        Assert.NotNull(parsed.widthExtend);
        Assert.Equal(1, parsed.widthExtend.v);
        Assert.NotNull(parsed.heightExtend);
        Assert.Equal(1, parsed.heightExtend.v);
        Assert.NotNull(parsed.rot);
        Assert.Equal(45f, parsed.rot.v);
        Assert.NotNull(parsed.Radius);
        Assert.Equal(15f, parsed.Radius.v);
        Assert.NotNull(parsed.BorderWidth);
        Assert.Equal(2f, parsed.BorderWidth.v);
        Assert.NotNull(parsed.Pos);
        Assert.Equal(10f, parsed.Pos.X);
        Assert.Equal(20f, parsed.Pos.Y);
        Assert.NotNull(parsed.size);
        Assert.Equal(300f, parsed.size.X);
        Assert.Equal(200f, parsed.size.Y);
        Assert.NotNull(parsed.color);
        Assert.Equal(0.5f, parsed.color.R);
        Assert.Equal(0.6f, parsed.color.G);
        Assert.Equal(0.7f, parsed.color.B);
        Assert.Equal(0.8f, parsed.color.A);
        Assert.NotNull(parsed.mask);
        Assert.True(parsed.mask.v);

        // CanvasNode → Base64
        string roundtripInline = SerializeToInlineBase64(result.Root, result.CanvasName);

        // 验证字符串级一致
        Assert.Equal(originalInline, roundtripInline);
    }

    [Fact]
    public void Roundtrip_DeepNestedInlineCanvas_ShouldBe_Lossless()
    {
        // 构建深度嵌套的内联 Canvas
        DictVar BuildNestedNode(int depth)
        {
            DictVar node = new DictVar();
            node[new zchar("type")] = new StringVar { v = "UIRect" };
            node[new zchar("name")] = new StringVar { v = $"level{depth}" };
            node[new zchar("parentAnchor")] = new LongVar { v = 4 };
            node[new zchar("anchor")] = new LongVar { v = 4 };
            node[new zchar("size")] = new Vector3Var { X = 100 - depth * 10, Y = 100 - depth * 10, Z = 1 };

            if (depth > 0)
            {
                ListVar sons = new ListVar();
                sons.v.Add(BuildNestedNode(depth - 1));
                node[new zchar("sons")] = sons;
            }

            return node;
        }

        DictVar root = BuildNestedNode(4);

        using BinWriter writer = new BinWriter(512);
        writer.Write((byte)VarType.List);
        writer.Write((ushort)1);
        root.SaveToBinary(writer, 0);

        string originalInline = CanvasInlineArg.BuildLegacyBase64RawText("deep_nested", writer.ToArray());

        // Base64 解码 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseInline(originalInline);

        Assert.True(result.Ok);
        Assert.NotNull(result.Root);

        // 验证嵌套深度
        CanvasNode current = result.Root.Children[0];
        int actualDepth = 0;
        while (current.Children.Count > 0)
        {
            actualDepth++;
            current = current.Children[0];
        }
        Assert.Equal(4, actualDepth);

        // CanvasNode → Base64
        string roundtripInline = SerializeToInlineBase64(result.Root, result.CanvasName);

        // 验证字符串级一致
        Assert.Equal(originalInline, roundtripInline);
    }
}
