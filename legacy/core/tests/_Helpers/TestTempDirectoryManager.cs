using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests;

internal static class TestTempDirectoryManager
{
    private static readonly string TempRoot = SystemIOWrapper.Combine(Path.GetTempPath(), "Toolkit.Core.Tests");
    private static readonly List<string> Directories = [];
    private static readonly object Sync = new();
    private static bool _initialized;

    public static string CreateDirectory(string prefix)
    {
        lock (Sync)
        {
            if (!_initialized)
            {
                _initialized = true;
                AppDomain.CurrentDomain.ProcessExit += static (_, _) => CleanupOnExit();
            }

            string directoryFullPath = SystemIOWrapper.Combine(TempRoot, prefix, Guid.NewGuid().ToString("N"));
            SystemIOWrapper.CreateDirectory(directoryFullPath);
            Directories.Add(directoryFullPath);
            return directoryFullPath;
        }
    }

    public static void TryDeleteDirectory(string directoryFullPath)
    {
        if (SystemIOWrapper.DirectoryExists(directoryFullPath))
        {
            SystemIOWrapper.DeleteDirectory(directoryFullPath, recursive: true);
        }

        lock (Sync)
        {
            Directories.Remove(directoryFullPath);
        }
    }
    private static void CleanupOnExit()
    {
        string[] directories;
        lock (Sync)
        {
            directories = Directories.ToArray();
        }

        foreach (string directoryFullPath in directories)
        {
            TryDeleteDirectory(directoryFullPath);
        }
    }
}
