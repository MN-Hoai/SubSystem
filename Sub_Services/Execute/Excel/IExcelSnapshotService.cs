using Sub_Entities.Entities;
using Sub_Services.Execute.Excel.Models;

namespace Sub_Services.Execute.Excel;

/// <summary>
/// Interface quản lý Snapshot (trạng thái dữ liệu lần đọc trước).
/// </summary>
public interface IExcelSnapshotService
{
    /// <summary>
    /// Load snapshot hiện tại của một file+sheet từ database.
    /// Key: RowKey → ExcelSnapshotRow
    /// </summary>
    Task<Dictionary<string, ExcelSnapshotRow>> LoadSnapshotAsync(
        Guid excelFileId,
        string sheetName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra snapshot có tồn tại chưa (để xác định Initial Load).
    /// </summary>
    Task<bool> HasSnapshotAsync(
        Guid excelFileId,
        string sheetName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Thay thế toàn bộ snapshot cũ bằng snapshot mới (trong một transaction đã mở bên ngoài).
    /// Chỉ gọi sau khi ChangeLog đã được ghi thành công.
    /// </summary>
    Task ReplaceSnapshotAsync(
        Guid excelFileId,
        string sheetName,
        List<ExcelRowData> newRows,
        CancellationToken cancellationToken = default);
}
