using System.Text.Json;
using Toolkit.Mcp;
using Xunit;

namespace Toolkit.Core.Tests.Mcp;

/// <summary>
/// 视图指纹（view_fingerprint / base_fingerprint）测试：
/// - read_view 返回指纹（TS = device 脚本实体哈希；HTML = canvas 二进制哈希）
/// - patch 携带错误指纹 → 拒绝"视图已变更"（外部并发修改检测）
/// - patch 携带正确指纹 / 缺省 → 不做指纹拒绝（走正常匹配流程）
/// 覆盖 patch_mech_view 与 patch_mech_view_multiedit 两工具 + TS/HTML 两视图类型。
/// </summary>
[Collection("McpBrowserTests")]
public sealed class ViewFingerprintTests
{
    private const string DeviceViewPath = "map.map_/54.ts";
    private const string CanvasViewPath = "UI_加载界面.html";
    private const string MissingFingerprint = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    private readonly McpBrowserTestFixture _fixture;

    public ViewFingerprintTests(McpBrowserTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static async Task<JsonElement> InvokeAsync(McpToolRegistry registry, string toolName, object args)
    {
        Func<JsonElement?, CancellationToken, Task<object?>>? handler = registry.TryGetHandler(toolName);
        Assert.NotNull(handler);

        using JsonDocument requestDoc = JsonDocument.Parse(JsonSerializer.Serialize(args));
        object? response = await handler(requestDoc.RootElement, CancellationToken.None);
        using JsonDocument responseDoc = JsonDocument.Parse(JsonSerializer.Serialize(response, CamelCaseOptions));
        return responseDoc.RootElement.Clone();
    }

    private static string[] ExtractErrorMessages(JsonElement response)
    {
        if (!response.TryGetProperty("errors", out JsonElement errorsElement))
        {
            return [];
        }
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

    private static string? ReadFingerprint(JsonElement response)
    {
        if (!response.TryGetProperty("data", out JsonElement dataElement) || dataElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (!dataElement.TryGetProperty("view_fingerprint", out JsonElement fingerprintElement))
        {
            return null;
        }
        return fingerprintElement.GetString();
    }

    private async Task<bool> DeviceViewAvailableAsync(McpToolRegistry registry)
    {
        JsonElement response = await InvokeAsync(registry, "read_view", new { view_path = DeviceViewPath });
        return ExtractErrorMessages(response).Length == 0;
    }

    // ═══════════════════════════════════════════════════════════════════
    // read_view 返回指纹
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadDeviceView_ReturnsViewFingerprint()
    {
        if (!_fixture.IsAvailable) return;

        JsonElement response = await InvokeAsync(_fixture.Registry, "read_view", new { view_path = DeviceViewPath });
        if (ExtractErrorMessages(response).Length > 0) return; // 视图未同步等环境因素，跳过

        string? fingerprint = ReadFingerprint(response);
        Assert.NotNull(fingerprint);
        Assert.Equal(64, fingerprint.Length);
        Assert.True(fingerprint.All(c => Uri.IsHexDigit(c)), $"指纹应为 64 位 hex: {fingerprint}");
    }

    [Fact]
    public async Task ReadCanvasView_ReturnsViewFingerprint()
    {
        if (!_fixture.IsAvailable) return;

        JsonElement response = await InvokeAsync(_fixture.Registry, "read_view", new { view_path = CanvasViewPath });
        if (ExtractErrorMessages(response).Length > 0) return; // 视图缓存未就绪等环境因素，跳过

        string? fingerprint = ReadFingerprint(response);
        Assert.NotNull(fingerprint);
        Assert.Equal(64, fingerprint.Length);
        Assert.True(fingerprint.All(c => Uri.IsHexDigit(c)), $"指纹应为 64 位 hex: {fingerprint}");
    }

    // ═══════════════════════════════════════════════════════════════════
    // TS 视图：patch 指纹校验
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchDeviceView_MismatchedFingerprint_IsRejected()
    {
        if (!_fixture.IsAvailable) return;
        if (!await DeviceViewAvailableAsync(_fixture.Registry)) return;

        JsonElement response = await InvokeAsync(_fixture.Registry, "patch_mech_view", new
        {
            view_path = DeviceViewPath,
            old_string = "__definitely_not_present__",
            new_string = "x",
            base_fingerprint = MissingFingerprint
        });

        string[] messages = ExtractErrorMessages(response);
        Assert.Contains(messages, m => m.Contains("视图已变更", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PatchDeviceView_MatchingFingerprint_IsNotRejectedForFingerprint()
    {
        if (!_fixture.IsAvailable) return;

        JsonElement readResponse = await InvokeAsync(_fixture.Registry, "read_view", new { view_path = DeviceViewPath });
        string? fingerprint = ReadFingerprint(readResponse);
        if (fingerprint is null) return;

        JsonElement response = await InvokeAsync(_fixture.Registry, "patch_mech_view", new
        {
            view_path = DeviceViewPath,
            old_string = "__definitely_not_present__",
            new_string = "x",
            base_fingerprint = fingerprint
        });

        string[] messages = ExtractErrorMessages(response);
        Assert.NotEmpty(messages); // 走到匹配阶段必然失败（old_string 不存在）
        Assert.DoesNotContain(messages, m => m.Contains("视图已变更", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PatchDeviceView_OmittedFingerprint_SkipsFingerprintCheck()
    {
        if (!_fixture.IsAvailable) return;
        if (!await DeviceViewAvailableAsync(_fixture.Registry)) return;

        JsonElement response = await InvokeAsync(_fixture.Registry, "patch_mech_view", new
        {
            view_path = DeviceViewPath,
            old_string = "__definitely_not_present__",
            new_string = "x"
        });

        string[] messages = ExtractErrorMessages(response);
        Assert.NotEmpty(messages); // 无指纹校验，走到匹配阶段必然失败（old_string 不存在）
        Assert.DoesNotContain(messages, m => m.Contains("视图已变更", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PatchDeviceViewMultiedit_MismatchedFingerprint_IsRejected()
    {
        if (!_fixture.IsAvailable) return;
        if (!await DeviceViewAvailableAsync(_fixture.Registry)) return;

        JsonElement response = await InvokeAsync(_fixture.Registry, "patch_mech_view_multiedit", new
        {
            view_path = DeviceViewPath,
            edits = new[]
            {
                new { old_string = "__definitely_not_present__", new_string = "x", replace_all = false }
            },
            base_fingerprint = MissingFingerprint
        });

        string[] messages = ExtractErrorMessages(response);
        Assert.Contains(messages, m => m.Contains("视图已变更", StringComparison.Ordinal));
    }

    // ═══════════════════════════════════════════════════════════════════
    // HTML 视图：patch 指纹校验
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchCanvasView_MismatchedFingerprint_IsRejected()
    {
        if (!_fixture.IsAvailable) return;

        JsonElement response = await InvokeAsync(_fixture.Registry, "patch_mech_view", new
        {
            view_path = CanvasViewPath,
            old_string = "__definitely_not_present__",
            new_string = "x",
            base_fingerprint = MissingFingerprint
        });

        string[] messages = ExtractErrorMessages(response);
        Assert.Contains(messages, m => m.Contains("视图已变更", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PatchCanvasView_OmittedFingerprint_SkipsFingerprintCheck()
    {
        if (!_fixture.IsAvailable) return;

        JsonElement response = await InvokeAsync(_fixture.Registry, "patch_mech_view", new
        {
            view_path = CanvasViewPath,
            old_string = "__definitely_not_present__",
            new_string = "x"
        });

        string[] messages = ExtractErrorMessages(response);
        Assert.NotEmpty(messages); // 无指纹校验，走到匹配阶段必然失败（old_string 不存在）
        Assert.DoesNotContain(messages, m => m.Contains("视图已变更", StringComparison.Ordinal));
    }
}
