using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sub_Entities.Entities;

namespace SNP_SubSystem.Controllers.ExcelMonitor;

/// <summary>
/// API truy vấn chi tiết một ChangeLog entry.
/// GET /api/excel-change/{id}
/// </summary>
[ApiController]
[Route("api/excel-change")]
public class ExcelChangeController : ControllerBase
{
    private readonly SNP_SubSystemDBContext _db;

    public ExcelChangeController(SNP_SubSystemDBContext db)
    {
        _db = db;
    }

    // ── GET /api/excel-change/{id} ────────────────────────────────────────
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var change = await _db.ExcelChangeLogs
            .Include(c => c.ExcelFile)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (change == null) return NotFound();

        return Ok(new
        {
            change.Id,
            change.ExcelFileId,
            FileName     = change.ExcelFile.FileName,
            change.SheetName,
            change.ExcelRowNumber,
            change.RowKey,
            change.GroupName,
            change.ChangeType,
            change.ColumnName,
            change.OldValue,
            change.NewValue,
            change.OldFormula,
            change.NewFormula,
            change.OldGroupName,
            change.NewGroupName,
            change.ChangeDate,
            change.UserName,
            change.ComputerName,
        });
    }

    // ── GET /api/excel-change/summary?fileId= ─────────────────────────────
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] Guid fileId)
    {
        var summary = await _db.ExcelChangeLogs
            .Where(c => c.ExcelFileId == fileId)
            .GroupBy(c => c.ChangeType)
            .Select(g => new { ChangeType = g.Key, Count = g.Count() })
            .ToListAsync();

        var lastChange = await _db.ExcelChangeLogs
            .Where(c => c.ExcelFileId == fileId)
            .OrderByDescending(c => c.ChangeDate)
            .Select(c => c.ChangeDate)
            .FirstOrDefaultAsync();

        return Ok(new { Summary = summary, LastChangeDate = lastChange });
    }
}
