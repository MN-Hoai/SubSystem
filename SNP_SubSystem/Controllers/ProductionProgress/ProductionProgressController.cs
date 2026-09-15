using Microsoft.AspNetCore.Mvc;
using Sub_Entities.Entities;
using Sub_Services.Execute;
using System;
using System.Threading.Tasks;

namespace SNP_SubSystem.Controllers.ProductionProgress
{
    public class ProductionProgressController : Controller
    {
        private readonly SubSystemService _service;

        public ProductionProgressController(SubSystemService service)
        {
            _service = service;
        }

        public IActionResult ATProgress()
        {
            return View("~/Views/ProductionProgress/Index.cshtml");
        }
        public IActionResult LineProgress()
        {
            return View("~/Views/ProductionProgress/LineProgress.cshtml");
        }

        /// <summary>Trang form tạo / chỉnh sửa mã hàng. Id = null → tạo mới.</summary>
        public IActionResult ProductionInfoForm(Guid? id = null)
        {
            return View("~/Views/ProductionProgress/ProductionInfoForm.cshtml", id);
        }

        // ---------------------------------------------------------------
        // GET: Trang lịch sử mã hàng
        // GET /ProductionProgress/ProductionInfoHistory
        // ---------------------------------------------------------------
        public IActionResult ProductionInfoHistory()
        {
            return View("~/Views/ProductionProgress/PartialView/_ProductionInfoHistory.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> GetDepartments()
        {
            var list = await _service.GetDepartmentList();
            return Ok(list);
        }

        [HttpGet]
        public async Task<IActionResult> GetLines(Guid? departmentId = null)
        {
            var list = await _service.GetLineList(departmentId);
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> GetList(
            [FromBody] SubSystemService.ProductionProgress_QueryRequest request)
        {
            if (request == null)
                return BadRequest(new { message = "Request body khong hop le." });

            if (request.FromDate > request.ToDate)
                return BadRequest(new { message = "FromDate khong duoc lon hon ToDate." });

            var result = await _service.GetProductionProgressList(request);
            return Ok(result);
        }

        // ---------------------------------------------------------------
        // GET: Lấy chi tiết một ProductionInfo
        // GET /ProductionProgress/GetById?id={guid}
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetById(Guid id)
        {
            var info = await _service.GetProductionInfoById(id);
            if (info == null)
                return NotFound(new { success = false, message = "Không tìm thấy mã hàng." });
            return Ok(info);
        }

        // ---------------------------------------------------------------
        // GET: Lịch sử mã hàng (cho trang _ProductionInfoHistory)
        // GET /ProductionProgress/GetProductionInfoHistory?from=yyyy-MM-dd&to=yyyy-MM-dd&deptId=&status=
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetProductionInfoHistory(
            string from = null, string to = null, Guid? deptId = null, int? status = null)
        {
            DateOnly? fromDate = null, toDate = null;
            if (!string.IsNullOrEmpty(from) && DateOnly.TryParse(from, out var f)) fromDate = f;
            if (!string.IsNullOrEmpty(to)   && DateOnly.TryParse(to,   out var t)) toDate   = t;
            var list = await _service.GetProductionInfoHistory(fromDate, toDate, deptId, status);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // DELETE (Soft): Đổi Status = -2
        // POST /ProductionProgress/SoftDelete
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> SoftDelete([FromBody] IdRequest req)
        {
            if (req?.Id == null || req.Id == Guid.Empty)
                return BadRequest(new { success = false, message = "Id không hợp lệ." });

            var (ok, msg) = await _service.SoftDeleteProductionInfo(req.Id);
            return ok ? Ok(new { success = true, message = msg })
                      : BadRequest(new { success = false, message = msg });
        }

        // ---------------------------------------------------------------
        // POST: BulkUpdateStatus
        // POST /ProductionProgress/BulkUpdateStatus
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> BulkUpdateStatus([FromBody] BulkStatusRequest req)
        {
            if (req == null || req.Ids == null || req.Ids.Count == 0)
                return BadRequest(new { success = false, message = "Danh sách ID trống." });

            var (ok, msg) = await _service.BulkUpdateStatus(req.Ids, req.TargetStatus);
            return ok ? Ok(new { success = true, message = msg })
                      : BadRequest(new { success = false, message = msg });
        }

        // ---------------------------------------------------------------
        // POST: Tạo mới hoặc cập nhật ProductionInfo
        // POST /ProductionProgress/Upsert
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> Upsert(
            [FromBody] SubSystemService.UpsertProductionInfo_Request req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Request không hợp lệ." });

            var (ok, msg, newId) = await _service.UpsertProductionInfo(req);
            return ok ? Ok(new { success = true, message = msg, id = newId })
                      : BadRequest(new { success = false, message = msg });
        }

        // ── Helper DTO ───────────────────────────────────────────────────────
        public class IdRequest { public Guid Id { get; set; } }
        public class BulkStatusRequest { 
            public System.Collections.Generic.List<Guid> Ids { get; set; } 
            public int TargetStatus { get; set; } 
        }
    }
}
