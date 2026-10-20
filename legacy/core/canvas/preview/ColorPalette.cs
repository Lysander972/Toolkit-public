using ClosedXML.Excel;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Canvas;

/// <summary>
/// 颜色表中的颜色值（RGBA 0-1）
/// </summary>
public sealed class PaletteColor
{
    public float R { get; set; }
    public float G { get; set; }
    public float B { get; set; }
    public float A { get; set; }

    public PaletteColor() { }
    public PaletteColor(float r, float g, float b, float a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }
}

/// <summary>
/// 颜色表加载
/// </summary>
public sealed class ColorPalette
{
    private readonly Dictionary<string, PaletteColor> _palette = new(StringComparer.Ordinal);

    /// <summary>
    /// 从 ProjectModel 加载颜色表。
    /// 通过 SettingsEntity.PaletteExcel 获取路径，使用 model.GetResource 统一读取。
    /// 资源缺失时优雅降级为空调色板（预览功能不应阻断视图重建流程）。
    /// </summary>
    public void LoadFromProject(ProjectModel model)
    {
        _palette.Clear();

        string? targetGuid = model.Root.SettingJson?.PaletteExcel;
        if (!GuidText.Validate(targetGuid))
        {
            return;
        }

        if (model.FindByGuid(targetGuid) is not ProjectEntity entity)
        {
            Log.For(LogModule.Core).Warning(
                $"paletteExcel GUID 未在实体索引中找到（可能模型刷新进行中）：{targetGuid}，跳过颜色表加载");
            return;
        }

        if (entity is not ResourceEntity resource)
        {
            Log.For(LogModule.Core).Warning(
                $"paletteExcel 指向的实体非 ResourceEntity（类型={entity.GetType().Name}）：{targetGuid}，跳过颜色表加载");
            return;
        }

        if (resource.Data.Length == 0)
        {
            Log.For(LogModule.Core).Warning(
                $"paletteExcel 资源数据为空：{targetGuid}，跳过颜色表加载");
            return;
        }

        LoadFromExcelBytes(resource.Data);
    }

    /// <summary>
    /// 从 xlsx 字节数据加载颜色表。
    /// 调用方应通过 ProjectModel 获取资源数据后传入。
    /// </summary>
    public void LoadFromExcelBytes(byte[] excelData)
    {
        _palette.Clear();

        using MemoryStream stream = new MemoryStream(excelData);
        using XLWorkbook workbook = new XLWorkbook(stream);

        IXLWorksheet sheet = workbook.Worksheet(1);

        // 读取表头（第 1 行）
        int nameColIndex = -1;
        int colorColIndex = -1;

        IXLColumn? lastColumnUsed = sheet.LastColumnUsed();
        if (lastColumnUsed == null)
            throw new InvalidDataException("颜色表 Sheet1 无任何列数据");
        int lastColumn = lastColumnUsed.ColumnNumber();
        for (int col = 1; col <= lastColumn; col++)
        {
            string? headerValue = sheet.Cell(1, col).GetValue<string?>();
            if (string.Equals(headerValue, "name", StringComparison.Ordinal))
            {
                nameColIndex = col;
            }
            else if (string.Equals(headerValue, "color", StringComparison.Ordinal))
            {
                colorColIndex = col;
            }
        }

        if (nameColIndex < 0 || colorColIndex < 0)
        {
            throw new InvalidDataException("颜色表缺少 name 或 color 列");
        }

        // 从第 4 行开始读取数据
        int parsedCount = 0;
        IXLRow? lastRowUsed = sheet.LastRowUsed();
        if (lastRowUsed == null)
            throw new InvalidDataException("颜色表 Sheet1 无任何行数据");
        int lastRow = lastRowUsed.RowNumber();

        for (int row = 4; row <= lastRow; row++)
        {
            string? rawName = sheet.Cell(row, nameColIndex).GetValue<string?>();
            string? rawColorStr = sheet.Cell(row, colorColIndex).GetValue<string?>();

            if (rawName is null && rawColorStr is null)
                continue;

            if (rawName is null)
                throw new InvalidDataException($"颜色表第 {row} 行数据不完整：缺少 name");
            if (rawColorStr is null)
                throw new InvalidDataException($"颜色表第 {row} 行数据不完整：缺少 color");

            string name = rawName;
            string colorStr = rawColorStr;

            // 解析颜色（格式：R;G;B;A，0-255）
            PaletteColor color = ParseColor(colorStr);
            _palette[name] = color;
            parsedCount++;
        }

        if (parsedCount == 0)
        {
            throw new InvalidDataException("颜色表未解析到任何有效颜色数据");
        }
    }

    /// <summary>
    /// 解析颜色字符串（格式：R;G;B;A，0-255）
    /// </summary>
    private static PaletteColor ParseColor(string colorStr)
    {
        string[] parts = colorStr.Split(';');
        if (parts.Length < 3)
        {
            throw new InvalidDataException($"颜色格式错误，至少需要 R;G;B: {colorStr}");
        }

        if (!int.TryParse(parts[0], out int r) ||
            !int.TryParse(parts[1], out int g) ||
            !int.TryParse(parts[2], out int b))
        {
            throw new InvalidDataException($"颜色格式错误，RGB 必须是整数：{colorStr}");
        }

        int a = 255;
        if (parts.Length >= 4)
        {
            if (!int.TryParse(parts[3], out a))
            {
                throw new InvalidDataException($"颜色格式错误，Alpha 必须是整数：{colorStr}");
            }
        }

        return new PaletteColor(r / 255f, g / 255f, b / 255f, a / 255f);
    }

    public IReadOnlyDictionary<string, PaletteColor> GetAllColors()
    {
        return _palette;
    }
}
