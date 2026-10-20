using System.Text.Json;
using Toolkit.Mcp;
using Xunit;
using Xunit.Abstractions;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Mcp;

[Collection("McpTests")]
public sealed class PatchMechViewRequestValidationTests
{
    private readonly McpTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PatchMechViewRequestValidationTests(McpTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    // ── 共用辅助 ──────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string[] ExtractErrorMessages(JsonElement response)
    {
        JsonElement errorsElement = response.GetProperty("errors");
        Assert.Equal(JsonValueKind.Array, errorsElement.ValueKind);

        return errorsElement.EnumerateArray()
            .Select(item =>
            {
                string? message = item.GetProperty("message").GetString();
                if (message is null)
                {
                    throw new InvalidOperationException("错误缺少 message");
                }

                return message;
            })
            .ToArray();
    }

    private static async Task AssertToolValidationFailedAsync(McpToolRegistry registry, string toolName, string rawJson, params string[] expectedFragments)
    {
        Func<JsonElement?, CancellationToken, Task<object?>>? handler = registry.TryGetHandler(toolName);
        Assert.NotNull(handler);

        using JsonDocument requestDoc = JsonDocument.Parse(rawJson);
        object? toolResponse = await handler(requestDoc.RootElement, CancellationToken.None);
        using JsonDocument responseDoc = JsonDocument.Parse(JsonSerializer.Serialize(toolResponse, CamelCaseOptions));
        JsonElement response = responseDoc.RootElement.Clone();

        string[] messages = ExtractErrorMessages(response);
        Assert.NotEmpty(messages);

        foreach (string fragment in expectedFragments)
            Assert.Contains(messages, message => message.Contains(fragment, StringComparison.Ordinal));
    }

    // ═══════════════════════════════════════════════════════════════════
    // 单编辑 - 路径校验
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchValidation_MissingViewPath_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "patch_mech_view",
            """
            {
              "old_string": "a",
              "new_string": "b"
            }
            """,
            "缺少 view_path");
    }

    [Fact]
    public async Task PatchValidation_PathWithBackslash_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "patch_mech_view",
            """
            {
              "view_path": "canvas\\UI_Menu.html",
              "old_string": "a",
              "new_string": "b"
            }
            """,
            "path 必须使用 /，禁止使用 \\");
    }

    [Fact]
    public async Task PatchValidation_PathWithTempViewsPrefix_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "patch_mech_view",
            """
            {
              "view_path": "temp/toolkit/views/UI_Menu.html",
              "old_string": "a",
              "new_string": "b"
            }
            """,
            "path 禁止包含 temp/ 前缀");
    }

    [Fact]
    public async Task PatchValidation_AbsolutePath_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;
        string absoluteViewPath = Path.Combine(Path.GetTempPath(), "patch-validation", "UI_Menu.html")
            .Replace(Path.DirectorySeparatorChar, '/');
        string rawJson = JsonSerializer.Serialize(new
        {
            view_path = absoluteViewPath,
            old_string = "a",
            new_string = "b"
        });

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "patch_mech_view",
            rawJson,
            "path 禁止使用绝对路径");
    }

    [Fact]
    public async Task PatchValidation_PathWithDotDot_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "patch_mech_view",
            """
            {
              "view_path": "canvas/../UI_Menu.html",
              "old_string": "a",
              "new_string": "b"
            }
            """,
            "path 禁止包含 ..");
    }

    [Fact]
    public async Task PatchValidation_CodeRepoPath_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "patch_mech_view",
            """
            {
              "view_path": "CodeRepo.ts",
              "old_string": "a",
              "new_string": "b"
            }
            """,
            "CodeRepo.ts 禁止 patch_mech_view");
    }

    [Fact]
    public async Task PatchValidation_UnsupportedViewPath_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "patch_mech_view",
            """
            {
              "view_path": "canvas/UI_Menu.xml",
              "old_string": "a",
              "new_string": "b"
            }
            """,
            "后缀不正确");
    }

    [Fact]
    public async Task PatchValidation_OldEqualsNew_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "patch_mech_view",
            """
            {
              "view_path": "canvas/UI_Menu.html",
              "old_string": "same",
              "new_string": "same"
            }
            """,
            "old_string 和 new_string 不能相同");
    }

    [Fact]
    public async Task PatchValidation_EmptyOldString_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;
        // old_string 空在修改场景（实体存在）被 ApplyStringEdits 拒绝；新建场景（实体不存在）old_string 空合法。
        CanvasEntity? canvas = _fixture.Services.Model.ListCanvases().FirstOrDefault();
        Assert.NotNull(canvas);
        string existingViewPath = WorkspacePath.RequireRelativePath(canvas.实体_完整相对路径标识);
        existingViewPath = existingViewPath[..^ProjectEntityLayout.CanvasExt.Length] + ".html";

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "patch_mech_view",
            $$"""
            {
              "view_path": "{{existingViewPath}}",
              "old_string": "",
              "new_string": "x"
            }
            """,
            "old_string 不能为 0 长度");
    }

    // ═══════════════════════════════════════════════════════════════════
    // 多编辑 - edits 数组校验
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchMultiValidation_AllEditsSame_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "patch_mech_view_multiedit",
            """
            {
              "view_path": "canvas/UI_Menu.html",
              "edits": [
                { "old_string": "same", "new_string": "same" }
              ]
            }
            """,
            "所有编辑项中 old_string 和 new_string 均相同");
    }

    [Fact]
    public async Task PatchMultiValidation_AllEditsSameMultiple_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "patch_mech_view_multiedit",
            """
            {
              "view_path": "canvas/UI_Menu.html",
              "edits": [
                { "old_string": "a", "new_string": "a" },
                { "old_string": "b", "new_string": "b" }
              ]
            }
            """,
            "所有编辑项中 old_string 和 new_string 均相同");
    }

    [Fact]
    public async Task PatchMultiValidation_EmptyOldString_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;
        // multiedit 空 old_string 在修改场景（实体存在）被 ApplyStringEdits 拒绝；新建场景 old_string 空合法。
        CanvasEntity? canvas = _fixture.Services.Model.ListCanvases().FirstOrDefault();
        Assert.NotNull(canvas);
        string existingViewPath = WorkspacePath.RequireRelativePath(canvas.实体_完整相对路径标识);
        existingViewPath = existingViewPath[..^ProjectEntityLayout.CanvasExt.Length] + ".html";

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "patch_mech_view_multiedit",
            $$"""
            {
              "view_path": "{{existingViewPath}}",
              "edits": [
                { "old_string": "", "new_string": "x" }
              ]
            }
            """,
            "old_string 不能为 0 长度");
    }

    // ═══════════════════════════════════════════════════════════════════
    // commit_draft / cleanup_drafts - draft_path 校验
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CommitDraftValidation_AbsolutePath_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;
        string draftFullPath = Path.Combine(Path.GetTempPath(), "toolkit", "views", "ui", "main.html.draft").Replace('\\', '/');

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "commit_draft",
            $$"""
            {
              "draft_path": "{{draftFullPath}}"
            }
            """,
            "draft_path 禁止使用绝对路径");
    }

    [Fact]
    public async Task CommitDraftValidation_PathOutsideViews_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "commit_draft",
            """
            {
              "draft_path": "temp/toolkit/other/ui/main.html.draft"
            }
            """,
            "draft_path 应为相对视图根的路径");
    }

    [Fact]
    public async Task CommitDraftValidation_ViewsPrefix_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "commit_draft",
            """
            {
              "draft_path": "temp/toolkit/views/ui/main.html.draft"
            }
            """,
            "draft_path 应为相对视图根的路径");
    }

    [Fact]
    public async Task CommitDraftValidation_CodeRepoDraft_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "commit_draft",
            """
            {
              "draft_path": "CodeRepo.ts.draft"
            }
            """,
            "CodeRepo.ts.draft 禁止 commit_draft");
    }

    [Fact]
    public async Task CleanupDraftsValidation_BackslashPath_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "cleanup_drafts",
            """
            {
              "draft_paths": ["ui\\main.html.draft"]
            }
            """,
            "draft_path 必须使用 /，禁止使用 \\");
    }

    [Fact]
    public async Task CleanupDraftsValidation_MissingDraftPaths_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "cleanup_drafts",
            """
            {
            }
            """,
            "缺少 draft_paths");
    }

    [Fact]
    public async Task CleanupDraftsValidation_EmptyDraftPaths_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "cleanup_drafts",
            """
            {
              "draft_paths": []
            }
            """,
            "draft_paths 不能为空");
    }

    [Fact]
    public async Task CleanupDraftsValidation_InvalidSuffix_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "cleanup_drafts",
            """
            {
              "draft_paths": ["ui/main.html.patch"]
            }
            """,
            "draft_path 后缀不正确（仅支持 .ts.draft/.html.draft）");
    }

    [Fact]
    public async Task CommitDraftValidation_InvalidSuffix_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "commit_draft",
            """
            {
              "draft_path": "ui/main.html.patch"
            }
            """,
            "draft_path 后缀不正确（仅支持 .ts.draft/.html.draft）");
    }

    [Fact]
    public async Task CommitDraftValidation_DoubleSlash_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "commit_draft",
            """
            {
              "draft_path": "ui//main.html.draft"
            }
            """,
            "draft_path 禁止包含 //");
    }

    [Fact]
    public async Task CommitDraftValidation_DotDot_ShouldReturnError()
    {
        if (!_fixture.IsAvailable) return;

        await AssertToolValidationFailedAsync(
            _fixture.Registry,
            "commit_draft",
            """
            {
              "draft_path": "../ui/main.html.draft"
            }
            """,
            "draft_path 禁止包含 ..");
    }

    // ═══════════════════════════════════════════════════════════════════
    // 反序列化测试
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Deserialize_ApplyPatchRequest_ReplaceAll_ShouldWork()
    {
        JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // replace_all = true
        string json1 = """{"view_path":"test.ts","old_string":"a","new_string":"b","replace_all":true}""";
        ApplyPatchRequest? req1 = JsonSerializer.Deserialize<ApplyPatchRequest>(json1, options);
        Assert.NotNull(req1);
        _output.WriteLine($"Test1: replace_all = true, deserialized ReplaceAll = {req1.ReplaceAll}");
        Assert.True(req1.ReplaceAll);

        // replace_all = false
        string json2 = """{"view_path":"test.ts","old_string":"a","new_string":"b","replace_all":false}""";
        ApplyPatchRequest? req2 = JsonSerializer.Deserialize<ApplyPatchRequest>(json2, options);
        Assert.NotNull(req2);
        _output.WriteLine($"Test2: replace_all = false, deserialized ReplaceAll = {req2.ReplaceAll}");
        Assert.False(req2.ReplaceAll);

        // 未提供（默认 false）
        string json3 = """{"view_path":"test.ts","old_string":"a","new_string":"b"}""";
        ApplyPatchRequest? req3 = JsonSerializer.Deserialize<ApplyPatchRequest>(json3, options);
        Assert.NotNull(req3);
        _output.WriteLine($"Test3: replace_all absent, deserialized ReplaceAll = {req3.ReplaceAll}");
        Assert.False(req3.ReplaceAll);
    }

    [Fact]
    public void Deserialize_ApplyPatchMultiRequest_Edits_ShouldWork()
    {
        JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        string json = """{"view_path":"test.ts","edits":[{"old_string":"a","new_string":"b"},{"old_string":"c","new_string":"d"}]}""";
        ApplyPatchMultiRequest? req = JsonSerializer.Deserialize<ApplyPatchMultiRequest>(json, options);
        Assert.NotNull(req);
        Assert.Equal(2, req.Edits.Length);
        Assert.Equal("a", req.Edits[0].OldString);
        Assert.Equal("b", req.Edits[0].NewString);
        Assert.Equal("c", req.Edits[1].OldString);
        Assert.Equal("d", req.Edits[1].NewString);
    }
}
