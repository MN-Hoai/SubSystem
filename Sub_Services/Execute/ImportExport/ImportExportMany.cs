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

            // Title row
            var titleRow = ws.Range("A1:F1");
            titleRow.Merge();
            titleRow.Value = "NHẬP SẢN LƯỢNG HÀNG NGÀY";
            titleRow.Style.Font.Bold = true;
            titleRow.Style.Font.FontSize = 14;
            titleRow.Style.Font.FontColor = XLColor.FromArgb(0x1F, 0x3A, 0x67);
            titleRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleRow.Style.Fill.BackgroundColor = XLColor.FromArgb(0xE8, 0xED, 0xF5);

            // Header row (row 2)
            string[] headers = { "Ngày (*)", "Giờ nhập", "Tên máy (*)", "Mã SP (*)", "Tên công đoạn (*)", "Số lượng (*)" };
            string[] helpText = {
                "Định dạng: dd/MM/yyyy",
                "Định dạng: HH:mm (mặc định 08:00)",
                "Tên máy chính xác (VD: M01, M-001)",
                "Mã sản phẩm chính xác (VD: SP-001)",
                "Tên công đoạn chính xác",
                "Số nguyên dương"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(2, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x1F, 0x3A, 0x67);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                // Note: help text is described in Sheet "HuongDan"
            }

            // Sample data rows
            var sampleData = new (string date, string time, string machine, string sp, string detail, int qty)[]
            {
                (DateTime.Today.ToString("dd/MM/yyyy"), "08:00", "M01", "SP-001", "Cắt", 100),
                (DateTime.Today.ToString("dd/MM/yyyy"), "08:00", "M01", "SP-001", "May", 95),
                (DateTime.Today.ToString("dd/MM/yyyy"), "13:00", "M01", "SP-001", "Cắt", 80),
                (DateTime.Today.ToString("dd/MM/yyyy"), "08:00", "M02", "SP-002", "Cắt", 120),
            };

            int dataStartRow = 3;
            for (int r = 0; r < sampleData.Length; r++)
            {
                var row = ws.Row(dataStartRow + r);
                var d = sampleData[r];
                ws.Cell(dataStartRow + r, 1).Value = d.date;
                ws.Cell(dataStartRow + r, 2).Value = d.time;
                ws.Cell(dataStartRow + r, 3).Value = d.machine;
                ws.Cell(dataStartRow + r, 4).Value = d.sp;
                ws.Cell(dataStartRow + r, 5).Value = d.detail;
                ws.Cell(dataStartRow + r, 6).Value = d.qty;

                // Zebra stripe
                if (r % 2 == 0)
                {
                    row.Cells(1, 6).Style.Fill.BackgroundColor = XLColor.FromArgb(0xF7, 0xF8, 0xFC);
                }

                for (int c = 1; c <= 6; c++)
                    ws.Cell(dataStartRow + r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
            }

            // Add 50 empty rows for user input (after samples)
            int blankStart = dataStartRow + sampleData.Length;
            for (int r = 0; r < 50; r++)
            {
                for (int c = 1; c <= 6; c++)
                    ws.Cell(blankStart + r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
            }

            // Column widths
            ws.Column(1).Width = 16;  // Ngày
            ws.Column(2).Width = 12;  // Giờ
            ws.Column(3).Width = 16;  // Tên máy
            ws.Column(4).Width = 18;  // Mã SP
            ws.Column(5).Width = 24;  // Công đoạn
            ws.Column(6).Width = 14;  // Số lượng

            // Note below table
            int noteRow = blankStart + 50 + 1;
            ws.Cell(noteRow, 1).Value = "(*) = Bắt buộc. Dữ liệu nhập sẽ được CỘNG DỒN vào ngày đã chọn. Cùng ngày nhập nhiều lần sẽ tích lũy.";
            ws.Cell(noteRow, 1).Style.Font.Italic = true;
            ws.Cell(noteRow, 1).Style.Font.FontColor = XLColor.FromArgb(0x64, 0x69, 0x7F);
            ws.Range(noteRow, 1, noteRow, 6).Merge();

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
        /// </summary>
        public async Task<ImportOutput_Result> ImportOutputFromExcel(Stream fileStream)
        {
            var result = new ImportOutput_Result();

            XLWorkbook wb;
            try { wb = new XLWorkbook(fileStream); }
            catch { return new ImportOutput_Result { Success = false, Message = "File Excel không hợp lệ hoặc bị hỏng." }; }

            IXLWorksheet ws;
            if (!wb.TryGetWorksheet("NhapSanLuong", out ws))
                return new ImportOutput_Result { Success = false, Message = "Không tìm thấy sheet 'NhapSanLuong' trong file." };

            // Parse rows starting from row 3 (skip title + header)
            var rows = new List<ImportOutput_Row>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 2;

            for (int r = 3; r <= lastRow; r++)
            {
                var dateCell   = ws.Cell(r, 1).GetString().Trim();
                var timeCell   = ws.Cell(r, 2).GetString().Trim();
                var machineVal = ws.Cell(r, 3).GetString().Trim();
                var spVal      = ws.Cell(r, 4).GetString().Trim();
                var detailVal  = ws.Cell(r, 5).GetString().Trim();
                var qtyCell    = ws.Cell(r, 6).GetString().Trim();

                // Skip completely empty rows
                if (string.IsNullOrEmpty(dateCell) && string.IsNullOrEmpty(machineVal) &&
                    string.IsNullOrEmpty(spVal) && string.IsNullOrEmpty(qtyCell))
                    continue;

                result.TotalRows++;

                // Validate date
                if (!TryParseDate(dateCell, out var date))
                {
                    result.Errors.Add($"Dòng {r}: Ngày '{dateCell}' không hợp lệ (cần định dạng dd/MM/yyyy).");
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

                rows.Add(new ImportOutput_Row
                {
                    Date         = date,
                    Time         = time.ToString("HH:mm"),
                    MachineName  = machineVal,
                    Spmain       = spVal,
                    DetailName   = detailVal,
                    OutputNumber = qty,
                    RowIndex     = r
                });
            }

            if (!rows.Any() && !result.Errors.Any())
                return new ImportOutput_Result { Success = false, Message = "File không có dữ liệu nào để nhập." };

            // Process each valid row
            foreach (var row in rows)
            {
                try
                {
                    // 1. Find machine by name
                    var machine = await _context.ProductionMachines
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.MachineNumber == row.MachineName && m.Status == 1);

                    if (machine == null)
                    {
                        result.Errors.Add($"Dòng {row.RowIndex}: Không tìm thấy máy '{row.MachineName}'.");
                        result.SkippedRows++;
                        continue;
                    }

                    // 2. Find ProductionInfo linked to this machine with matching SP
                    var infoIds = await _context.DailyOutputs
                        .Where(d => d.ProductionMachineId == machine.Id && d.Status == 1)
                        .Select(d => d.ProductionInfoId)
                        .Distinct()
                        .ToListAsync();

                    var productionInfo = await _context.ProductionInfos
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => infoIds.Contains(p.Id) && p.Spmain == row.Spmain);

                    if (productionInfo == null)
                    {
                        result.Errors.Add($"Dòng {row.RowIndex}: Máy '{row.MachineName}' không có mã SP '{row.Spmain}'.");
                        result.SkippedRows++;
                        continue;
                    }

                    // 3. Find StyleDetail by name via StyleInfo
                    // Get styleInfoId from DailyOutput or from ProductionInfo.Keyword
                    var styleInfoId = await _context.DailyOutputs
                        .Where(d => d.ProductionInfoId == productionInfo.Id
                                 && d.ProductionMachineId == machine.Id
                                 && d.StyleInfoId != null)
                        .Select(d => d.StyleInfoId)
                        .FirstOrDefaultAsync();

                    if (styleInfoId == null && productionInfo.Keyword != null)
                    {
                        var si = await _context.StyleInfos
                            .AsNoTracking()
                            .FirstOrDefaultAsync(s => s.StyleCode == productionInfo.Keyword && s.Status == 1);
                        styleInfoId = si?.Id;
                    }

                    Sub_Entities.Entities.StyleDetail styleDetail = null;
                    if (styleInfoId != null)
                    {
                        styleDetail = await _context.StyleDetails
                            .AsNoTracking()
                            .FirstOrDefaultAsync(sd =>
                                sd.StyleInfoId == styleInfoId &&
                                sd.DetailName  == row.DetailName &&
                                sd.Status      == 1);
                    }

                    if (styleDetail == null)
                    {
                        result.Errors.Add($"Dòng {row.RowIndex}: Không tìm thấy công đoạn '{row.DetailName}' cho mã '{row.Spmain}'.");
                        result.SkippedRows++;
                        continue;
                    }

                    // 4. Save (accumulate)
                    var saveReq = new RecordOutput_SaveRequest
                    {
                        ProductionInfoId = productionInfo.Id,
                        MachineId        = machine.Id,
                        Date             = row.Date,
                        Time             = row.Time,
                        Details          = new List<RecordOutput_DetailEntry>
                        {
                            new RecordOutput_DetailEntry
                            {
                                StyleDetailId = styleDetail.Id,
                                OutputNumber  = row.OutputNumber
                            }
                        }
                    };

                    var (ok, msg) = await SaveOutputRecord(saveReq);
                    if (ok)
                        result.ImportedRows++;
                    else
                    {
                        result.Errors.Add($"Dòng {row.RowIndex}: {msg}");
                        result.SkippedRows++;
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
                ? $"Nhập thành công {result.ImportedRows}/{result.TotalRows} dòng."
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
    }
}
