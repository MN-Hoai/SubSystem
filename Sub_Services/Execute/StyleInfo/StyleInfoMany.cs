using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        // =====================================================================
        //  STYLE INFO — Chi tiết mã hàng
        //  StyleInfo (header) 1 —> N StyleDetail (công đoạn)
        // =====================================================================

        #region DTOs

        public class StyleInfoListItem
        {
            public Guid   Id                     { get; set; }
            public Guid   ProductionDepartmentId { get; set; }
            public string DepartmentName         { get; set; }
            public string StyleCode              { get; set; }
            public string Keyword                { get; set; }
            public int    Status                 { get; set; }
            public int    DetailCount            { get; set; }
            public DateTime CreateDate           { get; set; }
            public DateTime UpdateDate           { get; set; }
        }

        public class StyleInfoDetail
        {
            public Guid   Id                     { get; set; }
            public Guid   ProductionDepartmentId { get; set; }
            public string StyleCode              { get; set; }
            public string Keyword                { get; set; }
            public int    Status                 { get; set; }
            public DateTime CreateDate           { get; set; }
            public DateTime UpdateDate           { get; set; }
            public List<StyleDetailItem> Details { get; set; } = new();
        }

        public class StyleDetailItem
        {
            public Guid   Id         { get; set; }
            public string DetailName { get; set; }
            public int    Status     { get; set; }
        }

        public class StyleInfoPagedResult
        {
            public List<StyleInfoListItem> Items    { get; set; } = new();
            public int                     Total    { get; set; }
            public int                     Page     { get; set; }
            public int                     PageSize { get; set; }
            public int                     TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 1;
        }

        public class StyleInfoUpsert_Request
        {
            public Guid?   Id                     { get; set; }
            public Guid    ProductionDepartmentId  { get; set; }
            public string  StyleCode               { get; set; }
            public string  Keyword                 { get; set; }
            public int     Status                  { get; set; } = 1;
            /// <summary>Danh sách tên công đoạn (detail). Gửi toàn bộ — hệ thống sẽ sync.</summary>
            public List<string> DetailNames        { get; set; } = new();
        }

        public class StyleInfoImport_Row
        {
            public string StyleCode    { get; set; }
            public string Keyword      { get; set; }
            public List<string> Details { get; set; } = new();
        }

        #endregion

        #region Queries

        /// <summary>Lấy danh sách StyleInfo có phân trang, lọc theo bộ phận và keyword.</summary>
        public async Task<StyleInfoPagedResult> GetStyleInfoList(
            Guid?  deptId   = null,
            string keyword  = null,
            int    page     = 1,
            int    pageSize = 20)
        {
            var q = _context.StyleInfos
                .AsNoTracking()
                .Where(s => s.Status >= 0);

            if (deptId.HasValue && deptId != Guid.Empty)
                q = q.Where(s => s.ProductionDepartmentId == deptId.Value);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim().ToLower();
                q = q.Where(s => s.StyleCode.ToLower().Contains(kw)
                              || (s.Keyword != null && s.Keyword.ToLower().Contains(kw)));
            }

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(s => s.UpdateDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new
                {
                    s.Id, s.ProductionDepartmentId, s.StyleCode, s.Keyword, s.Status,
                    s.CreateDate, s.UpdateDate,
                    DetailCount = s.StyleDetails.Count(d => d.Status >= 0)
                })
                .ToListAsync();

            // Lấy tên bộ phận
            var deptIds  = items.Select(i => i.ProductionDepartmentId).Distinct().ToList();
            var deptNames = await _context.ProductionDepartments
                .Where(d => deptIds.Contains(d.Id))
                .Select(d => new { d.Id, d.DepartmentName })
                .ToDictionaryAsync(d => d.Id, d => d.DepartmentName);

            return new StyleInfoPagedResult
            {
                Total    = total,
                Page     = page,
                PageSize = pageSize,
                Items    = items.Select(i => new StyleInfoListItem
                {
                    Id                     = i.Id,
                    ProductionDepartmentId = i.ProductionDepartmentId,
                    DepartmentName         = deptNames.TryGetValue(i.ProductionDepartmentId, out var dn) ? dn : "",
                    StyleCode              = i.StyleCode,
                    Keyword                = i.Keyword,
                    Status                 = i.Status,
                    DetailCount            = i.DetailCount,
                    CreateDate             = i.CreateDate,
                    UpdateDate             = i.UpdateDate,
                }).ToList()
            };
        }

        /// <summary>Lấy chi tiết một StyleInfo kèm các StyleDetail.</summary>
        public async Task<StyleInfoDetail> GetStyleInfoById(Guid id)
        {
            var si = await _context.StyleInfos
                .AsNoTracking()
                .Include(s => s.StyleDetails)
                .FirstOrDefaultAsync(s => s.Id == id && s.Status >= 0);

            if (si == null) return null;

            return new StyleInfoDetail
            {
                Id                     = si.Id,
                ProductionDepartmentId = si.ProductionDepartmentId,
                StyleCode              = si.StyleCode,
                Keyword                = si.Keyword,
                Status                 = si.Status,
                CreateDate             = si.CreateDate,
                UpdateDate             = si.UpdateDate,
                Details = si.StyleDetails
                    .Where(d => d.Status >= 0)
                    .OrderBy(d => d.CreateDate)
                    .Select(d => new StyleDetailItem
                    {
                        Id         = d.Id,
                        DetailName = d.DetailName,
                        Status     = d.Status,
                    }).ToList()
            };
        }

        #endregion

        #region Commands

        /// <summary>Tạo mới hoặc cập nhật StyleInfo và sync danh sách StyleDetail.</summary>
        public async Task<(bool Success, string Message, Guid? Id)> UpsertStyleInfo(StyleInfoUpsert_Request req)
        {
            if (req.ProductionDepartmentId == Guid.Empty)
                return (false, "Bộ phận không hợp lệ.", null);
            if (string.IsNullOrWhiteSpace(req.StyleCode))
                return (false, "StyleCode không được để trống.", null);

            var now = DateTime.Now;
            bool isNew = !req.Id.HasValue || req.Id == Guid.Empty;
            Sub_Entities.Entities.StyleInfo si;

            if (isNew)
            {
                si = new Sub_Entities.Entities.StyleInfo
                {
                    Id                     = Guid.NewGuid(),
                    ProductionDepartmentId = req.ProductionDepartmentId,
                    StyleCode              = req.StyleCode.Trim(),
                    Keyword                = BuildKeyword(req.Keyword, req.StyleCode),
                    Status                 = 1,
                    CreateDate             = now,
                    UpdateDate             = now,
                };
                _context.StyleInfos.Add(si);
            }
            else
            {
                si = await _context.StyleInfos
                    .Include(s => s.StyleDetails)
                    .FirstOrDefaultAsync(s => s.Id == req.Id.Value && s.Status >= 0);
                if (si == null) return (false, "Không tìm thấy StyleInfo.", null);

                si.ProductionDepartmentId = req.ProductionDepartmentId;
                si.StyleCode              = req.StyleCode.Trim();
                si.Keyword                = BuildKeyword(req.Keyword, req.StyleCode);
                si.UpdateDate             = now;
            }

            await _context.SaveChangesAsync();

            // Sync StyleDetails
            await SyncStyleDetails(si.Id, req.DetailNames, now);

            return (true, isNew ? "Tạo StyleInfo thành công." : "Cập nhật StyleInfo thành công.", si.Id);
        }

        /// <summary>Xoá mềm StyleInfo và các detail con.</summary>
        public async Task<(bool Success, string Message)> SoftDeleteStyleInfo(Guid id)
        {
            var si = await _context.StyleInfos
                .Include(s => s.StyleDetails)
                .FirstOrDefaultAsync(s => s.Id == id && s.Status >= 0);
            if (si == null) return (false, "Không tìm thấy StyleInfo.");

            var now = DateTime.Now;
            si.Status     = -1;
            si.UpdateDate = now;
            foreach (var d in si.StyleDetails.Where(d => d.Status >= 0))
            {
                d.Status     = -1;
                d.UpdateDate = now;
            }
            await _context.SaveChangesAsync();
            return (true, "Đã xoá StyleInfo thành công.");
        }

        /// <summary>
        /// Nhập hàng loạt StyleInfo từ Excel.
        /// Match theo StyleCode + DeptId → update; không match → tạo mới.
        /// </summary>
        public async Task<(int Created, int Updated, int Failed, List<string> Errors)>
            BulkImportStyleInfo(Guid deptId, Stream excelStream)
        {
            int created = 0, updated = 0, failed = 0;
            var errors = new List<string>();

            List<StyleInfoImport_Row> rows;
            try { rows = ParseStyleInfoExcel(excelStream); }
            catch (Exception ex)
            {
                return (0, 0, 1, new List<string> { "Lỗi đọc file Excel: " + ex.Message });
            }

            var now = DateTime.Now;
            // Load tất cả StyleInfo của dept (status >= 0) vào memory để match
            var existing = await _context.StyleInfos
                .Include(s => s.StyleDetails)
                .Where(s => s.ProductionDepartmentId == deptId && s.Status >= 0)
                .ToListAsync();

            var existingDict = existing
                .GroupBy(s => s.StyleCode?.Trim().ToUpper())
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.StyleCode))
                {
                    failed++;
                    errors.Add($"Bỏ qua dòng trống StyleCode.");
                    continue;
                }
                try
                {
                    var codeKey = row.StyleCode.Trim().ToUpper();
                    if (existingDict.TryGetValue(codeKey, out var si))
                    {
                        // Update
                        if (!string.IsNullOrWhiteSpace(row.Keyword))
                            si.Keyword = row.Keyword.Trim();
                        si.UpdateDate = now;
                        await SyncStyleDetails(si.Id, row.Details, now);
                        updated++;
                    }
                    else
                    {
                        // Create
                        var newSi = new Sub_Entities.Entities.StyleInfo
                        {
                            Id                     = Guid.NewGuid(),
                            ProductionDepartmentId = deptId,
                            StyleCode              = row.StyleCode.Trim(),
                            Keyword                = BuildKeyword(row.Keyword, row.StyleCode),
                            Status                 = 1,
                            CreateDate             = now,
                            UpdateDate             = now,
                        };
                        _context.StyleInfos.Add(newSi);
                        await _context.SaveChangesAsync();
                        await SyncStyleDetails(newSi.Id, row.Details, now);
                        existingDict[codeKey] = newSi;
                        created++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"StyleCode [{row.StyleCode}]: {ex.Message}");
                }
            }

            await _context.SaveChangesAsync();
            return (created, updated, failed, errors);
        }

        #endregion

        #region Private Helpers

        private async Task SyncStyleDetails(Guid styleInfoId, List<string> newNames, DateTime now)
        {
            var existing = await _context.StyleDetails
                .Where(d => d.StyleInfoId == styleInfoId && d.Status >= 0)
                .ToListAsync();

            var newNameSet  = newNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .ToList();

            // Xoá mềm những detail không còn trong danh sách mới
            var toRemove = existing
                .Where(d => !newNameSet.Contains(d.DetailName, StringComparer.OrdinalIgnoreCase))
                .ToList();
            foreach (var d in toRemove) { d.Status = -1; d.UpdateDate = now; }

            // Thêm những detail mới chưa có
            var existingNames = existing
                .Where(d => d.Status >= 0)
                .Select(d => d.DetailName?.ToLower())
                .ToHashSet();
            foreach (var name in newNameSet)
            {
                if (!existingNames.Contains(name.ToLower()))
                {
                    _context.StyleDetails.Add(new Sub_Entities.Entities.StyleDetail
                    {
                        Id          = Guid.NewGuid(),
                        StyleInfoId = styleInfoId,
                        DetailName  = name,
                        Status      = 1,
                        CreateDate  = now,
                        UpdateDate  = now,
                    });
                }
            }
            await _context.SaveChangesAsync();
        }

        private static string BuildKeyword(string manual, string styleCode)
        {
            if (!string.IsNullOrWhiteSpace(manual)) return manual.Trim();
            return styleCode?.Trim().ToUpperInvariant().Replace(" ", "") ?? "";
        }

        private static List<StyleInfoImport_Row> ParseStyleInfoExcel(Stream stream)
        {
            var rows = new List<StyleInfoImport_Row>();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheet(1);
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 2;

            for (int r = 2; r <= lastRow; r++)
            {
                var styleCode = ws.Cell(r, 1).GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(styleCode)) continue;

                var row = new StyleInfoImport_Row
                {
                    StyleCode = styleCode,
                    Keyword   = ws.Cell(r, 2).GetString()?.Trim(),
                };

                // Cột C trở đi là DetailName (dynamic)
                for (int c = 3; c <= lastCol; c++)
                {
                    var detailName = ws.Cell(r, c).GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(detailName))
                        row.Details.Add(detailName);
                }
                rows.Add(row);
            }
            return rows;
        }

        #endregion
    }
}
