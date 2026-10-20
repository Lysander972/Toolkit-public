using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.TsView;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.TsView;

/// <summary>
/// DeviceTsView 基础测试 - 使用新基础设施。
/// 统一入口：ProjectModel + ProjectLogicContext + ToolkitServices
/// </summary>
[Collection("WorkspaceState")]
public sealed class DeviceTsViewBasicTests
{
    [Fact]
    public void DeviceTsView_Can_Be_Created_With_New_Infrastructure()
    {
        // Arrange - 使用真实测试工程
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);
        ProjectModel model = services.Model;

        // 需要实体才能进行逻辑分析
        if (model.Root.Children.Count == 0)
        {
            return; // 跳过空工程
        }

        // Act & Assert
        Assert.NotNull(services.DeviceTsView);
    }

    [Fact]
    public void DeviceTsView_Can_Build_For_Device()
    {
        // Arrange
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);
        ProjectModel model = services.Model;

        if (model.Root.Children.Count == 0)
        {
            return;
        }

        // 获取第一个零件（使用新模型 API）
        DeviceEntity? device = model.EnumerateEntitiesUnderPath<DeviceEntity>(WorkspacePath.RootDirectory).FirstOrDefault();
        if (device == null)
        {
            return;
        }

        // Act - 新模型：使用 Read 方法
        (DeviceTsViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = services.DeviceTsView.Read(device.实体_完整相对路径标识);

        // Assert
        Assert.NotNull(data);
        Assert.NotNull(data.TsView);
    }

    [Fact]
    public void DeviceTsView_ReturnMetadata_ShouldUseBePrefixedTypeName()
    {
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);

        (DeviceTsViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("map.map_/54");

        Assert.Empty(errors);
        Assert.NotNull(data);
        Assert.Contains("public IsInGame = BeScript({ name: \"IsInGame\", retVar: \"ret\", retVarType: \"BeBool\" })", data.TsView, StringComparison.Ordinal);
        foreach (string line in TextLines.SplitLines(data.TsView).Where(static line => line.Contains("retVarType:", StringComparison.Ordinal)))
        {
            Assert.Contains("retVarType: \"Be", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DeviceTsView_ReturnSignature_ShouldSurviveNoopPatch()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_DeviceTsViewReturnSignatureRoundtrip");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\n" +
                "name:test");

            string mapGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), mapGuid);
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:[]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n GetProgress\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "GetProgress.code"),
                "begincodeV2\n" +
                "name:GetProgress\n" +
                "return:Float ret\n" +
                "lines:\n" +
                "Float limit=fun create.float(Float 10)\n" +
                "Float ret=fun float.division(Float 5:var limit)\n" +
                "endcode\n");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? data, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");

            Assert.Empty(readErrors);
            Assert.NotNull(data);
            Assert.Contains("public GetProgress = BeScript({ name: \"GetProgress\", retVar: \"ret\", retVarType: \"BeFloat\" })", data.TsView, StringComparison.Ordinal);
            Assert.DoesNotContain("repo: guid_", data.TsView, StringComparison.Ordinal);
            Assert.Contains("var ret: BeFloat", data.TsView, StringComparison.Ordinal);
            Assert.Contains("return ret;", data.TsView, StringComparison.Ordinal);

            string[] lines = TextLines.SplitLines(data.TsView);
            TsViewPatch patch = new(1, lines.Length, lines);
            (DeviceTsViewPatchData? patchData, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> _) =
                services.DeviceTsView.Patch("TestMap.map_/0", new[] { patch }, baseTsView: data.TsView, persistChanges: false);

            if (patchErrors.Count > 0)
            {
                string allErrors = string.Join("\n", patchErrors.Select(static e => $"{e.Path}:{e.Line}: {e.Message}"));
                Assert.Fail($"返回签名 no-op patch 不应产生错误:\n{allErrors}");
            }

            Assert.NotNull(patchData);
        }
        finally
        {
            TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
        }
    }

    [Fact]
    public void DeviceTsView_ShouldKeepBlankLinesAndReadableUnicodeStringLiterals()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_DeviceTsViewFormattingRegression");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\n" +
                "name:test");

            string mapGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), mapGuid);
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:[]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");

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
                "String label=fun create.string(String 称号区域)\n" +
                "\n" +
                "fun debug.enable(Bool 1)\n" +
                "endcode\n");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");

            Assert.Empty(errors);
            Assert.NotNull(data);
            Assert.Contains("BeString.fromBeConst(\"称号区域\")", data.TsView, StringComparison.Ordinal);
            Assert.DoesNotContain("\\u79F0\\u53F7\\u533A\\u57DF", data.TsView, StringComparison.Ordinal);

            string[] tsLines = TextLines.SplitLines(data.TsView);
            int stringLineIndex = Array.FindIndex(tsLines, static line => line.Contains("BeString.fromBeConst(\"称号区域\")", StringComparison.Ordinal));
            Assert.True(stringLineIndex >= 0, "未找到中文字符串字面量输出行。");
            Assert.True(stringLineIndex + 2 < tsLines.Length, "中文字符串字面量后缺少后续脚本行。");
            Assert.Equal(string.Empty, tsLines[stringLineIndex + 1]);
            Assert.Contains("G.debug.enable(BeBool.fromBeConst(\"1\"));", tsLines[stringLineIndex + 2], StringComparison.Ordinal);

            int startRegistrationLineIndex = Array.FindIndex(tsLines, static line => line.Contains("this.registerEvent_start(this,", StringComparison.Ordinal));
            int updateRegistrationLineIndex = Array.FindIndex(tsLines, static line => line.Contains("this.registerEvent_update(this,", StringComparison.Ordinal));
            Assert.True(startRegistrationLineIndex >= 0, "未找到 start 事件注册片段。");
            Assert.True(updateRegistrationLineIndex > startRegistrationLineIndex, "未找到 start 后面的 update 事件注册片段。");
            Assert.Contains(string.Empty, tsLines[(startRegistrationLineIndex + 1)..updateRegistrationLineIndex]);
        }
        finally
        {
            TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
        }
    }

    [Fact]
    public void DeviceTsView_ShouldNotEmitBlockClosingLinesAsBlankLines()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_DeviceTsViewBlockClosingBlankLines");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\n" +
                "name:test");

            string mapGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), mapGuid);
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:[]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n Tick\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Tick.code"),
                "begincodeV2\n" +
                "name:Tick\n" +
                "lines:\n" +
                "Float i=fun create.float(Float 0)\n" +
                "List keep=fun create.list()\n" +
                "while{fun float.lt(var i:Float 3)\n" +
                "  if{fun float.gte(var i:Float 1)\n" +
                "    fun debug.enable(Bool 1)\n" +
                "  }\n" +
                "  Float i=fun float.add(var i:Float 1)\n" +
                "}\n" +
                "List pseudoFxList=varf List.keep.ref()\n" +
                "endcode\n");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");

            Assert.Empty(errors);
            Assert.NotNull(data);

            string[] tsLines = TextLines.SplitLines(data.TsView);
            int afterWhileLineIndex = Array.FindIndex(tsLines, static line => line.Contains("keep.ref();", StringComparison.Ordinal));
            Assert.True(afterWhileLineIndex >= 0, "未找到 while 后的顶层语句。");
            Assert.Contains("}", tsLines[afterWhileLineIndex - 1], StringComparison.Ordinal);
        }
        finally
        {
            TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
        }
    }

    [Fact]
    public void DeviceTsView_ShouldNotEmitInlineCanvasLinesAsBlankLines()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_DeviceTsViewInlineCanvasBlankLines");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\n" +
                "name:test");

            string mapGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), mapGuid);
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:[]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");

            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:1");

            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n CreateUi\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "CreateUi.code"),
                "begincodeV2\n" +
                "name:CreateUi\n" +
                "return:Canvas ret\n" +
                "lines:\n" +
                "Canvas ret=fun create.canvas(Canvas ==hjson@1ui_root@1\n" +
                "[\n" +
                "]" +
                "==)\n" +
                "fun ui.X_2dmode(Bool 1:Float 1)\n" +
                "fun break()\n" +
                "endcode\n");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");

            Assert.Empty(errors);
            Assert.NotNull(data);

            string[] tsLines = TextLines.SplitLines(data.TsView);
            int afterCanvasLineIndex = Array.FindIndex(tsLines, static line => line.Contains("G.ui.X_2dmode", StringComparison.Ordinal));
            Assert.True(afterCanvasLineIndex >= 0, "未找到 Canvas 后的语句。");
            Assert.Contains("`));", tsLines[afterCanvasLineIndex - 1], StringComparison.Ordinal);
        }
        finally
        {
            TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
        }
    }

    [Fact]
    public void DeviceTsView_DeviceBaseType_ShouldNotEmitFuncDeviceEvents()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_DeviceTsViewDeviceBaseEvents");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        try
        {
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\n" +
                "name:test");

            string mapGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), mapGuid);
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:[]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
            string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
            SystemIOWrapper.CreateDirectory(deviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "device.json"), "type:200");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(deviceDir, "vars.json"), "[]");
            string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[]");

            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
            (DeviceTsViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/0");

            Assert.Empty(errors);
            Assert.NotNull(data);
            Assert.Contains("extends DeviceType_200", data.TsView, StringComparison.Ordinal);
            Assert.DoesNotContain("@events(", data.TsView, StringComparison.Ordinal);
            Assert.DoesNotContain("registerEvent_start", data.TsView, StringComparison.Ordinal);
            Assert.DoesNotContain("registerEvent_update", data.TsView, StringComparison.Ordinal);
        }
        finally
        {
            TestTempDirectoryManager.TryDeleteDirectory(projectRootFullPath);
        }
    }

    [Fact]
    public void BirthBuilding_SelfType_ShouldUseTargetCore_When_TargetMechHasSameDeviceId()
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_BirthBuildingSelfIdCollision");

        try
        {
            string mapGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            string mechGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\n" +
                "name:test\n" +
                "mainMap:" + mapGuid);

            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), mapGuid);
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:\n[\n 2-1110\n 3-328\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 2-1110\n 3-328\n]");

            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TargetMech.mech.guid"), mechGuid);
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TargetMech.mech"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            string mechDir = SystemIOWrapper.Combine(projectRootFullPath, "TargetMech.mech_");
            SystemIOWrapper.CreateDirectory(mechDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mechDir, "mech.json"), "list:\n[\n 0-1\n 2-328\n]");

            string coreDeviceDir = SystemIOWrapper.Combine(mechDir, "0");
            SystemIOWrapper.CreateDirectory(coreDeviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(coreDeviceDir, "device.json"), "type:1\next:\n{\n logicName:Core\n}");

            string collisionDeviceDir = SystemIOWrapper.Combine(mechDir, "2");
            SystemIOWrapper.CreateDirectory(collisionDeviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(collisionDeviceDir, "device.json"), "type:328\next:\n{\n logicName:Constraint\n}");

            string birthDeviceDir = SystemIOWrapper.Combine(mapDir, "2");
            SystemIOWrapper.CreateDirectory(birthDeviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(birthDeviceDir, "device.json"),
                "type:1110\n" +
                "ext:\n" +
                "{\n" +
                " logicName:Spawner\n" +
                " mechFileGUID:" + mechGuid + "\n" +
                "}");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(birthDeviceDir, "vars.json"),
                "[\n" +
                " {\n" +
                "  name:createdTime\n" +
                "  type:Float\n" +
                " }\n" +
                "]");

            string mapOnlyDeviceDir = SystemIOWrapper.Combine(mapDir, "3");
            SystemIOWrapper.CreateDirectory(mapOnlyDeviceDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapOnlyDeviceDir, "device.json"), "type:328\next:\n{\n logicName:MapOnly\n}");

            string scriptsDir = SystemIOWrapper.Combine(birthDeviceDir, "scripts");
            SystemIOWrapper.CreateDirectory(scriptsDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n CheckSelf\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "CheckSelf.code"),
                "begincodeV2\n" +
                "name:CheckSelf\n" +
                "lines:\n" +
                "Device dev=act 1.this.getDevice()\n" +
                "Device mapDevByName=act 328./MapOnly.getDevice()\n" +
                "Device mapDevById=act 328.3.getDevice()\n" +
                "endcode\n");

            string eventDir = SystemIOWrapper.Combine(birthDeviceDir, "event_mechStart");
            SystemIOWrapper.CreateDirectory(eventDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(eventDir, "list.json"), "[\n Start\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(eventDir, "Start.code"),
                "begincodeV2\n" +
                "name:Start\n" +
                "lines:\n" +
                "Float createdTime=fun time.time()\n" +
                "endcode\n");

            using BrowserRenderService browser = new(1, Array.Empty<int>());
            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);

            (DeviceTsViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("TestMap.map_/2");

            Assert.Empty(errors);
            Assert.NotNull(data);
            string tsView = data.TsView;
            Assert.Contains("class Device_Spawner_2_VirtualCore extends Mech_" + mechGuid + ".Device_Core_0", tsView, StringComparison.Ordinal);
            Assert.Contains("Act.self<Device_Spawner_2_VirtualCore>(this).getDevice()", tsView, StringComparison.Ordinal);
            Assert.Contains("function (this: Device_Spawner_2_VirtualCore)", tsView, StringComparison.Ordinal);
            Assert.Contains("this.createdTime = G.time.time();", tsView, StringComparison.Ordinal);
            Assert.DoesNotContain("Device_Constraint_2_VirtualCore", tsView, StringComparison.Ordinal);
            Assert.DoesNotContain("Device_MapOnly_3", tsView, StringComparison.Ordinal);
            Assert.Contains("Act.byName<DeviceType_328>(\"MapOnly\").getDevice()", tsView, StringComparison.Ordinal);
            Assert.Contains("Act.byId<DeviceType_328>(3).getDevice()", tsView, StringComparison.Ordinal);
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
