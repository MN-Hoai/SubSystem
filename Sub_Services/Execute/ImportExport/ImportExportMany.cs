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
        //  ImportExport — Xuất nhập file Excel
        // =====================================================================

        /// <summary>
        /// Tạo file Excel mẫu để nhập sản lượng.
        /// Sheet 1: DailyOutput — dữ liệu nhập
        /// Sheet 2: Hướng dẫn
        /// </summary>
        public byte[] GenerateOutputImportTemplate()
        {
            using var wb = new XLWorkbook();

            // ── Sheet 1: Dữ liệu nhập ──────────────────────────────────────
            var ws = wb.AddWorksheet("NhapSanLuong");
            ws.SheetView.FreezeRows(2);

            // Total columns: 10
            // Col: Ngày | Giờ | Tên máy | Line | Style | Mã SP | Order Qty | Màu | Công đoạn | Số lượng
            const int TOTAL_COLS = 10;

            // Title row
            var titleRow = ws.Range(1, 1, 1, TOTAL_COLS);
            titleRow.Merge();
            titleRow.Value = "NHẬP SẢN LƯỢNG HÀNG NGÀY";
            titleRow.Style.Font.Bold = true;
            titleRow.Style.Font.FontSize = 14;
            titleRow.Style.Font.FontColor = XLColor.FromArgb(0x1F, 0x3A, 0x67);
            titleRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleRow.Style.Fill.BackgroundColor = XLColor.FromArgb(0xE8, 0xED, 0xF5);

            // Header row (row 2)
            // (*) = bắt buộc
            var colHeaders = new[]
            {
                ("Ngày (*)",         "dd/MM/yyyy",                         false),
                ("Giờ nhập",         "HH:mm (mặc định 08:00)",             false),
                ("Tên máy (*)",      "Mã số máy chính xác trong hệ thống", false),
                ("Line (*)",         "VD: 01, 02, AT-04",                  true),
                ("Style (*)",        "Tên style chính xác",                true),
                ("Mã SP (*)",        "VD: 26090783TB",                     false),
                ("Order Qty",        "Số lượng đơn hàng (tùy chọn)",        false),
                ("Màu (*)",          "VD: 010, RED, BLK",                  true),
                ("Tên công đoạn (*)","Tên công đoạn chính xác",            false),
                ("Số lượng (*)",     "Số nguyên dương",                    false),
            };

            // Màu highlight cho cột mới (Line/Style/Màu)
            var headerColorNew = XLColor.FromArgb(0x14, 0x5C, 0x3B);  // green-dark
            var headerColorBase = XLColor.FromArgb(0x1F, 0x3A, 0x67); // navy

            for (int i = 0; i < colHeaders.Length; i++)
            {
                var cell = ws.Cell(2, i + 1);
                cell.Value = colHeaders[i].Item1;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = colHeaders[i].Item3 ? headerColorNew : headerColorBase;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                if (colHeaders[i].Item3)
                    cell.Style.Font.Italic = true; // đánh dấu cột mới
            }

            // Sample data rows (10 cột)
            var today = DateTime.Today.ToString("dd/MM/yyyy");
            var sampleData = new[]
            {
                (today, "08:00", "AT-04", "01", "IM2024", "26090783TB", 500, "010", "Thân trước", 100),
                (today, "08:00", "AT-04", "01", "IM2024", "26090783TB", 500, "010", "Thân sau",   95),
                (today, "13:00", "AT-04", "01", "IM2024", "26090783TB", 500, "010", "Tay áo",     80),
                (today, "08:00", "AT-05", "02", "VN2025", "26090784SP", 300, "BLK", "Thân trước", 120),
            };

            int dataStartRow = 2;
            for (int r = 0; r < sampleData.Length; r++)
            {
                var rowEl = ws.Row(dataStartRow + r);
                var d = sampleData[r];
                ws.Cell(dataStartRow + r, 1).Value = d.Item1;
                ws.Cell(dataStartRow + r, 2).Value = d.Item2;
                ws.Cell(dataStartRow + r, 3).Value = d.Item3;
                ws.Cell(dataStartRow + r, 4).Value = d.Item4;
                ws.Cell(dataStartRow + r, 5).Value = d.Item5;
                ws.Cell(dataStartRow + r, 6).Value = d.Item6;
                ws.Cell(dataStartRow + r, 7).Value = d.Item7;  // Order Qty
                ws.Cell(dataStartRow + r, 8).Value = d.Item8;  // Màu
                ws.Cell(dataStartRow + r, 9).Value = d.Item9;  // Công đoạn
                ws.Cell(dataStartRow + r, 10).Value = d.Item10; // Số lượng

                if (r % 2 == 0)
                    rowEl.Cells(1, TOTAL_COLS).Style.Fill.BackgroundColor = XLColor.FromArgb(0xF7, 0xF8, 0xFC);

                for (int c = 1; c <= TOTAL_COLS; c++)
                    ws.Cell(dataStartRow + r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
            }

            // 50 empty input rows
            int blankStart = dataStartRow + sampleData.Length;
            for (int r = 0; r < 50; r++)
                for (int c = 1; c <= TOTAL_COLS; c++)
                    ws.Cell(blankStart + r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;

            // Column widths
            ws.Column(1).Width = 16;  // Ngày
            ws.Column(2).Width = 12;  // Giờ
            ws.Column(3).Width = 14;  // Tên máy
            ws.Column(4).Width = 12;  // Line  ← mới
            ws.Column(5).Width = 18;  // Style ← mới
            ws.Column(6).Width = 18;  // Mã SP
            ws.Column(7).Width = 14;  // Order Qty ← mới
            ws.Column(8).Width = 12;  // Màu   ← mới
            ws.Column(9).Width = 24;  // Công đoạn
            ws.Column(10).Width = 14; // Số lượng

            // Note below table
            int noteRow = blankStart + 50 + 1;
            ws.Cell(noteRow, 1).Value = "(*) = Bắt buộc. Cột nền xanh lá (Line, Style, Màu) giúp hệ thống tìm đúng mã hàng. Cột Order Qty (tùy chọn): ghi số lượng đơn hàng khi mã cần tự tạo mới. Dữ liệu sản lượng sẽ được CỘNG DỒN, không ghi đè.";
            ws.Cell(noteRow, 1).Style.Font.Italic = true;
            ws.Cell(noteRow, 1).Style.Font.FontColor = XLColor.FromArgb(0x64, 0x69, 0x7F);
            ws.Range(noteRow, 1, noteRow, TOTAL_COLS).Merge();

            // ── Sheet 2: Hướng dẫn ────────────────────────────────────────
            var guide = wb.AddWorksheet("HuongDan");

            var guideLines = new[]
            {
                ("HƯỚNG DẪN NHẬP FILE SẢN LƯỢNG", true, 14),
                ("", false, 11),
                ("1. CẤU TRÚC FILE", true, 12),
                ("   Sheet 'NhapSanLuong': Chứa dữ liệu sản lượng cần nhập vào hệ thống", false, 11),
                ("   Dòng 1: Tiêu đề (không được xóa)", false, 11),
                ("   Dòng 2: Header cột (không được xóa/chỉnh sửa)", false, 11),
                ("   Dòng 3 trở đi: Dữ liệu nhập", false, 11),
                ("", false, 11),
                ("2. CÁC CỘT BẮT BUỘC", true, 12),
                ("   • Ngày: Định dạng dd/MM/yyyy (VD: 10/09/2026)", false, 11),
                ("   • Giờ nhập: Định dạng HH:mm (VD: 08:00). Nếu bỏ trống → mặc định 08:00", false, 11),
                ("   • Tên máy: Phải khớp CHÍNH XÁC với tên máy trong hệ thống", false, 11),
                ("   • Mã SP: Phải khớp CHÍNH XÁC với mã sản phẩm đang sản xuất trên máy đó", false, 11),
                ("   • Tên công đoạn: Phải khớp CHÍNH XÁC với tên công đoạn của style", false, 11),
                ("   • Số lượng: Số nguyên dương (> 0)", false, 11),
                ("", false, 11),
                ("3. LOGIC CỘNG DỒN", true, 12),
                ("   Hệ thống cộng dồn số lượng theo từng ngày và từng công đoạn.", false, 11),
                ("   Nếu đã có dữ liệu trong ngày đó, số lượng mới sẽ được CỘNG THÊM.", false, 11),
                ("   VD: Buổi sáng nhập 100, buổi chiều nhập 80 → Tổng ngày = 180", false, 11),
                ("", false, 11),
                ("4. CÁC LỖI THƯỜNG GẶP", true, 12),
                ("   ✗ Tên máy không tìm thấy → Kiểm tra lại tên máy trong hệ thống", false, 11),
                ("   ✗ Mã SP không gắn với máy đó → Kiểm tra máy đang sản xuất mã nào", false, 11),
                ("   ✗ Tên công đoạn không tìm thấy → Kiểm tra style của mã SP", false, 11),
                ("   ✗ Số lượng không hợp lệ → Nhập số nguyên dương", false, 11),
                ("", false, 11),
                ("5. QUY TRÌNH IMPORT", true, 12),
                ("   Bước 1: Tải file mẫu này từ trang Ghi sản lượng", false, 11),
                ("   Bước 2: Điền dữ liệu vào sheet 'NhapSanLuong'", false, 11),
                ("   Bước 3: Lưu file Excel", false, 11),
                ("   Bước 4: Vào trang Ghi sản lượng → Nhập bằng Excel → Chọn file → Import", false, 11),
                ("   Bước 5: Kiểm tra kết quả import (số dòng thành công / lỗi)", false, 11),
            };

            int gRow = 1;
            guide.Column(1).Width = 80;
            foreach (var (text, bold, size) in guideLines)
            {
                var cell = guide.Cell(gRow, 1);
                cell.Value = text;
                cell.Style.Font.Bold = bold;
                cell.Style.Font.FontSize = size;
                if (bold && size >= 12)
                    cell.Style.Font.FontColor = XLColor.FromArgb(0x1F, 0x3A, 0x67);
                gRow++;
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Import sản lượng từ file Excel.
        /// Logic:
        ///   1. Parse sheet "NhapSanLuong" bỏ qua 2 dòng header
        ///   2. Với mỗi dòng: tìm machine → productionInfo → styleDetail → SaveOutputRecord
        ///   3. Nếu options.AutoCreateProductionInfo = true và mã không tồn tại → tạo mới
        ///   4. Nếu options.AutoActivateStatus = true → gán Status = 1 khi tạo mới
        /// </summary>
        public async Task<ImportOutput_Result> ImportOutputFromExcel(
            Stream fileStream,
            Guid deptId,
            ImportOutput_Options options = null)
        {
            var result  = new ImportOutput_Result();
            options   ??= new ImportOutput_Options();
            int autoCreatedCount  = 0;  // đếm mã hàng tự tạo mới
            int autoActivatedCount = 0; // đếm mã hàng được bật trạng thái
            var activatedInfoIds  = new HashSet<Guid>(); // tránh update trùng

            XLWorkbook wb;
            try { wb = new XLWorkbook(fileStream); }
            catch { return new ImportOutput_Result { Success = false, Message = "File Excel không hợp lệ hoặc bị hỏng." }; }

            IXLWorksheet ws;
            if (!wb.TryGetWorksheet("NhapSanLuong", out ws))
                return new ImportOutput_Result { Success = false, Message = "Không tìm thấy sheet 'NhapSanLuong' trong file." };

            // Parse rows starting from row 3 (skip title + header)
            // Format 10 cột: Ngày | Giờ | Tên máy | Line | Style | Mã SP | Order Qty | Màu | Công đoạn | Số lượng
            var rows = new List<ImportOutput_Row>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 2;

            for (int r = 3; r <= lastRow; r++)
            {
                var dateCell   = ws.Cell(r, 1).GetString().Trim();
                var timeCell   = ws.Cell(r, 2).GetString().Trim();
                var machineVal = ws.Cell(r, 3).GetString().Trim();
                var lineVal    = ws.Cell(r, 4).GetString().Trim().ToUpperInvariant();
                var styleVal   = ws.Cell(r, 5).GetString().Trim().ToUpperInvariant();
                var spVal      = ws.Cell(r, 6).GetString().Trim().ToUpperInvariant();
                var orderQtyCell = ws.Cell(r, 7).GetString().Trim();  // cột 7: Order Qty (tùy chọn)
                var colorVal   = ws.Cell(r, 8).GetString().Trim().ToUpperInvariant(); // cột 8
                var detailVal  = ws.Cell(r, 9).GetString().Trim();                    // cột 9
                var qtyCell    = ws.Cell(r, 10).GetString().Trim();                   // cột 10

                // Skip dòng không có dữ liệu thực tế:
                // — dòng hoàn toàn trống
                // — dòng note/ghi chú cuối bảng (merged cell ở cột 1 nhưng các cột chính rỗng)
                if (string.IsNullOrEmpty(machineVal) &&
                    string.IsNullOrEmpty(spVal) &&
                    string.IsNullOrEmpty(qtyCell))
                    continue;


                result.TotalRows++;

                // Validate date
                if (!TryParseDate(dateCell, out var date))
                {
                    result.Errors.Add($"Dòng {r}: Ngày '{dateCell}' không hợp lệ (cần dd/MM/yyyy).");
                    result.SkippedRows++;
                    continue;
                }

                // Parse time
                if (string.IsNullOrEmpty(timeCell) || !TimeOnly.TryParse(timeCell, out var time))
                    time = new TimeOnly(8, 0);

                // Validate qty
                if (!int.TryParse(qtyCell, out var qty) || qty <= 0)
                {
                    result.Errors.Add($"Dòng {r}: Số lượng '{qtyCell}' không hợp lệ (cần số nguyên > 0).");
                    result.SkippedRows++;
                    continue;
                }

                // Validate bắt buộc
                if (string.IsNullOrEmpty(machineVal))
                {
                    result.Errors.Add($"Dòng {r}: Tên máy không được để trống.");
                    result.SkippedRows++;
                    continue;
                }
                if (string.IsNullOrEmpty(spVal))
                {
                    result.Errors.Add($"Dòng {r}: Mã SP không được để trống.");
                    result.SkippedRows++;
                    continue;
                }
                if (string.IsNullOrEmpty(detailVal))
                {
                    result.Errors.Add($"Dòng {r}: Tên công đoạn không được để trống.");
                    result.SkippedRows++;
                    continue;
                }

                // Parse Order Qty (tùy chọn, không bắt buộc)
                int? orderQty = null;
                if (int.TryParse(orderQtyCell, out var oq) && oq > 0)
                    orderQty = oq;

                rows.Add(new ImportOutput_Row
                {
                    Date         = date,
                    Time         = time.ToString("HH:mm"),
                    MachineName  = machineVal,
                    Line         = lineVal,
                    Style        = styleVal,
                    Spmain       = spVal,
                    OrderQty     = orderQty,
                    Color        = colorVal,
                    DetailName   = detailVal,
                    OutputNumber = qty,
                    RowIndex     = r
                });
            }

            if (!rows.Any() && !result.Errors.Any())
                return new ImportOutput_Result { Success = false, Message = "File không có dữ liệu nào để nhập." };

            // ── Pre-load cache để giảm số query khi có nhiều dòng ─────────────
            // Cache máy và ProductionInfo theo bộ phận được chọn
            var allMachinesCache = await _context.ProductionMachines
                .AsNoTracking()
                .Where(m => m.Status == 1 && m.ProductionDepartmentId == deptId)
                .ToListAsync();

            var machineByNumber = allMachinesCache
                .GroupBy(m => m.MachineNumber?.Trim().ToUpperInvariant() ?? "")
                .ToDictionary(g => g.Key, g => g.First());

            var allProdInfoCache = await _context.ProductionInfos
                .AsNoTracking()
                .Where(p => p.Status >= 0 && p.ProductionDepartmentId == deptId)
                .ToListAsync();

            // Cache StyleInfo: StyleCode (upper) → StyleInfo object
            // CHỈ lấy StyleInfo của bộ phận này (deptId) — mỗi bộ phận có công đoạn riêng
            var styleInfoList = await _context.StyleInfos
                .AsNoTracking()
                .Where(s => s.Status == 1 && s.ProductionDepartmentId == deptId)
                .ToListAsync();

            // Cache chính: (StyleCode.Upper, Keyword.Upper) → StyleInfo
            // Cho phép nhiều StyleInfo có cùng StyleCode nhưng Keyword khác nhau
            var styleInfoCache = styleInfoList
                .GroupBy(s => (
                    Code: (s.StyleCode ?? "").Trim().ToUpperInvariant(),
                    KW:   (s.Keyword    ?? "").Trim().ToUpperInvariant()
                ))
                .ToDictionary(g => g.Key, g => g.First());

            // Lookup helper: tìm StyleInfo khớp StyleCode + Keyword == SP
            Sub_Entities.Entities.StyleInfo FindStyleInfo(string styleCode, string spmain)
            {
                if (string.IsNullOrWhiteSpace(styleCode)) return null;
                var codeKey = styleCode.Trim().ToUpperInvariant();
                var kwKey   = (spmain ?? "").Trim().ToUpperInvariant();

                // 1. Khớp cả StyleCode lẫn Keyword
                if (styleInfoCache.TryGetValue((codeKey, kwKey), out var exact))
                    return exact;

                // 2. Fallback: StyleCode khớp, Keyword trống/null (StyleInfo chưa điền Keyword)
                if (styleInfoCache.TryGetValue((codeKey, ""), out var noKw))
                    return noKw;

                return null;
            }

            // Cache StyleDetail: (styleInfoId, detailName) → StyleDetail
            // Chỉ lấy các StyleDetail thuộc StyleInfo của bộ phận này
            var deptStyleInfoIds = styleInfoCache.Values.Select(s => s.Id).ToHashSet();
            var allStyleDetails = await _context.StyleDetails
                .AsNoTracking()
                .Where(sd => sd.Status == 1 && deptStyleInfoIds.Contains(sd.StyleInfoId))
                .ToListAsync();
            var styleDetailCache = allStyleDetails
                .GroupBy(sd => (sd.StyleInfoId, (sd.DetailName ?? "").Trim().ToUpperInvariant()))
                .ToDictionary(g => g.Key, g => g.First());

            // ── Process each valid row ────────────────────────────────────────
            foreach (var row in rows)
            {
                try
                {
                    // 1. Tìm máy từ cache
                    var machineKey = row.MachineName?.Trim().ToUpperInvariant() ?? "";
                    if (!machineByNumber.TryGetValue(machineKey, out var machine))
                    {
                        result.Errors.Add($"Dòng {row.RowIndex}: Không tìm thấy máy '{row.MachineName}'.");
                        result.SkippedRows++;
                        continue;
                    }

                    // 2. Tìm ProductionInfo
                    Sub_Entities.Entities.ProductionInfo productionInfo = null;

                    if (row.Line != null && row.Style != null && row.Color != null)
                    {
                        // Format mới: tra trực tiếp bằng Line + Style + Spmain + Color (chính xác)
                        var spUpper = row.Spmain.ToUpperInvariant();
                        var lineUpper = row.Line;
                        var styleUpper = row.Style;
                        var colorUpper = row.Color;

                        productionInfo = allProdInfoCache.FirstOrDefault(p =>
                            string.Equals(p.Line, lineUpper, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(p.Style, styleUpper, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(p.Spmain, spUpper, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(p.Color, colorUpper, StringComparison.OrdinalIgnoreCase));

                        if (productionInfo == null)
                        {
                            if (options.AutoCreateProductionInfo
                                && !string.IsNullOrEmpty(row.Line)
                                && !string.IsNullOrEmpty(row.Style)
                                && !string.IsNullOrEmpty(row.Spmain))
                            {
                                // Tự tạo ProductionInfo mới
                                var newInfo = new Sub_Entities.Entities.ProductionInfo
                                {
                                    Id = Guid.NewGuid(),
                                    ProductionDepartmentId = deptId,
                                    Line = row.Line,
                                    Style = row.Style,
                                    Spmain = row.Spmain,
                                    Color = row.Color ?? "",
                                    TotalQty = row.OrderQty,
                                    Status = 0, // Option 2 sẽ bật sau SaveOutputRecord (áp dụng đồng nhất cả mã mới lẫn có sẵn)
                                    CreateDate = DateTime.Now,
                                    UpdateDate = DateTime.Now,
                                };
                                _context.ProductionInfos.Add(newInfo);
                                await _context.SaveChangesAsync();

                                // Cập nhật cache để các dòng tiếp theo trong file cùng được dùng
                                allProdInfoCache.Add(newInfo);
                                productionInfo = newInfo;
                                autoCreatedCount++;
                                result.Logs.Add($"Dòng {row.RowIndex}: Tự tạo mã hàng mới Line='{row.Line}' Style='{row.Style}' SP='{row.Spmain}' Màu='{row.Color}' OrderQty={row.OrderQty?.ToString() ?? "-"}.");
                            }
                            else
                            {
                                result.Errors.Add($"Dòng {row.RowIndex}: Không tìm thấy mã hàng Line='{row.Line}' Style='{row.Style}' SP='{row.Spmain}' Màu='{row.Color}'.");
                                result.SkippedRows++;
                                continue;
                            }
                        }
                        else
                        {
                            // Format cũ: tìm theo Spmain qua DailyOutput của máy (tương thích ngược)
                            var infoIds = await _context.DailyOutputs
                                .Where(d => d.ProductionMachineId == machine.Id && d.Status == 1)
                                .Select(d => d.ProductionInfoId)
                                .Distinct()
                                .ToListAsync();

                            productionInfo = allProdInfoCache.FirstOrDefault(p =>
                                infoIds.Contains(p.Id) &&
                                string.Equals(p.Spmain, row.Spmain, StringComparison.OrdinalIgnoreCase));

                            if (productionInfo == null)
                            {
                                // Fallback: khớp đủ cả 4 — Line + Style + Spmain + Color
                                var matched = allProdInfoCache.FirstOrDefault(p =>
                                    string.Equals(p.Line,   row.Line,   StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(p.Style,  row.Style,  StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(p.Spmain, row.Spmain, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(p.Color,  row.Color,  StringComparison.OrdinalIgnoreCase) &&
                                    p.ProductionDepartmentId == deptId);

                                if (matched != null)
                                {
                                    // Tìm thấy khớp đủ 4 trường → liên kết
                                    productionInfo = matched;
                                    result.Logs.Add($"Dòng {row.RowIndex}: Máy '{row.MachineName}' chưa liên kết mã → tự động dùng mã khớp Line='{matched.Line}' Style='{matched.Style}' SP='{matched.Spmain}' Màu='{matched.Color}'.");
                                }
                                else if (options.AutoCreateProductionInfo
                                    && !string.IsNullOrEmpty(row.Line)
                                    && !string.IsNullOrEmpty(row.Style)
                                    && !string.IsNullOrEmpty(row.Spmain))
                                {
                                    // Không tìm thấy + bật autoCreate → tạo mới
                                    var newInfo = new Sub_Entities.Entities.ProductionInfo
                                    {
                                        Id                     = Guid.NewGuid(),
                                        ProductionDepartmentId = deptId,
                                        Line                   = row.Line,
                                        Style                  = row.Style,
                                        Spmain                 = row.Spmain,
                                        Color                  = row.Color ?? "",
                                        TotalQty               = row.OrderQty,
                                        Status                 = 0,
                                        CreateDate             = DateTime.Now,
                                        UpdateDate             = DateTime.Now,
                                    };
                                    _context.ProductionInfos.Add(newInfo);
                                    await _context.SaveChangesAsync();
                                    allProdInfoCache.Add(newInfo);
                                    productionInfo = newInfo;
                                    autoCreatedCount++;
                                    result.Logs.Add($"Dòng {row.RowIndex}: Tự tạo mã hàng mới Line='{row.Line}' Style='{row.Style}' SP='{row.Spmain}' Màu='{row.Color}' OrderQty={row.OrderQty?.ToString() ?? "-"}.");
                                }
                                else
                                {
                                    result.Errors.Add($"Dòng {row.RowIndex}: Không tìm thấy mã khớp Line='{row.Line}' Style='{row.Style}' SP='{row.Spmain}' Màu='{row.Color}'" +
                                        (options.AutoCreateProductionInfo ? "." : " — bật 'Tự động tạo mã hàng' để tạo mới."));
                                    result.SkippedRows++;
                                    continue;
                                }
                            }
                        }

                        // 3. Tìm StyleInfo — dùng (StyleCode + Keyword==SP) để phân biệt khi nhiều StyleInfo cùng StyleCode
                        Guid? styleInfoId = null;

                        // Ưu tiên 1: lấy từ DailyOutput đã có, verify StyleInfo thuộc đúng bộ phận + Keyword khớp SP
                        var doStyleId = await _context.DailyOutputs
                            .Where(d => d.ProductionInfoId == productionInfo.Id
                                     && d.ProductionMachineId == machine.Id
                                     && d.StyleInfoId != null
                                     && _context.StyleInfos.Any(s => s.Id == d.StyleInfoId
                                                                  && s.ProductionDepartmentId == deptId
                                                                  && s.Status == 1
                                                                  && (s.Keyword == null || s.Keyword.ToUpper() == row.Spmain.ToUpper())))
                            .Select(d => d.StyleInfoId)
                            .FirstOrDefaultAsync();
                        styleInfoId = doStyleId;

                        // Ưu tiên 2: tìm qua Style trong Excel (row.Style) + SP → (StyleCode, Keyword)
                        if (styleInfoId == null && !string.IsNullOrWhiteSpace(row.Style))
                        {
                            var si2 = FindStyleInfo(row.Style, row.Spmain);
                            if (si2 != null) styleInfoId = si2.Id;
                        }

                        // Ưu tiên 3: tìm qua productionInfo.Style (từ DB) + SP → (StyleCode, Keyword)
                        if (styleInfoId == null && !string.IsNullOrWhiteSpace(productionInfo.Style))
                        {
                            var si3 = FindStyleInfo(productionInfo.Style, row.Spmain);
                            if (si3 != null) styleInfoId = si3.Id;
                        }

                        // Fallback cuối: tìm qua Keyword của ProductionInfo làm StyleCode + SP → (StyleCode, Keyword)
                        if (styleInfoId == null && !string.IsNullOrWhiteSpace(productionInfo.Keyword))
                        {
                            var si4 = FindStyleInfo(productionInfo.Keyword, row.Spmain);
                            if (si4 != null) styleInfoId = si4.Id;
                        }

                        // 4. Tìm StyleDetail từ cache
                        Sub_Entities.Entities.StyleDetail styleDetail = null;
                        if (styleInfoId != null)
                        {
                            var detKey = (styleInfoId.Value, row.DetailName.Trim().ToUpperInvariant());
                            styleDetailCache.TryGetValue(detKey, out styleDetail);
                        }

                        if (styleDetail == null)
                        {
                            if (options.AutoCreateStyleDetail && !string.IsNullOrWhiteSpace(row.DetailName))
                            {
                                // Bước A: xác định StyleInfo target
                                Sub_Entities.Entities.StyleInfo targetStyleInfo = null;

                                if (styleInfoId.HasValue)
                                {
                                    // Đã tìm được StyleInfo — lấy từ list
                                    targetStyleInfo = styleInfoList.FirstOrDefault(s => s.Id == styleInfoId.Value);
                                }

                                if (targetStyleInfo == null)
                                {
                                    // Không tìm được → tạo mới StyleInfo
                                    targetStyleInfo = new Sub_Entities.Entities.StyleInfo
                                    {
                                        Id                     = Guid.NewGuid(),
                                        ProductionDepartmentId = deptId,
                                        StyleCode              = row.Style?.Trim() ?? "",
                                        Keyword                = row.Spmain?.Trim(),
                                        Status                 = 1,
                                        CreateDate             = DateTime.Now,
                                        UpdateDate             = DateTime.Now,
                                    };
                                    _context.StyleInfos.Add(targetStyleInfo);
                                    await _context.SaveChangesAsync();

                                    // Cập nhật các cache
                                    styleInfoList.Add(targetStyleInfo);
                                    var ck = (
                                        Code: (targetStyleInfo.StyleCode ?? "").Trim().ToUpperInvariant(),
                                        KW:   (targetStyleInfo.Keyword   ?? "").Trim().ToUpperInvariant()
                                    );
                                    styleInfoCache[ck] = targetStyleInfo;
                                    deptStyleInfoIds.Add(targetStyleInfo.Id);
                                    styleInfoId = targetStyleInfo.Id;

                                    result.Logs.Add($"Dòng {row.RowIndex}: Tự tạo StyleInfo — StyleCode='{targetStyleInfo.StyleCode}' Keyword='{targetStyleInfo.Keyword}'.");
                                }

                                // Bước B: tạo StyleDetail
                                var newDetail = new Sub_Entities.Entities.StyleDetail
                                {
                                    Id          = Guid.NewGuid(),
                                    StyleInfoId = targetStyleInfo.Id,
                                    DetailName  = row.DetailName.Trim(),
                                    Status      = 1,
                                    CreateDate  = DateTime.Now,
                                    UpdateDate  = DateTime.Now,
                                };
                                _context.StyleDetails.Add(newDetail);
                                await _context.SaveChangesAsync();

                                var dk = (targetStyleInfo.Id, row.DetailName.Trim().ToUpperInvariant());
                                styleDetailCache[dk] = newDetail;
                                styleDetail = newDetail;

                                result.Logs.Add($"Dòng {row.RowIndex}: Tự tạo công đoạn '{row.DetailName}' — StyleCode='{targetStyleInfo.StyleCode}' Keyword='{targetStyleInfo.Keyword}'.");
                            }
                            else
                            {
                                // Option tắt → báo lỗi
                                if (styleInfoId == null)
                                    result.Errors.Add($"Dòng {row.RowIndex}: Không tìm thấy StyleInfo khớp Style='{row.Style}' SP='{row.Spmain}'. Kiểm tra lại hoặc bật 'Tự tạo chi tiết mã hàng'.");
                                else
                                    result.Errors.Add($"Dòng {row.RowIndex}: Không tìm thấy công đoạn '{row.DetailName}' trong Style='{row.Style}' SP='{row.Spmain}'. Kiểm tra lại hoặc bật 'Tự tạo chi tiết mã hàng'.");
                                result.SkippedRows++;
                                continue;
                            }
                        }

                        // 5. Save (accumulate)
                        var saveReq = new RecordOutput_SaveRequest
                        {
                            ProductionInfoId = productionInfo.Id,
                            MachineId = machine.Id,
                            Date = row.Date,
                            Time = row.Time,
                            Keyword = "excel",
                            Details = new List<RecordOutput_DetailEntry>
                        {
                            new RecordOutput_DetailEntry
                            {
                                StyleDetailId = styleDetail.Id,
                                OutputNumber  = row.OutputNumber
                            }
                        }
                        };

                        // Option 3: Bỏ qua nếu đã tồn tại DailyOutputDetail cùng giờ+phút+công đoạn
                        if (options.SkipOnDuplicate
                            && DateOnly.TryParse(row.Date, out var skipDate)
                            && TimeOnly.TryParse(row.Time, out var skipTime))
                        {
                            var targetDate = skipDate.ToDateTime(TimeOnly.MinValue).Date;
                            var alreadyExists = await _context.DailyOutputs
                                .Where(d => d.ProductionInfoId    == productionInfo.Id
                                         && d.ProductionMachineId == machine.Id
                                         && d.Status              == 1
                                         && d.CreateDate.Date     == targetDate)
                                .AnyAsync(d => d.DailyOutputDetails
                                    .Any(dt => dt.StyleDetailId == styleDetail.Id
                                            && dt.Status        == 1
                                            && dt.InputTime.Hour   == skipTime.Hour
                                            && dt.InputTime.Minute == skipTime.Minute));
                            if (alreadyExists)
                            {
                                result.Logs.Add($"Dòng {row.RowIndex}: Bỏ qua — đã có sản lượng lúc {row.Time} ngày {row.Date} — Line='{productionInfo.Line}' Style='{productionInfo.Style}' SP='{productionInfo.Spmain}' Màu='{productionInfo.Color}'.");
                                result.SkippedRows++;
                                continue;
                            }
                        }

                        var (ok, msg, updatedCount) = await SaveOutputRecord(saveReq);
                        if (ok)
                        {
                            result.ImportedRows++;
                            if (updatedCount > 0)
                            {
                                result.UpdatedRows++;
                                result.Logs.Add($"Dòng {row.RowIndex}: Cập nhật {updatedCount} chi tiết — Line='{productionInfo.Line}' Style='{productionInfo.Style}' SP='{productionInfo.Spmain}' Màu='{productionInfo.Color}' (StyleDetail '{row.DetailName}', Giờ '{row.Time}').");
                            }
                            else
                            {
                                result.InsertedRows++;
                            }

                            // Option 2: Tự động bật trạng thái Đang SX cho tất cả mã được ghi thành công
                            if (options.AutoActivateStatus
                                && productionInfo.Status != 1
                                && !activatedInfoIds.Contains(productionInfo.Id))
                            {
                                var infoToActivate = await _context.ProductionInfos.FindAsync(productionInfo.Id);
                                if (infoToActivate != null && infoToActivate.Status != 1)
                                {
                                    infoToActivate.Status     = 1;
                                    infoToActivate.UpdateDate = DateTime.Now;
                                    await _context.SaveChangesAsync();
                                    productionInfo.Status = 1; // đồng bộ cache
                                    activatedInfoIds.Add(productionInfo.Id);
                                    autoActivatedCount++;
                                    result.Logs.Add($"Đã bật trạng thái Đang SX — Line='{productionInfo.Line}' Style='{productionInfo.Style}' SP='{productionInfo.Spmain}' Màu='{productionInfo.Color}'.");
                                }
                            }
                        }
                        else
                        {
                            result.Errors.Add($"Dòng {row.RowIndex}: {msg}");
                            result.SkippedRows++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Dòng {row.RowIndex}: Lỗi hệ thống — {ex.Message}");
                    result.SkippedRows++;
                }
            }

            result.Success = result.ImportedRows > 0;
            result.Message = result.ImportedRows > 0
                ? $"Nhập thành công {result.ImportedRows}/{result.TotalRows} dòng" +
                  (autoCreatedCount  > 0 ? $" | Tự tạo {autoCreatedCount} mã mới"    : "") +
                  (autoActivatedCount > 0 ? $" | Kích hoạt {autoActivatedCount} mã"   : "") +
                  (result.Logs.Any() ? "." : ".")
                : "Không có dòng nào được nhập thành công.";

            return result;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private bool TryParseDate(string input, out string isoDate)
        {
            isoDate = null;
            if (string.IsNullOrWhiteSpace(input)) return false;

            // Try dd/MM/yyyy
            if (DateTime.TryParseExact(input, "dd/MM/yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            {
                isoDate = dt.ToString("yyyy-MM-dd");
                return true;
            }
            // Try yyyy-MM-dd
            if (DateTime.TryParseExact(input, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dt))
            {
                isoDate = dt.ToString("yyyy-MM-dd");
                return true;
            }
            // Try d/M/yyyy
            if (DateTime.TryParse(input, out dt))
            {
                isoDate = dt.ToString("yyyy-MM-dd");
                return true;
            }
            return false;
        }

        // ── Helpers: parse DateOnly từ chuỗi dd/MM/yyyy hoặc yyyy-MM-dd ──────

        private bool TryParseDateOnly(string input, out DateOnly result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(input)) return false;

            if (DateOnly.TryParseExact(input, "dd/MM/yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out result))
                return true;

            if (DateOnly.TryParseExact(input, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out result))
                return true;

            // Try generic parse
            if (DateTime.TryParse(input, out var dt))
            {
                result = DateOnly.FromDateTime(dt);
                return true;
            }
            return false;
        }

        // =====================================================================
        //  Import Mã Hàng (ProductionInfo) từ file Excel
        // =====================================================================

        /// <summary>
        /// Tạo file Excel mẫu để nhập mã hàng (ProductionInfo).
        /// Cột A: Tên bộ phận (bỏ qua, chỉ để thông tin)
        /// Cột B: Line, C: Style, D: SP, E: TotalQty, F: Color, G: Target,
        /// H: InlineLine, I: InlineDepartment
        /// Dữ liệu bắt đầu từ dòng 2 (dòng 1 là header).
        /// </summary>
        public byte[] GenerateProductionInfoImportTemplate()
        {
            using var wb = new XLWorkbook();

            var ws = wb.AddWorksheet("NhapMaHang");
            ws.SheetView.FreezeRows(1);

            // Header row (row 1) — bỏ cột dept, bắt đầu từ Line
            string[] headers = {
                "Line (*)",
                "Style (*)",
                "SP (*)",
                "TotalQty",
                "Color (*)",
                "Target",
                "InlineLine (dd/MM/yyyy)",
                "InlineDepartment (dd/MM/yyyy)"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x1F, 0x3A, 0x67);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }


            // Sample data rows (từ dòng 2)
            var samples = new (string line, string style, string sp, int qty, string color, int target, string inlineLine, string inlineDept)[]
            {
                ("8", "S123", "SP-001", 5000, "010", 800, DateTime.Today.ToString("dd/MM/yyyy"), DateTime.Today.AddDays(5).ToString("dd/MM/yyyy")),
            };

            int dataStart = 2;
            for (int r = 0; r < samples.Length; r++)
            {
                var s = samples[r];
                ws.Cell(dataStart + r, 1).Value = s.line;
                ws.Cell(dataStart + r, 2).Value = s.style;
                ws.Cell(dataStart + r, 3).Value = s.sp;
                ws.Cell(dataStart + r, 4).Value = s.qty;
                ws.Cell(dataStart + r, 5).Value = s.color;
                ws.Cell(dataStart + r, 6).Value = s.target;
                ws.Cell(dataStart + r, 7).Value = s.inlineLine;
                ws.Cell(dataStart + r, 8).Value = s.inlineDept;

                if (r % 2 == 0)
                    ws.Range(dataStart + r, 1, dataStart + r, 8).Style.Fill.BackgroundColor = XLColor.FromArgb(0xF7, 0xF8, 0xFC);

                for (int c = 1; c <= 8; c++)
                    ws.Cell(dataStart + r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
            }

            // 50 dòng trống để nhập
            int blankStart = dataStart + samples.Length;
            for (int r = 0; r < 50; r++)
                for (int c = 1; c <= 8; c++)
                    ws.Cell(blankStart + r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;

            // Column widths
            ws.Column(1).Width = 12; // Line
            ws.Column(2).Width = 18; // Style
            ws.Column(3).Width = 18; // SP
            ws.Column(4).Width = 12; // TotalQty
            ws.Column(5).Width = 14; // Color
            ws.Column(6).Width = 12; // Target
            ws.Column(7).Width = 22; // InlineLine
            ws.Column(8).Width = 22; // InlineDepartment

            // Ghi chú
            int noteRow = blankStart + 50 + 1;
            ws.Cell(noteRow, 1).Value = "(*) = Bắt buộc. Bộ phận được chọn khi nhập trên web. Keyword tự động = Line+Style+SP+Color.";
            ws.Cell(noteRow, 1).Style.Font.Italic = true;
            ws.Cell(noteRow, 1).Style.Font.FontColor = XLColor.FromArgb(0x64, 0x69, 0x7F);
            ws.Range(noteRow, 1, noteRow, 8).Merge();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Import mã hàng (ProductionInfo) từ file Excel.
        /// - Bỏ qua dòng 1 (header). Không có cột dept.
        /// - Đọc từ dòng 2: A=Line, B=Style, C=SP, D=TotalQty, E=Color, F=Target, G=InlineLine, H=InlineDept.
        /// - Keyword = Line + Style + SP + Color (upper, không khoảng trắng).
        /// - Status mặc định = 0 (chờ sản xuất).
        /// - Nếu trùng Line+Style+SP+Color trong cùng bộ phận → UPDATE, không tạo mới.
        /// </summary>
        public async Task<ImportProductionInfo_Result> ImportProductionInfoFromExcel(
            Stream fileStream, Guid productionDepartmentId)
        {
            var result = new ImportProductionInfo_Result();

            // Kiểm tra bộ phận tồn tại
            var dept = await _context.ProductionDepartments.FindAsync(productionDepartmentId);
            if (dept == null)
                return new ImportProductionInfo_Result
                {
                    Success = false,
                    Message = "Không tìm thấy bộ phận được chọn."
                };

            XLWorkbook wb;
            try { wb = new XLWorkbook(fileStream); }
            catch { return new ImportProductionInfo_Result { Success = false, Message = "File Excel không hợp lệ hoặc bị hỏng." }; }

            // Tìm sheet đầu tiên (hỗ trợ cả file tải về từ template lẫn file tự tạo)
            IXLWorksheet ws;
            if (!wb.TryGetWorksheet("NhapMaHang", out ws))
                ws = wb.Worksheets.First();

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            // Parse từ dòng 2 (bỏ dòng 1 header)
            var rows = new List<ImportProductionInfo_Row>();

            for (int r = 2; r <= lastRow; r++)
            {
                // Đọc từ cột 1: A=Line, B=Style, C=SP, D=TotalQty, E=Color, F=Target, G=InlineLine, H=InlineDept
                var lineVal       = ws.Cell(r, 1).GetString().Trim();
                var styleVal      = ws.Cell(r, 2).GetString().Trim();
                var spVal         = ws.Cell(r, 3).GetString().Trim();
                var qtyCell       = ws.Cell(r, 4).GetString().Trim();
                var colorVal      = ws.Cell(r, 5).GetString().Trim();
                var targetCell    = ws.Cell(r, 6).GetString().Trim();
                var inlineLineVal = ws.Cell(r, 7).GetString().Trim();
                var inlineDeptVal = ws.Cell(r, 8).GetString().Trim();

                // Bỏ qua dòng hoàn toàn rỗng
                if (string.IsNullOrEmpty(lineVal) && string.IsNullOrEmpty(styleVal) &&
                    string.IsNullOrEmpty(spVal) && string.IsNullOrEmpty(colorVal))
                    continue;

                result.TotalRows++;

                // Validate các cột bắt buộc
                var errors = new List<string>();
                if (string.IsNullOrEmpty(lineVal))   errors.Add("Line trống");
                if (string.IsNullOrEmpty(styleVal))  errors.Add("Style trống");
                if (string.IsNullOrEmpty(spVal))     errors.Add("SP trống");
                if (string.IsNullOrEmpty(colorVal))  errors.Add("Color trống");

                if (errors.Any())
                {
                    result.Errors.Add($"Dòng {r}: {string.Join(", ", errors)}.");
                    result.SkippedRows++;
                    continue;
                }

                int? totalQty = null;
                if (!string.IsNullOrEmpty(qtyCell))
                {
                    if (int.TryParse(qtyCell, out var q) && q >= 0) totalQty = q;
                    else
                    {
                        result.Errors.Add($"Dòng {r}: TotalQty '{qtyCell}' không hợp lệ (cần số nguyên >= 0).");
                        result.SkippedRows++;
                        continue;
                    }
                }

                int? target = null;
                if (!string.IsNullOrEmpty(targetCell))
                {
                    if (int.TryParse(targetCell, out var t) && t >= 0) target = t;
                    else
                    {
                        result.Errors.Add($"Dòng {r}: Target '{targetCell}' không hợp lệ (cần số nguyên >= 0).");
                        result.SkippedRows++;
                        continue;
                    }
                }

                // Parse ngày (không bắt buộc)
                string inlineLineParsed = null;
                if (!string.IsNullOrEmpty(inlineLineVal))
                {
                    if (TryParseDate(inlineLineVal, out var il)) inlineLineParsed = il;
                    else
                    {
                        result.Errors.Add($"Dòng {r}: InlineLine '{inlineLineVal}' không đúng định dạng (dd/MM/yyyy).");
                        result.SkippedRows++;
                        continue;
                    }
                }

                string inlineDeptParsed = null;
                if (!string.IsNullOrEmpty(inlineDeptVal))
                {
                    if (TryParseDate(inlineDeptVal, out var id)) inlineDeptParsed = id;
                    else
                    {
                        result.Errors.Add($"Dòng {r}: InlineDepartment '{inlineDeptVal}' không đúng định dạng (dd/MM/yyyy).");
                        result.SkippedRows++;
                        continue;
                    }
                }

                rows.Add(new ImportProductionInfo_Row
                {
                    Line             = lineVal,
                    Style            = styleVal,
                    Spmain           = spVal,
                    TotalQty         = totalQty,
                    Color            = colorVal,
                    Target           = target,
                    InlineLine       = inlineLineParsed,
                    InlineDepartment = inlineDeptParsed,
                    RowIndex         = r
                });
            }

            if (!rows.Any() && !result.Errors.Any())
                return new ImportProductionInfo_Result { Success = false, Message = "File không có dữ liệu nào để nhập." };

            // Xử lý từng dòng hợp lệ
            var now = DateTime.Now;
            foreach (var row in rows)
            {
                try
                {
                    // Tạo keyword = Line + Style + SP + Color (viết hoa, không khoảng trắng)
                    var keyword = $"{row.Line}{row.Style}{row.Spmain}{row.Color}"
                        .ToUpperInvariant()
                        .Replace(" ", "");

                    // Kiểm tra đã tồn tại chưa (cùng Dept + Line + Style + SP + Color, chưa bị xóa)
                    var existing = await _context.ProductionInfos
                        .FirstOrDefaultAsync(p =>
                            p.ProductionDepartmentId == productionDepartmentId &&
                            p.Line   == row.Line   &&
                            p.Style  == row.Style  &&
                            p.Spmain == row.Spmain &&
                            p.Color  == row.Color  &&
                            p.Status >= 0);

                    if (existing != null)
                    {
                        // UPDATE: cập nhật thông tin mới
                        existing.TotalQty        = row.TotalQty        ?? existing.TotalQty;
                        existing.Target          = row.Target          ?? existing.Target;
                        existing.InlineLine      = row.InlineLine      != null
                            ? DateOnly.ParseExact(row.InlineLine, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                            : existing.InlineLine;
                        existing.InlineDepartment = row.InlineDepartment != null
                            ? DateOnly.ParseExact(row.InlineDepartment, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                            : existing.InlineDepartment;
                        existing.Keyword    = keyword;
                        existing.UpdateDate = now;

                        result.UpdatedRows++;
                    }
                    else
                    {
                        // INSERT: tạo mới
                        var info = new Sub_Entities.Entities.ProductionInfo
                        {
                            Id                    = Guid.NewGuid(),
                            ProductionDepartmentId = productionDepartmentId,
                            Line                  = row.Line,
                            Style                 = row.Style,
                            Spmain                = row.Spmain,
                            TotalQty              = row.TotalQty,
                            Color                 = row.Color,
                            Target                = row.Target,
                            InlineLine            = row.InlineLine != null
                                ? DateOnly.ParseExact(row.InlineLine, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                                : (DateOnly?)null,
                            InlineDepartment      = row.InlineDepartment != null
                                ? DateOnly.ParseExact(row.InlineDepartment, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                                : (DateOnly?)null,
                            Keyword               = keyword,
                            Status                = 0,  // 0 = đang chờ sản xuất
                            CreateDate            = now,
                            UpdateDate            = now
                        };
                        _context.ProductionInfos.Add(info);
                        result.ImportedRows++;
                    }

                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Dòng {row.RowIndex}: Lỗi hệ thống — {ex.Message}");
                    result.SkippedRows++;
                }
            }

            var total = result.ImportedRows + result.UpdatedRows;
            result.Success = total > 0;
            result.Message = total > 0
                ? $"Hoàn tất: tạo mới {result.ImportedRows}, cập nhật {result.UpdatedRows}, bỏ qua {result.SkippedRows} / {result.TotalRows} dòng."
                : "Không có dòng nào được xử lý thành công.";

            return result;
        }
    }
}
