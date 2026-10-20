using System.Text.Json.Nodes;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Persistence;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Lsp;

[Collection("WorkspaceState")]

public sealed class LspRequestHandlerTests : IDisposable
{
    private readonly LspRequestHandler _handler;
    private readonly ProjectModel _model;
    private readonly ToolkitServices _services;
    private readonly BrowserRenderService _browser;

    public LspRequestHandlerTests()
    {
        _model = ProjectModel.CreateForTesting();
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        _model.SetRootForTesting(root);
        ProjectLogicContext logic = new ProjectLogicContext(_model);
        _browser = new BrowserRenderService(1, Array.Empty<int>());
        _services = new ToolkitServices(_model, logic, _browser);
        _handler = new LspRequestHandler(_services);
    }

    public void Dispose()
    {
        _browser.Dispose();
        _model.Dispose();
    }

    private static JsonObject MakeRequest(string method, JsonObject? @params = null)
    {
        JsonObject req = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method };
        if (@params != null) req["params"] = @params;
        return req;
    }

    [Fact]
    public void Initialize_Returns_Capabilities()
    {
        JsonObject resp = _handler.Handle(MakeRequest("initialize"));
        Assert.Equal("2.0", resp["jsonrpc"]?.GetValue<string>());
        JsonObject result = Assert.IsType<JsonObject>(resp["result"]);
        JsonObject caps = Assert.IsType<JsonObject>(result["capabilities"]);
        Assert.True(caps["definitionProvider"]?.GetValue<bool>());
        Assert.True(caps["referencesProvider"]?.GetValue<bool>());
        Assert.True(caps["hoverProvider"]?.GetValue<bool>());
    }

    [Fact]
    public void Initialize_Contains_SemanticTokens_Legend()
    {
        JsonObject resp = _handler.Handle(MakeRequest("initialize"));
        JsonObject result = Assert.IsType<JsonObject>(resp["result"]);
        JsonObject caps = Assert.IsType<JsonObject>(result["capabilities"]);
        JsonObject semanticTokensProvider = Assert.IsType<JsonObject>(caps["semanticTokensProvider"]);
        JsonObject legend = Assert.IsType<JsonObject>(semanticTokensProvider["legend"]);
        JsonArray tokenTypes = Assert.IsType<JsonArray>(legend["tokenTypes"]);
        Assert.Equal(10, tokenTypes.Count);
        Assert.Equal("function", tokenTypes[0]?.GetValue<string>());
        Assert.Equal("variable", tokenTypes[1]?.GetValue<string>());
        Assert.Equal("parameter", tokenTypes[2]?.GetValue<string>());
        Assert.Equal("string", tokenTypes[3]?.GetValue<string>());
        Assert.Equal("keyword", tokenTypes[4]?.GetValue<string>());
        Assert.Equal("number", tokenTypes[5]?.GetValue<string>());
        Assert.Equal("class", tokenTypes[6]?.GetValue<string>());
        Assert.Equal("type", tokenTypes[7]?.GetValue<string>());
        Assert.Equal("comment", tokenTypes[8]?.GetValue<string>());
        Assert.Equal("disabled", tokenTypes[9]?.GetValue<string>());

        JsonArray tokenModifiers = Assert.IsType<JsonArray>(legend["tokenModifiers"]);
        Assert.Equal(3, tokenModifiers.Count);
        Assert.Equal("declaration", tokenModifiers[0]?.GetValue<string>());
        Assert.Equal("defaultLibrary", tokenModifiers[1]?.GetValue<string>());
        Assert.Equal("readonly", tokenModifiers[2]?.GetValue<string>());
    }

    [Fact]
    public void Initialize_Contains_Completion_TriggerChars()
    {
        JsonObject resp = _handler.Handle(MakeRequest("initialize"));
        JsonObject result = Assert.IsType<JsonObject>(resp["result"]);
        JsonObject caps = Assert.IsType<JsonObject>(result["capabilities"]);
        JsonObject completionProvider = Assert.IsType<JsonObject>(caps["completionProvider"]);
        JsonArray triggers = Assert.IsType<JsonArray>(completionProvider["triggerCharacters"]);
        Assert.Equal(2, triggers.Count);
        Assert.Equal(".", triggers[0]?.GetValue<string>());
        Assert.Equal(" ", triggers[1]?.GetValue<string>());
    }

    [Fact]
    public void Initialize_Contains_TextDocumentSync()
    {
        JsonObject resp = _handler.Handle(MakeRequest("initialize"));
        JsonObject result = Assert.IsType<JsonObject>(resp["result"]);
        JsonObject caps = Assert.IsType<JsonObject>(result["capabilities"]);
        JsonObject sync = Assert.IsType<JsonObject>(caps["textDocumentSync"]);
        Assert.True(sync["openClose"]?.GetValue<bool>());
        Assert.Equal(1, sync["change"]?.GetValue<int>());
    }

    [Fact]
    public void Unknown_Method_Returns_MethodNotFound()
    {
        JsonObject resp = _handler.Handle(MakeRequest("textDocument/nonexistent"));
        JsonObject error = Assert.IsType<JsonObject>(resp["error"]);
        Assert.Equal(-32601, error["code"]?.GetValue<int>());
    }

    [Fact]
    public void Definition_Null_Params_Returns_Null_Result()
    {
        // params 缺失 → 已知方法不可处理 → result:null（LSP 合法空结果），非 MethodNotFound
        JsonObject resp = _handler.Handle(MakeRequest("textDocument/definition"));
        Assert.Null(resp["result"]);
        Assert.Null(resp["error"]);
    }

    [Fact]
    public void SemanticTokens_Null_Params_Returns_EmptyData()
    {
        JsonObject resp = _handler.Handle(MakeRequest("textDocument/semanticTokens/full"));
        JsonObject result = Assert.IsType<JsonObject>(resp["result"]);
        JsonArray data = Assert.IsType<JsonArray>(result["data"]);
        Assert.Empty(data);
    }

    [Fact]
    public void SemanticTokens_Returns_AstDriven_Tokens_WithoutDeviceOwnership()
    {
        JsonObject @params = new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = "file:///test.code" },
            ["__documentText"] = @"begincodeV2
name:test
lines:
Bool value=fun create.bool(Bool 1)
=act 1101./Alias.MyScript()
endcode"
        };

        JsonObject resp = _handler.Handle(MakeRequest("textDocument/semanticTokens/full", @params));
        JsonObject result = Assert.IsType<JsonObject>(resp["result"]);
        JsonArray data = Assert.IsType<JsonArray>(result["data"]);

        Assert.True(data.Count > 0);
        Assert.Equal(0, data.Count % 5);

        HashSet<int> tokenTypes = [];
        for (int i = 3; i < data.Count; i += 5)
        {
            JsonValue tokenType = Assert.IsAssignableFrom<JsonValue>(data[i]);
            tokenTypes.Add(tokenType.GetValue<int>());
        }

        Assert.Contains(0, tokenTypes);
        Assert.Contains(4, tokenTypes);
        Assert.Contains(6, tokenTypes);
        Assert.Contains(7, tokenTypes);
    }

    [Fact]
    public void Completion_Null_Params_Returns_Items()
    {
        JsonObject resp = _handler.Handle(MakeRequest("textDocument/completion"));
        Assert.NotNull(resp["result"]?["items"]);
    }

    [Fact]
    public void Rename_Null_Params_Returns_Null_Result()
    {
        JsonObject resp = _handler.Handle(MakeRequest("textDocument/rename"));
        Assert.Null(resp["result"]);
        Assert.Null(resp["error"]);
    }

    [Fact]
    public void SignatureHelp_Null_Params_Returns_Null_Result()
    {
        JsonObject resp = _handler.Handle(MakeRequest("textDocument/signatureHelp"));
        Assert.Null(resp["result"]);
        Assert.Null(resp["error"]);
    }

    private const string UnresolvedScript = @"begincodeV2
name:test
lines:
=act 1101.this.NoSuchScript()
=act 1101.this.AlsoMissing()
endcode";

    [Fact]
    public void Diagnostic_Unresolved_ActCalls_Reported()
    {
        JsonObject @params = new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = "file:///test.code" },
            ["__documentText"] = UnresolvedScript
        };
        JsonObject resp = _handler.Handle(MakeRequest("textDocument/diagnostic", @params));
        JsonObject result = Assert.IsType<JsonObject>(resp["result"]);
        JsonArray items = Assert.IsType<JsonArray>(result["items"]);
        Assert.Equal(2, items.Count);
        JsonObject first = Assert.IsType<JsonObject>(items[0]);
        JsonObject second = Assert.IsType<JsonObject>(items[1]);
        Assert.Equal(1, first["severity"]?.GetValue<int>());
        Assert.Contains("NoSuchScript", first["message"]?.GetValue<string>());
        Assert.Contains("AlsoMissing", second["message"]?.GetValue<string>());
    }

    [Fact]
    public void Diagnostic_Null_Params_Returns_Empty()
    {
        JsonObject resp = _handler.Handle(MakeRequest("textDocument/diagnostic"));
        JsonObject result = Assert.IsType<JsonObject>(resp["result"]);
        JsonArray items = Assert.IsType<JsonArray>(result["items"]);
        Assert.Empty(items);
    }

    [Fact]
    public void FileOutsideWorkspace_Returns_Empty_Without_Throwing()
    {
        // 多项目场景：当前工作区根锚定为临时目录 A，请求的文件位于根之外（属于其它工程）。
        // 修复前：ToRelativePath 静默返回空串 → 下游 RequireRelativePath 抛"路径不能为空"。
        string tempRoot = Path.Combine(Path.GetTempPath(), "ToolkitLspOutside_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        ProjectModel.SetProjectRootFullPath(tempRoot);
        try
        {
            // documentSymbol：边界拒绝非本工作区文件，返回空符号列表而非抛异常
            JsonObject symbolResp = _handler.Handle(MakeRequest("textDocument/documentSymbol", MakeOutsideWorkspaceParams()));
            Assert.Null(symbolResp["error"]);
            JsonObject symbolResult = Assert.IsType<JsonObject>(symbolResp["result"]);
            JsonArray symbols = Assert.IsType<JsonArray>(symbolResult["result"]);
            Assert.Empty(symbols);

            // semanticTokens/full：同样短路，返回空 data
            JsonObject tokenResp = _handler.Handle(MakeRequest("textDocument/semanticTokens/full", MakeOutsideWorkspaceParams()));
            Assert.Null(tokenResp["error"]);
            JsonObject tokenResult = Assert.IsType<JsonObject>(tokenResp["result"]);
            JsonArray data = Assert.IsType<JsonArray>(tokenResult["data"]);
            Assert.Empty(data);

            // documentLink：返回空 result 数组
            JsonObject linkResp = _handler.Handle(MakeRequest("textDocument/documentLink", MakeOutsideWorkspaceParams()));
            Assert.Null(linkResp["error"]);
            JsonArray links = Assert.IsType<JsonArray>(Assert.IsType<JsonObject>(linkResp["result"])["result"]);
            Assert.Empty(links);

            // completion：返回空 items 数组
            JsonObject completionResp = _handler.Handle(MakeRequest("textDocument/completion", MakeOutsideWorkspaceParams()));
            Assert.Null(completionResp["error"]);
            JsonArray completionItems = Assert.IsType<JsonArray>(Assert.IsType<JsonObject>(completionResp["result"])["items"]);
            Assert.Empty(completionItems);

            // diagnostic：返回空 items 数组
            JsonObject diagResp = _handler.Handle(MakeRequest("textDocument/diagnostic", MakeOutsideWorkspaceParams()));
            Assert.Null(diagResp["error"]);
            JsonArray diagItems = Assert.IsType<JsonArray>(Assert.IsType<JsonObject>(diagResp["result"])["items"]);
            Assert.Empty(diagItems);
        }
        finally
        {
            FileSystemCache.Shutdown();
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* 临时目录清理失败无影响 */ }
        }
    }

    /// <summary>
    /// 构造指向当前工作区根之外的文件请求参数。每次返回独立实例（JsonNode 不可复用 parent）。
    /// </summary>
    private static JsonObject MakeOutsideWorkspaceParams()
    {
        string outsideFullPath = Path.Combine(Path.GetTempPath(), "toolkit-outside", "x.code").Replace('\\', '/');
        return new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = $"file:///{outsideFullPath}" },
            ["__documentText"] = "begincodeV2\nname:test\nlines:\nendcode"
        };
    }

    /// <summary>
    /// definition/references/hover/rename/signatureHelp 对非本工作区文件，与 semanticTokens 等方法语义统一：
    /// 不可处理时返回该方法对应的合法空结果（这五个方法的 LSP 合法空即 result:null），绝不返回 MethodNotFound。
    /// </summary>
    [Fact]
    public void OutsideWorkspace_NullResult_Methods_Return_Null_Result()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "ToolkitLspOutside_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        ProjectModel.SetProjectRootFullPath(tempRoot);
        try
        {
            string[] nullResultMethods =
            {
                "textDocument/definition",
                "textDocument/references",
                "textDocument/hover",
                "textDocument/rename",
                "textDocument/signatureHelp"
            };
            foreach (string method in nullResultMethods)
            {
                JsonObject resp = _handler.Handle(MakeRequest(method, MakeOutsideWorkspaceParams()));
                Assert.Null(resp["result"]);
                Assert.Null(resp["error"]);
            }
        }
        finally
        {
            FileSystemCache.Shutdown();
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* 临时目录清理失败无影响 */ }
        }
    }

    /// <summary>
    /// 文档文本缺失（有 textDocument.uri 但无 __documentText）时，ResolveDocument 返回 null，
    /// 与"工作区外文件"走同一空结果分发。不切根：文件可在工作区内，仅缺文档文本即触发 text==null 路径。
    /// </summary>
    [Fact]
    public void MissingDocumentText_Handled_As_Unresolvable()
    {
        // 空结果方法：返回各自空形状，不抛
        JsonObject tokenResp = _handler.Handle(MakeRequest("textDocument/semanticTokens/full", MakeParamsWithoutText()));
        JsonArray data = Assert.IsType<JsonArray>(Assert.IsType<JsonObject>(tokenResp["result"])["data"]);
        Assert.Empty(data);

        // null 结果方法：返回 result:null（LSP 合法空），非错误
        JsonObject defResp = _handler.Handle(MakeRequest("textDocument/definition", MakeParamsWithoutText()));
        Assert.Null(defResp["result"]);
        Assert.Null(defResp["error"]);
    }

    /// <summary>有 uri 但缺 __documentText 的请求参数（每次新建，JsonNode 不可复用 parent）。</summary>
    private static JsonObject MakeParamsWithoutText()
    {
        return new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = "file:///test.code" }
        };
    }
}
