namespace Toolkit.Core.Tests;

/// <summary>
/// 脚本组一致性相关集成测试共用的临时工程构造。
/// 结构：setting.json + map.map（map.map_/0 零件，device.json type=1001）+ scripts/list.json + .code。
/// </summary>
internal static class ScriptGroupTestProject
{
    internal const string DeviceRelativePath = "map.map_/0";

    /// <summary>创建最小工程骨架，返回 scripts 目录绝对路径。</summary>
    internal static string CreateBaseProject(string projectRoot)
    {
        File.WriteAllText(
            Path.Combine(projectRoot, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        File.WriteAllText(
            Path.Combine(projectRoot, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");

        string mapDir = Path.Combine(projectRoot, "map.map_");
        Directory.CreateDirectory(mapDir);
        File.WriteAllText(Path.Combine(projectRoot, "map.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        File.WriteAllBytes(Path.Combine(projectRoot, "map.map"), [0x01, 0x02, 0x03, 0x04]);
        File.WriteAllText(Path.Combine(mapDir, "mech.json"), "{\"list\":[\"0-1001\"]}");
        File.WriteAllText(Path.Combine(mapDir, "map.json"), "{\"list\":[]}");

        string deviceDir = Path.Combine(mapDir, "0");
        string scriptsDir = Path.Combine(deviceDir, "scripts");
        Directory.CreateDirectory(scriptsDir);
        File.WriteAllText(Path.Combine(deviceDir, "device.json"), "type: 1001\next:\n{\nlogicName: test\n}");
        return scriptsDir;
    }

    /// <summary>添加一个代码仓库脚本（CodeRepo 目录 + .code + .code.guid）。</summary>
    internal static void WriteCodeRepoScript(string projectRoot, string name, string guid, string sourceText)
    {
        string repoDir = Path.Combine(projectRoot, "CodeRepo");
        Directory.CreateDirectory(repoDir);
        File.WriteAllText(Path.Combine(repoDir, name + ".code"), sourceText);
        File.WriteAllText(Path.Combine(repoDir, name + ".code.guid"), guid);
    }
}
