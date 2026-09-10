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
            // Kiểm tra đã tồn tại chưa (tránh duplicate)
            bool exists = await _context.DailyOutputs
                .AnyAsync(d => d.ProductionInfoId    == productionInfoId
                            && d.ProductionMachineId == machineId
                            && d.Status == 1);

            if (exists)
                return (false, "Mã sản xuất này đã được gán vào máy.");

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
            Guid? deptId = null, string keyword = null)
        {
            var query = _context.ProductionMachines
                .AsNoTracking()
                .Where(m => m.Status == 1);

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
                    Remark      = m.Remark,
                    Status      = m.Status ?? 1,
                    DeptName    = m.ProductionDepartment.DepartmentName
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
                .Where(p => infoIds.Contains(p.Id));

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
        /// </summary>
        public async Task<List<RecordOutput_StyleDetailDto>> GetStyleDetailsForRecording(
            Guid productionInfoId, Guid machineId, DateOnly? date = null)
        {
            var targetDate = date ?? DateOnly.FromDateTime(DateTime.Today);
            var targetDt   = targetDate.ToDateTime(TimeOnly.MinValue);

            // Tìm StyleInfoId — thử nhiều cách:
            // 1. Qua DailyOutput.StyleInfoId (bất kỳ máy nào đang may mã này)
            Guid? styleInfoId = await _context.DailyOutputs
                .Where(d => d.ProductionInfoId == productionInfoId
                         && d.StyleInfoId != null
                         && d.Status == 1)
                .Select(d => d.StyleInfoId)
                .FirstOrDefaultAsync();

            if (styleInfoId == null)
            {
                // 2. Qua ProductionInfo.Keyword → StyleInfo.StyleCode
                var info = await _context.ProductionInfos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == productionInfoId);

                if (info != null)
                {
                    // Thử Keyword trước
                    if (!string.IsNullOrEmpty(info.Keyword))
                    {
                        var si = await _context.StyleInfos
                            .AsNoTracking()
                            .FirstOrDefaultAsync(s => s.StyleCode == info.Keyword && s.Status == 1);
                        styleInfoId = si?.Id;
                    }

                    // Fallback: tìm theo Style name khớp với StyleCode hoặc Keyword
                    if (styleInfoId == null && !string.IsNullOrEmpty(info.Style))
                    {
                        var si = await _context.StyleInfos
                            .AsNoTracking()
                            .FirstOrDefaultAsync(s =>
                                (s.StyleCode == info.Style || s.Keyword == info.Style)
                                && s.Status == 1);
                        styleInfoId = si?.Id;
                    }

                    // Fallback 2: tìm StyleDetail trực tiếp qua DailyOutputDetail đã từng lưu
                    if (styleInfoId == null)
                    {
                        var existingDetailId = await _context.DailyOutputDetails
                            .Where(dt => dt.DailyOutput.ProductionInfoId == productionInfoId
                                      && dt.Status == 1
                                      && dt.StyleDetailId != null)
                            .Select(dt => (Guid?)dt.StyleDetailId)
                            .FirstOrDefaultAsync();

                        if (existingDetailId.HasValue)
                        {
                            styleInfoId = await _context.StyleDetails
                                .Where(sd => sd.Id == existingDetailId.Value)
                                .Select(sd => (Guid?)sd.StyleInfoId)
                                .FirstOrDefaultAsync();
                        }
                    }
                }
            }

            if (styleInfoId == null)
            {
                // Không tìm được style — trả về placeholder để user biết
                return new List<RecordOutput_StyleDetailDto>
                {
                    new RecordOutput_StyleDetailDto
                    {
                        StyleDetailId = Guid.Empty,
                        DetailName    = "⚠ Không tìm được công đoạn (chưa gắn style)",
                        TodayTotal    = 0
                    }
                };
            }

            // Lấy danh sách StyleDetail
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
                        DetailName    = "⚠ Style chưa có công đoạn nào",
                        TodayTotal    = 0
                    }
                };
            }

            // Tổng đã nhập hôm nay per detail:
            // Lấy TẤT CẢ máy đang may mã này (không chỉ machineId)
            // để số liệu nhất quán với TodayOutput ở card
            var todayDetailIds = details.Select(d => d.Id).ToList();

            // Tổng của MÁY HIỆN TẠI (machineId) để hiển thị trong modal
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
    }
}
