namespace Toolkit.Core.Infra;

/// <summary>
/// 用户级数据目录定位（统一配置/密钥/日志的共同根目录）。
/// 规范唯一权威：docs/Common_ToolkitConfig_Spec.md §1。
/// 该目录是原版固定目录，不属于工程根目录，禁止走 FileIO（仅允许工程内相对路径）。
/// </summary>
public static class ToolkitUserPaths
{
    public static string DataDirFullPath { get; } = SystemIOWrapper.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "HYH", "小玩法");

    public static string ConfigFileFullPath { get; } = SystemIOWrapper.Combine(DataDirFullPath, "toolkit.config.toml");

    public static string KeyFileFullPath { get; } = SystemIOWrapper.Combine(DataDirFullPath, "toolkit.key");
}
