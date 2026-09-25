using System.Collections.Generic;

namespace Sub_Services.Execute.Excel.Models;

/// <summary>
/// Dữ liệu một Sheet Excel đã đọc
/// </summary>
public class ExcelSheetData
{
    public string SheetName { get; set; } = string.Empty;
    public List<string> Headers { get; set; } = new();
    public List<ExcelRowData> Rows { get; set; } = new();
}
