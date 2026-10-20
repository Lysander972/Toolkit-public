using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.TsView;

[Collection("WorkspaceState")]
public sealed class TsViewInlineCanvasWarningsTests
{
    [Fact]
    public void Read_WithGuidCanvasConst_ShouldNotWarn_AsInlineCanvasParseFailure()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_InlineCanvasWarnings");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            // 创建 setting.json（项目必需）
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "{\n" +
                " guid: test00000000000000000000000002\n" +
                " name: TestProject\n" +
                "}");

            // 创建地图实体（三元素平级：.map + .map.guid + .map_/）
            string mapGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), mapGuid);
            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "{list:[]}");

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n Hello\n]");

            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Hello.code"),
                "begincodeV2\n" +
                "name:Hello\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "Canvas UI=fun create.canvas(Canvas guid@1eec5522f0f3d48db868c2e201303901f)\n" +
                "endcode\n");

            // 创建 dummy 资源以通过 Canvas GUID 校验
            string dummyCanvas = SystemIOWrapper.Combine(projectRootFullPath, "Dummy.canvas_");
            SystemIOWrapper.WriteAllText(dummyCanvas, "dummy");
            SystemIOWrapper.WriteAllText(dummyCanvas + ".guid", "eec5522f0f3d48db868c2e201303901f");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> readWarnings) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);

            Assert.NotNull(readData);
            Assert.Contains("BeCanvas.fromGuidConstLegacy(\"eec5522f0f3d48db868c2e201303901f\")", readData.TsView);

            List<ToolError> inlineCanvasWarnings = readWarnings.Where(static w => w.Message.Contains("内联 Canvas 解析失败", StringComparison.Ordinal)).ToList();
            Assert.Empty(inlineCanvasWarnings);

            List<string> tsLines = TextLines.SplitLines(readData.TsView).ToList();
            int docIndex = tsLines.FindIndex(static l => l.Contains("sourcePath:", StringComparison.Ordinal) && l.Contains("Hello.code", StringComparison.Ordinal));
            Assert.True(docIndex >= 0, "未找到 Hello.code 的脚本文档块");

            int debugLineIndex = -1;
            for (int i = docIndex; i < tsLines.Count; i++)
            {
                if (i != docIndex && string.Equals(tsLines[i], "/**", StringComparison.Ordinal))
                {
                    break;
                }

                if (tsLines[i].Contains("G.debug.enable(BeBool.fromBeConst(\"1\"))", StringComparison.Ordinal))
                {
                    debugLineIndex = i;
                    break;
                }
            }

            Assert.True(debugLineIndex >= 0, "未在 Hello.code 方法体内找到脚本语句行: debug.enable");

            TsViewPatch[] patches = new[]
            {
                // 用正则确保确实产生差异，避免因为格式差异导致 Replace 未命中而误判"无变化"。
                new TsViewPatch(
                    StartLine: debugLineIndex + 1,
                    EndLine: debugLineIndex + 1,
                    NewLines: new[]
                    {
                        System.Text.RegularExpressions.Regex.Replace(
                            tsLines[debugLineIndex],
                            "BeBool\\.fromBeConst\\(\\\"1\\\"\\)",
                            "BeBool.fromBeConst(\"0\")")
                    })
            };
            string newLine = patches[0].NewLines[0];
            Assert.NotEqual(tsLines[debugLineIndex], newLine);
            (DeviceTsViewPatchData? patchData, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> patchWarnings) = services.DeviceTsView.Patch("TestMap.map_/0", patches, baseTsView: readData.TsView);
            Assert.Empty(patchErrors);
            Assert.NotNull(patchData);
            Assert.NotEmpty(patchData.AffectedRelativePaths);

            string helloText = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(scriptsDir, "Hello.code"));
            Assert.Contains("fun debug.enable(Bool 0)", helloText, StringComparison.Ordinal);
        }
        finally
        {
            TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
        }
    }

    [Fact]
    public void Read_And_Writeback_WithNodeCanvasConst_ShouldKeep_Node_Format()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_NodeInlineCanvas");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "{\n" +
                " guid: test00000000000000000000000003\n" +
                " name: TestProject\n" +
                "}");

            string mapGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), mapGuid);
            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "{list:[]}");

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n Hello\n]");

            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Hello.code"),
                "begincodeV2\n" +
                "name:Hello\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "Canvas UI=fun create.canvas(Canvas ==hjson@1@1\n" +
                "[\n" +
                " {\n" +
                "  type@1UILabel\n" +
                "  name@1标题\n" +
                "  Pos@1[0,0,0]\n" +
                "  parentAnchor@14\n" +
                "  size@1[120,40,1]\n" +
                "  text@1Hello\n" +
                " }\n" +
                "]==)\n" +
                "endcode\n");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);
            Assert.Contains("BeCanvas.fromNodeConst(\"\"", readData.TsView, StringComparison.Ordinal);

            List<string> tsLines = TextLines.SplitLines(readData.TsView).ToList();
            int docIndex = tsLines.FindIndex(static l => l.Contains("sourcePath:", StringComparison.Ordinal) && l.Contains("Hello.code", StringComparison.Ordinal));
            Assert.True(docIndex >= 0, "未找到 Hello.code 的脚本文档块");

            int debugLineIndex = -1;
            for (int i = docIndex; i < tsLines.Count; i++)
            {
                if (i != docIndex && string.Equals(tsLines[i], "/**", StringComparison.Ordinal))
                {
                    break;
                }

                if (tsLines[i].Contains("G.debug.enable(BeBool.fromBeConst(\"1\"))", StringComparison.Ordinal))
                {
                    debugLineIndex = i;
                    break;
                }
            }

            Assert.True(debugLineIndex >= 0, "未在 Hello.code 方法体内找到脚本语句行: debug.enable");
            Assert.StartsWith("    ", tsLines[debugLineIndex], StringComparison.Ordinal);
            Assert.False(tsLines[debugLineIndex].StartsWith("        ", StringComparison.Ordinal), "脚本语句行不应被官方 TS printer 改成 8 空格缩进。");

            TsViewPatch[] patches = new[]
            {
                new TsViewPatch(
                    StartLine: debugLineIndex + 1,
                    EndLine: debugLineIndex + 1,
                    NewLines: new[]
                    {
                        System.Text.RegularExpressions.Regex.Replace(
                            tsLines[debugLineIndex],
                            "BeBool\\.fromBeConst\\(\\\"1\\\"\\)",
                            "BeBool.fromBeConst(\"0\")")
                    })
            };

            (DeviceTsViewPatchData? patchData, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> patchWarnings) = services.DeviceTsView.Patch("TestMap.map_/0", patches, baseTsView: readData.TsView);
            Assert.Empty(patchErrors);
            Assert.NotNull(patchData);
            Assert.NotEmpty(patchData.AffectedRelativePaths);

            string helloText = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(scriptsDir, "Hello.code"));
            Assert.Contains("fun debug.enable(Bool 0)", helloText, StringComparison.Ordinal);
            Assert.Contains("==hjson@1@1", helloText, StringComparison.Ordinal);
        }
        finally
        {
            TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
        }
    }

    [Fact]
    public void Patch_WithRawBase64InFromBase64Const_ShouldReturnExplicitError()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_RawBase64Error");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "{\n" +
                " guid: test00000000000000000000000004\n" +
                " name: TestProject\n" +
                "}");

            string mapGuid = "cccccccccccccccccccccccccccccccc";
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), mapGuid);
            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "{list:[]}");

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n Hello\n]");

            // 使用新版 GUID 格式作为起点，patch 时替换成错误的 fromBase64Const
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Hello.code"),
                "begincodeV2\n" +
                "name:Hello\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "Canvas UI=fun create.canvas(Canvas ==guid@1aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa==)\n" +
                "endcode\n");

            string dummyCanvas = SystemIOWrapper.Combine(projectRootFullPath, "Dummy.canvas_");
            SystemIOWrapper.WriteAllText(dummyCanvas, "dummy");
            SystemIOWrapper.WriteAllText(dummyCanvas + ".guid", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);
            Assert.Contains("BeCanvas.fromGuidConst", readData.TsView, StringComparison.Ordinal);

            List<string> tsLines = TextLines.SplitLines(readData.TsView).ToList();
            int canvasLineIndex = tsLines.FindIndex(static l => l.Contains("BeCanvas.fromGuidConst", StringComparison.Ordinal));
            Assert.True(canvasLineIndex >= 0, "未找到 BeCanvas.fromGuidConst 行");
            TsViewPatch[] patches = new[]
                {
                    new TsViewPatch(
                        StartLine: canvasLineIndex + 1,
                        EndLine: canvasLineIndex + 1,
                        NewLines: new[]
                        {
                            "            var 特效Canvas: BeCanvas = G.create.canvas(BeCanvas.fromBase64Const(`3EAEAEQoABHR5cGUGBlVJUmVjdARuYW1lBgbog4zmma8DUG9zBAAAAAAAAAAAAAAAAAxwYXJlbnRBbmNob3IYBAAAAAAAAAAGYW5jaG9yGAQAAAAAAAAABHNpemUEAACWQwAAyEIAAIA/BWNvbG9yDAAAAAAAAAAAAAAAAAAAAAALQm9yZGVyV2lkdGgBAACAvwZSYWRpdXMBAAAAAARzb25zEAEAERYABHR5cGUGB1RleHRQcm8EbmFtZQYG5pWw5YC8A1BvcwQAAAAAAAAAAAAAAAAMcGFyZW50QW5jaG9yGAUAAAAAAAAABmFuY2hvchgFAAAAAAAAAAt3aWR0aEV4dGVuZBgCAAAAAAAAAARzaXplBAAAAAAAAKBCAACAPwVjb2xvcgwAAIA/AACAPwAAgD8AAIA/BHRleHQGBS05OTk5CEZvbnRTaXplAQAAQEILbGluZVNwYWNpbmcBAACAPwpGYWNlRGlsYXRlAc3MTD4QY2hhcmFjdGVyU3BhY2luZwEAAEBACWZvbnRTdHlsZRgCAAAAAAAAAAxvdXRsaW5lV2lkdGgBzcxMPg9vdXRsaW5lRGlzdGFuY2UEzcxMPs3MTD7NzEw+DG91dGxpbmVDb2xvcgwAAAAAAAAAAAAAAAAAAIA/FGVuYWJsZVZlcnRleEdyYWRpZW50BQEHdG9wTGVmdAwAAIA/CF4zPwAAAAAAAIA/CHRvcFJpZ2h0DAAAgD8IXjM/AAAAAAAAgD8KYm90dG9tTGVmdAwAAIA/CF4zPwAAAAAAAIA/C2JvdHRvbVJpZ2h0DAAAgD8IXjM/AAAAAAAAgD8=`));"
                        })
                };

            (DeviceTsViewPatchData? patchData, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("TestMap.map_/0", patches, baseTsView: readData.TsView);
            Assert.Null(patchData);
            Assert.True(patchErrors.Count > 0, "预期 patch 应失败");
            Assert.Contains(patchErrors, static e => e.Message.Contains("Canvas 常量格式规范", StringComparison.Ordinal));
            Assert.Contains(patchErrors, static e => e.Message.Contains("SDK 参考文档", StringComparison.Ordinal));
            Assert.Contains(patchErrors, static e => e.Message.Contains("Skills 的 Canvas 内联模板", StringComparison.Ordinal));
            Assert.Contains(patchErrors, static e => e.Message.Contains("Canvas XML解析失败", StringComparison.Ordinal));
        }
        finally
        {
            TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
        }
    }

    [Fact]
    public void Patch_WithRawHjsonInFromNodeConst_ShouldReturnExplicitError()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_RawHjsonError");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "{\n" +
                " guid: test00000000000000000000000005\n" +
                " name: TestProject\n" +
                "}");

            string mapGuid = "dddddddddddddddddddddddddddddddd";
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), mapGuid);
            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "{list:[]}");

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n Hello\n]");

            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Hello.code"),
                "begincodeV2\n" +
                "name:Hello\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "Canvas UI=fun create.canvas(Canvas ==guid@1aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa==)\n" +
                "endcode\n");

            string dummyCanvas = SystemIOWrapper.Combine(projectRootFullPath, "Dummy.canvas_");
            SystemIOWrapper.WriteAllText(dummyCanvas, "dummy");
            SystemIOWrapper.WriteAllText(dummyCanvas + ".guid", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);
            Assert.Contains("BeCanvas.fromGuidConst", readData.TsView, StringComparison.Ordinal);

            List<string> tsLines = TextLines.SplitLines(readData.TsView).ToList();
            int canvasLineIndex = tsLines.FindIndex(static l => l.Contains("BeCanvas.fromGuidConst", StringComparison.Ordinal));
            Assert.True(canvasLineIndex >= 0, "未找到 BeCanvas.fromGuidConst 行");
            string canvasLine = tsLines[canvasLineIndex];
            const string callPrefix = "BeCanvas.fromGuidConst(\"";
            int argumentStart = canvasLine.IndexOf(callPrefix, StringComparison.Ordinal) + callPrefix.Length;
            int argumentEnd = canvasLine.IndexOf("\")", argumentStart, StringComparison.Ordinal);
            Assert.True(argumentStart >= callPrefix.Length && argumentEnd > argumentStart, "Canvas GUID 参数格式不符合预期");
            string invalidCanvasLine = canvasLine[..argumentStart] + "==guid:" + canvasLine[argumentStart..argumentEnd] + "==" + canvasLine[argumentEnd..];

            TsViewPatch[] patches = new[]
                {
                    new TsViewPatch(
                        StartLine: canvasLineIndex + 1,
                        EndLine: canvasLineIndex + 1,
                        NewLines: new[]
                        {
                            "            var 特效Canvas: BeCanvas = G.create.canvas(BeCanvas.fromNodeConst(\"测试\", `[{type: UILabel}]`));"
                        })
                };

            (DeviceTsViewPatchData? patchData, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("TestMap.map_/0", patches, baseTsView: readData.TsView);
            Assert.Null(patchData);
            Assert.True(patchErrors.Count > 0, "预期 patch 应失败");
            Assert.Contains(patchErrors, static e => e.Message.Contains("Canvas 常量格式规范", StringComparison.Ordinal));
            Assert.Contains(patchErrors, static e => e.Message.Contains("SDK 参考文档", StringComparison.Ordinal));
            Assert.Contains(patchErrors, static e => e.Message.Contains("Skills 的 Canvas 内联模板", StringComparison.Ordinal));
            Assert.Contains(patchErrors, static e => e.Message.Contains("Canvas XML解析失败", StringComparison.Ordinal));
        }
        finally
        {
            TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
        }
    }

    [Fact]
    public void Patch_WithPrefixedGuidInFromGuidConst_ShouldReturnExplicitError()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_PrefixedGuidError");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "{\n" +
                " guid: test00000000000000000000000006\n" +
                " name: TestProject\n" +
                "}");

            string mapGuid = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), mapGuid);
            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "{list:[]}");

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n Hello\n]");

            // 新版 GUID 格式
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Hello.code"),
                "begincodeV2\n" +
                "name:Hello\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "Canvas UI=fun create.canvas(Canvas ==guid@1aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa==)\n" +
                "endcode\n");

            // 创建 dummy 资源以通过 Canvas GUID 校验
            string dummyCanvas = SystemIOWrapper.Combine(projectRootFullPath, "Dummy.canvas_");
            SystemIOWrapper.WriteAllText(dummyCanvas, "dummy");
            SystemIOWrapper.WriteAllText(dummyCanvas + ".guid", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);
            Assert.Contains("BeCanvas.fromGuidConst", readData.TsView, StringComparison.Ordinal);

            List<string> tsLines = TextLines.SplitLines(readData.TsView).ToList();
            int canvasLineIndex = tsLines.FindIndex(static l => l.Contains("BeCanvas.fromGuidConst", StringComparison.Ordinal));
            Assert.True(canvasLineIndex >= 0, "未找到 BeCanvas.fromGuidConst 行");

            TsViewPatch[] patches = new[]
                {
                    new TsViewPatch(
                        StartLine: canvasLineIndex + 1,
                        EndLine: canvasLineIndex + 1,
                        NewLines: new[]
                        {
                            // 故意把前后缀塞回参数
                            System.Text.RegularExpressions.Regex.Replace(
                                tsLines[canvasLineIndex],
                                @"BeCanvas\.fromGuidConst\(""([^""]+)""\)",
                                "BeCanvas.fromGuidConst(\"==guid:$1==\")")
                        })
                };

            string newLine = patches[0].NewLines[0];
            Assert.NotEqual(tsLines[canvasLineIndex], newLine);
            Assert.Contains("==guid:", newLine, StringComparison.Ordinal);

            (DeviceTsViewPatchData? patchData, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("TestMap.map_/0", patches, baseTsView: readData.TsView);
            Assert.Null(patchData);
            Assert.True(patchErrors.Count > 0, "预期 patch 应失败");
            Assert.Contains(patchErrors, static e => e.Message.Contains("Canvas 常量格式规范", StringComparison.Ordinal));
        }
        finally
        {
            TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
        }
    }
}
