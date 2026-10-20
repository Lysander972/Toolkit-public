// 静态关键集合判定：占据分级（支柱一）
//
// 外部进程占据文件时按"是否关键"分流：
// - 关键路径 → 前置阻断（EnterBlockedState，全面退让）
// - 非关键路径 → 容错（旧快照/占位继续，不进入受阻）
//
// 覆盖面（消费代码已知、可预测，静态可推导）：
// 1. setting.json（第一重要内容）
// 2. 各种实体的 hjson（map/mech/vars/list/device.json）与全部 .guid（形成索引）
// 3. setting.json 指向的表格资源（paletteExcel/languageExcel/coverPic）
// 4. 特定格式图片资源（png/jpg/jpeg，必然进 GUID 索引或被布局消费）
// 5. 全部 .code、.canvas 以及 temp/toolkit 范围内的任何东西
//
// 引用类判定（清单 3）依赖模型解析（GUID → 路径），由 ProjectModel 在刷新后更新。
// 引用集合就绪前 xlsx 一律按关键处理（保守：模型未加载时不可预测消费关系）。


using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

internal static class CriticalFilePolicy
{
    // 图片格式白名单（与 CanvasHtmlViewAssets 的 MIME 映射一致）
    private static readonly string[] s_imageExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
    ];

    // 实体 hjson 文件名（实体配置标准名）
    private static readonly string[] s_entityJsonNames =
    [
        "setting.json",
        "map.json",
        "mech.json",
        "vars.json",
        "list.json",
        "device.json",
    ];

    // setting.json 指向的表格资源路径（模型刷新后更新）
    private static readonly HashSet<string> s_referencedCriticalPaths = new(StringComparer.Ordinal);
    private static bool s_referencesInitialized;

    /// <summary>
    /// 模型刷新后更新 setting.json 引用解析结果（GUID → 相对路径）。
    /// </summary>
    public static void SetReferencedCriticalPaths(IEnumerable<string> relativePaths)
    {
        lock (s_referencedCriticalPaths)
        {
            s_referencedCriticalPaths.Clear();
            foreach (string path in relativePaths)
            {
                s_referencedCriticalPaths.Add(WorkspacePath.RequireRelativePath(path));
            }
            s_referencesInitialized = true;
        }
    }

    /// <summary>
    /// 路径是否属于静态关键集合。
    /// </summary>
    public static bool IsCriticalPath(string relativePath)
    {
        string path = WorkspacePath.RequireRelativePath(relativePath);
        string fileName = RelativePathUtils.GetLastSegment(path);

        // 1. setting.json（含 .guid）
        if (fileName == "setting.json" || fileName == "setting.json.guid")
            return true;

        // 2. 实体 hjson + 全部 .guid（形成索引）
        if (fileName.EndsWith(ProjectEntityLayout.GuidExt, StringComparison.Ordinal))
            return true;
        foreach (string jsonName in s_entityJsonNames)
        {
            if (fileName == jsonName)
                return true;
        }

        // 机械/地图锚点文件（实体识别依赖）
        if (fileName.EndsWith(ProjectEntityLayout.MechExt, StringComparison.Ordinal)
            || fileName.EndsWith(ProjectEntityLayout.MapExt, StringComparison.Ordinal))
            return true;

        // 3. setting 指向的表格资源（引用集合就绪后按集合；未就绪时 xlsx 保守全关键）
        lock (s_referencedCriticalPaths)
        {
            if (s_referencedCriticalPaths.Contains(path))
                return true;
            if (!s_referencesInitialized && fileName.EndsWith(".xlsx", StringComparison.Ordinal))
                return true;
        }

        // 4. 特定格式图片资源
        foreach (string ext in s_imageExtensions)
        {
            if (fileName.EndsWith(ext, StringComparison.Ordinal))
                return true;
        }

        // 5. 全部 .code、.canvas 以及 temp/toolkit 范围内的任何东西
        if (fileName.EndsWith(ProjectEntityLayout.CodeExt, StringComparison.Ordinal)
            || fileName.EndsWith(ProjectEntityLayout.CanvasExt, StringComparison.Ordinal))
            return true;
        if (path.StartsWith(ScriptPathHelper.TempToolkitPrefix, StringComparison.Ordinal)
            || path == ScriptPathHelper.TempToolkitPath)
            return true;

        return false;
    }
}
