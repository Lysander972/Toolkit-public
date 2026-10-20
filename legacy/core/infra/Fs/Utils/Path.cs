namespace Toolkit.Core.Infra;

/// <summary>
/// 全局统一的文本编码常量。
/// 项目内部固定使用 UTF-8 无 BOM 编码，禁止分散定义。
/// </summary>
public static class TextEncoding
{
    /// <summary>
    /// UTF-8 无 BOM 编码器（项目统一编码）。
    /// 所有文本读写必须使用此编码器，禁止使用 Encoding.UTF8。
    /// </summary>
    public static readonly System.Text.Encoding Utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 将文本转换为 UTF-8 字节，并按统一换行规范转为 LF。
    /// </summary>
    public static byte[] GetBytesLfOnly(string text)
    {
        string lfOnly = TextLineEndings.ToLf(text);
        return Utf8.GetBytes(lfOnly);
    }
}

/// <summary>
/// 工作区路径规范化工具。
/// 提供相对路径验证、格式统一、隐藏目录检测等功能。
/// </summary>
public static class WorkspacePath
{
    /// <summary>
    /// 根目录相对路径（统一使用此常量，禁止硬编码 "."）
    /// </summary>
    public const string RootDirectory = ".";

    /// <summary>
    /// 将路径分隔符统一为 Unix 风格（'/' 替换 '\\'）。
    /// 用于边界处接收外部输入时的预处理。
    /// </summary>
    public static string ToUnixSeparator(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>
    /// 验证相对路径格式（禁止自动转换）。
    /// 仅允许工程根相对路径，不接受任何绝对路径或"修复"输入。
    /// </summary>
    public static string RequireRelativePath(string path)
    {
        (string result, string? error) = TryRequireRelativePath(path);
        if (error != null)
        {
            throw new ArgumentException(error, nameof(path));
        }
        return result;
    }

    /// <summary>
    /// 尝试验证相对路径格式，返回验证后的路径和可能的错误信息。
    /// </summary>
    public static (string result, string? error) TryRequireRelativePath(string path)
    {
#pragma warning disable TK011 // 边界检查：外部输入 path 可能为空字符串
        if (path == string.Empty)
            return (string.Empty, "路径不能为空");
#pragma warning restore TK011

#pragma warning disable TK010 // 边界检查：显式验证路径不允许首尾空白
        if (!string.Equals(path, path.Trim(), StringComparison.Ordinal))
#pragma warning restore TK010
            return (string.Empty, "路径不允许包含首尾空白");

        if (IsAbsolutePath(path))
            return (string.Empty, "不允许绝对路径");

        if (path.Contains('\\'))
            return (string.Empty, "路径分隔符必须使用 '/'");

        if (path == RootDirectory)
            return (path, null);

        if (path.StartsWith("./", StringComparison.Ordinal))
            return (string.Empty, "路径不允许以'./'开头");

        string[] segments = path.Split('/', StringSplitOptions.None);
        foreach (string segment in segments)
        {
#pragma warning disable TK011 // 边界检查：Split 结果中的空段表示连续斜杠或末尾斜杠，属非法输入
            if (segment == string.Empty)
                return (string.Empty, $"路径不允许出现空段或以'/'结尾: [{path}]");
#pragma warning restore TK011

            if (segment is "." or "..")
                return (string.Empty, $"路径不允许包含'.'或'..'段: [{path}]");
        }

        return (path, null);
    }

    public static bool IsSameOrUnderRoot(string rootRelativePath, string pathRelativePath)
    {
        string root = RequireRelativePath(rootRelativePath);
        string path = RequireRelativePath(pathRelativePath);

        // 工程根目录 "." 是全局根：所有合法相对路径都视为位于其下。
        // 注意：RequireRelativePath 禁止任何 "./" 前缀，因此不能用 "." + "/" 做前缀判断。
        if (root == RootDirectory)
        {
            return true;
        }

        if (string.Equals(path, root, StringComparison.Ordinal))
        {
            return true;
        }

        string rootPrefix = root + "/";
        return path.StartsWith(rootPrefix, StringComparison.Ordinal);
    }

    public static void RequirePathUnderRoot(string rootRelativePath, string pathRelativePath)
    {
        if (!IsSameOrUnderRoot(rootRelativePath, pathRelativePath))
        {
            throw new ArgumentException("路径必须位于指定根目录内", nameof(pathRelativePath));
        }
    }

    public static bool IsAbsolutePath(string path)
    {
        return path.StartsWith("/", StringComparison.Ordinal)
               || path.StartsWith("//", StringComparison.Ordinal)
               || path.StartsWith("\\", StringComparison.Ordinal)
               || path.StartsWith("\\\\", StringComparison.Ordinal)
               || (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':');
    }

    /// <summary>
    /// 允许空值的相对路径校验，空值直接返回空字符串。
    /// </summary>
    public static string RequireRelativePathOrEmpty(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return RequireRelativePath(path);
    }

    public static bool IsHiddenSegment(string? segment)
    {
        return !string.IsNullOrEmpty(segment) && segment[0] == '.';
    }

    public static bool IsIgnoredSegment(string? segment)
    {
        if (string.IsNullOrEmpty(segment))
        {
            return false;
        }

        // 只忽略隐藏目录
        // temp 和编辑器 Mod 目录现在作为特殊目录实体处理，不再跳过
        return IsHiddenSegment(segment);
    }

    public static bool IsIgnoredFileName(string fileName)
    {
        return IsHiddenSegment(fileName);
    }

    public static bool ContainsHiddenSegment(string path)
    {
        string validated = RequireRelativePath(path);
        foreach (string segment in validated.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsIgnoredSegment(segment))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsHiddenUnderRoot(string rootDir, string fullPath)
    {
        string full = RequireRelativePath(fullPath);
        string root = RequireRelativePath(rootDir);

        if (full.Equals(root, StringComparison.Ordinal))
        {
            return false;
        }

        string rootPrefix = root + "/";
        if (full.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            return ContainsHiddenSegment(full[rootPrefix.Length..]);
        }

        return ContainsHiddenSegment(full);
    }
}

/// <summary>
/// 相对路径操作工具（仅针对工程根相对路径）。
///
/// 命名规范（与 PathWrapper 对齐）：
/// - GetLastSegment: 获取最后一级名称（如 "a/b/c" → "c"）
/// - GetParentDirectory: 获取父目录路径（如 "a/b/c" → "a/b"）
/// </summary>
public static class RelativePathUtils
{
    /// <summary>获取最后一级名称（如 "a/b/c" → "c"，对文件和目录都适用）</summary>
    public static string GetLastSegment(string relativePath)
    {
        int lastSlash = relativePath.LastIndexOf('/');
        return lastSlash >= 0 ? relativePath[(lastSlash + 1)..] : relativePath;
    }

    /// <summary>获取父目录路径（如 "a/b/c" → "a/b"），根目录的直接子项返回 "."</summary>
    public static string GetParentDirectory(string relativePath)
    {
        string validatedPath = WorkspacePath.RequireRelativePath(relativePath);
        int lastSlash = validatedPath.LastIndexOf('/');
        if (lastSlash > 0)
        {
            return validatedPath[..lastSlash];
        }
        // 无斜杠时父目录是根目录，统一返回 RootDirectory
        return WorkspacePath.RootDirectory;
    }

    /// <summary>获取最后一级名称（不含扩展名），如 "a/b/c.txt" → "c"</summary>
    public static string GetLastSegmentWithoutExtension(string relativePath)
    {
        string fileName = GetLastSegment(relativePath);
        int dotIndex = fileName.LastIndexOf('.');
        return dotIndex > 0 ? fileName[..dotIndex] : fileName;
    }

    /// <summary>获取扩展名（包含点），如 "a/b/c.txt" → ".txt"</summary>
    public static string GetExtension(string relativePath)
    {
        string fileName = GetLastSegment(relativePath);
        int dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 ? fileName[dotIndex..] : string.Empty;
    }

    /// <summary>
    /// 合并工程根相对路径（不允许空值，也不做任何平台兼容处理）。
    /// 仅接受相对路径并统一为 '/'。
    /// </summary>
    public static string Combine(string leftRelativePath, string rightRelativePath)
    {
        string left = WorkspacePath.RequireRelativePath(leftRelativePath);
        string right = WorkspacePath.RequireRelativePath(rightRelativePath);
        if (left == WorkspacePath.RootDirectory)
        {
            return right;
        }
        if (right == WorkspacePath.RootDirectory)
        {
            return left;
        }
        return $"{left}/{right}";
    }
}

/// <summary>
/// 所有者路径拆分工具。
/// 将 "OwnerType.MethodName" 格式的路径拆分为所有者类型和方法名。
/// </summary>
internal static class OwnerPath
{
    public static (string ownerType, string methodName)? SplitOwnerPath(string path)
    {
        int dotIndex = path.LastIndexOf('.');
        if (dotIndex <= 0 || dotIndex >= path.Length - 1)
            return null;

        return (path[..dotIndex], path[(dotIndex + 1)..]);
    }
}
