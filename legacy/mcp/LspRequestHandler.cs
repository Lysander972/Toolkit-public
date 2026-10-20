using System.Text.Json;
using System.Text.Json.Nodes;
using Toolkit.Core;
using Toolkit.Core.Infra;

using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Persistence;
using Toolkit.Core.Refactor;
using Toolkit.Core.Sdk;

namespace Toolkit.Mcp;

public sealed class LspRequestHandler
{
    private readonly ToolkitServices _services;

    public LspRequestHandler(ToolkitServices services)
    {
        _services = services;
    }

    public JsonObject Handle(JsonObject request)
    {
        string? method = request["method"]?.GetValue<string>();
        JsonNode? id = request["id"];
        JsonNode? @params = request["params"];

        if (method == null)
            return MakeError(id, -32601, "Method not found");

        try
        {
            if (method == "initialize")
                return MakeResponse(id, HandleInitialize());

            // 统一语义：未知方法 → Method not found。已知方法无论是否可处理，都返回该方法对应的合法空结果，
            // 绝不以错误码替代——definition/references/hover/rename/signatureHelp 的合法空结果即 JSON null
            // （LSP 规范允许），semanticTokens 等为各自空结构。result == null 是部分方法的正常空结果，不是错误。
            if (!IsKnownMethod(method))
                return MakeError(id, -32601, $"Method not found: {method}");

            // 系统边界：解析 textDocument 并校验文件归属与文档文本。
            // 不可处理（参数缺失、文件不在工作区、缺文档文本）走 EmptyResultFor。内部逻辑零边界检查。
            JsonNode? result;
            if (@params is JsonObject paramsObj && ResolveDocument(paramsObj) is TextDocumentInfo doc)
            {
                result = method switch
                {
                    "textDocument/definition" => HandleDefinition(doc, paramsObj),
                    "textDocument/references" => HandleReferences(doc, paramsObj),
                    "textDocument/hover" => HandleHover(doc, paramsObj),
                    "textDocument/semanticTokens/full" => HandleSemanticTokensFull(doc),
                    "textDocument/diagnostic" => HandleDiagnostic(doc),
                    "textDocument/rename" => HandleRename(doc, paramsObj),
                    "textDocument/documentLink" => HandleDocumentLink(doc),
                    "textDocument/completion" => HandleCompletion(),
                    "textDocument/documentSymbol" => HandleDocumentSymbol(doc),
                    "textDocument/signatureHelp" => HandleSignatureHelp(doc, paramsObj),
                    _ => null // 不可达：未知方法已在 IsKnownMethod 过滤
                };
            }
            else
            {
                result = EmptyResultFor(method);
            }

            return MakeResponse(id, result);
        }
        catch (Exception ex)
        {
            Log.For(LogModule.Mcp).Exception(ex, $"LSP handler error: {method}");
            return MakeError(id, -32603, ex.Message);
        }
    }

    private BescriptActCallRange? FindActCallAtPosition(string text, int line, int character)
    {
        IReadOnlyList<BescriptActCallRange> calls = _services.BescriptScanner.ScanActCalls(text);
        foreach (BescriptActCallRange call in calls)
        {
            int callLine = call.Range.Line - 1;
            if (callLine != line) continue;
            if (character >= call.Range.Column && character < call.Range.Column + call.Range.Length)
                return call;
        }
        return null;
    }

    #region initialize

    private static JsonObject HandleInitialize()
    {
        return new JsonObject
        {
            ["capabilities"] = new JsonObject
            {
                ["definitionProvider"] = true,
                ["referencesProvider"] = true,
                ["hoverProvider"] = true,
                ["renameProvider"] = new JsonObject { ["prepareProvider"] = true },
                ["documentLinkProvider"] = new JsonObject(),
                ["completionProvider"] = new JsonObject
                {
                    ["triggerCharacters"] = new JsonArray(".", " ")
                },
                ["documentSymbolProvider"] = true,
                ["signatureHelpProvider"] = new JsonObject
                {
                    ["triggerCharacters"] = new JsonArray("(", ",")
                },
                ["semanticTokensProvider"] = new JsonObject
                {
                    ["legend"] = new JsonObject
                    {
                        ["tokenTypes"] = new JsonArray("function", "variable", "parameter", "string", "keyword", "number", "class", "type", "comment", "disabled"),
                        ["tokenModifiers"] = new JsonArray("declaration", "defaultLibrary", "readonly")
                    },
                    ["full"] = true
                },
                ["textDocumentSync"] = new JsonObject
                {
                    ["openClose"] = true,
                    ["change"] = 1
                }
            }
        };
    }

    #endregion

    #region textDocument/definition

    private JsonObject? HandleDefinition(TextDocumentInfo doc, JsonObject @params)
    {
        PositionInfo? pos = ExtractPosition(@params);
        if (pos == null) return null;

        // ActCall → 跳转到脚本定义
        BescriptActCallRange? hit = FindActCallAtPosition(doc.Text, pos.Line, pos.Character);
        if (hit == null) return null;

        IReadOnlyList<string> paths = _services.Logic.FindScriptPathsByName(hit.ScriptName);
        if (paths.Count == 0) return null;

        if (paths.Count == 1)
        {
            return new JsonObject
            {
                ["uri"] = ToFileUri(paths[0]),
                ["range"] = ToRange(0, 0, 0, 0)
            };
        }

        JsonArray locations = [];
        foreach (string p in paths)
        {
            locations.Add(new JsonObject
            {
                ["uri"] = ToFileUri(p),
                ["range"] = ToRange(0, 0, 0, 0)
            });
        }
        return new JsonObject { ["result"] = locations };
    }

    #endregion

    #region textDocument/references

    private JsonObject? HandleReferences(TextDocumentInfo doc, JsonObject @params)
    {
        PositionInfo? pos = ExtractPosition(@params);
        if (pos == null) return null;

        BescriptActCallRange? hit = FindActCallAtPosition(doc.Text, pos.Line, pos.Character);
        if (hit == null) return null;

        IReadOnlyList<SymbolReference> refs = _services.Logic.FindSymbolReferences(hit.ScriptName);
        JsonArray results = [];
        foreach (SymbolReference r in refs)
        {
            results.Add(new JsonObject
            {
                ["uri"] = ToFileUri(r.ScriptRelativePath),
                ["range"] = ToRange(r.Line - 1, 0, r.Line - 1, 0)
            });
        }
        return new JsonObject { ["result"] = results };
    }

    #endregion

    #region textDocument/hover

    private JsonObject? HandleHover(TextDocumentInfo doc, JsonObject @params)
    {
        PositionInfo? pos = ExtractPosition(@params);
        if (pos == null) return null;

        IReadOnlyList<BescriptBuiltinMethodRange> methods = _services.BescriptScanner.ScanBuiltinMethodCalls(doc.Text);
        foreach (BescriptBuiltinMethodRange m in methods)
        {
            int line = m.Range.Line - 1;
            if (pos.Line != line) continue;
            if (pos.Character < m.Range.Column || pos.Character >= m.Range.Column + m.Range.Length)
                continue;

            (SdkDocsQueryData? data, IReadOnlyList<ToolError> _, IReadOnlyList<ToolError> _) = _services.SdkDocsQuery.Query(new SdkDocsQueryArgs("titleRegex", m.MethodName, null, null, "bes"));
            if (data?.Docs != null && data.Docs.Count > 0)
            {
                SdkDocHit hit = data.Docs[0];
                return new JsonObject
                {
                    ["contents"] = new JsonObject
                    {
                        ["kind"] = "markdown",
                        ["value"] = $"```bescript\n{m.MethodName}\n```\n\n{hit.Snippet}"
                    },
                    ["range"] = ToRange(m.Range)
                };
            }

            return new JsonObject
            {
                ["contents"] = new JsonObject { ["kind"] = "plaintext", ["value"] = m.MethodName },
                ["range"] = ToRange(m.Range)
            };
        }

        BescriptActCallRange? actHit = FindActCallAtPosition(doc.Text, pos.Line, pos.Character);
        if (actHit != null)
        {
            IReadOnlyList<string> paths = _services.Logic.FindScriptPathsByName(actHit.ScriptName);
            string hoverText = paths.Count switch
            {
                0 => $"{actHit.ScriptName} (unresolved)",
                1 => $"{actHit.ScriptName} → {paths[0]}",
                _ => $"{actHit.ScriptName} ({paths.Count} definitions)"
            };

            return new JsonObject
            {
                ["contents"] = new JsonObject { ["kind"] = "plaintext", ["value"] = hoverText },
                ["range"] = ToRange(actHit.Range)
            };
        }

        return null;
    }

    #endregion

    #region textDocument/semanticTokens/full

    private JsonObject HandleSemanticTokensFull(TextDocumentInfo doc)
    {
        string[] globalVarNames = Array.Empty<string>();
        ScriptOwnership ownership = _services.Logic.GetScriptOwnership(doc.RelativePath);
        if (ownership.BelongsToDevice && ownership.DeviceRelativePath != null)
        {
            RefactorResult<GlobalVarListData> listResult = _services.GlobalVarOperation.List(ownership.DeviceRelativePath);
            if (listResult.Data != null)
                globalVarNames = listResult.Data.Variables.Select(v => v.Name).ToArray();
        }

        IReadOnlyList<BescriptSemanticToken> tokens = _services.BescriptScanner.ScanSemanticTokens(doc.Text, globalVarNames);

        JsonArray data = [];
        int prevLine = 0, prevChar = 0;

        foreach (BescriptSemanticToken t in tokens.OrderBy(t => t.Line).ThenBy(t => t.Column))
        {
            int line = t.Line - 1;
            int deltaLine = line - prevLine;
            int deltaChar = t.Column;
            if (deltaLine == 0)
                deltaChar -= prevChar;

            data.Add(deltaLine);
            data.Add(deltaChar);
            data.Add(t.Length);
            data.Add(t.Type);
            data.Add(t.Modifiers);

            prevLine = line;
            prevChar = t.Column;
        }

        return new JsonObject { ["data"] = data };
    }

    #endregion

    #region textDocument/rename

    private JsonObject? HandleRename(TextDocumentInfo doc, JsonObject @params)
    {
        PositionInfo? pos = ExtractPosition(@params);
        if (pos == null) return null;

        string? newName = @params["newName"]?.GetValue<string>();
        if (string.IsNullOrEmpty(newName)) return null;

        BescriptActCallRange? actHit = FindActCallAtPosition(doc.Text, pos.Line, pos.Character);
        if (actHit != null)
        {
            RefactorResult<IdentifierRenameData> result = _services.IdentifierRename.Rename(doc.RelativePath, actHit.ScriptName, newName);
            if (!result.IsSuccess || result.Data == null || !result.Data.Applied)
                return new JsonObject { ["changes"] = new JsonObject() };

            return BuildWorkspaceEditFromChanges(doc.Uri, result.Data.Changes);
        }

        ScriptOwnership ownership = _services.Logic.GetScriptOwnership(doc.RelativePath);
        if (ownership.BelongsToDevice && ownership.DeviceRelativePath != null)
        {
            RefactorResult<GlobalVarListData> listResult = _services.GlobalVarOperation.List(ownership.DeviceRelativePath);
            if (listResult.Data != null)
            {
                string[] varNames = listResult.Data.Variables.Select(v => v.Name).ToArray();
                IReadOnlyList<BescriptGlobalVarRange> varRanges = _services.BescriptScanner.ScanGlobalVarRefs(doc.Text, varNames);

                foreach (BescriptGlobalVarRange vr in varRanges)
                {
                    int line = vr.Range.Line - 1;
                    if (pos.Line != line) continue;
                    if (pos.Character < vr.Range.Column || pos.Character >= vr.Range.Column + vr.Range.Length)
                        continue;

                    RefactorResult<IdentifierRenameData> renameResult = _services.IdentifierRename.Rename(doc.RelativePath, vr.Name, newName);
                    if (!renameResult.IsSuccess || renameResult.Data == null || !renameResult.Data.Applied)
                        return new JsonObject { ["changes"] = new JsonObject() };

                    return BuildWorkspaceEditFromChanges(doc.Uri, renameResult.Data.Changes);
                }
            }
        }

        RefactorResult<IdentifierRenameData> idResult = _services.IdentifierRename.Rename(doc.RelativePath, ExtractWordAtPosition(doc.Text, pos.Line, pos.Character), newName);
        if (!idResult.IsSuccess || idResult.Data == null || !idResult.Data.Applied)
            return new JsonObject { ["changes"] = new JsonObject() };

        return BuildWorkspaceEditFromChanges(doc.Uri, idResult.Data.Changes);
    }

    private JsonObject BuildWorkspaceEditFromChanges(string uri, IReadOnlyList<IdentifierChange> changes)
    {
        JsonArray edits = [];
        foreach (IdentifierChange c in changes)
        {
            int line = c.Line - 1;
            edits.Add(new JsonObject
            {
                ["range"] = ToRange(line, c.Column, line, c.Column + c.Length),
                ["newText"] = string.Empty
            });
        }
        return new JsonObject { ["changes"] = new JsonObject { [uri] = edits } };
    }

    #endregion

    #region textDocument/documentLink

    private JsonObject HandleDocumentLink(TextDocumentInfo doc)
    {
        JsonArray links = [];

        IReadOnlyList<BescriptActCallRange> actCalls = _services.BescriptScanner.ScanActCalls(doc.Text);
        foreach (BescriptActCallRange call in actCalls)
        {
            IReadOnlyList<string> paths = _services.Logic.FindScriptPathsByName(call.ScriptName);
            if (paths.Count == 1)
            {
                links.Add(new JsonObject
                {
                    ["range"] = ToRange(call.Range),
                    ["target"] = ToFileUri(paths[0]),
                    ["tooltip"] = $"Go to {call.ScriptName}"
                });
            }
        }

        IReadOnlyList<BescriptCanvasArgRange> canvasArgs = _services.BescriptScanner.ScanCanvasArgs(doc.Text);
        foreach (BescriptCanvasArgRange ca in canvasArgs)
        {
            if (ca is BescriptGuidCanvasArgRange guidArg)
            {
                links.Add(new JsonObject
                {
                    ["range"] = ToRange(ca.Range),
                    ["tooltip"] = $"Canvas (GUID: {guidArg.Guid[..Math.Min(8, guidArg.Guid.Length)]}...)"
                });
            }
        }

        return new JsonObject { ["result"] = links };
    }

    #endregion

    #region textDocument/completion

    private JsonObject HandleCompletion()
    {
        JsonArray items = [];

        IReadOnlyList<ScriptDocumentInfo> scripts = _services.Logic.GetAllScriptDocuments();
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (ScriptDocumentInfo s in scripts)
        {
            string name = s.Header.Name;
            if (!seen.Add(name)) continue;

            items.Add(new JsonObject
            {
                ["label"] = name,
                ["kind"] = 3,
                ["detail"] = s.RelativePath
            });
        }

        (SdkDocsQueryData? data, IReadOnlyList<ToolError> _, IReadOnlyList<ToolError> _) = _services.SdkDocsQuery.Query(new SdkDocsQueryArgs("listPaths", null, null, null, "bes"));
        if (data?.Paths != null)
        {
            foreach (string path in data.Paths)
            {
                items.Add(new JsonObject { ["label"] = path, ["kind"] = 2 });
            }
        }

        return new JsonObject { ["items"] = items };
    }

    #endregion

    #region textDocument/documentSymbol

    private JsonObject HandleDocumentSymbol(TextDocumentInfo doc)
    {
        JsonArray symbols = [];

        BescriptParseData? parseData = _services.Analysis.ParseScript(doc.RelativePath);
        if (parseData?.Ast != null && parseData.Ast.Header.Name.Length > 0)
        {
            symbols.Add(new JsonObject
            {
                ["name"] = parseData.Ast.Header.Name,
                ["kind"] = 12,
                ["range"] = ToRange(0, 0, 0, 0),
                ["selectionRange"] = ToRange(0, 0, 0, Math.Min(parseData.Ast.Header.Name.Length, 40))
            });
        }

        IReadOnlyList<BescriptActCallRange> actCalls = _services.BescriptScanner.ScanActCalls(doc.Text);
        foreach (BescriptActCallRange act in actCalls)
        {
            symbols.Add(new JsonObject
            {
                ["name"] = act.ScriptName,
                ["kind"] = 6,
                ["range"] = ToRange(act.Range),
                ["selectionRange"] = ToRange(act.Range)
            });
        }

        IReadOnlyList<BescriptBuiltinMethodRange> builtinCalls = _services.BescriptScanner.ScanBuiltinMethodCalls(doc.Text);
        foreach (BescriptBuiltinMethodRange m in builtinCalls)
        {
            symbols.Add(new JsonObject
            {
                ["name"] = m.MethodName,
                ["kind"] = 6,
                ["range"] = ToRange(m.Range),
                ["selectionRange"] = ToRange(m.Range)
            });
        }

        ScriptOwnership ownership = _services.Logic.GetScriptOwnership(doc.RelativePath);
        if (ownership.BelongsToDevice && ownership.DeviceRelativePath != null)
        {
            RefactorResult<GlobalVarListData> listResult = _services.GlobalVarOperation.List(ownership.DeviceRelativePath);
            if (listResult.Data != null)
            {
                string[] varNames = listResult.Data.Variables.Select(v => v.Name).ToArray();
                IReadOnlyList<BescriptGlobalVarRange> varRanges = _services.BescriptScanner.ScanGlobalVarRefs(doc.Text, varNames);
                foreach (BescriptGlobalVarRange vr in varRanges)
                {
                    symbols.Add(new JsonObject
                    {
                        ["name"] = vr.Name,
                        ["kind"] = 8,
                        ["range"] = ToRange(vr.Range),
                        ["selectionRange"] = ToRange(vr.Range)
                    });
                }
            }
        }

        return new JsonObject { ["result"] = symbols };
    }

    #endregion

    #region textDocument/signatureHelp

    private JsonObject? HandleSignatureHelp(TextDocumentInfo doc, JsonObject @params)
    {
        PositionInfo? pos = ExtractPosition(@params);
        if (pos == null) return null;

        IReadOnlyList<BescriptBuiltinMethodRange> methods = _services.BescriptScanner.ScanBuiltinMethodCalls(doc.Text);
        foreach (BescriptBuiltinMethodRange m in methods)
        {
            int line = m.Range.Line - 1;
            if (pos.Line != line) continue;

            (SdkDocsQueryData? data, IReadOnlyList<ToolError> _, IReadOnlyList<ToolError> _) = _services.SdkDocsQuery.Query(new SdkDocsQueryArgs("titleRegex", m.MethodName, null, null, "bes"));
            if (data?.Docs == null || data.Docs.Count == 0) continue;

            SdkDocHit hit = data.Docs[0];
            JsonObject sig = new JsonObject { ["label"] = hit.Snippet };
            if (hit.Content != null)
                sig["documentation"] = new JsonObject { ["kind"] = "markdown", ["value"] = hit.Content };

            return new JsonObject
            {
                ["signatures"] = new JsonArray { sig },
                ["activeSignature"] = 0,
                ["activeParameter"] = 0
            };
        }

        return null;
    }

    #endregion

    #region textDocument/diagnostic

    private JsonObject HandleDiagnostic(TextDocumentInfo doc)
    {
        JsonArray diagnostics = [];

        IReadOnlyList<BescriptActCallRange> actCalls = _services.BescriptScanner.ScanActCalls(doc.Text);
        foreach (BescriptActCallRange call in actCalls)
        {
            IReadOnlyList<string> paths = _services.Logic.FindScriptPathsByName(call.ScriptName);
            if (paths.Count == 0)
            {
                diagnostics.Add(new JsonObject
                {
                    ["range"] = ToRange(call.Range),
                    ["severity"] = 1,
                    ["source"] = "toolkit",
                    ["message"] = $"Unresolved reference: {call.ScriptName}"
                });
            }
        }

        return new JsonObject { ["items"] = diagnostics };
    }

    #endregion

    #region Helpers

    private static JsonObject MakeResponse(JsonNode? id, JsonNode? result)
    {
        JsonObject resp = new JsonObject { ["jsonrpc"] = "2.0", ["result"] = result };
        AssignId(resp, id);
        return resp;
    }

    private static JsonObject MakeError(JsonNode? id, int code, string message)
    {
        JsonObject resp = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };
        AssignId(resp, id);
        return resp;
    }

    /// <summary>
    /// 将 id 写入响应对象。
    /// JSON-RPC 规约要求 id 为 string | number | null，此处按原始类型重建独立节点，
    /// 避免复用请求中已有父节点的 JsonNode 引用。
    /// </summary>
    private static void AssignId(JsonObject resp, JsonNode? id)
    {
        if (id == null) return;

        resp["id"] = id switch
        {
            JsonValue v when v.GetValueKind() == JsonValueKind.Number => v.GetValue<int>(),
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
            _ => throw new InvalidOperationException($"JSON-RPC id 必须为 string 或 number，实际: {id.GetValueKind()}")
        };
    }

    /// <summary>
    /// 已校验的文档信息：归属本工作区且携带文档文本。handler 直接使用，无需任何边界检查。
    /// </summary>
    private sealed record TextDocumentInfo(string Uri, string RelativePath, string Text);

    /// <summary>
    /// 系统边界：解析 textDocument 并校验文件归属与文档文本。
    /// 返回 null 表示请求不可处理（文件不属于当前工作区，或缺少文档文本）。
    /// </summary>
    private static TextDocumentInfo? ResolveDocument(JsonObject @params)
    {
        JsonNode textDocumentNode = @params["textDocument"] ?? throw new InvalidOperationException("LSP textDocument 参数缺失");
        JsonNode uriNode = textDocumentNode["uri"] ?? throw new InvalidOperationException("LSP textDocument.uri 参数缺失");
        string uri = uriNode.GetValue<string>();
        string? text = @params["__documentText"]?.GetValue<string>();
        if (text == null)
            return null;

        string rawPath = string.Empty;
        if (uri.StartsWith("file:///"))
            rawPath = Uri.UnescapeDataString(uri["file:///".Length..]).Replace('\\', '/');
        else if (uri.StartsWith("file://"))
            rawPath = Uri.UnescapeDataString(uri["file://".Length..]).Replace('\\', '/');

        string? relativePath = GetRelativePath(rawPath);
        if (relativePath == null)
            return null;

        return new TextDocumentInfo(uri, relativePath, text);
    }

    /// <summary>
    /// 已知方法在"请求不可处理"（参数缺失、文件不在工作区、缺文档文本）时对应的合法空结果。
    /// semanticTokens/documentLink/documentSymbol/completion/diagnostic 返回各自空结构；
    /// definition/references/hover/rename/signatureHelp 的 LSP 合法空结果即 null（result: null）。
    /// 调用方已用 IsKnownMethod 过滤未知方法，此处的 _ 仅命中这五个返回 null 的方法。
    /// </summary>
    private static JsonObject? EmptyResultFor(string method) => method switch
    {
        "textDocument/semanticTokens/full" => new JsonObject { ["data"] = new JsonArray() },
        "textDocument/documentLink" => new JsonObject { ["result"] = new JsonArray() },
        "textDocument/documentSymbol" => new JsonObject { ["result"] = new JsonArray() },
        "textDocument/completion" => new JsonObject { ["items"] = new JsonArray() },
        "textDocument/diagnostic" => new JsonObject { ["items"] = new JsonArray() },
        _ => null
    };

    /// <summary>
    /// method 是否为本 handler 支持的已知 LSP 方法（initialize 在 Handle 顶部单独处理）。
    /// 仅未知方法返回 Method not found 错误；已知方法即使不可处理也返回其合法空结果，绝不以错误码替代。
    /// </summary>
    private static bool IsKnownMethod(string method) =>
        method is "textDocument/definition" or "textDocument/references" or "textDocument/hover"
            or "textDocument/semanticTokens/full" or "textDocument/diagnostic" or "textDocument/rename"
            or "textDocument/documentLink" or "textDocument/completion" or "textDocument/documentSymbol"
            or "textDocument/signatureHelp";

    /// <summary>
    /// 将传入路径转为工程根相对路径。
    /// 返回 null 表示文件不在当前工作区根下（多项目场景下属于其它工程），
    /// LSP 对此类文件不予处理。
    /// </summary>
    private static string? GetRelativePath(string absolutePath)
    {
        // 已是相对路径则直接返回（单元测试与部分调用方传入相对路径）
        if (!Path.IsPathRooted(absolutePath))
            return WorkspacePath.ToUnixSeparator(absolutePath);

        string root = ProjectModel.GetProjectRootFullPath();
        if (!absolutePath.StartsWith(root, StringComparison.Ordinal))
            return null;

        string relative = absolutePath[root.Length..];
        if (relative.StartsWith('/') || relative.StartsWith('\\'))
            relative = relative[1..];
        return WorkspacePath.ToUnixSeparator(relative);
    }

    private sealed record PositionInfo(int Line, int Character);

    private readonly record struct PositionPayload(int Line, int Character);

    private static PositionInfo? ExtractPosition(JsonObject @params)
    {
        JsonNode? pos = @params["position"];
        if (pos == null) return null;
        PositionPayload payload = JsonSerializer.Deserialize<PositionPayload>(pos, McpJsonOptions.Standard);
        if (payload.Line < 0) return null;
        return new PositionInfo(payload.Line, payload.Character);
    }

    private static JsonObject ToRange(BescriptTextSpan span)
    {
        int line = span.Line - 1;
        return new JsonObject
        {
            ["start"] = new JsonObject { ["line"] = line, ["character"] = span.Column },
            ["end"] = new JsonObject { ["line"] = line, ["character"] = span.Column + span.Length }
        };
    }

    private static JsonObject ToRange(int startLine, int startChar, int endLine, int endChar)
    {
        return new JsonObject
        {
            ["start"] = new JsonObject { ["line"] = startLine, ["character"] = startChar },
            ["end"] = new JsonObject { ["line"] = endLine, ["character"] = endChar }
        };
    }

    private static string ToFileUri(string relativePath)
    {
        string absolute = Path.GetFullPath(relativePath);
        return $"file:///{absolute.Replace('\\', '/')}";
    }

    private static string ExtractWordAtPosition(string text, int line, int character)
    {
        string[] lines = text.Split('\n');
        if (line < 0 || line >= lines.Length) return string.Empty;

        string lineText = lines[line];
        int start = character;
        while (start > 0 && GuardService.IsWordChar(lineText[start - 1]))
            start--;
        int end = character;
        while (end < lineText.Length && GuardService.IsWordChar(lineText[end]))
            end++;
        return lineText[start..end];
    }

    #endregion
}
