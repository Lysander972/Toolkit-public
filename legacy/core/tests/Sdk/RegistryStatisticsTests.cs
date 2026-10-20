using Xunit;
using Xunit.Abstractions;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Tests.Sdk;

/// <summary>
/// 注册表统计测试 - 用于最终验证
/// </summary>
public class RegistryStatisticsTests
{
    private readonly ITestOutputHelper _output;

    public RegistryStatisticsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void PrintRegistryStatistics()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        IReadOnlyDictionary<MethodCategory, int> counts = registry.GetCategoryCounts();

        _output.WriteLine("=== 内置方法注册表统计 ===");
        _output.WriteLine($"总方法数: {registry.Count}");
        _output.WriteLine(string.Empty);
        _output.WriteLine("按类别统计:");
        foreach ((MethodCategory category, int count) in counts.OrderBy(static x => x.Key))
        {
            _output.WriteLine($"  {category}: {count}");
        }

        // 按 OwnerType 统计 VarFunc
        _output.WriteLine(string.Empty);
        _output.WriteLine("VarFunc 按 OwnerType 统计:");
        MethodQueryResult varFuncs = registry.Query(category: MethodCategory.VarFunc, take: 2000);
        IOrderedEnumerable<IGrouping<string?, MethodSignature>> varFuncByOwner = varFuncs.Items.GroupBy(static s => s.OwnerType).OrderBy(static g => g.Key);
        foreach (IGrouping<string?, MethodSignature>? group in varFuncByOwner)
        {
            _output.WriteLine($"  {group.Key}: {group.Count()}");
        }

        // 按 OwnerType 统计 DeviceAction
        _output.WriteLine(string.Empty);
        _output.WriteLine("DeviceAction 按 OwnerType 统计:");
        MethodQueryResult actions = registry.Query(category: MethodCategory.DeviceAction, take: 2000);
        IOrderedEnumerable<IGrouping<string?, MethodSignature>> actionsByOwner = actions.Items.GroupBy(static s => s.OwnerType).OrderBy(static g => g.Key);
        foreach (IGrouping<string?, MethodSignature>? group in actionsByOwner)
        {
            _output.WriteLine($"  {group.Key}: {group.Count()}");
        }

        // 按 OwnerType 统计 EditorFunc
        _output.WriteLine(string.Empty);
        _output.WriteLine("EditorFunc 按 OwnerType 统计:");
        MethodQueryResult editorFuncs = registry.Query(category: MethodCategory.EditorFunc, take: 2000);
        IOrderedEnumerable<IGrouping<string?, MethodSignature>> editorFuncsByOwner = editorFuncs.Items.GroupBy(static s => s.OwnerType).OrderBy(static g => g.Key);
        foreach (IGrouping<string?, MethodSignature>? group in editorFuncsByOwner)
        {
            _output.WriteLine($"  {group.Key}: {group.Count()}");
        }

        // 验证总数一致
        int sumFromCounts = counts.Values.Sum();
        Assert.Equal(registry.Count, sumFromCounts);
    }

    [Fact]
    public void VerifyMinimumMethodCounts()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        IReadOnlyDictionary<MethodCategory, int> counts = registry.GetCategoryCounts();

        // 基于之前任务的实现，验证各类别有合理数量的方法
        Assert.True(counts[MethodCategory.GlobalFunc] >= 50, $"GlobalFunc 应至少有 50 个方法，实际: {counts[MethodCategory.GlobalFunc]}");
        Assert.True(counts[MethodCategory.VarFunc] >= 100, $"VarFunc 应至少有 100 个方法，实际: {counts[MethodCategory.VarFunc]}");
        Assert.True(counts[MethodCategory.DeviceAction] >= 200, $"DeviceAction 应至少有 200 个方法，实际: {counts[MethodCategory.DeviceAction]}");
        Assert.True(counts[MethodCategory.EditorFunc] >= 10, $"EditorFunc 应至少有 10 个方法，实际: {counts[MethodCategory.EditorFunc]}");
    }

    [Fact]
    public void PrintEventSignatureStatistics()
    {
        EventSignatureRegistry registry = EventSignatureRegistry.Instance;
        IReadOnlyList<string> deviceTypes = registry.GetDeviceTypes();

        _output.WriteLine("=== 事件签名注册表统计 ===");
        _output.WriteLine($"总事件数: {registry.Count}");
        _output.WriteLine($"零件类型数: {deviceTypes.Count}");
        _output.WriteLine(string.Empty);
        _output.WriteLine("按零件类型统计:");
        foreach (string? deviceType in deviceTypes.OrderBy(static x => x))
        {
            IReadOnlyList<EventSignature> events = registry.GetByDeviceType(deviceType);
            _output.WriteLine($"  {deviceType}: {events.Count}");
        }

        // 验证有事件被加载
        Assert.True(registry.Count > 0, "事件注册表应包含事件定义");
    }

    [Fact]
    public void VerifyMinimumEventCounts()
    {
        EventSignatureRegistry registry = EventSignatureRegistry.Instance;

        // 验证各零件类型有合理数量的事件
        Assert.True(registry.GetByDeviceType("FuncDevice").Count >= 2, "FuncDevice 应至少有 2 个事件");
        Assert.True(registry.GetByDeviceType("ClientMapLogicDevice").Count >= 10, "ClientMapLogicDevice 应至少有 10 个事件");
        Assert.True(registry.GetByDeviceType("MapCoreDevice").Count >= 5, "MapCoreDevice 应至少有 5 个事件");
        Assert.True(registry.GetByDeviceType("CoreDevice").Count >= 5, "CoreDevice 应至少有 5 个事件");
        Assert.True(registry.GetByDeviceType("BirthBuilding").Count >= 4, "BirthBuilding 应至少有 4 个事件");
        Assert.True(registry.GetByDeviceType("SLGLogicDevice").Count >= 5, "SLGLogicDevice 应至少有 5 个事件");
    }
}
