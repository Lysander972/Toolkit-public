using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests;

internal static class ToolkitRepoPaths
{
    private static readonly Lazy<string> WorktreeRootLazy = new(FindWorktreeRoot);

    public static string WorktreeRoot => WorktreeRootLazy.Value;

    public static string ProjectsRoot => SystemIOWrapper.Combine(WorktreeRoot, "test", "data", "projects");

    private static string FindWorktreeRoot()
    {
        DirectoryInfo? current = SystemIOWrapper.GetDirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (SystemIOWrapper.FileExists(SystemIOWrapper.Combine(current.FullName, "MODULE.bazel"))
                && SystemIOWrapper.DirectoryExists(SystemIOWrapper.Combine(current.FullName, "src", "core"))
                && SystemIOWrapper.DirectoryExists(SystemIOWrapper.Combine(current.FullName, "test")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("未找到 Toolkit 根目录（缺少 MODULE.bazel/core/test）。（旧版的 Bazel 现已不再使用，已改回纯 pnpm）");
    }
}
