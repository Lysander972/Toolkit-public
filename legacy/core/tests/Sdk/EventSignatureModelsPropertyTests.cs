using FsCheck;
using FsCheck.Xunit;
using Xunit;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Tests.Sdk;

/// <summary>
/// **Feature: event-registry-and-coverage-tool, Property 2: Invalid signature rejection**
/// **Validates: Requirements 1.3**
/// </summary>
public class EventSignatureModelsPropertyTests
{
    /// <summary>
    /// 生成有效的事件名（字母或下划线开头，仅包含字母、数字、下划线）
    /// </summary>
    private static Arbitrary<string> ValidEventNameArb()
    {
        Gen<char> firstChar = Gen.OneOf(
            Gen.Choose('a', 'z').Select(c => (char)c),
            Gen.Choose('A', 'Z').Select(c => (char)c),
            Gen.Constant('_')
        );

        Gen<char[]> restChars = Gen.ArrayOf(Gen.OneOf(
            Gen.Choose('a', 'z').Select(c => (char)c),
            Gen.Choose('A', 'Z').Select(c => (char)c),
            Gen.Choose('0', '9').Select(c => (char)c),
            Gen.Constant('_')
        ));

        return Arb.From(
            from first in firstChar
            from rest in restChars
            select first + new string(rest)
        );
    }

    /// <summary>
    /// 生成无效的事件名（空、纯空白、数字开头或包含非法字符）
    /// </summary>
    private static Arbitrary<string> InvalidEventNameArb()
    {
        return Arb.From(Gen.OneOf(
            Gen.Constant(string.Empty),
            Gen.Constant("   "),
            Gen.Constant("\t"),
            // 数字开头
            from digit in Gen.Choose('0', '9').Select(static c => (char)c)
            from rest in Gen.ArrayOf(Gen.Choose('a', 'z').Select(static c => (char)c))
            select digit + new string(rest),
            // 包含非法字符
            from valid in Gen.Elements("start", "beHit", "onTick")
            from invalid in Gen.Elements("!", "@", "#", " ", "-", ".")
            select valid + invalid + "event"
        ));
    }

    /// <summary>
    /// 生成空或纯空白字符串
    /// </summary>
    private static Arbitrary<string> EmptyOrWhitespaceArb()
    {
        return Arb.From(Gen.OneOf(
            Gen.Constant(string.Empty),
            Gen.Constant("   "),
            Gen.Constant("\t"),
            Gen.Constant("\n"),
            Gen.Constant("  \t  ")
        ));
    }

    /// <summary>
    /// 生成有效的零件类型名
    /// </summary>
    private static Arbitrary<string> ValidDeviceTypeArb()
    {
        return Gen.Elements(
            "CoreDevice", "FuncDevice", "ClientMapLogicDevice",
            "MapCoreDevice", "BirthBuilding", "TriggerBuilding",
            "LaserDevice", "SLGLogicDevice"
        ).ToArbitrary();
    }

    #region Property 2: Invalid signature rejection

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 2: Invalid signature rejection**
    /// 空事件名的签名应该验证失败
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EventSignature_WithEmptyEventName_ShouldBeInvalid()
    {
        return Prop.ForAll(
            EmptyOrWhitespaceArb(),
            ValidDeviceTypeArb(),
            static (eventName, deviceType) =>
            {
                EventSignature sig = new EventSignature(
                    eventName,
                    deviceType,
                    Array.Empty<EventParameter>(),
                    "测试事件");
                return !sig.IsValid();
            });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 2: Invalid signature rejection**
    /// 无效事件名（数字开头或含非法字符）的签名应该验证失败
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EventSignature_WithInvalidEventName_ShouldBeInvalid()
    {
        return Prop.ForAll(
            InvalidEventNameArb(),
            ValidDeviceTypeArb(),
            static (eventName, deviceType) =>
            {
                EventSignature sig = new EventSignature(
                    eventName,
                    deviceType,
                    Array.Empty<EventParameter>(),
                    "测试事件");
                return !sig.IsValid();
            });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 2: Invalid signature rejection**
    /// 空零件类型的签名应该验证失败
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EventSignature_WithEmptyDeviceType_ShouldBeInvalid()
    {
        return Prop.ForAll(
            ValidEventNameArb(),
            Gen.Constant(string.Empty).ToArbitrary(),
            static (eventName, deviceType) =>
            {
                EventSignature sig = new EventSignature(
                    eventName,
                    deviceType,
                    Array.Empty<EventParameter>(),
                    "测试事件");
                return !sig.IsValid();
            });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 2: Invalid signature rejection**
    /// 同时空事件名和空零件类型的签名应该验证失败
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EventSignature_WithBothEmpty_ShouldBeInvalid()
    {
        return Prop.ForAll(
            EmptyOrWhitespaceArb(),
            EmptyOrWhitespaceArb(),
            static (eventName, deviceType) =>
            {
                EventSignature sig = new EventSignature(
                    eventName,
                    deviceType,
                    Array.Empty<EventParameter>(),
                    "测试事件");
                return !sig.IsValid();
            });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 2: Invalid signature rejection**
    /// 包含无效参数的签名应该验证失败
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EventSignature_WithInvalidParameter_ShouldBeInvalid()
    {
        return Prop.ForAll(
            ValidEventNameArb(),
            ValidDeviceTypeArb(),
            static (eventName, deviceType) =>
            {
                // 类型为 Enum 但未声明 EnumType → IsValid 应为 false
                EventParameter invalidParam = new EventParameter("$valid_name", "Enum");
                EventSignature sig = new EventSignature(
                    eventName,
                    deviceType,
                    new[] { invalidParam },
                    "测试事件");
                return !sig.IsValid();
            });
    }

    [Property(MaxTest = 100)]
    public Property EventSignature_WithEmptyParameterTypeName_ShouldBeInvalid()
    {
        return Prop.ForAll(
            ValidEventNameArb(),
            ValidDeviceTypeArb(),
            static (eventName, deviceType) =>
            {
                // 类型非 Enum 却声明了 EnumType → IsValid 应为 false
                EventParameter invalidParam = new EventParameter("$param", "Float", EnumType: new MethodEnumType("TestEnum", string.Empty));
                EventSignature sig = new EventSignature(
                    eventName,
                    deviceType,
                    new[] { invalidParam },
                    "测试事件");
                return !sig.IsValid();
            });
    }

    #endregion

    #region Validate() 方法测试

    [Fact]
    public void EventSignature_IsAllowedForDeviceType_ShouldPreferExcludedIdsOverAllowedIds()
    {
        EventSignature sig = new EventSignature(
            "start",
            "FuncDevice",
            Array.Empty<EventParameter>(),
            "测试事件",
            AllowedDeviceTypeIds: new[] { 200 },
            ExcludedDeviceTypeIds: new[] { 200 });

        Assert.False(sig.IsAllowedForDeviceType(200));
    }

    [Fact]
    public void FuncDeviceStartEvent_ShouldAllowOnlyFuncDeviceDerivedTypes()
    {
        EventSignature sig = EventSignatureRegistry.Instance
            .GetByDeviceType("FuncDevice")
            .Single(static e => e.EventName == "start");

        Assert.False(sig.IsAllowedForDeviceType(200));
        Assert.True(sig.IsAllowedForDeviceType(DeviceTypeIds.FuncDevice_77));
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 2: Invalid signature rejection**
    /// 空事件名调用 Validate() 应抛出 ArgumentException
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EventSignature_Validate_WithEmptyEventName_ShouldThrow()
    {
        return Prop.ForAll(
            EmptyOrWhitespaceArb(),
            ValidDeviceTypeArb(),
            static (eventName, deviceType) =>
            {
                EventSignature sig = new EventSignature(
                    eventName,
                    deviceType,
                    Array.Empty<EventParameter>(),
                    "测试事件");
                try
                {
                    sig.Validate();
                    return false; // 应该抛出异常
                }
                catch (ArgumentException)
                {
                    return true;
                }
            });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 2: Invalid signature rejection**
    /// 空零件类型调用 Validate() 应抛出 ArgumentException
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EventSignature_Validate_WithEmptyDeviceType_ShouldThrow()
    {
        return Prop.ForAll(
            ValidEventNameArb(),
            Gen.Constant(string.Empty).ToArbitrary(),
            static (eventName, deviceType) =>
            {
                EventSignature sig = new EventSignature(
                    eventName,
                    deviceType,
                    Array.Empty<EventParameter>(),
                    "测试事件");
                try
                {
                    sig.Validate();
                    return false;
                }
                catch (ArgumentException)
                {
                    return true;
                }
            });
    }

    #endregion
}
