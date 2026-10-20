using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Analysis;

public sealed class EntityInspectToolTests
{
    [Fact]
    public void Inspect_Returns_Map_Entity()
    {
        using ProjectModel model = ProjectModel.CreateForTesting();
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        model.SetRootForTesting(root);

        ProjectLogicContext logic = new ProjectLogicContext(model);
        EntityInspectTool tool = new EntityInspectTool(logic);
        (EntityInspectData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = tool.Inspect(new EntityInspectArgs(
            EntityGuids: new[] { map.Guid },
            Include: new EntityInspectInclude(Graph: false, Events: false, DataView: false, KeyOrigins: false)));

        Assert.Empty(errors);
        EntityInspectData d = data ?? throw new InvalidOperationException();
        Assert.Single(d.Entities);
        Assert.Equal("Map", d.Entities[0].Kind);
    }

    [Fact]
    public void Inspect_Builds_Map_Graph_With_Mech_And_Device()
    {
        using ProjectModel model = ProjectModel.CreateForTesting();
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MechEntity mech = TestEntityFactory.CreateMech(root, "mech1", "22222222222222222222222222222222");
        MapEntity map = TestEntityFactory.CreateMap(root, "main", "11111111111111111111111111111111");
        map.SetMechImportsInternal([new MechImportInfo("22222222222222222222222222222222")]);

        TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1, logicName: "core");

        // 设置根实体并注册索引
        model.SetRootForTesting(root);

        ProjectLogicContext logic = new ProjectLogicContext(model);
        EntityInspectTool tool = new EntityInspectTool(logic);
        (EntityInspectData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = tool.Inspect(new EntityInspectArgs(
            EntityGuids: new[] { map.Guid },
            Include: new EntityInspectInclude(Graph: true, Events: false, DataView: false, KeyOrigins: false)));

        Assert.Empty(errors);
        EntityView view = Assert.Single((data ?? throw new InvalidOperationException()).Entities);
        EntityGraph graph = view.Graph ?? throw new InvalidOperationException();

        string[] nodeKinds = graph.Nodes.Select(n => n.Kind).ToArray();
        Assert.DoesNotContain("map", nodeKinds);
        Assert.Contains("mech", nodeKinds);
        Assert.Contains("device", nodeKinds);

        // 验证导入关系：地图通过 MechImports 引用机械
        Assert.Contains(graph.Edges, e => e.Kind == "imports" && e.From == map.Guid && e.To == mech.Guid);
        Assert.Contains(graph.Edges, e => e.Kind == "contains" && e.From == map.Guid && e.To == map.Devices[0].DeviceId.ToString());
    }
}
