using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sub_Services.Execute;
using System.Threading.Tasks;

namespace SNP_SubSystem.Controllers.ImportExport
{
    /// <summary>
    /// ImportExportController — Xử lý tất cả tính năng xuất nhập file Excel.
    /// Route: /ImportExport/
    /// </summary>
    public class ImportExportController : Controller
    {
        private readonly SubSystemService _service;

        public ImportExportController(SubSystemService service)
        {
            _service = service;
        }

        // ---------------------------------------------------------------
        // GET: Tải file mẫu nhập sản lượng
        // GET /ImportExport/DownloadOutputTemplate
        // ---------------------------------------------------------------

        [HttpGet]
        public IActionResult DownloadOutputTemplate()
        {
            var bytes    = _service.GenerateOutputImportTemplate();
            var fileName = $"MauNhapSanLuong_{System.DateTime.Today:yyyyMMdd}.xlsx";
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // ---------------------------------------------------------------
        // POST: Import sản lượng từ file Excel
        // POST /ImportExport/ImportOutputExcel
        // ---------------------------------------------------------------

        [HttpPost]
        public async Task<IActionResult> ImportOutputExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "Vui lòng chọn file Excel." });

            var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".xlsx" && ext != ".xls")
                return BadRequest(new { success = false, message = "Chỉ hỗ trợ file .xlsx hoặc .xls." });

            if (file.Length > 10 * 1024 * 1024) // 10 MB limit
                return BadRequest(new { success = false, message = "File quá lớn (tối đa 10 MB)." });

            using var stream = file.OpenReadStream();
            var result = await _service.ImportOutputFromExcel(stream);

            return Ok(result);
        }
    }
}
