using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Search;

namespace Toolkit.Core.Refactor;

/// <summary>
/// Canvas 提取结果（内联→独立）
/// </summary>
public sealed record CanvasExtractData(
    string ExtractedGuid,
    string CanvasRelativePath,
    string PatchedScriptRelativePath,
    bool Applied,
    IReadOnlyList<string> AffectedPaths
);

/// <summary>
/// Canvas 导入/链接结果（内联→链接到已有独立 canvas）
/// </summary>
public sealed record CanvasImportData(
    string LinkedGuid,
    string PatchedScriptRelativePath,
    bool Applied,
    IReadOnlyList<string> AffectedPaths
);

/// <summary>
/// Canvas 内联结果（独立→内联）
/// </summary>
public sealed record CanvasInlineData(
    string InlinedCanvasName,
    string PatchedScriptRelativePath,
    bool CanvasFileDeleted,
    bool Applied,
    IReadOnlyList<string> AffectedPaths
);

/// <summary>
/// Canvas 重构工具：Extract（内联→独立）与 Inline（独立→内联）完全对称。
///
/// 架构：原始BES文本 → Parse → AST → 修改节点 → SerializeFull → 新文本。
/// 不存在任何文本匹配定位步骤。
/// </summary>
public sealed class CanvasRefactor
{
    private readonly ProjectModel _model;
    private readonly CanvasPreview _canvasPreview;

    public CanvasRefactor(ProjectModel model, CanvasPreview canvasPreview)
    {
        _model = model;
        _canvasPreview = canvasPreview;
    }

    /// <summary>
    /// 提取内联 Canvas 为独立文件（内联→独立）。
    /// </summary>
    public RefactorResult<CanvasExtractData> Extract(
        string path,
        int line)
    {
        if (line <= 0)
        {
            return RefactorResult<CanvasExtractData>.Fail("line 必须为 1-based 正整数");
        }

        string relativePath = WorkspacePath.RequireRelativePath(path);
        InlineScriptEntity? scriptEntity = _model.Find<InlineScriptEntity>(relativePath);
        if (scriptEntity == null)
        {
            return RefactorResult<CanvasExtractData>.Fail($"未找到脚本实体: {relativePath}");
        }

        string scriptText = scriptEntity.SourceText;

        CanvasParseResult parseResult = ParseCanvasArg(scriptText, line);
        if (parseResult is CanvasParseResult.Fail { Error: var parseError })
        {
            return RefactorResult<CanvasExtractData>.Fail(parseError);
        }

        (BescriptAst ast, BescriptAstNode node, BescriptCallArgument canvasArg) = (CanvasParseResult.Ok)parseResult;
        string decodedArg = canvasArg.DecodedText;

        // 必须是内联格式
        if (CanvasInlineArg.Parse(decodedArg) is null)
        {
            CanvasInlineArgParseErrorKind parseErrorKind = CanvasInlineArg.ClassifyError(decodedArg);
            if (BescriptSyntax.IsAnyRef(decodedArg))
            {
                return RefactorResult<CanvasExtractData>.Fail(GuidText.AlreadyGuidRefMessage());
            }

            return RefactorResult<CanvasExtractData>.Fail(FormatCanvasParseError(parseErrorKind));
        }

        CanvasNodeResult inlineResult = _canvasPreview.ParseInline(decodedArg);
        if (!inlineResult.Ok)
        {
            return RefactorResult<CanvasExtractData>.Fail(inlineResult.Diagnostics[0].Message);
        }

        string guid = GuidText.NewGuid();
        string canvasName = inlineResult.CanvasName;

        if (CanvasName.TryGetInvalidReason(canvasName) is { } invalidReason)
        {
            return RefactorResult<CanvasExtractData>.Fail($"内联 Canvas 名称无法提取为独立文件: {invalidReason}");
        }

        string uiDir = "ui";
        string safeName = canvasName;

        string canvasFile = GetAvailableCanvasPath(uiDir, safeName, guid);
        string relativeCanvasPath = WorkspacePath.RequireRelativePath(canvasFile);

        // AST 替换：内联 → guid 引用
        string newScriptText = RebuildScriptText(ast, node, BescriptSyntax.FormatLegacyRef(guid));
        byte[] canvasFileBytes = CanvasConverter.ToBinary(
            inlineResult.Root ?? throw new InvalidOperationException("CanvasNodeResult: Ok=true but Root is null"),
            canvasName);

        try
        {
            WritableProjectRootEntity writableRoot = _model.Root.ToWritable();

            string scriptDir = RelativePathUtils.GetParentDirectory(relativePath);
            string scriptFileName = RelativePathUtils.GetLastSegment(relativePath);
            ProjectEntity current = writableRoot;
            foreach (string segment in scriptDir.Split('/'))
                current = ((IHasChildren<ProjectEntity>)current).Children.Single(child => child.实体_无相对路径标识符 == segment);
            WritableScriptGroupEntity writableScriptGroup = (WritableScriptGroupEntity)current;
            WritableInlineScriptEntity writableScript = writableScriptGroup.Scripts
                .Cast<WritableInlineScriptEntity>()
                .Single(script => script.实体_无相对路径标识符 == scriptFileName);

            WritableDirectoryEntity writableUiDir;
            if (_model.Find<DirectoryEntity>(uiDir) is DirectoryEntity)
            {
                writableUiDir = writableRoot.Children
                    .OfType<WritableDirectoryEntity>()
                    .Single(dir => dir.实体_无相对路径标识符 == uiDir);
            }
            else
            {
                writableUiDir = new WritableDirectoryEntity(uiDir) { Parent = writableRoot };
                writableRoot.Children.Add(writableUiDir);
            }

            WritableCanvasEntity writableCanvas = new()
            {
                Parent = writableUiDir,
                Guid = guid,
                Data = canvasFileBytes,
                GuidJson = new GuidJsonRecord()
            };
            writableCanvas.GuidJson.SetGuid(guid);
            writableCanvas.SetIdentifierInternal(RelativePathUtils.GetLastSegment(relativeCanvasPath));
            writableUiDir.Children.Add(writableCanvas);
            writableScript.SourceText = newScriptText;

            IReadOnlyList<string> affectedPaths = _model.SaveChanges(writableRoot);
            return RefactorResult<CanvasExtractData>.Success(new CanvasExtractData(
                ExtractedGuid: guid,
                CanvasRelativePath: relativeCanvasPath,
                PatchedScriptRelativePath: relativePath,
                Applied: affectedPaths.Count > 0,
                AffectedPaths: affectedPaths.Distinct(StringComparer.Ordinal).ToArray()
            ));
        }
        catch (Exception ex) when (ex is not WorkspaceFileBlockedException and not WorkspaceStateChangedException)
        {
            return RefactorResult<CanvasExtractData>.Fail($"保存变更失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 将独立 Canvas 内联回脚本（独立→内联），并根据引用情况决定是否删除文件。
    /// </summary>
    public RefactorResult<CanvasInlineData> Inline(
        string path,
        int line)
    {
        if (line <= 0)
        {
            return RefactorResult<CanvasInlineData>.Fail("line 必须为 1-based 正整数");
        }

        string inlineRelativePath = WorkspacePath.RequireRelativePath(path);
        InlineScriptEntity? inlineScript = _model.Find<InlineScriptEntity>(inlineRelativePath);
        if (inlineScript == null)
        {
            return RefactorResult<CanvasInlineData>.Fail($"未找到脚本实体: {inlineRelativePath}");
        }

        string scriptText = inlineScript.SourceText;

        CanvasParseResult parseResult = ParseCanvasArg(scriptText, line);
        if (parseResult is CanvasParseResult.Fail { Error: var parseError })
        {
            return RefactorResult<CanvasInlineData>.Fail(parseError);
        }

        (BescriptAst ast, BescriptAstNode node, BescriptCallArgument canvasArg) = (CanvasParseResult.Ok)parseResult;
        string decodedArg = canvasArg.DecodedText;

        // 必须是 GUID 引用格式（旧版或新版）
        if (BescriptSyntax.TryExtractGuid(decodedArg) is not string guid)
        {
            return RefactorResult<CanvasInlineData>.Fail("该行已是内联格式，无需转换");
        }

        // 直接通过 GUID 获取 Canvas 实体
        if (_model.FindByGuid<CanvasEntity>(guid) is not CanvasEntity typedCanvas)
        {
            return RefactorResult<CanvasInlineData>.Fail(GuidText.NotFoundMessage("Canvas", guid));
        }

        string canvasRelativePath = typedCanvas.实体_完整相对路径标识;

        // 检查数据是否有效
        if (typedCanvas.Data.Length == 0)
        {
            return RefactorResult<CanvasInlineData>.Fail(GuidText.DataEmptyMessage("Canvas", guid));
        }

        CanvasNodeResult canvasNodeResult = CanvasConverter.FromBinary(typedCanvas.Data);
        if (!canvasNodeResult.Ok)
        {
            return RefactorResult<CanvasInlineData>.Fail(canvasNodeResult.Diagnostics[0].Message);
        }

        string canvasName = canvasNodeResult.CanvasName;
        string hjsonPayload = CanvasInlineNodeHjsonCodec.Serialize(
            canvasNodeResult.Root ?? throw new InvalidOperationException("CanvasNodeResult: Ok=true but Root is null"));

        // AST 替换：guid 引用 → hjson 内联
        string newDecodedText = CanvasInlineArg.BuildNodeHjsonRawText(canvasName, hjsonPayload);
        string newScriptText = RebuildScriptText(ast, node, newDecodedText);

        // 检查是否有其他引用
        bool canDeleteFile = false;
        List<GuidReferenceResult> refs = GuidReferenceSearchCore.SearchGuidReferences(_model, guid, 1000);

        // 排除当前脚本和 canvas 自身的引用
        GuidReferenceResult[] otherRefs = refs
                .Where(r =>
                {
                    string p = WorkspacePath.RequireRelativePathOrEmpty(r.Path);
                    if (p.StartsWith("temp/toolkit/", StringComparison.Ordinal)) return false;
                    if (string.Equals(p, inlineRelativePath, StringComparison.Ordinal)) return false;
                    if (string.Equals(p, canvasRelativePath, StringComparison.Ordinal)) return false;
                    if (string.Equals(p, canvasRelativePath + ProjectEntityLayout.GuidExt, StringComparison.Ordinal)) return false;
                    return true;
                })
                .ToArray();

        canDeleteFile = otherRefs.Length == 0;

        try
        {
            WritableProjectRootEntity writableRoot = _model.Root.ToWritable();

            string scriptDir = RelativePathUtils.GetParentDirectory(inlineRelativePath);
            string scriptFileName = RelativePathUtils.GetLastSegment(inlineRelativePath);
            ProjectEntity current = writableRoot;
            foreach (string segment in scriptDir.Split('/'))
                current = ((IHasChildren<ProjectEntity>)current).Children.Single(child => child.实体_无相对路径标识符 == segment);
            WritableScriptGroupEntity writableScriptGroup = (WritableScriptGroupEntity)current;
            WritableInlineScriptEntity writableScript = writableScriptGroup.Scripts
                .Cast<WritableInlineScriptEntity>()
                .Single(script => script.实体_无相对路径标识符 == scriptFileName);

            writableScript.SourceText = newScriptText;
            bool fileDeleted = false;

            if (canDeleteFile)
            {
                string canvasParentDir = RelativePathUtils.GetParentDirectory(canvasRelativePath);
                string canvasFileName = RelativePathUtils.GetLastSegment(canvasRelativePath);
                if (canvasParentDir == WorkspacePath.RootDirectory)
                {
                    WritableCanvasEntity writableCanvas = writableRoot.Children
                        .OfType<WritableCanvasEntity>()
                        .Single(canvas => canvas.实体_无相对路径标识符 == canvasFileName);
                    writableRoot.Children.Remove(writableCanvas);
                }
                else
                {
                    ProjectEntity canvasParent = writableRoot;
                    foreach (string segment in canvasParentDir.Split('/'))
                        canvasParent = ((IHasChildren<ProjectEntity>)canvasParent).Children.Single(child => child.实体_无相对路径标识符 == segment);
                    WritableDirectoryEntity writableCanvasParent = (WritableDirectoryEntity)canvasParent;
                    WritableCanvasEntity writableCanvas = writableCanvasParent.Children
                        .OfType<WritableCanvasEntity>()
                        .Single(canvas => canvas.实体_无相对路径标识符 == canvasFileName);
                    writableCanvasParent.Children.Remove(writableCanvas);
                }

                fileDeleted = true;
            }

            IReadOnlyList<string> affectedPaths = _model.SaveChanges(writableRoot);
            return RefactorResult<CanvasInlineData>.Success(new CanvasInlineData(
                InlinedCanvasName: canvasName,
                PatchedScriptRelativePath: inlineRelativePath,
                CanvasFileDeleted: fileDeleted,
                Applied: affectedPaths.Count > 0,
                AffectedPaths: affectedPaths.Distinct(StringComparer.Ordinal).ToArray()
            ));
        }
        catch (Exception ex) when (ex is not WorkspaceFileBlockedException and not WorkspaceStateChangedException)
        {
            return RefactorResult<CanvasInlineData>.Fail($"保存变更失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 通过 Canvas 文件路径导入：从路径解析出 GUID，再执行替换。
    /// </summary>
    public RefactorResult<CanvasImportData> ImportByPath(string path, int line, string sourceCanvasPath)
    {
        string sourceCanvasRelativePath = WorkspacePath.RequireRelativePath(sourceCanvasPath);
        if (_model.Find<CanvasEntity>(sourceCanvasRelativePath) is not CanvasEntity canvasEntity)
        {
            return RefactorResult<CanvasImportData>.Fail($"无法加载 Canvas: {sourceCanvasRelativePath}");
        }

        return ImportCore(path, line, canvasEntity.Guid);
    }

    /// <summary>
    /// 通过目标 GUID 直接导入：验证 GUID 格式后执行替换。
    /// </summary>
    public RefactorResult<CanvasImportData> ImportByGuid(string path, int line, string targetGuid)
    {
        if (!GuidText.Validate(targetGuid))
        {
            return RefactorResult<CanvasImportData>.Fail(GuidText.FormatErrorMessage("targetGuid", targetGuid));
        }

        return ImportCore(path, line, targetGuid);
    }

    private RefactorResult<CanvasImportData> ImportCore(string path, int line, string targetGuidValue)
    {
        if (line <= 0)
        {
            return RefactorResult<CanvasImportData>.Fail("line 必须为 1-based 正整数");
        }

        string importRelativePath = WorkspacePath.RequireRelativePath(path);
        InlineScriptEntity? importScript = _model.Find<InlineScriptEntity>(importRelativePath);
        if (importScript == null)
        {
            return RefactorResult<CanvasImportData>.Fail($"未找到脚本实体: {importRelativePath}");
        }

        string scriptText = importScript.SourceText;

        CanvasParseResult parseResult = ParseCanvasArg(scriptText, line);
        if (parseResult is CanvasParseResult.Fail { Error: var parseError })
        {
            return RefactorResult<CanvasImportData>.Fail(parseError);
        }

        (BescriptAst ast, BescriptAstNode node, BescriptCallArgument canvasArg) = (CanvasParseResult.Ok)parseResult;
        string decodedArg = canvasArg.DecodedText;

        bool isGuidRef = BescriptSyntax.IsAnyRef(decodedArg);
        string existingGuid = string.Empty;

        if (isGuidRef)
        {
            if (BescriptSyntax.TryExtractGuid(decodedArg) is not string extractedGuid)
            {
                return RefactorResult<CanvasImportData>.Fail($"当前行的 GUID 格式不正确");
            }
            existingGuid = extractedGuid;
        }
        else
        {
            if (CanvasInlineArg.Parse(decodedArg) is null)
            {
                return RefactorResult<CanvasImportData>.Fail(FormatCanvasParseError(CanvasInlineArg.ClassifyError(decodedArg)));
            }
        }

        if (isGuidRef && existingGuid.Equals(targetGuidValue, StringComparison.Ordinal))
        {
            return RefactorResult<CanvasImportData>.Fail($"新旧 GUID 相同，无需替换: {targetGuidValue}");
        }

        if (_model.FindByGuid<CanvasEntity>(targetGuidValue) == null)
        {
            return RefactorResult<CanvasImportData>.Fail(GuidText.NotFoundMessage("Canvas", targetGuidValue));
        }

        string newScriptText = RebuildScriptText(ast, node, BescriptSyntax.FormatLegacyRef(targetGuidValue));

        List<string> affectedPaths = [];
        bool applied = false;

        try
        {
            IReadOnlyList<string> scriptPaths = EntityWriteScope.ApplyContentChange<InlineScriptEntity, WritableInlineScriptEntity>(
                _model,
                importScript,
                script => script.ToWritable(),
                writableImport => writableImport.SourceText = newScriptText);
            affectedPaths.AddRange(scriptPaths);
            applied = affectedPaths.Count > 0;
        }
        catch (Exception ex) when (ex is not WorkspaceFileBlockedException and not WorkspaceStateChangedException)
        {
            return RefactorResult<CanvasImportData>.Fail($"保存变更失败: {ex.Message}");
        }

        return RefactorResult<CanvasImportData>.Success(new CanvasImportData(
            LinkedGuid: targetGuidValue,
            PatchedScriptRelativePath: importRelativePath,
            Applied: applied,
            AffectedPaths: affectedPaths.Distinct(StringComparer.Ordinal).ToArray()
        ));
    }

    private abstract record CanvasParseResult
    {
        private CanvasParseResult() { }

        public sealed record Ok(
            BescriptAst Ast,
            BescriptAstNode Node,
            BescriptCallArgument CanvasArg
        ) : CanvasParseResult;

        public sealed record Fail(string Error) : CanvasParseResult;
    }

    #region AST 操作

    /// <summary>
    /// 解析脚本并查找目标行的 create.canvas 节点和 Canvas 参数。
    /// </summary>
    private static CanvasParseResult ParseCanvasArg(
        string scriptText,
        int targetLine)
    {
        BescriptScriptInfo info;
        try
        {
            info = BescriptTextParser.Parse(string.Empty, scriptText).Script;
        }
        catch (Exception ex)
        {
            return new CanvasParseResult.Fail($"脚本解析失败: {ex.Message}");
        }

        BescriptAst ast = info.Ast;
        BescriptAstNode? found = FindCanvasCallNode(ast.RootNodes, targetLine);
        if (found is not IHasCall hasCall)
        {
            return new CanvasParseResult.Fail(BuildInvalidLineError(targetLine, scriptText));
        }

        foreach (BescriptCallArgument arg in hasCall.Call.Arguments)
        {
            if (string.Equals(arg.TypeName, "Canvas", StringComparison.Ordinal))
            {
                return new CanvasParseResult.Ok(ast, found, arg);
            }
        }

        return new CanvasParseResult.Fail($"line {targetLine} 的 create.canvas 调用中未找到 Canvas 类型参数");
    }

    /// <summary>
    /// 替换 AST 中目标节点的 Canvas 参数，序列化为新的脚本文本。
    /// </summary>
    private static string RebuildScriptText(BescriptAst ast, BescriptAstNode targetNode, string newDecodedText)
    {
        BescriptCallSignature sig = ((IHasCall)targetNode).Call;
        List<BescriptCallArgument> newArgs = new(sig.Arguments.Count);
        bool replaced = false;
        foreach (BescriptCallArgument arg in sig.Arguments)
        {
            if (!replaced && string.Equals(arg.TypeName, "Canvas", StringComparison.Ordinal))
            {
                newArgs.Add(arg with { DecodedText = newDecodedText });
                replaced = true;
            }
            else
            {
                newArgs.Add(arg);
            }
        }

        BescriptAstNode newNode = targetNode.WithCallAndChildren(sig with { Arguments = newArgs }, targetNode.Children);
        IReadOnlyList<BescriptAstNode> newRootNodes = ReplaceNodeInTree(ast.RootNodes, targetNode, newNode);
        BescriptAst newAst = ast with { RootNodes = newRootNodes };
        return BescriptAstSerializer.SerializeFull(newAst);
    }

    /// <summary>
    /// 递归替换 AST 树中的目标节点。若无替换则返回原始引用。
    /// </summary>
    private static IReadOnlyList<BescriptAstNode> ReplaceNodeInTree(
        IReadOnlyList<BescriptAstNode> nodes,
        BescriptAstNode target,
        BescriptAstNode replacement)
    {
        List<BescriptAstNode> result = new(nodes.Count);
        bool found = false;
        foreach (BescriptAstNode node in nodes)
        {
            if (ReferenceEquals(node, target))
            {
                result.Add(replacement);
                found = true;
            }
            else if (node.Children.Count > 0)
            {
                IReadOnlyList<BescriptAstNode> newChildren = ReplaceNodeInTree(node.Children, target, replacement);
                if (!ReferenceEquals(newChildren, node.Children))
                {
                    result.Add(node.WithChildren(newChildren));
                    found = true;
                }
                else
                {
                    result.Add(node);
                }
            }
            else
            {
                result.Add(node);
            }
        }
        return found ? result : nodes;
    }

    private static BescriptAstNode? FindCanvasCallNode(IReadOnlyList<BescriptAstNode> nodes, int targetLine)
    {
        foreach (BescriptAstNode node in nodes)
        {
            if (node.Line == targetLine &&
                node is IHasCall hasCall &&
                hasCall.Call.Kind == BescriptCallKind.Fun &&
                string.Equals(hasCall.Call.FunctionName, "create.canvas", StringComparison.Ordinal))
            {
                return node;
            }

            if (node.Children.Count > 0)
            {
                BescriptAstNode? found = FindCanvasCallNode(node.Children, targetLine);
                if (found != null) return found;
            }
        }
        return null;
    }

    #endregion

    #region 错误信息

    /// <summary>
    /// 构建无效行号的错误信息，包含原始 .code 文件中所有合法的 create.canvas 行号。
    /// </summary>
    private static string BuildInvalidLineError(int requestedLine, string scriptText)
    {
        BescriptScanner scanner = new();
        IReadOnlyList<BescriptCanvasArgRange> canvasRanges = scanner.ScanCanvasArgs(scriptText);

        if (canvasRanges.Count == 0)
        {
            return $"line {requestedLine} 无效（原始 .code 文件行号），当前文件中不存在任何 create.canvas 调用";
        }

        int[] validLines = canvasRanges.Select(static r => r.Range.Line).Distinct().OrderBy(static l => l).ToArray();
        string linesStr = string.Join(", ", validLines);
        return $"line {requestedLine} 无效（原始 .code 文件行号），合法行号: {linesStr}";
    }

    private static string FormatCanvasParseError(CanvasInlineArgParseErrorKind errorKind)
    {
        return errorKind switch
        {
            CanvasInlineArgParseErrorKind.Empty => "Canvas 参数为空",
            CanvasInlineArgParseErrorKind.NotCanvasPrefix => "Canvas 参数格式不正确",
            CanvasInlineArgParseErrorKind.MissingEquals => "Canvas 参数缺少 '=' 分隔符",
            CanvasInlineArgParseErrorKind.EmptyBase64 => "Canvas Base64 为空",
            CanvasInlineArgParseErrorKind.MissingNodeHjsonTerminator => "新版内联 Canvas 缺少 == 结束标记",
            CanvasInlineArgParseErrorKind.EmptyNodeHjson => "新版内联 Canvas Hjson 为空",
            _ => "Canvas 参数格式不正确"
        };
    }

    #endregion

    #region 文件名工具

    private string GetAvailableCanvasPath(string uiDir, string baseName, string guid)
    {
        string name = baseName;
        int attempt = 0;
        while (true)
        {
            string file = RelativePathUtils.Combine(uiDir, name + ProjectEntityLayout.CanvasExt);

            // 通过实体模型检查文件是否存在
            if (!_model.EntityExists(file))
            {
                return file;
            }

            attempt++;
            name = $"{baseName}_{guid[..8]}";
            if (attempt != 1)
            {
                name = $"{baseName}_{guid[..8]}_{attempt}";
            }
        }
    }

    #endregion
}
