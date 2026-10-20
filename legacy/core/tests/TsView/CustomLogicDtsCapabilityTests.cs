using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.TsView;

/// <summary>
/// custom-logic.d.ts 中零件实例 interface 按能力 extends 的端到端测试。
/// 验证：具备 Collidable 能力的零件 → extends ICollidable；不具备的 → 不 extends。
/// 能力判定复用 DeviceCapabilityResolver。
/// </summary>
[Collection("WorkspaceState")]
public sealed class CustomLogicDtsCapabilityTests
{
    // ════════════════════════════════════════════════════════════════════
    // Device 基类规则：stickToBone 决定
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void CustomLogicDts_Capable_Device_Extends_ICollidable()
    {
        // 普通 type=1 在 Map 中 stickToBone 空 → HasCapability(Collidable) = true
        string dts = BuildDtsForSingleDeviceInMap(deviceType: 1, stickToBone: null);

        Assert.Contains("export interface Device_anonymous_0 extends DeviceType_1, ICollidable", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomLogicDts_StickToBone_NonEmpty_Does_Not_Extend_ICollidable()
    {
        // 普通 type=1 绑定骨骼时无 Collidable
        string dts = BuildDtsForSingleDeviceInMap(deviceType: 1, stickToBone: "bone01");
        Assert.DoesNotContain("ICollidable", dts, StringComparison.Ordinal);
    }

    // ════════════════════════════════════════════════════════════════════
    // AlwaysNoRigidbodyTypes：恒不 extends
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void CustomLogicDts_BirthBuilding_Does_Not_Extend_ICollidable()
    {
        string dts = BuildDtsForSingleDeviceInMap(deviceType: DeviceTypeIds.BirthBuilding, stickToBone: null);
        // BirthBuilding 在 CustomLogicDts 中是外壳声明：仅 extends DeviceType_1110
        Assert.Contains("export interface Device_anonymous_0 extends DeviceType_1110 {", dts, StringComparison.Ordinal);
        Assert.DoesNotContain("ICollidable", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomLogicDts_SLGLogicDevice_Does_Not_Extend_ICollidable()
    {
        string dts = BuildDtsForSingleDeviceInMap(deviceType: DeviceTypeIds.SLGLogicDevice, stickToBone: null);
        Assert.DoesNotContain("ICollidable", dts, StringComparison.Ordinal);
    }

    // ════════════════════════════════════════════════════════════════════
    // 载体与骨骼绑定条件 (PosDevice / TriggerBuilding)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void CustomLogicDts_PosDevice_InMap_Does_Not_Extend_ICollidable()
    {
        // PosDevice 在地图内无 Collidable
        string dts = BuildDtsForSingleDeviceInMap(deviceType: DeviceTypeIds.PosDevice, stickToBone: null);
        Assert.DoesNotContain("ICollidable", dts, StringComparison.Ordinal);
    }

    // ════════════════════════════════════════════════════════════════════
    // 载体条件 (TextBuilding / NoColliderBuilding)
    // 普通机械内的碰撞能力与骨骼绑定无关
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void CustomLogicDts_TextBuilding_InMap_Does_Not_Extend_ICollidable()
    {
        // TextBuilding 在地图内无 Collidable
        string dts = BuildDtsForSingleDeviceInMap(deviceType: DeviceTypeIds.TextBuilding, stickToBone: null);
        Assert.DoesNotContain("ICollidable", dts, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomLogicDts_TextBuilding_InMech_Extends_ICollidable_RegardlessOfStickToBone()
    {
        // TextBuilding 在普通机械内具备 Collidable，与骨骼绑定无关
        // 关键回归：修复前 stickToBone 非空时错误地不 extends
        string dtsEmpty = BuildDtsForSingleDeviceInMech(DeviceTypeIds.TextBuilding, stickToBone: null);
        string dtsWithBone = BuildDtsForSingleDeviceInMech(DeviceTypeIds.TextBuilding, stickToBone: "skull");

        Assert.Contains("ICollidable", dtsEmpty, StringComparison.Ordinal);
        Assert.Contains("ICollidable", dtsWithBone, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomLogicDts_NoColliderBuilding_AllVariants_FollowTextBuildingRule()
    {
        // 各类型使用相同的载体条件
        int[] noColliderIds =
        [
            DeviceTypeIds.NoColliderBuilding_1030,
            DeviceTypeIds.NoColliderBuilding_1032,
            DeviceTypeIds.NoColliderBuilding_1058,
            DeviceTypeIds.NoColliderBuilding_1061,
        ];

        foreach (int id in noColliderIds)
        {
            // Map 中不 extends
            Assert.DoesNotContain("ICollidable", BuildDtsForSingleDeviceInMap(id, null), StringComparison.Ordinal);
            // Mech 中即使 stickToBone 非空也 extends
            Assert.Contains("ICollidable", BuildDtsForSingleDeviceInMech(id, "bone"), StringComparison.Ordinal);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // 辅助：构建单零件 DTS
    // ════════════════════════════════════════════════════════════════════

    private static string BuildDtsForSingleDeviceInMap(int deviceType, string? stickToBone)
        => BuildDtsForSingleDevice(deviceType, stickToBone, carrierKind: "Map");

    private static string BuildDtsForSingleDeviceInMech(int deviceType, string? stickToBone)
        => BuildDtsForSingleDevice(deviceType, stickToBone, carrierKind: "Mech");

    private static string BuildDtsForSingleDevice(int deviceType, string? stickToBone, string carrierKind)
    {
        string projectRootFullPath = TestTempDirectoryManager.CreateDirectory("MechToolkit_CapabilityDts_" + Guid.NewGuid().ToString("N"));

        try
        {
            string mapGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            string mechGuid = "cccccccccccccccccccccccccccccccc";
            string carrierGuid = carrierKind == "Map" ? mapGuid : mechGuid;

            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(projectRootFullPath, "setting.json"),
                "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:" + mapGuid);

            // setting.json 的 mainMap 必须指向一个真实存在的 map，否则 ProjectModel 加载失败。
            // 即使是 Mech 场景测试，也需要先准备一个空的 mainMap 作为占位。
            string mapDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map_");
            SystemIOWrapper.CreateDirectory(mapDir);
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:\n[\n]");
            SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map.guid"), mapGuid);
            SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMap.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });

            if (carrierKind == "Map")
            {
                // Map 场景：把目标零件写入 mainMap
                WriteDeviceLayout(projectRootFullPath, mapDir, "0", deviceType, stickToBone);
                SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "list:\n[\n 0-1\n]");
                SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "list:\n[\n 0-1\n]");
            }
            else
            {
                // Mech 场景：mainMap 保留为空，目标零件写入独立 mech
                SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRootFullPath, "TestMech.mech"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
                string mechDir = SystemIOWrapper.Combine(projectRootFullPath, "TestMech.mech_");
                SystemIOWrapper.CreateDirectory(mechDir);
                SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mechDir, "mech.json"), "list:\n[\n 0-1\n]");
                SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRootFullPath, "TestMech.mech.guid"), mechGuid);

                WriteDeviceLayout(projectRootFullPath, mechDir, "0", deviceType, stickToBone);
            }

            using BrowserRenderService browser = new(1, Array.Empty<int>());
            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);

            return CustomLogicTsViewDeclarationLibrary.GetDtsText(services.Model);
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

    private static void WriteDeviceLayout(string projectRootFullPath, string carrierDir, string deviceId, int deviceType, string? stickToBone)
    {
        string deviceDir = SystemIOWrapper.Combine(carrierDir, deviceId);
        SystemIOWrapper.CreateDirectory(deviceDir);
        string stickField = stickToBone is null ? string.Empty : $"\"stickToBone\":\"{stickToBone}\",";
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(deviceDir, "device.json"),
            $"{{\"type\":{deviceType},{stickField}\"ext\":{{}}}}");

        // 提供至少一个普通脚本以满足 BuildCustomLogicDts 的非空检查
        string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
        SystemIOWrapper.CreateDirectory(scriptsDir);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "list.json"), "[\n Hello\n]");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "Hello.code"),
            "begincodeV2\nname:Hello\nlines:\nfun debug.enable(Bool 1)\nendcode\n");
    }
}
