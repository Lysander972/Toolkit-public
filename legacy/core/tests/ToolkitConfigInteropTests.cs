using System.Text.Json;
using Bazel;
using Tomlyn;
using Tomlyn.Model;
using Xunit;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests;

/// <summary>
/// 跨组件互操作常驻测试（规范 docs/Common_ToolkitConfig_Spec.md §7）。
/// 黄金夹具 test/data/toolkitconfig/：C#（本文件）/ Python（test_toolkit_config.py）/ TS（toolkitConfig.test.cjs）
/// 三侧读同一文件必须得到同一期望值（expected.json 为期望唯一来源），证明任一写入方产物其他组件可读。
/// </summary>
public sealed class ToolkitConfigInteropTests
{
    private const string FixtureConfigRunfilesPath = "_main/test/data/toolkitconfig/toolkit.config.toml";

    private static string FixtureDirFullPath => Path.GetDirectoryName(
        Runfiles.Create().Rlocation(FixtureConfigRunfilesPath)!)!;

    private static JsonElement LoadExpected()
    {
        string expectedJson = SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(FixtureDirFullPath, "expected.json"));
        return JsonDocument.Parse(expectedJson).RootElement;
    }

    private static TomlTable ParseFixture()
    {
        // 与 ToolkitConfig 同款解析入口（Tomlyn 2.x 无静态 Toml 类）
        TomlTable? table = TomlSerializer.Deserialize<TomlTable>(
            SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(FixtureDirFullPath, "toolkit.config.toml")));
        return table != null ? table : new TomlTable();
    }

    [Fact]
    public void ReadGoldenFixture_ConsumedEntriesMatchExpected()
    {
        ToolkitConfig config = new ToolkitConfig(SystemIOWrapper.Combine(FixtureDirFullPath, "toolkit.config.toml"));
        JsonElement expected = LoadExpected();

        // C# 消费条目（int/string/bool）；canvas_preview.scale 为 float，C# 侧无消费条目，不在此断言
        Assert.Equal(expected.GetProperty("mcp.port").GetInt32(), config.GetInt("mcp.port"));
        Assert.Equal(expected.GetProperty("lint.run_on_startup").GetBoolean(), config.GetBool("lint.run_on_startup"));
        Assert.Equal(expected.GetProperty("canvas_preview.default_width").GetInt32(), config.GetInt("canvas_preview.default_width"));
        Assert.Equal(expected.GetProperty("resource_gen.comfyui.base_url").GetString(), config.GetString("resource_gen.comfyui.base_url"));
        Assert.Equal(expected.GetProperty("resource_gen.comfyui.api_key").GetString(),
            ToolkitSecrets.FromKeyFile(SystemIOWrapper.Combine(FixtureDirFullPath, "toolkit.key"))
                .Decrypt(config.GetString("resource_gen.comfyui.api_key")));
    }

    [Fact]
    public void ReadGoldenFixture_SecretsDecryptWithFixtureKey()
    {
        // C# 无 llm.keys 消费条目，直接经 Tomlyn 导航取密文，验证加解密层跨语言互操作
        TomlTable model = ParseFixture();
        TomlTable keys = (TomlTable)((TomlTable)model["llm"])["keys"];
        byte[] fixtureKey = SystemIOWrapper.ReadAllBytes(SystemIOWrapper.Combine(FixtureDirFullPath, "toolkit.key"));
        ToolkitSecrets secrets = new ToolkitSecrets(fixtureKey);
        JsonElement expected = LoadExpected();

        Assert.Equal(expected.GetProperty("llm.keys.deepseek").GetString(), secrets.Decrypt((string)keys["deepseek"]));
        Assert.Equal(expected.GetProperty("llm.keys.local_llm").GetString(), secrets.Decrypt((string)keys["local_llm"]));
        Assert.Equal(string.Empty, (string)keys["zhipu"]);
    }

    [Fact]
    public void ReadGoldenFixture_ExtraSourcesTailParsedAsTableArray()
    {
        // 写入方产物的 [[llm.extra_sources]] 末尾段必须被解析为表数组（扁平点分 + 表数组混排是规范物理形状）
        TomlTable model = ParseFixture();
        TomlTable llm = (TomlTable)model["llm"];
        TomlTableArray extraSources = (TomlTableArray)llm["extra_sources"];

        Assert.Single(extraSources);
        Assert.Equal("local/qwen3-vl", (string)extraSources[0]["id"]);
        Assert.Equal("local_llm", (string)extraSources[0]["key"]);
    }

    [Fact]
    public void MaterializedDefaultsMatchAuthoritative()
    {
        // 默认值表对齐（跨语言 lint）：物化出的 DefaultToml 扁平点分必须与权威 defaults.json 逐键一致，
        // 防各端默认值漂移（曾发生 electron 默认值表缺 3 个 comfyui 键）。权威 = test/data/toolkitconfig/defaults.json。
        string dirFullPath = Path.Combine(Path.GetTempPath(), "toolkit-config-defaults-" + Guid.NewGuid().ToString("N"));
        string configFullPath = SystemIOWrapper.Combine(dirFullPath, "toolkit.config.toml");
        SystemIOWrapper.CreateDirectory(dirFullPath);
        _ = new ToolkitConfig(configFullPath); // 文件缺失 → 构造内物化 DefaultToml
        TomlTable model = TomlSerializer.Deserialize<TomlTable>(SystemIOWrapper.ReadAllText(configFullPath))
            ?? throw new InvalidOperationException("物化 TOML 解析失败");
        JsonElement authoritative = JsonDocument.Parse(
            SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(FixtureDirFullPath, "defaults.json"))).RootElement;
        Dictionary<string, object?> flat = new Dictionary<string, object?>();
        FlattenInto("", model, flat);

        foreach (JsonProperty prop in authoritative.EnumerateObject())
        {
            Assert.True(flat.TryGetValue(prop.Name, out object? actual), $"物化缺键: {prop.Name}");
            Assert.True(ValuesEqual(actual, prop.Value), $"默认值不一致: {prop.Name} (物化={actual}, 权威={prop.Value})");
        }
        Assert.Equal(flat.Count, authoritative.EnumerateObject().Count());
        SystemIOWrapper.DeleteDirectory(dirFullPath, recursive: true);
    }

    private static void FlattenInto(string prefix, object? value, Dictionary<string, object?> flat)
    {
        if (value is TomlTable nested)
        {
            foreach (KeyValuePair<string, object> kv in nested)
            {
                FlattenInto(prefix.Length > 0 ? $"{prefix}.{kv.Key}" : kv.Key, kv.Value, flat);
            }
        }
        else
        {
            flat[prefix] = value;
        }
    }

    private static bool ValuesEqual(object? actual, JsonElement expected)
    {
        if (expected.ValueKind == JsonValueKind.Array)
        {
            // 默认值表唯一数组 = 空 extra_sources;物化为 Tomlyn.TomlArray,
            // 其仅实现泛型 IList<object?>/ICollection<object?>,不是非泛型 System.Collections.ICollection
            return actual is System.Collections.Generic.ICollection<object?> { Count: 0 };
        }
        if (expected.ValueKind == JsonValueKind.Number)
        {
            return expected.TryGetInt64(out long integral)
                ? Convert.ToInt64(actual) == integral
                : Convert.ToDouble(actual) == expected.GetDouble();
        }
        if (expected.ValueKind == JsonValueKind.True)
        {
            return actual is true;
        }
        if (expected.ValueKind == JsonValueKind.False)
        {
            return actual is false;
        }
        return Equals(actual, expected.GetString());
    }
}
