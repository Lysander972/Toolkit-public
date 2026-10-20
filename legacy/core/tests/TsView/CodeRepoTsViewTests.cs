using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.TsView;

[Collection("WorkspaceState")]
public sealed class CodeRepoTsViewTests
{
    [Fact]
    public void CodeRepoTsView_ShouldRoundTrip_WithStatementConverter()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_CodeRepoTsView");

        try
        {
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            string codeDir = SystemIOWrapper.Combine(projectRootFullPath, "code", "Shared");
            SystemIOWrapper.CreateDirectory(codeDir);

            string codePath = SystemIOWrapper.Combine(codeDir, "Hello.code");
            SystemIOWrapper.WriteAllText(codePath,
                "begincodeV2\n" +
                "name:Hello\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "endcode\n");

            string guid = "0123456789abcdef0123456789abcdef";
            SystemIOWrapper.WriteAllText(codePath + ".guid", guid);

            using BrowserRenderService browser = new(1, Array.Empty<int>());
            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);

            (CodeRepoTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.CodeRepoTsView.Read(services.Model);
            Assert.Empty(readErrors);
            Assert.NotNull(readData);

            string tsView = readData.TsView;
            Assert.Contains("/// <reference path=\"./sdk.ts\" />", tsView, StringComparison.Ordinal);
            Assert.Contains("namespace CodeRepo", tsView);
            Assert.Contains("export namespace Shared", tsView);
            Assert.Contains("export function Hello(): void", tsView);
            Assert.Contains("declare const guid_0123456789abcdef0123456789abcdef", tsView);
            Assert.Contains("//|fun debug.enable(Bool 1)", tsView);
            Assert.False(tsView.EndsWith('\n'), "TS 视图最后一行必须是内容行，不得以换行符结尾");

            CodeRepoTsViewScriptIndex scriptIndex = Assert.Single(readData.Scripts);
            Assert.Equal(guid, scriptIndex.Guid);
            Assert.Equal("code/Shared/Hello.code", scriptIndex.SourceRelativePath);
            Assert.Equal("CodeRepo.code.Shared.Hello", scriptIndex.Symbol);
            string[] tsLines = tsView.Split('\n');
            Assert.EndsWith("export function Hello(): void {", tsLines[scriptIndex.StartLine - 1], StringComparison.Ordinal);
            Assert.EndsWith("}", tsLines[scriptIndex.EndLine - 1], StringComparison.Ordinal);

            string updated = tsView.Replace("fun debug.enable(Bool 1)", "fun debug.enable(Bool 0)", StringComparison.Ordinal);
            (CodeRepoTsViewWriteData? _, IReadOnlyList<ToolError> writeErrors, IReadOnlyList<ToolError> _) = services.CodeRepoTsView.Write(updated);
            Assert.NotNull(writeErrors);
            Assert.True(writeErrors.Count > 0, "Write 应被禁止");
        }
        finally
        {
            try
            {
                TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void CodeRepoTsView_ShouldScanWholeWorkspace_ExcludingEntityDerivedDirs()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_CodeRepoTsView");

        try
        {
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            string repoDir = SystemIOWrapper.Combine(projectRootFullPath, "misc", "helpers");
            SystemIOWrapper.CreateDirectory(repoDir);

            string repoPath = SystemIOWrapper.Combine(repoDir, "Helper.code");
            SystemIOWrapper.WriteAllText(repoPath,
                "begincodeV2\n" +
                "name:Helper\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "endcode\n");
            SystemIOWrapper.WriteAllText(repoPath + ".guid", "fedcba9876543210fedcba9876543210");

            // 完整的地图三元素：demo.map + demo.map.guid + demo.map_/
            // 地图派生目录下的零件脚本应被 MapEntityBuilder 接管，不出现在 CodeRepo 视图中
            SystemIOWrapper.WriteAllBytes(
                SystemIOWrapper.Combine(projectRootFullPath, "demo.map"),
                new byte[] { 0x01, 0x02, 0x03, 0x04 });
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(projectRootFullPath, "demo.map.guid"),
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n");

            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "demo.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(mapDir, "demo.map.guid"),
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n");
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(mapDir, "map.json"), "{\"list\":[]}");
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(mapDir, "mech.json"), "{\"list\":[\"0-1001\"]}");

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0", "event_start");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(deviceDir, "device.json"),
                "type: 1001\next:\n{\nlogicName: Device\n}");
            string devicePath = SystemIOWrapper.Combine(deviceDir, "Device.code");
            SystemIOWrapper.WriteAllText(devicePath,
                "begincodeV2\n" +
                "name:Device\n" +
                "lines:\n" +
                "fun debug.enable(Bool 1)\n" +
                "endcode\n");

            using BrowserRenderService browser = new(1, Array.Empty<int>());
            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);

            (CodeRepoTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.CodeRepoTsView.Read(services.Model);
            Assert.Empty(readErrors);
            Assert.NotNull(readData);

            string tsView = readData.TsView;
            Assert.Contains("export namespace misc", tsView, StringComparison.Ordinal);
            Assert.Contains("export function Helper(): void", tsView, StringComparison.Ordinal);
            Assert.Contains("declare const guid_fedcba9876543210fedcba9876543210", tsView, StringComparison.Ordinal);
            Assert.DoesNotContain("demo", tsView, StringComparison.Ordinal);

            string updated = tsView.Replace("fun debug.enable(Bool 1)", "fun debug.enable(Bool 0)", StringComparison.Ordinal);
            (CodeRepoTsViewWriteData? _, IReadOnlyList<ToolError> writeErrors, IReadOnlyList<ToolError> _) = services.CodeRepoTsView.Write(updated);
            Assert.NotNull(writeErrors);
            Assert.True(writeErrors.Count > 0, "Write 应被禁止");
        }
        finally
        {
            try
            {
                TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
            }
            catch
            {
                // ignore
            }
        }
    }
}
