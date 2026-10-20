using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint;

public sealed class LintResultsToolTests
{
    [Fact]
    public void Query_Should_Return_Path_For_GlobalKeywordDiagnostic()
    {
        ProjectModel model = ProjectModel.CreateForTesting();
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "logic");

        device.SetVarsInternal(new VarsConfig
        {
            Variables =
            [
                new("if", "Float", null)
            ]
        });

        model.SetRootForTesting(root);

        LintResultsTool tool = new(model);
        (LintQueryResult? result, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = tool.Query(new LintResultsRequest(
            Severity: "error",
            IgnoreRecommended: null,
            Filter: null,
            Start: 1,
            Limit: 50));

        Assert.Empty(errors);
        Assert.Empty(warnings);

        if (result is null)
        {
            throw new InvalidOperationException("Lint 查询无结果");
        }
        LintQueryResult data = result;
        LintDiagnostic diag = Assert.Single(data.Diagnostics);
        Assert.Equal(LintRuleIds.NamingReservedKeyword, diag.RuleId);
        Assert.Equal(device.vars配置_完整相对路径, diag.Location.Path);
    }
}
