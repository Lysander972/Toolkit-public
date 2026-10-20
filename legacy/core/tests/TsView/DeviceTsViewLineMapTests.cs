using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.TsView;

[Collection("WorkspaceState")]
public sealed class DeviceTsViewLineMapTests
{
    [Fact]
    public void Read_Should_Populate_ScriptIndex_LineMap()
    {
        // 验证脚本索引中的 LineMap 正确映射 BES 行号到 TS 视图行号
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_LineMap");

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
            // 包含多行命令的脚本
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Hello.code"),
                "begincodeV2\n" +
                "name:Hello\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "fun debug.enable(Bool 0)\n" +
                "endcode\n");

            using BrowserRenderService browser = new(1, Array.Empty<int>());
            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);
            Assert.NotNull(readData.ScriptIndexes);
            Assert.NotEmpty(readData.ScriptIndexes);

            DeviceTsViewScriptIndex helloScript = readData.ScriptIndexes.First(static s => s.ScriptName == "Hello");
            Assert.True(helloScript.StartLine > 0, "脚本起始行号应大于 0");
            Assert.True(helloScript.EndLine >= helloScript.StartLine, "脚本结束行号应不小于起始行号");

            // 验证 LineMap 存在且有映射条目
            Assert.NotNull(helloScript.LineMap);
            Assert.NotEmpty(helloScript.LineMap);

            // 验证映射的 BES 行号和 TS 行号均有效
            foreach (TsViewLineMapping mapping in helloScript.LineMap)
            {
                Assert.True(mapping.BesLine > 0, "BES 行号应大于 0");
                Assert.True(mapping.TsLine >= helloScript.StartLine, "TS 行号应在脚本范围内");
                Assert.True(mapping.TsLine <= helloScript.EndLine, "TS 行号应在脚本范围内");
            }

            // 验证映射条目数量（至少有两行命令）
            Assert.True(helloScript.LineMap.Count >= 2, "LineMap 应至少包含两个命令行的映射");
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
