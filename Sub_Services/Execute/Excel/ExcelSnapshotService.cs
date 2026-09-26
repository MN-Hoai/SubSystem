using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sub_Entities.Entities;
using Sub_Services.Execute.Excel.Models;

namespace Sub_Services.Execute.Excel;

/// <summary>
/// Implementation của IExcelSnapshotService.
/// Snapshot được lưu trong bảng ExcelSnapshotRow.
/// DataJson = JSON của Values, FormulaJson = JSON của Formulas.
/// </summary>
public class ExcelSnapshotService : IExcelSnapshotService
{
    private readonly SNP_SubSystemDBContext _db;
    private readonly ILogger<ExcelSnapshotService> _logger;

    public ExcelSnapshotService(SNP_SubSystemDBContext db, ILogger<ExcelSnapshotService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<Dictionary<string, ExcelSnapshotRow>> LoadSnapshotAsync(
        Guid excelFileId,
        string sheetName,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.ExcelSnapshotRows
            .Where(r => r.ExcelFileId == excelFileId && r.SheetName == sheetName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Cùng một RowKey có thể nằm ở nhiều Tổ khác nhau, nên GroupName + RowKey mới là định danh duy nhất.
        var dict = new Dictionary<string, ExcelSnapshotRow>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!string.IsNullOrEmpty(row.RowKey))
            {
                var uniqueKey = $"{row.GroupName ?? ""}||{row.RowKey}";
                dict[uniqueKey] = row;
            }
        }

        return dict;
    }

    public async Task<bool> HasSnapshotAsync(
        Guid excelFileId,
        string sheetName,
        CancellationToken cancellationToken = default)
    {
        return await _db.ExcelSnapshotRows
            .AnyAsync(r => r.ExcelFileId == excelFileId && r.SheetName == sheetName, cancellationToken);
    }

    public async Task ReplaceSnapshotAsync(
        Guid excelFileId,
        string sheetName,
        List<ExcelRowData> newRows,
        CancellationToken cancellationToken = default)
    {
        // Xóa thẳng xuống DB bằng SQL DELETE — không qua EF tracker
        // Tránh EF tracker tích lũy rows cũ gây nhân đôi khi nhiều sheet cùng pending
        var deletedCount = await _db.ExcelSnapshotRows
            .Where(r => r.ExcelFileId == excelFileId && r.SheetName == sheetName)
            .ExecuteDeleteAsync(cancellationToken);

        // Insert snapshot mới
        var now     = DateTime.Now;
        var options = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        var entities = newRows.Select(row => new ExcelSnapshotRow
        {
            ExcelFileId    = excelFileId,
            SheetName      = sheetName,
            ExcelRowNumber = row.ExcelRowNumber,
            RowKey         = row.RowKey,
            GroupName      = row.GroupName,
            RowHash        = row.RowHash,
            DataJson       = JsonSerializer.Serialize(row.Values, options),
            FormulaJson    = row.Formulas.Count > 0
                ? JsonSerializer.Serialize(row.Formulas, options)
                : null,
            CreateDate     = now,
        }).ToList();

        _db.ExcelSnapshotRows.AddRange(entities);

        // Commit ngay — atomic per-sheet, không để caller tích lũy nhiều sheet chung 1 SaveChanges
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Replace snapshot {FileId}/{Sheet}: xóa {OldCount} → insert {NewCount}",
            excelFileId, sheetName, deletedCount, newRows.Count);
    }


    /// <summary>
    /// Parse DataJson từ ExcelSnapshotRow thành Dictionary
    /// </summary>
    public static Dictionary<string, string> ParseDataJson(string? dataJson)
    {
        if (string.IsNullOrEmpty(dataJson)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(dataJson)
                   ?? new Dictionary<string, string>();
        }
        catch { return new Dictionary<string, string>(); }
    }

    /// <summary>
    /// Parse FormulaJson từ ExcelSnapshotRow thành Dictionary
    /// </summary>
    public static Dictionary<string, string> ParseFormulaJson(string? formulaJson)
    {
        if (string.IsNullOrEmpty(formulaJson)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(formulaJson)
                   ?? new Dictionary<string, string>();
        }
        catch { return new Dictionary<string, string>(); }
    }
}
