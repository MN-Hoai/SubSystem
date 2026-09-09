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
        /// Truy van va tra ve tu dien { ProductionInfoId -> tong OutputNumber }
        /// cho tat ca DailyOutputDetail co DailyOutput.CreateDate &lt;= cuoi toDate.
        ///
        /// Luong join:
        ///   DailyOutputDetail
        ///     -- (DailyOutputId) --> DailyOutput
        ///                              -- (ProductionInfoId) --> dung de nhom
        /// </summary>
        /// <param name="toDate">Ngay dieu kien toi han (lay den cuoi ngay nay, bao gom)</param>
        /// <returns>Dictionary[ProductionInfoId, TongOutputNumber]</returns>
        public async Task<Dictionary<Guid, int>> GetTotalOutputByProductionInfo(DateOnly toDate)
        {
            var dateLimit = toDate.ToDateTime(TimeOnly.MaxValue); // 23:59:59.9999999

            var result = await _context.DailyOutputDetails
                .Where(d =>
                    d.DailyOutputId.HasValue &&
                    d.OutputNumber.HasValue &&
                    d.DailyOutput.CreateDate <= dateLimit)
                .GroupBy(d => d.DailyOutput.ProductionInfoId)
                .Select(g => new
                {
                    ProductionInfoId = g.Key,
                    Total = g.Sum(d => d.OutputNumber!.Value)
                })
                .ToDictionaryAsync(x => x.ProductionInfoId, x => x.Total);

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
                .Where(d => d.Status == 1)
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
                .Where(p => p.Status == 1 && p.Line != null);

            if (productionDepartmentId.HasValue)
                query = query.Where(p => p.ProductionDepartmentId == productionDepartmentId.Value);

            return await query
                .Select(p => p.Line)
                .Distinct()
                .OrderBy(l => l)
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
            var todayStart = request.ToDate.ToDateTime(TimeOnly.MinValue);
            var todayEnd   = request.ToDate.ToDateTime(TimeOnly.MaxValue);

            var todayOutputMap = await _context.DailyOutputDetails
                .Where(d =>
                    d.DailyOutputId.HasValue &&
                    d.OutputNumber.HasValue &&
                    d.CreateDate >= todayStart &&
                    d.CreateDate <= todayEnd)
                .GroupBy(d => d.DailyOutput.ProductionInfoId)
                .Select(g => new
                {
                    ProductionInfoId = g.Key,
                    Today = g.Sum(d => d.OutputNumber!.Value)
                })
                .ToDictionaryAsync(x => x.ProductionInfoId, x => x.Today);

            // ----------------------------------------------------------------
            // 3. Xay dung query ProductionInfo voi cac dieu kien loc dong
            // ----------------------------------------------------------------
            var query = _context.ProductionInfos.AsQueryable();

            // --- Loc theo Status: neu truyen vao thi dung gia tri do, nguoc lai mac dinh Status == 1 ---
            if (request.Status.HasValue)
                query = query.Where(p => p.Status == request.Status.Value);
            else
                query = query.Where(p => p.Status == 1);

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
            var orderedQuery = query.OrderBy(p => p.Line);

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
    }
}
