using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

// =============================================================================
// 标准文件树视图 - 内部诊断工具
// =============================================================================
// 用途：对比文件系统实际内容与实体模型覆盖范围，找出未被扫描识别的缺口
// =============================================================================

/// <summary>
/// 文件树诊断结果
/// </summary>
public sealed class FileTreeDiagnostic
{
    /// <summary>文件系统存在但未被实体模型覆盖的目录</summary>
    public List<string> UncoveredDirectories { get; } = [];

    /// <summary>文件系统存在但未被实体模型覆盖的文件</summary>
    public List<string> UncoveredFiles { get; } = [];

    /// <summary>是否有缺口</summary>
    public bool HasGaps => UncoveredDirectories.Count > 0 || UncoveredFiles.Count > 0;
}

/// <summary>
/// 文件树诊断器 - 仅做实际扫描与实体模型的对比
/// </summary>
public sealed class FileTreeDiagnosticScanner
{
    private readonly ProjectModel _model;

    public FileTreeDiagnosticScanner(ProjectModel model)
    {
        _model = model;
    }

    /// <summary>
    /// 执行诊断：扫描文件系统，对比实体模型，找出缺口
    /// </summary>
    public FileTreeDiagnostic Scan()
    {
        FileTreeDiagnostic diagnostic = new();
        string rootPath = WorkspacePath.RootDirectory;

        if (!FileIO.DirectoryExists(rootPath))
            return diagnostic;

        ProjectRootEntity rootEntity = _model.Root;

        // 收集实体模型已覆盖的所有路径
        HashSet<string> coveredPaths = new(rootEntity.GetAllCoverage().AllPaths, StringComparer.Ordinal);

        // 递归扫描文件系统
        ScanDirectory(rootPath, coveredPaths, diagnostic);

        return diagnostic;
    }

    private void ScanDirectory(string dirPath, HashSet<string> coveredPaths, FileTreeDiagnostic diagnostic)
    {
        string dirName = RelativePathUtils.GetLastSegment(dirPath);

        // 跳过应忽略的目录
        if (!dirPath.Equals(WorkspacePath.RootDirectory, StringComparison.Ordinal)
            && WorkspacePath.IsIgnoredSegment(dirName))
            return;

        // 检查此目录是否被实体覆盖
        string validatedDir = WorkspacePath.RequireRelativePath(dirPath);
        if (!coveredPaths.Contains(validatedDir))
        {
            // 检查是否被间接覆盖（作为实体的父目录）
            if (!coveredPaths.Any(coveredPath => coveredPath.StartsWith(validatedDir + "/", StringComparison.Ordinal)))
            {
                diagnostic.UncoveredDirectories.Add(validatedDir);
            }
        }

        // 扫描子目录
        foreach (string subDir in FileIO.EnumerateDirectories(dirPath, "*", SearchOption.TopDirectoryOnly))
        {
            ScanDirectory(subDir, coveredPaths, diagnostic);
        }

        // 扫描文件
        foreach (string file in FileIO.EnumerateFiles(dirPath, "*", SearchOption.TopDirectoryOnly))
        {
            string fileName = RelativePathUtils.GetLastSegment(file);
            if (WorkspacePath.IsIgnoredFileName(fileName))
                continue;

            string validatedFile = WorkspacePath.RequireRelativePath(file);
            if (!coveredPaths.Contains(validatedFile))
            {
                diagnostic.UncoveredFiles.Add(validatedFile);
            }
        }
    }
}
