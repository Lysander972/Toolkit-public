using Xunit;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Tests.Sdk;

/// <summary>
/// DeviceCapabilityResolver 按零件类型、载体和骨骼绑定判断碰撞能力的静态单元测试。
/// </summary>
public sealed class DeviceCapabilityResolverTests
{
    // ════════════════════════════════════════════════════════════════════
    // AlwaysNoRigidbodyTypes：不具备碰撞能力的类型
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void BirthBuilding_Always_No_Collidable()
    {
        Assert.False(HasCap(CreateInMap(DeviceTypeIds.BirthBuilding)));
        Assert.False(HasCap(CreateInMech(DeviceTypeIds.BirthBuilding)));
    }

    [Fact]
    public void AreaBuilding_Always_No_Collidable()
    {
        Assert.False(HasCap(CreateInMap(DeviceTypeIds.AreaBuilding)));
        Assert.False(HasCap(CreateInMech(DeviceTypeIds.AreaBuilding)));
    }

    [Fact]
    public void StartBuilding_Always_No_Collidable()
    {
        Assert.False(HasCap(CreateInMap(DeviceTypeIds.StartBuilding)));
        Assert.False(HasCap(CreateInMech(DeviceTypeIds.StartBuilding)));
    }

    [Fact]
    public void ClientMapLogicDevice_Always_No_Collidable()
    {
        Assert.False(HasCap(CreateInMap(DeviceTypeIds.ClientMapLogicDevice)));
        Assert.False(HasCap(CreateInMech(DeviceTypeIds.ClientMapLogicDevice)));
    }

    [Fact]
    public void MapCoreDevice_Always_No_Collidable()
    {
        Assert.False(HasCap(CreateInMap(DeviceTypeIds.MapCoreDevice)));
    }

    [Fact]
    public void SLGLogicDevice_Always_No_Collidable()
    {
        Assert.False(HasCap(CreateInMap(DeviceTypeIds.SLGLogicDevice)));
        Assert.False(HasCap(CreateInMech(DeviceTypeIds.SLGLogicDevice)));
    }

    // ════════════════════════════════════════════════════════════════════
    // 普通零件的碰撞能力由骨骼绑定状态决定
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void PlainDevice_InMap_WithEmptyStickToBone_Has_Collidable()
    {
        Assert.True(HasCap(CreateInMap(deviceType: 1)));
    }

    [Fact]
    public void PlainDevice_InMap_WithStickToBone_No_Collidable()
    {
        Assert.False(HasCap(CreateInMap(deviceType: 1, stickToBone: "bone01")));
    }

    [Fact]
    public void PlainDevice_InMech_WithEmptyStickToBone_Has_Collidable()
    {
        Assert.True(HasCap(CreateInMech(deviceType: 1)));
    }

    [Fact]
    public void PlainDevice_InMech_WithStickToBone_No_Collidable()
    {
        Assert.False(HasCap(CreateInMech(deviceType: 1, stickToBone: "bone")));
    }

    // ════════════════════════════════════════════════════════════════════
    // 载体与骨骼绑定条件 (PosDevice / TriggerBuilding)：
    // 地图内无碰撞能力，普通机械内由骨骼绑定状态决定
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void PosDevice_InMap_No_Collidable_RegardlessOfStickToBone()
    {
        Assert.False(HasCap(CreateInMap(DeviceTypeIds.PosDevice)));
        Assert.False(HasCap(CreateInMap(DeviceTypeIds.PosDevice, stickToBone: "x")));
    }

    [Fact]
    public void PosDevice_InMech_WithEmptyStickToBone_Has_Collidable()
    {
        Assert.True(HasCap(CreateInMech(DeviceTypeIds.PosDevice)));
    }

    [Fact]
    public void PosDevice_InMech_WithStickToBone_No_Collidable()
    {
        Assert.False(HasCap(CreateInMech(DeviceTypeIds.PosDevice, stickToBone: "bone")));
    }

    [Fact]
    public void TriggerBuilding_InMap_No_Collidable_RegardlessOfStickToBone()
    {
        Assert.False(HasCap(CreateInMap(DeviceTypeIds.TriggerBuilding)));
        Assert.False(HasCap(CreateInMap(DeviceTypeIds.TriggerBuilding, stickToBone: "x")));
    }

    [Fact]
    public void TriggerBuilding_InMech_WithEmptyStickToBone_Has_Collidable()
    {
        Assert.True(HasCap(CreateInMech(DeviceTypeIds.TriggerBuilding)));
    }

    [Fact]
    public void TriggerBuilding_InMech_WithStickToBone_No_Collidable()
    {
        Assert.False(HasCap(CreateInMech(DeviceTypeIds.TriggerBuilding, stickToBone: "bone")));
    }

    // ════════════════════════════════════════════════════════════════════
    // 载体条件 (TextBuilding / NoColliderBuilding)：
    // 关键差异：在普通 Mech 内永远 true，与 stickToBone 无关
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void TextBuilding_InMap_No_Collidable_RegardlessOfStickToBone()
    {
        Assert.False(HasCap(CreateInMap(DeviceTypeIds.TextBuilding)));
        Assert.False(HasCap(CreateInMap(DeviceTypeIds.TextBuilding, stickToBone: "x")));
    }

    [Fact]
    public void TextBuilding_InMech_WithEmptyStickToBone_Has_Collidable()
    {
        Assert.True(HasCap(CreateInMech(DeviceTypeIds.TextBuilding)));
    }

    [Fact]
    public void TextBuilding_InMech_WithStickToBone_Still_Has_Collidable()
    {
        Assert.True(HasCap(CreateInMech(DeviceTypeIds.TextBuilding, stickToBone: "bone")));
    }

    [Theory]
    [InlineData(DeviceTypeIds.NoColliderBuilding_1030)]
    [InlineData(DeviceTypeIds.NoColliderBuilding_1032)]
    [InlineData(DeviceTypeIds.NoColliderBuilding_1058)]
    [InlineData(DeviceTypeIds.NoColliderBuilding_1061)]
    public void NoColliderBuilding_AllVariants_FollowTextBuildingRule(int typeId)
    {
        Assert.False(HasCap(CreateInMap(typeId)));
        Assert.False(HasCap(CreateInMap(typeId, stickToBone: "x")));
        Assert.True(HasCap(CreateInMech(typeId)));
        Assert.True(HasCap(CreateInMech(typeId, stickToBone: "bone")));
    }

    // ════════════════════════════════════════════════════════════════════
    // IsAlwaysDisallowedForType：仅覆盖 AlwaysNo 集合
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsAlwaysDisallowedForType_CoversOnlyAlwaysNoRigidbodySet()
    {
        Assert.True(DeviceCapabilityResolver.IsAlwaysDisallowedForType(DeviceTypeIds.BirthBuilding, DeviceCapability.Collidable));
        Assert.True(DeviceCapabilityResolver.IsAlwaysDisallowedForType(DeviceTypeIds.AreaBuilding, DeviceCapability.Collidable));
        Assert.True(DeviceCapabilityResolver.IsAlwaysDisallowedForType(DeviceTypeIds.StartBuilding, DeviceCapability.Collidable));
        Assert.True(DeviceCapabilityResolver.IsAlwaysDisallowedForType(DeviceTypeIds.ClientMapLogicDevice, DeviceCapability.Collidable));
        Assert.True(DeviceCapabilityResolver.IsAlwaysDisallowedForType(DeviceTypeIds.MapCoreDevice, DeviceCapability.Collidable));
        Assert.True(DeviceCapabilityResolver.IsAlwaysDisallowedForType(DeviceTypeIds.SLGLogicDevice, DeviceCapability.Collidable));

        // 条件类不属于"永远"集合：在 mech 内可能具备
        Assert.False(DeviceCapabilityResolver.IsAlwaysDisallowedForType(DeviceTypeIds.TextBuilding, DeviceCapability.Collidable));
        Assert.False(DeviceCapabilityResolver.IsAlwaysDisallowedForType(DeviceTypeIds.PosDevice, DeviceCapability.Collidable));
        Assert.False(DeviceCapabilityResolver.IsAlwaysDisallowedForType(DeviceTypeIds.TriggerBuilding, DeviceCapability.Collidable));
        Assert.False(DeviceCapabilityResolver.IsAlwaysDisallowedForType(DeviceTypeIds.NoColliderBuilding_1030, DeviceCapability.Collidable));

        // Device 基类
        Assert.False(DeviceCapabilityResolver.IsAlwaysDisallowedForType(1, DeviceCapability.Collidable));
    }

    // ════════════════════════════════════════════════════════════════════
    // 辅助
    // ════════════════════════════════════════════════════════════════════

    private static bool HasCap(DeviceEntity device)
        => DeviceCapabilityResolver.HasCapability(device, DeviceCapability.Collidable);

    private static DeviceEntity CreateInMap(int deviceType, string? stickToBone = null)
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        return TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType, logicName: "d", stickToBone);
    }

    private static DeviceEntity CreateInMech(int deviceType, string? stickToBone = null)
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MechEntity mech = TestEntityFactory.CreateMech(root, "mech");
        return TestEntityFactory.CreateDeviceOnMech(mech, deviceId: 1, deviceType, logicName: "d", stickToBone);
    }
}
