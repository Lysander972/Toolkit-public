// GUID 守卫 - 实体树流水线第一步
// 职责：基于 FileTreeNode 快照，在维护范围内确保 GUID 一致性
// 维护范围：跳过工程根 temp、.git/.svn/.map_/.mech_

using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

/// <summary>
/// GUID 守卫。
/// 文件树流水线中的 GUID 维护步骤，输入只能来自缓存层文件树快照。
/// 维护范围：跳过工程根 temp、.git、.svn、.map_、.mech_。
/// 范围内：补缺失 .guid、清悬空 .guid、修重复 GUID。
/// 范围外：整体视而不见。
/// .guid 只和同目录同名主文件构成伴生关系，禁止跨目录匹配。
/// 本步骤只允许发起磁盘维护；禁止手工修补 FileTreeNode 或实体模型来伪造流水线同步。
/// </summary>
internal static class GuidGuard
{
    /// <summary>
    /// 执行 GUID 守卫。
    /// </summary>
    /// <param name="root">文件树快照根节点</param>
    /// <param name="errors">错误收集器</param>
    /// <returns>GUID 守卫执行结果，包含是否修改文件系统以及发现的重复 GUID 问题</returns>
    public static GuidGuardResult Execute(FileTreeNode root, EntityLoadErrorCollector errors)
    {
        // 工程根身份校验（系统边界）：缺 setting.json 即非游戏工程（如开发仓库被误当工程根打开），
        // 拒绝一切磁盘维护，防止对任意目录全树洗盘生成 .guid 伴生文件。
        if (!root.Files.Contains(ProjectEntityLayout.SettingJsonName))
        {
            errors.AddGuidError(ProjectEntityLayout.SettingJsonName, "工程根缺少 setting.json，GUID 守卫拒绝执行");
            return new GuidGuardResult(false, []);
        }

        Dictionary<string, string> seenGuidPaths = new(StringComparer.Ordinal);
        List<GuidDuplicateIssue> duplicateIssues = [];
        bool modified = WalkNode(root, seenGuidPaths, duplicateIssues, errors);

        if (modified)
        {
            Log.For(LogModule.Core).Info("GUID 守卫完成：文件系统已修改，需要重新走快照");
        }

        return new GuidGuardResult(modified, duplicateIssues);
    }

    private static bool WalkNode(
        FileTreeNode node,
        Dictionary<string, string> seenGuidPaths,
        List<GuidDuplicateIssue> duplicateIssues,
        EntityLoadErrorCollector errors)
    {
        // 根节点（"."）不检查排除规则
        if (!node.RelativePath.Equals(WorkspacePath.RootDirectory, StringComparison.Ordinal))
        {
            // 跳过工程根 temp、.git、.svn、.map_、.mech_
            if (IsSkippedDirectory(node))
                return false;
        }

        bool modified = false;

        // 构建文件名集合用于快速查找
        HashSet<string> fileSet = new(node.Files, StringComparer.Ordinal);

        // Phase A: 检查 .guid 文件（悬空 + 重复）
        foreach (string fileName in node.Files)
        {
            if (!fileName.EndsWith(ProjectEntityLayout.GuidExt, StringComparison.Ordinal)) continue;

            string mainFileName = fileName[..^ProjectEntityLayout.GuidExt.Length];
            string guidFilePath = node.GetFileRelativePath(fileName);

            // 悬空检测：主文件不存在
            if (!fileSet.Contains(mainFileName))
            {
                FileIO.DeleteFile(guidFilePath);
                modified = true;
                Log.For(LogModule.Core).Debug($"GUID 守卫：清理悬空 .guid 文件 {guidFilePath}");
                continue;
            }

            // 重复检测：读取 .guid 内容
            string content = FileIO.ReadAllText(guidFilePath);
            GuidFile.Content? parsedGuidFile = GuidFile.TryParse(content);
            if (parsedGuidFile is null)
            {
                errors.AddGuidError(guidFilePath, "GUID 内容无效或为空");
                continue;
            }
            string guidValue = parsedGuidFile.Guid;

            if (seenGuidPaths.TryGetValue(guidValue, out string? firstGuidFilePath))
            {
                duplicateIssues.Add(new GuidDuplicateIssue(guidValue, firstGuidFilePath, guidFilePath));
                // 重复 GUID：重新生成
                string newGuid = GuidText.NewGuid();
                ProjectFileWriter.WriteGuid(guidFilePath, newGuid);
                seenGuidPaths.Add(newGuid, guidFilePath);
                modified = true;
                Log.For(LogModule.Core).Debug($"GUID 守卫：修复重复 GUID {guidFilePath}");
                continue;
            }

            seenGuidPaths.Add(guidValue, guidFilePath);
        }

        // Phase B: 补缺失的 .guid（原版不对无扩展名文件生成 guid）
        foreach (string fileName in node.Files)
        {
            if (fileName.EndsWith(ProjectEntityLayout.GuidExt, StringComparison.Ordinal)) continue;
            // 意图：跳过无扩展名文件（GetExtension 无扩展名时返回 string.Empty）
#pragma warning disable TK011
            if (RelativePathUtils.GetExtension(fileName) == string.Empty) continue;
#pragma warning restore TK011

            // 已有伴生 .guid 文件
            if (fileSet.Contains(fileName + ProjectEntityLayout.GuidExt)) continue;

            string mainFilePath = node.GetFileRelativePath(fileName);
            string guidFilePath = mainFilePath + ProjectEntityLayout.GuidExt;

            string newGuid = GuidText.NewGuid();
            ProjectFileWriter.WriteGuid(guidFilePath, newGuid);
            seenGuidPaths.Add(newGuid, guidFilePath);
            modified = true;
            Log.For(LogModule.Core).Debug($"GUID 守卫：创建 .guid 文件 {guidFilePath}");
        }

        // Phase C: 递归子目录
        foreach (FileTreeNode child in node.Children)
        {
            // 不进入跳过目录
            if (IsSkippedDirectory(child)) continue;
            modified |= WalkNode(child, seenGuidPaths, duplicateIssues, errors);
        }

        return modified;
    }

    /// <summary>
    /// 跳过工程根 temp、.git、.svn、.map_、.mech_ 目录。
    /// </summary>
    private static bool IsSkippedDirectory(FileTreeNode node)
        => node.Name is ".git" or ".svn"
        || node.RelativePath.Equals(ProjectEntityLayout.TempDirName, StringComparison.Ordinal)
        || node.Name.EndsWith(ProjectEntityLayout.MapDirExt, StringComparison.Ordinal)
        || node.Name.EndsWith(ProjectEntityLayout.MechDirExt, StringComparison.Ordinal);
}

internal sealed record GuidGuardResult(
    bool Modified,
    IReadOnlyList<GuidDuplicateIssue> DuplicateIssues);

public sealed record GuidDuplicateIssue(
    string Guid,
    string FirstGuidFileRelativePath,
    string DuplicateGuidFileRelativePath);
