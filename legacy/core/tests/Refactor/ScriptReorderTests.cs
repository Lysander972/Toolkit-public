using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Toolkit.Core.Tests.Common;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Refactor;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Refactor;

[Collection("WorkspaceState")]
public sealed class ScriptReorderTests : TempDirectoryTestFixture
{
    private ProjectModel? _model;
    private ScriptReorder? _reorder;

    public ScriptReorderTests() : base("Toolkit.Core.Tests.ScriptReorder")
    {
    }

    [MemberNotNull(nameof(_model), nameof(_reorder))]
    private void Init(string projectRoot)
    {
        _model = TestWorkspace.LoadProject(projectRoot);
        _reorder = new ScriptReorder(_model);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // ReadOutline
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadOutline_Returns_Scripts_In_Order()
    {
        string projectRoot = CreateTestProject(scripts: new[] { "alpha", "beta", "gamma" });
        Init(projectRoot);

        (DeviceScriptOutline? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = _reorder.ReadOutline("map.map_/0");

        Assert.Empty(errors);
        Assert.Empty(warnings);
        Assert.NotNull(data);
        Assert.Single(data.ScriptGroups);
        Assert.Equal("scripts", data.ScriptGroups[0].GroupName);
        Assert.Null(data.ScriptGroups[0].EventName);
        Assert.Equal(3, data.ScriptGroups[0].Scripts.Count);
        Assert.Equal("alpha", data.ScriptGroups[0].Scripts[0].Name);
        Assert.Equal("beta", data.ScriptGroups[0].Scripts[1].Name);
        Assert.Equal("gamma", data.ScriptGroups[0].Scripts[2].Name);
        Assert.Equal("inline", data.ScriptGroups[0].Scripts[0].Type);
    }

    [Fact]
    public void ReadOutline_Returns_CodeRepo_Entries()
    {
        string projectRoot = CreateTestProject(
            scripts: new[] { "local" },
            codeRepoScripts: new[] { ("remote", "1234567890abcdef1234567890abcdef") });
        Init(projectRoot);

        (DeviceScriptOutline? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.ReadOutline("map.map_/0");

        Assert.Empty(errors);
        Assert.NotNull(data);
        Assert.Equal(2, data.ScriptGroups[0].Scripts.Count);
        Assert.Equal("local", data.ScriptGroups[0].Scripts[0].Name);
        Assert.Equal("inline", data.ScriptGroups[0].Scripts[0].Type);
        Assert.Equal(string.Empty, data.ScriptGroups[0].Scripts[0].Guid);
        Assert.Equal("remote", data.ScriptGroups[0].Scripts[1].Name);
        Assert.Equal("codeRepo", data.ScriptGroups[0].Scripts[1].Type);
        Assert.Equal("1234567890abcdef1234567890abcdef", data.ScriptGroups[0].Scripts[1].Guid);
    }

    [Fact]
    public void ReadOutline_Includes_Global_Variables()
    {
        string projectRoot = CreateTestProject(
            scripts: new[] { "alpha" },
            globalVars: new[] { ("hp", "Float"), ("name", "String") });
        Init(projectRoot);

        (DeviceScriptOutline? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.ReadOutline("map.map_/0");

        Assert.Empty(errors);
        Assert.NotNull(data);
        Assert.Equal(2, data.GlobalVariables.Count);
        Assert.Equal("hp", data.GlobalVariables[0]);
        Assert.Equal("name", data.GlobalVariables[1]);
    }

    [Fact]
    public void ReadOutline_Empty_Global_Variables_When_No_Vars()
    {
        string projectRoot = CreateTestProject(scripts: new[] { "alpha" });
        Init(projectRoot);

        (DeviceScriptOutline? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.ReadOutline("map.map_/0");

        Assert.Empty(errors);
        Assert.NotNull(data);
        Assert.Empty(data.GlobalVariables);
    }

    [Fact]
    public void ReadOutline_Fails_For_Unknown_Device()
    {
        string projectRoot = CreateTestProject();
        Init(projectRoot);

        (DeviceScriptOutline? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.ReadOutline("nonexistent/99");

        Assert.Null(data);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ReadOutline_Fails_For_Empty_Path()
    {
        string projectRoot = CreateTestProject();
        Init(projectRoot);

        (DeviceScriptOutline? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.ReadOutline("");

        Assert.Null(data);
        Assert.NotEmpty(errors);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Reorder - 脚本组成功场景
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Reorder_Reverses_Script_Order()
    {
        string projectRoot = CreateTestProject(scripts: new[] { "alpha", "beta", "gamma" });
        Init(projectRoot);

        ScriptGroupOrder[] orders = new ScriptGroupOrder[]
        {
            new("scripts", new[] { "gamma", "beta", "alpha" })
        };
        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = _reorder.Reorder("map.map_/0", orders, null);

        Assert.Empty(errors);
        Assert.Empty(warnings);
        Assert.NotNull(data);
        Assert.True(data.Applied);
        Assert.NotEmpty(data.AffectedRelativePaths);
        Assert.Contains(data.AffectedRelativePaths, static p => p.Contains("list.json"));
    }

    [Fact]
    public void Reorder_Single_Swap()
    {
        string projectRoot = CreateTestProject(scripts: new[] { "A", "B" });
        Init(projectRoot);

        ScriptGroupOrder[] orders = new ScriptGroupOrder[]
        {
            new("scripts", new[] { "B", "A" })
        };
        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.Reorder("map.map_/0", orders, null);

        Assert.Empty(errors);
        Assert.NotNull(data);
        Assert.True(data.Applied);
        Assert.NotEmpty(data.AffectedRelativePaths);
    }

    [Fact]
    public void McpReorder_Allows_Outline_Without_GlobalVariables()
    {
        string projectRoot = CreateTestProject(scripts: new[] { "alpha", "beta" });
        ProjectModel model = TestWorkspace.LoadProject(projectRoot);
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices services = new(model, new ProjectLogicContext(model), browser);
        using McpToolRegistry registry = new(services, projectRoot, null, registerHandlers: false);

        using JsonDocument requestDoc = JsonDocument.Parse("""
            {
              "device_relative_path": "map.map_/0",
              "outline": {
                "script_groups": [
                  {
                    "group_name": "scripts",
                    "ordered_names": [ "beta", "alpha" ]
                  }
                ]
              }
            }
            """);
        MethodInfo? executeMethod = typeof(McpToolRegistry)
            .GetMethod("ExecuteScriptReorder", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(executeMethod);

        ITuple? resultTuple = Assert.IsAssignableFrom<ITuple>(executeMethod.Invoke(registry, [requestDoc.RootElement]));
        IReadOnlyList<ToolError> errors = Assert.IsAssignableFrom<IReadOnlyList<ToolError>>(resultTuple[1]);
        IReadOnlyList<string> affectedPathList = Assert.IsAssignableFrom<IReadOnlyList<string>>(resultTuple[3]);

        Assert.Empty(errors);
        Assert.NotEmpty(affectedPathList);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Reorder - 自动排序
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Reorder_Auto_Sorts_All_By_Name()
    {
        string projectRoot = CreateTestProject(
            scripts: new[] { "gamma", "alpha", "beta" },
            globalVars: new[] { ("zVar", "Float"), ("aVar", "String") });
        Init(projectRoot);

        // orders=null, globalVarOrder=null → 自动排序
        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = _reorder.Reorder("map.map_/0", null, null);

        Assert.Empty(errors);
        Assert.NotNull(data);
        Assert.True(data.Applied);

        // 验证排序后的顺序
        (DeviceScriptOutline? outline, IReadOnlyList<ToolError> _, IReadOnlyList<ToolError> _) = _reorder.ReadOutline("map.map_/0");
        Assert.NotNull(outline);
        Assert.Equal("alpha", outline.ScriptGroups[0].Scripts[0].Name);
        Assert.Equal("beta", outline.ScriptGroups[0].Scripts[1].Name);
        Assert.Equal("gamma", outline.ScriptGroups[0].Scripts[2].Name);
        Assert.Equal("aVar", outline.GlobalVariables[0]);
        Assert.Equal("zVar", outline.GlobalVariables[1]);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Reorder - 全局变量
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Reorder_Global_Vars()
    {
        string projectRoot = CreateTestProject(
            scripts: new[] { "alpha" },
            globalVars: new[] { ("hp", "Float"), ("name", "String"), ("speed", "Float") });
        Init(projectRoot);

        string[] globalVarOrder = new[] { "speed", "name", "hp" };
        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.Reorder("map.map_/0", null, globalVarOrder);

        Assert.Empty(errors);
        Assert.NotNull(data);
        Assert.True(data.Applied);
        Assert.Contains(data.AffectedRelativePaths, static p => p.Contains("vars.json"));

        (DeviceScriptOutline? outline, IReadOnlyList<ToolError> _, IReadOnlyList<ToolError> _) = _reorder.ReadOutline("map.map_/0");
        Assert.NotNull(outline);
        Assert.Equal("speed", outline.GlobalVariables[0]);
        Assert.Equal("name", outline.GlobalVariables[1]);
        Assert.Equal("hp", outline.GlobalVariables[2]);
    }

    [Fact]
    public void Reorder_Both_Scripts_And_Global_Vars()
    {
        string projectRoot = CreateTestProject(
            scripts: new[] { "beta", "alpha" },
            globalVars: new[] { ("zVar", "Float"), ("aVar", "String") });
        Init(projectRoot);

        ScriptGroupOrder[] orders = new ScriptGroupOrder[]
        {
            new("scripts", new[] { "alpha", "beta" })
        };
        string[] globalVarOrder = new[] { "aVar", "zVar" };
        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.Reorder("map.map_/0", orders, globalVarOrder);

        Assert.Empty(errors);
        Assert.NotNull(data);
        Assert.True(data.Applied);

        (DeviceScriptOutline? outline, IReadOnlyList<ToolError> _, IReadOnlyList<ToolError> _) = _reorder.ReadOutline("map.map_/0");
        Assert.NotNull(outline);
        Assert.Equal("alpha", outline.ScriptGroups[0].Scripts[0].Name);
        Assert.Equal("aVar", outline.GlobalVariables[0]);
    }

    [Fact]
    public async Task Reorder_Both_Scripts_And_Global_Vars_With_Concurrent_Readers()
    {
        string projectRoot = CreateTestProject(
            scripts: new[] { "beta", "alpha" },
            globalVars: new[] { ("zVar", "Float"), ("aVar", "String") });
        Init(projectRoot);
        ScriptReorder reorder = _reorder;

        ScriptGroupOrder[] orders = new ScriptGroupOrder[]
        {
            new("scripts", new[] { "alpha", "beta" })
        };
        string[] globalVarOrder = new[] { "aVar", "zVar" };

        using ManualResetEventSlim start = new(false);
        using ManualResetEventSlim finished = new(false);
        int readFailure = 0;

        Task[] readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            start.Wait();
            while (!finished.IsSet)
            {
                (DeviceScriptOutline? outline, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) =
                    reorder.ReadOutline("map.map_/0");
                if (errors.Count > 0 || outline == null)
                {
                    Interlocked.Exchange(ref readFailure, 1);
                    return;
                }
            }
        })).ToArray();

        Task<(ScriptReorderData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings)> writer = Task.Run(() =>
        {
            start.Wait();
            return reorder.Reorder("map.map_/0", orders, globalVarOrder);
        });

        start.Set();
        (ScriptReorderData? data, IReadOnlyList<ToolError> writeErrors, IReadOnlyList<ToolError> _) = await writer;
        finished.Set();
        await Task.WhenAll(readers);

        Assert.Equal(0, Volatile.Read(ref readFailure));
        Assert.Empty(writeErrors);
        Assert.NotNull(data);
        Assert.True(data.Applied);

        (DeviceScriptOutline? finalOutline, IReadOnlyList<ToolError> finalErrors, IReadOnlyList<ToolError> _) =
            reorder.ReadOutline("map.map_/0");
        Assert.Empty(finalErrors);
        Assert.NotNull(finalOutline);
        Assert.Equal("alpha", finalOutline.ScriptGroups[0].Scripts[0].Name);
        Assert.Equal("aVar", finalOutline.GlobalVariables[0]);
    }

    [Fact]
    public void Reorder_Global_Vars_Fails_When_Missing()
    {
        string projectRoot = CreateTestProject(
            scripts: new[] { "alpha" },
            globalVars: new[] { ("hp", "Float"), ("name", "String") });
        Init(projectRoot);

        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.Reorder("map.map_/0", null, new[] { "hp" });

        Assert.Null(data);
        Assert.NotEmpty(errors);
        Assert.Contains("缺少", errors[0].Message);
    }

    [Fact]
    public void Reorder_Global_Vars_Fails_When_Extra()
    {
        string projectRoot = CreateTestProject(
            scripts: new[] { "alpha" },
            globalVars: new[] { ("hp", "Float") });
        Init(projectRoot);

        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.Reorder("map.map_/0", null, new[] { "hp", "phantom" });

        Assert.Null(data);
        Assert.NotEmpty(errors);
        Assert.Contains("不存在", errors[0].Message);
    }

    [Fact]
    public void Reorder_Global_Vars_Fails_When_Duplicate()
    {
        string projectRoot = CreateTestProject(
            scripts: new[] { "alpha" },
            globalVars: new[] { ("hp", "Float"), ("name", "String") });
        Init(projectRoot);

        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.Reorder("map.map_/0", null, new[] { "hp", "hp" });

        Assert.Null(data);
        Assert.NotEmpty(errors);
        Assert.Contains("重复", errors[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Reorder - 脚本校验失败
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Reorder_Fails_When_Name_Missing()
    {
        string projectRoot = CreateTestProject(scripts: new[] { "alpha", "beta" });
        Init(projectRoot);

        ScriptGroupOrder[] orders = new ScriptGroupOrder[]
        {
            new("scripts", new[] { "alpha" })
        };
        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.Reorder("map.map_/0", orders, null);

        Assert.Null(data);
        Assert.NotEmpty(errors);
        Assert.Contains("缺少脚本", errors[0].Message);
    }

    [Fact]
    public void Reorder_Fails_When_Extra_Name()
    {
        string projectRoot = CreateTestProject(scripts: new[] { "alpha" });
        Init(projectRoot);

        ScriptGroupOrder[] orders = new ScriptGroupOrder[]
        {
            new("scripts", new[] { "alpha", "phantom" })
        };
        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.Reorder("map.map_/0", orders, null);

        Assert.Null(data);
        Assert.NotEmpty(errors);
        Assert.Contains("不存在", errors[0].Message);
    }

    [Fact]
    public void Reorder_Fails_When_Duplicate_Name()
    {
        string projectRoot = CreateTestProject(scripts: new[] { "alpha", "beta" });
        Init(projectRoot);

        ScriptGroupOrder[] orders = new ScriptGroupOrder[]
        {
            new("scripts", new[] { "alpha", "alpha" })
        };
        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.Reorder("map.map_/0", orders, null);

        Assert.Null(data);
        Assert.NotEmpty(errors);
        Assert.Contains("重复", errors[0].Message);
    }

    [Fact]
    public void Reorder_Fails_When_Group_Not_Found()
    {
        string projectRoot = CreateTestProject(scripts: new[] { "alpha" });
        Init(projectRoot);

        ScriptGroupOrder[] orders = new ScriptGroupOrder[]
        {
            new("nonexistent_group", new[] { "alpha" })
        };
        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.Reorder("map.map_/0", orders, null);

        Assert.Null(data);
        Assert.NotEmpty(errors);
        Assert.Contains("不存在", errors[0].Message);
    }

    [Fact]
    public void Reorder_Fails_When_Device_Not_Found()
    {
        string projectRoot = CreateTestProject();
        Init(projectRoot);

        ScriptGroupOrder[] orders = new ScriptGroupOrder[]
        {
            new("scripts", new[] { "alpha" })
        };
        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _reorder.Reorder("no/such/device", orders, null);

        Assert.Null(data);
        Assert.NotEmpty(errors);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Reorder - 无实质修改 → 警告但仍成功
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Reorder_Warns_When_No_Actual_Change()
    {
        string projectRoot = CreateTestProject(scripts: new[] { "alpha", "beta" });
        Init(projectRoot);

        ScriptGroupOrder[] orders = new ScriptGroupOrder[]
        {
            new("scripts", new[] { "alpha", "beta" })
        };
        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = _reorder.Reorder("map.map_/0", orders, null);

        Assert.Empty(errors);
        Assert.NotEmpty(warnings);
        Assert.Contains("无实质变更", warnings[0].Message);
        Assert.NotNull(data);
        Assert.False(data.Applied);
        Assert.Empty(data.AffectedRelativePaths);
    }

    [Fact]
    public void Reorder_Warns_When_Global_Vars_No_Change()
    {
        string projectRoot = CreateTestProject(
            scripts: new[] { "alpha" },
            globalVars: new[] { ("hp", "Float"), ("name", "String") });
        Init(projectRoot);

        (ScriptReorderData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = _reorder.Reorder("map.map_/0", null, new[] { "hp", "name" });

        Assert.Empty(errors);
        Assert.NotEmpty(warnings);
        Assert.Contains("无实质变更", warnings[0].Message);
        Assert.NotNull(data);
        Assert.False(data.Applied);
    }

    #region Test Project Setup

    private string CreateTestProject(
        string[]? scripts = null,
        (string Name, string Guid)[]? codeRepoScripts = null,
        (string Name, string Type)[]? globalVars = null)
    {
        string projectRoot = TempDir;

        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(projectRoot, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(projectRoot, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");

        string mapDir = SystemIOWrapper.Combine(projectRoot, "map.map_");
        SystemIOWrapper.CreateDirectory(mapDir);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "{\"list\":[\"0-1001\"]}");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "{\"list\":[]}");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRoot, "map.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRoot, "map.map"), new byte[] { 0x01, 0x02, 0x03, 0x04 });

        string deviceDir = SystemIOWrapper.Combine(mapDir, "0");
        string scriptsDir = SystemIOWrapper.Combine(deviceDir, "scripts");
        SystemIOWrapper.CreateDirectory(scriptsDir);

        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(deviceDir, "device.json"),
            "type: 1001\next:\n{\nlogicName: test\n}");

        // 构建 list.json（HJSON 格式：每行一项，无逗号）
        List<string> listLines = [];
        if (scripts != null)
        {
            foreach (string name in scripts)
            {
                listLines.Add($"  \"{name}\"");
                SystemIOWrapper.WriteAllText(
                    SystemIOWrapper.Combine(scriptsDir, name + ".code"),
                    "// test script");
            }
        }
        if (codeRepoScripts != null)
        {
            foreach ((string name, string guid) in codeRepoScripts)
            {
                listLines.Add($"  {{guid: \"{guid}\", name: \"{name}\"}}");
            }
        }

        if (listLines.Count > 0)
        {
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(scriptsDir, "list.json"),
                "[\n" + string.Join("\n", listLines) + "\n]");
        }

        // vars.json
        if (globalVars != null && globalVars.Length > 0)
        {
            IEnumerable<string> varLines = globalVars.Select(static v => $"  {{name: \"{v.Name}\", type: \"{v.Type}\"}}");
            SystemIOWrapper.WriteAllText(
                SystemIOWrapper.Combine(deviceDir, "vars.json"),
                "[\n" + string.Join("\n", varLines) + "\n]");
        }

        return projectRoot;
    }

    #endregion
}
