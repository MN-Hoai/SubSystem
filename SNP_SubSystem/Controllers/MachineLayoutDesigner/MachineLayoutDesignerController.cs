using Microsoft.AspNetCore.Mvc;
using Sub_Services.Execute;
using System;
using System.Threading.Tasks;

namespace SNP_SubSystem.Controllers.MachineLayoutDesigner
{
    public class MachineLayoutDesignerController : Controller
    {
        private readonly SubSystemService _service;

        public MachineLayoutDesignerController(SubSystemService service)
        {
            _service = service;
        }

        // ---------------------------------------------------------------
        // VIEW
        // ---------------------------------------------------------------

        public IActionResult Index()
        {
            return View("~/Views/MachineLayoutDesigner/Index.cshtml");
        }

        // ---------------------------------------------------------------
        // GET: Danh sách bộ phận (cho dropdown)
        // ---------------------------------------------------------------

        /// <summary>GET /MachineLayoutDesigner/GetDepartments</summary>
        [HttpGet]
        public async Task<IActionResult> GetDepartments()
        {
            var list = await _service.GetDepartmentsForLayout();
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Layout theo bộ phận
        // ---------------------------------------------------------------

        /// <summary>
        /// GET /MachineLayoutDesigner/GetLayoutByDepartment?departmentId={guid}
        /// Trả 200 + layout nếu có, 204 NoContent nếu chưa có layout.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetLayoutByDepartment(Guid departmentId)
        {
            if (departmentId == Guid.Empty)
                return BadRequest(new { message = "departmentId không hợp lệ." });

            var layout = await _service.GetLayoutByDepartment(departmentId);

            if (layout == null)
                return NoContent(); // 204 — chưa có layout, cho phép tạo mới

            return Ok(layout);
        }

        // ---------------------------------------------------------------
        // GET: Layout theo Id
        // ---------------------------------------------------------------

        /// <summary>GET /MachineLayoutDesigner/GetLayoutById?layoutId={guid}</summary>
        [HttpGet]
        public async Task<IActionResult> GetLayoutById(Guid layoutId)
        {
            if (layoutId == Guid.Empty)
                return BadRequest(new { message = "layoutId không hợp lệ." });

            var layout = await _service.GetLayoutById(layoutId);
            if (layout == null)
                return NotFound(new { message = "Không tìm thấy layout." });

            return Ok(layout);
        }

        // ---------------------------------------------------------------
        // GET: Danh sách máy theo bộ phận (cho picker "máy có sẵn")
        // ---------------------------------------------------------------

        /// <summary>
        /// GET /MachineLayoutDesigner/GetMachinesByDepartment?departmentId={guid}
        /// Trả về danh sách máy có sẵn của bộ phận để chọn đặt vào ô.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetMachinesByDepartment(Guid departmentId)
        {
            if (departmentId == Guid.Empty)
                return BadRequest(new { message = "departmentId không hợp lệ." });

            var list = await _service.GetMachinesByDepartment(departmentId);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // POST: Lưu layout (upsert)
        // ---------------------------------------------------------------

        /// <summary>
        /// POST /MachineLayoutDesigner/SaveLayout
        /// Body: MachineLayout_SaveRequest (JSON)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SaveLayout(
            [FromBody] SubSystemService.MachineLayout_SaveRequest request)
        {
            if (request == null)
                return BadRequest(new { message = "Request body không hợp lệ." });

            if (request.DepartmentId == Guid.Empty)
                return BadRequest(new { message = "DepartmentId bắt buộc." });

            if (request.RowCount < 1 || request.ColumnCount < 1)
                return BadRequest(new { message = "RowCount và ColumnCount phải >= 1." });

            var result = await _service.SaveLayout(request);

            if (!result.Success)
                return StatusCode(500, new { message = result.Message });

            return Ok(result);
        }

        // ---------------------------------------------------------------
        // POST: Xóa layout
        // ---------------------------------------------------------------

        /// <summary>
        /// POST /MachineLayoutDesigner/DeleteLayout?layoutId={guid}
        /// Đặt trạng thái layout về -2.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteLayout(Guid layoutId)
        {
            if (layoutId == Guid.Empty)
                return BadRequest(new { message = "layoutId không hợp lệ." });

            var ok = await _service.DeleteLayout(layoutId);
            if (!ok)
                return NotFound(new { message = "Không tìm thấy layout hoặc không thể xóa." });

            return Ok(new { success = true, message = "Đã xóa layout." });
        }

        // ---------------------------------------------------------------
        // DELETE: Xóa một item khỏi layout
        // ---------------------------------------------------------------

        /// <summary>
        /// DELETE /MachineLayoutDesigner/DeleteItem?itemId={guid}
        /// </summary>
        [HttpDelete]
        public async Task<IActionResult> DeleteItem(Guid itemId)
        {
            if (itemId == Guid.Empty)
                return BadRequest(new { message = "itemId không hợp lệ." });

            var ok = await _service.DeleteLayoutItem(itemId);
            if (!ok)
                return NotFound(new { message = "Không tìm thấy item." });

            return Ok(new { message = "Đã xóa item." });
        }

        // ---------------------------------------------------------------
        // POST: Cập nhật trạng thái một máy
        // ---------------------------------------------------------------

        /// <summary>
        /// POST /MachineLayoutDesigner/UpdateMachineStatus
        /// Body: { machineId: guid, status: int }
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> UpdateMachineStatus([FromBody] UpdateMachineStatusRequest req)
        {
            if (req == null || req.MachineId == Guid.Empty)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            var result = await _service.UpdateMachineStatus(req.MachineId, req.Status);
            if (!result.Success)
                return BadRequest(new { message = result.Message });

            return Ok(new { success = true, message = result.Message });
        }

        // ---------------------------------------------------------------
        // POST: Cập nhật trạng thái hàng loạt
        // ---------------------------------------------------------------

        /// <summary>
        /// POST /MachineLayoutDesigner/BulkUpdateMachineStatus
        /// Body: { machineIds: [guid], status: int }
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> BulkUpdateMachineStatus([FromBody] BulkUpdateMachineStatusRequest req)
        {
            if (req == null || req.MachineIds == null || !req.MachineIds.Any())
                return BadRequest(new { message = "Không có máy nào được chọn." });

            var result = await _service.BulkUpdateMachineStatus(req.MachineIds, req.Status);
            if (!result.Success)
                return BadRequest(new { message = result.Message });

            return Ok(new { success = true, message = result.Message });
        }

        // ---------------------------------------------------------------
        // POST: Cập nhật thông tin máy
        // ---------------------------------------------------------------

        /// <summary>
        /// POST /MachineLayoutDesigner/UpdateMachineInfo
        /// Body: { machineId: guid, machineName: string, remark: string, status: int }
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> UpdateMachineInfo([FromBody] UpdateMachineInfoRequest req)
        {
            if (req == null || req.MachineId == Guid.Empty)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            var result = await _service.UpdateMachineInfo(req.MachineId, req.MachineName, req.Remark, req.Status);
            if (!result.Success)
                return BadRequest(new { message = result.Message });

            return Ok(new { success = true, message = result.Message });
        }

        // ---------------------------------------------------------------
        // Request DTOs
        // ---------------------------------------------------------------
        public class UpdateMachineStatusRequest
        {
            public Guid MachineId { get; set; }
            public int Status { get; set; }
        }

        public class UpdateMachineInfoRequest
        {
            public Guid MachineId { get; set; }
            public string MachineName { get; set; }
            public string Remark { get; set; }
            public int Status { get; set; }
        }

        public class BulkUpdateMachineStatusRequest
        {
            public List<Guid> MachineIds { get; set; }
            public int Status { get; set; }
        }
    }
}

