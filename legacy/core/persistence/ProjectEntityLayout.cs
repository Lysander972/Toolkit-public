// 工程布局常量 - 仅包含扩展名、单段目录名、文件名
// 禁止多级路径常量！禁止路径构建/解析方法！

namespace Toolkit.Core.Persistence;

/// <summary>
/// 工程布局常量。
/// 仅包含扩展名、单段目录名、文件名。
/// 禁止多级路径常量！
/// </summary>
public static class ProjectEntityLayout
{
    // =============================================================================
    // 扩展名
    // =============================================================================

    public const string MapExt = ".map";
    public const string MechExt = ".mech";
    public const string CanvasExt = ".canvas";
    public const string CodeExt = ".code";
    public const string GuidExt = ".guid";
    public const string CodeGuidExt = ".code.guid";
    public const string MapDirExt = ".map_";
    public const string MechDirExt = ".mech_";
    public const string SpineDirExt = ".spine_";
    public const string MapGuidExt = ".map.guid";
    public const string MechGuidExt = ".mech.guid";

    // =============================================================================
    // 文件名（单段）
    // =============================================================================

    public const string SettingJsonName = "setting.json";
    public const string DeviceJsonName = "device.json";
    public const string VarsJsonName = "vars.json";
    public const string ListJsonName = "list.json";
    public const string MechJsonName = "mech.json";
    public const string MapJsonName = "map.json";

    // =============================================================================
    // 目录名（单段）
    // =============================================================================

    public const string ScriptsDirName = "scripts";
    public const string EventDirPrefix = "event_";
    public const string TempDirName = "temp";
    public const string ToolkitDirName = "toolkit";
    public const string ViewsDirName = "views";

    // =============================================================================
    // 编辑器 Mod 命名空间
    // =============================================================================

    public const string EditorNamespace = "MechEditorCode";
    public const string EditorToolNamespace = "MechEditorToolCode";
}
