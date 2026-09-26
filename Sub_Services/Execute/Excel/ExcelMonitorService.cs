using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sub_Entities.Entities;
using Sub_Services.Execute.Excel.Models;

namespace Sub_Services.Execute.Excel;

/// <summary>
/// Monitor Service - điều phối toàn bộ flow:
/// SHA256 check → ReadExcel → Compare → Transaction(ChangeLog + Snapshot + Hash)
/// </summary>
public class ExcelMonitorService : IExcelMonitorService
{
    private readonly SNP_SubSystemDBContext   _db;
    private readonly IExcelReaderService     _reader;
    private readonly IExcelSnapshotService   _snapshot;
    private readonly IExcelCompareService    _compare;
    private readonly ILogger<ExcelMonitorService> _logger;

    public ExcelMonitorService(
        SNP_SubSystemDBContext db,
        IExcelReaderService reader,
        IExcelSnapshotService snapshot,
        IExcelCompareService compare,
        ILogger<ExcelMonitorService> logger)
    {
        _db       = db;
        _reader   = reader;
        _snapshot = snapshot;
        _compare  = compare;
        _logger   = logger;
    }

    // ──────────────────────────────────────────────────────────────────────
    // PROCESS FILE CHANGED
    // ──────────────────────────────────────────────────────────────────────
    public async Task ProcessFileChangedAsync(Guid excelFileId, CancellationToken cancellationToken = default)
    {
        var excelFile = await _db.ExcelFiles
            .Include(f => f.ExcelSheetConfigs.Where(s => s.Status == 1))
            .FirstOrDefaultAsync(f => f.Id == excelFileId && f.Status == 1, cancellationToken);

        if (excelFile == null)
        {
            _logger.LogWarning("ExcelFile {Id} không tồn tại hoặc đã bị tắt monitor", excelFileId);
            return;
        }

        var filePath = excelFile.FilePath;

        // ── Bước 1: Kiểm tra file có tồn tại / đọc được không ─────────────
        if (!await _reader.CanReadAsync(filePath, cancellationToken))
        {
            await WriteMonitorLogAsync(excelFileId, "READ_ERROR",
                $"File không thể đọc: {filePath}", null, cancellationToken);
            return;
        }

        // ── Bước 2: Tính SHA256 và so sánh với LastHash ───────────────────
        var newHash = await ComputeFileHashAsync(filePath, cancellationToken);
        if (newHash == null)
        {
            await WriteMonitorLogAsync(excelFileId, "READ_ERROR",
                "Không thể tính hash file", null, cancellationToken);
            return;
        }

        if (string.Equals(excelFile.LastHash, newHash, StringComparison.Ordinal))
        {
            _logger.LogDebug("Hash không đổi → bỏ qua file {FileName}", excelFile.FileName);
            await WriteMonitorLogAsync(excelFileId, "HASH_MATCH",
                $"Hash không thay đổi: {newHash}", null, cancellationToken);
            return;
        }

        _logger.LogInformation("Hash thay đổi [{FileName}]: {OldHash} → {NewHash}",
            excelFile.FileName, excelFile.LastHash, newHash);

        await WriteMonitorLogAsync(excelFileId, "HASH_MISMATCH",
            $"Hash thay đổi → bắt đầu đọc Excel", null, cancellationToken);

        // ── Bước 3: Xử lý từng SheetConfig ───────────────────────────────
        var configs = excelFile.ExcelSheetConfigs.ToList();
        if (configs.Count == 0)
        {
            _logger.LogWarning("File {FileName} không có SheetConfig nào được cấu hình", excelFile.FileName);
            return;
        }

        foreach (var config in configs)
        {
            await ProcessSheetAsync(excelFile, config, newHash, cancellationToken);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // FORCE READ  (bỏ qua hash check)
    // ──────────────────────────────────────────────────────────────────────
    public async Task<(bool ok, string message)> ForceReadAsync(
        Guid excelFileId, CancellationToken cancellationToken = default)
    {
        var excelFile = await _db.ExcelFiles
            .Include(f => f.ExcelSheetConfigs.Where(s => s.Status == 1))
            .FirstOrDefaultAsync(f => f.Id == excelFileId, cancellationToken);

        if (excelFile == null)
            return (false, "Không tìm thấy file monitor");

        if (!await _reader.CanReadAsync(excelFile.FilePath, cancellationToken))
            return (false, $"Không thể đọc file: {excelFile.FilePath}");

        var configs = excelFile.ExcelSheetConfigs.ToList();
        if (configs.Count == 0)
            return (false, "File chưa được cấu hình sheet nào");

        // Tính hash mới
        var newHash = await ComputeFileHashAsync(excelFile.FilePath, cancellationToken);
        if (newHash == null)
            return (false, "Không thể tính hash file");

        // Reset LastHash → ProcessSheetAsync sẽ so sánh với snapshot và luôn ghi ChangeLog khi có diff
        excelFile.LastHash = null;
        await _db.SaveChangesAsync(cancellationToken);

        await WriteMonitorLogAsync(excelFileId, "FORCE_READ",
            "Người dùng yêu cầu cập nhật thủ công", null, cancellationToken);

        int totalChanges = 0;
        foreach (var config in configs)
        {
            await ProcessSheetAsync(excelFile, config, newHash, cancellationToken);
        }

        // Cập nhật LastHash và LastReadDate sau khi đọc xong
        excelFile.LastHash     = newHash;
        excelFile.LastReadDate = DateTime.Now;
        excelFile.UpdateDate   = DateTime.Now;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Force read [{FileName}] hoàn thành", excelFile.FileName);
        return (true, $"Đã đọc và so sánh {configs.Count} sheet thành công");
    }

    // ──────────────────────────────────────────────────────────────────────
    // INITIAL LOAD
    // ──────────────────────────────────────────────────────────────────────
    public async Task InitialLoadAsync(Guid excelFileId, CancellationToken cancellationToken = default)
    {
        var excelFile = await _db.ExcelFiles
            .Include(f => f.ExcelSheetConfigs.Where(s => s.Status == 1))
            .FirstOrDefaultAsync(f => f.Id == excelFileId, cancellationToken);

        if (excelFile == null) return;

        var newHash = await ComputeFileHashAsync(excelFile.FilePath, cancellationToken);
        if (newHash == null) return;

        foreach (var config in excelFile.ExcelSheetConfigs)
        {
            var rowKeyColumns = ParseRowKeyColumns(config.RowKeyColumns);
            var groupColumn   = config.GroupColumn ?? string.Empty;

            try
            {
                var sheets = await _reader.ReadAsync(
                    excelFile.FilePath, config.SheetName, groupColumn, rowKeyColumns,
                    config.HeaderRowIndex, cancellationToken);

                var sheet = sheets.FirstOrDefault(s =>
                    string.Equals(s.SheetName, config.SheetName, StringComparison.OrdinalIgnoreCase));

                if (sheet == null) continue;

                // Initial Load: chỉ tạo Snapshot, KHÔNG tạo ChangeLog
                await _snapshot.ReplaceSnapshotAsync(excelFile.Id, config.SheetName, sheet.Rows, cancellationToken);

                // SaveChanges ngay sau từng sheet để tránh race condition nhân đôi snapshot
                // khi nhiều InitialLoad chạy đồng thời (ReplaceSnapshotAsync đọc oldRows từ DB)
                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Initial Load [{Sheet}]: {Count} dòng", config.SheetName, sheet.Rows.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi Initial Load sheet {Sheet}", config.SheetName);
            }
        }

        // Cập nhật LastHash và LastReadDate
        excelFile.LastHash     = newHash;
        excelFile.LastReadDate = DateTime.Now;
        excelFile.UpdateDate   = DateTime.Now;

        await _db.SaveChangesAsync(cancellationToken);

        await WriteMonitorLogAsync(excelFileId, "INITIAL_LOAD",
            $"Initial Load hoàn thành. Hash: {newHash}", null, cancellationToken);
    }


    // ──────────────────────────────────────────────────────────────────────
    // HASH
    // ──────────────────────────────────────────────────────────────────────
    public async Task<string?> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var bytes = SHA256.HashData(stream);
                return Convert.ToHexString(bytes).ToLower();
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi tính hash file {FilePath}", filePath);
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // PRIVATE: Process một Sheet
    // ──────────────────────────────────────────────────────────────────────
    private async Task ProcessSheetAsync(
        ExcelFile excelFile,
        ExcelSheetConfig config,
        string newHash,
        CancellationToken cancellationToken)
    {
        var sheetName     = config.SheetName;
        var rowKeyColumns = ParseRowKeyColumns(config.RowKeyColumns);
        var groupColumn   = config.GroupColumn ?? string.Empty;

        try
        {
            // ── Đọc Excel ─────────────────────────────────────────────────
            List<ExcelSheetData> sheets;
            try
            {
                sheets = await _reader.ReadAsync(
                    excelFile.FilePath, sheetName, groupColumn, rowKeyColumns,
                    config.HeaderRowIndex, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi đọc Excel [{FileName}] sheet [{Sheet}]",
                    excelFile.FileName, sheetName);
                await WriteMonitorLogAsync(excelFile.Id, "READ_ERROR",
                    $"Lỗi đọc sheet {sheetName}", ex.Message, cancellationToken);
                // QUAN TRỌNG: không cập nhật Snapshot khi đọc thất bại
                return;
            }

            var sheet = sheets.FirstOrDefault(s =>
                string.Equals(s.SheetName, sheetName, StringComparison.OrdinalIgnoreCase));

            if (sheet == null)
            {
                _logger.LogWarning("Không tìm thấy sheet [{Sheet}] trong file [{FileName}]",
                    sheetName, excelFile.FileName);
                return;
            }

            // ── Load Snapshot cũ ──────────────────────────────────────────
            var hasSnapshot = await _snapshot.HasSnapshotAsync(excelFile.Id, sheetName, cancellationToken);
            if (!hasSnapshot)
            {
                // Chưa có snapshot → đây là lần monitor đầu tiên sau khi thêm file
                // Xử lý như Initial Load
                _logger.LogInformation("Chưa có snapshot [{Sheet}] → thực hiện Initial Load", sheetName);
                await _snapshot.ReplaceSnapshotAsync(excelFile.Id, sheetName, sheet.Rows, cancellationToken);

                excelFile.LastHash     = newHash;
                excelFile.LastReadDate = DateTime.Now;
                excelFile.UpdateDate   = DateTime.Now;
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            var oldSnapshot = await _snapshot.LoadSnapshotAsync(excelFile.Id, sheetName, cancellationToken);

            // ── Compare ───────────────────────────────────────────────────
            var compareResult = _compare.Compare(sheetName, oldSnapshot, sheet.Rows);

            if (!compareResult.HasChanges)
            {
                _logger.LogDebug("Không có thay đổi trong sheet [{Sheet}]", sheetName);
                // Vẫn cập nhật hash vì file binary đã thay đổi
                excelFile.LastHash     = newHash;
                excelFile.LastReadDate = DateTime.Now;
                excelFile.UpdateDate   = DateTime.Now;
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            // ── Transaction: Ghi ChangeLog + Cập nhật Snapshot + Cập nhật Hash ──
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var now          = DateTime.Now;
                var computerName = Environment.MachineName;

                // Ghi ChangeLog
                foreach (var change in compareResult.Changes)
                {
                    _db.ExcelChangeLogs.Add(new ExcelChangeLog
                    {
                        ExcelFileId    = excelFile.Id,
                        SheetName      = change.SheetName,
                        ExcelRowNumber = change.ExcelRowNumber,
                        RowKey         = change.RowKey,
                        GroupName      = change.GroupName,
                        ChangeType     = change.ChangeType,
                        ColumnName     = change.ColumnName,
                        OldValue       = change.OldValue,
                        NewValue       = change.NewValue,
                        OldFormula     = change.OldFormula,
                        NewFormula     = change.NewFormula,
                        OldGroupName   = change.OldGroupName,
                        NewGroupName   = change.NewGroupName,
                        OldRowKey      = change.OldRowKey,
                        NewRowKey      = change.NewRowKey,
                        ChangeDate     = now,
                        UserName       = null,
                        ComputerName   = computerName,
                    });
                }

                // Cập nhật Snapshot để lần so sánh tiếp theo chỉ ra sự khác biệt mới (Incremental)
                await _snapshot.ReplaceSnapshotAsync(excelFile.Id, sheetName, sheet.Rows, cancellationToken);

                // Cập nhật ExcelFile.LastHash
                excelFile.LastHash     = newHash;
                excelFile.LastReadDate = now;
                excelFile.UpdateDate   = now;

                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                _logger.LogInformation("Đã ghi {Count} thay đổi [{Sheet}] cho file [{FileName}]",
                    compareResult.Changes.Count, sheetName, excelFile.FileName);

                await WriteMonitorLogAsync(excelFile.Id, "READ_SUCCESS",
                    $"Sheet {sheetName}: {compareResult.Changes.Count} thay đổi", null, cancellationToken);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Lỗi transaction khi ghi ChangeLog [{Sheet}]", sheetName);
                await WriteMonitorLogAsync(excelFile.Id, "READ_ERROR",
                    $"Transaction rollback sheet {sheetName}", ex.Message, cancellationToken);
                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Lỗi xử lý sheet [{Sheet}] file [{FileName}]",
                sheetName, excelFile.FileName);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // HELPERS
    // ──────────────────────────────────────────────────────────────────────
    private async Task WriteMonitorLogAsync(
        Guid fileId, string eventType, string? message, string? error,
        CancellationToken cancellationToken)
    {
        try
        {
            _db.ExcelMonitorLogs.Add(new ExcelMonitorLog
            {
                ExcelFileId  = fileId,
                EventType    = eventType,
                Message      = message,
                ErrorMessage = error,
                CreateDate   = DateTime.Now,
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi ghi MonitorLog");
        }
    }

    private static List<string> ParseRowKeyColumns(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            // Fallback: dạng comma-separated
            return json.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }
}
