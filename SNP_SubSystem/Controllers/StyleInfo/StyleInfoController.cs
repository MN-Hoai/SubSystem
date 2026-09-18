using Microsoft.AspNetCore.Mvc;
using Sub_Services.Execute;
using System;
using System.Threading.Tasks;

namespace SNP_SubSystem.Controllers.StyleInfo
{
    public class StyleInfoController : Controller
    {
        private readonly SubSystemService _service;

        public StyleInfoController(SubSystemService service)
        {
            _service = service;
        }

        // GET /StyleInfo/Index
        public IActionResult Index()
        {
            return View("~/Views/StyleInfo/index.cshtml");
        }

        // ── LIST (paged) ─────────────────────────────────────────────────
        // GET /StyleInfo/GetList?deptId=&kw=&page=1&pageSize=20
        [HttpGet]
        public async Task<IActionResult> GetList(Guid? deptId, string kw, int page = 1, int pageSize = 20)
        {
            var result = await _service.GetStyleInfoList(deptId, kw, page, pageSize);
            return Ok(result);
        }

        // ── DEPARTMENTS dropdown ─────────────────────────────────────────
        // GET /StyleInfo/GetDepartments
        [HttpGet]
        public async Task<IActionResult> GetDepartments()
        {
            var list = await _service.GetDepartmentList();
            return Ok(list);
        }

        // ── GET BY ID ────────────────────────────────────────────────────
        // GET /StyleInfo/GetById?id=
        [HttpGet]
        public async Task<IActionResult> GetById(Guid id)
        {
            var item = await _service.GetStyleInfoById(id);
            if (item == null) return NotFound(new { success = false, message = "Không tìm thấy." });
            return Ok(item);
        }

        // ── UPSERT (Create / Update) ──────────────────────────────────────
        // POST /StyleInfo/Upsert
        [HttpPost]
        public async Task<IActionResult> Upsert([FromBody] SubSystemService.StyleInfoUpsert_Request req)
        {
            if (req == null) return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ." });
            var (ok, msg, id) = await _service.UpsertStyleInfo(req);
            return ok ? Ok(new { success = true, message = msg, id }) : BadRequest(new { success = false, message = msg });
        }

        // ── SOFT DELETE ───────────────────────────────────────────────────
        // POST /StyleInfo/SoftDelete
        [HttpPost]
        public async Task<IActionResult> SoftDelete([FromBody] IdRequest req)
        {
            if (req?.Id == null) return BadRequest(new { success = false, message = "Id không hợp lệ." });
            if (!Guid.TryParse(req.Id, out var id)) return BadRequest(new { success = false, message = "Id sai định dạng." });
            var (ok, msg) = await _service.SoftDeleteStyleInfo(id);
            return ok ? Ok(new { success = true, message = msg }) : BadRequest(new { success = false, message = msg });
        }

        // ── BULK DELETE ───────────────────────────────────────────────────
        // POST /StyleInfo/BulkDelete
        [HttpPost]
        public async Task<IActionResult> BulkDelete([FromBody] BulkDeleteRequest req)
        {
            if (req?.Ids == null || req.Ids.Count == 0)
                return BadRequest(new { success = false, message = "Không có mã nào được chọn." });

            var guids = new System.Collections.Generic.List<Guid>();
            foreach (var s in req.Ids)
                if (Guid.TryParse(s, out var g)) guids.Add(g);

            if (!guids.Any())
                return BadRequest(new { success = false, message = "Danh sách Id không hợp lệ." });

            var (count, msg) = await _service.BulkSoftDeleteStyleInfo(guids);
            return count > 0
                ? Ok(new { success = true, message = msg, deleted = count })
                : BadRequest(new { success = false, message = msg });
        }


        // ── IMPORT EXCEL ──────────────────────────────────────────────────
        // POST /StyleInfo/ImportExcel  (multipart/form-data: deptId + file)
        [HttpPost]
        public async Task<IActionResult> ImportExcel([FromForm] Guid deptId)
        {
            if (deptId == Guid.Empty)
                return BadRequest(new { success = false, message = "Bộ phận không hợp lệ." });

            var file = Request.Form.Files.Count > 0 ? Request.Form.Files[0] : null;
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "Vui lòng chọn file Excel." });

            using var stream = file.OpenReadStream();
            var (created, updated, failed, errs) = await _service.BulkImportStyleInfo(deptId, stream);

            return Ok(new
            {
                success = failed == 0 || (created + updated) > 0,
                created,
                updated,
                failed,
                errors  = errs,
                message = $"Nhập xong: {created} tạo mới, {updated} cập nhật" + (failed > 0 ? $", {failed} lỗi." : ".")
            });
        }

        // ── DOWNLOAD TEMPLATE ─────────────────────────────────────────────
        // GET /StyleInfo/DownloadTemplate
        [HttpGet]
        public IActionResult DownloadTemplate()
        {
            using var wb = new ClosedXML.Excel.XLWorkbook();
            var ws = wb.AddWorksheet("StyleInfo");

            // ── Header row ─────────────────────────────────────────────────
            ws.Cell(1, 1).Value = "StyleCode *";
            ws.Cell(1, 2).Value = "Keyword (tự động nếu trống)";
            ws.Cell(1, 3).Value = "Tên công đoạn (mỗi dòng 1 công đoạn)";

            var hdr = ws.Range(1, 1, 1, 3);
            hdr.Style.Font.Bold = true;
            hdr.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#3B82F6");
            hdr.Style.Font.FontColor       = ClosedXML.Excel.XLColor.White;

            // ── Sample data — format DỌC ───────────────────────────────────
            // StyleCode ST001 với 3 công đoạn
            ws.Cell(2, 1).Value = "ST001";
            ws.Cell(2, 2).Value = "keyword1";
            ws.Cell(2, 3).Value = "Cắt vải";

            ws.Cell(3, 1).Value = "ST001";
            ws.Cell(3, 2).Value = "";           // keyword chỉ cần điền 1 lần
            ws.Cell(3, 3).Value = "May thân trước";

            ws.Cell(4, 1).Value = "ST001";
            ws.Cell(4, 2).Value = "";
            ws.Cell(4, 3).Value = "Hoàn thiện";

            // StyleCode ST002 với 2 công đoạn
            ws.Cell(5, 1).Value = "ST002";
            ws.Cell(5, 2).Value = "";
            ws.Cell(5, 3).Value = "Cắt";

            ws.Cell(6, 1).Value = "ST002";
            ws.Cell(6, 2).Value = "";
            ws.Cell(6, 3).Value = "May";

            // StyleCode ST003 không có công đoạn (chỉ tạo header)
            ws.Cell(7, 1).Value = "ST003";
            ws.Cell(7, 2).Value = "keyword3";
            ws.Cell(7, 3).Value = "";

            // ── Styling ────────────────────────────────────────────────────
            // Tô màu xen kẽ theo nhóm StyleCode để dễ đọc
            var grp1 = ws.Range(2, 1, 4, 3);
            grp1.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#EFF6FF");

            var grp2 = ws.Range(5, 1, 6, 3);
            grp2.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#F0FDF4");

            ws.Columns().AdjustToContents();
            ws.Column(3).Width = Math.Max(ws.Column(3).Width, 36);

            // ── Sheet hướng dẫn ────────────────────────────────────────────
            var noteSheet = wb.AddWorksheet("Hướng dẫn");
            noteSheet.Cell(1, 1).Value = "HƯỚNG DẪN NHẬP FILE EXCEL — StyleInfo";
            noteSheet.Cell(1, 1).Style.Font.Bold = true;
            noteSheet.Cell(1, 1).Style.Font.FontSize = 14;

            noteSheet.Cell(3, 1).Value = "Cột A (StyleCode *)";
            noteSheet.Cell(3, 2).Value = "Bắt buộc. Lặp lại trên mỗi dòng nếu StyleCode có nhiều công đoạn.";
            noteSheet.Cell(4, 1).Value = "Cột B (Keyword)";
            noteSheet.Cell(4, 2).Value = "Không bắt buộc. Chỉ cần điền 1 lần cho mỗi StyleCode.";
            noteSheet.Cell(5, 1).Value = "Cột C (Tên công đoạn)";
            noteSheet.Cell(5, 2).Value = "Mỗi dòng là 1 công đoạn. Để trống nếu chỉ muốn tạo / cập nhật StyleCode.";
            noteSheet.Cell(7, 1).Value = "LƯU Ý:";
            noteSheet.Cell(7, 1).Style.Font.Bold = true;
            noteSheet.Cell(8, 1).Value = "- Nếu StyleCode đã tồn tại: chỉ THÊM công đoạn mới, KHÔNG xoá công đoạn cũ.";
            noteSheet.Cell(9, 1).Value = "- Nếu StyleCode chưa tồn tại: tạo mới StyleCode và thêm tất cả công đoạn.";
            noteSheet.Cell(10, 1).Value = "- Dòng 1 là tiêu đề, không đọc dữ liệu từ dòng 1.";
            noteSheet.Columns().AdjustToContents();
            noteSheet.Column(2).Width = 70;

            using var ms = new System.IO.MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "StyleInfo_Template.xlsx");
        }

        public class IdRequest { public string Id { get; set; } }
        public class BulkDeleteRequest { public System.Collections.Generic.List<string> Ids { get; set; } }
    }
}
