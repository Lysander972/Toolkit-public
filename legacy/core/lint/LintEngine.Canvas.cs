using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Canvas;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    /// <summary>
    /// 验证 Canvas 格式（二进制文件和内联 Canvas）
    /// 架构：解析为 Node → 统一调用 ValidateNode 检查
    /// </summary>
    private static IReadOnlyList<LintDiagnostic> ValidateCanvasFormats(ProjectModel model)
    {
        List<LintDiagnostic> diagnostics = [];
        BescriptScanner scanner = new();

        foreach (CanvasEntity canvas in model.List<CanvasEntity>())
        {
            // 1. 解析为 Node（纯净转换，无验证）
            CanvasNodeResult result = CanvasPreview.ParseBytesStatic(canvas.Data);
            if (!result.Ok)
            {
                string message = $"Canvas 结构错误: {result.Diagnostics[0].Message}";
                diagnostics.Add(new LintDiagnostic(
                    LintRuleIds.StructCanvasMalformed,
                    LintSeverity.Error,
                    message,
                    new LintLocation(canvas.实体_完整相对路径标识, 1)));
                continue;
            }

            // 2. 统一校验（阶段1字段 + 阶段2布局模拟）
            if (result.Root != null)
            {
                diagnostics.AddRange(ValidateCanvasTextCharacters(result.Root, canvas.实体_完整相对路径标识, 1));
                List<string> errors = CanvasViewValidation.ValidateLayout(result.Root);
                foreach (string error in errors)
                {
                    diagnostics.Add(new LintDiagnostic(
                        LintRuleIds.StructCanvasMalformed,
                        LintSeverity.Error,
                        $"Canvas 校验失败: {error}",
                        new LintLocation(canvas.实体_完整相对路径标识, 1)));
                }
            }
        }

        foreach (InlineScriptEntity script in model.List<InlineScriptEntity>())
        {
            IReadOnlyList<BescriptCanvasArgRange> canvasArgs = scanner.ScanCanvasArgs(script.SourceText);
            foreach (BescriptCanvasArgRange canvasArg in canvasArgs)
            {
                if (canvasArg is BescriptGuidCanvasArgRange)
                    continue;

                // 1. 解析为 Node（纯净转换，无验证）
                CanvasNodeResult result = CanvasPreview.ParseInlineStatic(canvasArg.RawArg);
                if (!result.Ok)
                {
                    string message = $"内联 Canvas 结构错误: {result.Diagnostics[0].Message}";
                    diagnostics.Add(new LintDiagnostic(
                        LintRuleIds.StructCanvasMalformed,
                        LintSeverity.Error,
                        message,
                        new LintLocation(script.实体_完整相对路径标识, canvasArg.Range.Line)));
                    continue;
                }

                // 2. 统一校验（阶段1字段 + 阶段2布局模拟）
                if (result.Root != null)
                {
                    diagnostics.AddRange(ValidateCanvasTextCharacters(result.Root, script.实体_完整相对路径标识, canvasArg.Range.Line));
                    List<string> errors = CanvasViewValidation.ValidateLayout(result.Root);
                    foreach (string error in errors)
                    {
                        diagnostics.Add(new LintDiagnostic(
                            LintRuleIds.StructCanvasMalformed,
                            LintSeverity.Error,
                            $"Canvas 校验失败: {error}",
                            new LintLocation(script.实体_完整相对路径标识, canvasArg.Range.Line)));
                    }
                }
            }
        }

        return diagnostics;
    }

    private static IReadOnlyList<LintDiagnostic> ValidateCanvasUiPathReferences(ProjectModel model, DeviceEntityBase device, ScriptAnalysisService analysisService)
    {
        Dictionary<string, HashSet<string>> pathsByType = new(StringComparer.Ordinal);
        foreach (CanvasEntity canvas in model.List<CanvasEntity>())
        {
            CanvasNodeResult result = CanvasPreview.ParseBytesStatic(canvas.Data);
            if (result.Root != null) UnionCanvasPaths(pathsByType, result.Root);
        }
        List<LintDiagnostic> diagnostics = [];
        void CollectInlineCanvasPaths(string scriptRelativePath)
        {
            BescriptAst ast = (analysisService.ParseScript(scriptRelativePath)
                ?? throw new InvalidOperationException($"脚本解析失败: {scriptRelativePath}")).Ast;
            void Walk(BescriptAstNode node)
            {
                if (node.Kind == BescriptStatementKind.Call && node is IHasCall { Call: var call })
                {
                    if (string.Equals(call.FunctionName, "create.canvas", StringComparison.Ordinal) && call.Arguments.Count > 0 && !call.Arguments[0].IsVariable)
                    {
                        string raw = call.Arguments[0].DecodedText;
                        if (BescriptSyntax.TryExtractGuid(raw) is null && CanvasPreview.ParseInlineStatic(raw).Root is { } root)
                            UnionCanvasPaths(pathsByType, root);
                    }
                }
                foreach (BescriptAstNode child in node.Children) Walk(child);
            }
            foreach (BescriptAstNode node in ast.RootNodes) Walk(node);
        }
        void CheckScript(string scriptRelativePath)
        {
            BescriptAst ast = (analysisService.ParseScript(scriptRelativePath)
                ?? throw new InvalidOperationException($"脚本解析失败: {scriptRelativePath}")).Ast;
            void Walk(BescriptAstNode node)
            {
                if (node.Kind == BescriptStatementKind.Call && node is IHasCall { Call: var call })
                {
                    string functionName = call.FunctionName;
                    if (call is VarfCallSignature varf && string.Equals(varf.OwnerType, "Canvas", StringComparison.Ordinal) && functionName.StartsWith("readUI", StringComparison.Ordinal) && call.Arguments.Count > 0)
                    {
                        BescriptCallArgument arg = call.Arguments[0];
                        string uiType = functionName[4..];
                        bool isStringConst = !arg.IsVariable && ((arg.VarType.HasValue && arg.VarType.Value == BescriptVarType.String) || string.Equals(arg.TypeName, "String", StringComparison.Ordinal));
                        if (isStringConst && (!pathsByType.TryGetValue(uiType, out HashSet<string>? knownPaths) || !knownPaths.Contains(arg.DecodedText)))
                        {
                            diagnostics.Add(new LintDiagnostic(LintRuleIds.CallCanvasUiPathNotFound, LintSeverity.Error, $"Canvas 路径不存在或类型不匹配: {arg.DecodedText}。若最近修改了 UI 组件名称，应注意逻辑中的读取路径也需要同步更新。", new LintLocation(scriptRelativePath, node.Line)));
                        }
                    }
                }
                foreach (BescriptAstNode child in node.Children) Walk(child);
            }
            foreach (BescriptAstNode node in ast.RootNodes) Walk(node);
        }
        foreach (InlineScriptEntity script in model.List<InlineScriptEntity>()) CollectInlineCanvasPaths(script.实体_完整相对路径标识);
        foreach (CodeRepoScriptEntity script in model.ListCodeRepoScripts()) CollectInlineCanvasPaths(script.实体_完整相对路径标识);
        foreach (InlineScriptEntityBase script in device.AllScripts) CheckScript(script.实体_完整相对路径标识);
        return diagnostics;
    }

    private static void UnionCanvasPaths(Dictionary<string, HashSet<string>> pathsByType, CanvasNode root)
    {
        void Walk(CanvasNode node, string prefix)
        {
            string path = prefix;
            string name = node.name.v;
#pragma warning disable TK011
            // Canvas 节点可能无名称（空名称继承父路径）
            if (name != string.Empty)
            {
                // 根级节点路径即节点名，无前缀
                if (prefix == string.Empty)
                    path = name;
                else
                    path = prefix + "/" + name;
            }// 空路径（根节点无名）不记录
            if (path != string.Empty && GetCanvasUiType(node) is { } uiType)
#pragma warning restore TK011
            {
                if (!pathsByType.TryGetValue(uiType, out HashSet<string>? set)) pathsByType[uiType] = set = new(StringComparer.Ordinal);
                set.Add(path);
            }
            foreach (CanvasNode child in node.Children) Walk(child, path);
        }
        foreach (CanvasNode child in root.Children) Walk(child, string.Empty);
    }

    private static string? GetCanvasUiType(CanvasNode node)
    {
        return node switch
        {
            UIRectNode => "UIRect",
            UIScrollRectNode => "UIRect",
            UILabelNode => "UILabel",
            UITextProNode => "UILabel",
            UIButtonNode => "UIButton",
            UIInputNode => "UIInput",
            UIRawImageNode => "UIRawImage",
            UISpineNode => "UISpine",
            _ => null
        };
    }
}
