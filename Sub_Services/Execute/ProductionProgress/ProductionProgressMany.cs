using Microsoft.EntityFrameworkCore;
using Sub_Entities.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        // =====================================================================
        //  HELPER: Lấy tổng sản lượng tích lũy theo từng ProductionInfoId
        //          đến (<=) một ngày cho trước.
        //  Hàm này viết riêng để có thể tái sử dụng ở nhiều nơi khác.
        // =====================================================================

        /// <summary>
        /// Tính TotalOutput "effective" theo ProductionInfoId:
        ///   1. Góm toàn bộ DailyOutputDetail của tất cả máy (cùng ProductionInfoId)
        ///      theo StyleDetailId → cộng tổng
        ///   2. So sánh số công đoạn có dữ liệu vs. tổng số công đoạn của StyleInfo
        ///   3. Nếu thiếu bất kỳ công đoạn nào → 0; đủ hết → min(sum)
        /// </summary>
        public async Task<Dictionary<Guid, int>> GetTotalOutputByProductionInfo(DateOnly toDate)
        {
            var dateLimit = toDate.ToDateTime(TimeOnly.MaxValue);

            // 1. DailyOutput có StyleInfoId để biết mã hàng thuộc StyleInfo nào
            var dailyList = await _context.DailyOutputs
                .Where(d => d.Status == 1 && d.CreateDate <= dateLimit)
                .Select(d => new { d.Id, d.ProductionInfoId, d.StyleInfoId, d.TotalOutputNumber })
                .ToListAsync();

            if (!dailyList.Any())
                return new Dictionary<Guid, int>();

            var dailyIdList  = dailyList.Select(d => d.Id).ToList();
            var dailyInfoMap = dailyList.ToDictionary(d => d.Id, d => d.ProductionInfoId);

            // ProductionInfoId → StyleInfoId (lấy cái đầu tiên khác null)
            var infoToStyleInfo = dailyList
                .Where(d => d.StyleInfoId != null)
                .GroupBy(d => d.ProductionInfoId)
                .ToDictionary(g => g.Key, g => g.First().StyleInfoId!.Value);

            // 2. Số công đoạn kỳ vọng của mỗi StyleInfo
            var styleInfoIds = infoToStyleInfo.Values.Distinct().ToList();
            var expectedDetailCount = await _context.StyleDetails
                 .Where(sd => sd.Status == 1 && styleInfoIds.Contains(sd.StyleInfoId))
.GroupBy(sd => sd.StyleInfoId)
               
                .Select(g => new { StyleInfoId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.StyleInfoId, x => x.Count);

            // 3. Toàn bộ Detail thực tế
            var detailList = await _context.DailyOutputDetails
                .Where(dt => dt.DailyOutputId.HasValue
                          && dt.OutputNumber.HasValue
                          && dt.Status == 1
                          && dailyIdList.Contains(dt.DailyOutputId!.Value))
                .Select(dt => new { dt.DailyOutputId, dt.StyleDetailId, dt.OutputNumber })
                .ToListAsync();

            // 4. Góm theo (ProductionInfoId, StyleDetailId) → cộng tổng
            var perInfoPerDetail = new Dictionary<Guid, Dictionary<Guid?, int>>();
            foreach (var dt in detailList)
            {
                var infoId = dailyInfoMap[dt.DailyOutputId!.Value];
                if (!perInfoPerDetail.TryGetValue(infoId, out var detMap))
                    perInfoPerDetail[infoId] = detMap = new Dictionary<Guid?, int>();
                var key = dt.StyleDetailId;
                detMap[key] = (detMap.TryGetValue(key, out var v) ? v : 0) + dt.OutputNumber!.Value;
            }

            // 5. Kiểm tra đủ công đoạn chưa, nếu thiếu bất kỳ → 0
            var result = new Dictionary<Guid, int>();
            foreach (var (infoId, detMap) in perInfoPerDetail)
            {
                if (infoToStyleInfo.TryGetValue(infoId, out var styleInfoId) &&
                    expectedDetailCount.TryGetValue(styleInfoId, out var expectedCount))
                {
                    var recordedCount = detMap.Keys.Count(k => k != null);
                    if (recordedCount < expectedCount)
                    {
                        result[infoId] = 0;  // Chưa đủ công đoạn
                        continue;
                    }
                }
                result[infoId] = detMap.Values.Any() ? detMap.Values.Min() : 0;
            }

            // Fallback: không có detail nào cả → 0
            foreach (var d in dailyList)
            {
                if (!result.ContainsKey(d.ProductionInfoId))
                    result[d.ProductionInfoId] = 0;
            }

            return result;
        }

        /// <summary>
        /// Tính TodayOutput "effective" cho ngày hôm nay thực tế.
        /// Cùng logic với TotalOutput: thiếu bất kỳ công đoạn → 0.
        /// Không bị ảnh hưởng bởi filter toDate của người dùng.
        /// </summary>
        private async Task<Dictionary<Guid, int>> GetTodayOutputByProductionInfo(DateOnly toDate)
        {
            var today      = DateOnly.FromDateTime(DateTime.Today);
            var todayStart = today.ToDateTime(TimeOnly.MinValue);
            var todayEnd   = today.ToDateTime(TimeOnly.MaxValue);

            var dailyList = await _context.DailyOutputs
                .Where(d => d.Status == 1
                         && d.CreateDate >= todayStart
                         && d.CreateDate <= todayEnd)
                .Select(d => new { d.Id, d.ProductionInfoId, d.StyleInfoId, d.TotalOutputNumber })
                .ToListAsync();

            if (!dailyList.Any())
                return new Dictionary<Guid, int>();

            var dailyIdList  = dailyList.Select(d => d.Id).ToList();
            var dailyInfoMap = dailyList.ToDictionary(d => d.Id, d => d.ProductionInfoId);

            var infoToStyleInfo = dailyList
                .Where(d => d.StyleInfoId != null)
                .GroupBy(d => d.ProductionInfoId)
                .ToDictionary(g => g.Key, g => g.First().StyleInfoId!.Value);

            var styleInfoIds = infoToStyleInfo.Values.Distinct().ToList();
            var expectedDetailCount = await _context.StyleDetails
                .Where(sd => sd.Status == 1
                          && styleInfoIds.Contains(sd.StyleInfoId))
                .GroupBy(sd => sd.StyleInfoId)
                .Select(g => new { StyleInfoId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.StyleInfoId, x => x.Count);

            var detailList = await _context.DailyOutputDetails
                .Where(dt => dt.DailyOutputId.HasValue
                          && dt.OutputNumber.HasValue
                          && dt.Status == 1
                          && dailyIdList.Contains(dt.DailyOutputId!.Value))
                .Select(dt => new { dt.DailyOutputId, dt.StyleDetailId, dt.OutputNumber })
                .ToListAsync();

            var perInfoPerDetail = new Dictionary<Guid, Dictionary<Guid?, int>>();
            foreach (var dt in detailList)
            {
                var infoId = dailyInfoMap[dt.DailyOutputId!.Value];
                if (!perInfoPerDetail.TryGetValue(infoId, out var detMap))
                    perInfoPerDetail[infoId] = detMap = new Dictionary<Guid?, int>();
                var key = dt.StyleDetailId;
                detMap[key] = (detMap.TryGetValue(key, out var v) ? v : 0) + dt.OutputNumber!.Value;
            }

            var result = new Dictionary<Guid, int>();
            foreach (var (infoId, detMap) in perInfoPerDetail)
            {
                if (infoToStyleInfo.TryGetValue(infoId, out var styleInfoId) &&
                    expectedDetailCount.TryGetValue(styleInfoId, out var expectedCount))
                {
                    var recordedCount = detMap.Keys.Count(k => k != null);
                    if (recordedCount < expectedCount)
                    {
                        result[infoId] = 0;  // Chưa đủ công đoạn trong ngày
                        continue;
                    }
                }
                result[infoId] = detMap.Values.Any() ? detMap.Values.Min() : 0;
            }

            foreach (var d in dailyList)
            {
                if (!result.ContainsKey(d.ProductionInfoId))
                    result[d.ProductionInfoId] = 0;
            }

            return result;
        }

        // =====================================================================
        //  Lấy danh sách phòng ban (ProductionDepartment) đang hoạt động
        // =====================================================================

        /// <summary>
        /// Tra ve danh sach phong ban dang hoat dong (Status == 1),
        /// sap xep theo DepartmentName.
        /// </summary>
        /// <returns>List { Id, DepartmentName }</returns>
        public async Task<List<ProductionDepartment_ListName>> GetDepartmentList()
        {
            return await _context.ProductionDepartments
                .Where(d => d.Status >= 0)   // >= 0: hoạt động, không lấy tạm khoá (-1) hay xóa (-2)
                .OrderBy(d => d.DepartmentName)
                .AsNoTracking()
                .Select(d => new ProductionDepartment_ListName
                {
                    Id             = d.Id,
                    DepartmentName = d.DepartmentName
                })
                .ToListAsync();
        }

        // =====================================================================
        //  Lấy danh sách Line (distinct) từ ProductionInfo
        //  Mỗi giá trị Line chỉ xuất hiện 1 lần, sắp xếp tự nhiên.
        //  Có thể lọc thêm theo ProductionDepartmentId nếu cần.
        // =====================================================================

        /// <summary>
        /// Tra ve danh sach gia tri Line khong trung lap (distinct) tu ProductionInfo,
        /// chi lay cac ban ghi co Status == 1.
        /// Co the loc them theo phong ban cu the.
        /// </summary>
        /// <param name="productionDepartmentId">
        ///   (Tuy chon) Loc theo phong ban. De null de lay tat ca.
        /// </param>
        /// <returns>List chua cac gia tri Line duy nhat, sap xep tang dan</returns>
        public async Task<List<string>> GetLineList(Guid? productionDepartmentId = null)
        {
            var query = _context.ProductionInfos
                .Where(p => p.Status >= 0 && p.Line != null);

            if (productionDepartmentId.HasValue)
                query = query.Where(p => p.ProductionDepartmentId == productionDepartmentId.Value);

            return await query
                .Select(p => p.Line)
                .Distinct()
                .OrderBy(l => 
                    l == "63" ? "043.5" : 
                    l == null ? "" :
                    l.Length == 1 ? "00" + l : 
                    l.Length == 2 ? "0" + l : 
                    l
                )
                .AsNoTracking()
                .ToListAsync();
        }


        // =====================================================================
        //  QUERY CHÍNH: Lấy danh sách tiến độ sản xuất — có phân trang
        //
        //  Nhận tham số qua ProductionProgress_QueryRequest thay vì tham số riêng lẻ.
        //  Phân trang thực hiện trên DB (Skip/Take) trước khi load vào bộ nhớ.
        // =====================================================================

        /// <summary>
        /// Tra ve PagedResult chua danh sach ProductionProgress_OutputInfo bao gom:
        ///   - Toan bo thong tin cua ProductionInfo
        ///   - TotalOutput : tong OutputNumber tich luy den cuoi toDate
        ///   - TodayOutput : tong OutputNumber trong khoang fromDate -> toDate
        ///
        /// Phan trang thuc hien tren DB (Skip/Take) truoc khi load vao bo nho.
        /// </summary>
        /// <param name="request">Request class chua toan bo dieu kien loc va phan trang</param>
        /// <returns>PagedResult&lt;ProductionProgress_OutputInfo&gt;</returns>
        public async Task<PagedResult<ProductionProgress_OutputInfo>> GetProductionProgressList(
            ProductionProgress_QueryRequest request)
        {
            // --- Dam bao gia tri phan trang hop le ---
            request.Normalize();

            // --- Chuyen DateOnly sang DateTime de so sanh voi cot CreateDate (datetime) ---
            var dtFromStart = request.FromDate.ToDateTime(TimeOnly.MinValue); // 00:00:00.000
            var dtToEnd     = request.ToDate.ToDateTime(TimeOnly.MaxValue);   // 23:59:59.9999999

            // ----------------------------------------------------------------
            // 1. TotalOutput: tong OutputNumber tich luy den cuoi ToDate
            //    (goi helper rieng de tai su dung o noi khac)
            // ----------------------------------------------------------------
            var totalOutputMap = await GetTotalOutputByProductionInfo(request.ToDate);

            // ----------------------------------------------------------------
            // 2. TodayOutput: tong OutputNumber cua ngay ToDate
            //    (Chỉ tính trong khoảng từ 00:00:00 đến 23:59:59 của ngày ToDate)
            // ----------------------------------------------------------------
            // 2. TodayOutput: tong san luong "effective" cua ngay ToDate (tuong tu TotalOutput)
            var todayOutputMap = await GetTodayOutputByProductionInfo(request.ToDate);

            // ----------------------------------------------------------------
            // 3. Xay dung query ProductionInfo voi cac dieu kien loc dong
            // ----------------------------------------------------------------
            var query = _context.ProductionInfos.AsQueryable();

            // Luon loai tru Status == -1 (da xoa mem), chi lay Status >= 0
            if (request.Status.HasValue)
                query = query.Where(p => p.Status == request.Status.Value);
            else
                query = query.Where(p => p.Status >= 0);

            // --- Loc theo bo phan san xuat ---
            if (request.ProductionDepartmentId.HasValue)
                query = query.Where(p => p.ProductionDepartmentId == request.ProductionDepartmentId.Value);

            // --- Loc theo Line (khop chinh xac, trim khoang trang) ---
            if (!string.IsNullOrWhiteSpace(request.Line))
                query = query.Where(p => p.Line == request.Line.Trim());

            // --- Loc theo tu khoa: Style, SPMain, Line, Keyword (Contains) ---
            if (!string.IsNullOrWhiteSpace(request.Keyword))
            {
                var kw = request.Keyword.Trim();
                query = query.Where(p =>
                    (p.Style   != null && p.Style.Contains(kw))   ||
                    (p.Spmain  != null && p.Spmain.Contains(kw))  ||
                    (p.Line    != null && p.Line.Contains(kw))     ||
                    (p.Keyword != null && p.Keyword.Contains(kw)));
            }

            // --- Sap xep ---
            // Sắp xếp tự nhiên (số), xếp Line "63" chen giữa "43" và "44"
            var orderedQuery = query.OrderBy(p => 
                p.Line == "63" ? "043.5" : 
                p.Line == null ? "" :
                p.Line.Length == 1 ? "00" + p.Line : 
                p.Line.Length == 2 ? "0" + p.Line : 
                p.Line
            );

            // ----------------------------------------------------------------
            // 4. Dem tong so ban ghi (truoc khi phan trang) — 1 round-trip DB
            // ----------------------------------------------------------------
            var totalCount = await orderedQuery.CountAsync();

            // ----------------------------------------------------------------
            // 5. Lay du lieu trang hien tai (Skip / Take) — 1 round-trip DB
            // ----------------------------------------------------------------
            var productionInfoList = await orderedQuery
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .AsNoTracking()
                .ToListAsync();

            // ----------------------------------------------------------------
            // 6. Map sang DTO, nap 2 truong tong hop san luong
            // ----------------------------------------------------------------
            var items = productionInfoList.Select(p => new ProductionProgress_OutputInfo
            {
                // -- Thong tin ProductionInfo --
                Id                     = p.Id,
                ProductionDepartmentId = p.ProductionDepartmentId,
                Line                   = p.Line,
                Style                  = p.Style,
                Spmain                 = p.Spmain,
                TotalQty               = p.TotalQty,
                FinishQty              = p.FinishQty,
                Color                  = p.Color,
                Target                 = p.Target,
                InlineLine             = p.InlineLine,
                InlineDepartment       = p.InlineDepartment,
                Remark                 = p.Remark,
                Machine                = p.Machine,
                Keyword                = p.Keyword,
                Status                 = p.Status,
                CreateDate             = p.CreateDate,
                UpdateDate             = p.UpdateDate,
                CreateBy               = p.CreateBy,
                UpdateBy               = p.UpdateBy,

                // -- Tong san luong tich luy den cuoi ToDate --
                TotalOutput = totalOutputMap.TryGetValue(p.Id, out var total) ? total : 0,

                // -- Tong san luong trong khoang FromDate -> ToDate --
                TodayOutput = todayOutputMap.TryGetValue(p.Id, out var today) ? today : 0
            }).ToList();

            // ----------------------------------------------------------------
            // 7. Tra ve PagedResult
            // ----------------------------------------------------------------
            return new PagedResult<ProductionProgress_OutputInfo>
            {
                Items      = items,
                TotalCount = totalCount,
                Page       = request.Page,
                PageSize   = request.PageSize
            };
        }

        // =====================================================================
        //  GET: Lấy chi tiết một ProductionInfo theo Id
        // =====================================================================

        /// <summary>Tra ve chi tiet ProductionInfo (Status != -1).</summary>
        public async Task<ProductionInfo?> GetProductionInfoById(Guid id)
        {
            return await _context.ProductionInfos
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.Status >= 0);
        }

        // =====================================================================
        //  DELETE (Soft): Đổi Status = -1
        // =====================================================================

        /// <summary>
        /// Soft-delete ProductionInfo: đổi Status = -1.
        /// Không xóa thực sự khỏi DB.
        /// </summary>
        public async Task<(bool Success, string Message)> SoftDeleteProductionInfo(Guid id)
        {
            var info = await _context.ProductionInfos.FindAsync(id);
            if (info == null || info.Status < 0)
                return (false, "Không tìm thấy mã hàng hoặc đã bị xóa.");

            info.Status     = -1;
            info.UpdateDate = DateTime.Now;
            await _context.SaveChangesAsync();
            return (true, "Đã xóa mã hàng thành công.");
        }

        /// <summary>
        /// Cập nhật trạng thái hàng loạt cho nhiều ProductionInfo.
        /// targetStatus: -1 = Xoá mềm, 1 = Đang hoạt động, 2 = Hoàn thành, 0 = Chờ sản xuất
        /// </summary>
        public async Task<(bool Success, string Message)> BulkUpdateStatus(List<Guid> ids, int targetStatus)
        {
            if (ids == null || !ids.Any())
                return (false, "Không có mã hàng nào được chọn.");

            var infos = await _context.ProductionInfos
                .Where(p => ids.Contains(p.Id) && p.Status >= 0)
                .ToListAsync();

            if (!infos.Any())
                return (false, "Không tìm thấy mã hàng hợp lệ để cập nhật.");

            var now = DateTime.Now;
            foreach (var info in infos)
            {
                info.Status = targetStatus;
                info.UpdateDate = now;
            }

            await _context.SaveChangesAsync();
            return (true, $"Đã cập nhật trạng thái cho {infos.Count} mã hàng.");
        }

        // =====================================================================
        //  HISTORY: Lịch sử mã hàng (ProductionInfo) — cho trang _ProductionInfoHistory
        // =====================================================================

        public class ProductionInfoHistoryDto
        {
            public Guid    Id                    { get; set; }
            public Guid    ProductionDepartmentId { get; set; }
            public string  DeptName              { get; set; }
            public string  Line                  { get; set; }
            public string  Style                 { get; set; }
            public string  Spmain                { get; set; }
            public string  Color                 { get; set; }
            public int?    TotalQty              { get; set; }
            public int?    Target                { get; set; }
            public string  InlineLine            { get; set; }   // yyyy-MM-dd or null
            public string  InlineDepartment      { get; set; }   // yyyy-MM-dd or null
            public string  Remark                { get; set; }
            public int     Status                { get; set; }
            public string  CreateDate            { get; set; }   // yyyy-MM-dd
            public string  UpdateDate            { get; set; }   // yyyy-MM-dd HH:mm
        }

        /// <summary>
        /// Lấy lịch sử mã hàng (ProductionInfo status >= 0) theo khoảng ngày tạo,
        /// bộ phận và trạng thái.
        /// </summary>
        public async Task<List<ProductionInfoHistoryDto>> GetProductionInfoHistory(
            DateOnly? from, DateOnly? to, Guid? deptId, int? status)
        {
            var fromDt = from.HasValue ? from.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Today.AddMonths(-1);
            var toDt   = to.HasValue   ? to.Value.ToDateTime(TimeOnly.MaxValue)   : DateTime.Today.AddDays(1).AddTicks(-1);

            var query = _context.ProductionInfos
                .AsNoTracking()
                .Where(p => p.Status >= 0
                         && p.CreateDate >= fromDt
                         && p.CreateDate <= toDt)
                .Include(p => p.ProductionDepartment)
                .AsQueryable();

            if (deptId.HasValue && deptId.Value != Guid.Empty)
                query = query.Where(p => p.ProductionDepartmentId == deptId.Value);

            if (status.HasValue)
                query = query.Where(p => p.Status == status.Value);

            var list = await query.OrderByDescending(p => p.CreateDate).ToListAsync();

            return list.Select(p => new ProductionInfoHistoryDto
            {
                Id                    = p.Id,
                ProductionDepartmentId = p.ProductionDepartmentId,
                DeptName              = p.ProductionDepartment?.DepartmentName,
                Line                  = p.Line,
                Style                 = p.Style,
                Spmain                = p.Spmain,
                Color                 = p.Color,
                TotalQty              = p.TotalQty,
                Target                = p.Target,
                InlineLine            = p.InlineLine.HasValue ? p.InlineLine.Value.ToString("yyyy-MM-dd") : null,
                InlineDepartment      = p.InlineDepartment.HasValue ? p.InlineDepartment.Value.ToString("yyyy-MM-dd") : null,
                Remark                = p.Remark,
                Status                = p.Status,
                CreateDate            = p.CreateDate.ToString("yyyy-MM-dd"),
                UpdateDate            = p.UpdateDate.ToString("yyyy-MM-dd HH:mm")
            }).ToList();
        }

        // =====================================================================
        //  UPSERT: Tạo mới hoặc cập nhật ProductionInfo
        // =====================================================================


        public class UpsertProductionInfo_Request
        {
            public Guid?   Id                    { get; set; }  // null → tạo mới
            public Guid    ProductionDepartmentId { get; set; }
            public string  Line                  { get; set; }
            public string  Style                 { get; set; }
            public string  Spmain                { get; set; }
            public int?    TotalQty              { get; set; }
            public string  Color                 { get; set; }
            public int?    Target                { get; set; }
            public string  InlineLine            { get; set; }  // yyyy-MM-dd or null
            public string  InlineDepartment      { get; set; }  // yyyy-MM-dd or null
            public string  Remark                { get; set; }
            public int?    Status                { get; set; }  // null → giữ nguyên khi update
        }

        /// <summary>
        /// Tạo mới (Id == null) hoặc cập nhật (Id != null) ProductionInfo.
        /// Keyword tự động = Line + Style + Spmain + Color (uppercase, no space).
        /// Khi tạo mới: Status mặc định = 0.
        /// </summary>
        public async Task<(bool Success, string Message, Guid? Id)> UpsertProductionInfo(
            UpsertProductionInfo_Request req)
        {
            if (string.IsNullOrWhiteSpace(req.Line) ||
                string.IsNullOrWhiteSpace(req.Style) ||
                string.IsNullOrWhiteSpace(req.Spmain) ||
                string.IsNullOrWhiteSpace(req.Color))
                return (false, "Line, Style, SP và Color là bắt buộc.", null);

            var keyword = $"{req.Line}{req.Style}{req.Spmain}{req.Color}"
                .ToUpperInvariant().Replace(" ", "");

            var now = DateTime.Now;

            if (req.Id.HasValue && req.Id.Value != Guid.Empty)
            {
                // --- UPDATE ---
                var info = await _context.ProductionInfos
                    .FirstOrDefaultAsync(p => p.Id == req.Id.Value && p.Status >= 0);
                if (info == null)
                    return (false, "Không tìm thấy mã hàng để cập nhật.", null);

                info.ProductionDepartmentId = req.ProductionDepartmentId;
                info.Line                  = req.Line.Trim();
                info.Style                 = req.Style.Trim();
                info.Spmain                = req.Spmain.Trim();
                info.TotalQty              = req.TotalQty;
                info.Color                 = req.Color.Trim();
                info.Target                = req.Target;
                info.InlineLine            = ParseDateOnly(req.InlineLine);
                info.InlineDepartment      = ParseDateOnly(req.InlineDepartment);
                info.Remark                = req.Remark?.Trim();
                info.Keyword               = keyword;
                info.UpdateDate            = now;
                if (req.Status.HasValue) info.Status = req.Status.Value;

                await _context.SaveChangesAsync();
                return (true, "Cập nhật mã hàng thành công.", info.Id);
            }
            else
            {
                // --- INSERT ---
                // Kiểm tra trùng (cùng Dept + Line + Style + SP + Color)
                bool dup = await _context.ProductionInfos.AnyAsync(p =>
                    p.ProductionDepartmentId == req.ProductionDepartmentId &&
                    p.Line   == req.Line.Trim()   &&
                    p.Style  == req.Style.Trim()  &&
                    p.Spmain == req.Spmain.Trim() &&
                    p.Color  == req.Color.Trim()  &&
                    p.Status >= 0);
                if (dup)
                    return (false, "Mã hàng với Line+Style+SP+Color này đã tồn tại trong bộ phận.", null);

                var info = new ProductionInfo
                {
                    Id                    = Guid.NewGuid(),
                    ProductionDepartmentId = req.ProductionDepartmentId,
                    Line                  = req.Line.Trim(),
                    Style                 = req.Style.Trim(),
                    Spmain                = req.Spmain.Trim(),
                    TotalQty              = req.TotalQty,
                    Color                 = req.Color.Trim(),
                    Target                = req.Target,
                    InlineLine            = ParseDateOnly(req.InlineLine),
                    InlineDepartment      = ParseDateOnly(req.InlineDepartment),
                    Remark                = req.Remark?.Trim(),
                    Keyword               = keyword,
                    Status                = 0,
                    CreateDate            = now,
                    UpdateDate            = now
                };
                _context.ProductionInfos.Add(info);
                await _context.SaveChangesAsync();
                return (true, "Tạo mã hàng mới thành công.", info.Id);
            }
        }

        // ── Helper ───────────────────────────────────────────────────────────
        private static DateOnly? ParseDateOnly(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (DateOnly.TryParseExact(s, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d)) return d;
            if (DateOnly.TryParseExact(s, "dd/MM/yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out d)) return d;
            return null;
        }
    }
}
