// 底层文件系统 API 封装 - 统一压制 Windows 混乱路径
// 禁止任何地方直接使用 Directory.* / File.* API

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Toolkit.Core.Infra;

/// <summary>
/// 底层文件系统 API 封装器 - 唯一接触 System.IO 的地方。
/// 所有返回路径的方法都自动转换 \ 为 /，彻底压制 Windows 混乱路径。
/// 禁止直接使用 Directory.* / File.* API。
/// </summary>
public static class SystemIOWrapper
{
    /// <summary>
    /// 将路径统一转换为 / 格式（边界处理）。
    /// </summary>
    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    // ========== Directory API 封装 ==========

    public static bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public static DirectoryInfo GetDirectoryInfo(string path)
    {
        return new DirectoryInfo(path);
    }

    public static void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public static void DeleteDirectory(string path, bool recursive = false)
    {
        Directory.Delete(path, recursive);
    }

    public static void MoveDirectory(string sourceDirName, string destDirName)
    {
        Directory.Move(sourceDirName, destDirName);
    }

    public static DirectoryInfo? GetParent(string path)
    {
        return Directory.GetParent(path);
    }

    public static string GetCurrentDirectory()
    {
        return NormalizePath(Directory.GetCurrentDirectory());
    }

    public static string[] GetFiles(string path, string searchPattern, SearchOption searchOption)
    {
        return Directory.GetFiles(path, searchPattern, searchOption)
            .Select(NormalizePath)
            .ToArray();
    }

    public static string[] GetDirectories(string path, string searchPattern, SearchOption searchOption)
    {
        return Directory.GetDirectories(path, searchPattern, searchOption)
            .Select(NormalizePath)
            .ToArray();
    }

    public static IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        return Directory.EnumerateFiles(path, searchPattern, searchOption)
            .Select(NormalizePath);
    }

    public static IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
    {
        return Directory.EnumerateDirectories(path, searchPattern, searchOption)
            .Select(NormalizePath);
    }

    public static IEnumerable<string> EnumerateFileSystemEntries(string path)
    {
        return Directory.EnumerateFileSystemEntries(path)
            .Select(NormalizePath);
    }

    // ========== File API 封装 ==========

    public static bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public static void DeleteFile(string path)
    {
        File.Delete(path);
    }

    public static void MoveFile(string sourceFileName, string destFileName, bool overwrite = false)
    {
        File.Move(sourceFileName, destFileName, overwrite);
    }

    public static void CopyFile(string sourceFileName, string destFileName, bool overwrite)
    {
        File.Copy(sourceFileName, destFileName, overwrite);
    }

    public static FileStream OpenRead(string path)
    {
        return File.OpenRead(path);
    }

    public static FileStream OpenFileStream(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share)
    {
        return File.Open(path, mode, access, share);
    }

    /// <summary>
    /// 路径是否为 reparse point(symlink/junction)。仅 Windows;属性查询不开句柄。
    /// 属性读取失败(占用/权限)按"非 reparse"处理,交由后续探测决定受阻。
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// 从路径获取物理 FileId(follow 链接到目标)。仅 Windows;取值失败抛异常。
    /// 内部以 FileShare.Read 只读打开;文件被外部进程占用时,OS 可能返回共享冲突(ERROR_SHARING_VIOLATION)
    /// 或访问拒绝(ERROR_ACCESS_DENIED),分别映射为 IOException(HResult 32/33)与 UnauthorizedAccessException,
    /// 二者语义等价(同为"文件被占用"),调用方须一并归入"占用受阻"处理。
    /// </summary>
    internal static unsafe FileId GetFileId(string fullPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("物理 FileId 仅支持 Windows");
        using FileStream fs = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        BY_HANDLE_FILE_INFORMATION info;
        if (!GetFileInformationByHandle(fs.SafeFileHandle, &info))
        {
            throw new IOException($"GetFileInformationByHandle 失败，Win32 错误: {Marshal.GetLastWin32Error()}");
        }
        ulong fileIndex = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
        return new FileId(info.VolumeSerialNumber, fileIndex);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        BY_HANDLE_FILE_INFORMATION* lpFileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    public static string ReadAllText(string path)
    {
        return File.ReadAllText(path);
    }

    public static void WriteAllText(string path, string contents)
    {
        File.WriteAllText(path, contents);
    }

    public static byte[] ReadAllBytes(string path)
    {
        return File.ReadAllBytes(path);
    }

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        File.WriteAllBytes(path, bytes);
    }

    public static string[] ReadAllLines(string path)
    {
        return File.ReadAllLines(path);
    }

    // ========== Path API 封装 ==========

    public static string Combine(string path1, string path2)
    {
        return NormalizePath(Path.Combine(path1, path2));
    }

    public static string Combine(string path1, string path2, string path3)
    {
        return NormalizePath(Path.Combine(path1, path2, path3));
    }

    public static string Combine(params string[] paths)
    {
        return NormalizePath(Path.Combine(paths));
    }

    /// <summary>获取父目录路径（如 "a/b/c" → "a/b"）</summary>
    public static string GetParentDirectory(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (dir is null)
            return string.Empty;
        return NormalizePath(dir);
    }

    /// <summary>获取最后一级名称（如 "a/b/c" → "c"，对文件和目录都适用）</summary>
    public static string GetLastSegment(string path)
    {
        return NormalizePath(Path.GetFileName(path));
    }

    public static long GetFileLength(string path)
    {
        return new FileInfo(path).Length;
    }

    public static DateTime GetFileLastWriteTimeUtc(string path)
    {
        return File.GetLastWriteTimeUtc(path);
    }

    public static string GetRelativePath(string relativeTo, string path)
    {
        return NormalizePath(Path.GetRelativePath(relativeTo, path));
    }

    public static string GetFullPath(string path)
    {
        return NormalizePath(Path.GetFullPath(path));
    }

    public static bool IsPathRooted(string path)
    {
        return Path.IsPathRooted(path);
    }
}
