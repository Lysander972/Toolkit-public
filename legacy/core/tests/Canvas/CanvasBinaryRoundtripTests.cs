using Xunit;
using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Canvas;

/// <summary>
/// Canvas 二进制独立文件到 Node 的双向转换测试
///
/// 测试架构：
/// - 二进制 → CanvasNode（CanvasPreview.ParseBytes）
/// - CanvasNode → 二进制（CanvasPreview.ConvertNodeToDict + DictVar.SaveToBinary）
///
/// 原则：全程无损，字节级一致
/// </summary>
public sealed class CanvasBinaryRoundtripTests
{
    /// <summary>
    /// 构建一个简单的 Canvas 二进制文件（带文件头）
    /// </summary>
    private static byte[] BuildSimpleCanvasBytes()
    {
        // 构建子节点
        DictVar childDict = new DictVar();
        childDict[new zchar("type")] = new StringVar { v = "UILabel" };
        childDict[new zchar("name")] = new StringVar { v = "label1" };
        childDict[new zchar("parentAnchor")] = new LongVar { v = 4 }; // MiddleCenter
        childDict[new zchar("anchor")] = new LongVar { v = 4 };
        childDict[new zchar("Pos")] = new Vector3Var { X = 0, Y = 100, Z = 0 };
        childDict[new zchar("size")] = new Vector3Var { X = 200, Y = 50, Z = 1 };
        childDict[new zchar("text")] = new StringVar { v = "Hello World" };
        childDict[new zchar("FontSize")] = new FloatVar { v = 24f };

        ListVar sons = new ListVar();
        sons.v.Add(childDict);

        // 构建根节点
        DictVar rootDict = new DictVar();
        rootDict[new zchar("type")] = new StringVar { v = "UIRect" };
        rootDict[new zchar("name")] = new StringVar { v = "root" };
        rootDict[new zchar("parentAnchor")] = new LongVar { v = 4 };
        rootDict[new zchar("anchor")] = new LongVar { v = 4 };
        rootDict[new zchar("Pos")] = new Vector3Var { X = 0, Y = 0, Z = 0 };
        rootDict[new zchar("size")] = new Vector3Var { X = 1080, Y = 1920, Z = 1 };
        rootDict[new zchar("sons")] = sons;

        // 写入完整 Canvas 文件格式
        using BinWriter writer = new BinWriter(512);
        writer.Write((byte)0x02);
        writer.Write((byte)0x76);
        writer.Write((byte)0x31);
        writer.WriteAsString("test_canvas".AsSpan());

        writer.Write((byte)VarType.List);
        writer.Write((ushort)1);
        rootDict.SaveToBinary(writer, 0);

        return writer.ToArray();
    }

    /// <summary>
    /// 构建包含多种节点类型的复杂 Canvas 二进制文件
    /// </summary>
    private static byte[] BuildComplexCanvasBytes()
    {
        ListVar sons = new ListVar();

        // UILabel
        DictVar label = new DictVar();
        label[new zchar("type")] = new StringVar { v = "UILabel" };
        label[new zchar("name")] = new StringVar { v = "titleLabel" };
        label[new zchar("parentAnchor")] = new LongVar { v = 1 }; // UpperCenter
        label[new zchar("anchor")] = new LongVar { v = 1 };
        label[new zchar("Pos")] = new Vector3Var { X = 0, Y = -50, Z = 0 };
        label[new zchar("size")] = new Vector3Var { X = 400, Y = 60, Z = 1 };
        label[new zchar("text")] = new StringVar { v = "Title" };
        label[new zchar("FontSize")] = new FloatVar { v = 32f };
        label[new zchar("Alignment")] = new LongVar { v = 4 };
        sons.v.Add(label);

        // UIButton
        DictVar button = new DictVar();
        button[new zchar("type")] = new StringVar { v = "UIButton" };
        button[new zchar("name")] = new StringVar { v = "okButton" };
        button[new zchar("parentAnchor")] = new LongVar { v = 7 }; // LowerCenter
        button[new zchar("anchor")] = new LongVar { v = 7 };
        button[new zchar("Pos")] = new Vector3Var { X = 0, Y = 100, Z = 0 };
        button[new zchar("size")] = new Vector3Var { X = 200, Y = 80, Z = 1 };
        button[new zchar("label")] = new StringVar { v = "OK" };
        button[new zchar("FontSize")] = new FloatVar { v = 20f };
        button[new zchar("clickCBName")] = new StringVar { v = "onOkClick" };
        sons.v.Add(button);

        // UIRect
        DictVar rect = new DictVar();
        rect[new zchar("type")] = new StringVar { v = "UIRect" };
        rect[new zchar("name")] = new StringVar { v = "background" };
        rect[new zchar("parentAnchor")] = new LongVar { v = 4 };
        rect[new zchar("anchor")] = new LongVar { v = 4 };
        rect[new zchar("Pos")] = new Vector3Var { X = 0, Y = 0, Z = 0 };
        rect[new zchar("size")] = new Vector3Var { X = 800, Y = 600, Z = 1 };
        rect[new zchar("color")] = new ColorVar { R = 0.2f, G = 0.3f, B = 0.4f, A = 1f };
        rect[new zchar("Radius")] = new FloatVar { v = 10f };
        sons.v.Add(rect);

        // 构建根节点
        DictVar rootDict = new DictVar();
        rootDict[new zchar("type")] = new StringVar { v = "UIRect" };
        rootDict[new zchar("name")] = new StringVar { v = "root" };
        rootDict[new zchar("parentAnchor")] = new LongVar { v = 4 };
        rootDict[new zchar("anchor")] = new LongVar { v = 4 };
        rootDict[new zchar("Pos")] = new Vector3Var { X = 0, Y = 0, Z = 0 };
        rootDict[new zchar("size")] = new Vector3Var { X = 1080, Y = 1920, Z = 1 };
        rootDict[new zchar("sons")] = sons;

        using BinWriter writer = new BinWriter(1024);
        writer.Write((byte)0x02);
        writer.Write((byte)0x76);
        writer.Write((byte)0x31);
        writer.WriteAsString("complex_canvas".AsSpan());

        writer.Write((byte)VarType.List);
        writer.Write((ushort)1);
        rootDict.SaveToBinary(writer, 0);

        return writer.ToArray();
    }

    /// <summary>
    /// 构建包含 TextPro 节点的 Canvas
    /// </summary>
    private static byte[] BuildTextProCanvasBytes()
    {
        DictVar textPro = new DictVar();
        textPro[new zchar("type")] = new StringVar { v = "TextPro" };
        textPro[new zchar("name")] = new StringVar { v = "textPro1" };
        textPro[new zchar("parentAnchor")] = new LongVar { v = 4 };
        textPro[new zchar("anchor")] = new LongVar { v = 4 };
        textPro[new zchar("Pos")] = new Vector3Var { X = 0, Y = 0, Z = 0 };
        textPro[new zchar("size")] = new Vector3Var { X = 300, Y = 100, Z = 1 };
        textPro[new zchar("text")] = new StringVar { v = "TextPro Content" };
        textPro[new zchar("FontSize")] = new FloatVar { v = 28f };
        textPro[new zchar("bGLOW")] = new BoolVar { v = true };
        textPro[new zchar("GlowColor")] = new ColorVar { R = 1f, G = 0.8f, B = 0f, A = 1f };
        textPro[new zchar("GlowOffset")] = new FloatVar { v = 0.5f };
        textPro[new zchar("enableVertexGradient")] = new BoolVar { v = true };
        textPro[new zchar("topLeft")] = new ColorVar { R = 1f, G = 1f, B = 1f, A = 1f };
        textPro[new zchar("bottomRight")] = new ColorVar { R = 0.5f, G = 0.5f, B = 0.5f, A = 1f };

        ListVar sons = new ListVar();
        sons.v.Add(textPro);

        DictVar rootDict = new DictVar();
        rootDict[new zchar("type")] = new StringVar { v = "UIRect" };
        rootDict[new zchar("name")] = new StringVar { v = "root" };
        rootDict[new zchar("parentAnchor")] = new LongVar { v = 4 };
        rootDict[new zchar("anchor")] = new LongVar { v = 4 };
        rootDict[new zchar("Pos")] = new Vector3Var { X = 0, Y = 0, Z = 0 };
        rootDict[new zchar("size")] = new Vector3Var { X = 1080, Y = 1920, Z = 1 };
        rootDict[new zchar("sons")] = sons;

        using BinWriter writer = new BinWriter(512);
        writer.Write((byte)0x02);
        writer.Write((byte)0x76);
        writer.Write((byte)0x31);
        writer.WriteAsString("textpro_canvas".AsSpan());

        writer.Write((byte)VarType.List);
        writer.Write((ushort)1);
        rootDict.SaveToBinary(writer, 0);

        return writer.ToArray();
    }

    /// <summary>
    /// 将 CanvasNode 树序列化为完整 Canvas 文件格式
    /// 注意：root 是虚拟容器（UICanvasNode），它的 Children 才是实际顶层节点
    /// </summary>
    private static byte[] SerializeCanvasNode(CanvasNode root, string canvasName)
    {
        CanvasPreview preview = new CanvasPreview();
        using BinWriter writer = new BinWriter(4096);

        // 文件头
        writer.Write((byte)0x02);
        writer.Write((byte)0x76);
        writer.Write((byte)0x31);
        writer.WriteAsString(canvasName.AsSpan());

        // 根列表：直接写入 root.Children（每个子节点是实际的顶层UI节点）
        writer.Write((byte)VarType.List);
        writer.Write((ushort)root.Children.Count);
        foreach (CanvasNode child in root.Children)
        {
            DictVar childDict = preview.ConvertNodeToDict(child);
            childDict.SaveToBinary(writer, 0);
        }

        return writer.ToArray();
    }

    [Fact]
    public void Roundtrip_SimpleCanvas_ShouldBe_Lossless()
    {
        byte[] originalBytes = BuildSimpleCanvasBytes();

        // 二进制 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(originalBytes);

        Assert.True(result.Ok);
        Assert.NotNull(result.Root);
        Assert.Equal("test_canvas", result.CanvasName);

        // 验证解析结果
        Assert.Single(result.Root.Children);
        CanvasNode root = result.Root.Children[0];
        Assert.Equal("UIRect", root.Type);
        Assert.Equal("root", root.name.v);
        Assert.Single(root.Children);

        CanvasNode child = root.Children[0];
        Assert.Equal("UILabel", child.Type);
        Assert.Equal("label1", child.name.v);

        // CanvasNode → 二进制
        byte[] roundtripBytes = SerializeCanvasNode(result.Root, result.CanvasName);

        // 验证字节级一致
        Assert.Equal(originalBytes, roundtripBytes);
    }

    [Fact]
    public void Roundtrip_ComplexCanvas_ShouldBe_Lossless()
    {
        byte[] originalBytes = BuildComplexCanvasBytes();

        // 二进制 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(originalBytes);

        Assert.True(result.Ok);
        Assert.NotNull(result.Root);
        Assert.Equal("complex_canvas", result.CanvasName);

        // 验证解析结果
        Assert.Single(result.Root.Children);
        CanvasNode root = result.Root.Children[0];
        Assert.Equal("UIRect", root.Type);
        Assert.Equal("root", root.name.v);
        Assert.Equal(3, root.Children.Count);

        // CanvasNode → 二进制
        byte[] roundtripBytes = SerializeCanvasNode(result.Root, result.CanvasName);

        // 验证字节级一致
        Assert.Equal(originalBytes, roundtripBytes);
    }

    [Fact]
    public void Roundtrip_TextProCanvas_ShouldBe_Lossless()
    {
        byte[] originalBytes = BuildTextProCanvasBytes();

        // 二进制 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(originalBytes);

        Assert.True(result.Ok);
        Assert.NotNull(result.Root);
        Assert.Equal("textpro_canvas", result.CanvasName);

        // 验证 TextPro 特有属性解析
        Assert.Single(result.Root.Children);
        CanvasNode root = result.Root.Children[0];
        Assert.Single(root.Children);

        UITextProNode textProNode = Assert.IsType<UITextProNode>(root.Children[0]);
        Assert.True(Assert.IsType<BoolVar>(textProNode.bGLOW).v);
        Assert.NotNull(textProNode.GlowColor);
        Assert.Equal(1f, textProNode.GlowColor.R);
        Assert.Equal(0.8f, textProNode.GlowColor.G);
        Assert.True(Assert.IsType<BoolVar>(textProNode.enableVertexGradient).v);

        // CanvasNode → 二进制
        byte[] roundtripBytes = SerializeCanvasNode(result.Root, result.CanvasName);

        // 验证字节级一致
        Assert.Equal(originalBytes, roundtripBytes);
    }

    [Fact]
    public void Roundtrip_EmptyCanvas_ShouldBe_Lossless()
    {
        // 构建空 Canvas（无子节点）
        using BinWriter writer = new BinWriter(64);
        writer.Write((byte)0x02);
        writer.Write((byte)0x76);
        writer.Write((byte)0x31);
        writer.WriteAsString("empty_canvas".AsSpan());
        writer.Write((byte)VarType.List);
        writer.Write((ushort)0); // 空列表

        byte[] originalBytes = writer.ToArray();

        // 二进制 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(originalBytes);

        Assert.True(result.Ok);
        Assert.NotNull(result.Root);
        Assert.Equal("empty_canvas", result.CanvasName);
        Assert.Empty(result.Root.Children);

        // CanvasNode → 二进制
        byte[] roundtripBytes = SerializeCanvasNode(result.Root, result.CanvasName);

        // 验证字节级一致
        Assert.Equal(originalBytes, roundtripBytes);
    }

    [Fact]
    public void Roundtrip_MultipleTopLevelNodes_ShouldBe_Lossless()
    {
        // 构建多个顶层节点的 Canvas
        ListVar sons = new ListVar();

        // 顶层节点1
        DictVar node1 = new DictVar();
        node1[new zchar("type")] = new StringVar { v = "UIRect" };
        node1[new zchar("name")] = new StringVar { v = "top1" };
        node1[new zchar("parentAnchor")] = new LongVar { v = 4 };
        node1[new zchar("anchor")] = new LongVar { v = 4 };
        node1[new zchar("size")] = new Vector3Var { X = 100, Y = 100, Z = 1 };
        sons.v.Add(node1);

        // 顶层节点2
        DictVar node2 = new DictVar();
        node2[new zchar("type")] = new StringVar { v = "UILabel" };
        node2[new zchar("name")] = new StringVar { v = "top2" };
        node2[new zchar("parentAnchor")] = new LongVar { v = 4 };
        node2[new zchar("anchor")] = new LongVar { v = 4 };
        node2[new zchar("size")] = new Vector3Var { X = 200, Y = 50, Z = 1 };
        node2[new zchar("text")] = new StringVar { v = "Top Level Label" };
        sons.v.Add(node2);

        using BinWriter writer = new BinWriter(256);
        writer.Write((byte)0x02);
        writer.Write((byte)0x76);
        writer.Write((byte)0x31);
        writer.WriteAsString("multi_top_canvas".AsSpan());

        writer.Write((byte)VarType.List);
        writer.Write((ushort)2);
        foreach (VarBase dict in sons.v)
        {
            dict.SaveToBinary(writer, 0);
        }

        byte[] originalBytes = writer.ToArray();

        // 二进制 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(originalBytes);

        Assert.True(result.Ok);
        Assert.NotNull(result.Root);
        Assert.Equal("multi_top_canvas", result.CanvasName);
        Assert.Equal(2, result.Root.Children.Count);

        // CanvasNode → 二进制
        CanvasPreview preview2 = new CanvasPreview();
        using BinWriter writer2 = new BinWriter(256);
        writer2.Write((byte)0x02);
        writer2.Write((byte)0x76);
        writer2.Write((byte)0x31);
        writer2.WriteAsString(result.CanvasName.AsSpan());

        writer2.Write((byte)VarType.List);
        writer2.Write((ushort)result.Root.Children.Count);
        foreach (CanvasNode child in result.Root.Children)
        {
            DictVar dict = preview2.ConvertNodeToDict(child);
            dict.SaveToBinary(writer2, 0);
        }

        byte[] roundtripBytes = writer2.ToArray();

        // 验证字节级一致
        Assert.Equal(originalBytes, roundtripBytes);
    }

    [Fact]
    public void Roundtrip_WithColorPalette_ShouldBe_Lossless()
    {
        // 构建包含调色板颜色的节点（使用UIRect，因为它有Visual.color属性）
        DictVar rect = new DictVar();
        rect[new zchar("type")] = new StringVar { v = "UIRect" };
        rect[new zchar("name")] = new StringVar { v = "paletteRect" };
        rect[new zchar("parentAnchor")] = new LongVar { v = 4 };
        rect[new zchar("anchor")] = new LongVar { v = 4 };
        rect[new zchar("size")] = new Vector3Var { X = 200, Y = 50, Z = 1 };

        // 使用调色板颜色（原版格式：StringVar 存储调色板名称）
        rect[new zchar("color")] = new StringVar { v = "PrimaryColor" };

        using BinWriter writer = new BinWriter(256);
        writer.Write((byte)0x02);
        writer.Write((byte)0x76);
        writer.Write((byte)0x31);
        writer.WriteAsString("palette_canvas".AsSpan());

        writer.Write((byte)VarType.List);
        writer.Write((ushort)1);
        rect.SaveToBinary(writer, 0);

        byte[] originalBytes = writer.ToArray();

        // 二进制 → CanvasNode（不提供调色板，仅验证保留调色板名称）
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(originalBytes);

        Assert.True(result.Ok);
        Assert.NotNull(result.Root);

        // CanvasNode → 二进制
        byte[] roundtripBytes = SerializeCanvasNode(result.Root, result.CanvasName);

        // 验证字节级一致
        Assert.Equal(originalBytes, roundtripBytes);
    }

    [Fact]
    public void Roundtrip_DeepNestedStructure_ShouldBe_Lossless()
    {
        // 构建深度嵌套结构
        DictVar BuildNestedNode(int depth, string name)
        {
            DictVar node = new DictVar();
            node[new zchar("type")] = new StringVar { v = "UIRect" };
            node[new zchar("name")] = new StringVar { v = name };
            node[new zchar("parentAnchor")] = new LongVar { v = 4 };
            node[new zchar("anchor")] = new LongVar { v = 4 };
            node[new zchar("size")] = new Vector3Var { X = 100 + depth * 10, Y = 100 + depth * 10, Z = 1 };

            if (depth > 0)
            {
                ListVar sons = new ListVar();
                sons.v.Add(BuildNestedNode(depth - 1, $"{name}_child"));
                node[new zchar("sons")] = sons;
            }

            return node;
        }

        DictVar rootDict = BuildNestedNode(5, "root");

        using BinWriter writer = new BinWriter(1024);
        writer.Write((byte)0x02);
        writer.Write((byte)0x76);
        writer.Write((byte)0x31);
        writer.WriteAsString("nested_canvas".AsSpan());

        writer.Write((byte)VarType.List);
        writer.Write((ushort)1);
        rootDict.SaveToBinary(writer, 0);

        byte[] originalBytes = writer.ToArray();

        // 二进制 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(originalBytes);

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
        Assert.Equal(5, actualDepth);

        // CanvasNode → 二进制
        byte[] roundtripBytes = SerializeCanvasNode(result.Root, result.CanvasName);

        // 验证字节级一致
        Assert.Equal(originalBytes, roundtripBytes);
    }

    [Fact]
    public void Roundtrip_AllNodeTypes_ShouldBe_Lossless()
    {
        // 构建包含所有节点类型的 Canvas
        ListVar sons = new ListVar();

        // UIRect
        DictVar rect = new DictVar();
        rect[new zchar("type")] = new StringVar { v = "UIRect" };
        rect[new zchar("name")] = new StringVar { v = "rect1" };
        rect[new zchar("parentAnchor")] = new LongVar { v = 4 };
        rect[new zchar("anchor")] = new LongVar { v = 4 };
        rect[new zchar("size")] = new Vector3Var { X = 100, Y = 100, Z = 1 };
        rect[new zchar("color")] = new ColorVar { R = 1f, G = 0f, B = 0f, A = 1f };
        rect[new zchar("Radius")] = new FloatVar { v = 5f };
        rect[new zchar("fillMethod")] = new LongVar { v = 0 }; // HORIZONTAL
        rect[new zchar("fillAmount")] = new FloatVar { v = 0.5f };
        sons.v.Add(rect);

        // UIButton
        DictVar button = new DictVar();
        button[new zchar("type")] = new StringVar { v = "UIButton" };
        button[new zchar("name")] = new StringVar { v = "button1" };
        button[new zchar("parentAnchor")] = new LongVar { v = 4 };
        button[new zchar("anchor")] = new LongVar { v = 4 };
        button[new zchar("size")] = new Vector3Var { X = 150, Y = 50, Z = 1 };
        button[new zchar("label")] = new StringVar { v = "Click" };
        button[new zchar("clickCBName")] = new StringVar { v = "onClick" };
        sons.v.Add(button);

        // UILabel
        DictVar label = new DictVar();
        label[new zchar("type")] = new StringVar { v = "UILabel" };
        label[new zchar("name")] = new StringVar { v = "label1" };
        label[new zchar("parentAnchor")] = new LongVar { v = 4 };
        label[new zchar("anchor")] = new LongVar { v = 4 };
        label[new zchar("size")] = new Vector3Var { X = 200, Y = 30, Z = 1 };
        label[new zchar("text")] = new StringVar { v = "Label Text" };
        label[new zchar("FontSize")] = new FloatVar { v = 18f };
        label[new zchar("outlineWidth")] = new FloatVar { v = 2f };
        sons.v.Add(label);

        // TextPro
        DictVar textPro = new DictVar();
        textPro[new zchar("type")] = new StringVar { v = "TextPro" };
        textPro[new zchar("name")] = new StringVar { v = "textPro1" };
        textPro[new zchar("parentAnchor")] = new LongVar { v = 4 };
        textPro[new zchar("anchor")] = new LongVar { v = 4 };
        textPro[new zchar("size")] = new Vector3Var { X = 200, Y = 30, Z = 1 };
        textPro[new zchar("text")] = new StringVar { v = "TextPro Text" };
        textPro[new zchar("bUNDERLAY")] = new BoolVar { v = true };
        sons.v.Add(textPro);

        // UIInput
        DictVar input = new DictVar();
        input[new zchar("type")] = new StringVar { v = "UIInput" };
        input[new zchar("name")] = new StringVar { v = "input1" };
        input[new zchar("parentAnchor")] = new LongVar { v = 4 };
        input[new zchar("anchor")] = new LongVar { v = 4 };
        input[new zchar("size")] = new Vector3Var { X = 300, Y = 40, Z = 1 };
        input[new zchar("multiLine")] = new BoolVar { v = false };
        input[new zchar("contentType")] = new LongVar { v = 0 }; // STANDARD
        sons.v.Add(input);

        // UIRawImage
        DictVar rawImage = new DictVar();
        rawImage[new zchar("type")] = new StringVar { v = "UIRawImage" };
        rawImage[new zchar("name")] = new StringVar { v = "rawImage1" };
        rawImage[new zchar("parentAnchor")] = new LongVar { v = 4 };
        rawImage[new zchar("anchor")] = new LongVar { v = 4 };
        rawImage[new zchar("size")] = new Vector3Var { X = 100, Y = 100, Z = 1 };
        rawImage[new zchar("fileGuid")] = new StringVar { v = "test-guid-1234" };
        sons.v.Add(rawImage);

        // ScrollRect
        DictVar scroll = new DictVar();
        scroll[new zchar("type")] = new StringVar { v = "ScrollRect" };
        scroll[new zchar("name")] = new StringVar { v = "scroll1" };
        scroll[new zchar("parentAnchor")] = new LongVar { v = 4 };
        scroll[new zchar("anchor")] = new LongVar { v = 4 };
        scroll[new zchar("size")] = new Vector3Var { X = 400, Y = 300, Z = 1 };
        scroll[new zchar("scrollHorizontal")] = new BoolVar { v = false };
        scroll[new zchar("scrollVertical")] = new BoolVar { v = true };
        sons.v.Add(scroll);

        using BinWriter writer = new BinWriter(1024);
        writer.Write((byte)0x02);
        writer.Write((byte)0x76);
        writer.Write((byte)0x31);
        writer.WriteAsString("all_types_canvas".AsSpan());

        writer.Write((byte)VarType.List);
        writer.Write((ushort)sons.v.Count);
        foreach (VarBase dict in sons.v)
        {
            dict.SaveToBinary(writer, 0);
        }

        byte[] originalBytes = writer.ToArray();

        // 二进制 → CanvasNode
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(originalBytes);

        Assert.True(result.Ok);
        Assert.NotNull(result.Root);
        Assert.Equal(7, result.Root.Children.Count);

        // 验证所有节点类型
        Assert.Equal("UIRect", result.Root.Children[0].Type);
        Assert.Equal("UIButton", result.Root.Children[1].Type);
        Assert.Equal("UILabel", result.Root.Children[2].Type);
        Assert.Equal("TextPro", result.Root.Children[3].Type);
        Assert.Equal("UIInput", result.Root.Children[4].Type);
        Assert.Equal("UIRawImage", result.Root.Children[5].Type);
        Assert.Equal("ScrollRect", result.Root.Children[6].Type);

        // CanvasNode → 二进制
        CanvasPreview preview2 = new CanvasPreview();
        using BinWriter writer2 = new BinWriter(1024);
        writer2.Write((byte)0x02);
        writer2.Write((byte)0x76);
        writer2.Write((byte)0x31);
        writer2.WriteAsString(result.CanvasName.AsSpan());

        writer2.Write((byte)VarType.List);
        writer2.Write((ushort)result.Root.Children.Count);
        foreach (CanvasNode child in result.Root.Children)
        {
            DictVar dict = preview2.ConvertNodeToDict(child);
            dict.SaveToBinary(writer2, 0);
        }

        byte[] roundtripBytes = writer2.ToArray();

        // 验证字节级一致
        Assert.Equal(originalBytes, roundtripBytes);
    }
}
