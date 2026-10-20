using ClosedXML.Excel;
using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint.Rules;

public sealed class TableFormatRuleTests
{
    [Fact]
    public void AnalyzeProject_Should_Not_Report_TableFormat_When_Table_Is_Valid()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        AddXlsxResource(root, "good.xlsx", BuildBaseTableBytes(static sheet =>
        {
            sheet.Cell(1, 1).Value = "id";
            sheet.Cell(1, 2).Value = "hp";
            sheet.Cell(2, 1).Value = "主键";
            sheet.Cell(2, 2).Value = "血量";
            sheet.Cell(3, 1).Value = "long";
            sheet.Cell(3, 2).Value = "float";
            sheet.Cell(4, 1).Value = "1001";
            sheet.Cell(4, 2).Value = "1.25";
        }));

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);

        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysis);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.StructTableFormat);
    }

    [Fact]
    public void AnalyzeProject_Should_Report_TableFormat_When_Float_Cell_Invalid()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        AddXlsxResource(root, "bad.xlsx", BuildBaseTableBytes(static sheet =>
        {
            sheet.Cell(1, 1).Value = "id";
            sheet.Cell(1, 2).Value = "hp";
            sheet.Cell(2, 1).Value = "主键";
            sheet.Cell(2, 2).Value = "血量";
            sheet.Cell(3, 1).Value = "long";
            sheet.Cell(3, 2).Value = "float";
            sheet.Cell(4, 1).Value = "1001";
            sheet.Cell(4, 2).Value = "not-a-float";
        }));

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);

        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysis);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.StructTableFormat && d.Severity == LintSeverity.Error && d.Message.Contains("【4.", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeProject_Should_Report_Category1_When_IdColumnMissing()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        AddXlsxResource(root, "missing_id.xlsx", BuildBaseTableBytes(static sheet =>
        {
            sheet.Cell(1, 1).Value = "uid";
            sheet.Cell(1, 2).Value = "hp";
            sheet.Cell(2, 1).Value = "主键";
            sheet.Cell(2, 2).Value = "血量";
            sheet.Cell(3, 1).Value = "long";
            sheet.Cell(3, 2).Value = "float";
        }));

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysis);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.StructTableFormat && d.Message.Contains("【1.", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeProject_Should_Not_Report_Error_When_CommentRow_Empty()
    {
        // 注释行可以空着，只要有占位行存在即可
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        AddXlsxResource(root, "empty_comment.xlsx", BuildBaseTableBytes(static sheet =>
        {
            sheet.Cell(1, 1).Value = "id";
            sheet.Cell(1, 2).Value = "hp";
            // 注释行故意留空（合法）
            sheet.Cell(3, 1).Value = "long";
            sheet.Cell(3, 2).Value = "float";
            sheet.Cell(4, 1).Value = "1001";
            sheet.Cell(4, 2).Value = "1.0";
        }));

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysis);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.StructTableFormat);
    }

    [Fact]
    public void AnalyzeProject_Should_Report_Category3_When_TypeInvalid()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        AddXlsxResource(root, "bad_type.xlsx", BuildBaseTableBytes(static sheet =>
        {
            sheet.Cell(1, 1).Value = "id";
            sheet.Cell(1, 2).Value = "hp";
            sheet.Cell(2, 1).Value = "主键";
            sheet.Cell(2, 2).Value = "血量";
            sheet.Cell(3, 1).Value = "long";
            sheet.Cell(3, 2).Value = "double";
        }));

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysis);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.StructTableFormat && d.Message.Contains("【3.", StringComparison.Ordinal));
    }

    // 各类型空格子合法，使用默认值。
    [Theory]
    [InlineData("float")]
    [InlineData("long")]
    [InlineData("vector")]
    [InlineData("color")]
    [InlineData("bool")]
    [InlineData("string")]
    public void AnalyzeProject_Should_Not_Report_When_Cell_Empty(string type)
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        AddXlsxResource(root, "empty_cell.xlsx", BuildBaseTableBytes(sheet =>
        {
            sheet.Cell(1, 1).Value = "id";
            sheet.Cell(1, 2).Value = "val";
            sheet.Cell(2, 1).Value = "主键";
            sheet.Cell(2, 2).Value = "值";
            sheet.Cell(3, 1).Value = "long";
            sheet.Cell(3, 2).Value = type;
            sheet.Cell(4, 1).Value = "1001";
            // val 列留空
        }));

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysis);

        Assert.DoesNotContain(diagnostics, static d => d.RuleId == LintRuleIds.StructTableFormat);
    }

    [Fact]
    public void AnalyzeProject_Should_Report_TableFormat_When_Long_Cell_Invalid()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        AddXlsxResource(root, "bad_long.xlsx", BuildBaseTableBytes(static sheet =>
        {
            sheet.Cell(1, 1).Value = "id";
            sheet.Cell(1, 2).Value = "count";
            sheet.Cell(2, 1).Value = "主键";
            sheet.Cell(2, 2).Value = "数量";
            sheet.Cell(3, 1).Value = "long";
            sheet.Cell(3, 2).Value = "long";
            sheet.Cell(4, 1).Value = "1001";
            sheet.Cell(4, 2).Value = "not-a-long";
        }));

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysis);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.StructTableFormat && d.Severity == LintSeverity.Error && d.Message.Contains("【4.", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeProject_Should_Report_Category5_When_FileEmpty()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        AddXlsxResource(root, "empty.xlsx", Array.Empty<byte>());

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysis);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.StructTableFormat && d.Message.Contains("【5.", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeProject_Should_Report_ColorPaletteFormat_When_Palette_Table_Invalid()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        ResourceEntity palette = AddXlsxResource(root, "palette.xlsx", BuildBaseTableBytes(static sheet =>
        {
            sheet.Cell(1, 1).Value = "id";
            sheet.Cell(1, 2).Value = "hp";
            sheet.Cell(2, 1).Value = "主键";
            sheet.Cell(2, 2).Value = "血量";
            sheet.Cell(3, 1).Value = "long";
            sheet.Cell(3, 2).Value = "float";
            sheet.Cell(4, 1).Value = "1001";
            sheet.Cell(4, 2).Value = "1.25";
        }));
        root.SettingJson = new SettingJsonRecord($"paletteExcel:{palette.Guid}");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ScriptAnalysisService analysis = new(model);
        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(model, analysis);

        Assert.Contains(diagnostics, static d => d.RuleId == LintRuleIds.StructColorPaletteFormat && d.Severity == LintSeverity.Error && d.Message.Contains("颜色表缺少 name 或 color 列", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectLogicContext_Should_Use_EmptyPalette_When_Palette_Table_Invalid()
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        ResourceEntity palette = AddXlsxResource(root, "palette.xlsx", BuildBaseTableBytes(static sheet =>
        {
            sheet.Cell(1, 1).Value = "name";
            sheet.Cell(1, 2).Value = "color";
            sheet.Cell(4, 1).Value = "Primary";
            sheet.Cell(4, 2).Value = "bad-color";
        }));
        root.SettingJson = new SettingJsonRecord($"paletteExcel:{palette.Guid}");

        ProjectModel model = ProjectModel.CreateForTesting();
        model.SetRootForTesting(root);
        ProjectLogicContext logic = new(model);

        ColorPalette loadedPalette = logic.GetColorPalette();

        Assert.Empty(loadedPalette.GetAllColors());
    }

#pragma warning disable TK006 // 测试辅助：封装 workbook 构建流程
    private static byte[] BuildBaseTableBytes(Action<IXLWorksheet> fill)
    {
        using XLWorkbook workbook = new();
        IXLWorksheet sheet = workbook.AddWorksheet("Sheet1");
        fill(sheet);

        using MemoryStream stream = new();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
#pragma warning restore TK006

    private static ResourceEntity AddXlsxResource(ProjectRootEntity root, string fileName, byte[] data)
    {
        ResourceEntity resource = new()
        {
            Parent = root,
            Guid = GuidText.NewGuid(),
            Data = data
        };
        resource.SetIdentifierInternal(fileName);

        if (root.Children is not List<ProjectEntity> children)
        {
            throw new InvalidOperationException("root.Children 不是可写 List");
        }

        children.Add(resource);
        return resource;
    }
}
