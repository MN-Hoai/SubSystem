using Sub_Services.Execute.Excel.Models;

namespace Sub_Services.Execute.Excel;

/// <summary>
/// Interface đọc file Excel. Hỗ trợ .xlsx, .xlsm, .xlsb.
/// Abstraction này giúp thay đổi thư viện đọc Excel mà không ảnh hưởng Compare Engine.
/// </summary>
public interface IExcelReaderService
{
    /// <summary>
    /// Đọc toàn bộ dữ liệu từ một file Excel.
    /// </summary>
    /// <param name="filePath">Đường dẫn vật lý tới file</param>
    /// <param name="sheetName">Tên sheet cần đọc. Null = đọc tất cả sheet.</param>
    /// <param name="groupColumn">Tên cột chứa thông tin Tổ</param>
    /// <param name="rowKeyColumns">Danh sách cột làm RowKey, VD: ["STYLE","SP","COLOR"]</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Danh sách sheet với đầy đủ rows</returns>
    Task<List<ExcelSheetData>> ReadAsync(
        string filePath,
        string? sheetName,
        string groupColumn,
        List<string> rowKeyColumns,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra file có thể đọc được không (không bị lock, format hợp lệ).
    /// </summary>
    Task<bool> CanReadAsync(string filePath, CancellationToken cancellationToken = default);
}
