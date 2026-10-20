using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Refactor;

public sealed record IdentifierChange(int Line, int Column, int Length);
public sealed record IdentifierRenameData(IReadOnlyList<IdentifierChange> Changes, bool Applied);
public sealed record IdentifierRenameItem(string OldName, string NewName);

/// <summary>
/// 局部变量重命名。仅在单个脚本内按 AST 替换局部标识符。
/// 不涉及全局变量——全局变量重命名请使用专用全局变量重构工具。
/// </summary>
public sealed class IdentifierRename
{
    private readonly ProjectModel _model;

    public IdentifierRename(ProjectModel model)
    {
        _model = model;
    }

    public RefactorResult<IdentifierRenameData> Rename(string path, string oldName, string newName)
        => Rename(path, new[] { new IdentifierRenameItem(oldName, newName) });

    public RefactorResult<IdentifierRenameData> Rename(string path, IReadOnlyList<IdentifierRenameItem> renames)
    {
        if (renames.Count == 0)
            return RefactorResult<IdentifierRenameData>.Fail("缺少 renames 参数");

        string relativePath = WorkspacePath.RequireRelativePath(path);

        if (_model.Find(relativePath) is not ProjectEntity baseScriptEntity)
            return RefactorResult<IdentifierRenameData>.Fail(
                $"未找到脚本实体: {relativePath}。" +
                $"scriptRelativePath 应为 .code 文件路径（内联脚本或代码仓库脚本）。" +
                $"注意：不支持 .ts 视图文件路径，根本不存在所谓 device/ 前缀。");

        string text;
        InlineScriptEntity? inlineScript = null;
        CodeRepoScriptEntity? codeRepoScript = null;

        if (baseScriptEntity is InlineScriptEntity inline)
        {
            inlineScript = inline;
            text = inlineScript.SourceText;
        }
        else if (baseScriptEntity is CodeRepoScriptEntity repo)
        {
            codeRepoScript = repo;
            text = codeRepoScript.SourceText;
        }
        else
        {
            return RefactorResult<IdentifierRenameData>.Fail(
                $"未找到脚本实体: {relativePath}。" +
                $"scriptRelativePath 应为 .code 文件路径（内联脚本或代码仓库脚本）。" +
                $"注意：不支持 .ts 视图文件路径，根本不存在所谓 device/ 前缀。");
        }

        HashSet<string> seenOldNames = new(StringComparer.Ordinal);
        HashSet<string> seenNewNames = new(StringComparer.Ordinal);
        foreach (IdentifierRenameItem rename in renames)
        {
            if (rename.OldName == rename.NewName) return RefactorResult<IdentifierRenameData>.Fail($"新名称与旧名称相同: {rename.OldName}");
            if (BescriptTextCodec.ReplaceInvalidChar(rename.NewName.AsSpan()).IndexOf('@') >= 0)
                return RefactorResult<IdentifierRenameData>.Fail($"标识符不允许包含需转义字符: {rename.NewName}");
            if (!seenOldNames.Add(rename.OldName))
                return RefactorResult<IdentifierRenameData>.Fail($"oldName 重复: {rename.OldName}");
            if (!seenNewNames.Add(rename.NewName))
                return RefactorResult<IdentifierRenameData>.Fail($"newName 重复: {rename.NewName}");
        }
        foreach (IdentifierRenameItem rename in renames)
        {
            if (seenOldNames.Contains(rename.NewName))
                return RefactorResult<IdentifierRenameData>.Fail($"批量重命名禁止把 newName 设为另一项 oldName: {rename.NewName}");
        }

        if (inlineScript != null)
        {
            string scriptDir = RelativePathUtils.GetParentDirectory(relativePath);
            string deviceDir = RelativePathUtils.GetParentDirectory(scriptDir);
            if (_model.Find<DeviceEntity>(deviceDir) is DeviceEntity device)
            {
                HashSet<string> globalNames = new(
                    device.Vars.Variables.Select(v => v.Name),
                    StringComparer.Ordinal);
                foreach (IdentifierRenameItem rename in renames)
                {
                    if (globalNames.Contains(rename.OldName))
                        return RefactorResult<IdentifierRenameData>.Fail(
                            $"'{rename.OldName}' 是该零件的全局变量，局部变量重命名工具不处理全局变量。请使用 refactor_rename_global_var。");
                    if (globalNames.Contains(rename.NewName))
                        return RefactorResult<IdentifierRenameData>.Fail(
                            $"'{rename.NewName}' 已是该零件的全局变量，重命名后将产生命名冲突，操作拒绝。");
                }
            }
        }

        string newText = text;
        List<IdentifierChange> allChanges = [];
        foreach (IdentifierRenameItem rename in renames)
        {
            (string PatchedText, List<IdentifierChange> Changes, string? Error) = RenameSingle(relativePath, newText, rename.OldName, rename.NewName);
            if (Error != null)
                return RefactorResult<IdentifierRenameData>.Fail(Error);
            newText = PatchedText;
            allChanges.AddRange(Changes);
        }

        try
        {
            if (inlineScript != null)
            {
                EntityWriteScope.ApplyContentChange<InlineScriptEntity, WritableInlineScriptEntity>(
                    _model,
                    inlineScript,
                    script => script.ToWritable(),
                    writableInline => writableInline.SourceText = newText);
            }
            else if (codeRepoScript != null)
            {
                EntityWriteScope.ApplyContentChange<CodeRepoScriptEntity, WritableCodeRepoScriptEntity>(
                    _model,
                    codeRepoScript,
                    script => script.ToWritable(),
                    writableCodeRepo => writableCodeRepo.SourceText = newText);
            }
            else
            {
                return RefactorResult<IdentifierRenameData>.Fail("未找到起点脚本实体");
            }
        }
        catch (Exception ex) when (ex is not WorkspaceFileBlockedException and not WorkspaceStateChangedException)
        {
            return RefactorResult<IdentifierRenameData>.Fail($"保存变更失败: {ex.Message}");
        }

        if (codeRepoScript != null)
        {
            return RefactorResult<IdentifierRenameData>.Success(
                new IdentifierRenameData(allChanges, true),
                [new ToolError(relativePath, null, null,
                    "目标为 coderepo 脚本，无法确认全局变量上下文。若改名后与链接此脚本的零件全局变量冲突，引用将断裂。")]);
        }

        return RefactorResult<IdentifierRenameData>.Success(new IdentifierRenameData(allChanges, true));
    }

    private static (string PatchedText, List<IdentifierChange> Changes, string? Error) RenameSingle(
        string relativePath,
        string text,
        string oldName,
        string newName)
    {
        List<IdentifierChange> changes = [];

        BescriptScriptInfo scriptInfo = BescriptTextParser.Parse(relativePath, text).Script;
        BescriptAst ast = scriptInfo.Ast;

        BescriptHeader newHeader = ast.Header;

        List<BescriptParameter> newParams = new(ast.Header.Parameters);
        BescriptParameter? newRet = ast.Header.ReturnParameter;
        bool headerChanged = false;

        for (int i = 0; i < newParams.Count; i++)
        {
            if (string.Equals(newParams[i].Name, oldName, StringComparison.Ordinal))
            {
                newParams[i] = newParams[i] with { Name = newName };
                headerChanged = true;
                changes.Add(new IdentifierChange(1, 0, oldName.Length));
            }
        }

        if (newRet != null && string.Equals(newRet.Name, oldName, StringComparison.Ordinal))
        {
            newRet = newRet with { Name = newName };
            headerChanged = true;
            changes.Add(new IdentifierChange(1, 0, oldName.Length));
        }

        if (headerChanged)
        {
            newHeader = ast.Header with
            {
                Parameters = newParams,
                ReturnParameter = newRet
            };
        }

        List<BescriptAstNode> newRootNodes = new List<BescriptAstNode>(ast.RootNodes.Count);
        foreach (BescriptAstNode node in ast.RootNodes)
            newRootNodes.Add(RenameInNode(node, oldName, newName, changes));

        if (changes.Count == 0)
        {
            return (text, changes,
                $"未找到可替换的标识符: {oldName}。rename.identifier 仅限局部变量重命名，请检查标识符拼写。");
        }

        BescriptAst newAst = ast with { Header = newHeader, RootNodes = newRootNodes };
        return (BescriptAstSerializer.SerializeFull(newAst), changes, null);
    }

    private static BescriptAstNode RenameInNode(BescriptAstNode node, string oldName, string newName, List<IdentifierChange> changes)
    {
        IReadOnlyList<BescriptAstNode> newChildren = node.Children;
        if (node.Children.Count > 0)
        {
            List<BescriptAstNode> list = new List<BescriptAstNode>(node.Children.Count);
            foreach (BescriptAstNode child in node.Children)
                list.Add(RenameInNode(child, oldName, newName, changes));
            newChildren = list;
        }

        if (node is IHasCall hasCall)
        {
            BescriptCallSignature newCall = RenameInCall(hasCall.Call, oldName, newName, changes, node.Line);
            if (newCall == hasCall.Call && newChildren == node.Children)
                return node;
            return node.WithCallAndChildren(newCall, newChildren);
        }

        return ReferenceEquals(newChildren, node.Children) ? node : node.WithChildren(newChildren);
    }

    private static BescriptCallSignature RenameInCall(BescriptCallSignature call, string oldName, string newName, List<IdentifierChange> changes, int line)
    {
        bool changed = false;

        ReturnVarDecl? newReturnVar = call.ReturnVar;
        if (call.ReturnVar is { } ret && string.Equals(ret.Name, oldName, StringComparison.Ordinal))
        {
            newReturnVar = new ReturnVarDecl(ret.TypeName, newName);
            changed = true;
            changes.Add(new IdentifierChange(line, 0, oldName.Length));
        }

        List<BescriptCallArgument>? newArgs = null;
        if (call.Arguments.Count > 0)
        {
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                BescriptCallArgument arg = call.Arguments[i];
                if (arg.IsVariable && string.Equals(arg.DecodedText, oldName, StringComparison.Ordinal))
                {
                    if (newArgs == null) newArgs = call.Arguments.ToList();
                    newArgs[i] = arg with { DecodedText = newName };
                    changed = true;
                    changes.Add(new IdentifierChange(line, 0, oldName.Length));
                }
            }
        }

        if (!changed) return call;

        BescriptCallSignature result = call switch
        {
            VarfCallSignature v => v with { ReturnVar = newReturnVar, OwnerName = RenameIfMatch(v.OwnerName, oldName, newName, changes, line, v.OwnerNameIsAlias) },
            ActCallSignature a => a with { ReturnVar = newReturnVar, OwnerName = RenameIfMatch(a.OwnerName, oldName, newName, changes, line, a.OwnerNameIsAlias) },
            DvfCallSignature d => d with { ReturnVar = newReturnVar, OwnerName = RenameIfMatch(d.OwnerName, oldName, newName, changes, line, d.OwnerNameIsAlias) },
            _ => call with { ReturnVar = newReturnVar }
        };
        return newArgs is not null ? result with { Arguments = newArgs } : result;
    }

    private static string RenameIfMatch(string current, string oldName, string newName, List<IdentifierChange> changes, int line, bool isAlias)
    {
        if (!isAlias && string.Equals(current, oldName, StringComparison.Ordinal))
        {
            changes.Add(new IdentifierChange(line, 0, oldName.Length));
            return newName;
        }
        return current;
    }
}
