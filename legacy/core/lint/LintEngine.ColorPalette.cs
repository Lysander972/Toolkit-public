using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    private static IReadOnlyList<LintDiagnostic> ValidateColorPaletteFormat(ProjectModel model)
    {
        string? paletteExcel = model.Root.SettingJson?.PaletteExcel;
        if (!GuidText.Validate(paletteExcel))
            return Array.Empty<LintDiagnostic>();

        if (model.FindByGuid(paletteExcel) is not ResourceEntity resource)
            return Array.Empty<LintDiagnostic>();

        try
        {
            ColorPalette palette = new();
            palette.LoadFromExcelBytes(resource.Data);
            return Array.Empty<LintDiagnostic>();
        }
        catch (Exception ex)
        {
            return
            [
                new LintDiagnostic(
                    LintRuleIds.StructColorPaletteFormat,
                    LintSeverity.Error,
                    $"颜色表格式错误: {resource.实体_完整相对路径标识}: {ex.Message}",
                    new LintLocation(resource.实体_完整相对路径标识, 1))
            ];
        }
    }
}
