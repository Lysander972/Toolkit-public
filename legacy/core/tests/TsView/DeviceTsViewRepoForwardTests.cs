using Xunit;
using Xunit.Abstractions;
using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.TsView;

[Collection("WorkspaceState")]
public sealed class DeviceTsViewRepoForwardTests
{
    private readonly ITestOutputHelper _output;

    public DeviceTsViewRepoForwardTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void RepoForwarding_InDeviceTsView_ShouldBeValidated_OnPatch()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_DeviceTsViewRepoForward");

        try
        {
            // 创建 setting.json（项目必需）
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "{\n" +
                " guid: test00000000000000000000000001\n" +
                " name: TestProject\n" +
                "}");

            string guid = "0123456789abcdef0123456789abcdef";

            string codeDir = SystemIOWrapper.Combine(projectRootFullPath, "code", "Shared");
            SystemIOWrapper.CreateDirectory(codeDir);

            string codePath = SystemIOWrapper.Combine(codeDir, "Hello.code");
            SystemIOWrapper.WriteAllText(codePath,
                "begincodeV2\n" +
                "guid:" + guid + "\n" +
                "name:Hello\n" +
                "params:\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "endcode\n");
            SystemIOWrapper.WriteAllText(codePath + ".guid", guid);

            // 创建机械实体（三元素平级：.mech + .mech.guid + .mech_/）
            string mechGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMech.mech"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMech.mech.guid"), mechGuid);
            string mechDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMech.mech_");
            SystemIOWrapper.CreateDirectory(mechDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mechDir, "mech.json"), "list:\n[\n 0-1\n]");

            string deviceDir = SystemIOWrapper.Combine(mechDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"),
                "[\n" +
                " {\n" +
                "  guid:" + guid + "\n" +
                "  name:Hello\n" +
                " }\n" +
                "]");

            string evDir = SystemIOWrapper.Combine(deviceDir, "event_fixedUpdate");
            SystemIOWrapper.CreateDirectory(evDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(evDir, "list.json"),
                "[\n" +
                " {\n" +
                "  guid:" + guid + "\n" +
                "  name:Tick\n" +
                " }\n" +
                "]");

            using BrowserRenderService browser = new(1, Array.Empty<int>());
            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);

            (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMech.mech_/0");
            Assert.Empty(readErrors);
            Assert.NotNull(readData);

            string tsView = readData.TsView;
            _output.WriteLine("=== TsView ===");
            _output.WriteLine(tsView);
            _output.WriteLine("=== End TsView ===");

            Assert.Contains("repo: guid_" + guid, tsView);
            Assert.Contains("static {", tsView);
            Assert.Equal(1, TextLines.SplitLines(tsView).Count(l => l.Contains("static {", StringComparison.Ordinal)));
            Assert.Contains("this.registerEvent_fixedUpdate(this, {", tsView);
            Assert.Contains("Tick: BeScript(", tsView);
            Assert.DoesNotContain("BeScript<", tsView);
            Assert.Contains("function (", tsView);
            Assert.Contains("=> {", tsView);
            Assert.Contains("function (this:", tsView);
            Assert.DoesNotContain("__tsViewEvent", tsView);
            Assert.DoesNotContain("EventGroup<", tsView);

            // 伪造一个不存在的 GUID（替换 repo 字段值）
            string fakeGuid = "ffffffffffffffffffffffffffffffff";
            string modifiedText = tsView.Replace("repo: guid_" + guid, "repo: guid_" + fakeGuid);

            string[] currentLines = TextLines.SplitLines(readData.TsView);
            string[] patchedLines = TextLines.SplitLines(modifiedText);
            TsViewPatch[] patches = new[] { new TsViewPatch(1, currentLines.Length, patchedLines) };
            (DeviceTsViewPatchData? _, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("TestMech.mech_/0", patches, baseTsView: readData.TsView);

            Assert.True(patchErrors.Count > 0, "应拦截 GUID 转发目标不一致的修改");
            Assert.True(
                patchErrors[0].Message.Contains("未匹配到代码仓库脚本", StringComparison.Ordinal),
                $"错误消息应包含 GUID 不匹配提示，实际: {patchErrors[0].Message}");
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
