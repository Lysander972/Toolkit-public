using FsCheck;
using FsCheck.Xunit;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Tests.Sdk;

/// <summary>
/// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
/// **Validates: Requirements 1.1, 1.2, 2.3**
/// </summary>
public class EventSignatureRegistryPropertyTests
{
    /// <summary>
    /// 生成有效的事件名（字母或下划线开头，仅包含字母、数字、下划线）
    /// </summary>
    private static Gen<string> ValidEventNameGen()
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

        return from first in firstChar
               from rest in restChars
               let name = first + new string(rest)
               where name.Length > 0 && name.Length < 30
               select name;
    }

    /// <summary>
    /// 生成有效的零件类型名
    /// </summary>
    private static Gen<string> ValidDeviceTypeGen()
    {
        return Gen.Elements(
            "CoreDevice", "FuncDevice", "ClientMapLogicDevice",
            "MapCoreDevice", "BirthBuilding", "TriggerBuilding",
            "LaserDevice", "SLGLogicDevice", "TestDevice"
        );
    }

    /// <summary>
    /// 生成有效的事件签名
    /// </summary>
    private static Gen<EventSignature> ValidEventSignatureGen()
    {
        return from eventName in ValidEventNameGen()
               from deviceType in ValidDeviceTypeGen()
               from paramCount in Gen.Choose(0, 3)
               from parameters in Gen.ListOf(
                   paramCount,
                   from name in ValidEventNameGen()
                   from typeName in Gen.Elements("Float", "Long", "String", "Bool", "Player", "Device", "Mech", "Any")
                   select new EventParameter($"${name}", typeName))
               select new EventSignature(
                   eventName,
                   deviceType,
                   parameters.ToArray(),
                   "测试事件");
    }

    #region Property 1: Registration and retrieval consistency

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// 对于任意有效的事件签名，注册后通过事件名查询应能找到该签名
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RegisteredEvent_QueryByName_ShouldContainSignature()
    {
        return Prop.ForAll(ValidEventSignatureGen().ToArbitrary(), sig =>
        {
            EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();
            registry.Register(sig);

            IReadOnlyList<EventSignature> result = registry.GetByName(sig.EventName);
            return result.Any(s =>
                s.EventName.Equals(sig.EventName, StringComparison.Ordinal) &&
                s.DeviceType.Equals(sig.DeviceType, StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// 对于任意有效的事件签名，注册后通过零件类型查询应能找到该签名
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RegisteredEvent_QueryByDeviceType_ShouldContainSignature()
    {
        return Prop.ForAll(ValidEventSignatureGen().ToArbitrary(), sig =>
        {
            EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();
            registry.Register(sig);

            IReadOnlyList<EventSignature> result = registry.GetByDeviceType(sig.DeviceType);
            return result.Any(s =>
                s.EventName.Equals(sig.EventName, StringComparison.Ordinal) &&
                s.DeviceType.Equals(sig.DeviceType, StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// 对于任意有效的事件签名，注册后通过 Get(deviceType, eventName) 应能精确获取该签名
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RegisteredEvent_Get_ShouldReturnExactSignature()
    {
        return Prop.ForAll(ValidEventSignatureGen().ToArbitrary(), static sig =>
        {
            EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();
            registry.Register(sig);

            EventSignature? result = registry.Get(sig.DeviceType, sig.EventName);
            return result is not null &&
                   result.EventName.Equals(sig.EventName, StringComparison.Ordinal) &&
                   result.DeviceType.Equals(sig.DeviceType, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// 对于任意有效的事件签名，注册后 GetAll 应包含该签名
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RegisteredEvent_GetAll_ShouldContainSignature()
    {
        return Prop.ForAll(ValidEventSignatureGen().ToArbitrary(), sig =>
        {
            EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();
            registry.Register(sig);

            IReadOnlyList<EventSignature> result = registry.GetAll();
            return result.Any(s =>
                s.EventName.Equals(sig.EventName, StringComparison.Ordinal) &&
                s.DeviceType.Equals(sig.DeviceType, StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// 对于任意有效的事件签名，注册后 GetDeviceTypes 应包含该零件类型
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RegisteredEvent_GetDeviceTypes_ShouldContainDeviceType()
    {
        return Prop.ForAll(ValidEventSignatureGen().ToArbitrary(), sig =>
        {
            EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();
            registry.Register(sig);

            IReadOnlyList<string> result = registry.GetDeviceTypes();
            return result.Any(dt => dt.Equals(sig.DeviceType, StringComparison.Ordinal));
        });
    }

    #endregion

    #region 批量注册一致性

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// 批量注册多个事件后，每个事件都应能通过各种方式检索到
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RegisterAll_AllEvents_ShouldBeRetrievable()
    {
        return Prop.ForAll(
            Gen.ListOf(5, ValidEventSignatureGen())
               .Where(list => list.Select(s => (s.DeviceType, s.EventName))
                                  .Distinct()
                                  .Count() == list.Count())
               .ToArbitrary(),
            sigs =>
            {
                EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();
                registry.RegisterAll(sigs);

                // 每个签名都应能检索到
                return sigs.All(sig =>
                {
                    IReadOnlyList<EventSignature> byName = registry.GetByName(sig.EventName);
                    IReadOnlyList<EventSignature> byDevice = registry.GetByDeviceType(sig.DeviceType);
                    EventSignature? exact = registry.Get(sig.DeviceType, sig.EventName);

                    return byName.Any(s => s.DeviceType == sig.DeviceType) &&
                           byDevice.Any(s => s.EventName == sig.EventName) &&
                           exact is not null;
                });
            });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// 批量注册后，Count 应等于注册的唯一事件数
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RegisterAll_Count_ShouldMatchUniqueEvents()
    {
        return Prop.ForAll(
            Gen.ListOf(5, ValidEventSignatureGen())
               .Where(static list => list.Select(static s => (s.DeviceType, s.EventName))
                                  .Distinct()
                                  .Count() == list.Count())
               .ToArbitrary(),
            static sigs =>
            {
                EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();
                registry.RegisterAll(sigs);

                return registry.Count == sigs.Count();
            });
    }

    #endregion

    #region 重复注册行为

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// 重复注册同一事件（相同 DeviceType + EventName）应覆盖旧定义
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DuplicateRegistration_ShouldOverwrite()
    {
        return Prop.ForAll(ValidEventSignatureGen().ToArbitrary(), static sig =>
        {
            EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();

            // 第一次注册
            registry.Register(sig);
            int countAfterFirst = registry.Count;

            // 创建同 DeviceType + EventName 但不同参数的签名
            EventSignature sig2 = new EventSignature(
                sig.EventName,
                sig.DeviceType,
                new[] { new EventParameter("$newParam", "String") },
                "Updated summary");

            // 第二次注册
            registry.Register(sig2);
            int countAfterSecond = registry.Count;

            // 应该只有一个事件
            return countAfterFirst == 1 && countAfterSecond == 1;
        });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// 重复注册后应返回最新版本
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DuplicateRegistration_ShouldReturnLatestVersion()
    {
        return Prop.ForAll(ValidEventSignatureGen().ToArbitrary(), static sig =>
        {
            EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();

            registry.Register(sig);

            // 创建带 Summary 的新版本
            EventSignature sig2 = sig with { Summary = "Updated version" };
            registry.Register(sig2);

            EventSignature? retrieved = registry.Get(sig.DeviceType, sig.EventName);
            return retrieved?.Summary == "Updated version";
        });
    }

    #endregion

    #region 查询空结果

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// 查询未注册的事件名应返回空列表
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetByName_UnregisteredEvent_ShouldReturnEmpty()
    {
        return Prop.ForAll(ValidEventNameGen().ToArbitrary(), static eventName =>
        {
            EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();
            IReadOnlyList<EventSignature> result = registry.GetByName(eventName);
            return result.Count() == 0;
        });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// 查询未注册的零件类型应返回空列表
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetByDeviceType_UnregisteredType_ShouldReturnEmpty()
    {
        return Prop.ForAll(ValidDeviceTypeGen().ToArbitrary(), static deviceType =>
        {
            EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();
            IReadOnlyList<EventSignature> result = registry.GetByDeviceType(deviceType);
            return result.Count() == 0;
        });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// Get 未注册的事件应返回 null
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Get_UnregisteredEvent_ShouldReturnNull()
    {
        return Prop.ForAll(
            ValidDeviceTypeGen().ToArbitrary(),
            ValidEventNameGen().ToArbitrary(),
            static (deviceType, eventName) =>
            {
                EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();
                EventSignature? result = registry.Get(deviceType, eventName);
                return result is null;
            });
    }

    #endregion

    #region 空/无效输入处理

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// GetByName 传入空字符串应返回空列表
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetByName_EmptyString_ShouldReturnEmpty()
    {
        return Prop.ForAll(
            Gen.Elements(string.Empty, "   ", "\t").ToArbitrary(),
            static input =>
            {
                EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();
                IReadOnlyList<EventSignature> result = registry.GetByName(input);
                return result.Count() == 0;
            });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// GetByDeviceType 传入空字符串应返回空列表
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetByDeviceType_EmptyString_ShouldReturnEmpty()
    {
        return Prop.ForAll(
            Gen.Elements(string.Empty, "   ", "\t").ToArbitrary(),
            static input =>
            {
                EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();
                IReadOnlyList<EventSignature> result = registry.GetByDeviceType(input);
                return result.Count() == 0;
            });
    }

    /// <summary>
    /// **Feature: event-registry-and-coverage-tool, Property 1: Registration and retrieval consistency**
    /// Get 传入空字符串应返回 null
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Get_EmptyStrings_ShouldReturnNull()
    {
        return Prop.ForAll(
            Gen.Elements(string.Empty, "   ", "\t").ToArbitrary(),
            Gen.Elements(string.Empty, "   ", "\t").ToArbitrary(),
            static (deviceType, eventName) =>
            {
                EventSignatureRegistry registry = EventSignatureRegistry.CreateEmpty();
                EventSignature? result = registry.Get(deviceType, eventName);
                return result is null;
            });
    }

    #endregion
}
