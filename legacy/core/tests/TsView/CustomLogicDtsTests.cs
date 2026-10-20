using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.TsView;

[Collection("WorkspaceState")]
public sealed class CustomLogicDtsTests
{
    [Fact]
    public void BuildCustomLogicDts_ShouldUseOfficialTsPrinter_Output()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_CustomLogicDts");

        try
        {
            string mapGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:" + mapGuid);

            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:\n[\n 0-1\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), mapGuid);
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n Hello\n GetProgress\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Hello.code"),
                "begincodeV2\n" +
                "name:Hello\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "endcode\n");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "GetProgress.code"),
                "begincodeV2\n" +
                "name:GetProgress\n" +
                "return:Float ret\n" +
                "lines:\n" +
                "Float ret=fun create.float(Float 1)\n" +
                "endcode\n");

            using BrowserRenderService browser = new(1, Array.Empty<int>());
            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);

            string dts = CustomLogicTsViewDeclarationLibrary.GetDtsText(services.Model);

            Assert.Contains("/// <reference path=\"./sdk.ts\" />", dts, StringComparison.Ordinal);
            Assert.Contains("declare namespace Map_" + mapGuid, dts, StringComparison.Ordinal);
            Assert.Contains("export interface Device_anonymous_0 extends DeviceType_1", dts, StringComparison.Ordinal);
            Assert.Contains("Hello(): void;", dts, StringComparison.Ordinal);
            Assert.Contains("GetProgress(): BeFloat;", dts, StringComparison.Ordinal);
            Assert.DoesNotContain("GetProgress(): BeDevice |", dts, StringComparison.Ordinal);
            Assert.DoesNotContain("GetProgress(): BeAny;", dts, StringComparison.Ordinal);
            Assert.False(dts.EndsWith('\n'), "声明文件最后一行必须是内容行，不得以换行符结尾");
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
