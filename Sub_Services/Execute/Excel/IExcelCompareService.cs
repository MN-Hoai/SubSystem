using Sub_Entities.Entities;
using Sub_Services.Execute.Excel.Models;

namespace Sub_Services.Execute.Excel;

/// <summary>
/// Interface cho Compare Engine.
/// </summary>
public interface IExcelCompareService
{
    /// <summary>
    /// So sánh snapshot cũ (từ DB) với dữ liệu mới (đã đọc từ Excel).
    /// Trả về danh sách thay đổi theo thứ tự: GROUP_CHANGED → ROW_ADDED → ROW_DELETED → VALUE_CHANGED → FORMULA_CHANGED
    /// </summary>
    ExcelCompareResult Compare(
        string sheetName,
        Dictionary<string, ExcelSnapshotRow> oldSnapshot,
        List<ExcelRowData> newRows);
}
