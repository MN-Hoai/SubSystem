using System.Collections.Generic;

namespace Sub_Services.Execute.Excel.Models;

/// <summary>
/// Dữ liệu một dòng Excel đã đọc (value + formula)
/// </summary>
public class ExcelRowData
{
    /// <summary>Số thứ tự dòng trong Excel (1-based)</summary>
    public int ExcelRowNumber { get; set; }

    /// <summary>RowKey tổng hợp từ các cột định danh, VD: "HV2745|26070742TB|133"</summary>
    public string RowKey { get; set; } = string.Empty;

    /// <summary>Tên Group/Tổ mà dòng này thuộc về</summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>Giá trị từng cột: { "STYLE": "HV2745", "QTY": "3795" }</summary>
    public Dictionary<string, string> Values { get; set; } = new();

    /// <summary>Công thức từng cột (nếu có): { "TARGET": "=SUM(G10:G20)" }</summary>
    public Dictionary<string, string> Formulas { get; set; } = new();

    /// <summary>Hash SHA256 của toàn bộ dữ liệu dòng (dùng để phát hiện thay đổi nhanh)</summary>
    public string RowHash { get; set; } = string.Empty;
}
