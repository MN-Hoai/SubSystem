using Sub_Services.Execute.Excel.Models;

namespace Sub_Services.Execute.Excel;

/// <summary>
/// Interface cho Monitor Service - điều phối toàn bộ flow:
/// Read → Compare → WriteChangeLog → UpdateSnapshot → UpdateHash
/// </summary>
public interface IExcelMonitorService
{
    /// <summary>
    /// Xử lý một file khi FileSystemWatcher phát hiện thay đổi.
    /// Flow: SHA256 → Compare Hash → ReadExcel → Compare → Transaction(ChangeLog + Snapshot + Hash)
    /// </summary>
    Task ProcessFileChangedAsync(Guid excelFileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Buộc đọc file ngay lập tức và ghi ChangeLog dù hash không đổi.
    /// Dùng khi người dùng bấm "Cập nhật ngay" — bỏ qua bước so sánh hash.
    /// </summary>
    Task<(bool ok, string message)> ForceReadAsync(Guid excelFileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Thực hiện Initial Load cho file mới thêm vào (không tạo ChangeLog).
    /// </summary>
    Task InitialLoadAsync(Guid excelFileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tính SHA256 của file
    /// </summary>
    Task<string?> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default);
}
