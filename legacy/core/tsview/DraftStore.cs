using Toolkit.Core.Infra;

using Toolkit.Core.Persistence;

namespace Toolkit.Core.TsView;

/// <summary>
/// 视图草稿存储（draft 生命周期唯一持有者）。
///
/// 存储约定：视图草稿位于 viewsRoot 下 viewPath + ".draft"，文本内容由调用方序列化。
/// draft 是视图领域的运行时草稿（非工程实体），实体文件操作仍须走 ProjectModel；
/// 本类只经 FileIO 操作 draft 文件（TsView → Infra 依赖，TK026 白名单登记）。
/// </summary>
public static class DraftStore
{
    /// <summary>draft 相对路径（viewsRoot 下 viewPath + .draft）。</summary>
    public static string DraftPathFor(string viewsRootRelativePath, string viewPath)
    {
        return RelativePathUtils.Combine(viewsRootRelativePath, viewPath + ".draft");
    }

    /// <summary>从 draft 相对路径还原视图路径（剥离 viewsRoot 前缀与 .draft 后缀）。</summary>
    public static string ViewPathFromDraft(string viewsRootRelativePath, string draftRelativePath)
    {
        return draftRelativePath[(viewsRootRelativePath.Length + 1)..^".draft".Length];
    }

    /// <summary>draft 是否存在。</summary>
    public static bool Exists(string draftRelativePath)
    {
        return FileIO.FileExists(WorkspacePath.RequireRelativePathOrEmpty(draftRelativePath));
    }

    /// <summary>读取 draft 文本。</summary>
    public static string Read(string draftRelativePath)
    {
        return FileIO.ReadAllText(WorkspacePath.RequireRelativePathOrEmpty(draftRelativePath));
    }

    /// <summary>写入 draft（自动创建父目录）。</summary>
    public static void Write(string draftRelativePath, string content)
    {
        FileIO.CreateDirectory(RelativePathUtils.GetParentDirectory(draftRelativePath));
        FileIO.WriteAllText(draftRelativePath, content);
    }

    /// <summary>删除 draft。</summary>
    public static void Delete(string draftRelativePath)
    {
        FileIO.DeleteFile(WorkspacePath.RequireRelativePathOrEmpty(draftRelativePath));
    }

    /// <summary>viewsRoot 目录是否存在。</summary>
    public static bool ViewsRootExists(string viewsRootRelativePath)
    {
        return FileIO.DirectoryExists(viewsRootRelativePath);
    }

    /// <summary>枚举 viewsRoot 下全部 draft（返回相对 viewsRoot 的路径）。</summary>
    public static string[] Enumerate(string viewsRootRelativePath)
    {
        string[] files = FileIO.GetFiles(viewsRootRelativePath, "*.draft", SearchOption.AllDirectories);
        string prefix = viewsRootRelativePath + "/";
        string[] result = new string[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            result[i] = files[i].StartsWith(prefix, StringComparison.Ordinal)
                ? files[i][prefix.Length..]
                : files[i];
        }
        return result;
    }
}
