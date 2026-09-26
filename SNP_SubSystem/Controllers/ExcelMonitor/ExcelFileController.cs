using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sub_Entities.Entities;
using Sub_Services.Execute;
using Sub_Services.Execute.Background;
using Sub_Services.Execute.Excel;

namespace SNP_SubSystem.Controllers.ExcelMonitor;

/// <summary>
/// API quản lý danh sách file Excel cần monitor.
/// GET    /api/excel-file          - Danh sách file
/// GET    /api/excel-file/{id}     - Chi tiết file
/// POST   /api/excel-file          - Thêm file mới
/// PUT    /api/excel-file/{id}     - Cập nhật cấu hình
/// DELETE /api/excel-file/{id}     - Ngừng monitor
/// GET    /api/excel-file/{id}/changes        - Lịch sử thay đổi
/// GET    /api/excel-file/{id}/groups         - Thống kê tổ
/// GET    /api/excel-file/{id}/groups/{group} - Chi tiết tổ
/// </summary>
[ApiController]
[Route("api/excel-file")]
public class ExcelFileController : ControllerBase
{
    private readonly SNP_SubSystemDBContext _db;
    private readonly IExcelMonitorService  _monitorService;
    private readonly ExcelMonitorWorker    _worker;
    private readonly SubSystemService      _subService;
    private readonly ILogger<ExcelFileController> _logger;

    public ExcelFileController(
        SNP_SubSystemDBContext db,
        IExcelMonitorService monitorService,
        ExcelMonitorWorker worker,
        SubSystemService subService,
        ILogger<ExcelFileController> logger)
    {
        _db             = db;
        _monitorService = monitorService;
        _worker         = worker;
        _subService     = subService;
        _logger         = logger;
    }

    // ── GET /api/excel-file ───────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var files = await _db.ExcelFiles
            .Where(f => f.Status != -2)            // bỏ qua file đã soft-delete
            .OrderByDescending(f => f.CreateDate)
            .Select(f => new
            {
                f.Id,
                f.FileName,
                f.FilePath,
                f.FileType,
                f.Status,
                f.LastHash,
                f.LastReadDate,
                f.CreateDate,
                f.UpdateDate,
                TotalChanges = f.ExcelChangeLogs.Count,
                SheetConfigs = f.ExcelSheetConfigs.Where(s => s.Status == 1).Select(s => new
                {
                    s.Id, s.SheetName, s.GroupColumn, s.RowKeyColumns, s.Status
                }).ToList()
            })
            .ToListAsync();

        return Ok(files);
    }

    // ── GET /api/excel-file/{id} ─────────────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var file = await _db.ExcelFiles
            .Include(f => f.ExcelSheetConfigs)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (file == null) return NotFound();

        var totalChanges = await _db.ExcelChangeLogs.CountAsync(c => c.ExcelFileId == id);
        var lastChange   = await _db.ExcelChangeLogs
            .Where(c => c.ExcelFileId == id)
            .OrderByDescending(c => c.ChangeDate)
            .Select(c => c.ChangeDate)
            .FirstOrDefaultAsync();

        return Ok(new
        {
            file.Id, file.FileName, file.FilePath, file.FileType,
            file.Status, file.LastHash, file.LastReadDate,
            file.CreateDate, file.UpdateDate,
            TotalChanges = totalChanges,
            LastChangeDate = lastChange,
            SheetConfigs = file.ExcelSheetConfigs.Select(s => new
            {
                s.Id,
                s.SheetName,
                s.GroupColumn,
                RowKeyColumns = TryParseJsonArray(s.RowKeyColumns),
                s.HeaderRowIndex,
                s.Status
            })
        });
    }

    // ── POST /api/excel-file ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateExcelFileRequest req)
    {
        // Chỉ Admin (Role tên "Admin" trong DB) mới được thêm file monitor
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var callerId) || !await _subService.IsAdminByUserIdAsync(callerId))
            return StatusCode(403, "Bạn không có quyền thực hiện thao tác này.");

        if (string.IsNullOrWhiteSpace(req.FilePath))
            return BadRequest("FilePath là bắt buộc");

        if (!System.IO.File.Exists(req.FilePath))
            return BadRequest($"File không tồn tại: {req.FilePath}");

        // Kiểm tra trùng: chỉ conflict nếu file đang active hoặc paused (status != -2)
        if (await _db.ExcelFiles.AnyAsync(f => f.FilePath == req.FilePath && f.Status != -2))
            return Conflict("File này đã được thêm vào monitor");

        var ext = Path.GetExtension(req.FilePath).TrimStart('.').ToLower();
        var now = DateTime.Now;

        var excelFile = new ExcelFile
        {
            Id         = Guid.NewGuid(),
            FileName   = Path.GetFileName(req.FilePath),
            FilePath   = req.FilePath,
            FileType   = ext,
            Status     = 1,
            CreateDate = now,
            UpdateDate = now,
        };
        _db.ExcelFiles.Add(excelFile);

        // Thêm SheetConfigs
        if (req.SheetConfigs != null)
        {
            foreach (var sc in req.SheetConfigs)
            {
                _db.ExcelSheetConfigs.Add(new ExcelSheetConfig
                {
                    Id            = Guid.NewGuid(),
                    ExcelFileId   = excelFile.Id,
                    SheetName     = sc.SheetName,
                    GroupColumn   = sc.GroupColumn,
                    RowKeyColumns = sc.RowKeyColumns != null
                        ? JsonSerializer.Serialize(sc.RowKeyColumns)
                        : null,
                    HeaderRowIndex = sc.HeaderRowIndex,
                    Status        = 1,
                    CreateDate    = now,
                    UpdateDate    = now,
                });
            }
        }

        await _db.SaveChangesAsync();

        // Initial Load (tạo snapshot, không tạo ChangeLog)
        // QUAN TRỌNG: await trước khi RegisterFile để tránh race condition
        // (worker có thể ProcessFileChanged trước khi snapshot được tạo → nhân đôi)
        await _monitorService.InitialLoadAsync(excelFile.Id);

        // Đăng ký FileSystemWatcher sau khi snapshot đã sẵn sàng
        _worker.RegisterFile(excelFile.Id, excelFile.FilePath, excelFile.FileName);

        _logger.LogInformation("Đã thêm file monitor: {FilePath}", req.FilePath);

        return CreatedAtAction(nameof(GetById), new { id = excelFile.Id }, new { excelFile.Id });
    }

    // ── PUT /api/excel-file/{id} ─────────────────────────────────────────
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateExcelFileRequest req)
    {
        var file = await _db.ExcelFiles
            .Include(f => f.ExcelSheetConfigs)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (file == null) return NotFound();

        file.Status     = req.Status;
        file.UpdateDate = DateTime.Now;

        // Cập nhật SheetConfigs với soft-delete
        if (req.SheetConfigs != null)
        {
            var now           = DateTime.Now;
            var incomingNames = req.SheetConfigs
                .Select(s => s.SheetName.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 1. Soft-delete: sheet có trong DB nhưng KHÔNG có trong request
            foreach (var dbSheet in file.ExcelSheetConfigs)
            {
                if (!incomingNames.Contains(dbSheet.SheetName))
                {
                    dbSheet.Status     = -1;  // xóa mềm
                    dbSheet.UpdateDate = now;
                }
            }

            // 2. Upsert mỗi sheet trong request
            foreach (var sc in req.SheetConfigs)
            {
                var existing = file.ExcelSheetConfigs
                    .FirstOrDefault(s => string.Equals(s.SheetName, sc.SheetName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    // Update (kể cả restore nếu đang soft-deleted)
                    existing.GroupColumn    = sc.GroupColumn;
                    existing.RowKeyColumns  = sc.RowKeyColumns != null
                        ? JsonSerializer.Serialize(sc.RowKeyColumns) : existing.RowKeyColumns;
                    existing.HeaderRowIndex = sc.HeaderRowIndex;
                    existing.Status         = sc.Status;  // 1 = active
                    existing.UpdateDate     = now;
                }
                else
                {
                    _db.ExcelSheetConfigs.Add(new ExcelSheetConfig
                    {
                        Id             = Guid.NewGuid(),
                        ExcelFileId    = id,
                        SheetName      = sc.SheetName.Trim(),
                        GroupColumn    = sc.GroupColumn,
                        RowKeyColumns  = sc.RowKeyColumns != null
                            ? JsonSerializer.Serialize(sc.RowKeyColumns) : null,
                        HeaderRowIndex = sc.HeaderRowIndex,
                        Status         = sc.Status,
                        CreateDate     = now,
                        UpdateDate     = now,
                    });
                }
            }
        }

        await _db.SaveChangesAsync();

        // Cập nhật watcher nếu thay đổi Status
        if (req.Status == 0)
            _worker.UnregisterFile(id);
        else
            _worker.RegisterFile(id, file.FilePath, file.FileName);

        return NoContent();
    }

    // ── DELETE /api/excel-file/{id} ──────────────────────────────────────
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var file = await _db.ExcelFiles.FindAsync(id);
        if (file == null) return NotFound();

        file.Status     = 0;
        file.UpdateDate = DateTime.Now;
        await _db.SaveChangesAsync();

        _worker.UnregisterFile(id);

        return NoContent();
    }

    // ── POST /api/excel-file/{id}/force-read ─────────────────────────────
    /// <summary>Buộc đọc file ngay lập tức, ghi ChangeLog dù hash không đổi</summary>
    [HttpPost("{id:guid}/force-read")]
    public async Task<IActionResult> ForceRead(Guid id)
    {
        var file = await _db.ExcelFiles.FindAsync(id);
        if (file == null) return NotFound("Không tìm thấy file monitor");

        _logger.LogInformation("Force read requested cho file {FileId} ({FileName})", id, file.FileName);

        var (ok, message) = await _monitorService.ForceReadAsync(id);

        if (!ok)
            return BadRequest(message);

        return Ok(new { message, lastReadDate = DateTime.Now });
    }

    // ── DELETE /api/excel-file/{id}/hard-delete ───────────────────────────
    /// <summary>Xóa mềm: set Status=-2, giữ nguyên toàn bộ data (ChangeLog, Snapshot, SheetConfig)</summary>
    [HttpDelete("{id:guid}/hard-delete")]
    public async Task<IActionResult> HardDelete(Guid id)
    {
        var file = await _db.ExcelFiles.FindAsync(id);
        if (file == null) return NotFound();

        // Dừng watcher trước
        _worker.UnregisterFile(id);

        // Xóa mềm: Status = -2 (deleted), giữ toàn bộ data
        file.Status     = -2;
        file.UpdateDate = DateTime.Now;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Soft-delete file [{FileName}] (Status=-2)", file.FileName);
        return NoContent();
    }


    // ── GET /api/excel-file/{id}/changes ─────────────────────────────────
    [HttpGet("{id:guid}/changes")]
    public async Task<IActionResult> GetChanges(
        Guid id,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] string? changeType = null,
        [FromQuery] string? groupName  = null,
        [FromQuery] int page    = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.ExcelChangeLogs
            .Where(c => c.ExcelFileId == id);

        if (fromDate.HasValue) query = query.Where(c => c.ChangeDate >= fromDate.Value);
        if (toDate.HasValue)   query = query.Where(c => c.ChangeDate <= toDate.Value);
        if (!string.IsNullOrEmpty(changeType)) query = query.Where(c => c.ChangeType == changeType);
        if (!string.IsNullOrEmpty(groupName))  query = query.Where(c => c.GroupName  == groupName);

        var total   = await query.CountAsync();
        var changes = await query
            .OrderByDescending(c => c.ChangeDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id, c.SheetName, c.ExcelRowNumber, c.RowKey, c.GroupName,
                c.ChangeType, c.ColumnName, c.OldValue, c.NewValue,
                c.OldFormula, c.NewFormula, c.OldGroupName, c.NewGroupName,
                c.ChangeDate, c.UserName, c.ComputerName
            })
            .ToListAsync();

        return Ok(new { Total = total, Page = page, PageSize = pageSize, Data = changes });
    }

    // ── GET /api/excel-file/{id}/groups ──────────────────────────────────
    [HttpGet("{id:guid}/groups")]
    public async Task<IActionResult> GetGroups(Guid id)
    {
        // Thống kê số dòng hiện tại theo tổ (từ Snapshot mới nhất)
        var configs = await _db.ExcelSheetConfigs
            .Where(s => s.ExcelFileId == id && s.Status == 1)
            .ToListAsync();

        var result = new List<object>();
        foreach (var config in configs)
        {
            var groups = await _db.ExcelSnapshotRows
                .Where(r => r.ExcelFileId == id && r.SheetName == config.SheetName)
                .GroupBy(r => r.GroupName)
                .Select(g => new { GroupName = g.Key, CurrentCount = g.Count() })
                .ToListAsync();

            // Lấy lần thay đổi cuối cùng của từng tổ
            result.Add(new { config.SheetName, Groups = groups });
        }

        return Ok(result);
    }

    // ── GET /api/excel-file/{id}/groups/{groupName} ───────────────────────
    [HttpGet("{id:guid}/groups/{groupName}")]
    public async Task<IActionResult> GetGroupDetail(Guid id, string groupName,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        var total = await _db.ExcelChangeLogs
            .CountAsync(c => c.ExcelFileId == id && c.GroupName == groupName);

        var changes = await _db.ExcelChangeLogs
            .Where(c => c.ExcelFileId == id && c.GroupName == groupName)
            .OrderByDescending(c => c.ChangeDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Snapshot hiện tại của tổ này
        var currentRows = await _db.ExcelSnapshotRows
            .Where(r => r.ExcelFileId == id && r.GroupName == groupName)
            .ToListAsync();

        return Ok(new
        {
            GroupName    = groupName,
            CurrentCount = currentRows.Count,
            Total        = total,
            Page         = page,
            PageSize     = pageSize,
            Changes      = changes,
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static List<string> TryParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json)
                   ?? new List<string>();
        }
        catch
        {
            // fallback: comma-separated
            return json.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .ToList();
        }
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────
public class SheetConfigRequest
{
    public string SheetName    { get; set; } = string.Empty;
    public string? GroupColumn { get; set; }
    public List<string>? RowKeyColumns { get; set; }
    public int HeaderRowIndex  { get; set; } = 0;
    public int Status          { get; set; } = 1;
}

public class CreateExcelFileRequest
{
    public string FilePath { get; set; } = string.Empty;
    public List<SheetConfigRequest>? SheetConfigs { get; set; }
}

public class UpdateExcelFileRequest
{
    public int Status { get; set; } = 1;
    public List<SheetConfigRequest>? SheetConfigs { get; set; }
}
