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
        //  GET: Danh sách mã sản xuất theo machineId (qua DailyOutput)
        // =====================================================================

        /// <summary>
        /// Lấy tất cả ProductionInfo có DailyOutput gắn với machineId,
        /// kèm tổng sản lượng. Tùy chọn filter theo keyword / spmain.
        /// </summary>
        public async Task<List<MachineDetail_ProductionInfoDto>> GetProductionInfosByMachine(
            Guid machineId, string keyword = null)
        {
            // Bước 1: Tìm danh sách ProductionInfo đang gắn với máy này
            var infoIds = await _context.DailyOutputs
                .Where(d => d.ProductionMachineId == machineId && d.Status == 1)
                .Select(d => d.ProductionInfoId)
                .Distinct()
                .ToListAsync();

            var query = _context.ProductionInfos
                .AsNoTracking()
                .Where(p => infoIds.Contains(p.Id));

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim().ToLower();
                query = query.Where(p =>
                    p.Spmain.ToLower().Contains(kw) ||
                    p.Style.ToLower().Contains(kw) ||
                    (p.Keyword != null && p.Keyword.ToLower().Contains(kw)));
            }

            var today = DateTime.Today;

            // Bước 2: Lấy tất cả DailyOutput của máy này (phẳng)
            var allDailyIds = await _context.DailyOutputs
                .Where(d => d.ProductionMachineId == machineId
                         && d.Status == 1
                         && infoIds.Contains(d.ProductionInfoId))
                .Select(d => new { d.Id, d.ProductionInfoId, d.TotalOutputNumber,
                                   IsToday = d.CreateDate.Date == today })
                .ToListAsync();

            var allDailyIdList = allDailyIds.Select(x => x.Id).ToList();

            // Bước 3: Lấy tất cả DailyOutputDetail tương ứng (phẳng)
            var allDetails = await _context.DailyOutputDetails
                .Where(dt => allDailyIdList.Contains(dt.DailyOutputId ?? Guid.Empty)
                          && dt.Status == 1)
                .Select(dt => new { dt.DailyOutputId, dt.StyleDetailId, dt.OutputNumber })
                .ToListAsync();

            // Bước 4: tính effective (min logic) per DailyOutput record — client-side
            // Nhóm details theo DailyOutputId → sum mỗi StyleDetail → lấy min
            var detailsByDaily = allDetails
                .GroupBy(dt => dt.DailyOutputId)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(dt => dt.StyleDetailId)
                           .Select(sg => sg.Sum(x => x.OutputNumber ?? 0))
                           .ToList()
                );

            // DailyOutput map: Id → (ProductionInfoId, IsToday, TotalOutputNumber fallback)
            var dailyMeta = allDailyIds.ToDictionary(x => x.Id);

            // Effective per DailyOutput = min(detail sums) nếu có detail, else TotalOutputNumber
            int EffectiveOf(Guid dailyId)
            {
                if (detailsByDaily.TryGetValue((Guid?)dailyId, out var sums) && sums.Count > 0)
                    return sums.Min();
                return dailyMeta.TryGetValue(dailyId, out var m) ? (m.TotalOutputNumber ?? 0) : 0;
            }

            // Tổng effective toàn kỳ và hôm nay theo ProductionInfoId
            var totalByInfo = new Dictionary<Guid, int>();
            var todayByInfo = new Dictionary<Guid, int>();

            foreach (var d in allDailyIds)
            {
                var eff = EffectiveOf(d.Id);

                if (!totalByInfo.ContainsKey(d.ProductionInfoId)) totalByInfo[d.ProductionInfoId] = 0;
                totalByInfo[d.ProductionInfoId] += eff;

                if (d.IsToday)
                {
                    if (!todayByInfo.ContainsKey(d.ProductionInfoId)) todayByInfo[d.ProductionInfoId] = 0;
                    todayByInfo[d.ProductionInfoId] += eff;
                }
            }

            // Bước 5: lấy thông tin ProductionInfo và ghép
            var infos = await query
                .OrderByDescending(p => p.CreateDate)
                .Select(p => new
                {
                    p.Id, p.Style, p.Spmain, p.Line, p.Color,
                    p.TotalQty, p.FinishQty, p.Target,
                    p.InlineLine, p.InlineDepartment,
                    p.Remark, p.Keyword, p.Status, p.CreateDate
                })
                .ToListAsync();

            return infos.Select(p => new MachineDetail_ProductionInfoDto
            {
                Id                = p.Id,
                Style             = p.Style,
                Spmain            = p.Spmain,
                Line              = p.Line,
                Color             = p.Color,
                TotalQty          = p.TotalQty,
                FinishQty         = p.FinishQty,
                Target            = p.Target,
                InlineLine        = p.InlineLine?.ToString("yyyy-MM-dd"),
                InlineDepartment  = p.InlineDepartment?.ToString("yyyy-MM-dd"),
                Remark            = p.Remark,
                Keyword           = p.Keyword,
                Status            = p.Status,
                CreateDate        = p.CreateDate.ToString("yyyy-MM-dd"),
                TotalOutputNumber = totalByInfo.TryGetValue(p.Id, out var tot) ? tot : 0,
                TodayOutput       = todayByInfo.TryGetValue(p.Id, out var tod) ? tod : 0
            }).ToList();
        }


        // =====================================================================
        //  GET: DailyOutput + Details của một mã sản xuất trên một máy
        // =====================================================================

        /// <summary>
        /// Lấy danh sách DailyOutput (kèm DailyOutputDetail) của một ProductionInfo
        /// trên một máy, tùy chọn filter theo khoảng ngày.
        /// </summary>
        public async Task<List<MachineDetail_DailyOutputDto>> GetDailyOutputs(
            Guid productionInfoId, Guid machineId,
            DateTime? fromDate = null, DateTime? toDate = null)
        {
            var query = _context.DailyOutputs
                .AsNoTracking()
                .Where(d => d.ProductionInfoId    == productionInfoId
                         && d.ProductionMachineId == machineId
                         && d.Status == 1);

            if (fromDate.HasValue)
                query = query.Where(d => d.CreateDate.Date >= fromDate.Value.Date);
            if (toDate.HasValue)
                query = query.Where(d => d.CreateDate.Date <= toDate.Value.Date);

            var outputs = await query
                .OrderByDescending(d => d.CreateDate)
                .Select(d => new MachineDetail_DailyOutputDto
                {
                    Id                = d.Id,
                    StyleCode         = d.StyleInfo != null ? d.StyleInfo.StyleCode : null,
                    StyleInfoId       = d.StyleInfoId.HasValue ? d.StyleInfoId.Value.ToString() : null,
                    TotalOutputNumber = d.TotalOutputNumber,   // sẽ override bên dưới
                    CreateDate        = d.CreateDate.ToString("yyyy-MM-dd"),
                    Details           = d.DailyOutputDetails
                        .Where(dt => dt.Status == 1)
                        .OrderBy(dt => dt.InputTime)
                        .Select(dt => new MachineDetail_DailyOutputDetailDto
                        {
                            Id            = dt.Id,
                            StyleDetailId = dt.StyleDetailId,
                            DetailName    = dt.StyleDetail.DetailName,
                            OutputNumber  = dt.OutputNumber,
                            InputTime     = dt.InputTime.ToString("HH:mm")
                        }).ToList()
                })
                .ToListAsync();

            // Tính lại TotalOutputNumber = min(tổng sản lượng từng công đoạn) nếu có chi tiết
            // Cơ chế: Gom nhóm theo từng công đoạn (StyleDetailId / DetailName), tính tổng sản lượng mỗi công đoạn trong ngày,
            // sau đó lấy min của các công đoạn (1 thành phẩm hoàn chỉnh cần đủ tất cả các công đoạn)
            foreach (var o in outputs)
            {
                if (o.Details != null && o.Details.Count > 0)
                {
                    var sumsByDetail = o.Details
                        .Where(dt => dt.OutputNumber.HasValue)
                        .GroupBy(dt => dt.StyleDetailId.HasValue ? (object)dt.StyleDetailId.Value : (dt.DetailName ?? string.Empty))
                        .Select(g => g.Sum(dt => dt.OutputNumber.Value))
                        .ToList();

                    o.TotalOutputNumber = sumsByDetail.Count > 0 ? sumsByDetail.Min() : o.TotalOutputNumber;
                }
            }

            return outputs;
        }

        // =====================================================================
        //  GET: Danh sách StyleInfo để pick
        // =====================================================================

        /// <summary>Danh sách Style của bộ phận để chọn khi gán mã sản xuất.</summary>
        public async Task<List<MachineDetail_StylePickerItem>> GetStylesForPicker(
            Guid deptId, string keyword = null)
        {
            var query = _context.StyleInfos
                .AsNoTracking()
                .Where(s => s.ProductionDepartmentId == deptId && s.Status == 1);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim().ToLower();
                query = query.Where(s =>
                    s.StyleCode.ToLower().Contains(kw) ||
                    (s.Keyword != null && s.Keyword.ToLower().Contains(kw)));
            }

            return await query
                .OrderByDescending(s => s.CreateDate)
                .Select(s => new MachineDetail_StylePickerItem
                {
                    Id          = s.Id,
                    StyleCode   = s.StyleCode,
                    Keyword     = s.Keyword,
                    DetailCount = s.StyleDetails.Count(sd => sd.Status == 1)
                })
                .ToListAsync();
        }

        // =====================================================================
        //  GET: Danh sách ProductionInfo theo bộ phận (để pick khi thêm)
        // =====================================================================

        /// <summary>
        /// Danh sách tất cả mã sản xuất của bộ phận (chưa filter theo máy)
        /// để chọn khi gán mã có sẵn vào máy.
        /// </summary>
        public async Task<List<MachineDetail_ProductionInfoDto>> GetProductionInfosByDept(
            Guid deptId, string keyword = null)
        {
            var query = _context.ProductionInfos
                .AsNoTracking()
                .Where(p => p.ProductionDepartmentId == deptId && p.Status == 1);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim().ToLower();
                query = query.Where(p =>
                    p.Spmain.ToLower().Contains(kw) ||
                    p.Style.ToLower().Contains(kw) ||
                    (p.Keyword != null && p.Keyword.ToLower().Contains(kw)));
            }

            return await query
                .OrderByDescending(p => p.CreateDate)
                .Select(p => new MachineDetail_ProductionInfoDto
                {
                    Id               = p.Id,
                    Style            = p.Style,
                    Spmain           = p.Spmain,
                    Line             = p.Line,
                    Color            = p.Color,
                    TotalQty         = p.TotalQty,
                    FinishQty        = p.FinishQty,
                    Target           = p.Target,
                    InlineLine       = p.InlineLine.HasValue ? p.InlineLine.Value.ToString("yyyy-MM-dd") : null,
                    InlineDepartment = p.InlineDepartment.HasValue ? p.InlineDepartment.Value.ToString("yyyy-MM-dd") : null,
                    Remark           = p.Remark,
                    Keyword          = p.Keyword,
                    Status           = p.Status,
                    CreateDate       = p.CreateDate.ToString("yyyy-MM-dd"),
                    TotalOutputNumber = 0
                })
                .ToListAsync();
        }
        // =====================================================================
        //  POST: Gán mã sản xuất có sẵn vào máy
        //  Tạo một DailyOutput record để liên kết ProductionInfo ↔ Machine
        // =====================================================================

        /// <summary>
        /// Kiểm tra xem mã đã được gán vào máy chưa.
        /// Nếu chưa → tạo DailyOutput đầu tiên (trạng thái chờ, output = 0).
        /// </summary>
        public async Task<(bool Success, string Message)> AssignProductionInfoToMachine(
            Guid productionInfoId, Guid machineId)
        {
            // Kiểm tra đã tồn tại và đang hoạt động
            bool activeExists = await _context.DailyOutputs
                .AnyAsync(d => d.ProductionInfoId    == productionInfoId
                            && d.ProductionMachineId == machineId
                            && d.Status == 1);

            if (activeExists)
                return (false, "Mã sản xuất này đã được gán vào máy.");

            // Kiểm tra có bản ghi cũ đã tách khỏi máy (Status == -1) không
            var existing = await _context.DailyOutputs
                .FirstOrDefaultAsync(d => d.ProductionInfoId    == productionInfoId
                                       && d.ProductionMachineId == machineId
                                       && d.Status == -1);

            if (existing != null)
            {
                // Khôi phục bản ghi cũ
                existing.Status     = 1;
                existing.UpdateDate = DateTime.Now;
                await _context.SaveChangesAsync();
                return (true, "Đã khôi phục mã sản xuất vào máy thành công.");
            }

            // Kiểm tra ProductionInfo tồn tại
            var info = await _context.ProductionInfos.FindAsync(productionInfoId);
            if (info == null)
                return (false, "Không tìm thấy mã sản xuất.");

            // Tạo DailyOutput đầu tiên (placeholder để liên kết)
            var now = DateTime.Now;
            var daily = new Sub_Entities.Entities.DailyOutput
            {
                Id                  = Guid.NewGuid(),
                ProductionInfoId    = productionInfoId,
                ProductionMachineId = machineId,
                TotalOutputNumber   = 0,
                Status              = 1,
                CreateDate          = now,
                UpdateDate          = now
            };
            _context.DailyOutputs.Add(daily);
            await _context.SaveChangesAsync();

            return (true, "Đã gán mã sản xuất vào máy thành công.");
        }

        /// <summary>
        /// Tách nhiều mã sản xuất khỏi máy (chuyển Status của DailyOutput link → -1).
        /// Giữ lại lịch sử sản lượng đã ghi.
        /// </summary>
        public async Task<(bool Success, string Message)> RemoveInfosFromMachine(
            List<Guid> productionInfoIds, Guid machineId)
        {
            if (productionInfoIds == null || !productionInfoIds.Any())
                return (false, "Không có mã nào được chọn.");

            var links = await _context.DailyOutputs
                .Where(d => productionInfoIds.Contains(d.ProductionInfoId)
                         && d.ProductionMachineId == machineId
                         && d.Status == 1)
                .ToListAsync();

            if (!links.Any())
                return (false, "Không tìm thấy liên kết hợp lệ để tách.");

            var now = DateTime.Now;
            foreach (var link in links)
            {
                link.Status     = -1;
                link.UpdateDate = now;
            }

            await _context.SaveChangesAsync();
            return (true, $"Đã tách {links.Count} mã sản xuất khỏi máy.");
        }

        // =====================================================================
        //  POST: Tạo mã sản xuất mới và gán vào máy
        // =====================================================================

        public class CreateProductionInfoRequest
        {
            public string Spmain         { get; set; }
            public string Style          { get; set; }
            public string Line           { get; set; }
            public string Color          { get; set; }
            public int?   Target         { get; set; }
            public int?   TotalQty       { get; set; }
            public string InlineLine     { get; set; }   // "yyyy-MM-dd" or null
            public string InlineDept     { get; set; }
            public string Remark         { get; set; }
            public Guid   MachineId      { get; set; }
            public Guid   DeptId         { get; set; }
        }

        /// <summary>
        /// Tạo mới ProductionInfo rồi gán vào máy (tạo DailyOutput đầu tiên).
        /// </summary>
        public async Task<(bool Success, string Message, Guid? InfoId)> CreateAndAssignProductionInfo(
            CreateProductionInfoRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Spmain) || string.IsNullOrWhiteSpace(req.Style))
                return (false, "Mã SP và Tên Style không được để trống.", null);

            var now = DateTime.Now;

            // Tạo ProductionInfo
            var info = new Sub_Entities.Entities.ProductionInfo
            {
                Id                    = Guid.NewGuid(),
                ProductionDepartmentId = req.DeptId,
                Spmain                = req.Spmain.Trim(),
                Style                 = req.Style.Trim(),
                Line                  = req.Line?.Trim(),
                Color                 = req.Color?.Trim(),
                Target                = req.Target,
                TotalQty              = req.TotalQty,
                InlineLine            = string.IsNullOrWhiteSpace(req.InlineLine)
                                        ? null
                                        : (DateOnly?)DateOnly.Parse(req.InlineLine),
                InlineDepartment      = string.IsNullOrWhiteSpace(req.InlineDept)
                                        ? null
                                        : (DateOnly?)DateOnly.Parse(req.InlineDept),
                Remark                = req.Remark?.Trim(),
                Status                = 1,
                CreateDate            = now,
                UpdateDate            = now
            };
            _context.ProductionInfos.Add(info);

            // Tạo DailyOutput đầu tiên để liên kết với máy
            var daily = new Sub_Entities.Entities.DailyOutput
            {
                Id                  = Guid.NewGuid(),
                ProductionInfoId    = info.Id,
                ProductionMachineId = req.MachineId,
                TotalOutputNumber   = 0,
                Status              = 1,
                CreateDate          = now,
                UpdateDate          = now
            };
            _context.DailyOutputs.Add(daily);

            await _context.SaveChangesAsync();
            return (true, "Đã tạo và gán mã sản xuất thành công.", info.Id);
        }

        // =====================================================================
        //  RecordOutput: Ghi sản lượng
        // =====================================================================

        /// <summary>
        /// Danh sách máy theo bộ phận (hoặc tất cả) để chọn khi ghi sản lượng.
        /// </summary>
        public async Task<List<RecordOutput_MachineDto>> GetMachinesForRecording(
            Guid? deptId = null, string keyword = null, int? machineStatus = null)
        {
            var query = _context.ProductionMachines
                .AsNoTracking()
                .Where(m => m.Status == 1                                      // máy đang hoạt động
                         && m.ProductLayoutItems.Any(li => li.Status == 1));   // còn trong layout

            if (machineStatus.HasValue)
                query = query.Where(m => m.Status == machineStatus.Value);

            if (deptId.HasValue && deptId.Value != Guid.Empty)
                query = query.Where(m => m.ProductionDepartmentId == deptId.Value);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim().ToLower();
                query = query.Where(m =>
                    m.MachineNumber.ToLower().Contains(kw) ||
                    (m.Remark != null && m.Remark.ToLower().Contains(kw)));
            }

            return await query
                .OrderBy(m => m.ProductionDepartment.DepartmentName)
                .ThenBy(m => m.MachineNumber)
                .Select(m => new RecordOutput_MachineDto
                {
                    Id          = m.Id,
                    MachineName = m.MachineNumber,
                    MachineCode = m.MachineNumber,
                    Remark      = m.Remark,
                    Status      = m.Status ?? 1,
                    DeptName    = m.ProductionDepartment.DepartmentName,
                    DeptId      = m.ProductionDepartmentId
                })
                .ToListAsync();
        }

        /// <summary>
        /// Danh sách mã sản xuất đang gắn vào máy, kèm sản lượng hôm nay.
        /// </summary>
        public async Task<List<RecordOutput_ProductionCodeDto>> GetProductionCodesForMachine(
            Guid machineId, string keyword = null, int? statusFilter = null)
        {
            var today = DateTime.Today;

            // Tất cả ProductionInfo gắn với máy này
            var infoIds = await _context.DailyOutputs
                .Where(d => d.ProductionMachineId == machineId && d.Status == 1)
                .Select(d => d.ProductionInfoId)
                .Distinct()
                .ToListAsync();

            var query = _context.ProductionInfos
                .AsNoTracking()
                .Where(p => infoIds.Contains(p.Id) && p.Status >= -1); // loại trừ đã xóa (-2)

            if (statusFilter.HasValue)
                query = query.Where(p => p.Status == statusFilter.Value);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim().ToLower();
                query = query.Where(p =>
                    p.Spmain.ToLower().Contains(kw) ||
                    p.Style.ToLower().Contains(kw)  ||
                    (p.Color != null && p.Color.ToLower().Contains(kw)) ||
                    (p.Line  != null && p.Line.ToLower().Contains(kw)));
            }

            var infos = await query
                .Select(p => new { p.Id, p.Style, p.Spmain, p.Color, p.Line, p.Status, p.Target })
                .ToListAsync();

            // Bước 1: lấy DailyOutput hôm nay của MÁY NÀY cho các mã đang may
            var todayDailyIds = await _context.DailyOutputs
                .Where(d => d.ProductionMachineId == machineId
                         && d.Status == 1
                         && d.CreateDate.Date == today
                         && infoIds.Contains(d.ProductionInfoId))
                .Select(d => new { d.Id, d.ProductionInfoId })
                .ToListAsync();

            // Bước 2: lấy tất cả details tương ứng rồi tính client-side
            var todayDailyIdList = todayDailyIds.Select(x => x.Id).ToList();

            var detailRows = await _context.DailyOutputDetails
                .Where(dt => todayDailyIdList.Contains(dt.DailyOutputId ?? Guid.Empty)
                          && dt.Status == 1)
                .Select(dt => new { dt.DailyOutputId, dt.StyleDetailId, dt.OutputNumber })
                .ToListAsync();

            // Bước 3: tính effective per DailyOutput (min tổng từng StyleDetail)
            //         rồi nhóm theo ProductionInfoId để cộng tổng TẤT CẢ máy
            var dailyInfoMap = todayDailyIds.ToDictionary(x => x.Id, x => x.ProductionInfoId);

            // Sum output per (dailyOutputId, styleDetailId)
            var perDetailSum = detailRows
                .GroupBy(dt => new { dt.DailyOutputId, dt.StyleDetailId })
                .Select(g => new { g.Key.DailyOutputId, Total = g.Sum(x => x.OutputNumber ?? 0) });

            // Min per dailyOutput = effective = số bộ hoàn chỉnh (1 máy)
            var effectivePerDaily = perDetailSum
                .GroupBy(x => x.DailyOutputId)
                .Select(g => new { DailyOutputId = g.Key, Effective = g.Min(x => x.Total) });

            // Sum effective per productionInfoId (cộng tất cả máy)
            var todayMap = effectivePerDaily
                .GroupBy(x => dailyInfoMap.TryGetValue(x.DailyOutputId ?? Guid.Empty, out var pid) ? pid : Guid.Empty)
                .Where(g => g.Key != Guid.Empty)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Effective));

            return infos.Select(p => new RecordOutput_ProductionCodeDto
            {
                ProductionInfoId = p.Id,
                MachineId        = machineId,
                Style            = p.Style,
                Spmain           = p.Spmain,
                Color            = p.Color,
                Line             = p.Line,
                Status           = p.Status,
                Target           = p.Target,
                TodayOutput      = todayMap.TryGetValue(p.Id, out var v) ? v : 0
            }).ToList();
        }

        /// <summary>
        /// Chi tiết công đoạn (StyleDetail) của một ProductionInfo,
        /// kèm tổng đã nhập hôm nay (hoặc ngày được chỉ định) để hiển thị khi ghi sản lượng.
        /// Luôn lọc StyleInfo theo ProductionDepartmentId — vì mỗi bộ phận có công đoạn riêng
        /// dù cùng một mã hàng.
        /// </summary>
        public async Task<List<RecordOutput_StyleDetailDto>> GetStyleDetailsForRecording(
            Guid productionInfoId, Guid machineId, DateOnly? date = null, Guid? departmentId = null)
        {
            var targetDate = date ?? DateOnly.FromDateTime(DateTime.Today);
            var targetDt   = targetDate.ToDateTime(TimeOnly.MinValue);

            // Kiểm tra ProductionInfo có Status == 1 không
            var infoOk = await _context.ProductionInfos
                .AsNoTracking()
                .AnyAsync(p => p.Id == productionInfoId && p.Status == 1);

            if (!infoOk)
            {
                return new List<RecordOutput_StyleDetailDto>
                {
                    new RecordOutput_StyleDetailDto
                    {
                        StyleDetailId = Guid.Empty,
                        DetailName    = "⚠ Mã hàng không ở trạng thái hoạt động",
                        TodayTotal    = 0
                    }
                };
            }

            // Xác định bộ phận cần lọc:
            // Ưu tiên departmentId được truyền vào, nếu không thì lấy từ máy
            Guid effectiveDeptId;
            if (departmentId.HasValue && departmentId != Guid.Empty)
            {
                effectiveDeptId = departmentId.Value;
            }
            else
            {
                // Lấy ProductionDepartmentId của máy hiện tại
                effectiveDeptId = await _context.ProductionMachines
                    .Where(m => m.Id == machineId)
                    .Select(m => m.ProductionDepartmentId)
                    .FirstOrDefaultAsync();  // Guid.Empty nếu không tìm thấy (default của Guid)
            }

            // Danh sách máy thuộc bộ phận (để lọc DailyOutput)
            var validMachineIds = _context.ProductionMachines
                .Where(m => m.ProductionDepartmentId == effectiveDeptId && (m.Status == null || m.Status == 1))
                .Select(m => m.Id);

            // -------------------------------------------------------
            // Tìm StyleInfoId — thử theo thứ tự ưu tiên:
            // Tất cả đều phải filter theo effectiveDeptId vì StyleInfo
            // có ProductionDepartmentId riêng cho từng bộ phận.
            // -------------------------------------------------------
            Guid? styleInfoId = null;

            // 1. Qua DailyOutput.StyleInfoId (bộ phận hiện tại, đang active)
            var styleInfoIdFromDaily = await _context.DailyOutputs
                .Where(d => d.ProductionInfoId == productionInfoId
                         && d.StyleInfoId != null
                         && d.Status == 1
                         && validMachineIds.Contains(d.ProductionMachineId))
                .Select(d => d.StyleInfoId)
                .FirstOrDefaultAsync();

            if (styleInfoIdFromDaily.HasValue)
            {
                // Kiểm tra StyleInfo này có đúng bộ phận không
                var siDeptOk = await _context.StyleInfos
                    .AnyAsync(s => s.Id == styleInfoIdFromDaily.Value
                               && s.ProductionDepartmentId == effectiveDeptId
                               && s.Status == 1);
                if (siDeptOk) styleInfoId = styleInfoIdFromDaily;
            }

            if (styleInfoId == null)
            {
                // 2. Qua ProductionInfo.Keyword / Style → StyleInfo của đúng bộ phận
                var info = await _context.ProductionInfos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == productionInfoId);

                if (info != null)
                {
                    // 2a. Thử Keyword trước
                    if (!string.IsNullOrEmpty(info.Keyword))
                    {
                        var si = await _context.StyleInfos
                            .AsNoTracking()
                            .FirstOrDefaultAsync(s => s.StyleCode == info.Keyword
                                                   && s.ProductionDepartmentId == effectiveDeptId
                                                   && s.Status == 1);
                        styleInfoId = si?.Id;
                    }

                    // 2b. Fallback: tìm theo Style name
                    if (styleInfoId == null && !string.IsNullOrEmpty(info.Style))
                    {
                        var si = await _context.StyleInfos
                            .AsNoTracking()
                            .FirstOrDefaultAsync(s =>
                                (s.StyleCode == info.Style || s.Keyword == info.Style)
                                && s.ProductionDepartmentId == effectiveDeptId
                                && s.Status == 1);
                        styleInfoId = si?.Id;
                    }

                    // 2c. Fallback: qua DailyOutputDetail đã từng lưu (đúng bộ phận)
                    if (styleInfoId == null)
                    {
                        var existingDetailId = await _context.DailyOutputDetails
                            .Where(dt => dt.DailyOutput.ProductionInfoId == productionInfoId
                                      && validMachineIds.Contains(dt.DailyOutput.ProductionMachineId)
                                      && dt.Status == 1
                                      && dt.StyleDetailId != null)
                            .Select(dt => (Guid?)dt.StyleDetailId)
                            .FirstOrDefaultAsync();

                        if (existingDetailId.HasValue)
                        {
                            styleInfoId = await _context.StyleDetails
                                .Where(sd => sd.Id == existingDetailId.Value
                                          && sd.StyleInfo.ProductionDepartmentId == effectiveDeptId)
                                .Select(sd => (Guid?)sd.StyleInfoId)
                                .FirstOrDefaultAsync();
                        }
                    }
                }
            }

            if (styleInfoId == null)
            {
                return new List<RecordOutput_StyleDetailDto>
                {
                    new RecordOutput_StyleDetailDto
                    {
                        StyleDetailId = Guid.Empty,
                        DetailName    = "⚠ Không tìm được công đoạn (chưa gắn style cho bộ phận này)",
                        TodayTotal    = 0
                    }
                };
            }

            // -------------------------------------------------------
            // Lấy danh sách StyleDetail của StyleInfo đúng bộ phận
            // -------------------------------------------------------
            var details = await _context.StyleDetails
                .AsNoTracking()
                .Where(sd => sd.StyleInfoId == styleInfoId && sd.Status == 1)
                .Select(sd => new { sd.Id, sd.DetailName })
                .OrderBy(sd => sd.DetailName)
                .ToListAsync();

            if (!details.Any())
            {
                return new List<RecordOutput_StyleDetailDto>
                {
                    new RecordOutput_StyleDetailDto
                    {
                        StyleDetailId = Guid.Empty,
                        DetailName    = "⚠ Style chưa có công đoạn nào (bộ phận: " + effectiveDeptId + ")",
                        TodayTotal    = 0
                    }
                };
            }

            // -------------------------------------------------------
            // Tổng đã nhập hôm nay của MÁY HIỆN TẠI
            // -------------------------------------------------------
            var todayDetailIds = details.Select(d => d.Id).ToList();

            var todayDailyOutputIds = await _context.DailyOutputs
                .Where(d => d.ProductionInfoId    == productionInfoId
                         && d.ProductionMachineId == machineId
                         && d.Status == 1
                         && d.CreateDate.Date == targetDt.Date)
                .Select(d => d.Id)
                .ToListAsync();

            List<(Guid? StyleDetailId, int Total)> todayTotals;
            if (todayDailyOutputIds.Any())
            {
                todayTotals = await _context.DailyOutputDetails
                    .Where(dt => todayDailyOutputIds.Contains(dt.DailyOutputId ?? Guid.Empty)
                              && todayDetailIds.Contains(dt.StyleDetailId)
                              && dt.Status == 1)
                    .GroupBy(dt => dt.StyleDetailId)
                    .Select(g => new { StyleDetailId = g.Key, Total = g.Sum(x => x.OutputNumber ?? 0) })
                    .ToListAsync()
                    .ContinueWith(t => t.Result.Select(x => ((Guid?)x.StyleDetailId, x.Total)).ToList());
            }
            else
            {
                todayTotals = new List<(Guid?, int)>();
            }

            var totalMap = todayTotals.ToDictionary(x => x.StyleDetailId, x => x.Total);

            return details.Select(sd => new RecordOutput_StyleDetailDto
            {
                StyleDetailId = sd.Id,
                DetailName    = sd.DetailName,
                TodayTotal    = totalMap.TryGetValue((Guid?)sd.Id, out var t) ? t : 0
            }).ToList();
        }

        /// <summary>
        /// Ghi sản lượng: tạo/cập nhật DailyOutput và cộng dồn DailyOutputDetail theo ngày.
        /// </summary>
        public async Task<(bool Success, string Message)> SaveOutputRecord(
            RecordOutput_SaveRequest req)
        {
            if (req.Details == null || !req.Details.Any())
                return (false, "Không có chi tiết nào để ghi.");

            if (!DateOnly.TryParse(req.Date, out var date))
                return (false, "Ngày không hợp lệ.");

            if (!TimeOnly.TryParse(req.Time, out var time))
                time = TimeOnly.FromDateTime(DateTime.Now);

            var targetDt = date.ToDateTime(TimeOnly.MinValue);
            var now      = DateTime.Now;

            // Tìm hoặc tạo DailyOutput cho (productionInfoId, machineId, date)
            var daily = await _context.DailyOutputs
                .Include(d => d.DailyOutputDetails)
                .FirstOrDefaultAsync(d =>
                    d.ProductionInfoId    == req.ProductionInfoId
                    && d.ProductionMachineId == req.MachineId
                    && d.Status == 1
                    && d.CreateDate.Date == targetDt.Date);

            if (daily == null)
            {
                daily = new Sub_Entities.Entities.DailyOutput
                {
                    Id                  = Guid.NewGuid(),
                    ProductionInfoId    = req.ProductionInfoId,
                    ProductionMachineId = req.MachineId,
                    TotalOutputNumber   = 0,
                    Status              = 1,
                    CreateDate          = targetDt.Date + now.TimeOfDay,
                    UpdateDate          = now
                };
                _context.DailyOutputs.Add(daily);
                await _context.SaveChangesAsync(); // cần Id trước khi thêm details
            }

            // Cộng dồn từng chi tiết
            foreach (var entry in req.Details)
            {
                if (entry.OutputNumber <= 0) continue;

                // Mỗi lần nhập = 1 DailyOutputDetail mới (accumulate)
                var detail = new Sub_Entities.Entities.DailyOutputDetail
                {
                    Id            = Guid.NewGuid(),
                    DailyOutputId = daily.Id,
                    StyleDetailId = entry.StyleDetailId,
                    OutputNumber  = entry.OutputNumber,
                    InputTime     = time,
                    Keyword       = req.Keyword,
                    Status        = 1,
                    CreateDate    = now,
                    UpdateDate    = now
                };
                _context.DailyOutputDetails.Add(detail);
            }

            // Cập nhật TotalOutputNumber trên DailyOutput (min của tổng details)
            await _context.SaveChangesAsync();

            // Tính lại tổng (min logic)
            var detailTotals = await _context.DailyOutputDetails
                .Where(dt => dt.DailyOutputId == daily.Id && dt.Status == 1)
                .GroupBy(dt => dt.StyleDetailId)
                .Select(g => g.Sum(x => x.OutputNumber ?? 0))
                .ToListAsync();

            if (detailTotals.Any())
            {
                daily.TotalOutputNumber = detailTotals.Min();
                daily.UpdateDate        = now;
                await _context.SaveChangesAsync();
            }

            return (true, "Đã ghi sản lượng thành công.");
        }

        // =====================================================================
        //  OUTPUT HISTORY: Lịch sử ghi sản lượng
        // =====================================================================

        public class OutputHistoryDto
        {
            public Guid   Id           { get; set; }
            public string CreateDate   { get; set; }   // yyyy-MM-dd
            public string InputTime    { get; set; }   // HH:mm
            public string MachineName  { get; set; }
            public string DeptName     { get; set; }
            public string Line         { get; set; }
            public string Spmain       { get; set; }
            public string Style        { get; set; }
            public string DetailName   { get; set; }
            public int    OutputNumber { get; set; }
            public string Source       { get; set; }   // "manual" | "excel"
        }

        /// <summary>
        /// Lấy lịch sử ghi sản lượng (DailyOutputDetail) theo khoảng ngày và bộ phận.
        /// </summary>
        public async Task<List<OutputHistoryDto>> GetOutputHistory(
            DateOnly? from, DateOnly? to, Guid? deptId)
        {
            var fromDt = from.HasValue ? from.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Today;
            var toDt   = to.HasValue   ? to.Value.ToDateTime(TimeOnly.MaxValue)   : DateTime.Today.AddDays(1).AddTicks(-1);

            // Dùng projection thay vì Include/ThenInclude để tránh N+1 và lock EF tracking
            var query = _context.DailyOutputDetails
                .AsNoTracking()
                .Where(d => d.Status == 1
                         && d.DailyOutputId.HasValue
                         && d.DailyOutput.Status == 1
                         && d.DailyOutput.CreateDate >= fromDt
                         && d.DailyOutput.CreateDate <= toDt);

            if (deptId.HasValue && deptId.Value != Guid.Empty)
                query = query.Where(d =>
                    d.DailyOutput.ProductionMachine.ProductionDepartmentId == deptId.Value);

            var list = await query
                .OrderByDescending(d => d.DailyOutput.CreateDate)
                .ThenByDescending(d => d.InputTime)
                .Take(3000)          // giới hạn để tránh block khi dữ liệu lớn
                .Select(d => new OutputHistoryDto
                {
                    Id           = d.Id,
                    CreateDate   = d.DailyOutput.CreateDate.ToString("yyyy-MM-dd"),
                    InputTime    = d.InputTime.ToString("HH:mm"),
                    MachineName  = string.IsNullOrEmpty(d.DailyOutput.ProductionMachine.Remark)
                                   ? d.DailyOutput.ProductionMachine.MachineNumber
                                   : d.DailyOutput.ProductionMachine.Remark,
                    DeptName     = d.DailyOutput.ProductionMachine.ProductionDepartment.DepartmentName,
                    Line         = d.DailyOutput.ProductionInfo.Line,
                    Spmain       = d.DailyOutput.ProductionInfo.Spmain,
                    Style        = d.DailyOutput.ProductionInfo.Style,
                    DetailName   = d.StyleDetail.DetailName,
                    OutputNumber = d.OutputNumber ?? 0,
                    Source       = string.IsNullOrEmpty(d.Keyword) ? "manual" : d.Keyword
                })
                .ToListAsync();

            return list;
        }


        public class UpdateOutputRecord_Request
        {
            public Guid   Id           { get; set; }
            public int    OutputNumber { get; set; }
            public string Date         { get; set; }
            public string Time         { get; set; }
        }

        /// <summary>Cập nhật sản lượng, ngày, giờ của một DailyOutputDetail.</summary>
        public async Task<(bool Success, string Message)> UpdateOutputRecord(
            UpdateOutputRecord_Request req)
        {
            var detail = await _context.DailyOutputDetails
                .Include(d => d.DailyOutput)
                .FirstOrDefaultAsync(d => d.Id == req.Id && d.Status == 1);

            if (detail == null) return (false, "Không tìm thấy bản ghi.");
            if (req.OutputNumber < 0) return (false, "Sản lượng không hợp lệ.");

            detail.OutputNumber = req.OutputNumber;
            detail.UpdateDate   = DateTime.Now;

            if (TimeOnly.TryParse(req.Time, out var t))
                detail.InputTime = t;

            if (DateOnly.TryParse(req.Date, out var d) && detail.DailyOutput != null)
            {
                var oldDate = DateOnly.FromDateTime(detail.DailyOutput.CreateDate);
                if (d != oldDate)
                {
                    // Đổi ngày → cập nhật CreateDate của DailyOutput (giữ giờ cũ)
                    detail.DailyOutput.CreateDate = d.ToDateTime(TimeOnly.FromDateTime(detail.DailyOutput.CreateDate));
                    detail.DailyOutput.UpdateDate  = DateTime.Now;
                }
            }

            await _context.SaveChangesAsync();
            return (true, "Đã cập nhật.");
        }

        /// <summary>Xóa mềm một DailyOutputDetail (Status = -2). Bản ghi không còn xuất hiện trong GetOutputHistory (chỉ lấy Status == 1).</summary>
        public async Task<(bool Success, string Message)> DeleteOutputRecord(Guid id)
        {
            var detail = await _context.DailyOutputDetails
                .FirstOrDefaultAsync(d => d.Id == id && d.Status == 1);

            if (detail == null) return (false, "Không tìm thấy bản ghi.");

            detail.Status     = -2;
            detail.UpdateDate = DateTime.Now;
            await _context.SaveChangesAsync();
            return (true, "Đã xóa.");
        }


        // =====================================================================

        public class DepartmentListItem
        {
            public Guid   Id             { get; set; }
            public string DepartmentName { get; set; }
            public string Keyword        { get; set; }
            public int    Status         { get; set; }
            public int    ActiveCodes    { get; set; }  // Số mã hàng status == 1
            public int    TotalCodes     { get; set; }  // Số mã hàng status >= -1
            public DateTime CreateDate   { get; set; }
            public DateTime UpdateDate   { get; set; }
        }

        public class DepartmentUpsert_Request
        {
            public Guid?  Id             { get; set; }
            public string DepartmentName { get; set; }
            public string Keyword        { get; set; }
        }

        /// <summary>Lấy toàn bộ bộ phận (status >= -1 = bao gồm tạm khoá, trừ bị xóa), kèm count mã hàng.</summary>
        public async Task<List<DepartmentListItem>> GetDepartmentListWithCount()
        {
            var depts = await _context.ProductionDepartments
                .AsNoTracking()
                .Where(d => d.Status >= -1)          // >= -1: bao gồm tạm khoá (-1), không lấy xóa (-2)
                .OrderBy(d => d.DepartmentName)
                .ToListAsync();

            // Count mã hàng theo từng bộ phận
            var stats = await _context.ProductionInfos
                .Where(p => p.Status >= -1)
                .GroupBy(p => p.ProductionDepartmentId)
                .Select(g => new { 
                    DeptId = g.Key, 
                    Total = g.Count(p => p.Status >= 0),
                    Active = g.Count(p => p.Status == 1) 
                })
                .ToDictionaryAsync(x => x.DeptId, x => x);

            return depts.Select(d => new DepartmentListItem
            {
                Id             = d.Id,
                DepartmentName = d.DepartmentName,
                Keyword        = d.Keyword,
                Status         = d.Status,
                ActiveCodes    = stats.TryGetValue(d.Id, out var s) ? s.Active : 0,
                TotalCodes     = stats.TryGetValue(d.Id, out var st) ? st.Total : 0,
                CreateDate     = d.CreateDate,
                UpdateDate     = d.UpdateDate,
            }).ToList();
        }

        /// <summary>Tạo mới bộ phận.</summary>
        public async Task<(bool Success, string Message, Guid? Id)> CreateDepartment(
            DepartmentUpsert_Request req)
        {
            if (string.IsNullOrWhiteSpace(req.DepartmentName))
                return (false, "Tên bộ phận không được để trống.", null);

            var now = DateTime.Now;
            var dept = new Sub_Entities.Entities.ProductionDepartment
            {
                Id             = Guid.NewGuid(),
                DepartmentName = req.DepartmentName.Trim(),
                Keyword        = string.IsNullOrWhiteSpace(req.Keyword)
                    ? req.DepartmentName.Trim().ToUpperInvariant().Replace(" ", "")
                    : req.Keyword.Trim(),
                Status         = 1,
                CreateDate     = now,
                UpdateDate     = now,
            };
            _context.ProductionDepartments.Add(dept);
            await _context.SaveChangesAsync();
            return (true, "Tạo bộ phận thành công.", dept.Id);
        }

        /// <summary>Cập nhật tên / keyword bộ phận. Chỉ cập nhật nếu status >= -1 (kể cả tạm khoá).</summary>
        public async Task<(bool Success, string Message)> UpdateDepartment(
            DepartmentUpsert_Request req)
        {
            if (!req.Id.HasValue || req.Id == Guid.Empty)
                return (false, "Id không hợp lệ.");
            if (string.IsNullOrWhiteSpace(req.DepartmentName))
                return (false, "Tên bộ phận không được để trống.");

            var dept = await _context.ProductionDepartments
                .FirstOrDefaultAsync(d => d.Id == req.Id.Value && d.Status >= -1);
            if (dept == null) return (false, "Không tìm thấy bộ phận.");

            dept.DepartmentName = req.DepartmentName.Trim();
            dept.Keyword        = string.IsNullOrWhiteSpace(req.Keyword)
                ? req.DepartmentName.Trim().ToUpperInvariant().Replace(" ", "")
                : req.Keyword.Trim();
            dept.UpdateDate = DateTime.Now;
            await _context.SaveChangesAsync();
            return (true, "Cập nhật bộ phận thành công.");
        }

        /// <summary>Tạm khoá bộ phận: Status = -1 (vẫn hiển thị trong danh sách).</summary>
        public async Task<(bool Success, string Message)> SuspendDepartment(Guid id)
        {
            var dept = await _context.ProductionDepartments
                .FirstOrDefaultAsync(d => d.Id == id && d.Status >= -1);
            if (dept == null) return (false, "Không tìm thấy bộ phận.");
            dept.Status     = -1;   // -1 = tạm khoá
            dept.UpdateDate = DateTime.Now;
            await _context.SaveChangesAsync();
            return (true, "Đã tạm khoá bộ phận.");
        }

        /// <summary>Mở khoá bộ phận: Status = 1 (hoạt động trở lại).</summary>
        public async Task<(bool Success, string Message)> UnlockDepartment(Guid id)
        {
            var dept = await _context.ProductionDepartments
                .FirstOrDefaultAsync(d => d.Id == id && d.Status == -1);
            if (dept == null) return (false, "Không tìm thấy bộ phận đang tạm khoá.");
            dept.Status     = 1;    // 1 = hoạt động
            dept.UpdateDate = DateTime.Now;
            await _context.SaveChangesAsync();
            return (true, "Đã mở khoá bộ phận thành công.");
        }

        /// <summary>Xoá mềm bộ phận: Status = -2 (hoàn toàn ẩn khỏi hệ thống).</summary>
        public async Task<(bool Success, string Message)> SoftDeleteDepartment(Guid id)
        {
            var dept = await _context.ProductionDepartments
                .FirstOrDefaultAsync(d => d.Id == id && d.Status >= -1);
            if (dept == null) return (false, "Không tìm thấy bộ phận.");
            dept.Status     = -2;   // -2 = xóa mềm
            dept.UpdateDate = DateTime.Now;
            await _context.SaveChangesAsync();
            return (true, "Đã xoá bộ phận thành công.");
        }
    }
}
