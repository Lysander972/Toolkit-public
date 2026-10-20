using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests;

/// <summary>
/// 统一的测试项目枚举逻辑，消除重复
/// </summary>
internal static class TestProjectEnumerator
{
    /// <summary>
    /// 枚举 ProjectsRoot 下所有项目目录
    /// </summary>
    public static IEnumerable<ProjectDirectory> EnumerateProjects()
    {
        string projectsRoot = ToolkitRepoPaths.ProjectsRoot;
        foreach (string dir in SystemIOWrapper.EnumerateDirectories(projectsRoot, "*", SearchOption.TopDirectoryOnly))
        {
            string name = SystemIOWrapper.GetLastSegment(dir);
            yield return new ProjectDirectory(name, dir);
        }
    }

    /// <summary>
    /// 枚举指定零件目录下的所有脚本文件路径（绝对路径）
    /// 包括 scripts/*.code 和 event_*/*.code
    /// </summary>
    public static IEnumerable<string> EnumerateDeviceScripts(string deviceDir)
    {
        string scriptsDir = $"{deviceDir}/scripts";
        if (SystemIOWrapper.DirectoryExists(scriptsDir))
        {
            foreach (string codePath in SystemIOWrapper.EnumerateFiles(scriptsDir, "*.code", SearchOption.TopDirectoryOnly))
            {
                yield return codePath;
            }
        }

        foreach (string eventDir in SystemIOWrapper.EnumerateDirectories(deviceDir, "event_*", SearchOption.TopDirectoryOnly))
        {
            foreach (string codePath in SystemIOWrapper.EnumerateFiles(eventDir, "*.code", SearchOption.TopDirectoryOnly))
            {
                yield return codePath;
            }
        }
    }

    /// <summary>
    /// 枚举项目下所有 .code 文件（递归）
    /// </summary>
    public static IEnumerable<string> EnumerateAllScripts(string projectRoot)
    {
        return SystemIOWrapper.EnumerateFiles(projectRoot, "*.code", SearchOption.AllDirectories);
    }

    /// <summary>
    /// 发现包含地图的项目根目录（包含 mech.json 的项目）
    /// </summary>
    public static IEnumerable<string> DiscoverMapRoots(string projectsRoot)
    {
        foreach (string projectDir in SystemIOWrapper.EnumerateDirectories(projectsRoot, "*", SearchOption.TopDirectoryOnly))
        {
            bool hasMech = SystemIOWrapper.EnumerateFiles(projectDir, "mech.json", SearchOption.AllDirectories).Any();
            if (hasMech)
            {
                yield return projectDir;
            }
        }
    }
}

/// <summary>
/// 项目目录信息
/// </summary>
internal readonly record struct ProjectDirectory(string Name, string Path);
