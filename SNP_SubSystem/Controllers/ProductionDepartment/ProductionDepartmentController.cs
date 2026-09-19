using Microsoft.AspNetCore.Mvc;
using Sub_Services.Execute;
using System;
using System.Linq;
using System.Threading.Tasks;


namespace SNP_SubSystem.Controllers.ProductionDepartment
{
    public class ProductionDepartmentController : Controller
    {
        private readonly SubSystemService _service;

        public ProductionDepartmentController(SubSystemService service)
        {
            _service = service;
        }

        public IActionResult ATDepartment()
        {
            return View("~/Views/ProductionDepartment/Index.cshtml");
        }

        // ---------------------------------------------------------------
        // GET: Tất cả layouts (optionally filter by dept)
        // GET /ProductionDepartment/GetLayouts
        // GET /ProductionDepartment/GetLayouts?departmentId={guid}
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetLayouts(Guid? departmentId = null)
        {
            var list = await _service.GetAllLayouts(departmentId);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Danh sách bộ phận cho dropdown
        // GET /ProductionDepartment/GetDepartments
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetDepartments()
        {
            var list = await _service.GetDepartmentsForLayout();
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Danh sách mã sản xuất theo máy
        // GET /ProductionDepartment/GetProductionInfos?machineId={guid}&keyword=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetProductionInfos(Guid machineId, string keyword = null)
        {
            var list = await _service.GetProductionInfosByMachine(machineId, keyword);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: DailyOutput + Details của một mã trên một máy
        // GET /ProductionDepartment/GetDailyOutputs?productionInfoId=&machineId=&from=&to=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetDailyOutputs(
            Guid productionInfoId, Guid machineId,
            DateTime? from = null, DateTime? to = null)
        {
            var list = await _service.GetDailyOutputs(productionInfoId, machineId, from, to);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Danh sách Style để pick
        // GET /ProductionDepartment/GetStylesForPicker?deptId=&keyword=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetStylesForPicker(Guid deptId, string keyword = null)
        {
            var list = await _service.GetStylesForPicker(deptId, keyword);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Danh sách mã sản xuất theo bộ phận (để chọn có sẵn)
        // GET /ProductionDepartment/GetProductionInfosByDept?deptId=&keyword=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetProductionInfosByDept(Guid deptId, string keyword = null)
        {
            var list = await _service.GetProductionInfosByDept(deptId, keyword);
            return Ok(list);
        }

        /// <summary>
        /// Trang chi tiết sản xuất của một máy.
        /// </summary>
        public IActionResult MachineDetail(
            string machineId   = "",
            string machineName = "",
            string machineCode = "",
            string dept        = "",
            string deptId      = "")
        {
            ViewData["MachineId"]   = machineId;
            ViewData["MachineName"] = machineName;
            ViewData["MachineCode"] = machineCode;
            ViewData["Dept"]        = dept;
            ViewData["DeptId"]      = deptId;
            return View("~/Views/ProductionDepartment/PartialView/_MachineDetail.cshtml");
        }

        /// <summary>
        /// Trang chi tiết sản lượng của một mã sản xuất trên một máy (standalone).
        /// </summary>
        public async Task<IActionResult> ProductionInfoDetail(
            string productionInfoId = "",
            string machineId        = "",
            string machineName      = "",
            string dept             = "")
        {
            if (!Guid.TryParse(productionInfoId, out var piGuid) ||
                !Guid.TryParse(machineId, out var mGuid))
                return RedirectToAction("ATDepartment");

            // Lấy thông tin mã sản xuất + sản lượng ngày hôm nay mặc định
            var infoList = await _service.GetProductionInfosByMachine(mGuid, null);
            var info = infoList.FirstOrDefault(i => i.Id == piGuid);
            if (info == null) return RedirectToAction("ATDepartment");

            ViewData["ProductionInfoId"] = productionInfoId;
            ViewData["MachineId"]        = machineId;
            ViewData["MachineName"]      = machineName;
            ViewData["Dept"]             = dept;
            ViewData["Spmain"]           = info.Spmain;
            ViewData["Style"]            = info.Style;
            ViewData["Line"]             = info.Line;
            ViewData["Color"]            = info.Color;
            ViewData["Status"]           = info.Status;
            ViewData["TotalQty"]         = info.TotalQty;
            ViewData["Target"]           = info.Target;
            ViewData["TotalOutput"]      = info.TotalOutputNumber;
            ViewData["TodayOutput"]      = info.TodayOutput;
            ViewData["Remark"]           = info.Remark;
            return View("~/Views/ProductionDepartment/PartialView/_ProductionInfoDetail.cshtml");
        }

        // ---------------------------------------------------------------
        // POST: Gán mã có sẵn vào máy
        // POST /ProductionDepartment/AssignProductionInfo
        // Body: { productionInfoId, machineId }
        // ---------------------------------------------------------------

        [HttpPost]
        public async Task<IActionResult> AssignProductionInfo([FromBody] AssignRequest req)
        {
            if (req == null || req.ProductionInfoId == Guid.Empty || req.MachineId == Guid.Empty)
                return BadRequest(new { success = false, message = "Thiếu dữ liệu." });

            var (ok, msg) = await _service.AssignProductionInfoToMachine(req.ProductionInfoId, req.MachineId);
            if (!ok) return Conflict(new { success = false, message = msg });
            return Ok(new { success = true, message = msg });
        }

        // ---------------------------------------------------------------
        // POST: Tạo mới mã sản xuất rồi gán vào máy
        // POST /ProductionDepartment/CreateProductionInfo
        // ---------------------------------------------------------------

        [HttpPost]
        public async Task<IActionResult> CreateProductionInfo(
            [FromBody] SubSystemService.CreateProductionInfoRequest req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Thiếu dữ liệu." });

            var (ok, msg, id) = await _service.CreateAndAssignProductionInfo(req);
            if (!ok) return BadRequest(new { success = false, message = msg });
            return Ok(new { success = true, message = msg, id = id });
        }

        public class AssignRequest
        {
            public Guid ProductionInfoId { get; set; }
            public Guid MachineId        { get; set; }
        }

        // ---------------------------------------------------------------
        // GET: Trang ghi sản lượng
        // GET /ProductionDepartment/RecordOutput
        // ---------------------------------------------------------------

        public IActionResult RecordOutput()
        {
            return View("~/Views/ProductionDepartment/PartialView/_RecordOutput.cshtml");
        }

        // ---------------------------------------------------------------
        // GET: Trang lịch sử ghi sản lượng
        // GET /ProductionDepartment/OutputHistory
        // ---------------------------------------------------------------

        public IActionResult OutputHistory()
        {
            return View("~/Views/ProductionDepartment/PartialView/_OutputHistory.cshtml");
        }

        // ---------------------------------------------------------------
        // GET: Lịch sử ghi sản lượng
        // GET /ProductionDepartment/GetOutputHistory?from=yyyy-MM-dd&to=yyyy-MM-dd&deptId=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetOutputHistory(
            string from = null, string to = null, Guid? deptId = null)
        {
            DateOnly? fromDate = null, toDate = null;
            if (!string.IsNullOrEmpty(from) && DateOnly.TryParse(from, out var f)) fromDate = f;
            if (!string.IsNullOrEmpty(to)   && DateOnly.TryParse(to,   out var t)) toDate   = t;
            var list = await _service.GetOutputHistory(fromDate, toDate, deptId);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // PUT: Cập nhật một lần ghi sản lượng
        // PUT /ProductionDepartment/UpdateOutputRecord
        // Body: { id, outputNumber, date, time }
        // ---------------------------------------------------------------

        [HttpPost]
        public async Task<IActionResult> UpdateOutputRecord(
            [FromBody] SubSystemService.UpdateOutputRecord_Request req)
        {
            if (req == null || req.Id == Guid.Empty)
                return BadRequest(new { success = false, message = "Thiếu dữ liệu." });
            var (ok, msg) = await _service.UpdateOutputRecord(req);
            if (!ok) return BadRequest(new { success = false, message = msg });
            return Ok(new { success = true, message = msg });
        }

        // ---------------------------------------------------------------
        // DELETE: Xóa mềm một lần ghi sản lượng
        // DELETE /ProductionDepartment/DeleteOutputRecord?id={guid}
        // ---------------------------------------------------------------

        [HttpPost]
        public async Task<IActionResult> DeleteOutputRecord([FromBody] System.Text.Json.JsonElement body)
        {
            if (!body.TryGetProperty("id", out var idEl) || !Guid.TryParse(idEl.GetString(), out var id) || id == Guid.Empty)
                return BadRequest(new { success = false, message = "Id không hợp lệ." });
            var (ok, msg) = await _service.DeleteOutputRecord(id);
            if (!ok) return NotFound(new { success = false, message = msg });
            return Ok(new { success = true, message = msg });
        }


        // ---------------------------------------------------------------
        // GET: Danh sách máy (cho RecordOutput)
        // GET /ProductionDepartment/GetMachinesForRecording?deptId=&keyword=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetMachinesForRecording(Guid? deptId = null, string keyword = null, int? machineStatus = null)
        {
            var list = await _service.GetMachinesForRecording(deptId, keyword, machineStatus);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Mã sản xuất theo máy (cho RecordOutput)
        // GET /ProductionDepartment/GetProductionCodesForMachine?machineId=&keyword=&status=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetProductionCodesForMachine(
            Guid machineId, string keyword = null, int? status = null)
        {
            var list = await _service.GetProductionCodesForMachine(machineId, keyword, status);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // GET: Chi tiết công đoạn (StyleDetail) của mã + tổng đã nhập
        // GET /ProductionDepartment/GetStyleDetailsForRecording?productionInfoId=&machineId=&date=
        // ---------------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> GetStyleDetailsForRecording(
            Guid productionInfoId, Guid machineId, string date = null, Guid? departmentId = null)
        {
            DateOnly? d = null;
            if (!string.IsNullOrEmpty(date) && DateOnly.TryParse(date, out var parsed))
                d = parsed;
            var list = await _service.GetStyleDetailsForRecording(productionInfoId, machineId, d, departmentId);
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // POST: Ghi sản lượng (cộng dồn)
        // POST /ProductionDepartment/SaveOutputRecord
        // ---------------------------------------------------------------

        [HttpPost]
        public async Task<IActionResult> SaveOutputRecord(
            [FromBody] SubSystemService.RecordOutput_SaveRequest req)
        {
            if (req == null)
                return BadRequest(new { success = false, message = "Thiếu dữ liệu." });
            var (ok, msg) = await _service.SaveOutputRecord(req);
            if (!ok) return BadRequest(new { success = false, message = msg });
            return Ok(new { success = true, message = msg });
        }

        // ---------------------------------------------------------------
        // GET: Trang danh sách bộ phận
        // ---------------------------------------------------------------
        public IActionResult DepartmentList()
        {
            return View("~/Views/ProductionDepartment/DepartmentList.cshtml");
        }

        // ---------------------------------------------------------------
        // GET: Danh sách bộ phận (JSON) kèm số mã hàng
        // GET /ProductionDepartment/GetDepartmentList
        // ---------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetDepartmentList()
        {
            var list = await _service.GetDepartmentListWithCount();
            return Ok(list);
        }

        // ---------------------------------------------------------------
        // POST: Tạo mới bộ phận
        // POST /ProductionDepartment/CreateDepartment
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> CreateDepartment(
            [FromBody] SubSystemService.DepartmentUpsert_Request req)
        {
            if (req == null) return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ." });
            var (ok, msg, id) = await _service.CreateDepartment(req);
            return ok ? Ok(new { success = true, message = msg, id })
                      : BadRequest(new { success = false, message = msg });
        }

        // ---------------------------------------------------------------
        // POST: Cập nhật thông tin bộ phận
        // POST /ProductionDepartment/UpdateDepartment
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> UpdateDepartment(
            [FromBody] SubSystemService.DepartmentUpsert_Request req)
        {
            if (req == null) return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ." });
            var (ok, msg) = await _service.UpdateDepartment(req);
            return ok ? Ok(new { success = true, message = msg })
                      : BadRequest(new { success = false, message = msg });
        }

        // ---------------------------------------------------------------
        // POST: Tạm khoá bộ phận (Status = -1)
        // POST /ProductionDepartment/SuspendDepartment
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> SuspendDepartment([FromBody] DeptIdRequest req)
        {
            if (req?.Id == Guid.Empty) return BadRequest(new { success = false, message = "Id không hợp lệ." });
            var (ok, msg) = await _service.SuspendDepartment(req.Id);
            return ok ? Ok(new { success = true, message = msg })
                      : BadRequest(new { success = false, message = msg });
        }

        // ---------------------------------------------------------------
        // POST: Mở khoá bộ phận (Status = 1)
        // POST /ProductionDepartment/UnlockDepartment
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> UnlockDepartment([FromBody] DeptIdRequest req)
        {
            if (req?.Id == Guid.Empty) return BadRequest(new { success = false, message = "Id không hợp lệ." });
            var (ok, msg) = await _service.UnlockDepartment(req.Id);
            return ok ? Ok(new { success = true, message = msg })
                      : BadRequest(new { success = false, message = msg });
        }

        // ---------------------------------------------------------------
        // POST: Xoá mềm bộ phận (Status = -2)
        // POST /ProductionDepartment/SoftDeleteDepartment
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> SoftDeleteDepartment([FromBody] DeptIdRequest req)
        {
            if (req?.Id == Guid.Empty) return BadRequest(new { success = false, message = "Id không hợp lệ." });
            var (ok, msg) = await _service.SoftDeleteDepartment(req.Id);
            return ok ? Ok(new { success = true, message = msg })
                      : BadRequest(new { success = false, message = msg });
        }


        // ---------------------------------------------------------------
        // POST: Tách mã sản xuất khỏi máy (bulk)
        // POST /ProductionDepartment/RemoveFromMachine
        // Body: { productionInfoIds: [guid,...], machineId: guid }
        // ---------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> RemoveFromMachine([FromBody] RemoveFromMachineRequest req)
        {
            if (req == null || req.ProductionInfoIds == null || req.ProductionInfoIds.Count == 0 || req.MachineId == Guid.Empty)
                return BadRequest(new { success = false, message = "D\u1eef li\u1ec7u kh\u00f4ng h\u1ee3p l\u1ec7." });

            var (ok, msg) = await _service.RemoveInfosFromMachine(req.ProductionInfoIds, req.MachineId);
            return ok ? Ok(new { success = true, message = msg })
                      : BadRequest(new { success = false, message = msg });
        }

        public class RemoveFromMachineRequest
        {
            public System.Collections.Generic.List<Guid> ProductionInfoIds { get; set; }
            public Guid MachineId { get; set; }
        }

        public class DeptIdRequest { public Guid Id { get; set; } }
    }
}
