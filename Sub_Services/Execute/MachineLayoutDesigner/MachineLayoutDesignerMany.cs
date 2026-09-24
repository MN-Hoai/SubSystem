using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sub_Services.Execute
{
    public partial class SubSystemService
    {
        // =====================================================================
        //  GET MANY: Lấy danh sách bộ phận (dùng cho dropdown)
        // =====================================================================

        /// <summary>
        /// Trả về danh sách bộ phận đang hoạt động (Status == 1) để hiện trong dropdown.
        /// </summary>
        public async Task<List<MachineLayout_DepartmentItem>> GetDepartmentsForLayout()
        {
            return await _context.ProductionDepartments
                .Where(d => d.Status >= 0)   // >= 0: hoạt động, không lấy tạm khoá (-1) hay xóa (-2)
                .OrderBy(d => d.DepartmentName)
                .AsNoTracking()
                .Select(d => new MachineLayout_DepartmentItem
                {
                    Id             = d.Id,
                    DepartmentName = d.DepartmentName
                })
                .ToListAsync();
        }

        // =====================================================================
        //  GET MANY: Lấy danh sách máy theo bộ phận (cho picker "máy có sẵn")
        // =====================================================================

        /// <summary>
        /// Trả về tất cả máy của một bộ phận (Status != 0),
        /// sắp xếp theo MachineNumber để dễ tìm.
        /// </summary>
        public async Task<List<MachineLayout_MachinePickerItem>> GetMachinesByDepartment(Guid departmentId)
        {
            return await _context.ProductionMachines
                .Where(m => m.ProductionDepartmentId == departmentId && m.Status >= -1)
                .OrderBy(m => m.MachineNumber)
                .AsNoTracking()
                .Select(m => new MachineLayout_MachinePickerItem
                {
                    Id            = m.Id,
                    MachineNumber = m.MachineNumber,
                    Remark        = m.Remark,
                    Status        = m.Status ?? 1
                })
                .ToListAsync();
        }

        // =====================================================================

        //  GET MANY: Lấy layout theo bộ phận (layout đầu tiên Status==1)
        // =====================================================================

        /// <summary>
        /// Lấy layout đang hoạt động (Status == 1) của một bộ phận,
        /// kèm toàn bộ ProductLayoutItems và thông tin ProductionMachine.
        /// Trả null nếu bộ phận chưa có layout.
        /// </summary>
        public async Task<MachineLayout_LayoutDto> GetLayoutByDepartment(Guid departmentId)
        {
            var layout = await _context.ProductionDepartmentLayouts
                .AsNoTracking()
                .Where(l => l.ProductionDepartmentId == departmentId && l.Status == 1)
                .OrderByDescending(l => l.CreateDate)
                .Select(l => new MachineLayout_LayoutDto
                {
                    Id             = l.Id,
                    DepartmentId   = l.ProductionDepartmentId,
                    DepartmentName = l.ProductionDepartment.DepartmentName,
                    Name           = l.Name,
                    RowCount       = l.RowCount,
                    ColumnCount    = l.ColumnCount,
                    Gap            = l.Gap ?? 12,
                    CellWidth      = l.CellWidth,
                    CellHeight     = l.CellHeight,
                    Status         = l.Status,
                    Items          = l.ProductLayoutItems
                        .Where(i => i.Status == 1)
                        .Select(i => new MachineLayout_ItemDto
                        {
                            ItemId      = i.Id,
                            MachineId   = i.ProductionMachineId,
                            RowIndex    = i.RowIndex ?? 1,
                            ColumnIndex = i.ColumnIndex ?? 1,
                            MachineName = i.ProductionMachine.MachineNumber,
                            Remark      = i.ProductionMachine.Remark,
                            Keyword     = i.Keyword,
                            Status      = i.ProductionMachine.Status ?? 1,  // trạng thái thực của máy
                            SortOrder   = i.SortOrder
                        })
                        .OrderBy(i => i.RowIndex).ThenBy(i => i.ColumnIndex)
                        .ToList()
                })
                .FirstOrDefaultAsync();

            return layout;
        }

        // =====================================================================
        //  GET MANY: Tất cả layout (cho trang ProductionDepartment)
        // =====================================================================

        /// <summary>
        /// Lấy tất cả layout Status==1, kèm items, tùy chọn lọc theo departmentId.
        /// Status của item lấy từ ProductionMachine.Status (trạng thái thực của máy).
        /// </summary>
        public async Task<List<MachineLayout_LayoutDto>> GetAllLayouts(Guid? departmentId = null)
        {
            var today      = DateTime.Today;
            var todayStart = today;
            var todayEnd   = today.AddDays(1).AddTicks(-1);

            // === PHASE 1: Load cấu trúc layout, items, production codes ===
            var query = _context.ProductionDepartmentLayouts
                .AsNoTracking()
                .Where(l => l.Status == 1);

            if (departmentId.HasValue && departmentId != Guid.Empty)
                query = query.Where(l => l.ProductionDepartmentId == departmentId.Value);

            var layouts = await query
                .OrderBy(l => l.ProductionDepartment.DepartmentName)
                .ThenByDescending(l => l.CreateDate)
                .Select(l => new MachineLayout_LayoutDto
                {
                    Id             = l.Id,
                    DepartmentId   = l.ProductionDepartmentId,
                    DepartmentName = l.ProductionDepartment.DepartmentName,
                    Name           = l.Name,
                    RowCount       = l.RowCount,
                    ColumnCount    = l.ColumnCount,
                    Gap            = l.Gap ?? 12,
                    CellWidth      = l.CellWidth,
                    CellHeight     = l.CellHeight,
                    Status         = l.Status,
                    Items          = l.ProductLayoutItems
                        .Where(i => i.Status == 1)
                        .Select(i => new MachineLayout_ItemDto
                        {
                            ItemId      = i.Id,
                            MachineId   = i.ProductionMachineId,
                            RowIndex    = i.RowIndex    ?? 1,
                            ColumnIndex = i.ColumnIndex ?? 1,
                            MachineName = i.ProductionMachine.MachineNumber,
                            Remark      = i.ProductionMachine.Remark,
                            Keyword     = i.Keyword,
                            Status      = i.ProductionMachine.Status ?? 1,
                            SortOrder   = i.SortOrder,
                            // Lấy danh sách ProductionInfo đang chạy trên máy (chưa có output)
                            ProductionCodes = i.ProductionMachine.DailyOutputs
                                .Where(d => d.Status == 1 && d.ProductionInfo != null && d.ProductionInfo.Status == 1)
                                .GroupBy(d => d.ProductionInfoId)
                                .Select(g => new MachineLayout_ProductionCodeBrief
                                {
                                    ProductionInfoId = g.Key,
                                    Spmain = g.First().ProductionInfo.Spmain,
                                    Style  = g.First().ProductionInfo.Style,
                                    Line   = g.First().ProductionInfo.Line,
                                    Color  = g.First().ProductionInfo.Color,
                                    Target = g.First().ProductionInfo.Target,
                                    // TodayOutput sẽ được tính lại ở Phase 2
                                    TodayOutput    = 0,
                                    TodayOutputPct = 0
                                })
                                .ToList()
                        })
                        .OrderBy(i => i.RowIndex).ThenBy(i => i.ColumnIndex)
                        .ToList()
                })
                .ToListAsync();

            if (!layouts.Any()) return layouts;

            // === PHASE 2: Tính TodayOutput bằng logic min-across-stages ===
            // Lấy tất cả DailyOutput hôm nay
            var allMachineIds = layouts
                .SelectMany(l => l.Items)
                .Select(i => i.MachineId)   // Guid non-nullable
                .Distinct().ToList();

            var todayDailyList = await _context.DailyOutputs
                .Where(d => d.Status == 1
                         && d.CreateDate >= todayStart
                         && d.CreateDate <= todayEnd
                         && allMachineIds.Contains(d.ProductionMachineId))
                .Select(d => new { d.Id, d.ProductionInfoId, MachineId = d.ProductionMachineId })
                .ToListAsync();

            if (!todayDailyList.Any()) return layouts;

            var todayDailyIds = todayDailyList.Select(d => d.Id).ToList();
            // Map DailyOutput.Id → (MachineId, ProductionInfoId) để phân biệt từng máy
            var dailyKeyMap = todayDailyList.ToDictionary(
                d => d.Id,
                d => (MachineId: d.MachineId, InfoId: d.ProductionInfoId));

            // Lấy tất cả detail hôm nay
            var detailList = await _context.DailyOutputDetails
                .Where(dt => dt.DailyOutputId.HasValue
                          && dt.OutputNumber.HasValue
                          && dt.Status == 1
                          && todayDailyIds.Contains(dt.DailyOutputId!.Value))
                .Select(dt => new { dt.DailyOutputId, dt.StyleDetailId, dt.OutputNumber })
                .ToListAsync();

            if (!detailList.Any()) return layouts;

            // Góm theo (MachineId, ProductionInfoId, StyleDetailId) — mỗi máy tính độc lập
            var perMachinePerDetail = new Dictionary<(Guid MachineId, Guid InfoId), Dictionary<Guid, int>>();
            foreach (var dt in detailList)
            {
                if (!dailyKeyMap.TryGetValue(dt.DailyOutputId!.Value, out var key)) continue;
                var detailId = dt.StyleDetailId;
                if (!perMachinePerDetail.TryGetValue(key, out var detMap))
                    perMachinePerDetail[key] = detMap = new Dictionary<Guid, int>();
                detMap[detailId] = (detMap.TryGetValue(detailId, out var v) ? v : 0) + dt.OutputNumber!.Value;
            }

            // Tính TodayOutput = min(tổng từng công đoạn) — riêng theo từng máy
            var todayOutputMap = new Dictionary<(Guid MachineId, Guid InfoId), int>();
            foreach (var (key, detMap) in perMachinePerDetail)
            {
                todayOutputMap[key] = detMap.Values.Any() ? detMap.Values.Min() : 0;
            }

            // Gán lại TodayOutput và TodayOutputPct — lookup theo (MachineId, ProductionInfoId)
            foreach (var layout in layouts)
                foreach (var item in layout.Items)
                    foreach (var code in item.ProductionCodes)
                    {
                        var lookupKey = (item.MachineId, code.ProductionInfoId);
                        var output = todayOutputMap.TryGetValue(lookupKey, out var o) ? o : 0;
                        var target = code.Target ?? 0;
                        code.TodayOutput    = output;
                        code.TodayOutputPct = target > 0
                            ? Math.Round((double)output / target * 100, 1)
                            : 0;
                    }

            return layouts;
        }
    }
}
