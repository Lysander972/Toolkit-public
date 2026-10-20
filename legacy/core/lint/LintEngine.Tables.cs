using ClosedXML.Excel;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    private static readonly HashSet<string> TableSupportedTypes = new(StringComparer.Ordinal)
    {
        "float",
        "long",
        "string",
        "vector",
        "color",
        "bool"
    };

    private static IReadOnlyList<LintDiagnostic> ValidateTableFormats(ProjectModel model)
    {
        List<LintDiagnostic> diagnostics = [];

        foreach (ResourceEntity resource in model.List<ResourceEntity>())
        {
            string fileRelativePath = resource.实体_完整相对路径标识;
            if (!fileRelativePath.EndsWith(".xlsx", StringComparison.Ordinal))
            {
                continue;
            }

            LintDiagnostic? error = ValidateSingleTable(resource.Data, fileRelativePath);
            if (error != null)
            {
                diagnostics.Add(error);
            }
        }

        return diagnostics;
    }

    private static LintDiagnostic? ValidateSingleTable(byte[] data, string fileRelativePath)
    {
        if (data.Length == 0)
        {
            return CreateTableError(5, 1, fileRelativePath, "文件为空");
        }

        try
        {
            using MemoryStream stream = new(data, writable: false);
            using XLWorkbook workbook = new(stream);
            IXLWorksheet sheet = workbook.Worksheet(1);

            return ValidateSheetContent(sheet, fileRelativePath);
        }
        catch (Exception ex)
        {
            return CreateTableError(5, 1, fileRelativePath, $"检查失败: {ex.Message}");
        }
    }

    private static LintDiagnostic? ValidateSheetContent(IXLWorksheet sheet, string fileRelativePath)
    {
        int lastColumn = 0;
        IXLColumn? lastColumnUsed = sheet.LastColumnUsed();
        if (lastColumnUsed is not null)
            lastColumn = lastColumnUsed.ColumnNumber();
        if (lastColumn <= 0)
        {
            return CreateTableError(2, 1, fileRelativePath, "缺少列名行");
        }

        // 读取列名
        List<string> nameList = [];
        for (int col = 1; col <= lastColumn; col++)
        {
            if (sheet.Cell(1, col).IsEmpty())
                break;
            string name = sheet.Cell(1, col).GetValue<string>();

            if (nameList.Contains(name, StringComparer.Ordinal))
            {
                return CreateTableError(2, 1, fileRelativePath, $"列名'{name}'重复");
            }

            nameList.Add(name);
        }

        if (nameList.Count < 2)
        {
            return CreateTableError(2, 1, fileRelativePath, "列名行字段不足");
        }

        if (!string.Equals(nameList[0], "id", StringComparison.Ordinal))
        {
            return CreateTableError(1, 1, fileRelativePath, $"第1列必须是'id'，当前为'{nameList[0]}'");
        }

        // 注释行（第2行）可空，但必须存在作为占位，原版按行号固定读取

        // 读取类型行
        List<string> types = new(nameList.Count);
        for (int col = 1; col <= nameList.Count; col++)
        {
            string typeName = sheet.Cell(3, col).GetValue<string>();
            if (!TableSupportedTypes.Contains(typeName))
            {
                return CreateTableError(3, 3, fileRelativePath, $"第{col}列类型'{typeName}'不合法");
            }

            types.Add(typeName);
        }

        // 验证数据行
        HashSet<int> idSet = [];
        int lastRow = 0;
        IXLRow? lastRowUsed = sheet.LastRowUsed();
        if (lastRowUsed is not null)
            lastRow = lastRowUsed.RowNumber();
        for (int row = 4; row <= lastRow; row++)
        {
            string idText = sheet.Cell(row, 1).GetValue<string>();
            if (!int.TryParse(idText, out int id))
            {
                return CreateTableError(4, row, fileRelativePath, "ID必须是整数");
            }

            if (!idSet.Add(id))
            {
                return CreateTableError(4, row, fileRelativePath, $"ID {id} 重复");
            }

            // 验证各列数据类型
            for (int col = 2; col <= nameList.Count; col++)
            {
                string value = sheet.Cell(row, col).GetValue<string>();
                string typeName = types[col - 1];
                string columnName = nameList[col - 1];

                LintDiagnostic? typeError = ValidateCellType(value, typeName, row, columnName, fileRelativePath);
                if (typeError != null)
                {
                    return typeError;
                }

                if (typeName == "string")
                {
                    List<LintDiagnostic> charDiags = [];
                    AddUnsupportedTextDiagnostics(charDiags, fileRelativePath, row, $"Excel 列 {columnName}", value);
                    if (charDiags.Count > 0)
                        return charDiags[0];
                }
            }
        }

        return null;
    }

    // 表格格式允许空格子，各类型使用默认值。
    // float→0, long→0, string→"", vector→(0,0,0), color→black, bool→false
    private static LintDiagnostic? ValidateCellType(string value, string typeName, int row, string columnName, string fileRelativePath)
    {
#pragma warning disable TK008 // 系统边界：xlsx 数据读取，空值判断对齐原版行为
        if (value.Length == 0)
            return null;
#pragma warning restore TK008

        if (typeName == "float")
        {
            if (float.TryParse(value, out _))
                return null;
            return CreateTableError(4, row, fileRelativePath, $"{columnName}: '{value}' 无法转换为float");
        }
        if (typeName == "long")
        {
            if (long.TryParse(value, out _))
                return null;
            return CreateTableError(4, row, fileRelativePath, $"{columnName}: '{value}' 无法转换为long");
        }
        if (typeName is "string" or "bool")
            return null;
        if (typeName == "vector")
            return ValidateVectorFormat(value, row, columnName, fileRelativePath);
        if (typeName == "color")
            return ValidateColorFormat(value, row, columnName, fileRelativePath);
        return CreateTableError(3, row, fileRelativePath, $"{columnName}: 类型'{typeName}'不合法");
    }

    private static LintDiagnostic? ValidateVectorFormat(string value, int row, string columnName, string fileRelativePath)
    {
        string[] parts = value.Split(';');
        if (parts.Length != 3)
        {
            return CreateTableError(4, row, fileRelativePath, $"{columnName}: '{value}' 不是有效的向量（需要3个分号分隔的数值）");
        }

        foreach (string part in parts)
        {
            if (!float.TryParse(part, out _))
            {
                return CreateTableError(4, row, fileRelativePath, $"{columnName}: '{value}' 向量分量不是有效数值");
            }
        }

        return null;
    }

    private static LintDiagnostic? ValidateColorFormat(string value, int row, string columnName, string fileRelativePath)
    {
        string[] parts = value.Split(';');
        if (parts.Length is not (3 or 4))
        {
            return CreateTableError(4, row, fileRelativePath, $"{columnName}: '{value}' 不是有效的颜色（需要3或4个分号分隔的整数）");
        }

        foreach (string part in parts)
        {
            if (!int.TryParse(part, out _))
            {
                return CreateTableError(4, row, fileRelativePath, $"{columnName}: '{value}' 颜色分量不是有效整数");
            }
        }

        return null;
    }

    private static string CategoryTitle(int category) => category switch
    {
        1 => "缺少ID列",
        2 => "表头格式缺失(列名行/注释行/类型行)",
        3 => "类型不合法",
        4 => "ID重复或者类型错误",
        _ => "其他问题"
    };

    private static LintDiagnostic CreateTableError(int category, int line, string fileRelativePath, string detail)
        => new(LintRuleIds.StructTableFormat, LintSeverity.Error,
            $"【{category}.{CategoryTitle(category)}】{fileRelativePath}: {detail}",
            new LintLocation(fileRelativePath, line));
}
