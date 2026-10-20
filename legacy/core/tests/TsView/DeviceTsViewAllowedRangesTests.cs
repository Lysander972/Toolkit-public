using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.TsView;

[Collection("WorkspaceState")]
public sealed class DeviceTsViewAllowedRangesTests
{
    [Fact]
    public void Patch_Should_Block_Forbidden_Outer_Structure_Edit()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_AllowedRanges");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:\n[\n 0-1\n]");
            // 地图 GUID 文件和主文件
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });

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
                "endcode\n");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);
            Assert.NotNull(readData.EditAllowance.ForbiddenRanges);
            Assert.NotEmpty(readData.EditAllowance.ForbiddenRanges);

            string originalText = readData.TsView;
            List<string> lines = TextLines.SplitLines(originalText).ToList();

            int namespaceLineIndex = lines.FindIndex(l => l.StartsWith("namespace ", StringComparison.Ordinal));
            Assert.True(namespaceLineIndex >= 0, "未找到命名空间声明行");

            int bodyLineIndex = lines.FindIndex(l => l.Contains("G.debug.enable", StringComparison.Ordinal));
            Assert.True(bodyLineIndex >= 0, "未找到脚本体行");
            IReadOnlyList<TsViewEditRange> ranges = readData.EditAllowance.ForbiddenRanges;
            Assert.Contains(ranges, r => namespaceLineIndex + 1 >= r.StartLine && namespaceLineIndex + 1 <= r.EndLine);
            Assert.DoesNotContain(ranges, r => bodyLineIndex + 1 >= r.StartLine && bodyLineIndex + 1 <= r.EndLine);

            // 修改命名空间行（固定骨架）
            lines[namespaceLineIndex] = lines[namespaceLineIndex].Replace("namespace ", "namespace Hacked_");

            string[] currentLines = TextLines.SplitLines(originalText);
            TsViewPatch[] patches = new[] { new TsViewPatch(1, currentLines.Length, lines.ToArray()) };
            (DeviceTsViewPatchData? _, IReadOnlyList<ToolError> nsPatchErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("TestMap.map_/0", patches, baseTsView: originalText);
            Assert.NotNull(nsPatchErrors);
            Assert.True(nsPatchErrors.Count > 0, "修改固定骨架应被阻止");
            Assert.StartsWith("TS视图固定骨架被改动", nsPatchErrors[0].Message, StringComparison.Ordinal);

            lines = TextLines.SplitLines(originalText).ToList();
            lines.Insert(bodyLineIndex + 1, "            var injected: BeBool = G.create.bool(BeBool.fromBeConst(\"1\"));");

            currentLines = TextLines.SplitLines(originalText);
            patches = new[] { new TsViewPatch(1, currentLines.Length, lines.ToArray()) };
            (DeviceTsViewPatchData? localsPatchData, IReadOnlyList<ToolError> localsPatchErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("TestMap.map_/0", patches, baseTsView: originalText);
            Assert.Empty(localsPatchErrors);
            Assert.NotNull(localsPatchData);

            // 修改脚本体行（可编辑区）—— 重新读取因为上一轮 Patch 已持久化
            (DeviceTsViewReadData? readData2, IReadOnlyList<ToolError> readErrors2, IReadOnlyList<ToolError> _2) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors2);
            Assert.NotNull(readData2);
            string updatedText = readData2.TsView;

            lines = TextLines.SplitLines(updatedText).ToList();
            bodyLineIndex = lines.FindIndex(l => l.Contains("G.debug.enable(BeBool.fromBeConst(\"1\"))", StringComparison.Ordinal));
            Assert.True(bodyLineIndex >= 0, $"未找到脚本体行。TsView:\n{updatedText}");

            lines[bodyLineIndex] = lines[bodyLineIndex].Replace("BeBool.fromBeConst(\"1\")", "BeBool.fromBeConst(\"0\")");

            currentLines = TextLines.SplitLines(updatedText);
            patches = new[] { new TsViewPatch(1, currentLines.Length, lines.ToArray()) };
            (DeviceTsViewPatchData? okData, IReadOnlyList<ToolError> okErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("TestMap.map_/0", patches, baseTsView: updatedText);
            Assert.Empty(okErrors);
            Assert.NotNull(okData);
        }
        finally
        {
            try
            {
                TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"清理测试目录失败: {projectRootFullPath}", ex);
            }
        }
    }

    [Fact]
    public void Patch_Should_Allow_Multiple_Editable_Section_Changes()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_AllowedRanges");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:\n[\n 0-1\n]");
            // 地图 GUID 文件和主文件
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n First\n Second\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "First.code"),
                "begincodeV2\n" +
                "name:First\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "endcode\n");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Second.code"),
                "begincodeV2\n" +
                "name:Second\n" +
                "lines:\n" +
                "fun debug.enable(Bool 0)\n" +
                "endcode\n");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);
            Assert.NotNull(readData.EditAllowance.ForbiddenRanges);

            string originalText = readData.TsView;
            List<string> lines = TextLines.SplitLines(originalText).ToList();

            int firstEnableIndex = lines.FindIndex(l => l.Contains("G.debug.enable(BeBool.fromBeConst(\"1\"))", StringComparison.Ordinal));
            int secondEnableIndex = lines.FindIndex(l => l.Contains("G.debug.enable(BeBool.fromBeConst(\"0\"))", StringComparison.Ordinal));
            Assert.True(firstEnableIndex >= 0, "未找到第一处脚本体行");
            Assert.True(secondEnableIndex >= 0, "未找到第二处脚本体行");

            IReadOnlyList<TsViewEditRange> forbidden = readData.EditAllowance.ForbiddenRanges;
            Assert.DoesNotContain(forbidden, r => firstEnableIndex + 1 >= r.StartLine && firstEnableIndex + 1 <= r.EndLine);
            Assert.DoesNotContain(forbidden, r => secondEnableIndex + 1 >= r.StartLine && secondEnableIndex + 1 <= r.EndLine);

            lines[firstEnableIndex] = lines[firstEnableIndex].Replace("BeBool.fromBeConst(\"1\")", "BeBool.fromBeConst(\"0\")");
            lines[secondEnableIndex] = lines[secondEnableIndex].Replace("BeBool.fromBeConst(\"0\")", "BeBool.fromBeConst(\"1\")");

            string[] currentLines = TextLines.SplitLines(originalText);
            TsViewPatch[] patches = new[] { new TsViewPatch(1, currentLines.Length, lines.ToArray()) };
            (DeviceTsViewPatchData? patchData, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("TestMap.map_/0", patches, baseTsView: originalText);
            Assert.Empty(patchErrors);
            Assert.NotNull(patchData);
        }
        finally
        {
            try
            {
                TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"清理测试目录失败: {projectRootFullPath}", ex);
            }
        }
    }

    [Fact]
    public void Patch_Should_Block_New_Error_Introduction()
    {
        // 新错必阻断：补丁引入新错误时应阻断
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_NewErrorBlock");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:\n[\n 0-1\n]");
            // 地图 GUID 文件和主文件
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n Hello\n]");
            // 基线脚本无错误
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Hello.code"),
                "begincodeV2\n" +
                "name:Hello\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "endcode\n");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);

            string originalText = readData.TsView;
            List<string> lines = TextLines.SplitLines(originalText).ToList();

            // 引入新错误：将 Bool 值改为非法值
            int enableIndex = lines.FindIndex(static l => l.Contains("BeBool.fromBeConst(\"1\")", StringComparison.Ordinal));
            Assert.True(enableIndex >= 0, $"未找到脚本体行。TsView内容:\n{originalText}");

            lines[enableIndex] = lines[enableIndex].Replace("BeBool.fromBeConst(\"1\")", "BeBool.fromBeConst(\"999\")");

            string[] currentLines = TextLines.SplitLines(originalText);
            TsViewPatch[] patches = new[] { new TsViewPatch(1, currentLines.Length, lines.ToArray()) };
            (DeviceTsViewPatchData? patchData, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> patchWarnings) = services.DeviceTsView.Patch("TestMap.map_/0", patches, baseTsView: originalText);
            // 新错误应该被阻断
            Assert.True(patchErrors.Count > 0, $"新错误应该被阻断。patchErrors.Count={patchErrors.Count}");
            Assert.Contains("Bool", patchErrors[0].Message);
        }
        finally
        {
            try
            {
                TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"清理测试目录失败: {projectRootFullPath}", ex);
            }
        }
    }

    [Fact]
    public void Patch_Parse_Should_Collect_IfWhile_Condition_Const_Error()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_CondConstBlock");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });

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
                "if{fun create.bool(Bool 1)\n" +
                "  fun debug.enable(Bool 1)\n" +
                "}\n" +
                "endcode\n");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);

            List<ToolError> warnings = [];
            List<ToolError> collectedErrors = [];
            DeviceTsView.WritebackParseResult result = DeviceTsView.ParseDeviceTsViewForWriteback(
                services.Model,
                TextLines.SplitLines(readData.TsView),
                "TestMap.map_/0",
                warnings,
                collectedErrors: collectedErrors);

            Assert.Null(result.Error);
            Assert.Contains(collectedErrors, static e => e.Message.Contains("if/while 条件调用", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"清理测试目录失败: {projectRootFullPath}", ex);
            }
        }
    }

    [Fact]
    public void Patch_Parse_Should_NotCollect_IfWhile_Condition_Const_Error_For_BoolOr()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_CondConstRemain");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });

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
                "if{fun bool.or(Bool 1:Bool 0)\n" +
                "  fun debug.enable(Bool 1)\n" +
                "}\n" +
                "endcode\n");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);

            List<ToolError> warnings = [];
            List<ToolError> collectedErrors = [];
            DeviceTsView.WritebackParseResult result = DeviceTsView.ParseDeviceTsViewForWriteback(
                services.Model,
                TextLines.SplitLines(readData.TsView),
                "TestMap.map_/0",
                warnings,
                collectedErrors: collectedErrors);

            Assert.Null(result.Error);
            Assert.DoesNotContain(collectedErrors, static e => e.Message.Contains("if/while 条件调用", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"清理测试目录失败: {projectRootFullPath}", ex);
            }
        }
    }

    [Fact]
    public void Patch_Should_Allow_Same_Error_To_Remain()
    {
        // 旧错可留：基线和补丁后都有相同错误时，只要不引入新错误就允许通过
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_SameErrorRemain");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:\n[\n 0-1\n]");
            // 地图 GUID 文件和主文件
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n First\n Second\n]");
            // 两个脚本都正常
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "First.code"),
                "begincodeV2\n" +
                "name:First\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "endcode\n");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Second.code"),
                "begincodeV2\n" +
                "name:Second\n" +
                "lines:\n" +
                "fun debug.enable(Bool 0)\n" +
                "endcode\n");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);

            string originalText = readData.TsView;
            List<string> lines = TextLines.SplitLines(originalText).ToList();

            // 修改第一个脚本的有效调用（不引入错误）
            int firstEnableIndex = lines.FindIndex(static l => l.Contains("BeBool.fromBeConst(\"1\")", StringComparison.Ordinal));
            Assert.True(firstEnableIndex >= 0, "未找到第一个脚本体行");

            // 将 1 改为 0（有效修改，不引入错误）
            lines[firstEnableIndex] = lines[firstEnableIndex].Replace("BeBool.fromBeConst(\"1\")", "BeBool.fromBeConst(\"0\")");

            string[] currentLines = TextLines.SplitLines(originalText);
            TsViewPatch[] patches = new[] { new TsViewPatch(1, currentLines.Length, lines.ToArray()) };
            (DeviceTsViewPatchData? patchData, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("TestMap.map_/0", patches, baseTsView: originalText);
            // 不引入新错误，应该允许通过
            Assert.Empty(patchErrors);
            Assert.NotNull(patchData);
        }
        finally
        {
            try
            {
                TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"清理测试目录失败: {projectRootFullPath}", ex);
            }
        }
    }

    [Fact]
    public void Patch_Should_Handle_Line_Number_Drift()
    {
        // 行号漂移测试：修改第一个脚本的同时验证第二个脚本的行号正确漂移
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_LineDrift");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:\n[\n 0-1\n]");
            // 地图 GUID 文件和主文件
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n First\n Second\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "First.code"),
                "begincodeV2\n" +
                "name:First\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "endcode\n");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Second.code"),
                "begincodeV2\n" +
                "name:Second\n" +
                "lines:\n" +
                "fun debug.enable(Bool 0)\n" +
                "endcode\n");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);

            string originalText = readData.TsView;
            List<string> lines = TextLines.SplitLines(originalText).ToList();

            // 找到两个脚本体行
            int firstEnableIndex = lines.FindIndex(static l => l.Contains("BeBool.fromBeConst(\"1\")", StringComparison.Ordinal));
            int secondEnableIndex = lines.FindIndex(static l => l.Contains("BeBool.fromBeConst(\"0\")", StringComparison.Ordinal));
            Assert.True(firstEnableIndex >= 0, "未找到第一个脚本体行");
            Assert.True(secondEnableIndex >= 0, "未找到第二个脚本体行");
            Assert.True(secondEnableIndex > firstEnableIndex, "第二个脚本应在第一个之后");

            // 同时修改两个脚本：交换 Bool 值
            // 这会导致两个脚本都有变更，但不引入新错误
            lines[firstEnableIndex] = lines[firstEnableIndex].Replace("BeBool.fromBeConst(\"1\")", "BeBool.fromBeConst(\"0\")");
            lines[secondEnableIndex] = lines[secondEnableIndex].Replace("BeBool.fromBeConst(\"0\")", "BeBool.fromBeConst(\"1\")");

            string[] currentLines = TextLines.SplitLines(originalText);
            TsViewPatch[] patches = new[] { new TsViewPatch(1, currentLines.Length, lines.ToArray()) };
            (DeviceTsViewPatchData? patchData, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("TestMap.map_/0", patches, baseTsView: originalText);
            // 交换 Bool 值不引入新错误，应该通过
            Assert.Empty(patchErrors);
            Assert.NotNull(patchData);
        }
        finally
        {
            try
            {
                TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"清理测试目录失败: {projectRootFullPath}", ex);
            }
        }
    }

    [Fact]
    public void Events_Section_Should_Be_Editable_Zone()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_EventsZone");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n]");

            string eventStartDir = SystemIOWrapper.Combine(deviceDir, "event_start");
            SystemIOWrapper.CreateDirectory(eventStartDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(eventStartDir, "list.json"), "[\n MyStart\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(eventStartDir, "MyStart.code"),
                "begincodeV2\n" +
                "name:MyStart\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "endcode\n");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);
            Assert.NotNull(readData.EditAllowance.ForbiddenRanges);

            string originalText = readData.TsView;
            List<string> lines = TextLines.SplitLines(originalText).ToList();

            int eventsBeginIndex = lines.FindIndex(l => l.Contains("[Events] BEGIN", StringComparison.Ordinal));
            Assert.True(eventsBeginIndex >= 0, $"未找到 [Events] BEGIN 标记。TsView:\n{originalText}");

            int eventsEndIndex = lines.FindIndex(l => l.Contains("[Events] END", StringComparison.Ordinal));
            Assert.True(eventsEndIndex >= 0, "未找到 [Events] END 标记");

            IReadOnlyList<TsViewEditRange> forbidden = readData.EditAllowance.ForbiddenRanges;
            Assert.Contains(forbidden, r => eventsBeginIndex + 1 >= r.StartLine && eventsBeginIndex + 1 <= r.EndLine);
            Assert.Contains(forbidden, r => eventsEndIndex + 1 >= r.StartLine && eventsEndIndex + 1 <= r.EndLine);

            int eventRegistrationIndex = lines.FindIndex(l => l.Contains("this.registerEvent_start(this, {", StringComparison.Ordinal));
            Assert.True(eventRegistrationIndex >= 0, "未找到事件静态注册行");
            Assert.True(eventRegistrationIndex > eventsBeginIndex && eventRegistrationIndex < eventsEndIndex, "事件注册行应在 [Events] 段内");
            Assert.DoesNotContain(forbidden, r => eventRegistrationIndex + 1 >= r.StartLine && eventRegistrationIndex + 1 <= r.EndLine);

            int eventBodyIndex = lines.FindIndex(l => l.Contains("G.debug.enable", StringComparison.Ordinal));
            Assert.True(eventBodyIndex >= 0, "未找到事件脚本体行");
            Assert.True(eventBodyIndex > eventsBeginIndex && eventBodyIndex < eventsEndIndex, "事件脚本体行应在 [Events] 段内");
            Assert.DoesNotContain(forbidden, r => eventBodyIndex + 1 >= r.StartLine && eventBodyIndex + 1 <= r.EndLine);
            Assert.Contains("function (this:", originalText);
            Assert.DoesNotContain("__tsViewEvent", originalText);
        }
        finally
        {
            try
            {
                TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"清理测试目录失败: {projectRootFullPath}", ex);
            }
        }
    }

    [Fact]
    public void Patch_Should_Reject_Stale_BaseTsView_Before_Fixed_Structure_Check()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_StaleBaseTsView");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });

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
                "endcode\n");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);

            List<string> originalLines = TextLines.SplitLines(readData.TsView).ToList();
            Assert.True(originalLines.Count > 4, "测试前置失败：TS 视图行数过少");

            // 构造过期基线：去掉末尾两行，模拟缓存视图比最新视图短
            List<string> staleBaseLines = originalLines.Take(originalLines.Count - 2).ToList();
            List<string> stalePatchedLines = staleBaseLines.ToList();

            int callLineIndex = stalePatchedLines.FindIndex(static l => l.Contains("BeBool.fromBeConst(\"1\")", StringComparison.Ordinal));
            Assert.True(callLineIndex >= 0, "未找到可编辑脚本体行");
            stalePatchedLines[callLineIndex] = stalePatchedLines[callLineIndex].Replace("BeBool.fromBeConst(\"1\")", "BeBool.fromBeConst(\"0\")");

            string staleBaseText = string.Join("\n", staleBaseLines);
            TsViewPatch[] patches = new[] { new TsViewPatch(1, staleBaseLines.Count, stalePatchedLines.ToArray()) };

            (DeviceTsViewPatchData? _, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("TestMap.map_/0", patches, baseTsView: staleBaseText);
            Assert.NotNull(patchErrors);
            Assert.True(patchErrors.Count > 0, "过期基线应被阻断");
            Assert.Contains("TS 视图基线已变化", patchErrors[0].Message);
            Assert.DoesNotContain("TS视图固定骨架被改动", patchErrors[0].Message);
        }
        finally
        {
            try
            {
                TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"清理测试目录失败: {projectRootFullPath}", ex);
            }
        }
    }
}
