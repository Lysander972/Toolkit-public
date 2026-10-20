using Toolkit.Core.Tests.Common;
using Xunit;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests;

public sealed class ToolkitConfigTests : TempDirectoryTestFixture
{
    private string ConfigFileFullPath => SystemIOWrapper.Combine(TempDir, "toolkit.config.toml");

    public ToolkitConfigTests() : base("ToolkitConfigTests") { }

    [Fact]
    public void Constructor_MissingFile_MaterializesDefaults()
    {
        ToolkitConfig config = new ToolkitConfig(ConfigFileFullPath);

        Assert.True(SystemIOWrapper.FileExists(ConfigFileFullPath));
        Assert.Equal(5050, config.GetInt("mcp.port"));
    }

    [Fact]
    public void GetInt_ExistingFile_ReadsValue()
    {
        SystemIOWrapper.WriteAllText(ConfigFileFullPath, "mcp.port = 6071\n");

        ToolkitConfig config = new ToolkitConfig(ConfigFileFullPath);

        Assert.Equal(6071, config.GetInt("mcp.port"));
    }

    [Fact]
    public void GetString_MissingEntry_FallsBackToDefault()
    {
        // 文件存在但缺条目：按规范默认值回读
        SystemIOWrapper.WriteAllText(ConfigFileFullPath, "mcp.port = 5051\n");

        ToolkitConfig config = new ToolkitConfig(ConfigFileFullPath);

        Assert.Equal("", config.GetString("llm.keys.deepseek"));
    }

    [Fact]
    public void GetString_UnknownEntry_Throws()
    {
        ToolkitConfig config = new ToolkitConfig(ConfigFileFullPath);

        Assert.Throws<KeyNotFoundException>(() => config.GetString("no.such.entry"));
    }

    [Fact]
    public void WriteAtomic_ReplacesExistingContent()
    {
        ToolkitConfig _ = new ToolkitConfig(ConfigFileFullPath);

        ToolkitConfig.WriteAtomic(ConfigFileFullPath, "mcp.port = 6060\n");

        Assert.Equal(6060, new ToolkitConfig(ConfigFileFullPath).GetInt("mcp.port"));
    }
}
