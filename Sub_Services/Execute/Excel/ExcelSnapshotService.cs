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

        // Key = RowKey, Value = Entity
        var dict = new Dictionary<string, ExcelSnapshotRow>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!string.IsNullOrEmpty(row.RowKey))
                dict[row.RowKey] = row;
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
        // Xóa toàn bộ snapshot cũ của file+sheet này
        var oldRows = await _db.ExcelSnapshotRows
            .Where(r => r.ExcelFileId == excelFileId && r.SheetName == sheetName)
            .ToListAsync(cancellationToken);

        if (oldRows.Count > 0)
            _db.ExcelSnapshotRows.RemoveRange(oldRows);

        // Insert snapshot mới
        var now     = DateTime.Now;
        var options = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        foreach (var row in newRows)
        {
            _db.ExcelSnapshotRows.Add(new ExcelSnapshotRow
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
            });
        }

        // Không gọi SaveChanges ở đây — caller sẽ gọi trong transaction
        _logger.LogDebug("Chuẩn bị replace snapshot {FileId}/{Sheet}: {OldCount} old → {NewCount} new",
            excelFileId, sheetName, oldRows.Count, newRows.Count);
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
