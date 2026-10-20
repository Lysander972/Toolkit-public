using Toolkit.Core.Bescript;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint;

/// <summary>
/// Lint 测试辅助类 - 使用实体模型替代已删除的解析模型。
/// </summary>
internal static class LintTestHelpers
{
    /// <summary>
    /// 创建包含传出调用的 DeviceEntity 用于测试。
    /// 统一入口：使用实体模型而非解析模型。
    /// </summary>
    public static DeviceEntity CreateDeviceWithOutgoingCall(string rawText, int line = 10)
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");

        device.SetVarsInternal(new VarsConfig
        {
            Variables =
            [
                new("HP", "Float", null),
                new("Name", "String", null)
            ]
        });

        ScriptGroupEntity scriptGroup = TestEntityFactory.CreateScriptGroup(device);
        scriptGroup.SetScriptEntriesInternal([new("A", string.Empty, false)]);

        TestEntityFactory.CreateInlineScript(
            scriptGroup,
            "A",
            $"begincodeV2\nname:A\nlines:\n{rawText}\nendcode");

        return device;
    }

    /// <summary>
    /// 创建默认的 DeviceIdentity。
    /// </summary>
    public static DeviceIdentity CreateDefaultOwner(
        string directory = "device",
        int deviceId = 1,
        int deviceType = 1,
        string mechId = "m1",
        string logicName = "logic",
        string carrierKind = "mech")
    {
        return new DeviceIdentity(
            Directory: directory,
            DeviceId: deviceId,
            DeviceType: deviceType,
            CarrierGuid: mechId,
            CarrierKind: carrierKind,
            LogicName: logicName
        );
    }

    /// <summary>
    /// 创建 BescriptParseData。
    /// </summary>
    public static BescriptParseData CreateDefaultParseData(
        string name,
        string? comment = null,
        bool debug = false,
        bool skip = false,
        bool isPrivate = false,
        string? guid = null,
        BescriptColor? color = null)
    {
        return new BescriptParseData(
            Name: name,
            Comment: comment,
            Debug: debug,
            Skip: skip,
            Private: isPrivate,
            Guid: guid,
            Color: color,
            Parameters: Array.Empty<BescriptParameter>(),
            ReturnParameter: null,
            Ast: new BescriptAst(null, BescriptHeader.Empty, Array.Empty<BescriptAstNode>(), BescriptAst.EmptyDeclaredVariables)
        );
    }
}
