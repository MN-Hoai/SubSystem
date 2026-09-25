using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sub_Entities.Entities;

namespace SNP_SubSystem.Controllers.ExcelMonitor;

/// <summary>
/// Controller trả về Razor Views cho giao diện Excel Monitor.
/// </summary>
[Route("excel-monitor")]
public class ExcelMonitorController : Controller
{
    private readonly SNP_SubSystemDBContext _db;

    public ExcelMonitorController(SNP_SubSystemDBContext db)
    {
        _db = db;
    }

    // ── Index: Danh sách file monitor ────────────────────────────────────
    [HttpGet("")]
    [HttpGet("index")]
    public async Task<IActionResult> Index()
    {
        var files = await _db.ExcelFiles
            .OrderByDescending(f => f.CreateDate)
            .Select(f => new ExcelMonitorIndexVm
            {
                Id           = f.Id,
                FileName     = f.FileName,
                FilePath     = f.FilePath,
                FileType     = f.FileType,
                Status       = f.Status,
                LastReadDate = f.LastReadDate,
                TotalChanges = f.ExcelChangeLogs.Count,
                LastChangeDate = f.ExcelChangeLogs
                    .OrderByDescending(c => c.ChangeDate)
                    .Select(c => (DateTime?)c.ChangeDate)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return View("~/Views/ExcelMonitor/Index.cshtml", files);
    }

    // ── Detail: Chi tiết file + thống kê tổ ──────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id)
    {
        var file = await _db.ExcelFiles
            .Include(f => f.ExcelSheetConfigs)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (file == null) return NotFound();

        var vm = new ExcelMonitorDetailVm
        {
            File         = file,
            TotalChanges = await _db.ExcelChangeLogs.CountAsync(c => c.ExcelFileId == id),
        };

        foreach (var config in file.ExcelSheetConfigs.Where(s => s.Status == 1))
        {
            var groups = await _db.ExcelSnapshotRows
                .Where(r => r.ExcelFileId == id && r.SheetName == config.SheetName)
                .GroupBy(r => r.GroupName)
                .Select(g => new GroupRowVm
                {
                    GroupName    = g.Key ?? string.Empty,
                    CurrentCount = g.Count(),
                })
                .ToListAsync();

            vm.SheetGroups.Add(new SheetGroupVm
            {
                SheetName = config.SheetName,
                Groups    = groups,
            });
        }

        return View("~/Views/ExcelMonitor/Detail.cshtml", vm);
    }

    // ── ChangeHistory: Lịch sử thay đổi ─────────────────────────────────
    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> ChangeHistory(
        Guid id,
        DateTime? fromDate  = null,
        DateTime? toDate    = null,
        string? sheetName   = null,
        string? changeType  = null,
        string? groupName   = null,
        string? columnName  = null,
        int page     = 1,
        int pageSize = 50)
    {
        var file = await _db.ExcelFiles
            .Include(f => f.ExcelSheetConfigs)
            .FirstOrDefaultAsync(f => f.Id == id);
        if (file == null) return NotFound();

        var query = _db.ExcelChangeLogs.Where(c => c.ExcelFileId == id).AsQueryable();
        if (fromDate.HasValue)              query = query.Where(c => c.ChangeDate >= fromDate.Value);
        if (toDate.HasValue)               query = query.Where(c => c.ChangeDate <= toDate.Value.AddDays(1));
        if (!string.IsNullOrEmpty(sheetName))  query = query.Where(c => c.SheetName  == sheetName);
        if (!string.IsNullOrEmpty(changeType)) query = query.Where(c => c.ChangeType == changeType);
        if (!string.IsNullOrEmpty(groupName))  query = query.Where(c => c.GroupName  == groupName);
        if (!string.IsNullOrEmpty(columnName)) query = query.Where(c => c.ColumnName == columnName);

        var total   = await query.CountAsync();
        var changes = await query
            .OrderByDescending(c => c.ChangeDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Lấy danh sách sheet từ config (và từ changelog để đảm bảo không bỏ sót)
        var sheetsFromConfig = file.ExcelSheetConfigs
            .Select(s => s.SheetName)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        var sheetsFromLog = await _db.ExcelChangeLogs
            .Where(c => c.ExcelFileId == id && c.SheetName != null)
            .Select(c => c.SheetName!)
            .Distinct()
            .ToListAsync();

        var allSheets = sheetsFromConfig
            .Union(sheetsFromLog)
            .OrderBy(s => s)
            .ToList();

        // Lấy danh sách cột đã xuất hiện trong changelog (filter theo sheet nếu đã chọn)
        var colQuery = _db.ExcelChangeLogs
            .Where(c => c.ExcelFileId == id && c.ColumnName != null);
        if (!string.IsNullOrEmpty(sheetName))
            colQuery = colQuery.Where(c => c.SheetName == sheetName);

        var columns = await colQuery
            .Select(c => c.ColumnName!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        var vm = new ChangeHistoryVm
        {
            File       = file,
            Changes    = changes,
            Total      = total,
            Page       = page,
            PageSize   = pageSize,
            FromDate   = fromDate,
            ToDate     = toDate,
            SheetName  = sheetName,
            ChangeType = changeType,
            GroupName  = groupName,
            ColumnName = columnName,
            Sheets     = allSheets,
            Groups     = await _db.ExcelSnapshotRows
                .Where(r => r.ExcelFileId == id &&
                            (string.IsNullOrEmpty(sheetName) || r.SheetName == sheetName))
                .Select(r => r.GroupName)
                .Distinct()
                .OrderBy(g => g)
                .ToListAsync(),
            Columns    = columns,
            RowKeyColumnNames = GetRowKeyNames(file, sheetName),
        };

        return View("~/Views/ExcelMonitor/ChangeHistory.cshtml", vm);
    }

    [HttpGet("{id}/recent-sheets")]
    public async Task<IActionResult> GetRecentSheets(Guid id)
    {
        var recentSheets = await _db.ExcelChangeLogs
            .Where(c => c.ExcelFileId == id && !string.IsNullOrEmpty(c.SheetName))
            .GroupBy(c => c.SheetName)
            .Select(g => new { 
                SheetName = g.Key, 
                LastChangeDate = g.Max(c => c.ChangeDate) 
            })
            .OrderByDescending(x => x.LastChangeDate)
            .Take(5)
            .Select(x => x.SheetName)
            .ToListAsync();

        return Json(new { success = true, sheets = recentSheets });
    }

    class RowState
    {
        public string CurrentGroup { get; set; } = string.Empty;
        public string CurrentKey { get; set; } = string.Empty;
        
        public bool IsAdded { get; set; }
        public bool IsDeleted { get; set; }
        
        public bool HasValueChanged { get; set; }
        public bool HasRowKeyChanged { get; set; }
        public bool HasGroupChanged { get; set; }
        
        public string OriginalGroup { get; set; } = string.Empty;
        public string OriginalKey { get; set; } = string.Empty;
    }

    [HttpGet("{id}/quick-info")]
    public async Task<IActionResult> GetQuickInfo(Guid id, [FromQuery] string? sheetName)
    {
        var query = _db.ExcelChangeLogs.Where(c => c.ExcelFileId == id);
        if (!string.IsNullOrEmpty(sheetName))
        {
            query = query.Where(c => c.SheetName == sheetName);
        }

        // Lấy tất cả lịch sử thay đổi theo thứ tự thời gian để Replay
        var logs = await query.OrderBy(c => c.ChangeDate).ThenBy(c => c.Id).ToListAsync();

        var states = new Dictionary<string, RowState>(StringComparer.Ordinal);
        string GetStateKey(string? group, string? key) => $"{group ?? ""}||{key ?? ""}";

        foreach (var log in logs)
        {
            if (log.ChangeType == "ROW_ADDED")
            {
                var k = GetStateKey(log.GroupName, log.RowKey);
                states[k] = new RowState { 
                    CurrentGroup = log.GroupName ?? "", CurrentKey = log.RowKey ?? "",
                    OriginalGroup = log.GroupName ?? "", OriginalKey = log.RowKey ?? "",
                    IsAdded = true 
                };
            }
            else if (log.ChangeType == "ROW_DELETED")
            {
                var k = GetStateKey(log.GroupName, log.RowKey);
                if (states.TryGetValue(k, out var state)) {
                    state.IsDeleted = true;
                } else {
                    states[k] = new RowState {
                        CurrentGroup = log.GroupName ?? "", CurrentKey = log.RowKey ?? "",
                        OriginalGroup = log.GroupName ?? "", OriginalKey = log.RowKey ?? "",
                        IsDeleted = true
                    };
                }
            }
            else if (log.ChangeType == "GROUP_CHANGED")
            {
                var oldK = GetStateKey(log.OldGroupName, log.RowKey);
                var newK = GetStateKey(log.NewGroupName, log.RowKey);
                
                if (states.TryGetValue(oldK, out var state)) {
                    states.Remove(oldK);
                    state.CurrentGroup = log.NewGroupName ?? "";
                    state.HasGroupChanged = (state.OriginalGroup != state.CurrentGroup);
                    states[newK] = state;
                } else {
                    states[newK] = new RowState {
                        CurrentGroup = log.NewGroupName ?? "", CurrentKey = log.RowKey ?? "",
                        OriginalGroup = log.OldGroupName ?? "", OriginalKey = log.RowKey ?? "",
                        HasGroupChanged = true
                    };
                }
            }
            else if (log.ChangeType == "ROWKEY_CHANGED")
            {
                var oldK = GetStateKey(log.GroupName, log.OldRowKey);
                var newK = GetStateKey(log.GroupName, log.NewRowKey);
                
                if (states.TryGetValue(oldK, out var state)) {
                    states.Remove(oldK);
                    state.CurrentKey = log.NewRowKey ?? "";
                    state.HasRowKeyChanged = (state.OriginalKey != state.CurrentKey);
                    states[newK] = state;
                } else {
                    states[newK] = new RowState {
                        CurrentGroup = log.GroupName ?? "", CurrentKey = log.NewRowKey ?? "",
                        OriginalGroup = log.GroupName ?? "", OriginalKey = log.OldRowKey ?? "",
                        HasRowKeyChanged = true
                    };
                }
            }
            else if (log.ChangeType == "VALUE_CHANGED" || log.ChangeType == "FORMULA_CHANGED")
            {
                var k = GetStateKey(log.GroupName, log.RowKey);
                if (states.TryGetValue(k, out var state)) {
                    state.HasValueChanged = true;
                } else {
                    states[k] = new RowState {
                        CurrentGroup = log.GroupName ?? "", CurrentKey = log.RowKey ?? "",
                        OriginalGroup = log.GroupName ?? "", OriginalKey = log.RowKey ?? "",
                        HasValueChanged = true
                    };
                }
            }
        }

        var activeStates = states.Values.ToList();

        // Lấy danh sách Thêm mã (Gồm những mã từng được sinh ra và hiện CHƯA BỊ XÓA)
        // Yêu cầu: Gôm thêm dữ liệu ở tab thay đổi mã (HasRowKeyChanged) VÀ tab chuyển tổ (HasGroupChanged) vào tab thêm mã
        var added = activeStates
            .Where(s => !s.IsDeleted && (s.IsAdded || s.HasRowKeyChanged || s.HasGroupChanged))
            .GroupBy(s => s.CurrentGroup)
            .Select(g => new {
                GroupName = string.IsNullOrEmpty(g.Key) ? "(Chưa phân tổ)" : g.Key,
                Rows = g.Select(s => s.CurrentKey).Distinct().ToList()
            })
            .OrderBy(x => x.GroupName)
            .ToList();

        // Lấy danh sách Chuyển tổ (GROUP_CHANGED), gom theo tổ cũ và tổ mới
        var groupChanged = activeStates
            .Where(s => !s.IsDeleted && !s.IsAdded && s.HasGroupChanged)
            .GroupBy(s => new { s.OriginalGroup, s.CurrentGroup })
            .Select(g => new {
                OldGroupName = string.IsNullOrEmpty(g.Key.OriginalGroup) ? "(Chưa phân tổ)" : g.Key.OriginalGroup,
                NewGroupName = string.IsNullOrEmpty(g.Key.CurrentGroup) ? "(Chưa phân tổ)" : g.Key.CurrentGroup,
                Rows = g.Select(s => s.CurrentKey).Distinct().ToList()
            })
            .OrderBy(x => x.OldGroupName)
            .ToList();

        // Tab thay đổi mã vẫn giữ nguyên (ROWKEY_CHANGED)
        var rowkeyChanged = activeStates
            .Where(s => !s.IsDeleted && !s.IsAdded && s.HasRowKeyChanged)
            .GroupBy(s => s.CurrentGroup)
            .Select(g => new {
                GroupName = string.IsNullOrEmpty(g.Key) ? "(Chưa phân tổ)" : g.Key,
                Rows = g.Select(s => new { OldKey = s.OriginalKey, NewKey = s.CurrentKey }).ToList()
            })
            .OrderBy(x => x.GroupName)
            .ToList();

        var valueChanged = activeStates
            .Where(s => !s.IsDeleted && !s.IsAdded && s.HasValueChanged)
            .GroupBy(s => s.CurrentGroup)
            .Select(g => new {
                GroupName = string.IsNullOrEmpty(g.Key) ? "(Chưa phân tổ)" : g.Key,
                Rows = g.Select(s => s.CurrentKey).Distinct().ToList()
            })
            .OrderBy(x => x.GroupName)
            .ToList();

        // Deleted items must not have been added during this period (ghosts)
        var deleted = activeStates
            .Where(s => s.IsDeleted && !s.IsAdded)
            .GroupBy(s => s.OriginalGroup) // Group by original group because it's deleted
            .Select(g => new {
                GroupName = string.IsNullOrEmpty(g.Key) ? "(Chưa phân tổ)" : g.Key,
                Rows = g.Select(s => s.OriginalKey).Distinct().ToList()
            })
            .OrderBy(x => x.GroupName)
            .ToList();

        return Json(new { success = true, added, groupChanged, rowkeyChanged, valueChanged, deleted });
    }

    // ── Helper: lấy tên các cột RowKey từ SheetConfig ────────────────────
    private static List<string> GetRowKeyNames(ExcelFile file, string? sheetName)
    {
        var config = file.ExcelSheetConfigs
            .Where(s => string.IsNullOrEmpty(sheetName) || s.SheetName == sheetName)
            .FirstOrDefault();

        if (config?.RowKeyColumns == null) return new List<string> { "RowKey" };
        try
        {
            var cols = System.Text.Json.JsonSerializer.Deserialize<List<string>>(config.RowKeyColumns);
            return cols != null && cols.Count > 0 ? cols : new List<string> { "RowKey" };
        }
        catch
        {
            var parts = config.RowKeyColumns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length > 0 ? parts.ToList() : new List<string> { "RowKey" };
        }
    }
}

// ── ViewModels ────────────────────────────────────────────────────────────
public class ExcelMonitorIndexVm
{
    public Guid     Id             { get; set; }
    public string   FileName       { get; set; } = string.Empty;
    public string   FilePath       { get; set; } = string.Empty;
    public string?  FileType       { get; set; }
    public int      Status         { get; set; }
    public DateTime? LastReadDate  { get; set; }
    public int      TotalChanges   { get; set; }
    public DateTime? LastChangeDate { get; set; }
}

public class ExcelMonitorDetailVm
{
    public ExcelFile File          { get; set; } = null!;
    public int TotalChanges        { get; set; }
    public List<SheetGroupVm> SheetGroups { get; set; } = new();
}

public class SheetGroupVm
{
    public string SheetName     { get; set; } = string.Empty;
    public List<GroupRowVm> Groups { get; set; } = new();
}

public class GroupRowVm
{
    public string GroupName    { get; set; } = string.Empty;
    public int    CurrentCount { get; set; }
}

public class ChangeHistoryVm
{
    public ExcelFile File              { get; set; } = null!;
    public List<ExcelChangeLog> Changes { get; set; } = new();
    public int Total                   { get; set; }
    public int Page                    { get; set; }
    public int PageSize                { get; set; }
    public int TotalPages              => (int)Math.Ceiling(Total / (double)PageSize);
    public DateTime? FromDate          { get; set; }
    public DateTime? ToDate            { get; set; }
    public string? SheetName           { get; set; }
    public string? ChangeType          { get; set; }
    public string? GroupName           { get; set; }
    public string? ColumnName          { get; set; }
    public List<string>  Sheets        { get; set; } = new();
    public List<string?> Groups        { get; set; } = new();
    public List<string>  Columns       { get; set; } = new();
    /// <summary>Tên các cột tạo RowKey, VD: ["STYLE","SP","COLOR"]</summary>
    public List<string>  RowKeyColumnNames { get; set; } = new();
}
