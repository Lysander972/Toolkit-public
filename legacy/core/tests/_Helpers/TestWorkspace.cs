using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests;

/// <summary>
/// 统一的测试工作区构建器。
/// </summary>
internal static class TestWorkspace
{
    private static readonly object s_lock = new();
    private static ProjectModel? s_cachedModel;
    // 自持当前工程根:Dispose 幂等判断不再查询 FileIO.GetProjectRoot()(FileSystemCache 静态单例),
    // 消除"测试助手隐式依赖全局单例"的坏味道——Load 记录、Dispose 比对并清空,单一持有者。
    private static string? s_cachedRootFullPath;

    /// <summary>
    /// 获取工程模型（缓存版本，同一工程只创建一次）。
    /// </summary>
    public static ProjectModel LoadProject(string workspaceRoot)
    {
        string fullPath = SystemIOWrapper.GetFullPath(workspaceRoot);

        lock (s_lock)
        {
            s_cachedModel?.Dispose();
            FileTreeBinding.Instance.Clear();

            ProjectModel.SetProjectRootFullPath(fullPath);
            s_cachedRootFullPath = fullPath;
            s_cachedModel = ProjectModel.Load();
            return s_cachedModel;
        }
    }

    public static ProjectModel LoadProjectWithRecordingPersistence(string workspaceRoot)
    {
        string fullPath = SystemIOWrapper.GetFullPath(workspaceRoot);

        lock (s_lock)
        {
            s_cachedModel?.Dispose();
            FileTreeBinding.Instance.Clear();

            ProjectModel.SetProjectRootFullPath(fullPath);
            s_cachedRootFullPath = fullPath;
            InMemoryWorkspaceOverlay overlay = new();
            FileSystemCache.Instance.AttachOverlay(overlay);
            RecordingPersistenceOperationApplier recorder = new(overlay);
            s_cachedModel = ProjectModel.LoadForTesting(recorder);
            return s_cachedModel;
        }
    }

    /// <summary>
    /// 加载工程模型，注入自定义持久化执行器（不挂内存 overlay，直接读写真实文件）。
    /// 用于测试需拦截 Apply 行为的场景（如模拟独占失败）。
    /// </summary>
    public static ProjectModel LoadProjectWithPersistence(string workspaceRoot, IPersistenceOperationApplier applier)
    {
        string fullPath = SystemIOWrapper.GetFullPath(workspaceRoot);

        lock (s_lock)
        {
            s_cachedModel?.Dispose();
            FileTreeBinding.Instance.Clear();

            ProjectModel.SetProjectRootFullPath(fullPath);
            s_cachedRootFullPath = fullPath;
            s_cachedModel = ProjectModel.LoadForTesting(applier);
            return s_cachedModel;
        }
    }

    public static ToolkitServices InitializeSingletonWithRecordingPersistence(
        string workspaceRoot,
        BrowserRenderService browser)
    {
        string fullPath = SystemIOWrapper.GetFullPath(workspaceRoot);
        return ToolkitServices.InitializeSingletonForTesting(fullPath, browser);
    }

    public static void DisposeProject(string workspaceRoot)
    {
        string fullPath = SystemIOWrapper.GetFullPath(workspaceRoot);

        lock (s_lock)
        {
            if (s_cachedModel == null) return;
            if (!string.Equals(s_cachedRootFullPath, fullPath, StringComparison.Ordinal)) return;

            s_cachedModel.Dispose();
            s_cachedModel = null;
            s_cachedRootFullPath = null;
            FileTreeBinding.Instance.Clear();
        }
    }

    /// <summary>
    /// 尝试删除目录（忽略清理错误）
    /// </summary>
    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (SystemIOWrapper.DirectoryExists(path))
            {
                SystemIOWrapper.DeleteDirectory(path, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup errors in tests
        }
    }
}
