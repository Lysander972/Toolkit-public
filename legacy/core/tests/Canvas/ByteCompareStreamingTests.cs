using Xunit;
using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Canvas;

public class ByteCompareStreamingTests
{
    public static readonly IReadOnlyList<string> TestCanvasPaths =
    [
        "test/data/projects/颜色对对碰/UI_Menu.canvas",
        "test/data/projects/颜色对对碰/UI_A2.canvas",
        "test/data/projects/水果切切切/UI_Menu.canvas",
        "test/data/projects/姓氏玩法/布局/UI_加载界面.canvas",
        "test/data/projects/姓氏玩法/布局/通用组件/UI_礼物半屏动图模板.canvas"
    ];

    private static readonly string s_projectRoot = FindProjectRoot();

    private static string FindProjectRoot()
    {
        // 从当前程序集位置向上查找包含 test/data 的目录
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "test", "data", "projects")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Cannot find project root with test/data/projects");
    }

    /// <summary>
    /// Node → HTML → Node，然后比较两个 Node 转成的二进制字节必须完全一致
    /// </summary>
    [Theory]
    [MemberData(nameof(GetTestCanvasData))]
    public void Node_Html_Node_BytesMustMatch(string testPath)
    {
        string fullPath = Path.Combine(s_projectRoot, testPath);
        if (!File.Exists(fullPath))
        {
            Console.WriteLine($"File not found: {fullPath}");
            return;
        }
        byte[] originalBytes = File.ReadAllBytes(fullPath);

        // 原始 Node
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(originalBytes);
        Assert.True(result.Ok);
        Assert.NotNull(result.Root);

        // Node → HTML → Node
        string html = CanvasConverter.BinaryToHtml(originalBytes);
        (CanvasNode node2, string name2) = CanvasConverter.FromHtml(html);
        Assert.NotNull(node2);

        // 两个 Node 分别转成二进制
        byte[] bytes1 = CanvasConverter.ToBinary(result.Root, result.CanvasName);
        byte[] bytes2 = CanvasConverter.ToBinary(node2, name2);

        Console.WriteLine($"{testPath}: Original={bytes1.Length}, Roundtrip={bytes2.Length}");

        // 递归逐字节比较
        CompareNodeBytes(result.Root, node2, "Root");

        // 最终字节比较
        if (!bytes1.SequenceEqual(bytes2))
        {
            for (int i = 0; i < Math.Min(bytes1.Length, bytes2.Length); i++)
            {
                if (bytes1[i] != bytes2[i])
                {
                    Console.WriteLine($"\nFirst byte diff at {i}: orig={bytes1[i]}, roundtrip={bytes2[i]}");
                    int ctxStart = Math.Max(0, i - 10);
                    int ctxEnd = Math.Min(bytes1.Length, i + 10);
                    Console.WriteLine($"Orig:     {string.Join(" ", bytes1[ctxStart..ctxEnd].Select(static b => $"{b,3:D}"))}");
                    Console.WriteLine($"Roundtrip:{string.Join(" ", bytes2[ctxStart..ctxEnd].Select(static b => $"{b,3:D}"))}");
                    break;
                }
            }
            Assert.Fail($"{testPath}: Node → HTML → Node bytes mismatch");
        }
    }

    public static IEnumerable<object[]> GetTestCanvasData() =>
        TestCanvasPaths.Select(static p => new object[] { p });

    private void CompareNodeBytes(CanvasNode node1, CanvasNode node2, string path)
    {
        // 比较类型和名称
        if (node1.Type != node2.Type)
        {
            Console.WriteLine($"[{path}] Type mismatch: {node1.Type} vs {node2.Type}");
            Assert.Fail($"[{path}] Type mismatch");
        }
        if (node1.name.v != node2.name.v)
        {
            Console.WriteLine($"[{path}] Name mismatch: '{node1.name.v}' vs '{node2.name.v}'");
        }

        // 转成 Dict 比较字段
        CanvasPreview preview = new CanvasPreview();
        DictVar dict1 = preview.ConvertNodeToDict(node1);
        DictVar dict2 = preview.ConvertNodeToDict(node2);

        if (dict1.Count != dict2.Count)
        {
            HashSet<string> fields1 = dict1.Keys.Select(static k => k.ToString()).ToHashSet();
            HashSet<string> fields2 = dict2.Keys.Select(static k => k.ToString()).ToHashSet();
            List<string> missing = fields1.Except(fields2).ToList();
            List<string> extra = fields2.Except(fields1).ToList();

            Console.WriteLine($"[{path}] Field count mismatch: {dict1.Count} vs {dict2.Count}");
            if (missing.Any()) Console.WriteLine($"  Missing in roundtrip: {string.Join(", ", missing)}");
            if (extra.Any()) Console.WriteLine($"  Extra in roundtrip: {string.Join(", ", extra)}");

            Assert.Fail($"[{path}] Field count mismatch");
        }

        // 比较每个字段的字节
        using BinWriter writer1 = new BinWriter(4096);
        using BinWriter writer2 = new BinWriter(4096);
        dict1.SaveToBinary(writer1, 0);
        dict2.SaveToBinary(writer2, 0);
        byte[] bytes1 = writer1.ToArray();
        byte[] bytes2 = writer2.ToArray();

        if (!bytes1.SequenceEqual(bytes2))
        {
            for (int i = 0; i < Math.Min(bytes1.Length, bytes2.Length); i++)
            {
                if (bytes1[i] != bytes2[i])
                {
                    Console.WriteLine($"[{path}] Byte diff at {i}: orig={bytes1[i]}, roundtrip={bytes2[i]}");
                    int ctxStart = Math.Max(0, i - 5);
                    int ctxEnd = Math.Min(bytes1.Length, i + 5);
                    Console.WriteLine($"  Orig:     {string.Join(" ", bytes1[ctxStart..ctxEnd].Select(static b => $"{b,3:D}"))}");
                    Console.WriteLine($"  Roundtrip:{string.Join(" ", bytes2[ctxStart..ctxEnd].Select(static b => $"{b,3:D}"))}");
                    break;
                }
            }
            Assert.Fail($"[{path}] Node '{node1.name.v}' bytes mismatch");
        }

        // 递归比较子节点
        if (node1.Children.Count != node2.Children.Count)
        {
            Console.WriteLine($"[{path}] Children count mismatch: {node1.Children.Count} vs {node2.Children.Count}");
            Assert.Fail($"[{path}] Children count mismatch");
        }

        for (int i = 0; i < node1.Children.Count; i++)
        {
            CompareNodeBytes(node1.Children[i], node2.Children[i], $"{path}/Children[{i}]");
        }
    }

    [Fact]
    public void Compare_Bytes_StepByStep()
    {
        string testPath = "test/data/projects/颜色对对碰/UI_A2.canvas";
        string fullPath = Path.Combine(s_projectRoot, testPath);
        if (!File.Exists(fullPath))
        {
            Console.WriteLine($"File not found: {fullPath} - skipping test");
            return; // 文件不存在时跳过而不是失败
        }
        byte[] originalBytes = File.ReadAllBytes(fullPath);

        Console.WriteLine($"Original size: {originalBytes.Length}");

        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(originalBytes);
        Assert.True(result.Ok);

        int pos = 0;

        // 比较文件头 [0x02 0x76 0x31]
        pos = AssertEqual(originalBytes, pos, new byte[] { 0x02, 0x76, 0x31 }, "File header");

        // 读取 canvas 名称长度和名称
        byte nameLen = originalBytes[pos++];
        byte[] nameBytes = originalBytes[pos..(pos + nameLen)];
        string canvasName = System.Text.Encoding.UTF8.GetString(nameBytes);
        pos += nameLen;
        Console.WriteLine($"[Canvas name] len={nameLen}, value='{canvasName}'");

        // 比较 List 类型和 count
        pos = AssertEqual(originalBytes, pos, new byte[] { (byte)VarType.List }, "List type marker");

        byte[] countBytes = originalBytes[pos..(pos + 2)];
        ushort count = BitConverter.ToUInt16(countBytes, 0);
        pos += 2;
        Console.WriteLine($"[List count] {count}");

        // 逐个子节点比较
        Assert.NotNull(result.Root);
        for (int i = 0; i < result.Root.Children.Count; i++)
        {
            CanvasNode child = result.Root.Children[i];
            Console.WriteLine($"\n=== Child[{i}] {child.Type} '{child.name.v}' ===");

            int beforePos = pos;

            // 读取原始数据的这个子节点
            VarType childType = (VarType)originalBytes[pos];
            Console.WriteLine($"  Original type at {pos}: {childType} ({(int)childType})");

            // 解析原始数据的 Dict
            // 注意: span 从 pos+1 开始，因为 pos 是 Dict 类型字节
            ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(originalBytes);

            if (childType == VarType.Dict)
            {
                DictVar dict = new DictVar();
                int endPos = dict.LoadFromBinary(span, pos + 1);
                Console.WriteLine($"  Dict has {dict.Count} fields: {string.Join(", ", dict.Keys.Select(static k => k.ToString()))}");

                // 我们写入的
                DictVar ourDict = preview.ConvertNodeToDict(child);
                Console.WriteLine($"  Our dict has {ourDict.Count} fields: {string.Join(", ", ourDict.Keys.Select(static k => k.ToString()))}");

                // 比较字段数
                if (dict.Count != ourDict.Count)
                {
                    HashSet<string> origFields = dict.Keys.Select(static k => k.ToString()).ToHashSet();
                    HashSet<string> ourFields = ourDict.Keys.Select(static k => k.ToString()).ToHashSet();
                    List<string> missing = origFields.Except(ourFields).ToList();
                    List<string> extra = ourFields.Except(origFields).ToList();

                    Console.WriteLine($"  FIELD COUNT MISMATCH: {dict.Count} vs {ourDict.Count}");
                    if (missing.Any()) Console.WriteLine($"  Missing fields: {string.Join(", ", missing)}");
                    if (extra.Any()) Console.WriteLine($"  Extra fields: {string.Join(", ", extra)}");
                }

                // 写入并比较字节
                using BinWriter writer = new BinWriter(4096);
                ourDict.SaveToBinary(writer, 0);
                byte[] ourBytes = writer.ToArray();

                byte[] origBytes = originalBytes[pos..endPos];  // 包含 Dict 类型字节

                // 详细比较两个 Dict 的字段内容
                Console.WriteLine("\n  === 详细字段比较 ===");
                List<(zchar key, VarBase value)> origFieldsList = dict.GetOrderedEntries().ToList();
                List<(zchar key, VarBase value)> ourFieldsList = ourDict.GetOrderedEntries().ToList();

                int maxFields = Math.Max(origFieldsList.Count, ourFieldsList.Count);
                for (int fi = 0; fi < maxFields; fi++)
                {
                    if (fi >= origFieldsList.Count)
                    {
                        string extraKey = ourFieldsList[fi].key.ToString();
                        Console.WriteLine($"  [{fi}] EXTRA in ours: '{extraKey}'");
                        continue;
                    }
                    if (fi >= ourFieldsList.Count)
                    {
                        string missingKey = origFieldsList[fi].key.ToString();
                        Console.WriteLine($"  [{fi}] MISSING in ours: '{missingKey}'");
                        continue;
                    }

                    (zchar key, VarBase value) origEntry = origFieldsList[fi];
                    (zchar key, VarBase value) ourEntry = ourFieldsList[fi];

                    if (origEntry.key.ToString() != ourEntry.key.ToString())
                    {
                        Console.WriteLine($"  [{fi}] KEY ORDER MISMATCH: orig='{origEntry.key}' vs ours='{ourEntry.key}'");
                    }
                    else if (origEntry.value.type != ourEntry.value.type)
                    {
                        Console.WriteLine($"  [{fi}] TYPE MISMATCH for '{origEntry.key}': orig={origEntry.value.type} vs ours={ourEntry.value.type}");
                    }
                }

                // 递归检查 sons 内部
                VarBase? origSons = dict.FindValue(new zchar("sons"));
                VarBase? ourSonsVar = ourDict.FindValue(new zchar("sons"));
                if (origSons != null && ourSonsVar != null)
                {
                    List<VarBase> origSonsList = ((ListVar)origSons).v;
                    List<VarBase> ourSonsList = ((ListVar)ourSonsVar).v;

                    Console.WriteLine($"\n  === sons 子节点比较 (orig={origSonsList.Count}, ours={ourSonsList.Count}) ===");

                    for (int si = 0; si < Math.Min(origSonsList.Count, ourSonsList.Count); si++)
                    {
                        if (origSonsList[si] is not DictVar origChild || ourSonsList[si] is not DictVar ourChild) continue;

                        if (origChild.FindValue(new zchar("type")) is not StringVar origTypeVar ||
                            ourChild.FindValue(new zchar("type")) is not StringVar ourTypeVar ||
                            origChild.FindValue(new zchar("name")) is not StringVar origNameVar ||
                            ourChild.FindValue(new zchar("name")) is not StringVar ourNameVar)
                        {
                            throw new InvalidOperationException("sons 子节点缺少 type/name 字段");
                        }
                        string origType = origTypeVar.v;
                        string ourType = ourTypeVar.v;
                        string origName = origNameVar.v;
                        string ourName = ourNameVar.v;

                        Console.WriteLine($"    Child[{si}]: orig={origType} '{origName}' ({origChild.Count} fields), ours={ourType} '{ourName}' ({ourChild.Count} fields)");

                        if (origChild.Count != ourChild.Count)
                        {
                            HashSet<string> origChildFields = origChild.Keys.Select(static k => k.ToString()).ToHashSet();
                            HashSet<string> ourChildFields = ourChild.Keys.Select(static k => k.ToString()).ToHashSet();
                            List<string> missing = origChildFields.Except(ourChildFields).ToList();
                            List<string> extra = ourChildFields.Except(origChildFields).ToList();

                            if (missing.Any()) Console.WriteLine($"      Missing: {string.Join(", ", missing)}");
                            if (extra.Any()) Console.WriteLine($"      Extra: {string.Join(", ", extra)}");
                        }
                    }
                }

                if (!origBytes.SequenceEqual(ourBytes))
                {
                    // 找第一个差异
                    for (int j = 0; j < Math.Min(origBytes.Length, ourBytes.Length); j++)
                    {
                        if (origBytes[j] != ourBytes[j])
                        {
                            Console.WriteLine($"\n  Byte diff at +{j}: orig={origBytes[j]}, ours={ourBytes[j]}");

                            // 显示上下文
                            int ctxStart = Math.Max(0, j - 5);
                            int ctxEnd = Math.Min(origBytes.Length, j + 5);
                            Console.WriteLine($"  Context: {string.Join(" ", origBytes[ctxStart..ctxEnd].Select(static b => $"{b,3:D}"))}");
                            Console.WriteLine($"  Ours:    {string.Join(" ", ourBytes[ctxStart..ctxEnd].Select(static b => $"{b,3:D}"))}");
                            break;
                        }
                    }

                    Assert.Fail($"Child[{i}] byte mismatch");
                }

                pos = originalBytes.Length - span.Length;
            }
            else
            {
                Console.WriteLine($"  Unexpected type: {childType}");
                break;
            }
        }

        Console.WriteLine($"\nFinal position: {pos} / {originalBytes.Length}");
    }

    private int AssertEqual(byte[] original, int pos, byte[] expected, string label)
    {
        byte[] actual = original[pos..(pos + expected.Length)];
        if (!expected.SequenceEqual(actual))
        {
            Console.WriteLine($"[{label}] MISMATCH at {pos}");
            Console.WriteLine($"  Expected: {string.Join(" ", expected.Select(static b => $"{b,3:D}"))}");
            Console.WriteLine($"  Actual:   {string.Join(" ", actual.Select(static b => $"{b,3:D}"))}");
            Assert.Fail($"{label} mismatch");
        }
        Console.WriteLine($"[{label}] OK at {pos}");
        return pos + expected.Length;
    }
}
