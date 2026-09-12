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

            // Header row
            ws.Cell(1, 1).Value = "StyleCode *";
            ws.Cell(1, 2).Value = "Keyword (tự động nếu trống)";
            ws.Cell(1, 3).Value = "Tên công đoạn 1";
            ws.Cell(1, 4).Value = "Tên công đoạn 2";
            ws.Cell(1, 5).Value = "Tên công đoạn 3";

            // Style header
            var hdr = ws.Range(1, 1, 1, 5);
            hdr.Style.Font.Bold = true;
            hdr.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#3B82F6");
            hdr.Style.Font.FontColor       = ClosedXML.Excel.XLColor.White;

            // Sample data
            ws.Cell(2, 1).Value = "ST001";
            ws.Cell(2, 2).Value = "";
            ws.Cell(2, 3).Value = "Cắt vải";
            ws.Cell(2, 4).Value = "May thân trước";
            ws.Cell(2, 5).Value = "Hoàn thiện";

            ws.Columns().AdjustToContents();

            using var ms = new System.IO.MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "StyleInfo_Template.xlsx");
        }

        public class IdRequest { public string Id { get; set; } }
    }
}
