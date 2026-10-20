using System.Text.Json;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Mcp;

/// <summary>
/// patch_mech_view 在目标视图不存在时的分流测试。
/// 覆盖入口拒绝路径（不触发 CDP/落盘）：
/// - .canvas 不存在 + old_string 非空 → 拒绝
/// - .canvas 不存在 + 父目录不存在 → 拒绝
/// - .ts 视图不存在 → 拒绝并引导用户去编辑器创建零件
/// 新建成功端到端受 CDP 渲染校验限制，由 PersistCore 与 CanvasRefactor.Extract 同范式保证。
/// </summary>
[Collection("McpTests")]
public sealed class PatchMechViewCanvasCreateTests
{
    private readonly McpTestFixture _fixture;

    public PatchMechViewCanvasCreateTests(McpTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static async Task<string[]> InvokePatchAndExtractErrorsAsync(McpToolRegistry registry, string rawJson)
    {
        Func<JsonElement?, CancellationToken, Task<object?>>? handler = registry.TryGetHandler("patch_mech_view");
        Assert.NotNull(handler);

        using JsonDocument requestDoc = JsonDocument.Parse(rawJson);
        object? response = await handler(requestDoc.RootElement, CancellationToken.None);
        using JsonDocument responseDoc = JsonDocument.Parse(JsonSerializer.Serialize(response, CamelCaseOptions));
        JsonElement root = responseDoc.RootElement.Clone();

        Assert.True(root.TryGetProperty("errors", out JsonElement errorsElement));
        Assert.Equal(JsonValueKind.Array, errorsElement.ValueKind);
        return errorsElement.EnumerateArray()
            .Select(item => item.GetProperty("message").GetString() ?? throw new InvalidOperationException("错误缺少 message"))
            .ToArray();
    }

    [Fact]
    public async Task CreateCanvas_When_OldStringNotEmpty_Should_Fail()
    {
        if (!_fixture.IsAvailable) return;
        // 父目录取自已存在 canvas，保证通过"父目录存在"检查后落到 old_string 校验。
        CanvasEntity? canvas = _fixture.Services.Model.ListCanvases().FirstOrDefault();
        Assert.NotNull(canvas);
        string parentDir = RelativePathUtils.GetParentDirectory(WorkspacePath.RequireRelativePath(canvas.实体_完整相对路径标识));
        string viewPath = (RelativePathUtils.Combine(parentDir, "patch_create_new.canvas"))[..^ProjectEntityLayout.CanvasExt.Length] + ".html";

        string raw = $$"""
        {
          "view_path": "{{viewPath}}",
          "old_string": "non-empty-baseline",
          "new_string": "<BeCanvas name=\"x\"></BeCanvas>"
        }
        """;

        string[] messages = await InvokePatchAndExtractErrorsAsync(_fixture.Registry, raw);
        Assert.Contains(messages, m => m.Contains("新建模式下 old_string 必须为空串", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateCanvas_When_ParentDirNotExists_Should_Fail()
    {
        if (!_fixture.IsAvailable) return;

        string raw = """
        {
          "view_path": "patch_no_such_dir_xyz/new.html",
          "old_string": "",
          "new_string": "<BeCanvas name=\"x\"></BeCanvas>"
        }
        """;

        string[] messages = await InvokePatchAndExtractErrorsAsync(_fixture.Registry, raw);
        Assert.Contains(messages, m => m.Contains("父目录不存在", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateCanvas_When_TsViewNotExists_Should_FailWithNotSupportedNewTs()
    {
        if (!_fixture.IsAvailable) return;

        string raw = """
        {
          "view_path": "patch_no_such_device_xyz.ts",
          "old_string": "a",
          "new_string": "b"
        }
        """;

        string[] messages = await InvokePatchAndExtractErrorsAsync(_fixture.Registry, raw);
        Assert.Contains(messages, m => m.Contains("不支持新建 TS 文件", StringComparison.Ordinal));
    }
}
